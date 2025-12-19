using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SemVersion;

namespace Morphir.Build.Helpers;

/// <summary>
/// Helper methods for finding packages and artifacts in the build output
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Regular expression pattern for matching semantic versions in package filenames
    /// </summary>
    private const string VersionPattern = @"\.(\d+\.\d+\.\d+(?:-[\w\d\.-]+)?(?:\+[\w\d\.-]+)?)$";

    /// <summary>
    /// Gets the default artifacts directory
    /// </summary>
    private static string GetDefaultArtifactsDirectory() => Path.Combine(GetRepositoryRoot(), "artifacts");

    /// <summary>
    /// Gets the default packages directory
    /// </summary>
    private static string GetDefaultPackagesDirectory() => Path.Combine(GetDefaultArtifactsDirectory(), "packages");

    /// <summary>
    /// Finds the latest package matching the given package ID
    /// Searches in the artifacts/packages directory by default
    /// </summary>
    /// <param name="packageId">Package ID to search for (e.g., "Morphir.Tool", "Morphir.Core")</param>
    /// <param name="packagesDirectory">Directory to search in (defaults to "artifacts/packages")</param>
    /// <returns>Full path to the latest package, or null if not found</returns>
    public static string? FindLatestPackage(string packageId, string? packagesDirectory = null)
    {
        var searchDir = packagesDirectory ?? GetDefaultPackagesDirectory();

        if (!Directory.Exists(searchDir))
        {
            return null;
        }

        // Find all packages matching the pattern: {packageId}.{version}.nupkg
        var pattern = $"{packageId}.*.nupkg";
        var files = Directory.GetFiles(searchDir, pattern);

        if (!files.Any())
        {
            return null;
        }

        // Parse versions and return the latest
        var packagesWithVersions = files
            .Select(f => new
            {
                Path = f,
                Version = TryParseVersionFromFilename(f)
            })
            .Where(p => p.Version != null)
            .OrderByDescending(p => p.Version)
            .ToList();

        return packagesWithVersions.FirstOrDefault()?.Path;
    }

    /// <summary>
    /// Gets the expected path for a tool package
    /// </summary>
    /// <param name="version">Version string (e.g., "0.3.0-rc.2")</param>
    /// <param name="packagesDirectory">Directory containing packages (defaults to "artifacts/packages")</param>
    /// <returns>Expected full path to the tool package</returns>
    public static string GetToolPackagePath(string version, string? packagesDirectory = null)
    {
        var searchDir = packagesDirectory ?? GetDefaultPackagesDirectory();
        return Path.Combine(searchDir, $"Morphir.Tool.{version}.nupkg");
    }

    /// <summary>
    /// Gets the expected path for a library package
    /// </summary>
    /// <param name="projectName">Project name (e.g., "Morphir.Core", "Morphir.Tooling")</param>
    /// <param name="version">Version string (e.g., "0.3.0-rc.2")</param>
    /// <param name="packagesDirectory">Directory containing packages (defaults to "artifacts/packages")</param>
    /// <returns>Expected full path to the library package</returns>
    public static string GetLibraryPackagePath(string projectName, string version, string? packagesDirectory = null)
    {
        var searchDir = packagesDirectory ?? GetDefaultPackagesDirectory();
        return Path.Combine(searchDir, $"{projectName}.{version}.nupkg");
    }

    /// <summary>
    /// Finds generated executables for a specific runtime identifier
    /// Searches in artifacts/single-file/{rid}/ by default
    /// </summary>
    /// <param name="rid">Runtime identifier (e.g., "linux-x64", "win-x64", "osx-arm64")</param>
    /// <param name="artifactsDirectory">Base artifacts directory (defaults to "artifacts")</param>
    /// <returns>List of full paths to executables found for the given RID</returns>
    public static List<string> FindGeneratedExecutables(string rid, string? artifactsDirectory = null)
    {
        var baseDir = artifactsDirectory ?? GetDefaultArtifactsDirectory();
        var singleFileDir = Path.Combine(baseDir, "single-file", rid);

        if (!Directory.Exists(singleFileDir))
        {
            return new List<string>();
        }

        // Find executables (files without extension on Unix, .exe on Windows)
        var executables = new List<string>();

        // Look for morphir executable (without extension) or morphir.exe
        var morphirExe = Path.Combine(singleFileDir, "morphir");
        var morphirExeWindows = Path.Combine(singleFileDir, "morphir.exe");

        if (File.Exists(morphirExe))
        {
            executables.Add(morphirExe);
        }

        if (File.Exists(morphirExeWindows))
        {
            executables.Add(morphirExeWindows);
        }

        // Also search for any .exe files in Windows RIDs
        if (rid.Contains("win"))
        {
            var exeFiles = Directory.GetFiles(singleFileDir, "*.exe");
            foreach (var exe in exeFiles)
            {
                if (!executables.Contains(exe))
                {
                    executables.Add(exe);
                }
            }
        }

        return executables;
    }

    /// <summary>
    /// Gets the repository root directory (where .git folder exists)
    /// </summary>
    /// <returns>Full path to the repository root</returns>
    /// <exception cref="InvalidOperationException">Thrown when repository root cannot be found</exception>
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
    /// Tries to parse a semantic version from a package filename
    /// </summary>
    /// <param name="packagePath">Full path to package file</param>
    /// <returns>Parsed SemanticVersion or null if parsing fails</returns>
    private static SemanticVersion? TryParseVersionFromFilename(string packagePath)
    {
        try
        {
            var filename = Path.GetFileNameWithoutExtension(packagePath);
            // Pattern: PackageName.Version.nupkg
            // Example: Morphir.Tool.0.3.0-rc.2.nupkg -> extract "0.3.0-rc.2"
            var match = Regex.Match(filename, VersionPattern);

            if (match.Success && SemanticVersion.TryParse(match.Groups[1].Value, out var version))
            {
                return version;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
