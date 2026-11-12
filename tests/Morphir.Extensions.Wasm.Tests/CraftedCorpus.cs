using System.Text.Json;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// One expectation of a crafted case, reduced to what an in-process check can compare: the <c>refused</c>
/// event (code and message fragments), the <c>started</c> event (ABI), the <c>fault</c> event (kind) and the
/// frames on stdout, and the members that only the worker process can show.
/// </summary>
/// <param name="RefusedCode">The <c>error.code</c> of the expected <c>refused</c> event, or null when none is expected.</param>
/// <param name="MessageIncludes">The strings the refusal's message must contain.</param>
/// <param name="StartedAbi">The <c>abi</c> of the expected <c>started</c> event, or null when none is fixed.</param>
/// <param name="GuestRunMembers">The members of the expectation that only a running guest can show
/// (<c>stdoutFrames</c>, or an exit status after a served session).</param>
/// <param name="ExpectsStarted">Whether the events list a <c>started</c> event.</param>
/// <param name="ExpectsFault">Whether the events list a <c>fault</c> event.</param>
/// <param name="FaultKind">The <c>kind</c> of the expected <c>fault</c> event, or null when the kind is not constrained.</param>
/// <param name="NoEvents">The events that must not appear.</param>
/// <param name="ExitCode">The worker's expected exit status, or null when it is not constrained.</param>
/// <param name="StdoutBytes">The expected byte count of all of stdout, or null when it is not constrained.</param>
/// <param name="SameAsFrames">For each expected stdout frame that is <c>sameAsFrame</c>, the index of the run's frame it equals.</param>
/// <param name="WithinSeconds">The <c>withinSeconds</c> range and what it is measured until, or null.</param>
/// <param name="StdoutFrames">The expected stdout frames as written: each a <c>sameAsFrame</c>, <c>json</c> or
/// <c>pointers</c> matcher. Empty when the expectation lists none.</param>
/// <param name="Expect">The <c>expect</c> object itself, for the members this record does not reduce.</param>
internal sealed record CaseExpectation(
    string? RefusedCode,
    IReadOnlyList<string> MessageIncludes,
    string? StartedAbi,
    IReadOnlyList<string> GuestRunMembers,
    bool ExpectsStarted,
    bool ExpectsFault,
    string? FaultKind,
    IReadOnlyList<string> NoEvents,
    int? ExitCode,
    int? StdoutBytes,
    IReadOnlyList<int> SameAsFrames,
    (string Until, double Low, double High)? WithinSeconds,
    IReadOnlyList<JsonElement> StdoutFrames,
    JsonElement Expect)
{
    /// <summary>
    /// Passes when the observation satisfies one of <paramref name="alternatives"/>, as the corpus's
    /// <c>alternatives</c> convention says: <paramref name="check"/> is the assertions of one alternative against
    /// an observation made once. With one alternative its failure is the test's failure; with several, each is
    /// tried in turn and the test fails, naming every failure, when none is satisfied.
    /// </summary>
    public static async Task SatisfyOneAsync(IReadOnlyList<CaseExpectation> alternatives, Func<CaseExpectation, Task> check)
    {
        if (alternatives.Count == 1)
        {
            await check(alternatives[0]);
            return;
        }

        var failures = new List<string>();
        foreach (var alternative in alternatives)
        {
            try
            {
                await check(alternative);
                return;
            }
            catch (TUnit.Assertions.Exceptions.AssertionException failure)
            {
                failures.Add(failure.Message);
            }
        }

        Assert.Fail($"No alternative is satisfied:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }
}

/// <summary>A crafted case of the pinned corpus with the module it runs.</summary>
internal sealed record CraftedCase(
    string CaseId,
    string AppliesTo,
    string Module,
    string? DeclaredAbi,
    string WasmPath,
    string Sha256,
    JsonElement Runs)
{
    public override string ToString() => CaseId;

    /// <summary>The names of the bounds the case exercises (its <c>bounds</c> member), empty when it has none.</summary>
    public IReadOnlyList<string> Bounds { get; init; } = [];

    /// <summary>The case's <c>worker.requestBytes</c>, or null when the run uses the default.</summary>
    public long? WorkerRequestBytes { get; init; }

    /// <summary>The case's <c>worker.responseBytes</c>, or null when the run uses the default.</summary>
    public long? WorkerResponseBytes { get; init; }

    /// <summary>The case's <c>worker.instructions</c>, the worker's <c>--instructions</c>, or null when the run uses
    /// the default. In-process it is <see cref="SessionLimits.InstructionBudget"/>.</summary>
    public ulong? WorkerInstructions { get; init; }

    /// <summary>The session limits of the case's runs: <paramref name="defaults"/> with the case's
    /// <c>worker</c> members, each the in-process value of the worker argument it names.</summary>
    public SessionLimits LimitsFor(SessionLimits defaults)
    {
        var limits = defaults;
        if (WorkerRequestBytes is { } request)
        {
            limits = limits with { RequestFrameBytes = request };
        }

        if (WorkerResponseBytes is { } response)
        {
            limits = limits with { ResponseFrameBytes = response };
        }

        if (WorkerInstructions is { } instructions)
        {
            limits = limits with { InstructionBudget = instructions };
        }

        return limits;
    }

    /// <summary>The frame names of each run, in run order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> RunFrames =>
        Runs.EnumerateArray()
            .Select(run => (IReadOnlyList<string>)run.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToList())
            .ToList();

    /// <summary>
    /// For each run, in run order, the expectations that apply to a host that declares
    /// <paramref name="hostProfiles"/>, as the corpus conventions select them: a run's <c>expect</c>, or every
    /// alternative whose <c>when</c> matches. Two alternatives may have the same <c>when</c>; the run passes when the
    /// observation satisfies one of them (<see cref="CaseExpectation.SatisfyOneAsync"/>). A <c>{bound, enforced}</c>
    /// alternative is selected by <paramref name="enforcedBounds"/>, the bounds the host declares enforced; without
    /// it such an alternative cannot be selected. A run with no matching alternative is a defect of the corpus or
    /// of this reader, and throws.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<CaseExpectation>> ExpectationsFor(IReadOnlySet<string> hostProfiles, IReadOnlySet<string>? enforcedBounds = null)
    {
        var expectations = new List<IReadOnlyList<CaseExpectation>>();
        foreach (var run in Runs.EnumerateArray())
        {
            if (run.TryGetProperty("expect", out var expect))
            {
                expectations.Add([Read(expect)]);
                continue;
            }

            var chosen = run.GetProperty("alternatives").EnumerateArray()
                .Where(alternative => Matches(alternative.GetProperty("when"), hostProfiles, enforcedBounds))
                .Select(alternative => Read(alternative.GetProperty("expect")))
                .ToList();
            if (chosen.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{CaseId}: no alternative matches the host profiles {string.Join(", ", hostProfiles)}.");
            }

            expectations.Add(chosen);
        }

        return expectations;
    }

    /// <summary>Every expectation of every run and every alternative, whatever the host declares.</summary>
    public IEnumerable<CaseExpectation> AllExpectations()
    {
        foreach (var run in Runs.EnumerateArray())
        {
            if (run.TryGetProperty("expect", out var expect))
            {
                yield return Read(expect);
            }
            else
            {
                foreach (var alternative in run.GetProperty("alternatives").EnumerateArray())
                {
                    yield return Read(alternative.GetProperty("expect"));
                }
            }
        }
    }

    static bool Matches(JsonElement when, IReadOnlySet<string> hostProfiles, IReadOnlySet<string>? enforcedBounds)
    {
        if (when.TryGetProperty("hostRuns", out var hostRuns))
        {
            return hostProfiles.Contains(hostRuns.GetString()!) == when.GetProperty("runs").GetBoolean();
        }

        if (when.TryGetProperty("bound", out var bound) && enforcedBounds is not null)
        {
            return enforcedBounds.Contains(bound.GetString()!) == when.GetProperty("enforced").GetBoolean();
        }

        // {bound, enforced} alternatives depend on the bounds a started event declares, which a check that is
        // not told the host's enforced bounds cannot know.
        throw new NotSupportedException($"An alternative with the condition {when} cannot be selected in-process.");
    }

    static CaseExpectation Read(JsonElement expect)
    {
        string? refusedCode = null;
        string? startedAbi = null;
        string? faultKind = null;
        var started = false;
        var fault = false;
        var includes = new List<string>();
        // A run that expects only an exit status, such as a worker started without --abi, lists no events.
        var events = expect.TryGetProperty("events", out var listed) ? listed.EnumerateArray().ToList() : [];
        foreach (var matcher in events)
        {
            switch (matcher.GetProperty("event").GetString())
            {
                case "refused":
                    refusedCode = matcher.GetProperty("code").GetString();
                    if (matcher.TryGetProperty("messageIncludes", out var fragments))
                    {
                        includes.AddRange(fragments.EnumerateArray().Select(fragment => fragment.GetString()!));
                    }

                    break;
                case "started":
                    started = true;
                    startedAbi = matcher.TryGetProperty("abi", out var abi) ? abi.GetString() : null;
                    break;
                case "fault":
                    fault = true;
                    faultKind = matcher.TryGetProperty("kind", out var kind) ? kind.GetString() : null;
                    break;
            }
        }

        var guestRun = new List<string>();
        var sameAs = new List<int>();
        var stdoutFrames = new List<JsonElement>();
        if (expect.TryGetProperty("stdoutFrames", out var frames))
        {
            guestRun.Add("stdoutFrames");
            stdoutFrames.AddRange(frames.EnumerateArray());
            sameAs.AddRange(frames.EnumerateArray()
                .Where(frame => frame.TryGetProperty("sameAsFrame", out _))
                .Select(frame => frame.GetProperty("sameAsFrame").GetInt32()));
        }

        int? exitCode = expect.TryGetProperty("exitCode", out var exit) ? exit.GetInt32() : null;
        if (refusedCode is null && exitCode is { } code)
        {
            guestRun.Add($"exitCode {code}");
        }

        var noEvents = expect.TryGetProperty("noEvents", out var none)
            ? none.EnumerateArray().Select(name => name.GetString()!).ToList()
            : [];
        int? stdoutBytes = expect.TryGetProperty("stdoutBytes", out var bytes) ? bytes.GetInt32() : null;
        (string, double, double)? within = null;
        if (expect.TryGetProperty("withinSeconds", out var seconds))
        {
            var range = seconds.GetProperty("range").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            within = (seconds.GetProperty("until").GetString()!, range[0], range[1]);
        }

        return new CaseExpectation(refusedCode, includes, startedAbi, guestRun, started, fault, faultKind, noEvents,
            exitCode, stdoutBytes, sameAs, within, stdoutFrames, expect);
    }
}

/// <summary>Reads the cases of the pinned crafted corpus, <c>wasm-host.cases.json</c>.</summary>
internal static class CraftedCorpus
{
    static readonly Lazy<JsonDocument> Document = new(() => JsonDocument.Parse(File.ReadAllBytes(MepWasmFixtures.Corpus)));
    static readonly Lazy<IReadOnlyList<CraftedCase>> All = new(Load);

    public static IReadOnlyList<CraftedCase> Cases => All.Value;

    public static CraftedCase Case(string caseId) => Cases.Single(@case => @case.CaseId == caseId);

    /// <summary>The corpus's <c>deadlineMarginSeconds</c>: how long after a deadline a host may still stop the call.</summary>
    public static TimeSpan DeadlineMargin => TimeSpan.FromSeconds(Document.Value.RootElement.GetProperty("deadlineMarginSeconds").GetDouble());

    /// <summary>The bytes of the module the corpus names <paramref name="module"/>.</summary>
    public static byte[] ModuleBytes(string module) => File.ReadAllBytes(ModulePath(module));

    public static string ModulePath(string module)
    {
        var relative = Document.Value.RootElement.GetProperty("modules").GetProperty(module).GetProperty("wasm").GetString()!;
        return MepWasmFixtures.FullPath(relative);
    }

    /// <summary>
    /// The body of the frame the corpus names <paramref name="name"/>: the UTF-8 of its value serialized compactly
    /// with members in the order written, as the corpus conventions say.
    /// </summary>
    public static byte[] FrameBody(string name)
    {
        var frame = Document.Value.RootElement.GetProperty("frames").GetProperty(name);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            frame.WriteTo(writer);
        }

        return buffer.ToArray();
    }

    /// <summary>The <c>method</c> of the frame <paramref name="name"/>.</summary>
    public static string FrameMethod(string name) =>
        Document.Value.RootElement.GetProperty("frames").GetProperty(name).GetProperty("method").GetString()!;

    /// <summary>Whether the frame <paramref name="name"/> is a request, a message with an <c>id</c>.</summary>
    public static bool IsRequest(string name) =>
        Document.Value.RootElement.GetProperty("frames").GetProperty(name).TryGetProperty("id", out _);

    static IReadOnlyList<CraftedCase> Load()
    {
        // The document stays alive for the test run: each case keeps its runs as a JsonElement.
        var root = Document.Value.RootElement;
        var modules = root.GetProperty("modules");
        return root.GetProperty("cases").EnumerateArray()
            .Select(@case =>
            {
                var module = @case.GetProperty("module").GetString()!;
                var entry = modules.GetProperty(module);
                return new CraftedCase(
                    @case.GetProperty("caseId").GetString()!,
                    @case.GetProperty("appliesTo").GetString()!,
                    module,
                    @case.TryGetProperty("declaredAbi", out var declared) ? declared.GetString() : null,
                    MepWasmFixtures.FullPath(entry.GetProperty("wasm").GetString()!),
                    entry.GetProperty("sha256").GetString()!,
                    @case.GetProperty("runs"))
                {
                    Bounds = @case.TryGetProperty("bounds", out var bounds)
                        ? bounds.EnumerateArray().Select(bound => bound.GetString()!).ToList()
                        : [],
                    WorkerRequestBytes = @case.TryGetProperty("worker", out var worker) && worker.TryGetProperty("requestBytes", out var request)
                        ? request.GetInt64()
                        : null,
                    WorkerResponseBytes = @case.TryGetProperty("worker", out var limits) && limits.TryGetProperty("responseBytes", out var response)
                        ? response.GetInt64()
                        : null,
                    WorkerInstructions = @case.TryGetProperty("worker", out var budget) && budget.TryGetProperty("instructions", out var instructions)
                        ? instructions.GetUInt64()
                        : null,
                };
            })
            .ToList();
    }
}
