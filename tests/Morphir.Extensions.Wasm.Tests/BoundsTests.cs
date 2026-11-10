using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 2.5 of add-wasm-extension-host (parent decisions BND-1 and BND-2): the instruction budget, the call
/// deadline, cancellation and the memory ceiling, each mapped to the contract's failure, with the memory sizes
/// on every failure. The crafted <c>bounds/</c> cases are run in-process by calling <c>handle</c> with the
/// case's request frames as raw bytes; no MEP session exists at this level.
/// </summary>
public class BoundsTests
{
    // Counted in spec/mep/mck/wasm-host.cases.json at finos/morphir 7b049c223 (MepWasmFixtures.PinnedCommit).
    // Change them only when the pin moves.
    private const int PinnedBoundsCaseCount = 8;
    private const int PinnedLimitsCaseCount = 2;

    private const string BoundsGroup = "bounds/";
    private const string LimitsGroup = "limits/";
    private const long MiB = 1024 * 1024;
    private const long Page = 65536;

    /// <summary>The bounds this host enforces: the store's memory limit, fuel and the epoch deadline.</summary>
    private static readonly IReadOnlySet<string> EnforcedBounds =
        new HashSet<string>(StringComparer.Ordinal) { "memoryCeiling", "instructionBudget", "callDeadline" };

