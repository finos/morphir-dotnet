using System.Reflection;
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
        _repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        _currentRid = GetCurrentRid();
    }

    /// <summary>The executable type under test: <c>MORPHIR_EXECUTABLE_TYPE</c>, or <c>aot</c>.</summary>
    public static string CurrentType => Environment.GetEnvironmentVariable("MORPHIR_EXECUTABLE_TYPE") ?? "aot";

    /// <summary>
    /// The four deployment offerings as executable types. They run from the packaged release artifacts that the
    /// PackRelease target writes, installed into a new directory, not from a build output folder.
    /// </summary>
    public static IReadOnlyList<string> PackagedTypes { get; } = ["native-aot", "self-contained", "framework-dependent", "dotnet-tool"];

    /// <summary>True for an executable type that runs from a packaged release artifact.</summary>
    public static bool IsPackaged(string executableType) => PackagedTypes.Contains(executableType);

    /// <summary>True for an executable type that runs as <c>dotnet-morphir</c>: the built tool or the packaged one.</summary>
    public static bool IsToolHost(string executableType) => executableType is "tool" or "dotnet-tool";

    /// <summary>The host that an executable type runs as: <c>dotnet-morphir</c> for the tool types, else <c>morphir</c>.</summary>
    public static string HostFor(string executableType) => IsToolHost(executableType) ? "dotnet-morphir" : "morphir";

    /// <summary>The platform of this machine, for example <c>win-arm64</c>.</summary>
    public string CurrentRid => _currentRid;

    /// <summary>Where the packaged release artifacts are: <c>MORPHIR_RELEASE_DIR</c>, or <c>artifacts/release</c>.</summary>
    public string ReleaseDirectory =>
        Environment.GetEnvironmentVariable("MORPHIR_RELEASE_DIR") is { Length: > 0 } directory
            ? Path.GetFullPath(directory)
            : Path.Combine(_repositoryRoot, "artifacts", "release");

    private static string Configuration =>
        typeof(ExecutableLocator).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";

    private static string AppHost(string name) => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? name + ".exe" : name;

    /// <summary>
    /// The app host of the built dotnet tool, <c>src/Morphir.Tool/bin/&lt;configuration&gt;/net10.0/dotnet-morphir</c>.
    /// It runs <c>dotnet-morphir.dll</c> on the installed .NET runtime, as the packed tool does.
    /// </summary>
    public string? GetToolPath()
    {
        var path = Path.Combine(_repositoryRoot, "src", "Morphir.Tool", "bin", Configuration, "net10.0", AppHost("dotnet-morphir"));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// A <c>morphir</c> executable: the published AOT or single-file executable, or else the app host of the same build.
    /// </summary>
    public string? GetNativeHostPath()
    {
        foreach (var type in new[] { "aot", "trimmed", "untrimmed" })
            if (GetExecutablePath(type) is { } path && !path.Contains(Path.Combine("src", "Morphir.Tool"), StringComparison.Ordinal))
                return path;
        var appHost = Path.Combine(_repositoryRoot, "src", "Morphir", "bin", Configuration, "net10.0", AppHost("morphir"));
        return File.Exists(appHost) ? appHost : null;
    }

    /// <summary>
    /// Get the path to the executable based on type
    /// </summary>
    /// <param name="executableType">Type of executable: "aot", "trimmed", "untrimmed", "tool", one of <see cref="PackagedTypes"/>, or null for auto-detect</param>
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
        var type = executableType ?? CurrentType;

        // The tool is a different host, so never fall back to a morphir executable for it.
        if (type == "tool")
            return GetToolPath();

        // A packaged offering runs only from its installed artifact; there is no fallback to a build folder.
        if (IsPackaged(type))
            return PackagedInstallation.For(type).LaunchPath;

        // All executables use lowercase "morphir" (consistent naming)
        var aotExecutableName = GetExecutableName(isAot: false);
        var singleFileExecutableName = GetExecutableName(isAot: false);

        // If specific type requested, prioritize that location
        if (type == "trimmed")
        {
            // First try RID-specific path (when artifacts preserve structure)
            var trimmedPath = Path.Combine(_repositoryRoot, "artifacts", "single-file", _currentRid, singleFileExecutableName);
            if (File.Exists(trimmedPath))
                return trimmedPath;


            // Fallback: Check flat structure (when merge-multiple flattens artifacts)
            var flatPath = Path.Combine(_repositoryRoot, "artifacts", "single-file", singleFileExecutableName);
            if (File.Exists(flatPath))
                return flatPath;
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

        // Try all paths in order (all use lowercase naming)
        var paths = new[]
        {
            // AOT executables (lowercase) - RID-specific
            Path.Combine(_repositoryRoot, "artifacts", "executables", _currentRid, aotExecutableName),
            // Single-file trimmed executables - try RID-specific first, then flat
            Path.Combine(_repositoryRoot, "artifacts", "single-file", _currentRid, singleFileExecutableName),
            Path.Combine(_repositoryRoot, "artifacts", "single-file", singleFileExecutableName), // Flat structure fallback
            // Single-file untrimmed executables - try RID-specific first, then flat
            Path.Combine(_repositoryRoot, "artifacts", "single-file-untrimmed", _currentRid, singleFileExecutableName),
            Path.Combine(_repositoryRoot, "artifacts", "single-file-untrimmed", singleFileExecutableName), // Flat structure fallback
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Get the executable name based on platform (all use lowercase)
    /// </summary>
    private string GetExecutableName(bool isAot = false)
    {
        var baseName = "morphir";
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
            // .git is a directory in a clone and a file in a worktree
            if (Path.Exists(Path.Combine(current.FullName, ".git")))
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

