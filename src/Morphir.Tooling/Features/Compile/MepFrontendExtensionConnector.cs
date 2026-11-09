// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using Morphir.Compilation;
using Morphir.Compilation.Mep;

namespace Morphir.Tooling.Features.Compile;

/// <summary>
/// Connects a configured frontend extension as a MEP 0.2.0-draft.1 guest process (FE-2, MEP-2). The extension
/// setting is an executable path, a command name on PATH, or a framework-dependent <c>.dll</c> that the
/// <c>dotnet</c> host runs. The host starts the guest with explicit arguments and no shell; the runner disposes
/// the frontend, which ends the session or stops the process tree.
/// </summary>
public sealed class MepFrontendExtensionConnector : IFrontendExtensionConnector
{
    private static readonly string HostVersion =
        typeof(MepFrontendExtensionConnector).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <inheritdoc />
    public Task<Checked<IFrontend>> ConnectAsync(FrontendExtensionRequest request, CancellationToken ct)
    {
        var (executable, arguments) = Resolve(request.Extension.Value, request.WorkingDirectory);
        var options = MepFrontendOptions.Create(executable, arguments, request.WorkingDirectory, Console.Error, "morphir-dotnet", HostVersion);
        return MepFrontend.ConnectCheckedAsync(options, ct);
    }

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
