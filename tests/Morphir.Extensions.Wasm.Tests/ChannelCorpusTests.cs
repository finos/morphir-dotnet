using System.Text.Json;
using Morphir.Mep;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 3.1 of add-wasm-extension-host: the <c>extism/</c> and <c>limits/</c> cases of the pinned crafted corpus,
/// run in-process through <see cref="WasmChannel"/>. Each run's frames are passed as raw bytes, a request through
/// <c>RequestAsync</c> and a notification through <c>NotifyAsync</c>, with the case's <c>worker</c> frame limits.
/// </summary>
/// <remarks>
/// <para>
/// What a worker shows and how this check sees it in-process:
/// </para>
/// <list type="bullet">
/// <item>A stdout frame is the reply bytes of a request. The worker writes no frame for a notification.</item>
/// <item>A <c>fault</c> of kind <c>failure</c> is one of three things: the channel throws
/// <see cref="WasmChannelException"/> with that fault kind and the contract's code; the channel returns no reply
/// for a request and records <see cref="WasmChannelFailure.NoOutput"/>; or the reply is not one JSON-RPC message,
/// which the worker's message check (task 4.1) and the host session (<c>JsonRpc.Read</c>, its
/// <c>InvalidReply</c>) both refuse. A refused reply reaches no stdout, so it counts toward no frame.</item>
/// <item>Exit status 0 is the instance ending at <c>morphir.exit</c> with no failure.</item>
/// </list>
/// <para>
/// What remains for task 4.1, the worker: the <c>started</c> event and its <c>bounds</c>, the <c>fault</c> and
/// <c>exited</c> events themselves, the exit status as a number, <c>stdoutBytes</c> as a count of framed bytes,
/// and the worker's own message check of a reply.
/// </para>
/// </remarks>
public class ChannelCorpusTests
{
    // Counted in spec/mep/mck/wasm-host.cases.json at finos/morphir 7b049c223 (MepWasmFixtures.PinnedCommit).
    // Change them only when the pin moves.
    private const int PinnedExtismCaseCount = 8;
    private const int PinnedLimitsCaseCount = 2;

    /// <summary>
    /// The cases that need a guest that receives notifications: task 3.2 (design NTFY-1). Each has a guest that
    /// advertises acceptance, and its expectation holds only for a host that delivers. Task 3.1 delivers no
    /// notification to any guest.
    /// </summary>
    private static readonly IReadOnlySet<string> DeliveryCases = new HashSet<string>(StringComparer.Ordinal)
    {
        "extism/notifications-accepted",
        "extism/notification-answered",
    };

    private static IEnumerable<CraftedCase> ChannelCases() =>
        CraftedCorpus.Cases.Where(@case =>
            @case.CaseId.StartsWith("extism/", StringComparison.Ordinal) || @case.CaseId.StartsWith("limits/", StringComparison.Ordinal));

    public static IEnumerable<string> CheckedCaseIds() =>
        ChannelCases().Select(@case => @case.CaseId).Where(id => !DeliveryCases.Contains(id));

    public static IEnumerable<string> DeliveryCaseIds() => DeliveryCases.Order(StringComparer.Ordinal);

    [Test]
    public async Task Every_extism_and_limits_case_is_checked_here_or_waits_for_delivery()
    {
        var ids = ChannelCases().Select(@case => @case.CaseId).ToList();

        await Assert.That(ids.Count(id => id.StartsWith("extism/", StringComparison.Ordinal))).IsEqualTo(PinnedExtismCaseCount);
        await Assert.That(ids.Count(id => id.StartsWith("limits/", StringComparison.Ordinal))).IsEqualTo(PinnedLimitsCaseCount);
        await Assert.That(DeliveryCases.All(ids.Contains)).IsTrue();
        await Assert.That(CheckedCaseIds().Count() + DeliveryCases.Count).IsEqualTo(ids.Count);
    }

    /// <summary>What one run gave through the channel.</summary>
    private sealed record RunObservation(
        IReadOnlyList<byte[]> StdoutFrames,
        WasmChannelException? Thrown,
        bool RefusedReply,
        int HandleCalls,
        int RequestsSent,
        WasmChannelFailure? Failure,
        bool InstanceEnded);

