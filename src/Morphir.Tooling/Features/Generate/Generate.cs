using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Morphir.Tooling.Features.Generate;

public sealed record GenerateRequest(GenerateOverrides Overrides, InvocationContext Context);
public sealed record GenerationSuccess(string Target, string Backend, string InputPath, string OutputDirectory,
    string SourceVersion, string Family, string DistributionKind, string Representation,
    GenerationPublication Publication, ImmutableArray<GenerationDiagnostic> Diagnostics);

/// <summary>Coordinates generation through injectable application boundaries in the existing mediator.</summary>
public static class GenerateHandler
{
    public static async Task<Outcome<GenerationSuccess>> Handle(GenerateRequest command,
        IGenerationSettingsResolver settingsResolver, IModelLoader loader, IGenerationBackendResolver backendResolver,
        IGenerationPublisher publisher, CancellationToken ct, ILogger<GenerateRequest>? logger = null)
    {
        try
        {
            var resolution = await settingsResolver.ResolveAsync(command.Overrides, command.Context, ct);
            if (resolution is Outcome<GenerationSettings>.Failure settingsFailure) return GenerationStages.Failed(settingsFailure.Error, null);
            var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
            var loaded = await LoadAsync(loader, settings, ct);
            if (loaded is Outcome<LoadedModel>.Failure loadFailure) return GenerationStages.Failed(loadFailure.Error, null);
            var model = ((Outcome<LoadedModel>.Success)loaded).Value;
            return await GenerationStages.RunFrom(model, settings, backendResolver, publisher, ct, logger);
        }
        catch (OperationCanceledException) { return GenerationStages.Cancelled(null); }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        {
            return GenerationStages.Unexpected(error, null, logger);
        }
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
        IGenerationBackendResolver backendResolver, IGenerationPublisher publisher, CancellationToken ct, ILogger? logger = null)
    {
        string? backendIdentity = null;
        try
        {
            var selected = backendResolver.Resolve(settings.Target, model.Descriptor);
            if (selected is Outcome<IGenerationBackend>.Failure selectionFailure) return Failed(selectionFailure.Error, backendIdentity);
            var backend = ((Outcome<IGenerationBackend>.Success)selected).Value;
            backendIdentity = backend.Descriptor.Id.Value;
            var validation = backend.ValidateOptions(settings.Options);
            if (validation is Outcome<ValidatedBackendOptions>.Failure optionFailure) return Failed(optionFailure.Error, backendIdentity);
            var options = ((Outcome<ValidatedBackendOptions>.Success)validation).Value;
            var generation = await backend.GenerateAsync(new(model, backend.Descriptor.Target, options), ct);
            if (generation is Outcome<GenerationArtifacts>.Failure generationFailure) return Failed(generationFailure.Error, backendIdentity);
            var artifacts = ((Outcome<GenerationArtifacts>.Success)generation).Value;
            var family = ModelLabels.Family(model.Model.Family);
            var kind = ModelLabels.Kind(model.Model.Kind);
            ct.ThrowIfCancellationRequested();
            var publication = await publisher.PublishAsync(artifacts, settings.Input, settings.Publication, ct);
            if (publication is Outcome<GenerationPublication>.Failure publicationFailure) return Failed(publicationFailure.Error, backendIdentity);
            var receipt = ((Outcome<GenerationPublication>.Success)publication).Value;
            return new Outcome<GenerationSuccess>.Success(new(backend.Descriptor.Target.Value, backend.Descriptor.Id.Value, model.Original.SourcePath,
                settings.Publication.OutputDirectory, model.Model.SourceVersion.Text, family, kind,
                model.Representation.ToString(), receipt, artifacts.Diagnostics));
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
