// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Morphir.Build.Helpers;

// The CI plan of .github/workflows/development.yml. This file is pure: it decides which platforms a run selects, which
// passing jobs of earlier runs for the same head commit it reuses, what the job summaries report, and the verdict of
// the aggregate ci check. The Fallout targets CIPlan and CIGate in Build.CI.cs read the GitHub event and API and write
// the step outputs and summaries.

/// <summary>
/// One check of the development workflow. The set is closed: the constructor is private and <see cref="All"/> holds
/// the only instances.
/// </summary>
public sealed class CiCheck
{
    public static readonly CiCheck Lint = new("lint", "lint", "lint", null);
    public static readonly CiCheck OpenSpec = new("openspec", "openspec", "OpenSpec workflow", null);
    public static readonly CiCheck TestLinux = new("test-linux", "test", "test (ubuntu-latest)", "ubuntu-latest");
    public static readonly CiCheck TestWindows = new("test-windows", "test", "test (windows-latest)", "windows-latest");
    public static readonly CiCheck TestMacOS = new("test-macos", "test", "test (macOS-latest)", "macOS-latest");

    /// <summary>Every check, in the order of the plan outputs and the job summaries.</summary>
    public static readonly ImmutableArray<CiCheck> All = [Lint, OpenSpec, TestLinux, TestWindows, TestMacOS];

    /// <summary>The checks that run on Linux. Every run selects them.</summary>
    public static readonly ImmutableArray<CiCheck> Linux = [Lint, OpenSpec, TestLinux];

    private CiCheck(string id, string workflowJob, string jobName, string? runner) =>
        (Id, WorkflowJob, JobName, Runner) = (id, workflowJob, jobName, runner);

    /// <summary>The stable identifier in the serialized plan.</summary>
    public string Id { get; }

    /// <summary>The job id in development.yml. The three test checks are the legs of one matrix job.</summary>
    public string WorkflowJob { get; }

    /// <summary>The job name that the GitHub Actions jobs API reports.</summary>
    public string JobName { get; }

    /// <summary>The runner label of a test matrix leg, or null for a check that is not a matrix leg.</summary>
    public string? Runner { get; }

    public static bool TryParse(string id, [NotNullWhen(true)] out CiCheck? check)
    {
        check = All.FirstOrDefault(candidate => candidate.Id == id);
        return check is not null;
    }

    public static bool TryFromJobName(string jobName, [NotNullWhen(true)] out CiCheck? check)
    {
        check = All.FirstOrDefault(candidate => candidate.JobName == jobName);
        return check is not null;
    }

    public override string ToString() => JobName;
}

/// <summary>
/// A platform that a run can add to Linux. Windows and macOS runners cost more than Linux runners, so a run selects
/// them only when it needs them. The set is closed.
/// </summary>
public sealed class CiPlatform
{
    public static readonly CiPlatform Windows = new("windows", "Windows", CiLabels.Windows, CiCheck.TestWindows);
    public static readonly CiPlatform MacOS = new("macos", "macOS", CiLabels.MacOS, CiCheck.TestMacOS);

    /// <summary>Every optional platform, in the order of the matrix and the reports.</summary>
    public static readonly ImmutableArray<CiPlatform> All = [Windows, MacOS];

    private CiPlatform(string id, string displayName, string label, CiCheck check) =>
        (Id, DisplayName, Label, Check) = (id, displayName, label, check);

    /// <summary>The token in the os input of a dispatched run.</summary>
    public string Id { get; }

    public string DisplayName { get; }

    /// <summary>The pull request label that selects this platform.</summary>
    public string Label { get; }

    /// <summary>The test matrix leg of this platform.</summary>
    public CiCheck Check { get; }

    public static bool TryParse(string id, [NotNullWhen(true)] out CiPlatform? platform)
    {
        platform = All.FirstOrDefault(candidate => candidate.Id == id);
        return platform is not null;
    }

    /// <summary>The platforms of a set, in the order of <see cref="All"/>.</summary>
    public static ImmutableArray<CiPlatform> Ordered(IEnumerable<CiPlatform> platforms)
    {
        var set = platforms.ToHashSet();
        return [.. All.Where(set.Contains)];
    }

    /// <summary>"Linux", then the display name of each platform: for example "Linux, Windows".</summary>
    public static string Describe(IEnumerable<CiPlatform> platforms) =>
        string.Join(", ", platforms.Select(platform => platform.DisplayName).Prepend("Linux"));

    public override string ToString() => DisplayName;
}

