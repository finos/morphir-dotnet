// WACS (NuGet WACS, a pure C# WebAssembly interpreter) as the morphir-bytes-v1 host.
using Spike;
using Wacs.Core;
using Wacs.Core.Runtime;

return Probe.Run(new WacsRuntime(), args);

sealed class WacsRuntime : IGuestRuntime
{
    public string Name => "wacs";

    public IBytesGuest Load(byte[] wasm, Limits limits) => new WacsGuest(wasm, limits);
}

sealed class WacsGuest : IBytesGuest
{
    private readonly Exchange _exchange = new();
    private readonly Limits _limits;
    private readonly WasmRuntime _runtime = new();
    private readonly Delegates.GenericFuncs _handle;

    public WacsGuest(byte[] wasm, Limits limits)
    {
        _limits = limits;
        const string ns = "morphir_bytes_v1";
        _runtime.BindHostFunction<Func<int>>((ns, "input_len"), () => _exchange.InputLen());
        _runtime.BindHostFunction<Func<int, int>>((ns, "input_byte"), i => _exchange.InputByte(i));
        _runtime.BindHostFunction<Action<int>>((ns, "output_byte"), b => _exchange.OutputByte(b));

        using var stream = new MemoryStream(wasm);
        var module = BinaryModuleParser.ParseWasm(stream);

        // WACS has no host memory limit. The host sets the ceiling as the memory
        // maximum before instantiation, as upstream does with a module rewrite.
        var ceilingPages = limits.MemoryBytes / 65536;
        foreach (var memory in module.Memories)
            if (memory.Limits.Maximum is null || memory.Limits.Maximum > ceilingPages)
                memory.Limits.Maximum = ceilingPages;

        var instance = _runtime.InstantiateModule(module);
        _runtime.RegisterModule("guest", instance);
        if (!_runtime.TryGetExportedFunction(("guest", "handle"), out var handle))
            throw new InvalidOperationException("the module does not export handle");
        // CreateInvokerFunc<Value> works under JIT but fails under Native AOT
        // ("Return type mismatch"). The untyped invoker works in both.
        _handle = _runtime.CreateInvoker(handle, new InvokerOptions
        {
            GasLimit = limits.Fuel is { } fuel ? (long)fuel : 0,
        });
    }

    public int Handle(byte[] input, out byte[] output)
    {
        _exchange.Begin(input);
        using var deadline = _limits.Deadline is { } d ? new CancellationTokenSource(d) : null;
        // The cancellation token is checked only at function-call boundaries.
        _runtime.ExecContext.Ct = deadline?.Token ?? CancellationToken.None;
        try
        {
            return _handle()[0];
        }
        catch (Exception e) when (e is not GuestFaultException)
        {
            // The invoker wraps guest faults in TargetInvocationException.
            var inner = e is System.Reflection.TargetInvocationException { InnerException: { } i } ? i : e;
            throw new GuestFaultException(inner.GetType().Name + ": " + inner.Message.Split('\n')[0], inner);
        }
        finally
        {
            output = _exchange.End();
        }
    }

    public void Dispose() { }
}
