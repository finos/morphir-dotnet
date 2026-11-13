// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps for the install scripts of morphirup, <c>scripts/install-morphirup.sh</c> and
/// <c>scripts/install-morphirup.ps1</c>, which the test project copies as fixtures. A release list is a fixture in
/// the form of the GitHub API, and a release feed a fixture in the form of <c>releases.atom</c>.
/// </summary>
[Binding]
public sealed class InstallScriptSteps(IInstallScriptDriver scripts, IScenarioWorkspace workspace, ScenarioRun run)
{
    private InstallScriptInputs _inputs = new();
    private InstallScriptRun? _run;

    private InstallScriptRun Run => _run ?? throw new InvalidOperationException("The scenario ran no install script before this step.");

    private ReleaseMirror Mirror => _inputs.Mirror ?? throw new InvalidOperationException("The scenario started no release mirror before this step.");

    [Given("the Morphir release list {string}")]
    public void TheReleaseList(string fixture) => _inputs = _inputs with { Releases = workspace.CopyFixture($"InstallScripts/{fixture}") };

    [Given("the Morphir release feed {string}")]
    public void TheReleaseFeed(string fixture) => _inputs = _inputs with { Feed = workspace.CopyFixture($"InstallScripts/{fixture}") };

    [Given("a release mirror with morphirup {string}")]
    public void AMirrorWithMorphirup(string version) => _inputs = _inputs with { Mirror = scripts.StartMirror([version]) };

    [Given("a release mirror with no morphirup")]
    public void AMirrorWithNoMorphirup() => _inputs = _inputs with { Mirror = scripts.StartMirror([]) };

    [When("I run the install script of this machine with no version")]
    public Task RunWithNoVersion() => RunTheScript(_inputs);

    [When("I run the install script of this machine for the version {string}")]
    public Task RunForTheVersion(string version) => RunTheScript(_inputs with { Version = version });

    private async Task RunTheScript(InstallScriptInputs inputs)
    {
        var script = workspace.CopyFixture($"InstallScripts/{scripts.ScriptName}");
        _run = await scripts.InstallAsync(script, inputs);
        run.Result = _run.Result;
    }

    [Then("the install script chose the release {string}")]
    public void TheChosenRelease(string version) =>
        Run.ChosenVersion.Should().Be(version, Run.Result.Describe());

    [Then("the install script downloads morphirup from {string}")]
    public void TheDownloadSource(string prefix) =>
        Run.DownloadUrl.Should().StartWith(prefix, Run.Result.Describe());

    [Then("the install script downloads morphirup {string} from the mirror")]
    public void TheDownloadFromTheMirror(string version) =>
        Run.DownloadUrl.Should().StartWith($"{Mirror.Url}/v{version}/morphirup-", Run.Result.Describe());

    [Then("the mirror was asked for the morphirup of {string}")]
    public void TheMirrorWasAsked(string tag) =>
        Mirror.Requests.Should().Contain(request => request.Contains($" {tag}/morphirup-", StringComparison.Ordinal),
            $"the script checks the tag {tag}; the mirror was asked for {string.Join(", ", Mirror.Requests)}{Environment.NewLine}{Run.Result.Describe()}");

    [Then("the install script says {string}")]
    public void TheMessage(string text) =>
        Run.Messages.Should().Contain(text, Run.Result.Describe());

    [Then("the install script installed morphirup")]
    public void InstalledMorphirup()
    {
        Run.Result.ExitCode.Should().Be(0, Run.Result.Describe());
        Run.InstalledFiles.Should().ContainSingle(file => file == "morphirup" || file == "morphirup.exe", Run.Result.Describe());
    }

    [Then("the install script installed nothing")]
    public void InstalledNothing()
    {
        Run.Result.ExitCode.Should().NotBe(0, Run.Result.Describe());
        Run.InstalledFiles.Should().BeEmpty(Run.Result.Describe());
    }
}