/// <summary>The pull request labels that select the platforms.</summary>
public static class CiLabels
{
    /// <summary>Add Windows.</summary>
    public const string Windows = "ci:windows";

    /// <summary>Add macOS.</summary>
    public const string MacOS = "ci:macos";

    /// <summary>Add Windows and macOS.</summary>
    public const string Full = "ci:full";

    /// <summary>Add no platform: Linux only, also on a pull request that is ready for review.</summary>
    public const string LinuxOnly = "ci:linux-only";

    public const string Prefix = "ci:";

    /// <summary>The platforms that each label selects.</summary>
    public static readonly ImmutableSortedDictionary<string, ImmutableArray<CiPlatform>> Platforms =
        ImmutableSortedDictionary.CreateRange<string, ImmutableArray<CiPlatform>>(StringComparer.Ordinal,
        [
            new(Windows, [CiPlatform.Windows]),
            new(MacOS, [CiPlatform.MacOS]),
            new(Full, CiPlatform.All),
            new(LinuxOnly, []),
        ]);
}

/// <summary>The <c>os</c> input of a manual or label-dispatched run.</summary>
public abstract record OsChoice
{
    private OsChoice() { }

    /// <summary>Every platform.</summary>
    public static readonly OsChoice Auto = new AutoChoice();

    /// <summary>Every platform.</summary>
    public sealed record AutoChoice : OsChoice;

    /// <summary>Linux and exactly these platforms.</summary>
    /// <param name="Selected">The platforms, in the order of <see cref="CiPlatform.All"/>.</param>
    /// <param name="Input">The os input, for the reports.</param>
    public sealed record Platforms(ImmutableArray<CiPlatform> Selected, string Input) : OsChoice;
}

/// <summary>What started the run.</summary>
public abstract record CiTrigger
{
    private CiTrigger() { }

    /// <summary>A pull request event, or the pull request state that the GitHub API reported.</summary>
    public sealed record PullRequest(bool Draft, ImmutableSortedSet<string> Labels) : CiTrigger;

    /// <summary>
    /// A manual workflow_dispatch run of a branch. GitHub does not count its checks for a pull request, so it is never a
    /// merge candidate, and its jobs are not evidence for a pull request run.
    /// </summary>
    public sealed record Dispatch(OsChoice Choice) : CiTrigger;

    /// <summary>A workflow_call from a release. It tests every platform again and reuses nothing.</summary>
    public sealed record Release : CiTrigger;
}

/// <summary>The policy of a run, before it looks at earlier passing jobs.</summary>
/// <param name="Platforms">The platforms that the run adds to Linux.</param>
/// <param name="MergeCandidate">True when every platform is necessary for the ci check to pass.</param>
/// <param name="ReuseEvidence">True when passing jobs of earlier runs for the same head commit count.</param>
/// <param name="Reason">Why the run selected these platforms, for the reports.</param>
public sealed record CiRequest(ImmutableArray<CiPlatform> Platforms, bool MergeCandidate, bool ReuseEvidence, string Reason);

public static class CiPolicy
{
    public static CiRequest Resolve(CiTrigger trigger) => trigger switch
    {
        CiTrigger.PullRequest pr => ForPullRequest(pr),
        CiTrigger.Dispatch { Choice: OsChoice.Platforms platforms } =>
            new(platforms.Selected, MergeCandidate: false, ReuseEvidence: false, $"os input {platforms.Input}"),
        CiTrigger.Dispatch => new(CiPlatform.All, MergeCandidate: false, ReuseEvidence: false, "os input auto"),
        CiTrigger.Release => new(CiPlatform.All, MergeCandidate: true, ReuseEvidence: false, "release call"),
        _ => throw new UnreachableException(),
    };

    /// <summary>
    /// Parses the os input: <c>auto</c> (or empty), <c>linux</c>, <c>full</c>, or a comma-separated list of
    /// <c>windows</c> and <c>macos</c>. Linux always runs, so it is not part of a list.
    /// </summary>
    public static bool TryParseOsChoice(string input, [NotNullWhen(true)] out OsChoice? choice)
    {
        var text = input.Trim();
        choice = text switch
        {
            "" or "auto" => OsChoice.Auto,
            "linux" => new OsChoice.Platforms([], text),
            "full" => new OsChoice.Platforms(CiPlatform.All, text),
            _ => null,
        };
        if (choice is not null) return true;

        var platforms = new List<CiPlatform>();
        foreach (var token in text.Split(','))
        {
            if (!CiPlatform.TryParse(token.Trim(), out var platform)) return false;
            platforms.Add(platform);
        }

        var ordered = CiPlatform.Ordered(platforms);
        choice = new OsChoice.Platforms(ordered, string.Join(",", ordered.Select(platform => platform.Id)));
        return true;
    }