    private static IReadOnlySet<string> HostProfiles =>
        WasmHost.Profiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);

    public static IEnumerable<string> BoundsCaseIds() =>
        CraftedCorpus.Cases
            .Where(@case => @case.CaseId.StartsWith(BoundsGroup, StringComparison.Ordinal))
            .Select(@case => @case.CaseId);

    private static ExtismModule Admit(string module) =>
        WasmEngine.Shared.Load(CraftedCorpus.ModuleBytes(module)).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));

    private static SessionStore Open(string module, SessionLimits limits) =>
        SessionStore.Open(Admit(module), limits).Match(
            session => session,
            failure => throw new InvalidOperationException($"The session did not open: {failure.Message}"));

    private static GuestFailure Failed(HandleOutcome outcome) =>
        outcome.Match(
            completed => throw new InvalidOperationException($"The call completed with return code {completed.Result.ReturnCode}."),
            failed => failed,
            notRun => throw new InvalidOperationException($"The call did not run: {notRun.Message}"));

    private static byte[] Initialize => CraftedCorpus.FrameBody("initialize");

    [Test]
    public async Task The_corpus_has_every_pinned_bounds_and_limits_case()
    {
        await Assert.That(BoundsCaseIds().Count()).IsEqualTo(PinnedBoundsCaseCount);
        // The limits/ cases are the frame limits; ChannelCorpusTests runs them through the channel (task 3.1).
        await Assert.That(CraftedCorpus.Cases.Count(@case => @case.CaseId.StartsWith(LimitsGroup, StringComparison.Ordinal)))
            .IsEqualTo(PinnedLimitsCaseCount);
    }

    /// <summary>
    /// Each <c>bounds/</c> case gives its expected observation in-process, under the contract's default bounds. A
    /// case for <c>morphir-bytes-v1</c> is <c>not_applicable</c> to this host; its in-process equivalent is that
    /// this host refuses the module naming the profile. For the others the session opens (the <c>started</c>
    /// event) or fails before any request (a fault with no <c>started</c>), each request frame is one
    /// <c>handle</c> call, the first failure has the expected fault kind within the expected time, and a run
    /// without a fault answers each request with the frame it names. Notifications, stdout bytes and exit
    /// statuses belong to the session and the worker (tasks 3.1, 3.2 and 4.1).
    /// </summary>
    [Test]
    [MethodDataSource(nameof(BoundsCaseIds))]
    public async Task Bounds_case_gives_its_expected_observation(string caseId)
    {
        var @case = CraftedCorpus.Case(caseId);
        await Assert.That(MepWasmFixtures.Sha256(@case.WasmPath)).IsEqualTo(@case.Sha256);
        await Assert.That(@case.Bounds.All(EnforcedBounds.Contains)).IsTrue();

        var bytes = await File.ReadAllBytesAsync(@case.WasmPath);
        if (!HostProfiles.Contains(@case.AppliesTo))
        {
            var refused = HostOutcome.Admit(bytes, @case.DeclaredAbi);
            await Assert.That(refused.RefusalCode).IsEqualTo("extension.abi_unsupported");
            await Assert.That(refused.Message!).Contains(@case.AppliesTo);
            return;
        }

        var module = WasmEngine.Shared.Load(bytes).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.TryParse(@case.DeclaredAbi, out var declared) ? declared : null).Match(
                admitted => admitted,
                refusal => throw new InvalidOperationException(refusal.Message)),
            refusal => throw new InvalidOperationException(refusal.Message),
            notWasm => throw new InvalidOperationException(notWasm));

        var expectations = @case.ExpectationsFor(HostProfiles, EnforcedBounds);
        var runs = @case.RunFrames;
        for (var run = 0; run < runs.Count; run++)
        {
            var expectation = expectations[run];
            await Assert.That(expectation.RefusedCode).IsNull();
            var opening = SessionStore.Open(module, SessionLimits.Default);
            if (!expectation.ExpectsStarted)
            {
                // A fault before any request: no started event, so the session does not open.
                await Assert.That(expectation.NoEvents).Contains("started");
                var failure = opening.Match<GuestFailure?>(_ => null, failed => failed);
                await Assert.That(failure).IsNotNull();
                await Assert.That(failure!.FaultKind).IsEqualTo(expectation.FaultKind);
                continue;
            }

            using var session = opening.Match(
                opened => opened,
                failed => throw new InvalidOperationException($"{caseId}: the session did not open: {failed.Message}"));
            var frames = runs[run];
            var answers = new List<(int Frame, byte[] Output)>();
            GuestFailure? first = null;
            var clock = Stopwatch.StartNew();
            for (var index = 0; index < frames.Count && first is null; index++)
            {
                if (!CraftedCorpus.IsRequest(frames[index]))
                {
                    continue;
                }

                clock.Restart();
                var outcome = session.CallHandle(CraftedCorpus.FrameBody(frames[index]));
                clock.Stop();
                outcome.Match<object?>(
                    completed => { answers.Add((index, completed.Result.Output)); return null; },
                    failed => first = failed,
                    notRun => throw new InvalidOperationException($"{caseId}: a call did not run: {notRun.Message}"));
            }

            if (expectation.ExpectsFault)
            {
                await Assert.That(first).IsNotNull();
                if (expectation.FaultKind is { } kind)
                {
                    await Assert.That(first!.FaultKind).IsEqualTo(kind);
                }

                if (expectation.WithinSeconds is { Until: "fault" } within)
                {
                    await Assert.That(clock.Elapsed.TotalSeconds).IsGreaterThanOrEqualTo(within.Low);
                    await Assert.That(clock.Elapsed.TotalSeconds).IsLessThanOrEqualTo(within.High);
                }
            }
            else
            {
                await Assert.That(first).IsNull();
                for (var frame = 0; frame < expectation.SameAsFrames.Count; frame++)
                {
                    var expected = CraftedCorpus.FrameBody(frames[expectation.SameAsFrames[frame]]);
                    await Assert.That(JsonElementText(answers[frame].Output)).IsEqualTo(JsonElementText(expected));
                }
            }
        }
    }

    private static string JsonElementText(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    [Test]
    public async Task A_spinning_guest_ends_with_the_budget_failure()
    {
        using var session = Open("extism-spin", SessionLimits.Default with { InstructionBudget = 10_000_000 });

        var failure = Failed(session.CallHandle(Initialize));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.BudgetExhausted);
        await Assert.That(failure.Code).IsEqualTo("extension.budget_exhausted");
        await Assert.That(failure.FaultKind).IsEqualTo("budget-exhausted");
        await Assert.That(failure.Outcome).IsNull();
        await Assert.That(failure.FuelUsed).IsEqualTo(10_000_000UL);
    }

    [Test]
    public async Task A_spinning_guest_with_a_large_budget_ends_at_its_deadline_within_the_margin()
    {
        var deadline = TimeSpan.FromMilliseconds(500);
        using var session = Open("extism-spin", SessionLimits.Default with { InstructionBudget = ulong.MaxValue, CallDeadline = deadline });

        var clock = Stopwatch.StartNew();
        var failure = Failed(session.CallHandle(Initialize));
        clock.Stop();

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.CallDeadline);
        await Assert.That(failure.Outcome).IsEqualTo("deadline");
        await Assert.That(failure.FaultKind).IsEqualTo("call-deadline");
        await Assert.That(failure.Code).IsNull();
        await Assert.That(clock.Elapsed).IsGreaterThanOrEqualTo(deadline);
        await Assert.That(clock.Elapsed).IsLessThanOrEqualTo(deadline + CraftedCorpus.DeadlineMargin);
    }

    [Test]
    [NotInParallel] // It keeps every processor busy, which would delay the timing of any test beside it.
    public async Task The_deadline_holds_while_the_thread_pool_is_busy()
    {
        // A host whose thread pool is blocked must still stop the guest: the epoch may not wait for a pool thread.
        // On a loaded CI runner a pool-driven epoch let a 500 ms deadline pass by seconds.
        var deadline = TimeSpan.FromMilliseconds(300);
        using var session = Open("extism-spin", SessionLimits.Default with { InstructionBudget = ulong.MaxValue, CallDeadline = deadline });
        ThreadPool.GetMinThreads(out var workers, out _);
        using var release = new ManualResetEventSlim();
        // Every pool thread burns a processor, as it does when other calls run WASM. The pool adds no thread while
        // the processors are saturated, so work queued to it, such as a timer callback, waits. The loops stop after
        // five seconds at the latest, so a regression fails this test and does not hang it.
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

        GuestFailure failure;
        var clock = Stopwatch.StartNew();
        try
        {
            failure = Failed(session.CallHandle(Initialize));
            clock.Stop();
        }
        finally
        {
            release.Set();
        }

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.CallDeadline);
        await Assert.That(clock.Elapsed).IsLessThanOrEqualTo(deadline + CraftedCorpus.DeadlineMargin);
    }

    [Test]
    public async Task Cancellation_stops_a_spinning_guest_within_one_second()
    {
        // The contract's deadline and an unlimited budget: only cancellation can stop the call in time.
        using var session = Open("extism-spin", SessionLimits.Default with { InstructionBudget = ulong.MaxValue });
        using var cancellation = new CancellationTokenSource();
        var cancelledAt = TimeSpan.Zero;
        var clock = Stopwatch.StartNew();
        using var timer = new Timer(_ => { cancelledAt = clock.Elapsed; cancellation.Cancel(); }, null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);

        var failure = Failed(session.CallHandle(Initialize, cancellation.Token));
        var stoppedAt = clock.Elapsed;

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.Cancelled);
        await Assert.That(failure.Outcome).IsEqualTo("cancelled");
        await Assert.That(failure.Code).IsNull();
        await Assert.That(failure.FaultKind).IsNull();
        await Assert.That(cancelledAt).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(stoppedAt - cancelledAt).IsLessThan(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task A_call_with_a_cancelled_token_does_not_run_the_guest()
    {
        using var session = Open("extism-echo", SessionLimits.Default);

        var failure = Failed(session.CallHandle(Initialize, new CancellationToken(canceled: true)));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.Cancelled);
        await Assert.That(failure.FuelUsed).IsEqualTo(0UL);
    }

    [Test]
    public async Task A_guest_that_grows_to_the_ceiling_and_fails_gives_the_memory_limit_with_both_sizes()
    {
        using var session = Open("extism-grow-then-trap", SessionLimits.Default);

        var failure = Failed(session.CallHandle(Initialize));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.MemoryLimit);
        await Assert.That(failure.Signal).IsEqualTo(GuestFailureKind.GuestTrap);
        await Assert.That(failure.Code).IsEqualTo("extension.memory_limit");
        await Assert.That(failure.FaultKind).IsEqualTo("memory-limit");
        await Assert.That(failure.Memory.GuestBytes).IsEqualTo(256 * MiB);
        await Assert.That(failure.Memory.KernelBytes).IsEqualTo(1 * MiB);
        await Assert.That(failure.Memory.CeilingBytes).IsEqualTo(256 * MiB);
        await Assert.That(failure.Memory.GuestAtCeiling).IsTrue();
    }

    [Test]
    public async Task A_memory_that_cannot_grow_by_one_more_page_is_at_the_ceiling()
    {
        // A ceiling that is not a whole number of pages: the guest stops at 20 pages, 1000 bytes short of it.
        var ceiling = 20 * Page + 1000;
        using var session = Open("extism-grow-then-trap", SessionLimits.Default with { MemoryCeilingBytes = ceiling });

        var failure = Failed(session.CallHandle(Initialize));

        await Assert.That(failure.Memory.GuestBytes).IsEqualTo(20 * Page);
        await Assert.That(failure.Memory.CeilingBytes).IsEqualTo(ceiling);
        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.MemoryLimit);
    }

    [Test]
    public async Task A_guest_that_fails_under_the_ceiling_keeps_its_own_failure_and_reports_its_size()
    {
        using var session = Open("extism-grow-below-ceiling", SessionLimits.Default);

        var failure = Failed(session.CallHandle(Initialize));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.GuestTrap);
        await Assert.That(failure.Code).IsEqualTo("extension.guest_trap");
        await Assert.That(failure.FaultKind).IsEqualTo("trap");
        await Assert.That(failure.Memory.GuestBytes).IsEqualTo(100 * MiB);
        await Assert.That(failure.Memory.CeilingBytes).IsEqualTo(256 * MiB);
        await Assert.That(failure.Memory.GuestAtCeiling).IsFalse();
        await Assert.That(failure.Message).Contains("100 MiB");
        await Assert.That(failure.Message).Contains("256 MiB");
    }

    [Test]
    public async Task A_guest_that_recovers_from_a_refused_growth_completes()
    {
        using var session = Open("extism-grow-then-respond", SessionLimits.Default);

        var outcome = session.CallHandle(Initialize);

        var output = outcome.Match(completed => completed.Result.Output, failed => throw new InvalidOperationException(failed.Message), notRun => throw new InvalidOperationException(notRun.Message));
        await Assert.That(Encoding.UTF8.GetString(output)).IsEqualTo(Encoding.UTF8.GetString(Initialize));
        await Assert.That(outcome.Match<long?>(completed => completed.Memory.GuestBytes, _ => null, _ => null)).IsEqualTo(Page);
    }

    [Test]
    public async Task A_module_with_an_initial_memory_above_the_ceiling_fails_before_any_request()
    {
        var opening = SessionStore.Open(Admit("initial-memory-4097"), SessionLimits.Default);

        var failure = opening.Match<GuestFailure?>(_ => null, failed => failed);

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Kind).IsEqualTo(GuestFailureKind.MemoryLimit);
        await Assert.That(failure.Code).IsEqualTo("extension.memory_limit");
        await Assert.That(failure.Memory.GuestBytes).IsEqualTo(4097 * Page);
        await Assert.That(failure.Memory.CeilingBytes).IsEqualTo(256 * MiB);
        await Assert.That(failure.FuelUsed).IsEqualTo(0UL);
    }

    [Test]
    public async Task A_trap_under_the_ceiling_is_a_guest_trap()
    {
        using var session = Open("extism-trap", SessionLimits.Default);

        var failure = Failed(session.CallHandle(Initialize));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.GuestTrap);
        await Assert.That(failure.Code).IsEqualTo("extension.guest_trap");
        await Assert.That(failure.Memory.GuestBytes).IsNull();
    }

    [Test]
    public async Task A_call_after_a_failed_call_does_not_run_the_guest()
    {
        using var session = Open("extism-echo", SessionLimits.Default);
        var first = Failed(session.CallHandle(Initialize, new CancellationToken(canceled: true)));

        var later = session.CallHandle(Initialize);

        var endedBy = later.Match<GuestFailure?>(_ => null, _ => null, notRun => notRun);
        await Assert.That(endedBy).IsNotNull();
        await Assert.That(ReferenceEquals(endedBy, first)).IsTrue();
        await Assert.That(ReferenceEquals(session.Failure, first)).IsTrue();
    }

    [Test]
    public async Task A_call_after_a_trap_does_not_run_the_guest()
    {
        using var session = Open("extism-trap", SessionLimits.Default);
        var first = Failed(session.CallHandle(Initialize));

        var later = session.CallHandle(Initialize);

        await Assert.That(later.Match<GuestFailure?>(_ => null, _ => null, notRun => notRun)).IsEqualTo(first);
    }

    [Test]
    public async Task A_host_function_that_stops_the_call_is_the_contract_failure()
    {
        // http_request is not provided yet (task 3.3); its callback stops the call, as the policy says it must.
        using var session = Open("extism-http", SessionLimits.Default);

        var failure = Failed(session.CallHandle(Initialize));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.IncompleteSession);
        await Assert.That(failure.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(failure.FaultKind).IsEqualTo("failure");
        await Assert.That(failure.Detail).Contains("extism:host/env http_request");
        await Assert.That(failure.Memory.GuestBytes).IsEqualTo(Page);
    }

    [Test]
    public async Task A_proc_exit_with_code_0_completes_the_call()
    {
        using var session = Open("extism-proc-exit", SessionLimits.Default);

        var completed = session.CallHandle(Initialize).Match(completed => completed, failed => throw new InvalidOperationException(failed.Message), notRun => throw new InvalidOperationException(notRun.Message));

        await Assert.That(Encoding.UTF8.GetString(completed.Result.Output)).IsEqualTo("""{"jsonrpc":"2.0","id":1,"result":"exited"}""");
    }

    public static IEnumerable<Func<(string Module, SessionLimits Limits, bool Cancelled, long? GuestBytes)>> FailingCalls()
    {
        var defaults = SessionLimits.Default;
        yield return () => ("extism-trap", defaults, false, null);
        yield return () => ("extism-spin", defaults with { InstructionBudget = 1_000_000 }, false, null);
        yield return () => ("extism-spin", defaults with { InstructionBudget = ulong.MaxValue, CallDeadline = TimeSpan.FromMilliseconds(50) }, false, null);
        yield return () => ("extism-echo", defaults, true, null);
        yield return () => ("extism-grow-then-trap", defaults with { MemoryCeilingBytes = 32 * Page }, false, 32 * Page);
        yield return () => ("extism-grow-below-ceiling", defaults, false, 100 * MiB);
    }

    [Test]
    [MethodDataSource(nameof(FailingCalls))]
    public async Task Every_failure_states_the_memory_sizes_and_the_ceiling(string module, SessionLimits limits, bool cancelled, long? guestBytes)
    {
        using var session = Open(module, limits);

        var failure = Failed(session.CallHandle(Initialize, new CancellationToken(cancelled)));

        await Assert.That(failure.Memory.CeilingBytes).IsEqualTo(limits.MemoryCeilingBytes);
        await Assert.That(failure.Memory.GuestBytes).IsEqualTo(guestBytes);
        await Assert.That(failure.Memory.KernelBytes).IsGreaterThan(0L);
        await Assert.That(failure.Message).Contains("ceiling");
        await Assert.That(failure.Message).Contains("kernel memory");
        await Assert.That(failure.Message).Contains(guestBytes is null ? "no memory of its own" : "guest memory");
    }

    [Test]
    public async Task A_completed_call_reports_its_fuel_and_memory()
    {
        using var session = Open("extism-echo", SessionLimits.Default);

        var completed = session.CallHandle(Initialize).Match(completed => completed, failed => throw new InvalidOperationException(failed.Message), notRun => throw new InvalidOperationException(notRun.Message));

        await Assert.That(completed.FuelUsed).IsGreaterThan(0UL);
        await Assert.That(completed.FuelUsed).IsLessThan(SessionLimits.Default.InstructionBudget);
        await Assert.That(completed.Memory.KernelBytes).IsEqualTo(1 * MiB);
        await Assert.That(completed.Memory.GuestBytes).IsNull();
    }

    [Test]
    public async Task The_one_instruction_budget_is_the_contract_budget_of_MEP_0_2_0_draft_1()
    {
        // Design MEP-1: the host speaks 0.2.0-draft.1 only, so it reads that budget and no other.
        using var contract = JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var budget = contract.RootElement.GetProperty("bounds").GetProperty("instructionBudget").GetProperty("0.2.0-draft.1").GetUInt64();

        await Assert.That(WasmProfileContract.SessionProtocol).IsEqualTo("0.2.0-draft.1");
        await Assert.That(WasmProfileContract.Pinned.InstructionBudget).IsEqualTo(budget);
        await Assert.That(SessionLimits.Default.InstructionBudget).IsEqualTo(budget);
        await Assert.That(SessionLimits.Default.InstructionBudget).IsEqualTo(5_000_000_000UL);
        await Assert.That(SessionLimits.Default.MemoryCeilingBytes).IsEqualTo(256 * MiB);
        await Assert.That(SessionLimits.Default.CallDeadline).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task No_budget_is_chosen_by_protocol_version()
    {
        // The MEP 0.1 budget of the contract has no reader: SessionLimits has no member that takes a version.
        var members = typeof(SessionLimits).GetMembers().Select(member => member.Name).ToList();

        await Assert.That(members).DoesNotContain("ForProtocol");
        await Assert.That(members).DoesNotContain("TryForProtocol");
        await Assert.That(typeof(WasmProfileContract).GetMethods().Select(method => method.Name)).DoesNotContain("TryGetInstructionBudget");
    }

    [Test]
    public async Task The_failure_codes_come_from_the_contract()
    {
        using var contract = JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var faults = contract.RootElement.GetProperty("faults").EnumerateArray().ToDictionary(
            fault => fault.GetProperty("kind").GetString()!,
            fault => (Code: fault.TryGetProperty("hostCode", out var code) ? code.GetString() : null,
                Outcome: fault.TryGetProperty("hostOutcome", out var outcome) ? outcome.GetString() : null));

        foreach (var kind in Enum.GetValues<GuestFailureKind>().Where(kind => kind != GuestFailureKind.Cancelled))
        {
            var failure = new GuestFailure(kind, kind, "detail", new MemoryUse(null, MiB, 256 * MiB), 0, TimeSpan.Zero);
            await Assert.That(faults).ContainsKey(failure.FaultKind!);
            await Assert.That(failure.Code).IsEqualTo(faults[failure.FaultKind!].Code);
            await Assert.That(failure.Outcome).IsEqualTo(faults[failure.FaultKind!].Outcome);
        }

        await Assert.That(faults.Count).IsEqualTo(Enum.GetValues<GuestFailureKind>().Length - 1);
    }
}
