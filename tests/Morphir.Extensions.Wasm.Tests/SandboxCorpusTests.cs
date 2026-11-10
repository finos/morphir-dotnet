using System.Diagnostics;
using System.Text.Json;
using Morphir.Mep;
using Morphir.TestSupport;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Tasks 3.3 and 3.4 of add-wasm-extension-host: every <c>sandbox/</c> case of the pinned crafted corpus, run
/// in-process through <see cref="WasmChannel"/>, each run in a new channel, as a worker runs each in a new process.
/// </summary>
/// <remarks>
/// <para>What a worker shows and how this check sees it in-process:</para>
/// <list type="bullet">
/// <item>A stdout frame is the reply of a request; <c>stdoutBytes</c> 0 is no reply.</item>
/// <item>A <c>fault</c> of kind <c>failure</c> is a <see cref="WasmChannelException"/> with that fault kind and the
/// contract's code <c>extension.incomplete_session</c>, and an ended instance.</item>
/// <item>Exit status 0 is the instance ending at <c>morphir.exit</c> with no failure; 1 is a failure.</item>
/// <item>The text of the <c>output</c> events is the channel's <see cref="WasmChannel.GuestOutput"/>: the text of
/// each output, joined in order.</item>
/// <item>A <c>withinSeconds</c> range until exit is the time of the whole run.</item>
/// </list>
/// <para>
/// What remains for task 4.1, the worker: the <c>started</c> event with its <c>bounds</c>, the <c>fault</c>,
/// <c>output</c> and <c>exited</c> events as JSON lines (the 4 KiB cut of each message and the
/// <c>droppedBytes</c> event), the exit status as a number, <c>stdoutBytes</c> as a count of framed bytes, the
/// worker's message check, and timing from the write of the last frame.
/// </para>
/// </remarks>
public class SandboxCorpusTests
{
    // Counted in spec/mep/mck/wasm-host.cases.json at finos/morphir 48d10706f (MepWasmFixtures.PinnedCommit).
    // Change it only when the pin moves.
    private const int PinnedSandboxCaseCount = 16;

    public static IEnumerable<string> SandboxCaseIds() =>
        CraftedCorpus.Cases.Where(@case => @case.CaseId.StartsWith("sandbox/", StringComparison.Ordinal)).Select(@case => @case.CaseId);

    [Test]
    public async Task Every_sandbox_case_of_the_pin_is_checked()
    {
        await Assert.That(SandboxCaseIds().Count()).IsEqualTo(PinnedSandboxCaseCount);
    }

    private sealed record RunObservation(
        IReadOnlyList<byte[]> Replies,
        WasmChannelException? Thrown,
        WasmChannelFailure? Failure,
        bool InstanceEnded,
        string GuestOutput,
        string? WorkspaceDirectory,
        TimeSpan Elapsed);

