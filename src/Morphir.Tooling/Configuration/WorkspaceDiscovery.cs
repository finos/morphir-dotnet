using Microsoft.Extensions.Logging;

namespace Morphir.Tooling.Configuration;

/// <summary>
/// Discovers the workspace root directory.
/// Prefers VCS root (Git) over .morphir/ directory.
/// </summary>
public class WorkspaceDiscovery
{
    private readonly ILogger<WorkspaceDiscovery> _logger;

    public WorkspaceDiscovery(ILogger<WorkspaceDiscovery> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovers workspace root starting from the specified directory.
    /// Returns null if no workspace root is found.
    /// </summary>
    public string? DiscoverWorkspaceRoot(string? startPath = null)
    {
        var currentPath = startPath ?? Directory.GetCurrentDirectory();

        _logger.LogDebug("Starting workspace discovery from: {Path}", currentPath);

        // First, look for VCS root (Git)
        var gitRoot = FindGitRoot(currentPath);

        // Also look for .morphir directory
        var morphirRoot = FindMorphirDirectory(currentPath);

        // Prefer VCS root
        if (gitRoot != null && morphirRoot != null)
        {
            // Check if they're the same
            if (string.Equals(gitRoot, morphirRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Found VCS root and .morphir/ at same location: {Root}", gitRoot);
                return gitRoot;
            }

            // Check if .morphir is below VCS root (expected case)
            if (IsSubdirectory(gitRoot, morphirRoot))
            {
                _logger.LogWarning(
                    ".morphir/ directory found at {MorphirRoot} is below VCS root at {VcsRoot}. " +
                    "Using VCS root as workspace root.",
                    morphirRoot, gitRoot);
                return gitRoot;
            }

            // .morphir is above or unrelated to VCS root - log conflict and prefer VCS
            _logger.LogWarning(
                "Conflict: VCS root at {VcsRoot} and .morphir/ at {MorphirRoot} are in different locations. " +
                "Preferring VCS root.",
                gitRoot, morphirRoot);
            return gitRoot;
        }

        if (gitRoot != null)
        {
            _logger.LogDebug("Found VCS root (no .morphir/): {Root}", gitRoot);
            return gitRoot;
        }

        if (morphirRoot != null)
        {
            _logger.LogDebug("Found .morphir/ directory (no VCS root): {Root}", morphirRoot);
            return morphirRoot;
        }

        _logger.LogDebug("No workspace root found");
        return null;
    }

    private string? FindGitRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            var gitDir = Path.Combine(currentDir.FullName, ".git");
            if (Directory.Exists(gitDir) || File.Exists(gitDir)) // .git can be a file in submodules
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        return null;
    }

    private string? FindMorphirDirectory(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            var morphirDir = Path.Combine(currentDir.FullName, ".morphir");
            if (Directory.Exists(morphirDir))
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        return null;
    }

    private static bool IsSubdirectory(string parentPath, string childPath)
    {
        var parentUri = new Uri(EnsureTrailingSlash(Path.GetFullPath(parentPath)));
        var childUri = new Uri(Path.GetFullPath(childPath));

        return parentUri.IsBaseOf(childUri);
    }

    private static string EnsureTrailingSlash(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
