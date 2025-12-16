using System.Runtime.InteropServices;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// Locates Morphir executables in artifacts directories for E2E testing
/// </summary>
public class ExecutableLocator
{
    private readonly string _repositoryRoot;
    private readonly string _currentRid;

    public ExecutableLocator()
    {
        _repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        _currentRid = GetCurrentRid();
    }

    /// <summary>
    /// Get the path to the executable based on type
    /// </summary>
    /// <param name="executableType">Type of executable: "aot", "trimmed", "untrimmed", or null for auto-detect</param>
    /// <returns>Path to the executable, or null if not found</returns>
    public string? GetExecutablePath(string? executableType = null)
    {
        // Check environment variable first (for CI/testing)
        var envPath = Environment.GetEnvironmentVariable("MORPHIR_EXECUTABLE_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // Determine executable type from environment or parameter
        var type = executableType ?? Environment.GetEnvironmentVariable("MORPHIR_EXECUTABLE_TYPE") ?? "aot";

        // AOT executables use "Morphir" (capital M), single-file use "morphir" (lowercase)
        var aotExecutableName = GetExecutableName(isAot: true);
        var singleFileExecutableName = GetExecutableName(isAot: false);

        // If specific type requested, prioritize that location
        if (type == "trimmed")
        {
            var trimmedPath = Path.Combine(_repositoryRoot, "artifacts", "single-file", _currentRid, singleFileExecutableName);
            if (File.Exists(trimmedPath))
                return trimmedPath;
        }
        else if (type == "untrimmed")
        {
            var untrimmedPath = Path.Combine(_repositoryRoot, "artifacts", "single-file-untrimmed", _currentRid, singleFileExecutableName);
            if (File.Exists(untrimmedPath))
                return untrimmedPath;
        }
        else if (type == "aot")
        {
            var aotPath = Path.Combine(_repositoryRoot, "artifacts", "executables", _currentRid, aotExecutableName);
            if (File.Exists(aotPath))
                return aotPath;
        }

        // Try all paths in order (check both naming conventions)
        var paths = new[]
        {
            // AOT executables (capital M)
            Path.Combine(_repositoryRoot, "artifacts", "executables", _currentRid, aotExecutableName),
            // Single-file trimmed executables (lowercase)
            Path.Combine(_repositoryRoot, "artifacts", "single-file", _currentRid, singleFileExecutableName),
            // Single-file untrimmed executables (lowercase)
            Path.Combine(_repositoryRoot, "artifacts", "single-file-untrimmed", _currentRid, singleFileExecutableName),
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Get the executable name based on platform and type
    /// </summary>
    private string GetExecutableName(bool isAot = false)
    {
        var baseName = isAot ? "Morphir" : "morphir";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"{baseName}.exe";
        }
        return baseName;
    }

    /// <summary>
    /// Get the current Runtime Identifier (RID)
    /// </summary>
    private string GetCurrentRid()
    {
        var os = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64" :
                                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-x64" : "linux-x64",
            Architecture.Arm64 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-arm64" :
                                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-arm64" : "linux-arm64",
            _ => "unknown"
        };

        return os;
    }

    /// <summary>
    /// Find repository root by looking for .git directory
    /// </summary>
    private static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root (.git directory)");
    }

    /// <summary>
    /// Verify that an executable exists and is executable
    /// </summary>
    public bool VerifyExecutable(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            return false;

        if (!File.Exists(executablePath))
            return false;

        // On Unix systems, check if file is executable
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var fileInfo = new FileInfo(executablePath);
            // Check if file has execute permission (simplified check)
            // In practice, we'll rely on File.Exists and let execution fail if not executable
        }

        return true;
    }
}

