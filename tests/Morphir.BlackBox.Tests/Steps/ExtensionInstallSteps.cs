// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of <c>extension install</c> from a NuGet folder feed, through <see cref="IExtensionInstallDriver"/>.</summary>
[Binding]
public sealed class ExtensionInstallSteps(IExtensionInstallDriver installation, ScenarioRun run)
{
    private WorkspaceDirectory? _feed;

    private WorkspaceDirectory Feed => _feed ?? throw new InvalidOperationException("The scenario has no NuGet folder feed.");

    [Given("a NuGet folder feed with version {string} of the extension package {string}")]
    public void AFolderFeedWithTheExtensionPackage(string version, string id) => _feed = installation.WriteNuGetFolderFeed(id, version);

    [When("I install the extension {string} from the NuGet folder feed")]
    public async Task InstallFromTheFolderFeed(string source) =>
        run.Result = await installation.InstallAsync(new ExtensionInstallOptions { Source = source, NuGetSource = Feed });

    [When("I plan the installation of the extension {string} from the NuGet folder feed")]
    public async Task PlanFromTheFolderFeed(string source) =>
        run.Result = await installation.InstallAsync(new ExtensionInstallOptions { Source = source, NuGetSource = Feed, DryRun = true });

    [Then("the extension home records the NuGet package {string} version {string}")]
    public void TheHomeRecordsTheNuGetPackage(string id, string version) =>
        installation.Installation(run.Result).ShouldRecordNuGetPackage(ExtensionInstallDriver.ExtensionId, id, version);

    [Then("the extension home has no installed extension")]
    public void TheHomeHasNoInstalledExtension() => installation.Installation(run.Result).ShouldHaveNothingInstalled();
}
