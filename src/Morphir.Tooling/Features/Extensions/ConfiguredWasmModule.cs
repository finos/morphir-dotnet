// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Extensions.Wasm;
using Morphir.Mep;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>Reads a selected module from the command's existing effective configuration snapshot.</summary>
internal static class ConfiguredWasmModule
{
    internal static bool TryRead(ProjectCompileSettingsResolver.ConfigurationView config, string name, string root,
        out WasmModuleSource? source, out string? error)
    {
        source = null;
        error = null;
        if (!config.Has("extensions", name, "path")) return true;
        var path = config.String("extensions", name, "path");
        if (path is null || string.IsNullOrWhiteSpace(path.Value))
        {
            error = $"extensions.{name}.path must be a nonempty string.";
            return false;
        }
        WasmAbi? abi = null;
        ModuleDigest? digest = null;
        if (config.Has("extensions", name, "abi") && !WasmAbi.TryParse(config.String("extensions", name, "abi")?.Value, out abi))
        {
            error = $"extensions.{name}.abi must name a supported WASM profile.";
            return false;
        }
        if (config.Has("extensions", name, "sha256"))
        {
            if (!ModuleDigest.TryParse(config.String("extensions", name, "sha256")?.Value, out var parsed))
            {
                error = $"extensions.{name}.sha256 must contain 64 hexadecimal digits.";
                return false;
            }
            digest = parsed;
        }
        try { source = new(name, config.Resolve(path, root), abi, digest); }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"extensions.{name}.path: {failure.Message}";
            return false;
        }
        return true;
    }

    internal static bool TryOpen(IWasmModuleLoader? loader, WasmModuleSource source,
        out ISingleMessageChannel? channel, out string code, out string message)
    {
        channel = null;
        code = "extension.unavailable";
        message = "The host has no configured WASM module loader.";
        if (loader is null) return false;
        var loaded = loader.Load(source);
        if (loaded is WasmModuleLoad.Failed failed)
        {
            code = failed.Failure.Code ?? code;
            message = failed.Failure.Message;
            return false;
        }
        var module = ((WasmModuleLoad.Loaded)loaded).Module;
        var opening = WasmChannel.Open(module.Module, SessionLimits.Default);
        if (opening is WasmChannelOpening.Failed refused)
        {
            code = refused.Failure.Code ?? code;
            message = refused.Failure.Message;
            return false;
        }
        channel = new OutputChannel(((WasmChannelOpening.Opened)opening).Channel);
        return true;
    }

    // Only output routing and ownership are added here. HostSession retains all protocol/model checks.
    private sealed class OutputChannel(WasmChannel channel) : ISingleMessageChannel, IDisposable
    {
        public async Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> body, CancellationToken ct)
        {
            try { return await channel.RequestAsync(method, body, ct); }
            finally { Drain(); }
        }
        public async Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> body, CancellationToken ct)
        {
            try { return await channel.NotifyAsync(method, body, ct); }
            finally { Drain(); }
        }
        private void Drain()
        {
            var output = channel.GuestOutput.Drain();
            foreach (var entry in output.Outputs) Console.Error.WriteLine(entry.Text);
            if (output.DroppedBytes > 0) Console.Error.WriteLine($"WASM guest output omitted {output.DroppedBytes} bytes.");
        }
        public void Dispose()
        {
            try { channel.Dispose(); }
            finally { Drain(); }
        }
    }
}
