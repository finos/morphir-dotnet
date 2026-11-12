// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

[Category("PlatformSpecific")]
public sealed class WorkspaceMigrationCompositionTests
{
    [Test]
    [Arguments(null)]
    [Arguments("shared-output")]
    public void RootClassicConversionAndMembershipAdditionShareTheProposedContext(string? output)
    {
        using var fixture = new WorkspaceFixture();
        var root = "{name:'Root',exposedModules:[],workspace:{members:['old']" + (output is null ? "" : ",out_dir:'" + output + "'") + "}}";
        fixture.Write("morphir.json", root);
        fixture.Write("old/morphir.toml", "# retain old\n[project]\nname='Old'\n");
        fixture.Write("new/morphir.json", "{name:'New',exposedModules:[]}");
        var oldBytes = File.ReadAllBytes(fixture.Root + "/old/morphir.toml");
        var plan = fixture.Service.Plan(fixture.Root);
        plan.CanApply.Should().BeTrue(string.Join(",", plan.Diagnostics.Select(d => d.Code)));
        plan.Members.Should().Equal("old", "new");
        plan.Projects.Should().HaveCount(2).And.OnlyContain(p => p.Verification!.Verified);
        plan.Projects.Should().OnlyContain(p => p.Verification!.BaseWorkspace!.Value.OutputDirectory!.Value == ProjectPath.join(fixture.Root, output ?? ".morphir/out"));
        fixture.Service.Apply(plan).Status.Should().Be(ConfigurationTransactionStatus.Written);
        var actual = ProjectSystem.Discover(new FileSystemProjectTree(fixture.Root), fixture.Root);
        actual.Discovery.Projects.Select(p => p.Path).Should().Equal(fixture.Root, fixture.Root + "/new", fixture.Root + "/old");
        File.ReadAllBytes(fixture.Root + "/old/morphir.toml").Should().Equal(oldBytes);
        File.ReadAllText(fixture.Root + "/morphir.json").Should().Be(root);
    }

    [Test]
    public void AnExistingRootMembershipConflictIsNotHiddenByAdoptionComposition()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Write("morphir.json", "{name:'Root',exposedModules:[],workspace:{members:['other']}}");
        const string target = "[workspace]\nmembers=['old']\n";
        fixture.Write("morphir.toml", target);
        fixture.Write("old/morphir.toml", "[project]\nname='Old'\n");
        fixture.Write("new/morphir.json", "{name:'New',exposedModules:[]}");
        var plan = fixture.Service.Plan(fixture.Root);
        plan.CanApply.Should().BeFalse();
        plan.Projects.Should().Contain(p => p.Plan != null && p.Plan.Conflicts.Any(c => c.KeyPath.SequenceEqual(new[] { "workspace", "members" })));
        File.ReadAllText(fixture.Root + "/morphir.toml").Should().Be(target);
        File.Exists(fixture.Root + "/new/morphir.toml").Should().BeFalse();
    }
    [Test]
    [Arguments("original")]
    [Arguments("members")]
    public void ProposedContextCannotInventOriginalTargetDataOrUndeclaredMembership(string mode)
    {
        using var fixture = new WorkspaceFixture();
        fixture.Write("morphir.json", "{name:'Root',exposedModules:[],custom:{value:'source'}}");
        const string targetText = "[workspace]\nmembers=[]\n[custom]\nvalue='existing'\n";
        fixture.Write("morphir.toml", targetText);
        var tree = new FileSystemProjectTree(fixture.Root);
        var discovered = ProjectSystem.Discover(tree, fixture.Root).Discovery;
        var target = new SourceIdentity(fixture.Root + "/morphir.toml", DocumentFormat.Toml, SourceRole.RootPrimary, 4, null);
        var context = new ProjectMigrationContext(fixture.Root, discovered.Workspace, discovered.Root.Sources,
            proposedMembers: mode == "members" ? ["unlisted"] : [],
            originalTarget: mode == "original" ? new SourceInput(target, SourceContent.Missing, false) : null);
        var service = new ProjectMigrationService(tree, new NativeConfigurationTransactionHost());
        var preview = service.PlanInWorkspace(fixture.Root, context, fixture.Root + "/morphir.json");
        preview.CanApply.Should().BeFalse();
        preview.Diagnostics.Should().Contain(d => d.Code == "migration.context");
        File.ReadAllText(fixture.Root + "/morphir.toml").Should().Be(targetText);
    }

    [Test]
    public void ProposedContextComponentsCannotBeAppliedOutsideTheAggregateCoordinator()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Write("morphir.json", "{name:'Root',exposedModules:[]}");
        const string original = "[workspace]\nmembers=[]\n";
        fixture.Write("morphir.toml", original);
        var tree = new FileSystemProjectTree(fixture.Root);
        var discovered = ProjectSystem.Discover(tree, fixture.Root).Discovery;
        var context = new ProjectMigrationContext(fixture.Root, discovered.Workspace, discovered.Root.Sources,
            proposedMembers: [], originalTarget: discovered.Root.Sources.Single(s => s.Source.Path == fixture.Root + "/morphir.toml"));
        var service = new ProjectMigrationService(tree, new NativeConfigurationTransactionHost());
        var preview = service.PlanInWorkspace(fixture.Root, context, fixture.Root + "/morphir.json");
        preview.CanApply.Should().BeTrue();
        service.Apply(preview).Code.Should().Be("migration.unverified_plan");
        File.ReadAllText(fixture.Root + "/morphir.toml").Should().Be(original);
    }

    [Test]
    public void DeclaredWorkspaceBaseDoesNotReanchorTheRootProjectSourceDirectory()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Write("morphir.json", "{name:'Root',sourceDirectory:'root-src',exposedModules:[],workspace:{root:'sub',members:['old']}}");
        fixture.Write("sub/old/morphir.toml", "[project]\nname='Old'\n");
        fixture.Write("sub/new/morphir.json", "{name:'New',exposedModules:[]}");
        var plan = fixture.Service.Plan(fixture.Root);
        plan.CanApply.Should().BeTrue(string.Join(",", plan.Diagnostics.Select(d => d.Code)));
        plan.RootIsProject.Should().BeTrue();
        plan.Members.Should().Equal("old", "new");
        plan.Projects.Should().OnlyContain(p => p.Verification!.BaseWorkspace!.Value.Root == fixture.Root + "/sub");
        var root = plan.Projects.Single(p => p.ProjectPath == fixture.Root);
        root.Verification!.Differences.ToArray().Should().BeEmpty();
        ConfigTree.tryFind(Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "project", "source_directory" }), root.Verification.After)!.Value.Should().Be(ConfigValue.NewString("root-src"));
        fixture.Service.Apply(plan).Status.Should().Be(ConfigurationTransactionStatus.Written);
        var actual = ProjectSystem.Discover(new FileSystemProjectTree(fixture.Root), fixture.Root).Discovery;
        actual.Workspace!.Value.Root.Should().Be(fixture.Root + "/sub");
        actual.Projects.Select(p => p.Path).Should().Equal(fixture.Root, fixture.Root + "/sub/new", fixture.Root + "/sub/old");
    }

}
