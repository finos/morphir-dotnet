// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The pinned crafted corpus (<c>tests/Fixtures/MepWasm/wasm-host.cases.json</c>, finos/morphir <c>48d10706f</c>) driven
/// through a real worker process of a session driver, with the corpus's <c>conventions</c>: the worker arguments, the
/// frames, and one check for each matcher the corpus uses. This is a test driver (task 4.1 of add-wasm-extension-host),
/// not the kit's runner. It reads the pinned JSON directly; it does not use the generated features. The host it
/// describes runs <c>extism-kernel-v1</c> only and enforces every bound, as the .NET worker's <c>started</c> event
/// declares. The corpus is the fixture folder <c>Fixtures/MepWasm</c> of the test process.
/// </summary>
public static class WorkerCorpus
{
    const string HostProfile = "extism-kernel-v1";

    /// <summary>All cases instead of a sample: <c>MORPHIR_WORKER_CORPUS=all</c>.</summary>
    public const string AllCasesVariable = "MORPHIR_WORKER_CORPUS";

    /// <summary>The pinned corpus directory: <c>Fixtures/MepWasm</c> beside the test assembly.</summary>
    public static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "MepWasm");

    /// <summary><paramref name="sample"/>, or every case when <c>MORPHIR_WORKER_CORPUS=all</c>.</summary>
    public static IEnumerable<string> Selected(IEnumerable<string> sample) =>
        Environment.GetEnvironmentVariable(AllCasesVariable) == "all" ? CaseIds : sample;

    static readonly Lazy<JsonElement> Document = new(() =>
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "wasm-host.cases.json")));
        return document.RootElement.Clone();
    });

    /// <summary>Every case id, in corpus order.</summary>
    public static IReadOnlyList<string> CaseIds =>
        Document.Value.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("caseId").GetString()!).ToArray();

    static JsonElement Case(string id) =>
        Document.Value.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("caseId").GetString() == id);

    /// <summary>The path of a corpus module.</summary>
    public static string ModulePath(string module) =>
        Path.Combine(Fixtures, Document.Value.GetProperty("modules").GetProperty(module).GetProperty("wasm").GetString()!);

    /// <summary>The path of a corpus module relative to the fixture folder of the test project, for a fixture copy.</summary>
    public static string ModuleFixture(string module) =>
        "MepWasm/" + Document.Value.GetProperty("modules").GetProperty(module).GetProperty("wasm").GetString()!;

    /// <summary>The SHA-256 that the corpus pins for a module.</summary>
    public static string ModuleSha256(string module) =>
        Document.Value.GetProperty("modules").GetProperty(module).GetProperty("sha256").GetString()!;

    /// <summary>A named frame of the corpus as the conventions write it: compact, with members in the order written.</summary>
    public static byte[] FrameBody(string name)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            Document.Value.GetProperty("frames").GetProperty(name).WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>What a case gave: passed, not applicable to this host, or failed with every reason.</summary>
    public sealed record Outcome(string CaseId, string State, IReadOnlyList<string> Problems)
    {
        public override string ToString() => $"{CaseId}: {State}{(Problems.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, Problems))}";
    }

    /// <summary>Runs every run of a case against the worker of <paramref name="sessions"/>.</summary>
    public static async Task<Outcome> RunAsync(IProcessSessionDriver sessions, string identity, string caseId)
    {
        var testCase = Case(caseId);
        if (testCase.GetProperty("appliesTo").GetString() != HostProfile) return new Outcome(caseId, "not_applicable", []);

        var module = testCase.GetProperty("module").GetString()!;
        var path = ModulePath(module);
        var sha = Document.Value.GetProperty("modules").GetProperty(module).GetProperty("sha256").GetString()!;
        var actualSha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        if (actualSha != sha) return new Outcome(caseId, "failed", [$"the module {module} has SHA-256 {actualSha}, the corpus records {sha}"]);

        var problems = new List<string>();
        var number = 0;
        foreach (var run in testCase.GetProperty("runs").EnumerateArray())
        {
            number++;
            var frames = run.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray();
            var arguments = Arguments(testCase, identity, path);
            await using var worker = await sessions.StartAsync([.. ExtensionWorker.Command, .. arguments]);
            await worker.WriteAsync([.. frames.Select(frame => ContentLengthFrames.Frame(FrameBody(frame)))]);
            var lastWritten = worker.Elapsed;
            var limit = TimeSpan.FromMilliseconds(CallDeadlineMs(testCase) * Math.Max(1, frames.Length))
                + TimeSpan.FromSeconds(Document.Value.GetProperty("deadlineMarginSeconds").GetDouble());
            var observed = await worker.WaitForExitAsync(limit);

            var expectations = run.TryGetProperty("expect", out var expect)
                ? [expect]
                : run.GetProperty("alternatives").EnumerateArray().Where(Applies).Select(a => a.GetProperty("expect")).ToArray();
            if (expectations.Length == 0)
            {
                problems.Add($"run {number}: no alternative applies to this host");
                continue;
            }

            var general = General(observed, sha);
            var failures = expectations.Select(e => Check(e, observed, frames, lastWritten)).ToArray();
            if (general.Count > 0 || failures.All(f => f.Count > 0))
            {
                problems.Add($"run {number} with {string.Join(" ", arguments)}: {observed.Describe()}");
                problems.AddRange(general.Select(p => $"run {number}: {p}"));
                for (var i = 0; i < failures.Length; i++) problems.AddRange(failures[i].Select(p => $"run {number}, expectation {i + 1}: {p}"));
            }
        }

        return new Outcome(caseId, problems.Count == 0 ? "passed" : "failed", problems);
    }

    /// <summary>The worker arguments of the corpus's <c>worker</c> convention.</summary>
    static List<string> Arguments(JsonElement testCase, string identity, string modulePath)
    {
        var worker = testCase.TryGetProperty("worker", out var w) && w.ValueKind == JsonValueKind.Object ? w : default;
        string? Member(string name) => worker.ValueKind == JsonValueKind.Object && worker.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString()
            : null;
        var omit = worker.ValueKind == JsonValueKind.Object && worker.TryGetProperty("omit", out var o)
            ? o.EnumerateArray().Select(e => e.GetString()!).ToHashSet()
            : [];
        if (Member("buildIdentity") == "other") identity = identity[..^1] + (identity[^1] == '0' ? '1' : '0');

        var pairs = new List<(string Name, string? Value)>();
        if (testCase.TryGetProperty("declaredAbi", out var abi)) pairs.Add(("--abi", abi.GetString()));
        pairs.Add(("--module", modulePath));
        pairs.Add(("--build-identity", identity));
        if (Member("requestBytes") is { } request) pairs.Add(("--request-bytes", request));
        if (Member("responseBytes") is { } response) pairs.Add(("--response-bytes", response));
        if (Member("instructions") is { } instructions) pairs.Add(("--instructions", instructions));
        pairs.Add(("--json", null));
        return pairs.Where(pair => !omit.Contains(pair.Name))
            .SelectMany(pair => pair.Value is null ? new[] { pair.Name } : [pair.Name, pair.Value]).ToList();
    }

    static double CallDeadlineMs(JsonElement testCase) =>
        testCase.TryGetProperty("declaredAbi", out var abi) && abi.GetString() == "morphir-bytes-v1" ? 10000 : 30000;

    /// <summary>Whether an alternative's <c>when</c> matches this host: it runs extism-kernel-v1 only and enforces every bound.</summary>
    static bool Applies(JsonElement alternative)
    {
        var when = alternative.GetProperty("when");
        if (when.TryGetProperty("hostRuns", out var profile))
            return (profile.GetString() == HostProfile) == when.GetProperty("runs").GetBoolean();
        if (when.TryGetProperty("bound", out _)) return when.GetProperty("enforced").GetBoolean();
        return false;
    }

    /// <summary>The checks every run gets: stdout is frames only; each started event has the module's digest and
    /// exactly the three bounds; each fault has kind and optional message, and nothing else.</summary>
    static List<string> General(SessionExit run, string sha)
    {
        var problems = new List<string>();
        if (run.ExitCode is null) problems.Add("the worker did not exit by itself");
        if (run.Frames.Problem is { } frames) problems.Add(frames);
        foreach (var (name, json, _) in run.Events)
        {
            if (name == "started")
            {
                if (json.GetProperty("moduleSha256").GetString() != sha) problems.Add($"started.moduleSha256 is not {sha}");
                var bounds = json.GetProperty("bounds").EnumerateObject().ToArray();
                if (!bounds.Select(b => b.Name).Order().SequenceEqual(new[] { "callDeadline", "instructionBudget", "memoryCeiling" }))
                    problems.Add("started.bounds does not have exactly memoryCeiling, instructionBudget and callDeadline");
                if (bounds.Any(b => b.Value.GetRawText() != """{"enforced":true}"""))
                    problems.Add("a started bound is not {\"enforced\":true}");
            }

            if (name == "fault")
            {
                var members = json.GetProperty("fault").EnumerateObject().Select(m => m.Name).ToArray();
                if (!members.Contains("kind") || members.Except(["kind", "message"]).Any())
                    problems.Add($"a fault has the members {string.Join(", ", members)}, not kind and an optional message");
            }
        }

        return problems;
    }

    static List<string> Check(JsonElement expect, SessionExit run, string[] sentFrames, TimeSpan lastWritten)
    {
        var problems = new List<string>();
        if (expect.TryGetProperty("exitCode", out var exit) && run.ExitCode != exit.GetInt32())
            problems.Add($"exit status {run.ExitCode}, expected {exit.GetInt32()}");
        if (expect.TryGetProperty("stdoutBytes", out var bytes) && run.StandardOutput.Length != bytes.GetInt32())
            problems.Add($"stdout has {run.StandardOutput.Length} bytes, expected {bytes.GetInt32()}");

        if (expect.TryGetProperty("stdoutFrames", out var expectedFrames))
        {
            var frames = run.Frames.Frames;
            var expected = expectedFrames.EnumerateArray().ToArray();
            if (frames.Count != expected.Length) problems.Add($"stdout has {frames.Count} frames, expected {expected.Length}");
            for (var i = 0; i < Math.Min(frames.Count, expected.Length); i++)
            {
                if (FrameProblem(frames[i], expected[i], sentFrames) is { } problem) problems.Add($"frame {i + 1}: {problem}");
            }
        }

        var events = run.Events;
        if (expect.TryGetProperty("events", out var matchers))
        {
            var at = 0;
            foreach (var matcher in matchers.EnumerateArray())
            {
                while (at < events.Count && !Matches(matcher, events[at].Name, events[at].Json)) at++;
                if (at == events.Count)
                {
                    problems.Add($"no event {matcher.GetRawText()} in order among {string.Join(", ", run.EventNames)}");
                    break;
                }

                at++;
            }
        }

        if (expect.TryGetProperty("noEvents", out var absent))
        {
            foreach (var name in absent.EnumerateArray().Select(n => n.GetString()))
            {
                if (run.EventNames.Contains(name)) problems.Add($"an event {name} appears");
            }
        }

        var output = run.GuestOutput;
        if (expect.TryGetProperty("guestOutput", out var ordered))
        {
            var from = 0;
            foreach (var text in ordered.EnumerateArray().Select(t => t.GetString()!))
            {
                var found = output.IndexOf(text, from, StringComparison.Ordinal);
                if (found < 0)
                {
                    problems.Add($"guest output has no '{text}' in order");
                    break;
                }

                from = found + text.Length;
            }
        }

        if (expect.TryGetProperty("guestOutputIncludes", out var includes))
        {
            foreach (var text in includes.EnumerateArray().Select(t => t.GetString()!))
            {
                if (!output.Contains(text, StringComparison.Ordinal)) problems.Add($"guest output has no '{text}'");
            }
        }

        if (expect.TryGetProperty("noGuestOutput", out var excluded))
        {
            foreach (var text in excluded.EnumerateArray().Select(t => t.GetString()!))
            {
                if (output.Contains(text, StringComparison.Ordinal)) problems.Add($"guest output has '{text}'");
            }
        }

        if (expect.TryGetProperty("withinSeconds", out var within))
        {
            var range = within.GetProperty("range").EnumerateArray().Select(r => r.GetDouble()).ToArray();
            TimeSpan? until = within.GetProperty("until").GetString() == "fault"
                ? events.Where(e => e.Name == "fault").Select(e => (TimeSpan?)e.At).FirstOrDefault()
                : run.ExitedAt;
            if (until is not { } end) problems.Add("no fault event to time");
            else
            {
                var seconds = (end - lastWritten).TotalSeconds;
                if (seconds < range[0] || seconds > range[1])
                    problems.Add($"took {seconds.ToString("0.000", CultureInfo.InvariantCulture)} s, outside [{range[0]}, {range[1]}]");
            }
        }

        return problems;
    }

    static bool Matches(JsonElement matcher, string name, JsonElement json)
    {
        if (matcher.GetProperty("event").GetString() != name) return false;
        if (matcher.TryGetProperty("abi", out var abi) && Text(json, "abi") != abi.GetString()) return false;
        if (matcher.TryGetProperty("code", out var code) && Text(json, "error", "code") != code.GetString()) return false;
        if (matcher.TryGetProperty("kind", out var kind) && Text(json, "fault", "kind") != kind.GetString()) return false;
        if (matcher.TryGetProperty("messageIncludes", out var includes))
        {
            var message = Text(json, "error", "message") ?? "";
            if (includes.EnumerateArray().Any(text => !message.Contains(text.GetString()!, StringComparison.Ordinal))) return false;
        }

        return true;
    }

    static string? Text(JsonElement json, params string[] path)
    {
        foreach (var name in path)
        {
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(name, out json)) return null;
        }

        return json.ValueKind == JsonValueKind.String ? json.GetString() : null;
    }

    static string? FrameProblem(byte[] body, JsonElement expected, string[] sentFrames)
    {
        JsonElement actual;
        try
        {
            using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(body));
            actual = document.RootElement.Clone();
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException)
        {
            return $"the body is not UTF-8 JSON: {error.Message}";
        }

        if (expected.TryGetProperty("json", out var json))
            return JsonElement.DeepEquals(actual, json) ? null : $"{actual.GetRawText()} is not {json.GetRawText()}";
        if (expected.TryGetProperty("sameAsFrame", out var index))
        {
            using var sent = JsonDocument.Parse(FrameBody(sentFrames[index.GetInt32()]));
            return JsonElement.DeepEquals(actual, sent.RootElement) ? null : $"{actual.GetRawText()} is not the sent frame {index.GetInt32()}";
        }

        foreach (var entry in expected.GetProperty("pointers").EnumerateArray())
        {
            var pointer = entry.GetProperty("pointer").GetString()!;
            if (Resolve(actual, pointer) is not { } value) return $"{pointer} does not resolve in {actual.GetRawText()}";
            if (entry.TryGetProperty("equals", out var equals) && !JsonElement.DeepEquals(value, equals))
                return $"{pointer} is {value.GetRawText()}, not {equals.GetRawText()}";
            if (entry.TryGetProperty("type", out var type) && TypeName(value) != type.GetString())
                return $"{pointer} is a {TypeName(value)}, not a {type.GetString()}";
        }

        return null;
    }

    static JsonElement? Resolve(JsonElement value, string pointer)
    {
        if (pointer.Length == 0) return value;
        foreach (var raw in pointer[1..].Split('/'))
        {
            var token = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(token, out var member)) value = member;
            else if (value.ValueKind == JsonValueKind.Array && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
                && i < value.GetArrayLength()) value = value[i];
            else return null;
        }

        return value;
    }

    static string TypeName(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => "null",
    };
}
