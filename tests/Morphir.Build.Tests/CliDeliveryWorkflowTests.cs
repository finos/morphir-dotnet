// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using TUnit.Core;
using YamlDotNet.RepresentationModel;

namespace Morphir.Build.Tests;

/// <summary>
/// cli-build-interface: finos/morphir builds, qualifies and publishes the official CLI from a pinned commit of this
/// repository. This repository publishes its libraries and extension tools, and publishes the CLI (the morphir-&lt;rid&gt;
/// executables, morphirup and Morphir.Tool) only when MORPHIR_DOTNET_CLI_PUBLICATION is enabled, as a rollback.
/// </summary>
public class CliDeliveryWorkflowTests
{
    private const string Enabled = "vars.MORPHIR_DOTNET_CLI_PUBLICATION == 'enabled'";

    [Test]
    [Arguments("build-executables")]
    [Arguments("build-morphirup")]
    public void TheCliBuildJobsRunOnlyWhenCliPublicationIsEnabled(string job)
    {
        var condition = Job(Deployment(), job)["if"].ToString();
        condition.Should().Contain(Enabled);
    }

    [Test]
    public void TheReleaseAlwaysPublishesTheLibrariesAndExtensionToolsAndTheCliToolOnlyWhenEnabled()
    {
        var steps = Steps(Job(Deployment(), "release"));
        var always = steps.Single(step => Name(step) == "Publish packages to NuGet");
        always["run"].ToString().Should().Contain("--target PublishLibs PublishExtensionTools");
        always.Children.Should().NotContainKey(new YamlScalarNode("if"));

        var cli = steps.Single(step => Run(step).Contains("--target PublishCliTool", StringComparison.Ordinal));
        cli["if"].ToString().Should().Contain(Enabled);
        cli["env"]["NUGET_API_KEY"].ToString().Should().Be(always["env"]["NUGET_API_KEY"].ToString());

        steps.Should().NotContain(step => Run(step).Contains("PublishAll", StringComparison.Ordinal) || Run(step).Contains("--target PublishTool", StringComparison.Ordinal),
            "PublishAll and PublishTool would publish Morphir.Tool unconditionally");
    }

    [Test]
    public void TheReleaseDownloadsTheExecutablesOnlyWhenCliPublicationIsEnabled()
    {
        foreach (var job in new[] { "release", "create-github-release" })
        {
            var cliSteps = Steps(Job(Deployment(), job)).Where(step =>
                Uses(step).StartsWith("actions/download-artifact", StringComparison.Ordinal)
                || Run(step).Contains("for rid in", StringComparison.Ordinal)
                || Run(step).Contains("MorphirupChecksums", StringComparison.Ordinal)).ToList();
            cliSteps.Should().NotBeEmpty();
            foreach (var step in cliSteps)
                step["if"].ToString().Should().Contain(Enabled, $"{job}: {Name(step)} handles CLI assets");
        }
    }

    [Test]
    public void TheToolTargetsAreSplitSoTheCliToolCanBeLeftOut()
    {
        var publishing = File.ReadAllText(Path.Combine(TestFixture.GetRepositoryRoot(), "build", "Build.Publishing.cs"));
        publishing.Should().Contain("Target PublishCliTool").And.Contain("Target PublishExtensionTools");
        publishing.Should().MatchRegex(@"Target PublishTool => _ => _\s+\.DependsOn\(PublishCliTool, PublishExtensionTools\)",
            "PublishTool keeps publishing all three tool packages");
    }

    [Test]
    public void TheOfferingWorkflowIsCalledWithARepositoryACommitAndOneOffering()
    {
        var workflow = Offering();
        var call = (YamlMappingNode)workflow["on"]["workflow_call"];
        var inputs = (YamlMappingNode)call["inputs"];
        inputs.Children.Keys.Select(key => key.ToString()).Should().Equal("repository", "ref", "deployment", "rid", "os", "configuration");
        inputs["repository"]["default"].ToString().Should().Be("finos/morphir-dotnet");
        inputs["ref"]["required"].ToString().Should().Be("true");
        inputs["deployment"]["required"].ToString().Should().Be("true");
        ((YamlMappingNode)call["outputs"]).Children.Keys.Select(key => key.ToString())
            .Should().Equal("artifact-name", "artifact", "sha256", "release");

        var checkout = Steps(Job(workflow, "build")).Single(step => Uses(step).StartsWith("actions/checkout", StringComparison.Ordinal));
        checkout["with"]["repository"].ToString().Should().Be("${{ inputs.repository }}");
        checkout["with"]["ref"].ToString().Should().Be("${{ inputs.ref }}", "the caller's ref would be checked out otherwise");
        checkout["with"]["fetch-depth"].ToString().Should().Be("0", "the version comes from the CHANGELOG and the history");
    }

    [Test]
    public void TheOfferingWorkflowAcceptsExactlyTheFourOfferings()
    {
        var validate = Steps(Job(Offering(), "build")).Single(step => Name(step) == "Check the inputs");
        foreach (var deployment in Morphir.Build.Helpers.Deployment.All)
            validate["run"].ToString().Should().Contain(deployment.Id);
        Morphir.Build.Helpers.Deployment.All.Select(deployment => deployment.Id)
            .Should().Equal("native-aot", "self-contained", "framework-dependent", "dotnet-tool");
    }

    [Test]
    public void TheOfferingWorkflowStartsTheProducedExecutableNotDotnetExec()
    {
        var steps = Steps(Job(Offering(), "build"));
        steps.Should().Contain(step => Run(step).Contains("--target PackRelease", StringComparison.Ordinal));
        var qualify = steps.Single(step => Name(step) == "Start the produced executable");
        var script = qualify["run"].ToString();
        script.Should().Contain("--version").And.Contain("launchPath").And.Contain("dotnet-morphir");
        // The tool package is installed only from the built bytes (ToolPackageInstallTests), never with --add-source,
        // which races the configured feeds.
        script.Should().Contain("./.github/scripts/install-tool-package.ps1 -Package $artifact").And.NotContain("--add-source")
            .And.NotContain("dotnet tool install");
        script.Should().NotContain("dotnet exec");
        // The executable prints the release with its commit as build metadata (0.4.0-alpha.4+<sha>), and the launch
        // metadata names the release alone; the check compares the part before '+'.
        script.Should().Contain("$version.Split('+')[0] -ne $launch.release");

        var upload = steps.Single(step => Uses(step).StartsWith("actions/upload-artifact", StringComparison.Ordinal));
        upload["with"]["path"].ToString().Should().Contain(".build-launch.json").And.Contain(".sha256");
    }

    private static YamlMappingNode Deployment() => Read("deployment.yml");

    private static YamlMappingNode Offering() => Read("cli-offering.yml");

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

    private static string Name(YamlMappingNode step) => Value(step, "name");

    private static string Run(YamlMappingNode step) => Value(step, "run");

    private static string Uses(YamlMappingNode step) => Value(step, "uses");
}
