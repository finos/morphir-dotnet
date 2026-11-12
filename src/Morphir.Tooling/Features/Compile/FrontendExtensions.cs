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
    /// <summary>Explicit installed identity; null preserves the path/command connector.</summary>
    public Morphir.Extensions.ExtensionId? InstalledId { get; init; }
    /// <summary>The exact invocation environment, including a manager-owned overlay.</summary>
    public IReadOnlyDictionary<string,string>? Environment { get; init; }
    /// <summary>The resolved Morphir Home of this invocation.</summary>
    public string? Home { get; init; }
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