    private static CiRequest ForPullRequest(CiTrigger.PullRequest pr)
    {
        var mergeCandidate = !pr.Draft;
        var labels = pr.Labels.Where(CiLabels.Platforms.ContainsKey).Order(StringComparer.Ordinal).ToList();
        if (labels.Count > 0)
        {
            return new(
                CiPlatform.Ordered(labels.SelectMany(label => CiLabels.Platforms[label])),
                mergeCandidate,
                ReuseEvidence: true,
                $"{(labels.Count == 1 ? "label" : "labels")} {string.Join(", ", labels)}");
        }

        return pr.Draft
            ? new([], mergeCandidate, ReuseEvidence: true, "draft pull request with no platform label")
            : new(CiPlatform.All, mergeCandidate, ReuseEvidence: true, "pull request ready for review with no platform label");
    }
}

/// <summary>A job that passed in an earlier run for the same head commit.</summary>
/// <param name="Check">The check that the job ran.</param>
/// <param name="RunId">The id of the workflow run.</param>
/// <param name="Url">The web page of the job.</param>
public sealed record CiEvidence(CiCheck Check, long RunId, string Url);

/// <summary>The checks that a run selects, reuses and runs.</summary>
public sealed record CiPlan(
    ImmutableArray<CiPlatform> Platforms,
    bool MergeCandidate,
    string Reason,
    ImmutableArray<CiCheck> Selected,
    ImmutableArray<CiEvidence> Reused,
    ImmutableArray<CiCheck> Run)
{
    /// <summary>The selected operating systems, for example "Linux, Windows".</summary>
    public string Selection => CiPlatform.Describe(Platforms);

    /// <summary>
    /// Creates the plan. A selected check that passed in an earlier run is reused when the request allows it; the first
    /// passing job of a check in <paramref name="passed"/> is the one that the reports link.
    /// </summary>
    public static CiPlan Create(CiRequest request, IEnumerable<CiEvidence> passed)
    {
        var selected = CiCheck.All.Where(check =>
            CiCheck.Linux.Contains(check) || request.Platforms.Any(platform => platform.Check == check)).ToImmutableArray();
        var evidence = request.ReuseEvidence
            ? passed.GroupBy(item => item.Check).ToDictionary(group => group.Key, group => group.First())
            : [];
        return new(
            request.Platforms,
            request.MergeCandidate,
            request.Reason,
            selected,
            [.. selected.Where(evidence.ContainsKey).Select(check => evidence[check])],
            [.. selected.Where(check => !evidence.ContainsKey(check))]);
    }

    /// <summary>The evidence for a check, or null when the plan did not reuse one.</summary>
    public CiEvidence? ReusedFor(CiCheck check) => Reused.FirstOrDefault(item => item.Check == check);

    /// <summary>The step outputs of the plan job, which the jobs of development.yml read.</summary>
    public ImmutableSortedDictionary<string, string> Outputs()
    {
        var matrix = Run.Where(check => check.Runner is not null).Select(check => check.Runner!).ToArray();
        return ImmutableSortedDictionary.CreateRange<string, string>(
        [
            new("lint", Bool(Run.Contains(CiCheck.Lint))),
            new("openspec", Bool(Run.Contains(CiCheck.OpenSpec))),
            new("test", Bool(matrix.Length > 0)),
            new("matrix", JsonSerializer.Serialize(matrix)),
            new("selection", Selection),
            new("plan", Serialize()),
        ]);
    }

    public static bool TryParse(string json, [NotNullWhen(true)] out CiPlan? plan)
    {
        plan = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var platforms = ImmutableArray.CreateBuilder<CiPlatform>();
            foreach (var item in root.GetProperty("platforms").EnumerateArray())
            {
                if (item.GetString() is not { } id || !CiPlatform.TryParse(id, out var platform)) return false;
                platforms.Add(platform);
            }

            var reused = ImmutableArray.CreateBuilder<CiEvidence>();
            foreach (var item in root.GetProperty("reused").EnumerateArray())
            {
                if (item.GetProperty("check").GetString() is not { } id || !CiCheck.TryParse(id, out var check)) return false;
                reused.Add(new(check, item.GetProperty("run").GetInt64(), item.GetProperty("url").GetString() ?? ""));
            }

            if (!TryChecks(root.GetProperty("selected"), out var selected) || !TryChecks(root.GetProperty("run"), out var run))
            {
                return false;
            }

            plan = new(platforms.ToImmutable(), root.GetProperty("mergeCandidate").GetBoolean(),
                root.GetProperty("reason").GetString() ?? "", selected, reused.ToImmutable(), run);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private string Serialize() => JsonSerializer.Serialize(new
    {
        platforms = Platforms.Select(platform => platform.Id),
        mergeCandidate = MergeCandidate,
        reason = Reason,
        selected = Selected.Select(check => check.Id),
        reused = Reused.Select(item => new { check = item.Check.Id, run = item.RunId, url = item.Url }),
        run = Run.Select(check => check.Id),
    });

    private static bool TryChecks(JsonElement array, out ImmutableArray<CiCheck> checks)
    {
        var builder = ImmutableArray.CreateBuilder<CiCheck>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.GetString() is not { } id || !CiCheck.TryParse(id, out var check))
            {
                checks = [];
                return false;
            }

            builder.Add(check);
        }

        checks = builder.ToImmutable();
        return true;
    }

    private static string Bool(bool value) => value ? "true" : "false";
}

