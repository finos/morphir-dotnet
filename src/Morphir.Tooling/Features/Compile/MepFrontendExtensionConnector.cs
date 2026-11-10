// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Morphir.Compilation.Mep;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Compile;

/// <summary>
/// Connects a configured frontend extension as a MEP 0.2.0-draft.1 guest process (FE-2, MEP-2). The extension
/// setting is an executable path, a command name on PATH, or a framework-dependent <c>.dll</c> that the
/// <c>dotnet</c> host runs. The host starts the guest with explicit arguments and no shell; the runner disposes
/// the frontend, which ends the session or stops the process tree.
/// </summary>
public sealed class MepFrontendExtensionConnector : IFrontendExtensionConnector
{
    /// <inheritdoc />
    public Task<Checked<IFrontend>> ConnectAsync(FrontendExtensionRequest request, CancellationToken ct)
    {
        var (executable, arguments) = Resolve(request.Extension.Value, request.WorkingDirectory);
        var options = MepFrontendOptions.Create(executable, arguments, request.WorkingDirectory, Console.Error,
            MepExtensionCommand.HostName, MepExtensionCommand.HostVersion);
        return MepFrontend.ConnectCheckedAsync(options, ct);
    }

    /// <summary>
    /// The executable and arguments for an extension setting, as <see cref="MepExtensionCommand.Resolve"/> gives
    /// them for frontend and backend extensions alike.
    /// </summary>
    public static (string Executable, string[] Arguments) Resolve(string extension, string workingDirectory) =>
        MepExtensionCommand.Resolve(extension, workingDirectory);
}
