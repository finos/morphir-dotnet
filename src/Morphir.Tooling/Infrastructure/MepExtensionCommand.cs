// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Morphir.Tooling.Infrastructure;

/// <summary>
/// How a MEP extension setting becomes a process to start, for frontend and backend extensions alike, and the
/// host identity that their sessions send.
/// </summary>
public static class MepExtensionCommand
{
    /// <summary>The host name that MEP sessions send.</summary>
    public static string HostName { get; } = "morphir-dotnet";

    /// <summary>The host's release version, from its informational version.</summary>
    public static string HostVersion { get; } =
        typeof(MepExtensionCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>
    /// The executable and arguments for an extension setting, with this process's PATH, PATHEXT and
    /// DOTNET_HOST_PATH. See the overload for the rules.
    /// </summary>
    public static bool TryResolve(string extension, string workingDirectory, out string executable, out string[] arguments,
        [NotNullWhen(false)] out string? error) =>
        TryResolve(extension, workingDirectory, Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT"),
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"), out executable, out arguments, out error);

    /// <summary>
    /// The executable and arguments for an extension setting, or why there are none. The executable is always an
    /// absolute path, so process start does no search of its own:
    /// <list type="bullet">
    /// <item>A path with a directory is relative to <paramref name="workingDirectory"/>, or absolute.</item>
    /// <item>A name without a directory is searched only in the absolute entries of <paramref name="searchPath"/>,
    /// in order. Process start would also look in the host's own directory and the current directory first
    /// (CreateProcess on Windows, Process.Start on Unix), so a file planted in a checkout could run; that never
    /// happens here. On Windows a name without an extension matches <c>.exe</c> or <c>.com</c> files in PATHEXT
    /// order; batch files are not started. On Unix the file must have an execute bit.</item>
    /// <item>A path that ends in <c>.dll</c> runs on the <c>dotnet</c> host: <paramref name="dotnetHostPath"/> when it
    /// is set, else <c>dotnet</c> searched on PATH in the same way.</item>
    /// </list>
    /// </summary>
    public static bool TryResolve(string extension, string workingDirectory, string? searchPath, string? pathExtensions,
        string? dotnetHostPath, out string executable, out string[] arguments, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        executable = "";
        arguments = [];

        string path;
        if (HasDirectory(extension) || Path.IsPathRooted(extension))
            path = Path.GetFullPath(extension, workingDirectory);
        else if (FindOnPath(extension, searchPath, pathExtensions) is { } found)
            path = found;
        else
        {
            error = NotOnPath(extension);
            return false;
        }

        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(dotnetHostPath))
                executable = dotnetHostPath;
            else if (FindOnPath("dotnet", searchPath, pathExtensions) is { } dotnet)
                executable = dotnet;
            else
            {
                error = $"The extension {extension} is a .dll, which runs on the dotnet host, and neither DOTNET_HOST_PATH is set nor is dotnet on PATH.";
                return false;
            }
            arguments = [path];
            error = null;
            return true;
        }

        executable = path;
        error = null;
        return true;
    }

    /// <summary>
    /// The executable and arguments for an extension setting (see <see cref="TryResolve(string, string, out string, out string[], out string?)"/>).
    /// A name that is not on PATH throws <see cref="FileNotFoundException"/>.
    /// </summary>
    public static (string Executable, string[] Arguments) Resolve(string extension, string workingDirectory) =>
        TryResolve(extension, workingDirectory, out var executable, out var arguments, out var error)
            ? (executable, arguments)
            : throw new FileNotFoundException(error, extension);

    private static bool HasDirectory(string extension) =>
        extension.Contains(Path.DirectorySeparatorChar) || extension.Contains(Path.AltDirectorySeparatorChar);

    private static string NotOnPath(string name) =>
        $"The extension command {name} was not found on PATH. A name without a directory is searched only on PATH, not in the current " +
        "directory or the host's directory; give a path (for example ./" + name + ") to run a file there.";

    /// <summary>The first absolute PATH entry that holds the command, as an absolute path, or null.</summary>
    internal static string? FindOnPath(string name, string? searchPath, string? pathExtensions)
    {
        if (string.IsNullOrEmpty(searchPath)) return null;
        var candidates = OperatingSystem.IsWindows() && !Path.HasExtension(name)
            ? (pathExtensions ?? ".COM;.EXE")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(extension => extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".com", StringComparison.OrdinalIgnoreCase))
                .Select(extension => name + extension.ToLowerInvariant())
                .ToArray()
            : [name];

        foreach (var entry in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A relative entry (".", "bin") would be resolved against the current directory.
            if (!Path.IsPathFullyQualified(entry)) continue;
            foreach (var candidate in candidates)
            {
                var file = Path.Combine(entry, candidate);
                if (File.Exists(file) && IsExecutable(file)) return Path.GetFullPath(file);
            }
        }
        return null;
    }

    /// <summary>On Unix, whether the file has an execute bit; on Windows always true.</summary>
    internal static bool IsExecutable(string file) =>
        OperatingSystem.IsWindows()
        || (File.GetUnixFileMode(file) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
}
