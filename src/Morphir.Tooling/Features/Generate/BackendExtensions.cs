// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Extensions;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Infrastructure;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Features.Generate;

/// <summary>
/// A backend that runs outside the host, as an extension. It is named by its own extension id and version, and
/// it says how it runs. Disposing it ends the extension's session.
/// </summary>
public interface IExternalGenerationBackend : IGenerationBackend, IAsyncDisposable
{
    /// <summary>The extension's own release version, as it gave it.</summary>
    string ExtensionVersion { get; }

    /// <summary>How the backend runs, as reports write it: <c>mep</c> for a MEP guest process.</summary>
    string Transport { get; }
}

/// <summary>A request to connect the backend extension that the settings name.</summary>
/// <param name="Extension">The extension and where it was configured.</param>
/// <param name="Target">The target that the extension must generate.</param>
/// <param name="WorkingDirectory">The invocation directory, for relative extension paths.</param>
public sealed record BackendExtensionRequest(Setting<string> Extension, TargetId Target, string WorkingDirectory)
{
    /// <summary>Explicit installed identity; null preserves the path/command connector.</summary>
    public ExtensionId? InstalledId { get; init; }
    /// <summary>The exact invocation environment.</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    /// <summary>The module parsed by the settings resolver, or null for a process guest.</summary>
    public Features.Extensions.WasmModuleSource? WasmModule { get; init; }
}

/// <summary>
/// Connects a configured backend extension and gives it as an <see cref="IExternalGenerationBackend"/>, as
/// <see cref="IFrontendExtensionConnector"/> does for frontends. <c>generate</c> resolves the run over that
/// backend alone and disposes it when the run ends.
/// </summary>
public interface IBackendExtensionConnector
{
    /// <summary>Connects the extension. A failure names the extension and the reason.</summary>
    Task<Outcome<IExternalGenerationBackend>> ConnectAsync(BackendExtensionRequest request, CancellationToken ct);
}

/// <summary>
/// Connects a configured backend extension as a MEP 0.2.0-draft.1 guest process. The extension setting has the
/// same forms as for <c>compile</c>: an executable path, a command name on PATH, or a framework-dependent
/// <c>.dll</c> that the <c>dotnet</c> host runs. The host starts the guest with explicit arguments and no shell,
/// and passes the guest's stderr to its own stderr.
/// </summary>
public sealed class MepBackendExtensionConnector(IWasmModuleLoader? wasmLoader) : IBackendExtensionConnector
{
    /// <summary>Preserves the process-only connector's parameterless constructor.</summary>
    public MepBackendExtensionConnector() : this(null) { }

    /// <inheritdoc />
    public async Task<Outcome<IExternalGenerationBackend>> ConnectAsync(BackendExtensionRequest request, CancellationToken ct)
    {
        if (request.InstalledId is not null) return await InstalledExtensionConnector.BackendAsync(request, ct);
        Morphir.Mep.ISingleMessageChannel? channel = null;
        string executable;
        string[] arguments;
        if (request.WasmModule is { } module)
        {
            if (!ConfiguredWasmModule.TryOpen(wasmLoader, module, out channel, out var code, out var message))
                return new Outcome<IExternalGenerationBackend>.Failure(GenerationFailure.One(GenerationFailureKind.Operational, code, message));
            executable = module.Path;
            arguments = [];
        }
        else if (!MepExtensionCommand.TryResolve(request.Extension.Value, request.WorkingDirectory, out executable, out arguments, out var error))
            return new Outcome<IExternalGenerationBackend>.Failure(GenerationFailure.One(GenerationFailureKind.Operational, "extension.unavailable", error));
        var options = new MepBackendOptions(executable, arguments, request.Target)
        {
            WorkingDirectory = request.WorkingDirectory,
            Stderr = Console.Error,
            HostName = MepExtensionCommand.HostName,
            HostVersion = MepExtensionCommand.HostVersion,
        };
        return await (channel is null ? MepGenerationBackend.ConnectAsync(options, ct) : MepGenerationBackend.ConnectChannelAsync(options, channel, ct)) switch
        {
            Outcome<MepGenerationBackend>.Success success => new Outcome<IExternalGenerationBackend>.Success(success.Value),
            Outcome<MepGenerationBackend>.Failure failure => new Outcome<IExternalGenerationBackend>.Failure(failure.Error),
            _ => throw new InvalidOperationException("Unknown outcome.")
        };
    }
}
