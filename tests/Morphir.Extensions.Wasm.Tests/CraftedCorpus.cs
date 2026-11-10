using System.Text.Json;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// One expectation of a crafted case, reduced to what an in-process check can compare: the <c>refused</c>
/// event (code and message fragments) or the <c>started</c> event (ABI), and whether the worker-level
/// expectation also needs the guest to run.
/// </summary>
/// <param name="RefusedCode">The <c>error.code</c> of the expected <c>refused</c> event, or null when none is expected.</param>
/// <param name="MessageIncludes">The strings the refusal's message must contain.</param>
/// <param name="StartedAbi">The <c>abi</c> of the expected <c>started</c> event, or null when none is fixed.</param>
/// <param name="GuestRunMembers">The members of the expectation that only a running guest can show
/// (<c>stdoutFrames</c>, or an exit status after a served session).</param>
internal sealed record CaseExpectation(
    string? RefusedCode,
    IReadOnlyList<string> MessageIncludes,
    string? StartedAbi,
    IReadOnlyList<string> GuestRunMembers);

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

    /// <summary>
    /// The expectations of every run for a host that declares <paramref name="hostProfiles"/>, as the corpus
    /// conventions select them: a run's <c>expect</c>, or the alternative whose <c>when</c> matches.
    /// </summary>
    public IReadOnlyList<CaseExpectation> ExpectationsFor(IReadOnlySet<string> hostProfiles)
    {
        var expectations = new List<CaseExpectation>();
        foreach (var run in Runs.EnumerateArray())
        {
            if (run.TryGetProperty("expect", out var expect))
            {
                expectations.Add(Read(expect));
                continue;
            }

            var chosen = run.GetProperty("alternatives").EnumerateArray()
                .Where(alternative => Matches(alternative.GetProperty("when"), hostProfiles))
                .Select(alternative => alternative.GetProperty("expect"))
                .ToList();
            if (chosen.Count != 1)
            {
                throw new InvalidOperationException(
                    $"{CaseId}: {chosen.Count} alternatives match the host profiles {string.Join(", ", hostProfiles)}.");
            }

            expectations.Add(Read(chosen[0]));
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

    static bool Matches(JsonElement when, IReadOnlySet<string> hostProfiles)
    {
        if (when.TryGetProperty("hostRuns", out var hostRuns))
        {
            return hostProfiles.Contains(hostRuns.GetString()!) == when.GetProperty("runs").GetBoolean();
        }

        // {bound, enforced} alternatives depend on the bounds a started event declares, which a classification
        // check cannot know. No classification case has one at the pin.
        throw new NotSupportedException($"An alternative with the condition {when} cannot be selected in-process.");
    }

    static CaseExpectation Read(JsonElement expect)
    {
        string? refusedCode = null;
        string? startedAbi = null;
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
                    startedAbi = matcher.TryGetProperty("abi", out var abi) ? abi.GetString() : null;
                    break;
            }
        }

        var guestRun = new List<string>();
        if (expect.TryGetProperty("stdoutFrames", out _))
        {
            guestRun.Add("stdoutFrames");
        }

        if (refusedCode is null && expect.TryGetProperty("exitCode", out var exitCode))
        {
            guestRun.Add($"exitCode {exitCode.GetInt32()}");
        }

        return new CaseExpectation(refusedCode, includes, startedAbi, guestRun);
    }
}

/// <summary>Reads the cases of the pinned crafted corpus, <c>wasm-host.cases.json</c>.</summary>
internal static class CraftedCorpus
{
    static readonly Lazy<IReadOnlyList<CraftedCase>> All = new(Load);

    public static IReadOnlyList<CraftedCase> Cases => All.Value;

    public static CraftedCase Case(string caseId) => Cases.Single(@case => @case.CaseId == caseId);

    /// <summary>The bytes of the module the corpus names <paramref name="module"/>.</summary>
    public static byte[] ModuleBytes(string module) => File.ReadAllBytes(ModulePath(module));

    public static string ModulePath(string module)
    {
        using var corpus = JsonDocument.Parse(File.ReadAllBytes(MepWasmFixtures.Corpus));
        var relative = corpus.RootElement.GetProperty("modules").GetProperty(module).GetProperty("wasm").GetString()!;
        return MepWasmFixtures.FullPath(relative);
    }

    static IReadOnlyList<CraftedCase> Load()
    {
        // The document stays alive for the test run: each case keeps its runs as a JsonElement.
        var corpus = JsonDocument.Parse(File.ReadAllBytes(MepWasmFixtures.Corpus));
        var modules = corpus.RootElement.GetProperty("modules");
        return corpus.RootElement.GetProperty("cases").EnumerateArray()
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
                    @case.GetProperty("runs"));
            })
            .ToList();
    }
}
