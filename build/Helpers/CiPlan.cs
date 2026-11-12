// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Morphir.Build.Helpers;

// The CI plan of .github/workflows/development.yml. This file is pure: it decides which checks a run selects, which
// passing jobs of earlier runs for the same head commit it reuses, and the verdict of the aggregate ci check. The
// Fallout targets CIPlan and CIGate in Build.CI.cs read the GitHub event and API and write the step outputs.

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

    /// <summary>Every check, in the order of the plan outputs and the job summary.</summary>
    public static readonly ImmutableArray<CiCheck> All = [Lint, OpenSpec, TestLinux, TestWindows, TestMacOS];

    /// <summary>The checks that run on Linux only. Windows and macOS are the expensive runners.</summary>
    public static readonly ImmutableArray<CiCheck> LinuxOnly = [Lint, OpenSpec, TestLinux];

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

/// <summary>The pull request labels that select the OS matrix.</summary>
public static class CiLabels
{
    /// <summary>Run Windows and macOS too, also on a draft.</summary>
    public const string Full = "ci:full";

    /// <summary>Run Linux only, also on a pull request that is ready for review.</summary>
    public const string LinuxOnly = "ci:linux-only";

    public const string Prefix = "ci:";
}

/// <summary>Which operating systems a run tests on.</summary>
public enum OsSelection
{
    Linux,
    Full,
}

/// <summary>The <c>os</c> input of a manual or label-dispatched run.</summary>
public enum OsChoice
{
    /// <summary>Use the pull request state and labels, or the full matrix when there is no pull request.</summary>
    Auto,
    Linux,
    Full,
}

/// <summary>What started the run.</summary>
public abstract record CiTrigger
{
    private CiTrigger() { }

    /// <summary>A pull request event, or the pull request state that the GitHub API reported.</summary>
    public sealed record PullRequest(bool Draft, ImmutableSortedSet<string> Labels) : CiTrigger;

    /// <summary>A workflow_dispatch run, for a pull request or for a branch only.</summary>
    public sealed record Dispatch(OsChoice Choice, PullRequest? PullRequestState) : CiTrigger;

    /// <summary>A workflow_call from a release. It tests the full matrix again and reuses nothing.</summary>
    public sealed record Release : CiTrigger;
}

/// <summary>The policy of a run, before it looks at earlier passing jobs.</summary>
/// <param name="Selection">The operating systems that the run selects.</param>
/// <param name="MergeCandidate">True when the full matrix is necessary for the ci check to pass.</param>
/// <param name="ReuseEvidence">True when passing jobs of earlier runs for the same head commit count.</param>
/// <param name="Reason">Why the run selected these operating systems, for the job summary.</param>
public sealed record CiRequest(OsSelection Selection, bool MergeCandidate, bool ReuseEvidence, string Reason);

public static class CiPolicy
{
    public static CiRequest Resolve(CiTrigger trigger) => trigger switch
    {
        CiTrigger.PullRequest pr => ForPullRequest(pr),
        CiTrigger.Dispatch { Choice: OsChoice.Auto, PullRequestState: { } pr } => ForPullRequest(pr),
        CiTrigger.Dispatch { Choice: OsChoice.Auto } =>
            new(OsSelection.Full, MergeCandidate: false, ReuseEvidence: true, "manual run with os=auto and no pull request"),
        CiTrigger.Dispatch dispatch => new(
            dispatch.Choice == OsChoice.Linux ? OsSelection.Linux : OsSelection.Full,
            MergeCandidate: dispatch.PullRequestState is { Draft: false },
            ReuseEvidence: true,
            $"manual run with os={Format(dispatch.Choice)}"),
        CiTrigger.Release => new(OsSelection.Full, MergeCandidate: true, ReuseEvidence: false, "release call"),
        _ => throw new UnreachableException(),
    };

    public static bool TryParseOsChoice(string input, out OsChoice choice)
    {
        (var parsed, choice) = input switch
        {
            "" or "auto" => (true, OsChoice.Auto),
            "linux" => (true, OsChoice.Linux),
            "full" => (true, OsChoice.Full),
            _ => (false, OsChoice.Auto),
        };
        return parsed;
    }

    private static CiRequest ForPullRequest(CiTrigger.PullRequest pr)
    {
        var mergeCandidate = !pr.Draft;
        return pr switch
        {
            _ when pr.Labels.Contains(CiLabels.Full) =>
                new(OsSelection.Full, mergeCandidate, ReuseEvidence: true, $"label {CiLabels.Full}"),
            _ when pr.Labels.Contains(CiLabels.LinuxOnly) =>
                new(OsSelection.Linux, mergeCandidate, ReuseEvidence: true, $"label {CiLabels.LinuxOnly}"),
            { Draft: true } => new(OsSelection.Linux, mergeCandidate, ReuseEvidence: true, "draft pull request"),
            _ => new(OsSelection.Full, mergeCandidate, ReuseEvidence: true, "pull request ready for review"),
        };
    }

    private static string Format(OsChoice choice) => choice.ToString().ToLowerInvariant();
}

