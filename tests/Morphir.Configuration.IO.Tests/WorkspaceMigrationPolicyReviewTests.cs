// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class WorkspaceMigrationPolicyReviewTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void NewlyAdoptedProjectsCannotGainUnrelatedExistingWorkspaceSettings(bool classic)
    {
        using var fixture = new WorkspaceFixture();
        fixture.Write("morphir.toml", "[workspace]\nmembers=[]\n[pipeline]\nbackend='root-specific'\n");
        fixture.Write(classic ? "app/morphir.json" : "app/morphir.toml", classic
            ? "{name:'App',exposedModules:[]}"
            : "[project]\nname='App'\nsource_directory='src'\nexposed_modules=[]\n");
        var original = File.ReadAllBytes(fixture.Root + "/morphir.toml");
        var preview = fixture.Service.Plan(fixture.Root);
        preview.CanApply.Should().BeFalse("new members must retain non-adoption values even when the workspace exists");
        preview.Diagnostics.Should().Contain(d => d.Code == "workspace.migrate_adoption_value");
        fixture.Service.Apply(preview).Code.Should().Be("workspace.migrate_unverified");
        File.ReadAllBytes(fixture.Root + "/morphir.toml").Should().Equal(original);
    }

    [Test]
    public void NewAndRetainedMembersReportTheirActualOriginalOwnershipAndOutput()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['old']\nout_dir='shared'\n",
            ["/repo/old/morphir.toml"] = "[project]\nname='Old'\n",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'\n"
        });
        var preview = new WorkspaceMigrationService(tree).Plan("/repo");
        preview.CanApply.Should().BeTrue();
        preview.AdoptionEffects.Should().Contain(e => e.ProjectPath == "/repo/app" && e.Kind == "ownership" && e.Before == "/repo/app" && e.After == "/repo");
        preview.AdoptionEffects.Should().Contain(e => e.ProjectPath == "/repo/app" && e.Kind == "shared-output-root" && e.Before == "/repo/app/.morphir/out" && e.After == "/repo/shared");
        preview.AdoptionEffects.Should().Contain(e => e.ProjectPath == "/repo/old" && e.Kind == "ownership" && e.Before == "/repo" && e.After == "/repo");
        preview.AdoptionEffects.Should().Contain(e => e.ProjectPath == "/repo/old" && e.Kind == "shared-output-root" && e.Before == "/repo/shared" && e.After == "/repo/shared");
    }

    [Test]
    [Arguments(".")]
    [Arguments("**")]
    public void SuppliedExclusionsPreventRootConversionWithoutRemovingItsIdentity(string pattern)
    {
        using var fixture = new WorkspaceFixture();
        const string rootSource = "{name:'Root',exposedModules:[]}";
        fixture.Write("morphir.json", rootSource);
        fixture.Write("app/morphir.json", "{name:'App',exposedModules:[]}");
        var preview = fixture.Service.Plan(fixture.Root, excludes: [pattern]);
        preview.RootIsProject.Should().BeTrue();
        preview.Inventory.Should().Contain(i => i.Path == fixture.Root && i.Kind == WorkspaceInventoryKind.Excluded);
        preview.Projects.Should().NotContain(p => p.ProjectPath == fixture.Root);
        if (pattern == ".")
        {
            preview.CanApply.Should().BeTrue(string.Join(",", preview.Diagnostics.Select(d => d.Code)));
            preview.Projects.Should().ContainSingle().Which.ProjectPath.Should().Be(fixture.Root + "/app");
            fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
            File.ReadAllText(fixture.Root + "/morphir.json").Should().Be(rootSource);
            var target = ConfigurationReader.ReadText(fixture.Root + "/morphir.toml", File.ReadAllText(fixture.Root + "/morphir.toml"));
            ConfigTree.tryFind(Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "project" }), ((SourceContent.Available)target.Content).Item.Raw).Should().BeNull("only workspace composition belongs in the root target");
        }
        else preview.CanApply.Should().BeFalse("excluding every project leaves no adoption to plan");
    }
}
