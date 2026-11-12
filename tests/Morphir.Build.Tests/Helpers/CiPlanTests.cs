// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The CI plan of development.yml: which checks a run selects, which earlier passing jobs of the same head commit it
/// reuses, and when the aggregate <c>ci</c> check fails because Windows and macOS did not run.
/// </summary>
public class CiPlanTests
{
    private static readonly CiCheck[] Linux = [CiCheck.Lint, CiCheck.OpenSpec, CiCheck.TestLinux];

    private static CiTrigger.PullRequest Pr(bool draft, params string[] labels) => new(draft, [.. labels]);

    [Test]
    public void TheChecksAreLintOpenSpecAndOneTestJobForEachOs() =>
        CiCheck.All.Select(check => check.JobName).Should().Equal(
            "lint", "OpenSpec workflow", "test (ubuntu-latest)", "test (windows-latest)", "test (macOS-latest)");

    [Test]
    public void ADraftPullRequestSelectsLinuxOnly()
    {
        var request = CiPolicy.Resolve(Pr(draft: true));
        request.Selection.Should().Be(OsSelection.Linux);
        request.MergeCandidate.Should().BeFalse();
        request.ReuseEvidence.Should().BeTrue();
    }

    [Test]
    public void APullRequestThatIsReadyForReviewSelectsTheFullMatrix()
    {
        var request = CiPolicy.Resolve(Pr(draft: false));
        request.Selection.Should().Be(OsSelection.Full);
        request.MergeCandidate.Should().BeTrue();
    }

    [Test]
    public void TheFullLabelSelectsTheFullMatrixOnADraft() =>
        CiPolicy.Resolve(Pr(draft: true, CiLabels.Full)).Selection.Should().Be(OsSelection.Full);

    [Test]
    public void TheLinuxOnlyLabelSelectsLinuxOnAPullRequestThatIsReady()
    {
        var request = CiPolicy.Resolve(Pr(draft: false, CiLabels.LinuxOnly));
        request.Selection.Should().Be(OsSelection.Linux);
        request.MergeCandidate.Should().BeTrue("the label does not make the pull request less ready to merge");
    }

    [Test]
    public void TheFullLabelWinsWhenBothLabelsAreSet() =>
        CiPolicy.Resolve(Pr(draft: false, CiLabels.LinuxOnly, CiLabels.Full)).Selection.Should().Be(OsSelection.Full);

    [Test]
    public void OtherLabelsDoNotChangeTheSelection() =>
        CiPolicy.Resolve(Pr(draft: true, "dependencies")).Selection.Should().Be(OsSelection.Linux);

    [Test]
    public void AReleaseCallSelectsTheFullMatrixAndReusesNothing()
    {
        var request = CiPolicy.Resolve(new CiTrigger.Release());
        request.Selection.Should().Be(OsSelection.Full);
        request.MergeCandidate.Should().BeTrue();
        request.ReuseEvidence.Should().BeFalse();
    }

    [Test]
    [Arguments(OsChoice.Linux, OsSelection.Linux)]
    [Arguments(OsChoice.Full, OsSelection.Full)]
    public void ADispatchInputOverridesThePullRequestState(OsChoice choice, OsSelection expected) =>
        CiPolicy.Resolve(new CiTrigger.Dispatch(choice, Pr(draft: false, CiLabels.Full))).Selection.Should().Be(expected);

    [Test]
    public void AnAutomaticDispatchForAPullRequestUsesItsStateAndLabels()
    {
        var request = CiPolicy.Resolve(new CiTrigger.Dispatch(OsChoice.Auto, Pr(draft: true, CiLabels.Full)));
        request.Selection.Should().Be(OsSelection.Full);
        request.MergeCandidate.Should().BeFalse();
    }

    [Test]
    public void ADispatchWithoutAPullRequestIsNotAMergeCandidate()
    {
        var request = CiPolicy.Resolve(new CiTrigger.Dispatch(OsChoice.Auto, null));
        request.Selection.Should().Be(OsSelection.Full);
        request.MergeCandidate.Should().BeFalse();
    }

    [Test]
    [Arguments("", OsChoice.Auto)]
    [Arguments("auto", OsChoice.Auto)]
    [Arguments("linux", OsChoice.Linux)]
    [Arguments("full", OsChoice.Full)]
    public void AnOsChoiceParsesFromTheDispatchInput(string input, OsChoice expected)
    {
        CiPolicy.TryParseOsChoice(input, out var choice).Should().BeTrue();
        choice.Should().Be(expected);
    }