    private static ExtismModule Admit(CraftedCase @case) =>
        WasmEngine.Shared.Load(File.ReadAllBytes(@case.WasmPath)).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.TryParse(@case.DeclaredAbi, out var declared) ? declared : null).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));

    private static async Task<RunObservation> RunAsync(CraftedCase @case, IReadOnlyList<string> frames, string workspaceRoot)
    {
        var clock = Stopwatch.StartNew();
        using var channel = WasmChannel.Open(Admit(@case), SessionLimits.Default, workspaceRoot).Match(
            opened => opened,
            failed => throw new InvalidOperationException($"{@case.CaseId}: the channel did not open: {failed.Message}"));
        var replies = new List<byte[]>();
        WasmChannelException? thrown = null;
        foreach (var frame in frames)
        {
            var method = CraftedCorpus.FrameMethod(frame);
            var body = CraftedCorpus.FrameBody(frame);
            try
            {
                var reply = CraftedCorpus.IsRequest(frame)
                    ? await channel.RequestAsync(method, body, CancellationToken.None)
                    : await channel.NotifyAsync(method, body, CancellationToken.None);
                if (reply is null)
                {
                    if (CraftedCorpus.IsRequest(frame))
                    {
                        break;
                    }

                    continue;
                }

                replies.Add(reply);
            }
            catch (WasmChannelException failure)
            {
                thrown = failure;
                break;
            }
        }

        var workspace = channel.WorkspaceDirectory;
        var output = string.Concat(channel.GuestOutput.Outputs.Select(item => item.Text));
        return new RunObservation(replies, thrown, channel.Failure, channel.InstanceEnded, output, workspace, clock.Elapsed);
    }

    private static string Canonical(JsonElement json) => JsonSerializer.Serialize(json);

    private static string Canonical(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    /// <summary>The value at an RFC 6901 pointer of object members, or null when a member is missing.</summary>
    private static JsonElement? At(JsonElement root, string pointer)
    {
        var current = root;
        foreach (var token in pointer.Split('/').Skip(1).Select(part => part.Replace("~1", "/").Replace("~0", "~")))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(token, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static string JsonType(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "undefined",
    };

    private static IReadOnlyList<string> Strings(JsonElement expect, string member) =>
        expect.TryGetProperty(member, out var values) ? values.EnumerateArray().Select(value => value.GetString()!).ToList() : [];

    [Test]
    [MethodDataSource(nameof(SandboxCaseIds))]
    public async Task Sandbox_case_gives_its_expected_observation_through_the_channel(string caseId)
    {
        var @case = CraftedCorpus.Case(caseId);
        await Assert.That(MepWasmFixtures.Sha256(@case.WasmPath)).IsEqualTo(@case.Sha256);
        await Assert.That(@case.AppliesTo).IsEqualTo(WasmAbi.ExtismKernel.Id);
        using var sandbox = TestSandbox.Create("morphir-wasm-sandbox-");

        var expectations = @case.ExpectationsFor(new HashSet<string>(StringComparer.Ordinal) { WasmAbi.ExtismKernel.Id });
        var runs = @case.RunFrames;
        for (var run = 0; run < runs.Count; run++)
        {
            var frames = runs[run];
            var observed = await RunAsync(@case, frames, sandbox.Work);
            await CaseExpectation.SatisfyOneAsync(expectations[run], expectation => ExpectObservation(observed, frames, expectation));
        }

        await Assert.That(Directory.EnumerateFileSystemEntries(sandbox.Work).Any()).IsFalse();
    }

    /// <summary>The assertions of one expectation against one run's observation.</summary>
    private static async Task ExpectObservation(RunObservation observed, IReadOnlyList<string> frames, CaseExpectation expectation)
    {
        var expect = expectation.Expect;
        await Assert.That(expectation.ExpectsStarted).IsTrue();
        await Assert.That(expectation.StartedAbi).IsEqualTo(WasmAbi.ExtismKernel.Id);
        if (expectation.ExpectsFault)
        {
            await Assert.That(observed.Thrown).IsNotNull();
            await Assert.That(observed.Thrown!.Failure.FaultKind).IsEqualTo(expectation.FaultKind);
            await Assert.That(observed.Thrown.Failure.Code).IsEqualTo(WasmProfileContract.Pinned.Fault(expectation.FaultKind!).HostCode);
            await Assert.That(observed.Failure).IsEqualTo(observed.Thrown.Failure);
            await Assert.That(observed.InstanceEnded).IsTrue();
            await Assert.That(expectation.ExitCode).IsEqualTo(1);
        }
        else
        {
            await Assert.That(observed.Thrown).IsNull();
            await Assert.That(observed.Failure).IsNull();
            await Assert.That(expectation.NoEvents).Contains("fault");
        }

        if (expectation.ExitCode == 0)
        {
            await Assert.That(CraftedCorpus.FrameMethod(frames[^1])).IsEqualTo("morphir.exit");
            await Assert.That(observed.InstanceEnded).IsTrue();
        }

        if (expectation.StdoutBytes == 0)
        {
            await Assert.That(observed.Replies.Count).IsEqualTo(0);
        }

        var matchers = expectation.StdoutFrames;
        if (expectation.StdoutBytes != 0)
        {
            await Assert.That(observed.Replies.Count).IsEqualTo(matchers.Count);
        }

        for (var index = 0; index < matchers.Count; index++)
        {
            var matcher = matchers[index];
            await Assert.That(JsonRpc.Read(observed.Replies[index]).Match(_ => true, _ => false)).IsTrue();
            if (matcher.TryGetProperty("json", out var json))
            {
                await Assert.That(Canonical(observed.Replies[index])).IsEqualTo(Canonical(json));
                continue;
            }

            using var reply = JsonDocument.Parse(observed.Replies[index]);
            foreach (var entry in matcher.GetProperty("pointers").EnumerateArray())
            {
                var pointer = entry.GetProperty("pointer").GetString()!;
                var value = At(reply.RootElement, pointer);
                await Assert.That(value).IsNotNull();
                if (entry.TryGetProperty("equals", out var equals))
                {
                    await Assert.That(Canonical(value!.Value)).IsEqualTo(Canonical(equals));
                }

                if (entry.TryGetProperty("type", out var type))
                {
                    await Assert.That(JsonType(value!.Value.ValueKind)).IsEqualTo(type.GetString());
                }
            }
        }

        var position = 0;
        foreach (var text in Strings(expect, "guestOutput"))
        {
            var found = observed.GuestOutput.IndexOf(text, position, StringComparison.Ordinal);
            await Assert.That(found).IsGreaterThanOrEqualTo(0);
            position = found + text.Length;
        }

        foreach (var text in Strings(expect, "guestOutputIncludes"))
        {
            await Assert.That(observed.GuestOutput).Contains(text);
        }

        foreach (var text in Strings(expect, "noGuestOutput"))
        {
            await Assert.That(observed.GuestOutput).DoesNotContain(text);
        }

        // Guest output never reaches a reply.
        foreach (var reply in observed.Replies)
        {
            foreach (var text in Strings(expect, "guestOutput").Concat(Strings(expect, "guestOutputIncludes")))
            {
                await Assert.That(System.Text.Encoding.UTF8.GetString(reply)).DoesNotContain(text);
            }
        }

        if (expectation.WithinSeconds is { Until: "exit" } within)
        {
            await Assert.That(observed.Elapsed.TotalSeconds).IsGreaterThanOrEqualTo(within.Low);
            await Assert.That(observed.Elapsed.TotalSeconds).IsLessThanOrEqualTo(within.High);
        }

        // A workspace the guest asked for is gone with the session.
        if (observed.WorkspaceDirectory is { } workspace)
        {
            await Assert.That(Directory.Exists(workspace)).IsFalse();
        }
    }
}
