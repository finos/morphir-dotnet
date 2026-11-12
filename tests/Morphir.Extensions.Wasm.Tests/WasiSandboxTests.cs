using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Morphir.TestSupport;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 3.4 of add-wasm-extension-host: the WASI preview 1 sandbox. No preopened directory, no arguments, no
/// environment and an empty stdin whatever the host process has; clocks and random bytes; stdout and stderr as
/// guest output that never reaches a reply; every other call an errno, at once.
/// </summary>
public class WasiSandboxTests
{
    private const int Badf = 8;
    private const int Notcapable = 76;

    private static SessionStore Open(TestSandbox sandbox) =>
        SessionStore.Open(CraftedGuest.Admit(), SessionLimits.Default, sandbox.Work).Match(
            opened => opened,
            failure => throw new InvalidOperationException(failure.Message));

    private static byte[] Output(HandleOutcome outcome) => outcome.Match(
        completed => completed.Result.Output,
        failed => throw new InvalidOperationException($"The call failed: {failed.Message}"),
        notRun => throw new InvalidOperationException($"The call did not run: {notRun.Message}"));

    private static byte[] Call(SessionStore store, string step) => Output(store.CallHandle(CraftedGuest.Input(step)));

    private static int Int32At(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static long Int64At(byte[] bytes, int offset) => BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, 8));

    [Test]
    public async Task Path_open_returns_badf_and_the_session_continues()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-wasi-");
        using var store = Open(sandbox);

        var first = Int32At(Call(store, CraftedGuest.Step.PathOpenFd3), 0);
        var stdin = Int32At(Call(store, CraftedGuest.Step.PathOpenFd0), 0);
        var later = Encoding.UTF8.GetString(Call(store, CraftedGuest.Step.VarGet));

        await Assert.That(first).IsEqualTo(Badf);
        await Assert.That(stdin).IsEqualTo(Notcapable);
        await Assert.That(later).IsEqualTo("missing");
        await Assert.That(store.Failure).IsNull();
        await Assert.That(store.RefusedWasiCalls["path_open"]).IsEqualTo(2);
    }

    [Test]
    public async Task Path_open_through_the_channel_leaves_the_session_open_for_a_later_request()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-wasi-");
        var module = WasmEngine.Shared.Load(CraftedCorpus.ModuleBytes("extism-path-open")).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(admitted => admitted, refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));
        using var channel = WasmChannel.Open(module, SessionLimits.Default, sandbox.Work).Match(opened => opened, failure => throw new InvalidOperationException(failure.Message));

        var first = await channel.RequestAsync("morphir.initialize", CraftedCorpus.FrameBody("initialize"), CancellationToken.None);
        var second = await channel.RequestAsync("morphir.shutdown", CraftedCorpus.FrameBody("shutdown"), CancellationToken.None);

        await Assert.That(Encoding.UTF8.GetString(first!)).IsEqualTo("""{"jsonrpc":"2.0","id":1,"result":"t"}""");
        await Assert.That(Encoding.UTF8.GetString(second!)).IsEqualTo("""{"jsonrpc":"2.0","id":1,"result":"t"}""");
        await Assert.That(channel.Failure).IsNull();
    }

    [Test]
    public async Task The_realtime_clock_gives_the_time_and_the_monotonic_clock_does_not_go_back()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-wasi-");
        using var store = Open(sandbox);

        var before = DateTimeOffset.UtcNow;
        var realtime = Call(store, CraftedGuest.Step.Realtime);
        var after = DateTimeOffset.UtcNow;
        var monotonic = Enumerable.Range(0, 5).Select(_ => Call(store, CraftedGuest.Step.Monotonic)).ToList();

        await Assert.That(Int32At(realtime, 0)).IsEqualTo(0);
        var nanoseconds = Int64At(realtime, 8);
        var time = DateTimeOffset.UnixEpoch.AddTicks(nanoseconds / 100);
        await Assert.That(time).IsGreaterThanOrEqualTo(before.AddMilliseconds(-1));
        await Assert.That(time).IsLessThanOrEqualTo(after.AddMilliseconds(1));
        await Assert.That(monotonic.All(bytes => Int32At(bytes, 0) == 0)).IsTrue();
        var readings = monotonic.Select(bytes => Int64At(bytes, 8)).ToList();
        await Assert.That(readings.Zip(readings.Skip(1)).All(pair => pair.Second >= pair.First)).IsTrue();
        await Assert.That(readings[0]).IsGreaterThan(0);
    }

    [Test]
    public async Task Random_get_gives_random_bytes()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-wasi-");
        using var store = Open(sandbox);

        var first = Call(store, CraftedGuest.Step.Random);
        var second = Call(store, CraftedGuest.Step.Random);

        await Assert.That(Int32At(first, 0)).IsEqualTo(0);
        await Assert.That(Int32At(second, 0)).IsEqualTo(0);
        var firstBytes = first.AsSpan(16, 32).ToArray();
        var secondBytes = second.AsSpan(16, 32).ToArray();
        await Assert.That(firstBytes.Any(b => b != 0)).IsTrue();
        await Assert.That(firstBytes.SequenceEqual(secondBytes)).IsFalse();
    }

    [Test]
    public async Task The_guest_gets_no_arguments_environment_or_stdin_although_the_host_process_has_them()
    {
        const string Variable = "MORPHIR_WASM_SANDBOX_TEST_VARIABLE";
        Environment.SetEnvironmentVariable(Variable, "the guest must not see this");
        try
        {
            await Assert.That(Environment.GetCommandLineArgs().Length).IsGreaterThan(0);
            await Assert.That(Environment.GetEnvironmentVariables().Count).IsGreaterThan(0);
            using var sandbox = TestSandbox.Create("morphir-wasm-wasi-");
            using var store = Open(sandbox);

            var world = Call(store, CraftedGuest.Step.ArgsAndEnvironment);
            var stdin = Call(store, CraftedGuest.Step.Stdin);

            // args_sizes_get and environ_sizes_get succeed with counts and sizes of 0.
            await Assert.That(Int32At(world, 0)).IsEqualTo(0);
            await Assert.That(Int32At(world, 4)).IsEqualTo(0);
            await Assert.That(Int32At(world, 16)).IsEqualTo(0);
            await Assert.That(Int32At(world, 20)).IsEqualTo(0);
            await Assert.That(Int32At(world, 24)).IsEqualTo(0);
            await Assert.That(Int32At(world, 28)).IsEqualTo(0);
            // args_get and environ_get are other functions: notcapable.
            await Assert.That(Int32At(world, 8)).IsEqualTo(Notcapable);
            await Assert.That(Int32At(world, 12)).IsEqualTo(Notcapable);
            // A read of stdin succeeds with 0 bytes.
            await Assert.That(Int32At(stdin, 0)).IsEqualTo(0);
            await Assert.That(Int32At(stdin, 4)).IsEqualTo(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    [Test]
    public async Task Unsupported_calls_return_notcapable_at_once()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-wasi-");
        using var store = Open(sandbox);

        var clock = Stopwatch.StartNew();
        var errnos = Call(store, CraftedGuest.Step.Unsupported);
        var elapsed = clock.Elapsed;

        // poll_oneoff asked for a 10 second sleep.
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(2));
        await Assert.That(Int32At(errnos, 0)).IsEqualTo(Notcapable);
        await Assert.That(Int32At(errnos, 4)).IsEqualTo(Notcapable);
        await Assert.That(Int32At(errnos, 8)).IsEqualTo(Notcapable);
        await Assert.That(store.RefusedWasiCalls.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(new[] { "fd_fdstat_get", "poll_oneoff", "sched_yield" });
    }

    [Test]
    public async Task Guest_stdout_never_reaches_a_MEP_response()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-wasi-");
        var module = CraftedGuest.Admit();
        using var channel = WasmChannel.Open(module, SessionLimits.Default, sandbox.Work).Match(opened => opened, failure => throw new InvalidOperationException(failure.Message));

        var reply = await channel.RequestAsync("morphir.initialize", CraftedGuest.Input(CraftedGuest.Step.StdoutLooksLikeReply), CancellationToken.None);

        await Assert.That(Encoding.UTF8.GetString(reply!)).IsEqualTo(CraftedGuest.OutputJson);
        var output = channel.GuestOutput.Outputs.Single();
        await Assert.That(output.Source).IsEqualTo(GuestOutputSource.Stdout);
        await Assert.That(output.Text).IsEqualTo("stdout: " + CraftedGuest.StdoutJson);
    }

    [Test]
    public async Task Stdout_and_stderr_become_guest_output_in_order()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-wasi-");
        var module = WasmEngine.Shared.Load(CraftedCorpus.ModuleBytes("extism-fd-write")).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(admitted => admitted, refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));
        using var store = SessionStore.Open(module, SessionLimits.Default, sandbox.Work).Match(opened => opened, failure => throw new InvalidOperationException(failure.Message));

        var reply = Encoding.UTF8.GetString(Output(store.CallHandle(CraftedCorpus.FrameBody("initialize"))));

        await Assert.That(reply).IsEqualTo("""{"jsonrpc":"2.0","id":1,"result":"printed"}""");
        await Assert.That(string.Join("|", store.GuestOutput.Outputs.Select(output => $"{output.Source}:{output.Text}")))
            .IsEqualTo("Stdout:stdout: kit-stdout-marker\n|Stderr:stderr: kit-stderr-marker\n");
    }

    /// <summary>
    /// The library side of task 2.6's "a guest that logs 10 MiB": a guest writes 10 MiB in one call, through WASI
    /// stdout or through <c>log_info</c>, in 10,240 writes of 1 KiB. The session keeps the newest 64 KiB of reported
    /// text and counts the rest as dropped; its reply is the guest's output, unchanged; and the managed heap does not
    /// grow with what the guest wrote.
    /// </summary>
    [Test]
    [NotInParallel] // It measures the managed heap, which any test beside it would move.
    [Arguments(CraftedGuest.Step.FloodStdout)]
    [Arguments(CraftedGuest.Step.FloodLog)]
    public async Task A_guest_that_writes_10_MiB_keeps_a_bounded_tail_and_its_reply(string step)
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-flood-");
        var module = CraftedGuest.Admit();
        using var channel = WasmChannel.Open(module, SessionLimits.Default, sandbox.Work).Match(opened => opened, failure => throw new InvalidOperationException(failure.Message));
        await channel.RequestAsync("morphir.ping", CraftedGuest.Input(CraftedGuest.Step.VarGet), CancellationToken.None);
        var heapBefore = GC.GetTotalMemory(forceFullCollection: true);

        var reply = await channel.RequestAsync("morphir.ping", CraftedGuest.Input(step), CancellationToken.None);
        var heapAfter = GC.GetTotalMemory(forceFullCollection: true);
        var tail = channel.GuestOutput;
        var outputs = tail.Outputs;

        const long Written = (long)CraftedGuest.FloodWrites * CraftedGuest.FloodChunkBytes;
        await Assert.That(Encoding.UTF8.GetString(reply!)).IsEqualTo("done");
        await Assert.That(channel.Failure).IsNull();
        await Assert.That(tail.KeptBytes).IsLessThanOrEqualTo(GuestOutputTail.TailBytes);
        await Assert.That(outputs.Sum(output => GuestOutputTail.ReportedBytes(output.Text))).IsEqualTo(tail.KeptBytes);
        await Assert.That(outputs.Count).IsGreaterThan(0);
        // Every write is either kept or counted: the dropped bytes and the kept payloads add up to what was written.
        var prefix = step == CraftedGuest.Step.FloodStdout ? "stdout: " : "log info: ";
        await Assert.That(outputs.All(output => output.Text == prefix + new string('x', CraftedGuest.FloodChunkBytes))).IsTrue();
        await Assert.That(tail.DroppedBytes + outputs.Count * (long)(prefix.Length + CraftedGuest.FloodChunkBytes))
            .IsEqualTo(CraftedGuest.FloodWrites * (long)(prefix.Length + CraftedGuest.FloodChunkBytes));
        await Assert.That(tail.DroppedBytes).IsGreaterThan(Written - GuestOutputTail.TailBytes - 1);
        // 10 MiB of text would be 20 MiB on the managed heap; the tail holds at most 64 KiB of it.
        await Assert.That(heapAfter - heapBefore).IsLessThan(4L * 1024 * 1024);

        var drained = tail.Drain();
        await Assert.That(drained.Outputs.Count).IsEqualTo(outputs.Count);
        await Assert.That(drained.DroppedBytes).IsGreaterThan(0);
        await Assert.That(tail.Outputs.Count).IsEqualTo(0);
        await Assert.That(tail.DroppedBytes).IsEqualTo(0);
    }
}
