// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The CI plan of development.yml: which platforms a run selects, which earlier passing jobs of the same head commit it
/// reuses, how the job summaries report the selection, and when the aggregate <c>ci</c> check fails.
/// </summary>
public class CiPlanTests
{
    private static readonly CiCheck[] Linux = [CiCheck.Lint, CiCheck.OpenSpec, CiCheck.TestLinux];

    private static CiTrigger.PullRequest Pr(bool draft, params string[] labels) => new(draft, [.. labels]);

    private static CiEvidence Passed(CiCheck check, long run = 100) => new(check, run, $"https://github.com/o/r/actions/runs/{run}/job/1");

    private static CiEvidence[] PassedLinux(long run = 100) => [.. Linux.Select(check => Passed(check, run))];

    [Test]
    public void TheChecksAreLintOpenSpecAndOneTestJobForEachOs() =>
        CiCheck.All.Select(check => check.JobName).Should().Equal(
            "lint", "OpenSpec workflow", "test (ubuntu-latest)", "test (windows-latest)", "test (macOS-latest)");

    [Test]
    public void TheOptionalPlatformsAreWindowsAndMacOs()
    {
        CiPlatform.All.Select(platform => platform.Label).Should().Equal(CiLabels.Windows, CiLabels.MacOS);
        CiPlatform.All.Select(platform => platform.Check).Should().Equal(CiCheck.TestWindows, CiCheck.TestMacOS);
    }

    [Test]
    public void ADraftPullRequestSelectsLinuxOnly()
    {
        var request = CiPolicy.Resolve(Pr(draft: true));
        request.Platforms.Should().BeEmpty();
        request.MergeCandidate.Should().BeFalse();
        request.ReuseEvidence.Should().BeTrue();
        request.Reason.Should().Be("draft pull request with no platform label");
    }

