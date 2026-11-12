// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;

namespace Morphir.Tooling.Features.Compile;

/// <summary>A request to connect the frontend extension that the settings name.</summary>
/// <param name="Extension">The extension and where it was configured.</param>
/// <param name="Language">The selected language, or null when the documents decide it.</param>
/// <param name="WorkingDirectory">The invocation directory, for relative extension paths.</param>
public sealed record FrontendExtensionRequest(Setting<string> Extension, LanguageId? Language, string WorkingDirectory)
{
    /// <summary>The module parsed by the settings resolver, or null for a process guest.</summary>
    public Features.Extensions.WasmModuleSource? WasmModule { get; init; }
}

/// <summary>
/// Connects a configured frontend extension and gives it as an <see cref="IFrontend"/> (FE-2). A transport adds
/// one connector registration in <c>CompilationScenario</c>, for example a MEP adapter whose frontend
/// <c>MepFrontend.ConnectAsync</c> creates. The runner and the handler do not change. When the frontend is
/// also <see cref="IAsyncDisposable"/>, the runner disposes it when the run ends.
/// </summary>
public interface IFrontendExtensionConnector
{
    /// <summary>Connects the extension. A failure names the extension and the reason.</summary>
    Task<Checked<IFrontend>> ConnectAsync(FrontendExtensionRequest request, CancellationToken ct);
}
