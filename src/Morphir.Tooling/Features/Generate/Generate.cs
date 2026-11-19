using Foundatio.Mediator;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Morphir.IR.Model;

namespace Morphir.Tooling.Features.Generate;

public sealed record GenerateRequest(GenerateOverrides Overrides, InvocationContext Context);
public sealed record GenerationSuccess(string Target, string Backend, string InputPath, string OutputDirectory,
    string SourceVersion, string Family, string DistributionKind, string Representation,
    GenerationPublication Publication, ImmutableArray<GenerationDiagnostic> Diagnostics)
{
    /// <summary>Actual installed-provider operation observation; absent for legacy and built-in paths.</summary>
    public System.Text.Json.JsonElement? Provider { get; init; }
    /// <summary>The transport of the external backend that ran, for example <c>mep</c>; null for a built-in.</summary>
    public string? BackendTransport { get; init; }

    /// <summary>The version of the external backend that ran, as the extension gave it; null for a built-in.</summary>
    public string? BackendVersion { get; init; }

    /// <summary>Whether the backend formatted the files: <c>applied</c>, <c>off</c> or <c>unavailable</c>; null when it does not report it.</summary>
    public string? Formatting { get; init; }

    /// <summary>The passes that ran before the backend, with their application list; null when no pass ran.</summary>
    public Transform.PassesReport? Passes { get; init; }
}

/// <summary>Coordinates generation through injectable application boundaries in the existing mediator.</summary>
public static class GenerateHandler
{
    /// <summary>
    /// Runs one generate with the MEP backend extension connector and no registered backends to protect: the
    /// signature of earlier releases, kept for compiled callers. See the overload with the connector. The mediator does not use it.
    /// </summary>
    [FoundatioIgnore]
    public static Task<Outcome<GenerationSuccess>> Handle(GenerateRequest command,
        IGenerationSettingsResolver settingsResolver, IModelLoader loader, IGenerationBackendResolver backendResolver,
        IGenerationPublisher publisher, CancellationToken ct, ILogger<GenerateRequest>? logger = null) =>
        Handle(command, settingsResolver, loader, backendResolver, publisher, new MepBackendExtensionConnector(), [], ct, logger);

    /// <summary>
    /// Runs one generate. With a backend extension in the settings (<c>--extension</c> or
    /// <c>[codegen.&lt;target&gt;].extension</c>), the extension connector connects it and the run resolves over that
    /// backend alone; the result names it with its transport and version, and the run ends its session. Without
    /// one, the registered backends of the target are resolved as before. An extension whose id is the id of a
    /// <paramref name="registered"/> backend is refused: a guest cannot report itself as a built-in. Without a connector,
    /// the MEP backend extension connector is used.
    /// </summary>
    public static async Task<Outcome<GenerationSuccess>> Handle(GenerateRequest command,
        IGenerationSettingsResolver settingsResolver, IModelLoader loader, IGenerationBackendResolver backendResolver,
        IGenerationPublisher publisher, IBackendExtensionConnector? backendExtensions, IEnumerable<IGenerationBackend> registered,
        CancellationToken ct, ILogger<GenerateRequest>? logger = null)
    {
        try
        {
            var resolution = await settingsResolver.ResolveAsync(command.Overrides, command.Context, ct);
            if (resolution is Outcome<GenerationSettings>.Failure settingsFailure) return GenerationStages.Failed(settingsFailure.Error, null);
            var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
            if (settings.InputLease is RecordGenerationSettingsResolver.RecordLease recorded
                && recorded.Output.Ir.Layout == TaskRecords.IrLayout.DocumentTree
                && recorded.Output.Ir.Format is TaskRecords.IrStorageFormat.Json or TaskRecords.IrStorageFormat.Yaml
                && settings.Extension is { } configured && settings.InstalledExtensionId is null
                && settings.Target is RequestedTarget.Valid boundedTarget && backendExtensions is MepBackendExtensionConnector or null)
            {
                using (recorded)
                {
                    var request = new BackendExtensionRequest(configured, boundedTarget.Id, command.Context.WorkingDirectory)
                    {
                        WasmModule = settings.WasmModule,
                        Environment = command.Context.Environment,
                    };
                    return await RecordedBoundedGeneration.RunAsync(recorded.Output, request, settings,
                        backendExtensions as MepBackendExtensionConnector ?? new MepBackendExtensionConnector(),
                        publisher, registered, ct);
                }
            }
            var loaded = await LoadAsync(loader, settings, ct);
            if (loaded is Outcome<LoadedModel>.Failure loadFailure) return GenerationStages.Failed(loadFailure.Error, null);
            var model = ((Outcome<LoadedModel>.Success)loaded).Value;
            if ((settings.Extension is not null || settings.InstalledExtensionId is not null) && settings.Target is RequestedTarget.Valid target)
            {
                var extension = settings.Extension ?? new Morphir.Tooling.Features.Compile.Setting<string>(settings.InstalledExtensionId!.Value, new Morphir.Tooling.Features.Compile.SettingOrigin.CommandLine("--extension-id"));
                return await RunWithExtension(extension, target.Id, command.Context, model, settings, publisher, ct, logger,
                    backendExtensions ?? new MepBackendExtensionConnector(), registered);
            }
            return await GenerationStages.RunFrom(model, settings, backendResolver, publisher, ct, logger);
        }
        catch (OperationCanceledException) { return GenerationStages.Cancelled(null); }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        {
            return GenerationStages.Unexpected(error, null, logger);
        }
    }

