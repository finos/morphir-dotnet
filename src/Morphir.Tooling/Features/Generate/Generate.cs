using System.Collections.Immutable;

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
        IGenerationPublisher publisher, CancellationToken ct)
    {
        static Outcome<GenerationSuccess> Failed(GenerationFailure failure) => new Outcome<GenerationSuccess>.Failure(failure);
        try
        {
            var resolution = await settingsResolver.ResolveAsync(command.Overrides, command.Context, ct);
            if (resolution is Outcome<GenerationSettings>.Failure settingsFailure) return Failed(settingsFailure.Error);
            var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
            var loaded = await loader.LoadAsync(settings.Input, settings.Limits, ct);
            if (loaded is Outcome<LoadedModel>.Failure loadFailure) return Failed(loadFailure.Error);
            var model = ((Outcome<LoadedModel>.Success)loaded).Value;
            var selected = backendResolver.Resolve(settings.Target, model.Descriptor);
            if (selected is Outcome<IGenerationBackend>.Failure selectionFailure) return Failed(selectionFailure.Error);
            var backend = ((Outcome<IGenerationBackend>.Success)selected).Value;
            var validation = backend.ValidateOptions(settings.Options);
            if (validation is Outcome<ValidatedBackendOptions>.Failure optionFailure) return Failed(optionFailure.Error);
            var options = ((Outcome<ValidatedBackendOptions>.Success)validation).Value;
            var generation = await backend.GenerateAsync(new(model, settings.Target, options), ct);
            if (generation is Outcome<GenerationArtifacts>.Failure generationFailure) return Failed(generationFailure.Error);
            var artifacts = ((Outcome<GenerationArtifacts>.Success)generation).Value;
            ct.ThrowIfCancellationRequested();
            var publication = await publisher.PublishAsync(artifacts, settings.Input, settings.Publication, ct);
            if (publication is Outcome<GenerationPublication>.Failure publicationFailure) return Failed(publicationFailure.Error);
            var receipt = ((Outcome<GenerationPublication>.Success)publication).Value;
            return new Outcome<GenerationSuccess>.Success(new(settings.Target.Value, backend.Descriptor.Identity, model.Original.SourcePath,
                settings.Publication.OutputDirectory, model.Model.SourceVersion.Text, model.Model.Family.ToString(), model.Model.Kind.ToString(),
                model.Representation.ToString(), receipt, artifacts.Diagnostics));
        }
        catch (OperationCanceledException) { return Failed(GenerationFailure.One(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled before publication.")); }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        { return Failed(GenerationFailure.One(GenerationFailureKind.Operational, "generation-failed", "Generation failed unexpectedly.")); }
    }
}
