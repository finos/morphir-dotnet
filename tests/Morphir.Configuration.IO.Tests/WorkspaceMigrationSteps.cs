// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration.IO;
namespace Morphir.Configuration.IO.Tests;

[Binding]
public sealed class WorkspaceMigrationSteps
{
    private Dictionary<string, string> files = [];
    private WorkspaceMigrationPreview? preview;
    [Given("a repository with two classic workspace candidates")]
    public void Two() => files = new() { ["/repo/a/morphir.json"] = "{name:'A',exposedModules:[]}", ["/repo/b/morphir.json"] = "{name:'B',exposedModules:[]}" };
    [Given("a repository with a root classic project and one child")]
    public void Root() { Two(); files.Remove("/repo/b/morphir.json"); files["/repo/morphir.json"] = "{name:'Root',exposedModules:[]}"; }
    [Given("a repository with excluded and nested workspace candidates")]
    public void Boundaries() { Two(); files["/repo/morphir.toml"] = "[workspace]\nmembers=[]\nexclude=['b']\n"; files["/repo/nested/morphir.toml"] = "[workspace]\nmembers=['inner']\n"; files["/repo/nested/inner/morphir.json"] = "{name:'Inner',exposedModules:[]}"; }
    [When("a host previews bounded workspace migration")]
    public void Preview() => preview = new WorkspaceMigrationService(new InMemoryProjectTree("/repo", files)).Plan("/repo");
    [Then("both conversions share one verified virtual workspace")]
    public void Virtual() { preview!.CanApply.Should().BeTrue(string.Join(",", preview.Diagnostics.Select(d => d.Code))); preview.Verified.Should().BeTrue(); preview.RootIsProject.Should().BeFalse(); preview.Members.Should().Equal("a", "b"); preview.Projects.Should().HaveCount(2).And.OnlyContain(p => p.Verification!.BaseWorkspace!.Value.Root == "/repo"); }
    [Then("workspace preview changes no files")]
    public void Unchanged() => files.Keys.Should().OnlyContain(p => p.EndsWith(".json", StringComparison.Ordinal));
    [Then("the root remains a project outside the member list")]
    public void RootIdentity() { preview!.CanApply.Should().BeTrue(); preview.RootIsProject.Should().BeTrue(); preview.Members.Should().Equal("a"); }
    [Then("excluded and boundary paths have no conversions")]
    public void Omitted() { preview!.CanApply.Should().BeTrue(); preview.Inventory.Should().Contain(i => i.RelativePath == "b" && i.Kind == WorkspaceInventoryKind.Excluded).And.Contain(i => i.RelativePath == "nested" && i.Kind == WorkspaceInventoryKind.Boundary); preview.Projects.Should().ContainSingle(); }
}