    /// <summary>Connects the selected backend extension, runs the stages over it alone, and ends its session.</summary>
    private static async Task<Outcome<GenerationSuccess>> RunWithExtension(Morphir.Tooling.Features.Compile.Setting<string> extension,
        Morphir.Extensions.TargetId target, InvocationContext context, LoadedModel model, GenerationSettings settings,
        IGenerationPublisher publisher, CancellationToken ct, ILogger? logger, IBackendExtensionConnector connector,
        IEnumerable<IGenerationBackend> registered)
    {
        var connected = await connector.ConnectAsync(new BackendExtensionRequest(extension, target, context.WorkingDirectory)
        { InstalledId = settings.InstalledExtensionId, Environment = context.Environment, WasmModule = settings.WasmModule }, ct);
        if (connected is Outcome<IExternalGenerationBackend>.Failure connectFailure) return GenerationStages.Failed(connectFailure.Error, null);
        await using var backend = ((Outcome<IExternalGenerationBackend>.Success)connected).Value;
        var id = backend.Descriptor.Id;
        if (registered.Any(builtIn => builtIn.Descriptor.Id.Equals(id)))
            return GenerationStages.Failed(GenerationFailure.One(GenerationFailureKind.Operational, "extension.unavailable",
                $"The backend extension {extension.Value} gave the extension id {id.Value}, which is the id of a built-in backend. An extension must have its own id."), null);
        // A backend extension reads the bytes of the model, so the built-in passes do not apply to it.
        var outcome = await GenerationStages.RunFrom(model, settings, new RegisteredBackendResolver([backend]), publisher, ct, logger, Transform.ModelPasses.None);
        return outcome switch
        {
            Outcome<GenerationSuccess>.Success success => new Outcome<GenerationSuccess>.Success(success.Value with
            {
                BackendTransport = backend.Transport,
                BackendVersion = backend.ExtensionVersion,
                Provider = (backend as Morphir.Tooling.Infrastructure.IInstalledProviderObservation)?.Provider,
            }),
            Outcome<GenerationSuccess>.Failure failure => new Outcome<GenerationSuccess>.Failure(failure.Error with
            {
                BackendTransport = backend.Transport,
                BackendVersion = backend.ExtensionVersion,
            }),
            _ => outcome
        };
    }

    /// <summary>
    /// Loads the model while the settings' input lease is held, admits it through the lease, then releases the
    /// lease. A compile record's shared lock is therefore held until loading ends, and no longer (REC-1).
    /// </summary>
    private static async ValueTask<Outcome<LoadedModel>> LoadAsync(IModelLoader loader, GenerationSettings settings, CancellationToken ct)
    {
        using var lease = settings.InputLease;
        var loaded = await loader.LoadAsync(settings.Input, settings.Limits, ct);
        return lease is not null && loaded is Outcome<LoadedModel>.Success success ? lease.Admit(success.Value) : loaded;
    }
}

