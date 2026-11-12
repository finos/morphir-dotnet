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
        foreach (var output in new[] { "lint", "openspec", "test", "matrix", "plan" })
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
    public void APullRequestThatBecomesReadyForReviewStartsARun() =>
        ((YamlSequenceNode)Development()["on"]["pull_request"]["types"]).Children.Select(node => node.ToString())
            .Should().Contain("ready_for_review").And.NotContain("edited").And.NotContain("labeled");

    [Test]
    public void TheCiRequestWorkflowDispatchesOnlyForCiLabelsAndRunsNoPullRequestCode()
    {
        var workflow = Read("ci-request.yml");
        var dispatch = Job(workflow, "dispatch");
        dispatch["if"].ToString().Should().Contain($"startsWith(github.event.label.name, '{CiLabels.Prefix}')");
        dispatch["permissions"]["actions"].ToString().Should().Be("write");
        ((YamlMappingNode)dispatch["permissions"]).Children.Should().HaveCount(1);
        Steps(dispatch).Should().NotContain(step => Value(step, "uses").StartsWith("actions/checkout", StringComparison.Ordinal));
        Steps(dispatch).Should().ContainSingle().Which["run"].ToString().Should().Contain("gh workflow run development.yml");
    }

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
