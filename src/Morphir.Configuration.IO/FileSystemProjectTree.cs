namespace Morphir.Configuration.IO;

/// <summary>A native tree that rejects links, including linked ancestors, before listing or reading.</summary>
public sealed class FileSystemProjectTree : IProjectTree
{
    public string Boundary { get; }

    public FileSystemProjectTree(string boundary) => Boundary = ProjectPath.normalize(Path.GetFullPath(boundary));

    public ProjectEntry Observe(string path)
    {
        path = ProjectPath.normalize(path);
        if (!ProjectPath.isAbsolute(path) || !ProjectPath.contains(Boundary, path))
            return ProjectEntry.NewFailure("Path is outside the authorized filesystem boundary: " + path);
        try
        {
            // Check every component before dereferencing any discovered link. A physical boundary
            // is required, including physical ancestors such as /private/tmp on macOS.
            var nativeRoot = Path.GetPathRoot(path)!;
            var current = ProjectPath.normalize(nativeRoot);
            foreach (var component in path[nativeRoot.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = ProjectPath.join(current, component);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return ProjectEntry.Link;
            }
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
                return ProjectEntry.NewFile(ConfigurationReader.ReadFile(new SourceIdentity(path, DocumentFormat.Memory,
                    SourceRole.RootPrimary, 0, null)).Content);
            var children = new List<string>();
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                // Directory evidence contains only directories and links, never arbitrary file contents.
                // Failed child probes are retained for Observe to diagnose, not silently discarded.
                try
                {
                    var childAttributes = File.GetAttributes(child);
                    if ((childAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                        children.Add(ProjectPath.normalize(child));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    children.Add(ProjectPath.normalize(child));
                }
            }
            return ProjectEntry.NewDirectory(ReaderValues.List(children.Order(StringComparer.Ordinal)));
        }
        catch (FileNotFoundException) { return ProjectEntry.Missing; }
        catch (DirectoryNotFoundException) { return ProjectEntry.Missing; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ProjectEntry.NewFailure(error.Message);
        }
    }
}