/// <summary>The checks that a run selects, reuses and runs.</summary>
public sealed record CiPlan(
    OsSelection Selection,
    bool MergeCandidate,
    string Reason,
    ImmutableArray<CiCheck> Selected,
    ImmutableArray<CiCheck> Reused,
    ImmutableArray<CiCheck> Run)
{
    public static CiPlan Create(CiRequest request, IEnumerable<CiCheck> passed)
    {
        var selected = request.Selection == OsSelection.Full ? CiCheck.All : CiCheck.LinuxOnly;
        var evidence = request.ReuseEvidence ? passed.ToImmutableHashSet() : [];
        return new(
            request.Selection,
            request.MergeCandidate,
            request.Reason,
            selected,
            [.. selected.Where(evidence.Contains)],
            [.. selected.Where(check => !evidence.Contains(check))]);
    }

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
            if (!Enum.TryParse<OsSelection>(root.GetProperty("selection").GetString(), out var selection)
                || !TryChecks(root.GetProperty("selected"), out var selected)
                || !TryChecks(root.GetProperty("reused"), out var reused)
                || !TryChecks(root.GetProperty("run"), out var run))
            {
                return false;
            }

            plan = new(selection, root.GetProperty("mergeCandidate").GetBoolean(), root.GetProperty("reason").GetString() ?? "",
                selected, reused, run);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A Markdown table for the job summary: what runs, what an earlier run passed, what is not selected.</summary>
    public string Summary()
    {
        var text = new StringBuilder($"### CI plan\n\nSelection: **{Selection}** ({Reason}).");
        text.Append(MergeCandidate ? " The ci check needs the full matrix.\n\n" : " This run is not a merge candidate.\n\n");
        text.Append("| Check | This run |\n| --- | --- |\n");
        foreach (var check in CiCheck.All)
        {
            var state = Run.Contains(check) ? "runs"
                : Reused.Contains(check) ? "passed in an earlier run for this commit"
                : "not selected";
            text.Append($"| `{check.JobName}` | {state} |\n");
        }

        if (Selection == OsSelection.Linux)
        {
            text.Append($"\nAdd the `{CiLabels.Full}` label to run Windows and macOS. Checks that passed for this commit do not run again.\n");
        }

        return text.ToString();
    }

    private string Serialize() => JsonSerializer.Serialize(new
    {
        selection = Selection.ToString(),
        mergeCandidate = MergeCandidate,
        reason = Reason,
        selected = Selected.Select(check => check.Id),
        reused = Reused.Select(check => check.Id),
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

public static class CiGate
{
    /// <summary>
    /// Evaluates the run. Every check that the plan ran must succeed. A merge candidate also needs every check of the
    /// full matrix, from this run or an earlier run for the same head commit.
    /// </summary>
    /// <param name="plan">The plan of this run.</param>
    /// <param name="results">The result of each development.yml job, by job id.</param>
    public static CiVerdict Evaluate(CiPlan plan, IReadOnlyDictionary<string, string> results)
    {
        var failures = plan.Run
            .Select(check => check.WorkflowJob)
            .Distinct()
            .Select(job => (Job: job, Result: results.TryGetValue(job, out var result) ? result : "missing"))
            .Where(job => job.Result != "success")
            .Select(job => $"Job `{job.Job}` was planned but its result is {job.Result}.")
            .ToList();

        var missing = CiCheck.All.Except(plan.Selected).Select(check => check.JobName).ToList();
        if (missing.Count > 0)
        {
            var notRun = $"{string.Join(" and ", missing)} did not run ({plan.Reason}).";
            if (plan.MergeCandidate)
            {
                failures.Add($"{notRun} A pull request that is ready for review needs the full matrix: add the `{CiLabels.Full}` label "
                    + $"or remove `{CiLabels.LinuxOnly}`. Checks that passed for this commit do not run again.");
            }
            else
            {
                return failures.Count > 0
                    ? new(CiOutcome.Fail, [.. failures])
                    : new(CiOutcome.Warn, [$"{notRun} This run is not a merge candidate, so the ci check passes."]);
            }
        }

        return failures.Count > 0 ? new(CiOutcome.Fail, [.. failures]) : new(CiOutcome.Pass, []);
    }

    /// <summary>Evaluates the run from the <c>needs</c> context of the ci job, as JSON.</summary>
    public static CiVerdict FromNeeds(string needsJson)
    {
        using var document = JsonDocument.Parse(needsJson);
        var needs = document.RootElement;
        var planJob = needs.GetProperty("plan");
        var planResult = planJob.GetProperty("result").GetString();
        if (planResult != "success")
        {
            return new(CiOutcome.Fail, [$"Job `plan` did not succeed: its result is {planResult}."]);
        }

        if (!planJob.GetProperty("outputs").TryGetProperty("plan", out var planOutput)
            || planOutput.GetString() is not { } planJson
            || !CiPlan.TryParse(planJson, out var plan))
        {
            return new(CiOutcome.Fail, ["Job `plan` wrote no valid plan output."]);
        }

        var results = needs.EnumerateObject()
            .Where(job => job.Name != "plan")
            .ToImmutableDictionary(job => job.Name, job => job.Value.GetProperty("result").GetString() ?? "missing");
        return Evaluate(plan, results);
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

    /// <summary>The ids of the runs in <c>GET .../actions/workflows/{file}/runs</c> responses.</summary>
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

    /// <summary>The checks that succeeded in <c>GET .../actions/runs/{id}/jobs?filter=latest</c> responses.</summary>
    public static ImmutableHashSet<CiCheck> PassedChecks(IEnumerable<string> responses) =>
    [
        .. responses.SelectMany(response =>
        {
            using var document = JsonDocument.Parse(response);
            return document.RootElement.GetProperty("jobs").EnumerateArray()
                .Where(job => job.GetProperty("conclusion") is { ValueKind: JsonValueKind.String } conclusion
                    && conclusion.GetString() == "success")
                .Select(job => job.GetProperty("name").GetString())
                .Select(name => name is not null && CiCheck.TryFromJobName(name, out var check) ? check : null)
                .OfType<CiCheck>()
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