/// <summary>
/// The generation stages that follow model loading: backend selection, option validation, generation and
/// publication. <c>generate</c> runs them after it loads its input, and <c>compile --target</c> runs them after it
/// loads the bytes of the IR that it wrote, so the backend sees the same <see cref="LoadedModel"/> in both cases.
/// </summary>
public static class GenerationStages
{
    /// <summary>Runs the stages from a loaded model. A failure names the backend when one was selected.</summary>
    public static async Task<Outcome<GenerationSuccess>> RunFrom(LoadedModel model, GenerationSettings settings,
        IGenerationBackendResolver backendResolver, IGenerationPublisher publisher, CancellationToken ct, ILogger? logger = null,
        Transform.ModelPasses? passes = null)
    {
        string? backendIdentity = null;
        try
        {
            var selected = backendResolver.Resolve(settings.Target, model.Descriptor);

            // A backend that does not read the classic model directly, but reads the current IR, gets the model upgraded.
            // The report keeps the model as it was loaded.
            var backendModel = model;
            if (selected is Outcome<IGenerationBackend>.Failure firstFailure
                && model.Model.Family == ModelFamily.Classic
                && firstFailure.Error.Diagnostics.Any(diagnostic => diagnostic.Code == "incompatible-model")
                && backendResolver.Resolve(settings.Target, ModelUpgrade.Reachable(model.Descriptor)) is Outcome<IGenerationBackend>.Success)
            {
                var upgraded = ModelUpgrade.Upgrade(model);
                if (upgraded is Outcome<LoadedModel>.Failure upgradeFailure) return Failed(upgradeFailure.Error, backendIdentity);
                backendModel = ((Outcome<LoadedModel>.Success)upgraded).Value;
                selected = backendResolver.Resolve(settings.Target, backendModel.Descriptor);
            }

            if (selected is Outcome<IGenerationBackend>.Failure selectionFailure) return Failed(selectionFailure.Error, backendIdentity);
            var backend = ((Outcome<IGenerationBackend>.Success)selected).Value;
            backendIdentity = backend.Descriptor.Id.Value;
            var validation = backend.ValidateOptions(settings.Options);
            if (validation is Outcome<ValidatedBackendOptions>.Failure optionFailure) return Failed(optionFailure.Error, backendIdentity);
            var options = ((Outcome<ValidatedBackendOptions>.Success)validation).Value;

            // The passes of the stage before this backend run on the current IR, and the host checks that no feature remains
            // that the backend cannot take. A pass error stops the run before the backend.
            var passed = (passes ?? Transform.ModelPasses.BuiltIn).RunBeforeBackend(backendModel, backend.Descriptor.Target.Value);
            if (passed is Outcome<Transform.PassedModel>.Failure passFailure) return Failed(passFailure.Error, backendIdentity);
            var passedModel = ((Outcome<Transform.PassedModel>.Success)passed).Value;
            backendModel = passedModel.Model;

            var generation = await backend.GenerateAsync(new(backendModel, backend.Descriptor.Target, options), ct);
            if (generation is Outcome<GenerationArtifacts>.Failure generationFailure) return Failed(generationFailure.Error, backendIdentity);
            var artifacts = ((Outcome<GenerationArtifacts>.Success)generation).Value;
            // The diagnostics of the passes go with the diagnostics of the backend, so that a publisher that reports them at
            // publication, as compile --target does, reports both.
            artifacts = artifacts with { Diagnostics = artifacts.Diagnostics.AddRange(passedModel.Diagnostics) };
            var family = ModelLabels.Family(model.Model.Family);
            var kind = ModelLabels.Kind(model.Model.Kind);
            ct.ThrowIfCancellationRequested();
            var publication = await publisher.PublishAsync(artifacts, settings.Input, settings.Publication, ct);
            if (publication is Outcome<GenerationPublication>.Failure publicationFailure) return Failed(publicationFailure.Error, backendIdentity);
            var receipt = ((Outcome<GenerationPublication>.Success)publication).Value;
            return new Outcome<GenerationSuccess>.Success(new(backend.Descriptor.Target.Value, backend.Descriptor.Id.Value, model.Original.SourcePath,
                settings.Publication.OutputDirectory, model.Model.SourceVersion.Text, family, kind,
                model.Representation.ToString(), receipt, artifacts.Diagnostics)
            { Formatting = artifacts.Formatting, Passes = passedModel.Report });
        }
        catch (OperationCanceledException) { return Cancelled(backendIdentity); }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        {
            return Unexpected(error, backendIdentity, logger);
        }
    }

    internal static Outcome<GenerationSuccess> Failed(GenerationFailure failure, string? backendIdentity) =>
        new Outcome<GenerationSuccess>.Failure(failure with { Backend = backendIdentity });

    internal static Outcome<GenerationSuccess> Cancelled(string? backendIdentity) =>
        Failed(GenerationFailure.One(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled before publication."), backendIdentity);

    internal static Outcome<GenerationSuccess> Unexpected(Exception error, string? backendIdentity, ILogger? logger)
    {
        logger?.LogError(error, "Generation failed unexpectedly.");
        return Failed(GenerationFailure.One(GenerationFailureKind.Operational, "generation-failed", "Generation failed unexpectedly."), backendIdentity);
    }
}