public enum CiOutcome
{
    Pass,
    Warn,
    Fail,
}

/// <summary>The verdict of the aggregate ci check, with the reasons for a warning or a failure.</summary>
public sealed record CiVerdict(CiOutcome Outcome, ImmutableArray<string> Messages);

/// <summary>The verdict of the ci job, with the plan and job results it read. The plan is null when it was not available.</summary>
public sealed record CiGateEvaluation(CiVerdict Verdict, CiPlan? Plan, ImmutableDictionary<string, string> Results);

public static class CiGate
{
    /// <summary>
    /// Evaluates the run. Every check that the plan ran must succeed. A merge candidate also needs every platform, from
    /// this run or an earlier run for the same head commit.
    /// </summary>
    /// <param name="plan">The plan of this run.</param>
    /// <param name="results">The result of each development.yml job, by job id.</param>
    public static CiVerdict Evaluate(CiPlan plan, IReadOnlyDictionary<string, string> results)
    {
        var failures = plan.Run
            .Select(check => check.WorkflowJob)
            .Distinct()
            .Select(job => (Job: job, Result: ResultOf(results, job)))
            .Where(job => job.Result != "success")
            .Select(job => $"Job `{job.Job}` was planned, but its result is {job.Result}.")
            .ToList();

        var missing = CiPlatform.All.Except(plan.Platforms).ToList();
        if (missing.Count == 0 || plan.MergeCandidate)
        {
            if (missing.Count > 0)
            {
                failures.Add($"{NotRun(plan, missing)} A pull request that is ready for review needs Linux, Windows and macOS: "
                    + $"add {AddLabels(missing)}. Checks that passed for this commit do not run again.");
            }

            return failures.Count > 0 ? new(CiOutcome.Fail, [.. failures]) : new(CiOutcome.Pass, []);
        }

        return failures.Count > 0
            ? new(CiOutcome.Fail, [.. failures])
            : new(CiOutcome.Warn, [$"{NotRun(plan, missing)} This run is not a merge candidate, so the ci check passes."]);
    }

    /// <summary>Evaluates the run from the <c>needs</c> context of the ci job, as JSON.</summary>
    public static CiGateEvaluation FromNeeds(string needsJson)
    {
        using var document = JsonDocument.Parse(needsJson);
        var needs = document.RootElement;
        var results = needs.EnumerateObject()
            .ToImmutableDictionary(job => job.Name, job => job.Value.GetProperty("result").GetString() ?? "missing");

        var planResult = ResultOf(results, "plan");
        if (planResult != "success")
        {
            return new(new(CiOutcome.Fail, [$"Job `plan` did not succeed: its result is {planResult}."]), null, results);
        }

        if (!needs.GetProperty("plan").GetProperty("outputs").TryGetProperty("plan", out var planOutput)
            || planOutput.GetString() is not { } planJson
            || !CiPlan.TryParse(planJson, out var plan))
        {
            return new(new(CiOutcome.Fail, ["Job `plan` wrote no valid plan output."]), null, results);
        }

        return new(Evaluate(plan, results), plan, results);
    }

