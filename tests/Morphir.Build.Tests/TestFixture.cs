using System.IO.Compression;

namespace Morphir.Build.Tests;

/// <summary>
/// Test fixture providing helper methods for build package testing
/// </summary>
public static class TestFixture
{
    /// <summary>
    /// Get the repository root directory (where .git folder exists)
    /// </summary>
    public static string GetRepositoryRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(currentDir);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find repository root directory");
    }

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
    /// Get all entries in a NuGet package (.nupkg is a zip file)
    /// </summary>
    public static List<string> GetPackageEntries(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    /// <summary>
    /// Read an entry from a NuGet package
    /// </summary>
    public static async Task<string> ReadPackageEntry(string packagePath, string entryPath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry(entryPath);

        if (entry == null)
        {
            throw new FileNotFoundException($"Entry {entryPath} not found in package {packagePath}");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
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

    /// <summary>
    /// Extract version from package filename
    /// </summary>
    /// <param name="packagePath">Full path to .nupkg file like "/path/Morphir.Tool.0.3.0-rc.2.nupkg"</param>
    /// <returns>Version string like "0.3.0-rc.2"</returns>
    public static string GetPackageVersion(string packagePath)
    {
        var filename = Path.GetFileNameWithoutExtension(packagePath);
        // Format: PackageName.Version.nupkg
        // Example: Morphir.Tool.0.3.0-rc.2.nupkg -> extract "0.3.0-rc.2"

        // Use regex to match semantic version pattern: starts with digit.digit.digit
        // This handles: major.minor.patch[-prerelease][+build]
        var versionPattern = @"\.(\d+\.\d+\.\d+(?:-[\w\d\.-]+)?(?:\+[\w\d\.-]+)?)$";
        var match = System.Text.RegularExpressions.Regex.Match(filename, versionPattern);

        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        throw new ArgumentException($"Could not extract semantic version from package filename: {filename}");
    }
}
