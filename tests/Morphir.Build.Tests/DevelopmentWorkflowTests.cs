// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;
using YamlDotNet.RepresentationModel;

namespace Morphir.Build.Tests;

/// <summary>
/// development.yml takes its OS matrix and its check selection from the Fallout target CIPlan, and its aggregate ci
/// check from CIGate. ci-request.yml starts a run when a ci:* label changes.
/// </summary>
[Category("PlatformSpecific")]
public class DevelopmentWorkflowTests
{
    [Test]
    [Arguments("lint")]
    [Arguments("openspec")]
    [Arguments("test")]
    public void EveryCheckJobRunsOnlyWhenThePlanSelectsIt(string job)
    {
        var definition = Job(Development(), job);
        definition["needs"].ToString().Should().Be("plan");
        definition["if"].ToString().Should().Be($"needs.plan.outputs.{job} == 'true'");
    }

    [Test]
    public void TheTestMatrixComesFromThePlan() =>
        Job(Development(), "test")["strategy"]["matrix"]["os"].ToString().Should().Be("${{ fromJSON(needs.plan.outputs.matrix) }}");

    [Test]
    public void ThePlanJobRunsTheCIPlanTarget()
    {
        var plan = Job(Development(), "plan");
        Steps(plan).Should().Contain(step => Run(step).StartsWith("./build.sh --target CIPlan", StringComparison.Ordinal));
        foreach (var output in new[] { "lint", "openspec", "test", "matrix", "selection", "plan" })
        {
            plan["outputs"][output].ToString().Should().Be($"${{{{ steps.plan.outputs.{output} }}}}");
        }
    }

    [Test]
    public void TheCiJobRunsTheCIGateTargetOnTheNeedsOfEveryJob()
    {
        var ci = Job(Development(), "ci");
        ((YamlSequenceNode)ci["needs"]).Children.Select(node => node.ToString()).Should().BeEquivalentTo("plan", "lint", "test", "openspec");
        ci["if"].ToString().Should().Be("${{ !cancelled() }}", "a skipped or failed job must reach the gate");
        var gate = Steps(ci).Single(step => Run(step) == "./build.sh --target CIGate");
        gate["env"]["CI_NEEDS"].ToString().Should().Be("${{ toJSON(needs) }}");
    }

    [Test]
    public void TheJobNamesMatchTheChecksOfThePlan()
    {
        var workflow = Development();
        Job(workflow, "lint").Children.Should().NotContainKey(new YamlScalarNode("name"), "the jobs API reports the job id");
        Job(workflow, "openspec")["name"].ToString().Should().Be(CiCheck.OpenSpec.JobName);
        CiCheck.Lint.JobName.Should().Be("lint");
        CiCheck.All.Where(check => check.Runner is not null)
            .Should().OnlyContain(check => check.JobName == $"test ({check.Runner})");
    }

    [Test]
    public void TheOsInputIsFreeTextSoItCanListPlatforms()
    {
        var os = Development()["on"]["workflow_dispatch"]["inputs"]["os"];
        os["type"].ToString().Should().Be("string");
        os["default"].ToString().Should().Be("auto");
    }

    [Test]
    public void APullRequestThatBecomesReadyForReviewStartsARun() =>
        ((YamlSequenceNode)Development()["on"]["pull_request"]["types"]).Children.Select(node => node.ToString())
            .Should().Contain("ready_for_review").And.NotContain("edited").And.NotContain("labeled");

    [Test]
    public void TheCiRequestWorkflowRerunsOnlyForCiLabelsAndRunsNoPullRequestCode()
    {
        var workflow = Read("ci-request.yml");
        workflow["on"].Should().BeOfType<YamlMappingNode>().Which.Children.Keys.Select(key => key.ToString())
            .Should().Equal("pull_request_target");
        var rerun = Job(workflow, "rerun");
        rerun["if"].ToString().Should().Contain($"startsWith(github.event.label.name, '{CiLabels.Prefix}')");
        ((YamlMappingNode)rerun["permissions"]).Children.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToString())
            .Should().BeEquivalentTo(new Dictionary<string, string> { ["actions"] = "write", ["contents"] = "read" });

        var checkout = Steps(rerun).Single(step => Value(step, "uses").StartsWith("actions/checkout", StringComparison.Ordinal));
        checkout["with"]["ref"].ToString().Should().Be("${{ github.event.pull_request.base.sha }}", "pull_request_target must not run pull request code");
        checkout["with"]["persist-credentials"].ToString().Should().Be("false");
        Steps(rerun).Should().Contain(step => Run(step) == "./build.sh --target CIRequest");
    }

    [Test]
    public void ManualRunsHaveNoPullRequestInput() =>
        ((YamlMappingNode)Development()["on"]["workflow_dispatch"]["inputs"]).Children.Keys.Select(key => key.ToString())
            .Should().Equal("os");

    private static YamlMappingNode Development() => Read("development.yml");

    private static YamlMappingNode Read(string name)
    {
        using var reader = File.OpenText(Path.Combine(TestFixture.GetRepositoryRoot(), ".github", "workflows", name));
        var yaml = new YamlStream();
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    private static YamlMappingNode Job(YamlMappingNode workflow, string name) => (YamlMappingNode)workflow["jobs"][name];

    private static List<YamlMappingNode> Steps(YamlMappingNode job) =>
        ((YamlSequenceNode)job["steps"]).Children.Cast<YamlMappingNode>().ToList();

    private static string Value(YamlMappingNode step, string key) =>
        step.Children.TryGetValue(key, out var value) ? value.ToString() : "";

    private static string Run(YamlMappingNode step) => Value(step, "run");
}