    [Test]
    [Arguments("windows")]
    [Arguments("Linux")]
    public void AnUnknownOsChoiceDoesNotParse(string input) =>
        CiPolicy.TryParseOsChoice(input, out _).Should().BeFalse();

    [Test]
    public void APlanRunsTheSelectedChecksThatHaveNotPassed()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), Linux);
        plan.Selected.Should().Equal(CiCheck.All);
        plan.Reused.Should().Equal(Linux);
        plan.Run.Should().Equal(CiCheck.TestWindows, CiCheck.TestMacOS);
    }

    [Test]
    public void APlanThatMustNotReuseEvidenceRunsEverySelectedCheck() =>
        CiPlan.Create(CiPolicy.Resolve(new CiTrigger.Release()), Linux).Run.Should().Equal(CiCheck.All);

    [Test]
    public void APlanDoesNotRunAWindowsOrMacOsCheckThatItDidNotSelect() =>
        CiPlan.Create(CiPolicy.Resolve(Pr(draft: true)), []).Run.Should().Equal(Linux);

    [Test]
    public void ThePlanOutputsSelectTheWorkflowJobsAndTheMatrix()
    {
        var outputs = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), [CiCheck.Lint, CiCheck.TestLinux]).Outputs();
        outputs["lint"].Should().Be("false");
        outputs["openspec"].Should().Be("true");
        outputs["test"].Should().Be("true");
        outputs["matrix"].Should().Be("""["windows-latest","macOS-latest"]""");
    }

    [Test]
    public void AnEmptyMatrixTurnsTheTestJobOff()
    {
        var outputs = CiPlan.Create(CiPolicy.Resolve(Pr(draft: true)), Linux).Outputs();
        outputs["test"].Should().Be("false");
        outputs["matrix"].Should().Be("[]");
    }

    [Test]
    public void APlanRoundTripsThroughItsOutput()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), [CiCheck.Lint]);
        CiPlan.TryParse(plan.Outputs()["plan"], out var parsed).Should().BeTrue();
        parsed.Should().BeEquivalentTo(plan);
    }

    [Test]
    public void ThePassedChecksAreTheSuccessfulJobsOfOtherRuns()
    {
        const string jobs = """
            {"jobs":[
              {"name":"lint","conclusion":"success"},
              {"name":"test (ubuntu-latest)","conclusion":"success"},
              {"name":"test (windows-latest)","conclusion":"failure"},
              {"name":"test (macOS-latest)","conclusion":null},
              {"name":"ci","conclusion":"success"}
            ]}
            """;
        GitHubCi.PassedChecks([jobs]).Should().BeEquivalentTo([CiCheck.Lint, CiCheck.TestLinux]);
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
    public void TheGatePassesWhenTheFullMatrixPassedOrWasReused()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), Linux);
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
    public void TheGateFailsAMergeCandidateWithoutWindowsAndMacOs()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false, CiLabels.LinuxOnly)), []);
        var verdict = CiGate.Evaluate(plan, Results(lint: "success", openspec: "success", test: "success"));
        verdict.Outcome.Should().Be(CiOutcome.Fail);
        verdict.Messages.Should().ContainSingle().Which.Should().Contain(CiLabels.Full);
    }

    [Test]
    public void TheGateWarnsOnADraftWithoutWindowsAndMacOs()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: true)), []);
        CiGate.Evaluate(plan, Results(lint: "success", openspec: "success", test: "success")).Outcome.Should().Be(CiOutcome.Warn);
    }

    [Test]
    public void TheGateFailsWhenThePlanJobDidNotSucceed() =>
        CiGate.FromNeeds("""{"plan":{"result":"failure","outputs":{}}}""").Outcome.Should().Be(CiOutcome.Fail);

    [Test]
    public void TheGateReadsThePlanAndResultsFromTheNeedsContext()
    {
        var plan = CiPlan.Create(CiPolicy.Resolve(Pr(draft: false)), Linux);
        var planJson = System.Text.Json.JsonSerializer.Serialize(plan.Outputs()["plan"]);
        var needs = "{\"plan\":{\"result\":\"success\",\"outputs\":{\"plan\":" + planJson + "}},"
            + "\"lint\":{\"result\":\"skipped\",\"outputs\":{}},"
            + "\"openspec\":{\"result\":\"skipped\",\"outputs\":{}},"
            + "\"test\":{\"result\":\"success\",\"outputs\":{}}}";
        CiGate.FromNeeds(needs).Outcome.Should().Be(CiOutcome.Pass);
    }

    private static Dictionary<string, string> Results(string lint, string openspec, string test) =>
        new() { ["lint"] = lint, ["openspec"] = openspec, ["test"] = test };
}
