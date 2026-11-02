using System.Collections.Immutable;
using Morphir.IR.Pipeline;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Adapts generated artifacts to the existing owned tree publisher.</summary>
public sealed class GenerationPublisher : IGenerationPublisher
{
    public ValueTask<Outcome<GenerationPublication>> PublishAsync(GenerationArtifacts artifacts, ModelInput input, PublicationPolicy policy, CancellationToken ct)
    {
        Outcome<GenerationPublication> Fail(GenerationFailureKind kind, string code, string message) =>
            new Outcome<GenerationPublication>.Failure(GenerationFailure.One(kind, code, message, policy.OutputDirectory));
        if (ct.IsCancellationRequested)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled before publication."));
        if (VFileTreeModule.hasErrors(artifacts.Tree))
            return ValueTask.FromResult(Fail(GenerationFailureKind.Semantic, "generation-errors", "Artifacts with generation errors cannot be published."));
        var options = new PublishOptions(policy.OutputDirectory, ManifestMode.NewManifest(policy.ManifestName));
        var published = VFileTreeModule.publish(options, artifacts.Tree);
        if (published.IsError)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Operational, "publication-failed", published.ErrorValue.IsInvalidTree
                ? TreeWriteErrorModule.describe(published.ErrorValue)
                : "Output publication failed during staging or commit. A commit-time failure may leave mixed output."));
        var manifest = VFileTreeModule.readManifest(options);
        if (manifest.IsError)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Operational, "publication-receipt", "Output was published but its ownership manifest could not be read."));
        var receipt = published.ResultValue;
        return ValueTask.FromResult<Outcome<GenerationPublication>>(new Outcome<GenerationPublication>.Success(new(
            manifest.ResultValue.ToImmutableArray(), receipt.Written.ToImmutableArray(), receipt.Unchanged.ToImmutableArray(), receipt.Removed.ToImmutableArray())));
    }
}
