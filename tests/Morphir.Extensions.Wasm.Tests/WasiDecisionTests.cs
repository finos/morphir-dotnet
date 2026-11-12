using System.Diagnostics;
using Wasmtime;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// The evidence for the WASI decision of task 3.4 (design, open question "How the WASI surface is supplied"). The
/// engine's own WASI, configured as this host would configure it (no arguments, environment, preopens or stdin),
/// answers calls the contract says must give errno 76 (<c>notcapable</c>), and blocks in <c>poll_oneoff</c>, where
/// the contract says no call can block. So the host defines WASI preview 1 in managed code. If a later Wasmtime
/// answers as the contract does, this test fails and the decision can be revisited.
/// </summary>
public class WasiDecisionTests
{
    private const string Probe = """
        (module
          (import "wasi_snapshot_preview1" "path_open"
            (func $path_open (param i32 i32 i32 i32 i32 i64 i64 i32 i32) (result i32)))
          (import "wasi_snapshot_preview1" "poll_oneoff" (func $poll_oneoff (param i32 i32 i32 i32) (result i32)))
          (import "wasi_snapshot_preview1" "fd_fdstat_get" (func $fd_fdstat_get (param i32 i32) (result i32)))
          (import "wasi_snapshot_preview1" "sched_yield" (func $sched_yield (result i32)))
          (import "wasi_snapshot_preview1" "args_get" (func $args_get (param i32 i32) (result i32)))
          (memory (export "memory") 1)
          (data (i32.const 512) "/etc/passwd")
          (func (export "path_open_fd0") (result i32)
            (call $path_open (i32.const 0) (i32.const 0) (i32.const 512) (i32.const 11) (i32.const 0)
              (i64.const 0) (i64.const 0) (i32.const 0) (i32.const 256)))
          ;; One relative monotonic clock subscription of 200 ms.
          (func (export "poll_200ms") (result i32)
            (i64.store (i32.const 0) (i64.const 0)) (i64.store (i32.const 8) (i64.const 0))
            (i32.store (i32.const 16) (i32.const 1)) (i64.store (i32.const 24) (i64.const 200000000))
            (i64.store (i32.const 32) (i64.const 0)) (i64.store (i32.const 40) (i64.const 0))
            (call $poll_oneoff (i32.const 0) (i32.const 128) (i32.const 1) (i32.const 192)))
          (func (export "fdstat_stdout") (result i32) (call $fd_fdstat_get (i32.const 1) (i32.const 304)))
          (func (export "yield") (result i32) (call $sched_yield))
          (func (export "args_get") (result i32) (call $args_get (i32.const 400) (i32.const 450))))
        """;

    [Test]
    public async Task The_engine_WASI_does_not_answer_as_the_contract_requires()
    {
        using var engine = new Engine();
        using var module = Module.FromText(engine, "wasi-probe", Probe);
        using var linker = new Linker(engine);
        using var store = new Store(engine);
        linker.DefineWasi();
        store.SetWasiConfiguration(new WasiConfiguration());
        var instance = linker.Instantiate(store, module);
        int Run(string name) => instance.GetFunction(name)!.Invoke() is int errno ? errno : -1;

        var pathOpen = Run("path_open_fd0");
        var clock = Stopwatch.StartNew();
        var poll = Run("poll_200ms");
        var polled = clock.Elapsed;
        var fdstat = Run("fdstat_stdout");
        var yielded = Run("yield");
        var args = Run("args_get");

        // Contract: path_open on fd 0 is errno 76; the engine gives another errno.
        await Assert.That(pathOpen).IsNotEqualTo(76);
        // Contract: poll_oneoff is errno 76 and no call blocks; the engine sleeps for the timeout and succeeds.
        await Assert.That(poll).IsEqualTo(0);
        await Assert.That(polled).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
        // Contract: any other function is errno 76; the engine answers these with success.
        await Assert.That(fdstat).IsEqualTo(0);
        await Assert.That(yielded).IsEqualTo(0);
        await Assert.That(args).IsEqualTo(0);
    }
}