    /// <summary>The labels that add the missing platforms, for example "`ci:macos` or `ci:full`".</summary>
    internal static string AddLabels(IReadOnlyCollection<CiPlatform> missing) =>
        missing.Count == CiPlatform.All.Length
            ? $"`{CiLabels.Full}`"
            : $"{string.Join(" and ", missing.Select(platform => $"`{platform.Label}`"))} or `{CiLabels.Full}`";

    internal static string ResultOf(IReadOnlyDictionary<string, string> results, string job) =>
        results.TryGetValue(job, out var result) ? result : "missing";

    private static string NotRun(CiPlan plan, IEnumerable<CiPlatform> missing) =>
        $"{string.Join(" and ", missing.Select(platform => platform.Check.JobName))} did not run ({plan.Reason}).";
}

/// <summary>The job summaries and annotations that show the selection of a run.</summary>
public static partial class CiReport
{
    /// <summary>The job summary of the plan job: the selection, why, and what happens to each check.</summary>
    public static string PlanSummary(CiPlan plan)
    {
        var text = new StringBuilder($"### CI plan: {plan.Selection}\n\n");
        text.Append($"Selected by: {Code(plan.Reason)}.\n\n");
        text.Append(plan.MergeCandidate
            ? "The `ci` check of this run needs Linux, Windows and macOS.\n\n"
            : "This run is not a merge candidate: the `ci` check passes with a warning when Windows or macOS is not selected.\n\n");
        text.Append("| Check | Selected | This run |\n| --- | --- | --- |\n");
        foreach (var check in CiCheck.All)
        {
            var state = plan.Run.Contains(check) ? "runs"
                : plan.ReusedFor(check) is { } evidence ? $"passed in {RunLink(evidence)}, not run again"
                : $"not selected: add {CiGate.AddLabels([.. CiPlatform.All.Where(platform => platform.Check == check)])}";
            text.Append($"| `{check.JobName}` | {(plan.Selected.Contains(check) ? "yes" : "no")} | {state} |\n");
        }

        text.Append(Legend);
        return text.ToString();
    }

    /// <summary>A one-line annotation for the run page.</summary>
    public static string PlanNotice(CiPlan plan)
    {
        static string List(IEnumerable<string> items) => items.Any() ? string.Join(", ", items) : "none";
        return $"{plan.Selection} ({plan.Reason}). Runs: {List(plan.Run.Select(check => check.JobName))}. "
            + $"Passed earlier: {List(plan.Reused.Select(item => $"{item.Check.JobName} (run {item.RunId})"))}. "
            + $"Not selected: {List(CiCheck.All.Except(plan.Selected).Select(check => check.JobName))}.";
    }

    /// <summary>The job summary of the ci job: the verdict, the selection, and the result of each check.</summary>
    public static string GateSummary(CiPlan? plan, CiVerdict verdict, IReadOnlyDictionary<string, string> results)
    {
        var text = new StringBuilder($"### ci: {verdict.Outcome}\n\n");
        foreach (var message in verdict.Messages) text.Append($"- {message}\n");
        if (verdict.Messages.Length > 0) text.Append('\n');
        if (plan is null) return text.ToString();

        text.Append($"Selection: {plan.Selection} ({Code(plan.Reason)}).\n\n");
        text.Append("| Check | Selected | Result |\n| --- | --- | --- |\n");
        foreach (var check in CiCheck.All)
        {
            var result = plan.Run.Contains(check)
                ? $"{CiGate.ResultOf(results, check.WorkflowJob)} in this run{(check.Runner is null ? "" : $" (job `{check.WorkflowJob}`)")}"
                : plan.ReusedFor(check) is { } evidence ? $"passed in {RunLink(evidence)}"
                : "not selected";
            text.Append($"| `{check.JobName}` | {(plan.Selected.Contains(check) ? "yes" : "no")} | {result} |\n");
        }

        text.Append(Legend);
        return text.ToString();
    }

    private const string Legend =
        "\nLinux always runs. Labels add platforms: `ci:windows`, `ci:macos`, `ci:full` (both) or `ci:linux-only` (neither). "
        + "Without a label, a draft runs Linux only and a pull request that is ready for review runs all three. "
        + "Checks that passed for this commit do not run again.\n";

    private static string RunLink(CiEvidence evidence) => $"[run {evidence.RunId}]({evidence.Url})";

    private static string Code(string reason) => LabelPattern().Replace(reason, "`$0`");

    [GeneratedRegex(@"\bci:[a-z-]+")]
    private static partial Regex LabelPattern();
}

