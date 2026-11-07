// Wasmtime for .NET (NuGet Wasmtime) as the morphir-bytes-v1 host.
using Spike;
using Wasmtime;

return Probe.Run(new WasmtimeRuntime(), args);

sealed class WasmtimeRuntime : IGuestRuntime
{
    public string Name => "wasmtime";

    public IBytesGuest Load(byte[] wasm, Limits limits) => new WasmtimeGuest(wasm, limits);
}

sealed class WasmtimeGuest : IBytesGuest
{
    // One epoch tick is 10 ms. A real host keeps one ticker thread per engine.
    private const int TickMilliseconds = 10;

    private readonly Exchange _exchange = new();
    private readonly Limits _limits;
    private readonly Engine _engine;
    private readonly Module _module;
    private readonly Linker _linker;
    private readonly Store _store;
    private readonly Func<int> _handle;
    private readonly Timer? _ticker;

    public WasmtimeGuest(byte[] wasm, Limits limits)
    {
        _limits = limits;
        using var config = new Config()
            .WithFuelConsumption(limits.Fuel is not null)
            .WithEpochInterruption(limits.Deadline is not null);
        _engine = new Engine(config);
        _module = Module.FromBytes(_engine, "guest", wasm);
        _linker = new Linker(_engine);
        _store = new Store(_engine);
        _store.SetLimits(memorySize: limits.MemoryBytes);

        // Three variants of the binding (see publish.sh):
        // - default: untyped callbacks and an untyped call. No trim or AOT warnings.
        //   Each host call boxes its values, so host calls are slow.
        // - WASMTIME_TYPED_IMPORTS: typed callbacks and an untyped call. One warning
        //   (IL3050 in ValueRaw.Converter<T>, a branch for tuple types that int does not reach).
        //   Host calls are about 8 times faster.
        // - WASMTIME_TYPED: typed callbacks and a typed call. Five warnings
        //   (IL2055, IL2070, IL2072, IL3050 x2).
#if WASMTIME_TYPED || WASMTIME_TYPED_IMPORTS
        _linker.Define("morphir_bytes_v1", "input_len", Function.FromCallback(_store, _exchange.InputLen));
        _linker.Define("morphir_bytes_v1", "input_byte", Function.FromCallback<int, int>(_store, _exchange.InputByte));
        _linker.Define("morphir_bytes_v1", "output_byte", Function.FromCallback<int>(_store, _exchange.OutputByte));
#else
        ValueKind[] none = [], i32 = [ValueKind.Int32];
        _linker.Define("morphir_bytes_v1", "input_len", Function.FromCallback(_store,
            (Caller _, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => r[0] = _exchange.InputLen(), none, i32));
        _linker.Define("morphir_bytes_v1", "input_byte", Function.FromCallback(_store,
            (Caller _, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => r[0] = _exchange.InputByte(a[0].AsInt32()), i32, i32));
        _linker.Define("morphir_bytes_v1", "output_byte", Function.FromCallback(_store,
            (Caller _, ReadOnlySpan<ValueBox> a, Span<ValueBox> r) => _exchange.OutputByte(a[0].AsInt32()), i32, none));
#endif

        var instance = _linker.Instantiate(_store, _module);
#if WASMTIME_TYPED
        _handle = instance.GetFunction<int>("handle")
            ?? throw new InvalidOperationException("the module does not export handle as () -> i32");
#else
        var handle = instance.GetFunction("handle")
            ?? throw new InvalidOperationException("the module does not export handle");
        _handle = () => (int)handle.Invoke(ReadOnlySpan<ValueBox>.Empty)!;
#endif

        if (limits.Deadline is not null)
            _ticker = new Timer(_ => _engine.IncrementEpoch(), null, TickMilliseconds, TickMilliseconds);
    }

    public int Handle(byte[] input, out byte[] output)
    {
        if (_limits.Fuel is { } fuel) _store.Fuel = fuel;
        if (_limits.Deadline is { } deadline)
            _store.SetEpochDeadline((ulong)Math.Ceiling(deadline.TotalMilliseconds / TickMilliseconds));
        _exchange.Begin(input);
        try
        {
            return _handle();
        }
        catch (TrapException e)
        {
            throw new GuestFaultException($"trap {e.Type}", e);
        }
        finally
        {
            output = _exchange.End();
        }
    }

    public void Dispose()
    {
        _ticker?.Dispose();
        _store.Dispose();
        _linker.Dispose();
        _module.Dispose();
        _engine.Dispose();
    }
}
