using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

/// <summary>A host supplies a bounded tree. Responses use canonical absolute '/' paths.</summary>
public interface IProjectTree
{
    string Boundary { get; }
    ProjectEntry Observe(string path);
}

/// <summary>A mounted tree with explicit file contents and optional empty directories.</summary>
public sealed class InMemoryProjectTree : IProjectTree
{
    private readonly Dictionary<string, string> _files;
    private readonly HashSet<string> _directories;
    public string Boundary { get; }
    public List<string> ObservedPaths { get; } = [];
    public InMemoryProjectTree(string boundary, IReadOnlyDictionary<string, string> files, IEnumerable<string>? directories = null)
    {
        Boundary = ProjectPath.normalize(boundary);
        _files = files.ToDictionary(p => ProjectPath.normalize(p.Key), p => p.Value, StringComparer.Ordinal);
        _directories = new HashSet<string>((directories ?? []).Select(ProjectPath.normalize), StringComparer.Ordinal) { Boundary };
        foreach (var file in _files.Keys.ToArray())
            for (var parent = ProjectPath.parent(file); ProjectPath.contains(Boundary, parent); parent = ProjectPath.parent(parent))
            {
                _directories.Add(parent);
                if (parent == Boundary) break;
            }
    }
    public ProjectEntry Observe(string path)
    {
        ObservedPaths.Add(path);
        if (!ProjectPath.contains(Boundary, path)) return ProjectEntry.NewFailure("Outside mounted boundary");
        if (_files.TryGetValue(path, out var text)) return ProjectEntry.NewFile(ConfigurationReader.ReadText(path, text).Content);
        if (_directories.Contains(path)) return ProjectEntry.NewDirectory(ReaderValues.List(_directories
            .Where(p => p != path && ProjectPath.parent(p) == path).Order(StringComparer.Ordinal)));
        return ProjectEntry.Missing;
    }
}

public sealed record ProjectDiscoveryResult(ProjectDiscovery Discovery, ProjectSelection Selection);

/// <summary>Fulfills the shared pure policy's requests without CLI construction.</summary>
public static class ProjectSystem
{
    public static ProjectDiscoveryResult Discover(IProjectTree tree, string startPath, string? selector = null)
    {
        var request = new DiscoveryRequest(ProjectPath.normalize(startPath), tree.Boundary);
        var entries = MapModule.Empty<string, ProjectEntry>();
        while (true)
        {
            var step = DiscoveryPolicy.evaluate(request, new ProjectSnapshot(entries));
            if (step is DiscoveryStep.Complete complete)
                return new(complete.Item, DiscoveryPolicy.select(complete.Item, selector is null ? null : FSharpOption<string>.Some(selector)));
            foreach (var path in ((DiscoveryStep.Requests)step).Item)
                entries = entries.Add(path, tree.Observe(path));
        }
    }
}
