// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Morphir.Compilation.Mep;
using Morphir.Tooling.Infrastructure;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Features.Compile;

/// <summary>
/// Connects a configured frontend extension as a MEP 0.2.0-draft.1 guest process (FE-2, MEP-2). The extension
/// setting is an executable path, a command name on PATH, or a framework-dependent <c>.dll</c> that the
/// <c>dotnet</c> host runs. The host starts the guest with explicit arguments and no shell; the runner disposes
/// the frontend, which ends the session or stops the process tree.
/// </summary>
public sealed class MepFrontendExtensionConnector(IWasmModuleLoader? wasmLoader) : IFrontendExtensionConnector
{
    /// <summary>Preserves the process-only connector's parameterless constructor.</summary>
    public MepFrontendExtensionConnector() : this(null) { }

    /// <inheritdoc />
    public Task<Checked<IFrontend>> ConnectAsync(FrontendExtensionRequest request, CancellationToken ct)
    {
        if (request.InstalledId is not null) return InstalledExtensionConnector.FrontendAsync(request, wasmLoader, ct);
        if (request.WasmModule is { } module)
        {
            if (!ConfiguredWasmModule.TryOpen(wasmLoader, module, out var channel, out var code, out var message))
                return Task.FromResult(Checked<IFrontend>.Fail([Diagnostic.Create(DiagnosticSeverity.Error, DiagnosticCode.Create(code), message, null)]));
            var channelOptions = MepFrontendOptions.Create(module.Path, [], request.WorkingDirectory, Console.Error,
                MepExtensionCommand.HostName, MepExtensionCommand.HostVersion);
            return MepFrontend.ConnectChannelCheckedAsync(channelOptions, channel!, ct);
        }
        if (!MepExtensionCommand.TryResolve(request.Extension.Value, request.WorkingDirectory, out var executable, out var arguments, out var error))
            return Task.FromResult(Checked<IFrontend>.Fail([Diagnostic.Create(DiagnosticSeverity.Error, MepFrontendCodes.Unavailable, error, null)]));
        var options = MepFrontendOptions.Create(executable, arguments, request.WorkingDirectory, Console.Error,
            MepExtensionCommand.HostName, MepExtensionCommand.HostVersion);
        return MepFrontend.ConnectCheckedAsync(options, ct);
    }

    /// <summary>
    /// The executable and arguments for an extension setting, as <see cref="MepExtensionCommand.Resolve"/> gives
    /// them for frontend and backend extensions alike. A name without a directory is searched only on PATH, and a
    /// name that is not there throws <see cref="FileNotFoundException"/>.
    /// </summary>
    public static (string Executable, string[] Arguments) Resolve(string extension, string workingDirectory) =>
        MepExtensionCommand.Resolve(extension, workingDirectory);
}
