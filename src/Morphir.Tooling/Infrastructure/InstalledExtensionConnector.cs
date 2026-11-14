// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Morphir.Compilation.Mep;
using Morphir.Extensions.Installation;
using Morphir.Toolchain.IO;
using Morphir.Extensions.Wasm;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Mep;
using System.Text.Json;
using Diagnostic = Morphir.Compilation.Diagnostic;
using DiagnosticSeverity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Infrastructure;

internal interface IInstalledProviderObservation { JsonElement? Provider { get; } }

/// <summary>Composes opaque installed records with the existing MEP connectors and their awaited lifetimes.</summary>
internal static class InstalledExtensionConnector
{
    internal sealed class Refused(string code, string message) : IOException(message)
    {
        public string Code { get; } = code;
    }

    private static IReadOnlyDictionary<string, string> EnvironmentOf(IReadOnlyDictionary<string, string>? environment) =>
        environment ?? System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.OrdinalIgnoreCase);

    private static async Task<CapturedExtension> CaptureAsync(Morphir.Extensions.ExtensionId id,
        IReadOnlyDictionary<string, string> environment, string? selectedHome, CancellationToken ct)
    {
        var home = selectedHome ?? environment.GetValueOrDefault("MORPHIR_HOME");
        if (home is null || !Path.IsPathFullyQualified(home))
            throw new Refused("extension.home_required", "Installed selection requires an absolute Morphir Home.");
        var host = Morphir.Toolchain.SemVerModule.tryParse(MepExtensionCommand.HostVersion)
            .Match(value => value, _ => throw new Refused("extension.host_incompatible", "Host version is not valid SemVer."));
        var result = environment.TryGetValue("MORPHIR_EXTENSION_OVERLAY", out var overlay)
            ? await InstalledExtensions.CaptureOverlayAsync(home, overlay, id, host, ct)
            : await InstalledExtensions.CaptureAsync(home, id, host, ct);
        return result.Match(value => value, failure => throw new Refused(failure.Code, failure.Message));
    }

    private static void Check(InstalledActivationRecord record, Morphir.Mep.NegotiatedSession session) =>
        record.CheckSession(session).Match(_ => true, failure => throw new Refused(failure.Code, failure.Message));

    /// <summary>
    /// Opens the channel of an installed WASM extension (design WASM-2) through the loader that configured modules
    /// use: it checks the runtime, the locked digest and the profile. The module was rehashed against the lock when
    /// it was captured.
    /// </summary>
    private static ISingleMessageChannel OpenModule(CapturedExtension captured, IWasmModuleLoader? loader)
    {
        var module = captured.Module ?? throw new Refused("extension.unavailable", "The installed extension is not a WASM module.");
        if (loader is null)
            throw new Refused("extension.runtime_unavailable", "This host has no WASM runtime, so it cannot run the installed WASM extension.");
        if (!ModuleDigest.TryParse(module.Digest.Value, out var digest))
            throw new Refused("extension.digest_mismatch", "The locked digest of the installed WASM module is not a SHA-256.");
        var source = new WasmModuleSource(captured.Record.ExtensionId.Value, module.ModulePath, null, digest);
        if (!ConfiguredWasmModule.TryOpen(loader, source, out var channel, out var code, out var message))
            throw new Refused(code, message);
        return channel!;
    }

    internal static async Task<Checked<IFrontend>> FrontendAsync(FrontendExtensionRequest request, CancellationToken ct) =>
        await FrontendAsync(request, null, ct);

    internal static async Task<Checked<IFrontend>> FrontendAsync(FrontendExtensionRequest request, IWasmModuleLoader? wasmLoader, CancellationToken ct)
    {
        CapturedExtension? captured = null;
        MepFrontend? guest = null;
        try
        {
            var environment = EnvironmentOf(request.Environment);
            captured = await CaptureAsync(request.InstalledId!, environment, request.Home, ct);
            Checked<IFrontend> connected;
            if (captured.IsWasm)
            {
                var channel = OpenModule(captured, wasmLoader);
                var channelOptions = MepFrontendOptions.Create(captured.Module!.ModulePath, [], request.WorkingDirectory, Console.Error,
                    MepExtensionCommand.HostName, MepExtensionCommand.HostVersion);
                connected = await MepFrontend.ConnectChannelCheckedAsync(channelOptions, channel, ct);
            }
            else
            {
                var options = MepFrontendOptions.Create(captured.Activation.EntryPointPath, captured.Record.Arguments,
                    request.WorkingDirectory, Console.Error, MepExtensionCommand.HostName, MepExtensionCommand.HostVersion);
                connected = await MepFrontend.ConnectCheckedAsync(options, environment, ct);
            }
            if (!connected.TryGetValue(out var frontend))
            {
                await ((IAsyncDisposable)captured).DisposeAsync();
                return connected;
            }
            guest = (MepFrontend)frontend;
            Check(captured.Record, guest.Session);
            return Checked<IFrontend>.Pass(new OwnedFrontend(guest, captured));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        {
            if (captured is not null)
                await AsyncOwnership.RunAsync(captured, async () => { if (guest is not null) await ((IAsyncDisposable)guest).DisposeAsync(); return true; });
            if (error is OperationCanceledException) throw;
            return Checked<IFrontend>.Fail([Diagnostic.Create(DiagnosticSeverity.Error,
                DiagnosticCode.Create(error is Refused refused ? refused.Code : "extension.unavailable"),error.Message,null)]);
        }
    }

    internal static async Task<Outcome<IExternalGenerationBackend>> BackendAsync(BackendExtensionRequest request, CancellationToken ct) =>
        await BackendAsync(request, null, ct);

    internal static async Task<Outcome<IExternalGenerationBackend>> BackendAsync(BackendExtensionRequest request, IWasmModuleLoader? wasmLoader, CancellationToken ct)
    {
        CapturedExtension? captured = null;
        MepGenerationBackend? guest = null;
        try
        {
            var environment = EnvironmentOf(request.Environment);
            captured = await CaptureAsync(request.InstalledId!, environment, null, ct);
            var wasm = captured.IsWasm;
            var options = new MepBackendOptions(wasm ? captured.Module!.ModulePath : captured.Activation.EntryPointPath,
                wasm ? [] : captured.Record.Arguments, request.Target)
            {
                WorkingDirectory = request.WorkingDirectory,
                Environment = environment,
                Stderr = Console.Error,
                HostName = MepExtensionCommand.HostName,
                HostVersion = MepExtensionCommand.HostVersion
            };
            var connected = wasm
                ? await MepGenerationBackend.ConnectChannelAsync(options, OpenModule(captured, wasmLoader), ct)
                : await MepGenerationBackend.ConnectAsync(options, ct);
            if (connected is Outcome<MepGenerationBackend>.Failure failure)
            {
                await ((IAsyncDisposable)captured).DisposeAsync();
                return new Outcome<IExternalGenerationBackend>.Failure(failure.Error);
            }
            guest = ((Outcome<MepGenerationBackend>.Success)connected).Value;
            Check(captured.Record, guest.Session);
            return new Outcome<IExternalGenerationBackend>.Success(new OwnedBackend(guest, captured));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        {
            if (captured is not null)
                await AsyncOwnership.RunAsync(captured, async () => { if (guest is not null) await guest.DisposeAsync(); return true; });
            if (error is OperationCanceledException) throw;
            return new Outcome<IExternalGenerationBackend>.Failure(GenerationFailure.One(GenerationFailureKind.Operational,
                error is Refused refused ? refused.Code : "extension.unavailable", error.Message));
        }
    }

    private static JsonElement Observation(string role, NegotiatedSession session, ModelTransportTuple tuple)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteString("role", role); writer.WriteString("extensionId", session.Extension.Id);
            writer.WriteString("version", session.Extension.Version); writer.WriteString("mepVersion", session.ProtocolVersion.Value);
            writer.WritePropertyName("modelTuple"); JsonSerializer.Serialize(writer, tuple, MepJson.Default.ModelTransportTuple);
            writer.WriteStartArray("checks");
            writer.WriteStartObject(); writer.WriteString("check", "integrity"); writer.WriteString("status", "passed");
            writer.WriteString("reason", "VERIFY-B manifest, tree structure, lengths and executable declarations checked while acquiring the package lifetime; no fresh full-file hash claimed."); writer.WriteEndObject();
            writer.WriteStartObject(); writer.WriteString("check", "capability"); writer.WriteString("status", "passed");
            writer.WriteString("reason", "Actual negotiated session agrees with the installed claims and the operation completed using this tuple."); writer.WriteEndObject();
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(bytes.ToArray());
        return document.RootElement.Clone();
    }

    private sealed class OwnedFrontend(MepFrontend guest, CapturedExtension lifetime) : IFrontend, IAsyncDisposable, IInstalledProviderObservation
    {
        private IFrontend Frontend => guest;
        public ComponentId Identity => Frontend.Identity;
        public FrontendTransport Transport => Frontend.Transport;
        public Morphir.Compilation.FrontendCapability Capability => Frontend.Capability;
        public JsonElement? Provider { get; private set; }
        public async Task<FrontendOutcome> CompileAsync(CompileRequest request, CancellationToken ct)
        {
            Provider = null;
            var outcome = await Frontend.CompileAsync(request, ct);
            if (outcome.Match(completed => completed.IsPassed, _ => false) && guest.LastModelTupleOrNull() is { } tuple)
                Provider = Observation("frontend", guest.Session, tuple);
            return outcome;
        }
        public ValueTask DisposeAsync() => new(AsyncOwnership.RunAsync(lifetime, async () => { await ((IAsyncDisposable)guest).DisposeAsync(); return true; }));
    }

    private sealed class OwnedBackend(MepGenerationBackend guest, CapturedExtension lifetime) : IExternalGenerationBackend, IInstalledProviderObservation
    {
        public BackendDescriptor Descriptor => guest.Descriptor;
        public string ExtensionVersion => guest.ExtensionVersion;
        public string Transport => guest.Transport;
        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => guest.ValidateOptions(options);
        public JsonElement? Provider { get; private set; }
        public async ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            Provider = null;
            var outcome = await guest.GenerateAsync(request, ct);
            if (outcome is Outcome<GenerationArtifacts>.Success && guest.LastModelTuple is { } tuple)
                Provider = Observation("backend", guest.Session, tuple);
            return outcome;
        }
        public ValueTask DisposeAsync() => new(AsyncOwnership.RunAsync(lifetime, async () => { await guest.DisposeAsync(); return true; }));
    }
}
