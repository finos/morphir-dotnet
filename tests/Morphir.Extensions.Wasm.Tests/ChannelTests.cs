using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Morphir.Mep;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 3.1 of add-wasm-extension-host: the Extism adapter, <see cref="WasmChannel"/>. One guest instance for the
/// life of the channel, one <c>handle</c> call per request, Extism errors and every failed call ending the
/// instance, the host session's frame limits, no notification for a guest that advertises nothing, and
/// <c>morphir.exit</c> ending the instance.
/// </summary>
public class ChannelTests
{
    private const string Initialize = "morphir.initialize";
    private const string Initialized = "morphir.initialized";
    private const string Shutdown = "morphir.shutdown";
    private const string Exit = "morphir.exit";

    private static readonly PeerInfo Host = new(PeerKind.Cli, "morphir-dotnet-tests", "0.0.0");

    /// <summary>How soon a cancelled call must stop. The engine stops it at the first epoch tick after the
    /// cancellation (<see cref="WasmEngine.EpochInterval"/>, 10 ms); this is 200 ticks, for a runner whose processors
    /// are busy, and still well under the call deadline at which a call that ignored its cancellation would end.</summary>
    private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(2);

    private static ExtismModule Admit(string module) =>
        WasmEngine.Shared.Load(CraftedCorpus.ModuleBytes(module)).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));

    private static WasmChannel Open(string module, SessionLimits? limits = null) =>
        WasmChannel.Open(Admit(module), limits ?? SessionLimits.Default).Match(
            channel => channel,
            failure => throw new InvalidOperationException($"The channel did not open: {failure.Message}"));

    private static byte[] Request(long id, string method = Initialize) =>
        Encoding.UTF8.GetBytes($$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"{{{method}}}","params":{}}""");

    private static byte[] Notification(string method) =>
        Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","method":"{{method}}"}""");

    /// <summary>The <c>result</c> of the count-calls guest's reply: the number of its handle calls so far.</summary>
    private static int CallNumber(byte[]? reply)
    {
        using var document = JsonDocument.Parse(reply ?? throw new InvalidOperationException("No reply."));
        return document.RootElement.GetProperty("result").GetInt32();
    }

    [Test]
    public async Task Each_request_is_one_handle_call()
    {
        using var channel = Open("extism-count-calls");

        var first = await channel.RequestAsync(Initialize, Request(1), CancellationToken.None);
        var second = await channel.RequestAsync("morphir.ping", Request(2, "morphir.ping"), CancellationToken.None);
        var third = await channel.RequestAsync(Shutdown, Request(3, Shutdown), CancellationToken.None);

        await Assert.That(channel.HandleCalls).IsEqualTo(3);
        await Assert.That(CallNumber(first)).IsEqualTo(1);
        await Assert.That(CallNumber(second)).IsEqualTo(2);
        await Assert.That(CallNumber(third)).IsEqualTo(3);
    }

    [Test]
    public async Task A_notification_to_a_guest_that_advertises_nothing_does_not_call_the_guest()
    {
        using var channel = Open("extism-count-calls");
        await channel.RequestAsync(Initialize, Request(1), CancellationToken.None);

        var reply = await channel.NotifyAsync(Initialized, Notification(Initialized), CancellationToken.None);
        var next = await channel.RequestAsync(Shutdown, Request(2, Shutdown), CancellationToken.None);

        await Assert.That(reply).IsNull();
        await Assert.That(channel.DeliversNotifications).IsFalse();
        await Assert.That(channel.HandleCalls).IsEqualTo(2);
        // The guest counts its own calls: 2 means morphir.initialized never reached it.
        await Assert.That(CallNumber(next)).IsEqualTo(2);
    }

    [Test]
    public async Task An_Extism_error_ends_the_session_with_its_text()
    {
        using var channel = Open("extism-error");

        var thrown = await Assert.That(async () => await channel.RequestAsync(Initialize, Request(1), CancellationToken.None))
            .Throws<WasmChannelException>();

        var error = thrown!.Failure as WasmChannelFailure.ExtismError;
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Text).IsEqualTo("boom");
        await Assert.That(error.ReturnCode).IsEqualTo(1);
        await Assert.That(thrown.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(thrown.Method).IsEqualTo(Initialize);
        await Assert.That(thrown.Message).Contains("boom");
        await Assert.That(thrown.Message).Contains("ceiling");
        await Assert.That(channel.Failure).IsEqualTo(thrown.Failure);
        await Assert.That(channel.InstanceEnded).IsTrue();
    }

    [Test]
    public async Task An_Extism_error_reaches_the_host_session_as_a_channel_failure()
    {
        using var channel = Open("extism-error");
        var session = new HostSession(channel, Host);

        var initialized = await session.InitializeAsync(CancellationToken.None);

        await Assert.That(initialized.Match(_ => "ok", failure => failure.ToString())).Contains("boom");
        var error = session.FailureOrNull()?.Match<Exception?>(
            noReply: _ => null,
            replyForAnotherRequest: (_, _, _) => null,
            notAResponse: (_, _) => null,
            invalidReply: (_, _) => null,
            replyTooLarge: (_, _) => null,
            answeredNotification: _ => null,
            cancelled: _ => null,
            channelFailed: (_, exception) => exception);
        var thrown = error as WasmChannelException;
        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(thrown.Memory.CeilingBytes).IsEqualTo(SessionLimits.Default.MemoryCeilingBytes);
    }

    /// <summary>A guest that allocates kernel blocks until the kernel cannot grow (64 KiB, then 1 KiB), so that the
    /// kernel's memory, which holds its input and output, is at its ceiling, then does <paramref name="then"/>.
    /// <c>$out</c> is a 64-byte block allocated before the kernel filled.</summary>
    private static WasmChannel OpenFillingTheKernel(string then)
    {
        var module = CraftedGuest.AdmitWat($$"""
            (module
              (import "extism:host/env" "alloc" (func $alloc (param i64) (result i64)))
              (import "extism:host/env" "output_set" (func $output_set (param i64 i64)))
              (func (export "handle") (result i32)
                (local $out i64)
                (local.set $out (call $alloc (i64.const 64)))
                (block $big (loop $more (br_if $big (i64.eqz (call $alloc (i64.const 65536)))) (br $more)))
                (block $small (loop $more (br_if $small (i64.eqz (call $alloc (i64.const 1024)))) (br $more)))
                {{then}}))
            """);
        return WasmChannel.Open(module, SessionLimits.Default with { MemoryCeilingBytes = 32 * 65536L, ResponseFrameBytes = 64 }).Match(
            opened => opened,
            failure => throw new InvalidOperationException($"The channel did not open: {failure.Message}"));
    }

    [Test]
    public async Task A_non_zero_result_with_the_kernel_memory_at_its_ceiling_stays_a_failure()
    {
        // Contract memory rule guest-reported: a call the guest ended itself is failure, also at the ceiling.
        using var channel = OpenFillingTheKernel("(i32.const 1)");

        var thrown = await Assert.That(async () => await channel.RequestAsync(Initialize, Request(1), CancellationToken.None))
            .Throws<WasmChannelException>();

        await Assert.That(thrown!.Failure).IsTypeOf<WasmChannelFailure.NonZeroResult>();
        await Assert.That(thrown.Memory.KernelAtCeiling).IsTrue();
        await Assert.That(thrown.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(thrown.Failure.FaultKind).IsEqualTo("failure");
        await Assert.That(thrown.Message).Contains("handle returned 1");
        await Assert.That(thrown.Message).Contains("kernel memory 2 MiB");
        await Assert.That(thrown.Message).Contains("ceiling 2 MiB");
    }

    [Test]
    public async Task A_response_over_the_limit_with_the_kernel_memory_at_its_ceiling_keeps_its_code()
    {
        // A failure the host detected keeps its own code at the ceiling: 64 bytes of output, over a 64-byte response
        // frame limit once the Content-Length header is counted.
        using var channel = OpenFillingTheKernel("(call $output_set (local.get $out) (i64.const 64)) (i32.const 0)");

        var thrown = await Assert.That(async () => await channel.RequestAsync(Initialize, Request(1), CancellationToken.None))
            .Throws<WasmChannelException>();

        await Assert.That(thrown!.Failure).IsTypeOf<WasmChannelFailure.ResponseOverLimit>();
        await Assert.That(thrown.Memory.KernelAtCeiling).IsTrue();
        await Assert.That(thrown.Code).IsEqualTo(WasmProfileContract.Pinned.ResponseLimitCode);
        await Assert.That(thrown.Failure.FaultKind).IsEqualTo("failure");
        await Assert.That(thrown.Message).Contains("kernel memory 2 MiB");
        await Assert.That(thrown.Message).Contains("ceiling 2 MiB");
    }

    [Test]
    public async Task An_Extism_error_with_the_guest_at_the_ceiling_stays_a_failure()
    {
        // The module of bounds/error-at-ceiling: it grows its memory to the ceiling, sets the Extism error "boom" and
        // returns 1. The guest ended the call itself (contract memory rule guest-reported).
        using var channel = Open("extism-grow-then-error");

        var thrown = await Assert.That(async () => await channel.RequestAsync(Initialize, Request(1), CancellationToken.None))
            .Throws<WasmChannelException>();

        var error = thrown!.Failure as WasmChannelFailure.ExtismError;
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Text).IsEqualTo("boom");
        await Assert.That(error.ReturnCode).IsEqualTo(1);
        await Assert.That(thrown.Memory.GuestAtCeiling).IsTrue();
        await Assert.That(thrown.Memory.GuestBytes).IsEqualTo(SessionLimits.Default.MemoryCeilingBytes);
        await Assert.That(thrown.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(thrown.Failure.FaultKind).IsEqualTo("failure");
        await Assert.That(thrown.Message).Contains("guest memory 256 MiB");
        await Assert.That(thrown.Message).Contains("ceiling 256 MiB");
        await Assert.That(channel.InstanceEnded).IsTrue();
    }

    public static IEnumerable<Func<WasmChannelFailure>> GuestReportedFailuresAtTheCeiling()
    {
        var full = new MemoryUse(256L * 1024 * 1024, 256L * 1024 * 1024, 256L * 1024 * 1024);
        yield return () => new WasmChannelFailure.ExtismError("boom", 1, full);
        yield return () => new WasmChannelFailure.NonZeroResult(1, 0, full);
        yield return () => new WasmChannelFailure.NoOutput(full);
        yield return () => new WasmChannelFailure.AnsweredNotification(2, full);
        yield return () => new WasmChannelFailure.ResponseOverLimit(100, 64, full);
    }

    [Test]
    [MethodDataSource(nameof(GuestReportedFailuresAtTheCeiling))]
    public async Task A_channel_failure_with_both_memories_at_the_ceiling_keeps_its_own_code(WasmChannelFailure failure)
    {
        await Assert.That(failure.Memory.GuestAtCeiling).IsTrue();
        await Assert.That(failure.Memory.KernelAtCeiling).IsTrue();
        await Assert.That(failure.FaultKind).IsEqualTo("failure");
        await Assert.That(failure.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(failure.Message).Contains("guest memory 256 MiB");
        await Assert.That(failure.Message).Contains("kernel memory 256 MiB");
        await Assert.That(failure.Message).Contains("ceiling 256 MiB");
    }

    [Test]
    public async Task A_non_zero_result_ends_the_session_and_its_output_is_not_passed_on()
    {
        using var channel = Open("extism-nonzero");

        var thrown = await Assert.That(async () => await channel.RequestAsync(Initialize, Request(1), CancellationToken.None))
            .Throws<WasmChannelException>();

        var result = thrown!.Failure as WasmChannelFailure.NonZeroResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ReturnCode).IsEqualTo(1);
        await Assert.That(result.OutputBytes).IsEqualTo(40);
        await Assert.That(thrown.Code).IsEqualTo("extension.incomplete_session");
    }

    [Test]
    public async Task No_output_for_a_request_is_no_reply_and_ends_the_instance()
    {
        using var channel = Open("extism-no-output");

        var reply = await channel.RequestAsync(Initialize, Request(1), CancellationToken.None);

        await Assert.That(reply).IsNull();
        await Assert.That(channel.Failure is WasmChannelFailure.NoOutput).IsTrue();
        await Assert.That(channel.Failure!.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(channel.InstanceEnded).IsTrue();
    }

    [Test]
    public async Task A_failed_call_ends_the_session_and_a_later_request_does_not_call_the_guest()
    {
        using var channel = Open("extism-trap");
        var first = await Assert.That(async () => await channel.RequestAsync(Initialize, Request(1), CancellationToken.None))
            .Throws<WasmChannelException>();

        var later = await Assert.That(async () => await channel.RequestAsync(Shutdown, Request(2, Shutdown), CancellationToken.None))
            .Throws<WasmChannelException>();

        await Assert.That(first!.Code).IsEqualTo("extension.guest_trap");
        await Assert.That(later!.Failure).IsEqualTo(first.Failure);
        await Assert.That(later.Method).IsEqualTo(Shutdown);
        await Assert.That(channel.HandleCalls).IsEqualTo(1);
        await Assert.That(channel.InstanceEnded).IsTrue();
    }

    [Test]
    public async Task A_request_over_the_limit_is_refused_before_the_guest_is_called()
    {
        // extism-trap traps when called, so a refusal with the request limit shows that handle did not run.
        using var channel = Open("extism-trap", SessionLimits.Default with { RequestFrameBytes = 64 });
        var body = CraftedCorpus.FrameBody("initialize");
        var frameBytes = Encoding.ASCII.GetByteCount($"Content-Length: {body.Length}\r\n\r\n") + body.Length;

        var thrown = await Assert.That(async () => await channel.RequestAsync(Initialize, body, CancellationToken.None))
            .Throws<WasmChannelException>();

        var over = thrown!.Failure as WasmChannelFailure.RequestOverLimit;
        await Assert.That(over).IsNotNull();
        await Assert.That(over!.FrameBytes).IsEqualTo(frameBytes);
        await Assert.That(over.LimitBytes).IsEqualTo(64L);
        await Assert.That(thrown.Code).IsEqualTo("extension.request_limit");
        // The diagnostic names the limit and the frame's size.
        await Assert.That(thrown.Message).Contains("64");
        await Assert.That(thrown.Message).Contains(frameBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(channel.HandleCalls).IsEqualTo(0);
        await Assert.That(channel.InstanceEnded).IsTrue();
    }

    [Test]
    public async Task A_request_frame_at_the_limit_is_passed()
    {
        var body = CraftedCorpus.FrameBody("initialize");
        var frameBytes = Encoding.ASCII.GetByteCount($"Content-Length: {body.Length}\r\n\r\n") + body.Length;
        using var channel = Open("extism-echo", SessionLimits.Default with { RequestFrameBytes = frameBytes });

        var reply = await channel.RequestAsync(Initialize, body, CancellationToken.None);

        await Assert.That(reply).IsNotNull();
        await Assert.That(channel.HandleCalls).IsEqualTo(1);
    }

    [Test]
    public async Task A_response_over_the_limit_ends_the_session_and_is_not_passed_on()
    {
        using var channel = Open("extism-echo", SessionLimits.Default with { ResponseFrameBytes = 32 });
        var body = CraftedCorpus.FrameBody("initialize");

        var thrown = await Assert.That(async () => await channel.RequestAsync(Initialize, body, CancellationToken.None))
            .Throws<WasmChannelException>();

        var over = thrown!.Failure as WasmChannelFailure.ResponseOverLimit;
        await Assert.That(over).IsNotNull();
        await Assert.That(over!.LimitBytes).IsEqualTo(32L);
        await Assert.That(over.FrameBytes).IsGreaterThan(body.Length);
        await Assert.That(thrown.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(channel.HandleCalls).IsEqualTo(1);
        await Assert.That(channel.InstanceEnded).IsTrue();
    }

    [Test]
    public async Task The_default_frame_limits_are_the_contract_limits()
    {
        using var contract = JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var limits = contract.RootElement.GetProperty("hostSession").GetProperty("frameLimits");

        await Assert.That(SessionLimits.Default.RequestFrameBytes).IsEqualTo(limits.GetProperty("requestBytes").GetInt64());
        await Assert.That(SessionLimits.Default.ResponseFrameBytes).IsEqualTo(limits.GetProperty("responseBytes").GetInt64());
        await Assert.That(SessionLimits.Default.RequestFrameBytes).IsEqualTo(6L * 1024 * 1024);
        await Assert.That(SessionLimits.Default.ResponseFrameBytes).IsEqualTo(16L * 1024 * 1024);
    }

    [Test]
    public async Task Cancelling_during_a_call_throws_after_the_call_has_stopped()
    {
        using var channel = Open("extism-spin", SessionLimits.Default with { InstructionBudget = ulong.MaxValue });
        using var cancellation = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var cancelledAt = TimeSpan.Zero;
        TimeSpan? stoppedAt = null;
        // The cancellation comes from a thread of its own, which records its moment and then the moment the instance
        // ended. The instance is discarded only after the call has returned, so that moment is when the call
        // stopped. Neither moment waits for the thread pool: a timer on it fires late, and the awaiting test resumes
        // late, when other tests' spinning guests and blocked threads keep it busy. Measured after the await, the
        // test would time the thread pool, not how fast the call stops.
        var canceller = new Thread(() =>
        {
            Thread.Sleep(300);
            cancelledAt = clock.Elapsed;
            cancellation.Cancel();
            while (clock.Elapsed - cancelledAt < StopLimit)
            {
                if (channel.InstanceEnded)
                {
                    stoppedAt = clock.Elapsed;
                    return;
                }

                Thread.Sleep(1);
            }
        })
        { IsBackground = true };
        canceller.Start();

        await Assert.That(async () => await channel.RequestAsync(Initialize, Request(1), cancellation.Token))
            .Throws<OperationCanceledException>();
        canceller.Join();

        await Assert.That(channel.InstanceEnded).IsTrue();
        await Assert.That(channel.Failure?.Outcome).IsEqualTo("cancelled");
        await Assert.That(stoppedAt).IsNotNull();
        await Assert.That(stoppedAt!.Value - cancelledAt).IsLessThan(StopLimit);
        await Assert.That(channel.HandleCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Cancelling_a_host_session_request_ends_the_session_as_cancelled()
    {
        using var channel = Open("extism-spin", SessionLimits.Default with { InstructionBudget = ulong.MaxValue });
        var session = new HostSession(channel, Host);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var initialized = await session.InitializeAsync(cancellation.Token);

        await Assert.That(initialized.Match(_ => 0, failure => failure.Match(error => error.Code, () => 0, _ => 0, _ => 0))).IsEqualTo(-32800);
        await Assert.That(session.FailureOrNull()?.Match(_ => "", (_, _, _) => "", (_, _) => "", (_, _) => "", (_, _) => "", _ => "", method => $"cancelled {method}", (_, _) => ""))
            .IsEqualTo($"cancelled {Initialize}");
        await Assert.That(channel.InstanceEnded).IsTrue();
    }

    [Test]
    public async Task Exit_ends_the_instance_and_no_later_call_runs()
    {
        using var channel = Open("extism-echo");
        await channel.RequestAsync(Initialize, CraftedCorpus.FrameBody("initialize"), CancellationToken.None);

        var reply = await channel.NotifyAsync(Exit, Notification(Exit), CancellationToken.None);

        await Assert.That(reply).IsNull();
        await Assert.That(channel.InstanceEnded).IsTrue();
        await Assert.That(channel.Failure).IsNull();
        // The guest advertised nothing, so exit ended its instance without a call.
        await Assert.That(channel.HandleCalls).IsEqualTo(1);
        await Assert.That(async () => await channel.RequestAsync(Initialize, Request(2), CancellationToken.None))
            .Throws<ObjectDisposedException>();
        await Assert.That(channel.HandleCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Two_channels_on_one_compiled_module_share_nothing()
    {
        var module = Admit("extism-count-calls");
        using var first = WasmChannel.Open(module, SessionLimits.Default).Match(channel => channel, failure => throw new InvalidOperationException(failure.Message));
        using var second = WasmChannel.Open(module, SessionLimits.Default).Match(channel => channel, failure => throw new InvalidOperationException(failure.Message));

        await first.RequestAsync(Initialize, Request(1), CancellationToken.None);
        var firstAgain = await first.RequestAsync(Shutdown, Request(2, Shutdown), CancellationToken.None);
        var secondFirst = await second.RequestAsync(Initialize, Request(1), CancellationToken.None);
        await first.NotifyAsync(Exit, Notification(Exit), CancellationToken.None);
        var secondAfterExit = await second.RequestAsync(Shutdown, Request(2, Shutdown), CancellationToken.None);

        await Assert.That(CallNumber(firstAgain)).IsEqualTo(2);
        await Assert.That(CallNumber(secondFirst)).IsEqualTo(1);
        await Assert.That(CallNumber(secondAfterExit)).IsEqualTo(2);
        await Assert.That(first.InstanceEnded).IsTrue();
        await Assert.That(second.InstanceEnded).IsFalse();
        await Assert.That(first.HandleCalls).IsEqualTo(2);
        await Assert.That(second.HandleCalls).IsEqualTo(2);
    }

    [Test]
    public async Task A_failed_channel_does_not_affect_another_on_the_same_module()
    {
        var module = Admit("extism-echo");
        using var failed = WasmChannel.Open(module, SessionLimits.Default with { ResponseFrameBytes = 32 }).Match(channel => channel, failure => throw new InvalidOperationException(failure.Message));
        using var healthy = WasmChannel.Open(module, SessionLimits.Default).Match(channel => channel, failure => throw new InvalidOperationException(failure.Message));
        var body = CraftedCorpus.FrameBody("initialize");

        await Assert.That(async () => await failed.RequestAsync(Initialize, body, CancellationToken.None)).Throws<WasmChannelException>();
        var reply = await healthy.RequestAsync(Initialize, body, CancellationToken.None);

        await Assert.That(Encoding.UTF8.GetString(reply!)).IsEqualTo(Encoding.UTF8.GetString(body));
        await Assert.That(healthy.Failure).IsNull();
    }

    [Test]
    [NotInParallel] // It keeps every processor busy, which would delay the timing of any test beside it.
    public async Task The_deadline_holds_while_the_thread_pool_is_busy()
    {
        // The call runs on a thread the channel owns, not on the pool: a pool whose threads all spin, as they do when
        // other calls run WASM, must not delay the call or its deadline. The wait below needs no pool thread either.
        var deadline = TimeSpan.FromMilliseconds(300);
        using var channel = Open("extism-spin", SessionLimits.Default with { InstructionBudget = ulong.MaxValue, CallDeadline = deadline });
        ThreadPool.GetMinThreads(out var workers, out _);
        using var release = new ManualResetEventSlim();
        var busy = Stopwatch.StartNew();
        for (var blocked = 0; blocked < workers * 2; blocked++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (!release.IsSet && busy.Elapsed < TimeSpan.FromSeconds(5))
                {
                }
            });
        }

        bool stopped;
        TimeSpan elapsed;
        Task<byte[]?> call;
        try
        {
            var clock = Stopwatch.StartNew();
            call = channel.RequestAsync(Initialize, Request(1), CancellationToken.None);
            stopped = ((IAsyncResult)call).AsyncWaitHandle.WaitOne(deadline + CraftedCorpus.DeadlineMargin);
            elapsed = clock.Elapsed;
        }
        finally
        {
            release.Set();
        }

        await Assert.That(stopped).IsTrue();
        await Assert.That(elapsed).IsLessThanOrEqualTo(deadline + CraftedCorpus.DeadlineMargin);
        var thrown = await Assert.That(async () => await call).Throws<WasmChannelException>();
        await Assert.That(thrown!.Outcome).IsEqualTo("deadline");
    }

    [Test]
    public async Task A_channel_whose_store_cannot_open_returns_the_failure()
    {
        var opening = WasmChannel.Open(Admit("initial-memory-4097"), SessionLimits.Default);

        var failure = opening.Match<GuestFailure?>(_ => null, failed => failed);

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo("extension.memory_limit");
    }
}
