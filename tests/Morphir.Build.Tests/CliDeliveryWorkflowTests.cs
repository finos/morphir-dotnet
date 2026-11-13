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
[Category("PlatformSpecific")]
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

    // The runner of each released platform, native for its RID: Native AOT does not cross-compile between operating
    // systems, and each job runs what it built. win-arm64 builds on the hosted Windows 11 ARM64 runner, not on x64.
    private static readonly IReadOnlyDictionary<string, string> NativeRunners = new Dictionary<string, string>
    {
        ["linux-x64"] = "ubuntu-latest",
        ["linux-arm64"] = "ubuntu-24.04-arm",
        ["win-x64"] = "windows-latest",
        ["win-arm64"] = "windows-11-arm",
        ["osx-arm64"] = "macos-latest",
    };

    [Test]
    [Arguments("deployment.yml", "build-executables")]
    [Arguments("deployment.yml", "build-morphirup")]
    [Arguments("morphirup.yml", "build")]
    public void TheCliReleaseMatricesBuildEveryPlatformOnItsNativeRunner(string workflow, string job)
    {
        var include = (YamlSequenceNode)Job(Read(workflow), job)["strategy"]["matrix"]["include"];
        var entries = include.Children.Cast<YamlMappingNode>()
            .Select(entry => (Rid: entry["rid"].ToString(), Os: entry["os"].ToString())).ToList();

        entries.Select(entry => entry.Rid).Should().Equal(Morphir.Build.Helpers.MorphirupRelease.Platforms);
        entries.Should().Contain(("win-arm64", "windows-11-arm"), "Windows ARM64 is released, built on the native ARM64 runner");
        foreach (var (rid, os) in entries)
            os.Should().Be(NativeRunners[rid], $"{workflow} {job} builds {rid} on a native runner");
    }

    [Test]
    [Arguments("deployment.yml", "build-executables", "--target PublishSingleFile")]
    [Arguments("morphirup-build.yml", "build", "targets=(PackMorphirup CheckMorphirup)")]
    [Arguments("cli-offering.yml", "build", "--target PackRelease")]
    public void TheCliBuildsCheckTheMachineIsNativeBeforeTheyBuild(string workflow, string job, string build)
    {
        var steps = Steps(Job(Read(workflow), job));
        var check = steps.FindIndex(step => Run(step).Contains("--target CheckBuildMachine", StringComparison.Ordinal));
        var publish = steps.FindIndex(step => Run(step).Contains(build, StringComparison.Ordinal));

        check.Should().BeGreaterThanOrEqualTo(0, $"{workflow} checks that the runner and its .NET SDK are native for the RID");
        publish.Should().BeGreaterThan(check, "the check runs before anything is built");
    }

    [Test]
    public void OneReusableJobProducesEachWindowsToolchainAndItsManager()
    {
        // One producer per Windows RID: morphirup-build.yml, which build-morphirup and the pull request workflow call.
        // Its toolchain upload and its manager upload come from the same job, so the release never puts a second build
        // of a manager over the bytes that its toolchain's SHA256SUMS names.
        var folder = Path.Combine(TestFixture.GetRepositoryRoot(), ".github", "workflows");
        Directory.GetFiles(folder, "*.yml").Where(path => File.ReadAllText(path).Contains("PackWindowsToolchain", StringComparison.Ordinal))
            .Select(Path.GetFileName).Should().Equal("morphirup-build.yml");
        ((YamlMappingNode)Deployment()["jobs"]).Children.Keys.Select(key => key.ToString()).Should().NotContain("build-windows-toolchain");
        File.Exists(Path.Combine(folder, "morphirup-preview3-arm64-proof.yml")).Should().BeFalse("its proof lane is part of morphirup.yml");

        var steps = Steps(Job(Read("morphirup-build.yml"), "build"));
        var toolchain = steps.Single(step => Name(step) == "Upload the complete Windows toolchain proof inputs");
        toolchain["with"]["name"].ToString().Should().Be("windows-release-toolchain-${{ inputs.rid }}");
        toolchain["if"].ToString().Should().Contain("win-x64").And.Contain("win-arm64");
        steps.Single(step => Name(step) == "Upload the morphirup release assets")["with"]["name"].ToString()
            .Should().Be("morphirup-${{ inputs.rid }}");
    }

    [Test]
    [Arguments("deployment.yml", "create-github-release")]
    [Arguments("morphirup.yml", "checksums")]
    public void TheChecksumJobsAssembleBothWindowsToolchainsUnderOneChecksumFile(string workflow, string job)
    {
        var steps = Steps(Job(Read(workflow), job));
        var download = steps.Single(step => Uses(step).StartsWith("actions/download-artifact", StringComparison.Ordinal)
            && Value((YamlMappingNode)step["with"], "pattern") == "windows-release-toolchain-*");
        download["with"]["merge-multiple"].ToString().Should().Be("false", "each Windows output lands in a folder of its own");
        download["with"]["path"].ToString().Should().Be("./artifacts/downloads/windows-toolchain");

        var run = Run(steps.Single(step => Run(step).Contains("WindowsToolchainChecksums", StringComparison.Ordinal)));
        // Each producer's own SHA256SUMS is checked against its own output before anything is copied.
        run.Should().Contain("for rid in win-x64 win-arm64; do")
            .And.Contain("(cd \"./artifacts/downloads/windows-toolchain/windows-release-toolchain-$rid\" && sha256sum -c SHA256SUMS)");
        // Every output is flattened once: a manager or notices file that two downloads hold must be byte-identical.
        run.Should().Contain("--target FlattenReleaseAssets MorphirupChecksums WindowsToolchainChecksums")
            .And.Contain("--release-asset-sources ./artifacts/downloads/morphirup/* ./artifacts/downloads/windows-toolchain/*")
            .And.Contain("--windows-toolchain-sources ./artifacts/downloads/windows-toolchain/*");
        run.IndexOf("sha256sum -c SHA256SUMS)", StringComparison.Ordinal).Should().BeLessThan(run.IndexOf("--target FlattenReleaseAssets", StringComparison.Ordinal));
        run.TrimEnd().Should().EndWith("sha256sum -c SHA256SUMS", "the one release checksum file is read back the way a user would");
    }

    [Test]
    public void TheReleaseAttachesTheOneWindowsChecksumFile() =>
        Steps(Job(Deployment(), "create-github-release")).Single(step => Uses(step).StartsWith("softprops/action-gh-release", StringComparison.Ordinal))
            ["with"]["files"].ToString().Should().Contain("./artifacts/release-assets/SHA256SUMS");

    [Test]
    public void APullRequestVerifiesTheWinArm64OutputOnANativeRunnerAndKeepsIt()
    {
        var verify = Job(Read("morphirup.yml"), "verify-win-arm64");
        verify["runs-on"].ToString().Should().Be("windows-11-arm");
        ((YamlSequenceNode)verify["needs"]).Children.Select(node => node.ToString()).Should().Equal("build");
        var steps = Steps(verify);
        steps.Should().Contain(step => Uses(step).StartsWith("actions/download-artifact", StringComparison.Ordinal)
            && step["with"]["name"].ToString() == "windows-release-toolchain-win-arm64");
        Run(steps.Single(step => Name(step) == "Verify retained checksums before running the manager"))
            .Should().Contain("managerPeMachine").And.Contain("& $manager --version");
        steps.Should().Contain(step => Uses(step).StartsWith("actions/upload-artifact", StringComparison.Ordinal)
            && step["with"]["retention-days"].ToString() == "7");
    }

    [Test]
    public void ADryRunBuildsAndAssemblesTheReleaseButPublishesNothing()
    {
        var deployment = Deployment();
        var dryRun = (YamlMappingNode)deployment["on"]["workflow_dispatch"]["inputs"]["dry-run"];
        dryRun["type"].ToString().Should().Be("boolean");
        dryRun["default"].ToString().Should().Be("false");

        // The CLI jobs run for a dry run whatever the publication variable says ...
        foreach (var job in new[] { "build-executables", "build-morphirup" })
            Job(deployment, job)["if"].ToString().Should().Contain(Enabled).And.Contain("inputs.dry-run");

        // ... and nothing publishes: the job that holds every NuGet publish step does not run, and the GitHub release is
        // not created.
        Job(deployment, "release")["if"].ToString().Should().Contain("!inputs.dry-run");
        Steps(Job(deployment, "create-github-release")).Should().NotContain(step => Run(step).Contains("--api-key", StringComparison.Ordinal));
        var assemble = Job(deployment, "create-github-release");
        assemble["if"].ToString().Should().Contain("inputs.dry-run");
        var steps = Steps(assemble);
        steps.Single(step => Uses(step).StartsWith("softprops/action-gh-release", StringComparison.Ordinal))["if"].ToString()
            .Should().Contain("!inputs.dry-run");
        var upload = steps.Single(step => Uses(step).StartsWith("actions/upload-artifact", StringComparison.Ordinal));
        upload["if"].ToString().Should().Contain("inputs.dry-run").And.NotContain("!inputs.dry-run");
        upload["with"]["path"].ToString().Should().Be("./artifacts/release-assets/*");
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
