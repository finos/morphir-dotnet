// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps for the install scripts of morphirup, <c>scripts/install-morphirup.sh</c> and
/// <c>scripts/install-morphirup.ps1</c>, which the test project copies as fixtures. A release list is a fixture in
/// the form of the GitHub API.
/// </summary>
[Binding]
public sealed class InstallScriptSteps(IInstallScriptDriver scripts, IScenarioWorkspace workspace, ScenarioRun run)
{
    private WorkspaceFile? _releases;
    private InstallScriptRun? _run;

    private InstallScriptRun Run => _run ?? throw new InvalidOperationException("The scenario ran no install script before this step.");

    [Given("the Morphir release list {string}")]
    public void TheReleaseList(string fixture) => _releases = workspace.CopyFixture($"InstallScripts/{fixture}");

    [When("I run the install script of this machine with no version")]
    public async Task RunTheInstallScript()
    {
        var releases = _releases ?? throw new InvalidOperationException("The scenario gave no release list before this step.");
        var script = workspace.CopyFixture($"InstallScripts/{scripts.ScriptName}");
        _run = await scripts.InstallNewestAsync(script, releases);
        run.Result = _run.Result;
    }

    [Then("the install script chose the release {string}")]
    public void TheChosenRelease(string version) =>
        Run.ChosenVersion.Should().Be(version, Run.Result.Describe());

    [Then("the install script downloads morphirup from {string}")]
    public void TheDownloadSource(string prefix) =>
        Run.DownloadUrl.Should().StartWith(prefix, Run.Result.Describe());

    [Then("the install script says {string}")]
    public void TheMessage(string text) =>
        Run.Messages.Should().Contain(text, Run.Result.Describe());

    [Then("the install script installed nothing")]
    public void InstalledNothing()
    {
        Run.Result.ExitCode.Should().NotBe(0, Run.Result.Describe());
        Run.InstalledFiles.Should().BeEmpty(Run.Result.Describe());
    }
}
