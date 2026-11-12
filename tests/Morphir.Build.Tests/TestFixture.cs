namespace Morphir.Build.Tests;

/// <summary>
/// Test fixture providing helper methods for build package testing
/// </summary>
public static class TestFixture
{
    /// <summary>
    /// Get the repository root directory
    /// </summary>
    public static string GetRepositoryRoot() => Morphir.Build.Helpers.PathHelper.GetRepositoryRoot();

    /// <summary>
    /// Get the artifacts/packages directory
    /// </summary>
    public static string GetPackagesDirectory()
    {
        var repoRoot = GetRepositoryRoot();
        return Path.Combine(repoRoot, "artifacts", "packages");
    }

    /// <summary>
    /// Find the latest package matching a glob pattern
    /// </summary>
    /// <param name="pattern">Pattern like "Morphir.Tool.*.nupkg"</param>
    /// <returns>Full path to the package, or null if not found</returns>
    public static string? FindLatestPackage(string pattern)
    {
        var packagesDir = GetPackagesDirectory();

        if (!Directory.Exists(packagesDir))
        {
            return null;
        }

        // Directory.GetFiles supports * and ? wildcards directly
        var files = Directory.GetFiles(packagesDir, pattern);

        // Return the most recently created file
        return files
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    /// <summary>
    /// Check if artifacts/packages directory exists and has any .nupkg files
    /// </summary>
    public static bool HasPackages()
    {
        var packagesDir = GetPackagesDirectory();
        return Directory.Exists(packagesDir) &&
               Directory.GetFiles(packagesDir, "*.nupkg").Any();
    }
}
