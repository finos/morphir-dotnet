// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>A request to connect the frontend extension that the settings name.</summary>
/// <param name="Extension">The extension and where it was configured.</param>
/// <param name="Language">The selected language, or null when the documents decide it.</param>
/// <param name="WorkingDirectory">The invocation directory, for relative extension paths.</param>
public sealed record FrontendExtensionRequest(Setting<string> Extension, LanguageId? Language, string WorkingDirectory);

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

/// <summary>The connector of a build that has no extension transport. Every request fails.</summary>
public sealed class UnavailableFrontendExtensionConnector : IFrontendExtensionConnector
{
    /// <inheritdoc />
    public Task<Checked<IFrontend>> ConnectAsync(FrontendExtensionRequest request, CancellationToken ct) =>
        Task.FromResult(Checked<IFrontend>.Fail([Diagnostic.Create(Severity.Error, CompileCodes.ExtensionUnavailable,
            $"The frontend extension '{request.Extension.Value}' from {ProjectCompileSettingsResolver.Describe(request.Extension.Origin)} cannot be started: this build has no frontend extension transport. MEP frontend extensions arrive in a later delivery. The dotnet tool dotnet-morphir has a built-in F# frontend; remove the extension setting to use it.",
            null)]));
}
