// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;

namespace Morphir.Tooling.Infrastructure;

/// <summary>
/// How a MEP extension setting becomes a process to start, for frontend and backend extensions alike, and the
/// host identity that their sessions send.
/// </summary>
public static class MepExtensionCommand
{
    /// <summary>The host name that MEP sessions send.</summary>
    public const string HostName = "morphir-dotnet";

    /// <summary>The host's release version, from its informational version.</summary>
    public static string HostVersion { get; } =
        typeof(MepExtensionCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>
    /// The executable and arguments for an extension setting. A name without a directory is looked up on PATH by
    /// the process start; a relative path is relative to the working directory; a <c>.dll</c> runs on the
    /// <c>dotnet</c> host (<c>DOTNET_HOST_PATH</c> when it is set).
    /// </summary>
    public static (string Executable, string[] Arguments) Resolve(string extension, string workingDirectory)
    {
        var hasDirectory = extension.Contains(Path.DirectorySeparatorChar) || extension.Contains(Path.AltDirectorySeparatorChar);
        var path = hasDirectory && !Path.IsPathRooted(extension) ? Path.GetFullPath(extension, workingDirectory) : extension;

        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            return (string.IsNullOrEmpty(host) ? "dotnet" : host, [path]);
        }

        return (path, []);
    }
}
