// The harness with no WASM runtime. It gives the size of the AOT binary without a runtime.
using Spike;

return Probe.Run(new NoRuntime(), args);

sealed class NoRuntime : IGuestRuntime
{
    public string Name => "baseline";

    public IBytesGuest Load(byte[] wasm, Limits limits) => throw new LimitNotSupportedException("no runtime");
}