    private static ExtismModule Admit(CraftedCase @case) =>
        WasmEngine.Shared.Load(File.ReadAllBytes(@case.WasmPath)).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.TryParse(@case.DeclaredAbi, out var declared) ? declared : null).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));

    private static SessionLimits LimitsFor(CraftedCase @case)
    {
        var limits = SessionLimits.Default;
        if (@case.WorkerRequestBytes is { } request)
        {
            limits = limits with { RequestFrameBytes = request };
        }

        if (@case.WorkerResponseBytes is { } response)
        {
            limits = limits with { ResponseFrameBytes = response };
        }

        return limits;
    }

    private static async Task<RunObservation> RunAsync(CraftedCase @case, IReadOnlyList<string> frames)
    {
        using var channel = WasmChannel.Open(Admit(@case), LimitsFor(@case)).Match(
            opened => opened,
            failed => throw new InvalidOperationException($"{@case.CaseId}: the channel did not open: {failed.Message}"));
        var stdout = new List<byte[]>();
        WasmChannelException? thrown = null;
        var refused = false;
        var requests = 0;
        foreach (var frame in frames)
        {
            var method = CraftedCorpus.FrameMethod(frame);
            var body = CraftedCorpus.FrameBody(frame);
            try
            {
                if (CraftedCorpus.IsRequest(frame))
                {
                    requests++;
                    var reply = await channel.RequestAsync(method, body, CancellationToken.None);
                    if (reply is null)
                    {
                        break;
                    }

                    if (JsonRpc.Read(reply).Match(_ => false, _ => true))
                    {
                        refused = true;
                        break;
                    }

                    stdout.Add(reply);
                }
                else
                {
                    var reply = await channel.NotifyAsync(method, body, CancellationToken.None);
                    if (reply is not null)
                    {
                        refused = true;
                        break;
                    }
                }
            }
            catch (WasmChannelException failure)
            {
                thrown = failure;
                break;
            }
        }

        return new RunObservation(stdout, thrown, refused, channel.HandleCalls, requests, channel.Failure, channel.InstanceEnded);
    }

    private static string Canonical(JsonElement json) => JsonSerializer.Serialize(json);

    private static string Canonical(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    [Test]
    [MethodDataSource(nameof(CheckedCaseIds))]
    public async Task Extism_and_limits_case_gives_its_expected_observation_through_the_channel(string caseId)
    {
        var @case = CraftedCorpus.Case(caseId);
        await Assert.That(MepWasmFixtures.Sha256(@case.WasmPath)).IsEqualTo(@case.Sha256);
        await Assert.That(@case.AppliesTo).IsEqualTo(WasmAbi.ExtismKernel.Id);

        var expectations = @case.ExpectationsFor(new HashSet<string>(StringComparer.Ordinal) { WasmAbi.ExtismKernel.Id });
        var runs = @case.RunFrames;
        for (var run = 0; run < runs.Count; run++)
        {
            var expectation = expectations[run];
            var frames = runs[run];
            await Assert.That(expectation.ExpectsStarted).IsTrue();
            await Assert.That(expectation.RefusedCode).IsNull();

            var observed = await RunAsync(@case, frames);

            // A notification is never a handle call: the guest advertises nothing in task 3.1.
            await Assert.That(observed.HandleCalls).IsLessThanOrEqualTo(observed.RequestsSent);
            if (expectation.ExpectsFault)
            {
                var failed = observed.Thrown is not null || observed.RefusedReply || observed.Failure is WasmChannelFailure.NoOutput;
                await Assert.That(failed).IsTrue();
                if (observed.Thrown is { } thrown)
                {
                    await Assert.That(thrown.Failure.FaultKind).IsEqualTo(expectation.FaultKind);
                    await Assert.That(thrown.Failure).IsEqualTo(observed.Failure);
                    await Assert.That(observed.InstanceEnded).IsTrue();
                }

                if (observed.Failure is { } failure)
                {
                    var code = failure is WasmChannelFailure.RequestOverLimit
                        ? WasmProfileContract.Pinned.RequestLimitCode
                        : WasmProfileContract.Pinned.Fault(expectation.FaultKind!).HostCode;
                    await Assert.That(failure.Code).IsEqualTo(code);
                    await Assert.That(failure.FaultKind).IsEqualTo(expectation.FaultKind);
                }

                if (expectation.StdoutBytes == 0)
                {
                    await Assert.That(observed.StdoutFrames.Count).IsEqualTo(0);
                }
            }
            else
            {
                await Assert.That(observed.Thrown).IsNull();
                await Assert.That(observed.RefusedReply).IsFalse();
                await Assert.That(observed.Failure).IsNull();
                await Assert.That(expectation.NoEvents).Contains("fault");
            }

            if (expectation.ExitCode == 0)
            {
                // Served until the instance ended: morphir.exit was the last frame and ended it without a failure.
                await Assert.That(CraftedCorpus.FrameMethod(frames[^1])).IsEqualTo("morphir.exit");
                await Assert.That(observed.InstanceEnded).IsTrue();
                await Assert.That(observed.Failure).IsNull();
            }

            var matchers = @case.StdoutFrameMatchers[run];
            await Assert.That(observed.StdoutFrames.Count).IsEqualTo(matchers.Count);
            for (var index = 0; index < matchers.Count; index++)
            {
                var expected = matchers[index].TryGetProperty("sameAsFrame", out var same)
                    ? Canonical(CraftedCorpus.FrameBody(frames[same.GetInt32()]))
                    : Canonical(matchers[index].GetProperty("json"));
                await Assert.That(Canonical(observed.StdoutFrames[index])).IsEqualTo(expected);
            }
        }
    }

    /// <summary>
    /// The cases of a guest that advertises acceptance wait for task 3.2. Until then the channel withholds every
    /// notification, so the first reply is the guest's own <c>morphir.initialize</c> result and no notification
    /// reaches the guest. The rest of each expectation (a delivered <c>morphir.initialized</c>, a failure for an
    /// answered notification) is task 3.2's to meet.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(DeliveryCaseIds))]
    public async Task Delivery_case_waits_for_task_3_2_and_receives_no_notification(string caseId)
    {
        var @case = CraftedCorpus.Case(caseId);
        var frames = @case.RunFrames[0];

        var observed = await RunAsync(@case, frames);

        await Assert.That(observed.Thrown).IsNull();
        await Assert.That(observed.HandleCalls).IsEqualTo(observed.RequestsSent);
        var first = @case.StdoutFrameMatchers[0][0].GetProperty("json");
        await Assert.That(Canonical(observed.StdoutFrames[0])).IsEqualTo(Canonical(first));
    }
}
