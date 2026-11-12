// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.TaskRecords;
using ConfigSeverity = Morphir.Configuration.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Generate;

/// <summary>
/// The generation settings resolver in front of the CLI resolver (REC-1). An explicit <c>--input</c> goes to
/// the CLI resolver and never reads a record. Without <c>--input</c>, it finds the project and the out root in
/// the same way as <c>morphir compile</c>, opens the most recent successful compile record with the shared lock,
/// and gives the record's IR as the input. The lock is held until model loading ends, so a compile cannot clear
/// <c>.dest</c> while the IR is read. A missing record or a tombstone fails with exit code 2 and tells the user
/// to run <c>morphir compile</c> first.
/// <para>
/// In both modes, when <c>--extension</c> is absent, it loads the project configuration as compile does and
/// takes the backend extension from <c>[codegen.&lt;target&gt;].extension</c>. A configuration that fails to
/// load fails with exit code 2, except with <c>--input</c> and neither <c>--project</c> nor <c>--config</c>: then
/// nothing asked for the configuration, and the failure is a warning on <paramref name="warnings"/> (stderr by
/// default) while the built-in backend runs, as before configuration was read.
/// </para>
/// </summary>
public sealed class RecordGenerationSettingsResolver(
    IGenerationSettingsResolver inner, ICompileConfigurationLoader loader, TaskStoreOptions? options = null, TextWriter? warnings = null)
    : IGenerationSettingsResolver
{
    private readonly TaskStoreOptions _options = options ?? new TaskStoreOptions();
    private readonly TextWriter _warnings = warnings ?? Console.Error;

    /// <inheritdoc />
    public async ValueTask<Outcome<GenerationSettings>> ResolveAsync(GenerateOverrides overrides, InvocationContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(context);

        // The project and the out root, as compile finds them.
        var compileContext = context.Environment is { } environment
            ? new CompileContext(context.WorkingDirectory, environment)
            : CompileContext.Current() with { WorkingDirectory = context.WorkingDirectory };

        if (!string.IsNullOrWhiteSpace(overrides.Input))
        {
            var explicitSettings = await inner.ResolveAsync(overrides, context, ct);
            // Installed identity is exclusive. A legacy --extension may name a configured WASM alias.
            if (explicitSettings is not Outcome<GenerationSettings>.Success { Value: var settings } || settings.InstalledExtensionId is not null)
                return explicitSettings;
            var configuration = Load(overrides, compileContext);
            if (configuration is Outcome<LoadedCompileConfiguration>.Failure loadFailure)
            {
                // An explicit process extension previously needed no project configuration.
                if (settings.Extension is not null) return explicitSettings;
                // Only --input, --target and --output were given: nothing asked for the configuration, so a broken or
                // unrelated discovered one does not fail the command. It is a warning on stderr, never on stdout.
                if (overrides.Project is null && overrides.Config is null)
                {
                    var key = settings.Target is RequestedTarget.Valid warnedTarget ? ProjectCompileSettingsResolver.CodegenExtensionKey(warnedTarget.Id.Value) : "[codegen.<target>].extension";
                    _warnings.WriteLine($"warning: The project configuration was not loaded, so {key} was not read and the built-in backend runs: "
                        + string.Join(" ", loadFailure.Error.Diagnostics.Select(diagnostic => diagnostic.Message)));
                    return explicitSettings;
                }
                return Fail(loadFailure.Error);
            }
            return WithConfiguredExtension(settings, ((Outcome<LoadedCompileConfiguration>.Success)configuration).Value, compileContext.WorkingDirectory);
        }

        var loadedOutcome = Load(overrides, compileContext);
        if (loadedOutcome is Outcome<LoadedCompileConfiguration>.Failure configurationFailure) return Fail(configurationFailure.Error);
        var loaded = ((Outcome<LoadedCompileConfiguration>.Success)loadedOutcome).Value;
        var located = CompileTaskLocator.Locate(loaded, compileContext, overrides.OutDir);
        if (located is Outcome<CompileTaskPlace>.Failure notLocated) return Fail(notLocated.Error);
        var store = ((Outcome<CompileTaskPlace>.Success)located).Value.Store(_options);

        // Check the other values before the record is read. The input path is a placeholder until then.
        var validated = await inner.ResolveAsync(overrides with { Input = store.Location.Record }, context, ct);
        if (validated is not Outcome<GenerationSettings>.Success valid) return validated;

        var opened = await store.OpenCompletedAsync(ct);
        if (opened is Outcome<CompileTaskOutput>.Failure missing) return Fail(missing.Error);
        var output = ((Outcome<CompileTaskOutput>.Success)opened).Value;
        var format = output.Ir.Format switch
        {
            IrStorageFormat.Json => InputFormat.Json,
            IrStorageFormat.Ion => InputFormat.Ion,
            _ => (InputFormat?)null,
        };
        var requested = valid.Value.Input.Format;
        string? refusal = format is null
            ? $"The compile record at {store.Location.Record} stores its IR as {IrStorageNames.Name(output.Ir.Format)}, which generate cannot read."
            : requested != InputFormat.Auto && requested != format
                ? $"--input-format {requested.ToString().ToLowerInvariant()} differs from the format {IrStorageNames.Name(output.Ir.Format)} of the compiled IR in {store.Location.Record}. Remove --input-format, or give --input."
                : null;
        if (refusal is not null)
        {
            output.Dispose();
            return Fail(GenerationFailure.One(GenerationFailureKind.Invocation,
                format is null ? "compile.record_unsupported_format" : "generate.input_format_conflict", refusal, store.Location.Record));
        }

        var fromRecord = valid.Value with
        {
            Input = new ModelInput(output.IrPath, format!.Value),
            InputLease = new RecordLease(output),
        };
        var withExtension = WithConfiguredExtension(fromRecord, loaded, compileContext.WorkingDirectory);
        if (withExtension is Outcome<GenerationSettings>.Failure) output.Dispose();
        return withExtension;
    }

    /// <summary>Loads the configuration of the project that <c>--project</c> and <c>--config</c> select, as compile does.</summary>
    private Outcome<LoadedCompileConfiguration> Load(GenerateOverrides overrides, CompileContext compileContext)
    {
        var loaded = loader.Load(new CompileOverrides { Project = overrides.Project, Config = overrides.Config }, compileContext);
        var errors = loaded.Diagnostics.Where(diagnostic => diagnostic.Severity == ConfigSeverity.Error).ToArray();
        return errors.Length > 0
            ? new Outcome<LoadedCompileConfiguration>.Failure(new GenerationFailure(GenerationFailureKind.Invocation,
                [.. errors.Select(error => new GenerationDiagnostic(CompileCodes.Configuration.Value,
                    $"{error.Source.Path}: {string.Join('.', error.KeyPath)}: {error.Message} ({error.Code})".Replace(": : ", ": ")))]))
            : new Outcome<LoadedCompileConfiguration>.Success(loaded);
    }

    /// <summary>
    /// The settings with the backend extension of <c>[codegen.&lt;target&gt;].extension</c>, when the configuration
    /// sets it for the requested target, as compile reads <c>[frontend.&lt;language&gt;].extension</c>. An empty
    /// value is an invocation failure that names the file.
    /// </summary>
    private static Outcome<GenerationSettings> WithConfiguredExtension(GenerationSettings settings, LoadedCompileConfiguration loaded, string workingDirectory)
    {
        if (settings.InstalledExtensionId is not null) return new Outcome<GenerationSettings>.Success(settings);
        if (settings.Target is not RequestedTarget.Valid target) return new Outcome<GenerationSettings>.Success(settings);
        var config = new ProjectCompileSettingsResolver.ConfigurationView(loaded);
        var configured = settings.Extension ?? config.String("codegen", target.Id.Value, "extension");
        if (configured is null && config.String("extensions", target.Id.Value, "path") is { } path)
            configured = new(target.Id.Value, path.Origin);
        if (configured is null) return new Outcome<GenerationSettings>.Success(settings);
        if (string.IsNullOrWhiteSpace(configured.Value))
            return Fail(GenerationFailure.One(GenerationFailureKind.Invocation, "invalid-extension",
                $"The backend extension from {ProjectCompileSettingsResolver.Describe(configured.Origin)} is empty."));
        if (!Morphir.Tooling.Features.Extensions.ConfiguredWasmModule.TryRead(config, configured.Value,
                loaded.ProjectRoot ?? workingDirectory, out var module, out var error))
            return Fail(GenerationFailure.One(GenerationFailureKind.Invocation, "invalid-extension", error!));
        return new Outcome<GenerationSettings>.Success(settings with { Extension = configured, WasmModule = module });
    }

    private static Outcome<GenerationSettings> Fail(GenerationFailure failure) => new Outcome<GenerationSettings>.Failure(failure);

    /// <summary>Holds the shared lock of the record, and keeps the format version that the record names.</summary>
    private sealed class RecordLease(CompileTaskOutput output) : ModelInputLease
    {
        public override Outcome<LoadedModel> Admit(LoadedModel model)
        {
            var recorded = output.Ir.Version;
            if (!FormatVersion.SameVersion(recorded.Version, model.Model.SourceVersion.Version))
                return new Outcome<LoadedModel>.Failure(GenerationFailure.One(GenerationFailureKind.Operational, "compile.record_version_mismatch",
                    $"The compile record at {output.Location.Record} names IR version {output.Ir.Spelling}, but {output.IrPath} declares formatVersion {model.Model.SourceVersion.Text}. Run `morphir compile` again.",
                    output.IrPath));
            var versioned = new VersionedModel(recorded, model.Model.Distribution);
            return new Outcome<LoadedModel>.Success(model with { Model = versioned, Descriptor = model.Descriptor with { Version = recorded } });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) output.Dispose();
        }
    }
}