/// <summary>What the CI request workflow does after a ci:* label change.</summary>
public abstract record CiRerunAction
{
    private CiRerunAction() { }

    /// <summary>There is no pull_request run for the head commit yet. Its run reads the labels when it starts.</summary>
    public static readonly CiRerunAction None = new NoRun();

    public sealed record NoRun : CiRerunAction;

    /// <summary>Re-run every job of the run. Its plan job reads the labels again and reuses the jobs that passed.</summary>
    public sealed record Rerun(long RunId) : CiRerunAction;

    /// <summary>The run has not completed, and GitHub re-runs only a completed run: cancel it, then re-run it.</summary>
    public sealed record CancelThenRerun(long RunId) : CiRerunAction;
}

/// <summary>
/// A label change re-runs the pull_request run of the head commit. GitHub counts the checks of a re-run for the pull
/// request; it does not count the checks of a workflow_dispatch run.
/// </summary>
public static class CiRerun
{
    /// <summary>Decides from a <c>GET .../actions/workflows/development.yml/runs?event=pull_request&amp;head_sha=...</c> response.</summary>
    public static CiRerunAction Decide(string runsJson)
    {
        using var document = JsonDocument.Parse(runsJson);
        var latest = document.RootElement.GetProperty("workflow_runs").EnumerateArray().FirstOrDefault();
        if (latest.ValueKind != JsonValueKind.Object) return CiRerunAction.None;

        var id = latest.GetProperty("id").GetInt64();
        return latest.GetProperty("status").GetString() == "completed"
            ? new CiRerunAction.Rerun(id)
            : new CiRerunAction.CancelThenRerun(id);
    }

    /// <summary>True when a <c>GET .../actions/runs/{id}</c> response reports a completed run.</summary>
    public static bool IsCompleted(string runJson)
    {
        using var document = JsonDocument.Parse(runJson);
        return document.RootElement.GetProperty("status").GetString() == "completed";
    }
}

/// <summary>A pull request as the plan needs it.</summary>
public sealed record PullRequestInfo(int Number, string HeadSha, CiTrigger.PullRequest Trigger);

/// <summary>Reads the GitHub event payload and API responses.</summary>
public static class GitHubCi
{
    /// <summary>The pull request of a pull_request event payload.</summary>
    public static PullRequestInfo PullRequestFromEvent(string eventJson)
    {
        using var document = JsonDocument.Parse(eventJson);
        return PullRequest(document.RootElement.GetProperty("pull_request"));
    }

    /// <summary>The pull request of a <c>GET /repos/{owner}/{repo}/pulls/{number}</c> response.</summary>
    public static PullRequestInfo PullRequestFromApi(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        return PullRequest(document.RootElement);
    }

    /// <summary>The ids of the runs in <c>GET .../actions/workflows/{file}/runs</c> responses, newest first.</summary>
    public static ImmutableArray<long> RunIds(IEnumerable<string> responses) =>
    [
        .. responses.SelectMany(response =>
        {
            using var document = JsonDocument.Parse(response);
            return document.RootElement.GetProperty("workflow_runs").EnumerateArray()
                .Select(run => run.GetProperty("id").GetInt64())
                .ToList();
        }),
    ];

    /// <summary>The jobs that succeeded in <c>GET .../actions/runs/{id}/jobs?filter=all</c> responses.</summary>
    public static ImmutableArray<CiEvidence> PassedChecks(IEnumerable<string> responses) =>
    [
        .. responses.SelectMany(response =>
        {
            using var document = JsonDocument.Parse(response);
            return document.RootElement.GetProperty("jobs").EnumerateArray()
                .Where(job => job.GetProperty("conclusion") is { ValueKind: JsonValueKind.String } conclusion
                    && conclusion.GetString() == "success")
                .Select(job => job.GetProperty("name").GetString() is { } name && CiCheck.TryFromJobName(name, out var check)
                    ? new CiEvidence(check, job.GetProperty("run_id").GetInt64(), job.GetProperty("html_url").GetString() ?? "")
                    : null)
                .OfType<CiEvidence>()
                .ToList();
        }),
    ];

    private static PullRequestInfo PullRequest(JsonElement pr) => new(
        pr.GetProperty("number").GetInt32(),
        pr.GetProperty("head").GetProperty("sha").GetString() ?? "",
        new CiTrigger.PullRequest(
            pr.GetProperty("draft").GetBoolean(),
            [.. pr.GetProperty("labels").EnumerateArray().Select(label => label.GetProperty("name").GetString() ?? "")]));
}
