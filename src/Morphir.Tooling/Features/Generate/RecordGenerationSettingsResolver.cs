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
/// </summary>
public sealed class RecordGenerationSettingsResolver(
    IGenerationSettingsResolver inner, ICompileConfigurationLoader loader, TaskStoreOptions? options = null) : IGenerationSettingsResolver
{
    private readonly TaskStoreOptions _options = options ?? new TaskStoreOptions();

    /// <inheritdoc />
    public async ValueTask<Outcome<GenerationSettings>> ResolveAsync(GenerateOverrides overrides, InvocationContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(context);
        if (!string.IsNullOrWhiteSpace(overrides.Input)) return await inner.ResolveAsync(overrides, context, ct);

        // The project and the out root, as compile finds them.
        var compileContext = context.Environment is { } environment
            ? new CompileContext(context.WorkingDirectory, environment)
            : CompileContext.Current() with { WorkingDirectory = context.WorkingDirectory };
        var loaded = loader.Load(new CompileOverrides { Project = overrides.Project, Config = overrides.Config }, compileContext);
        var errors = loaded.Diagnostics.Where(diagnostic => diagnostic.Severity == ConfigSeverity.Error).ToArray();
        if (errors.Length > 0)
            return new Outcome<GenerationSettings>.Failure(new GenerationFailure(GenerationFailureKind.Invocation,
                [.. errors.Select(error => new GenerationDiagnostic(CompileCodes.Configuration.Value,
                    $"{error.Source.Path}: {string.Join('.', error.KeyPath)}: {error.Message} ({error.Code})".Replace(": : ", ": ")))]));
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

        return new Outcome<GenerationSettings>.Success(valid.Value with
        {
            Input = new ModelInput(output.IrPath, format!.Value),
            InputLease = new RecordLease(output),
        });
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
