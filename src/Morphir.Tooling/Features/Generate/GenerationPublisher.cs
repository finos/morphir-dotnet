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
        string output;
        try
        {
            output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(policy.OutputDirectory));
            var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input.Path));
            if (Contains(output, source) || Contains(source, output))
                return ValueTask.FromResult(Fail(GenerationFailureKind.Invocation, "input-output-overlap", "Input and output paths must not overlap."));
            if (LinkedPath(output) is not null || LinkedPath(source) is not null)
                return ValueTask.FromResult(Fail(GenerationFailureKind.Invocation, "unsafe-publication-path", "Input/output paths and their ancestors must not be links."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ValueTask.FromResult(Fail(GenerationFailureKind.Operational, "publication-path", "Publication paths could not be validated."));
        }
        var options = new PublishOptions(policy.OutputDirectory, ManifestMode.NewManifest(policy.ManifestName));
        var validation = VFileTreeModule.validatePublish(options, artifacts.Tree);
        if (validation.IsError)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Operational, "invalid-artifacts", TreeWriteErrorModule.describe(validation.ErrorValue)));
        var previous = VFileTreeModule.readManifest(options);
        if (previous.IsError)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Operational, "publication-manifest", "The previous ownership manifest could not be validated."));
        try
        {
            foreach (var location in Locations(artifacts.Tree).Concat(previous.ResultValue).Append(policy.ManifestName))
                if (LinkedPath(Path.Combine(output, location)) is not null)
                    return ValueTask.FromResult(Fail(GenerationFailureKind.Operational, "unsafe-artifact-path", "Artifact and ownership paths must not be links."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ValueTask.FromResult(Fail(GenerationFailureKind.Operational, "publication-path", "Artifact paths could not be validated."));
        }
        if (ct.IsCancellationRequested)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled before publication."));
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

    private static bool Contains(string parent, string child) => parent.Equals(child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string? LinkedPath(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
        {
            try { if (File.GetAttributes(current.FullName).HasFlag(FileAttributes.ReparsePoint)) return current.FullName; }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return null;
    }

    private static IEnumerable<string> Locations(VFileTree tree, string prefix = "")
    {
        foreach (var node in tree.Content)
        {
            if (node is TreeNode.File file && file.file.Path is not null) yield return Path.Combine(prefix, file.file.Path.Value);
            else if (node is TreeNode.Directory directory)
                foreach (var path in Locations(directory.tree, Path.Combine(prefix, directory.tree.Path))) yield return path;
        }
    }
}