    [Test]
    public void APullRequestThatIsReadyForReviewSelectsEveryPlatform()
    {
        var request = CiPolicy.Resolve(Pr(draft: false));
        request.Platforms.Should().Equal(CiPlatform.All);
        request.MergeCandidate.Should().BeTrue();
        request.Reason.Should().Be("pull request ready for review with no platform label");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void APlatformLabelSelectsExactlyItsPlatformOnADraftAndOnAReadyPullRequest(bool draft)
    {
        var request = CiPolicy.Resolve(Pr(draft, CiLabels.Windows));
        request.Platforms.Should().Equal(CiPlatform.Windows);
        request.Reason.Should().Be("label ci:windows");
    }

    [Test]
    public void TheFullLabelSelectsBothPlatforms() =>
        CiPolicy.Resolve(Pr(draft: true, CiLabels.Full)).Platforms.Should().Equal(CiPlatform.All);

    [Test]
    public void TheLinuxOnlyLabelSelectsNoPlatformOnAReadyPullRequest()
    {
        var request = CiPolicy.Resolve(Pr(draft: false, CiLabels.LinuxOnly));
        request.Platforms.Should().BeEmpty();
        request.MergeCandidate.Should().BeTrue("the label does not make the pull request less ready to merge");
    }

    [Test]
    public void PlatformLabelsCombineAndTheLargerSelectionWins()
    {
        var request = CiPolicy.Resolve(Pr(draft: false, CiLabels.MacOS, CiLabels.LinuxOnly, CiLabels.Windows));
        request.Platforms.Should().Equal(CiPlatform.All);
        request.Reason.Should().Be("labels ci:linux-only, ci:macos, ci:windows");
    }

    [Test]
    public void OtherLabelsDoNotChangeTheSelection() =>
        CiPolicy.Resolve(Pr(draft: true, "dependencies", "ci")).Platforms.Should().BeEmpty();

    [Test]
    public void AReleaseCallSelectsEveryPlatformAndReusesNothing()
    {
        var request = CiPolicy.Resolve(new CiTrigger.Release());
        request.Platforms.Should().Equal(CiPlatform.All);
        request.MergeCandidate.Should().BeTrue();
        request.ReuseEvidence.Should().BeFalse();
    }

    [Test]
    public void AnOsInputOverridesThePullRequestLabels()
    {
        CiPolicy.TryParseOsChoice("macos", out var choice).Should().BeTrue();
        var request = CiPolicy.Resolve(new CiTrigger.Dispatch(choice!, Pr(draft: false, CiLabels.Windows)));
        request.Platforms.Should().Equal(CiPlatform.MacOS);
        request.MergeCandidate.Should().BeTrue();
        request.Reason.Should().Be("os input macos");
    }

    [Test]
    public void AnAutomaticDispatchForAPullRequestUsesItsStateAndLabels()
    {
        var request = CiPolicy.Resolve(new CiTrigger.Dispatch(OsChoice.Auto, Pr(draft: true, CiLabels.MacOS)));
        request.Platforms.Should().Equal(CiPlatform.MacOS);
        request.MergeCandidate.Should().BeFalse();
    }

    [Test]
    public void AnAutomaticDispatchWithoutAPullRequestSelectsEveryPlatformAndIsNotAMergeCandidate()
    {
        var request = CiPolicy.Resolve(new CiTrigger.Dispatch(OsChoice.Auto, null));
        request.Platforms.Should().Equal(CiPlatform.All);
        request.MergeCandidate.Should().BeFalse();
    }

    [Test]
    [Arguments("", true, new string[0])]
    [Arguments("auto", true, new string[0])]
    [Arguments("linux", false, new string[0])]
    [Arguments("full", false, new[] { "windows", "macos" })]
    [Arguments("windows", false, new[] { "windows" })]
    [Arguments("macos,windows", false, new[] { "windows", "macos" })]
    [Arguments(" windows , macos ", false, new[] { "windows", "macos" })]
    public void AnOsChoiceParsesFromTheDispatchInput(string input, bool auto, string[] platforms)
    {
        CiPolicy.TryParseOsChoice(input, out var choice).Should().BeTrue();
        if (auto)
        {
            choice.Should().Be(OsChoice.Auto);
        }
        else
        {
            choice.Should().BeOfType<OsChoice.Platforms>()
                .Which.Selected.Select(platform => platform.Id).Should().Equal(platforms);
        }
    }

    [Test]
    [Arguments("Windows")]
    [Arguments("linux,windows")]
    [Arguments("windows,")]
    [Arguments("solaris")]
    public void AnUnknownOsChoiceDoesNotParse(string input) =>
        CiPolicy.TryParseOsChoice(input, out _).Should().BeFalse();

    [Test]
    public void APlanRunsTheSelectedChecksThatHaveNotPassed()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), PassedLinux());
        plan.Selected.Should().Equal(CiCheck.All);
        plan.Reused.Select(evidence => evidence.Check).Should().Equal(Linux);
        plan.Run.Should().Equal(CiCheck.TestWindows, CiCheck.TestMacOS);
    }

    [Test]
    public void APlanReusesTheFirstPassingRunOfACheck()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: true)), [Passed(CiCheck.Lint, 300), Passed(CiCheck.Lint, 200)]);
        plan.Reused.Should().ContainSingle().Which.RunId.Should().Be(300);
    }

    [Test]
    public void APlanThatMustNotReuseEvidenceRunsEverySelectedCheck() =>
        CiPlan.Create(CiPolicy.Resolve(new CiTrigger.Release()), PassedLinux()).Run.Should().Equal(CiCheck.All);

    [Test]
    public void APlanForOnePlatformRunsLinuxAndThatPlatform() =>
        CiPlan.Create(CiPolicy.Resolve(Pr(draft: true, CiLabels.Windows)), []).Run
            .Should().Equal(CiCheck.Lint, CiCheck.OpenSpec, CiCheck.TestLinux, CiCheck.TestWindows);

    [Test]
    public void ThePlanOutputsSelectTheWorkflowJobsAndTheMatrix()
    {
        var outputs = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), [Passed(CiCheck.Lint), Passed(CiCheck.TestLinux)]).Outputs();
        outputs["lint"].Should().Be("false");
        outputs["openspec"].Should().Be("true");
        outputs["test"].Should().Be("true");
        outputs["matrix"].Should().Be("""["windows-latest","macOS-latest"]""");
        outputs["selection"].Should().Be("Linux, Windows, macOS");
    }

    [Test]
    public void AnEmptyMatrixTurnsTheTestJobOff()
    {
        var outputs = CiPlan.Create(CiPolicy.Resolve(Pr(draft: true)), PassedLinux()).Outputs();
        outputs["test"].Should().Be("false");
        outputs["matrix"].Should().Be("[]");
        outputs["selection"].Should().Be("Linux");
    }

    [Test]
    public void APlanRoundTripsThroughItsOutput()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false, CiLabels.MacOS)), [Passed(CiCheck.Lint, 42)]);
        CiPlan.TryParse(plan.Outputs()["plan"], out var parsed).Should().BeTrue();
        parsed.Should().BeEquivalentTo(plan);
    }

    [Test]
    public void ThePlanSummaryNamesTheSelectionItsSourceAndEachCheck()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false, CiLabels.Windows)), [Passed(CiCheck.Lint, 42)]);
        var summary = CiReport.PlanSummary(plan);
        summary.Should().StartWith("### CI plan: Linux, Windows\n");
        summary.Should().Contain("Selected by: label `ci:windows`.");
        summary.Should().Contain("needs Linux, Windows and macOS");
        summary.Should().Contain("| `lint` | yes | passed in [run 42](https://github.com/o/r/actions/runs/42/job/1), not run again |");
        summary.Should().Contain("| `test (windows-latest)` | yes | runs |");
        summary.Should().Contain("| `test (macOS-latest)` | no | not selected: add `ci:macos` or `ci:full` |");
    }

    [Test]
    public void ThePlanNoticeIsOneLine()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: true)), [Passed(CiCheck.Lint, 42)]);
        CiReport.PlanNotice(plan).Should().Be(
            "Linux (draft pull request with no platform label). Runs: OpenSpec workflow, test (ubuntu-latest). Passed earlier: lint (run 42). Not selected: test (windows-latest), test (macOS-latest).");
    }

    [Test]
    public void ThePassedChecksAreTheSuccessfulJobsOfOtherRuns()
    {
        const string jobs = """
            {"jobs":[
              {"name":"lint","conclusion":"success","run_id":7,"html_url":"https://x/7/lint"},
              {"name":"test (ubuntu-latest)","conclusion":"success","run_id":7,"html_url":"https://x/7/ubuntu"},
              {"name":"test (windows-latest)","conclusion":"failure","run_id":7,"html_url":"https://x/7/windows"},
              {"name":"test (macOS-latest)","conclusion":null,"run_id":7,"html_url":"https://x/7/macos"},
              {"name":"ci","conclusion":"success","run_id":7,"html_url":"https://x/7/ci"}
            ]}
            """;
        GitHubCi.PassedChecks([jobs]).Should().BeEquivalentTo(
            [new CiEvidence(CiCheck.Lint, 7, "https://x/7/lint"), new CiEvidence(CiCheck.TestLinux, 7, "https://x/7/ubuntu")]);
    }

    [Test]
    public void APullRequestStateParsesFromTheEventPayload()
    {
        const string payload = """
            {"pull_request":{"number":7,"draft":true,"head":{"sha":"abc"},"labels":[{"name":"ci:full"},{"name":"dependencies"}]}}
            """;
        var state = GitHubCi.PullRequestFromEvent(payload);
        state.Number.Should().Be(7);
        state.HeadSha.Should().Be("abc");
        state.Trigger.Should().BeEquivalentTo(Pr(draft: true, "ci:full", "dependencies"));
    }

    [Test]
    public void TheGatePassesWhenEveryPlatformPassedOrWasReused()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), PassedLinux());
        var verdict = CiGate.Evaluate(plan, Results(lint: "skipped", openspec: "skipped", test: "success"));
        verdict.Outcome.Should().Be(CiOutcome.Pass);
    }

    [Test]
    [Arguments("failure")]
    [Arguments("cancelled")]
    [Arguments("skipped")]
    public void TheGateFailsWhenAPlannedJobDidNotSucceed(string result)
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: true)), []);
        CiGate.Evaluate(plan, Results(lint: "success", openspec: "success", test: result)).Outcome.Should().Be(CiOutcome.Fail);
    }

    [Test]
    public void TheGateFailsAMergeCandidateWithoutEveryPlatformAndNamesTheMissingLabel()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false, CiLabels.Windows)), []);
        var verdict = CiGate.Evaluate(plan, Results(lint: "success", openspec: "success", test: "success"));
        verdict.Outcome.Should().Be(CiOutcome.Fail);
        verdict.Messages.Should().ContainSingle().Which.Should().Contain("test (macOS-latest)").And.Contain("`ci:macos`")
            .And.NotContain("test (windows-latest)");
    }

    [Test]
    public void TheGateWarnsOnADraftWithoutEveryPlatform()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: true)), []);
        CiGate.Evaluate(plan, Results(lint: "success", openspec: "success", test: "success")).Outcome.Should().Be(CiOutcome.Warn);
    }

    [Test]
    public void TheGateSummaryShowsTheResultOfEachCheck()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: true, CiLabels.Windows)), [Passed(CiCheck.Lint, 42)]);
        var results = Results(lint: "skipped", openspec: "success", test: "failure");
        var summary = CiReport.GateSummary(plan, CiGate.Evaluate(plan, results), results);
        summary.Should().StartWith("### ci: Fail\n");
        summary.Should().Contain("Selection: Linux, Windows (label `ci:windows`).");
        summary.Should().Contain("| `lint` | yes | passed in [run 42](https://github.com/o/r/actions/runs/42/job/1) |");
        summary.Should().Contain("| `OpenSpec workflow` | yes | success in this run |");
        summary.Should().Contain("| `test (windows-latest)` | yes | failure in this run (job `test`) |");
        summary.Should().Contain("| `test (macOS-latest)` | no | not selected |");
    }

    [Test]
    public void TheGateFailsWhenThePlanJobDidNotSucceed() =>
        CiGate.FromNeeds("""{"plan":{"result":"failure","outputs":{}}}""").Verdict.Outcome.Should().Be(CiOutcome.Fail);

    [Test]
    public void TheGateReadsThePlanAndResultsFromTheNeedsContext()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), PassedLinux());
        var planJson = JsonSerializer.Serialize(plan.Outputs()["plan"]);
        var needs = "{\"plan\":{\"result\":\"success\",\"outputs\":{\"plan\":" + planJson + "}},"
            + "\"lint\":{\"result\":\"skipped\",\"outputs\":{}},"
            + "\"openspec\":{\"result\":\"skipped\",\"outputs\":{}},"
            + "\"test\":{\"result\":\"success\",\"outputs\":{}}}";
        var evaluation = CiGate.FromNeeds(needs);
        evaluation.Verdict.Outcome.Should().Be(CiOutcome.Pass);
        evaluation.Plan.Should().BeEquivalentTo(plan);
        evaluation.Results["test"].Should().Be("success");
    }

    private static Dictionary<string, string> Results(string lint, string openspec, string test) =>
        new() { ["lint"] = lint, ["openspec"] = openspec, ["test"] = test };
}
