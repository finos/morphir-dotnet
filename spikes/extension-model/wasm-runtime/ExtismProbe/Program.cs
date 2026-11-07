// Extism for .NET (NuGet Extism.Sdk with Extism.runtime.osx-arm64) as the morphir-bytes-v1 host.
//
// Finding: Plugin.Call in Extism.Sdk 1.10.0 does not give the result of the export.
// A positive result (the response length) and 0 (exit) both return normally, and
// -1 or -2 without a plugin error also return normally. The host cannot tell them apart.
// EXTISM_SDK_CALL=1 runs that path. The default path keeps the SDK for the host
// functions, and calls libextism directly (extism_plugin_new, extism_plugin_call),
// which returns the result of handle.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Extism.Sdk;
using Spike;

return Probe.Run(new ExtismRuntime(), args);

sealed class ExtismRuntime : IGuestRuntime
{
    public string Name => Environment.GetEnvironmentVariable("EXTISM_SDK_CALL") == "1" ? "extism-sdk-call" : "extism";

    public IBytesGuest Load(byte[] wasm, Limits limits) =>
        Environment.GetEnvironmentVariable("EXTISM_SDK_CALL") == "1"
            ? new ExtismSdkGuest(wasm, limits)
            : new ExtismDirectGuest(wasm, limits);

    internal static HostFunction[] Imports(Exchange exchange)
    {
        const string ns = "morphir_bytes_v1";
        return
        [
            HostFunction.FromMethod<int>("input_len", null!, _ => exchange.InputLen()).WithNamespace(ns),
            HostFunction.FromMethod<int, int>("input_byte", null!, (_, i) => exchange.InputByte(i)).WithNamespace(ns),
            HostFunction.FromMethod<int>("output_byte", null!, (CurrentPlugin _, int b) => exchange.OutputByte(b)).WithNamespace(ns),
        ];
    }

    internal static Exception Fault(string message) =>
        new GuestFaultException(message.Split('\n')[0], new InvalidOperationException(message));
}

/// <summary>The plain SDK path: Plugin.Call. It loses the result of handle.</summary>
sealed class ExtismSdkGuest : IBytesGuest
{
    private readonly Exchange _exchange = new();
    private readonly HostFunction[] _functions;
    private readonly Plugin _plugin;

    public ExtismSdkGuest(byte[] wasm, Limits limits)
    {
        _functions = ExtismRuntime.Imports(_exchange);
        var manifest = new Manifest(new ByteArrayWasmSource(wasm, "main"))
        {
            MemoryOptions = new MemoryOptions { MaxPages = (int)(limits.MemoryBytes / 65536) },
            Timeout = limits.Deadline,
        };
        _plugin = new Plugin(manifest, _functions, new PluginIntializationOptions
        {
            WithWasi = false,
            FuelLimit = limits.Fuel is { } fuel ? (long)fuel : null,
        });
    }

    public int Handle(byte[] input, out byte[] output)
    {
        _exchange.Begin(input);
        try
        {
            _plugin.Call("handle", []);
            return 0; // The SDK does not give the result.
        }
        catch (ExtismException e)
        {
            throw ExtismRuntime.Fault(e.Message);
        }
        finally
        {
            output = _exchange.End();
        }
    }

    public void Dispose()
    {
        _plugin.Dispose();
        foreach (var f in _functions) f.Dispose();
    }
}

/// <summary>The SDK host functions with direct libextism calls for the plugin.</summary>
sealed unsafe class ExtismDirectGuest : IBytesGuest
{
    private readonly Exchange _exchange = new();
    private readonly HostFunction[] _functions;
    private readonly nint _plugin;

    public ExtismDirectGuest(byte[] wasm, Limits limits)
    {
        _functions = ExtismRuntime.Imports(_exchange);
        var timeout = limits.Deadline is { } d ? $",\"timeout_ms\":{(long)d.TotalMilliseconds}" : "";
        var manifest = Encoding.UTF8.GetBytes(
            $"{{\"wasm\":[{{\"data\":\"{Convert.ToBase64String(wasm)}\",\"name\":\"main\"}}]," +
            $"\"memory\":{{\"max_pages\":{limits.MemoryBytes / 65536}}}{timeout}}}");
        var handles = _functions.Select(NativeHandle).ToArray();
        nint error;
        fixed (byte* m = manifest)
        fixed (nint* f = handles)
        {
            _plugin = limits.Fuel is { } fuel
                ? Native.extism_plugin_new_with_fuel_limit(m, manifest.Length, f, handles.Length, false, (long)fuel, out error)
                : Native.extism_plugin_new(m, manifest.Length, f, handles.Length, false, out error);
        }
        if (_plugin == 0)
        {
            var text = Marshal.PtrToStringUTF8(error) ?? "unknown error";
            Native.extism_plugin_new_error_free(error);
            throw new InvalidOperationException(text);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_NativeHandle")]
    private static extern nint NativeHandle(HostFunction function);

    public int Handle(byte[] input, out byte[] output)
    {
        _exchange.Begin(input);
        try
        {
            int rc;
            fixed (byte* data = input)
                rc = Native.extism_plugin_call(_plugin, "handle", data, input.Length);
            var error = Native.extism_plugin_error(_plugin);
            if (error != 0) throw ExtismRuntime.Fault(Marshal.PtrToStringUTF8(error) + $" (rc={rc})");
            return rc;
        }
        finally
        {
            output = _exchange.End();
        }
    }

    public void Dispose()
    {
        Native.extism_plugin_free(_plugin);
        foreach (var f in _functions) f.Dispose();
    }

    private static class Native
    {
        [DllImport("extism")]
        public static extern nint extism_plugin_new(byte* wasm, long size, nint* functions, long count,
            [MarshalAs(UnmanagedType.I1)] bool wasi, out nint error);

        [DllImport("extism")]
        public static extern nint extism_plugin_new_with_fuel_limit(byte* wasm, long size, nint* functions, long count,
            [MarshalAs(UnmanagedType.I1)] bool wasi, long fuel, out nint error);

        [DllImport("extism")]
        public static extern void extism_plugin_new_error_free(nint error);

        [DllImport("extism")]
        public static extern void extism_plugin_free(nint plugin);

        [DllImport("extism")]
        public static extern int extism_plugin_call(nint plugin, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte* data, int length);

        [DllImport("extism")]
        public static extern nint extism_plugin_error(nint plugin);
    }
}
