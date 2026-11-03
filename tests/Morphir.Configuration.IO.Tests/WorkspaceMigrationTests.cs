// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration.IO;
namespace Morphir.Configuration.IO.Tests;

public sealed class WorkspaceMigrationInventoryTests
{
    [Test]
    public void InventoryIsStableBoundedAndExplainsDefaultSkips()
    {
        var files=new Dictionary<string,string> {
            ["/repo/z/morphir.json"]="{name:'Same',exposedModules:[]}",
            ["/repo/a/.morphir/morphir.json"]="{name:'Same',exposedModules:[]}",
            ["/repo/node_modules/pkg/morphir.json"]="{name:'Ignored',exposedModules:[]}",
            ["/outside/morphir.json"]="{name:'Outside',exposedModules:[]}"
        };
        var tree=new InMemoryProjectTree("/",files);
        var plan=new WorkspaceMigrationService(tree).Plan("/repo");
        plan.CanApply.Should().BeTrue(string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        plan.Members.Should().Equal("a","z");
        plan.Projects.Select(p=>p.ProjectPath).Should().Equal("/repo/a","/repo/z");
        plan.Inventory.Should().Contain(i=>i.RelativePath=="node_modules"&&i.Kind==WorkspaceInventoryKind.Excluded);
        plan.Diagnostics.Should().Contain(d=>d.Code=="discovery.duplicate_name");
        tree.ObservedPaths.Should().OnlyContain(p=>ProjectPath.contains("/repo",p));
    }
    [Test]
    public void FiltersAreRootRelativeAndExistingExclusionsRemainEffective()
    {
        var files=new Dictionary<string,string> {
            ["/repo/morphir.toml"]="[workspace]\nmembers=[]\nexclude=['apps/b']\n",
            ["/repo/apps/a/morphir.json"]="{name:'A',exposedModules:[]}",
            ["/repo/apps/b/morphir.json"]="{name:'B',exposedModules:[]}",
            ["/repo/other/morphir.json"]="{name:'Other',exposedModules:[]}"
        };
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",files)).Plan("/repo",["apps/**"],["**/ignored"]);
        plan.CanApply.Should().BeTrue(); plan.Members.Should().Equal("apps/a");
        plan.Inventory.Should().Contain(i=>i.RelativePath=="apps/b"&&i.Kind==WorkspaceInventoryKind.Excluded).And.Contain(i=>i.RelativePath=="other"&&i.Kind==WorkspaceInventoryKind.Excluded);
    }
}

public sealed class WorkspaceMigrationPlanningTests
{
    private const string Classic="{name:'A',sourceDirectory:'../source',exposedModules:[],profiles:{default:['dev']}}";
    [Test]
    public void ExistingWorkspaceSettingsDefaultsAndMigratedMembersRemainEffective()
    {
        var tree=new InMemoryProjectTree("/repo",new Dictionary<string,string> {
            ["/repo/morphir.toml"]="# root\n[workspace]\nmembers=['old']\ndefault_member='old'\nout_dir='shared-output'\n[pipeline]\nbackend='shared'\n",
            ["/repo/old/morphir.toml"]="# exact old bytes\n[project]\nname='Old'\n",
            ["/repo/new/.morphir/morphir.json"]=Classic,
            ["/repo/new/morphir.user.toml"]="[project]\nsource_directory='WRONG'\n",
            ["/repo/new/morphir.dev.toml"]="[project]\nsource_directory='WRONG'\n"
        });
        var plan=new WorkspaceMigrationService(tree).Plan("/repo");
        plan.CanApply.Should().BeTrue(string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        plan.Members.Should().Equal("old","new");
        plan.Projects.Should().ContainSingle().Which.ProjectPath.Should().Be("/repo/new");
        var verification=plan.Projects[0].Verification!;
        verification.Verified.Should().BeTrue();
        verification.BaseWorkspace!.Value.OutputDirectory!.Value.Should().Be("/repo/shared-output");
        ConfigTree.tryFind(Microsoft.FSharp.Collections.ListModule.OfSeq(new[]{"pipeline","backend"}),verification.After)!.Value.Should().Be(ConfigValue.NewString("shared"));
        tree.ObservedPaths.Should().NotContain(p=>p.Contains(".user.",StringComparison.Ordinal)||p.Contains(".dev.",StringComparison.Ordinal));
        plan.Inventory.Should().Contain(i=>i.RelativePath=="old"&&i.Kind==WorkspaceInventoryKind.AlreadyMigrated);
    }
    [Test]
    public void RootAndChildConversionsShareProposedBaseWithEveryClassicExcluded()
    {
        var tree=new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/.morphir/morphir.json"]="{name:'Root',exposedModules:[],custom:{same:1}}",
            ["/repo/app/.config/morphir/config.json"]="{name:'App',exposedModules:[],custom:{same:1}}"
        });
        var plan=new WorkspaceMigrationService(tree).Plan("/repo");
        plan.CanApply.Should().BeTrue(string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        plan.RootIsProject.Should().BeTrue(); plan.Members.Should().Equal("app");
        plan.Projects.Select(p=>p.ProjectPath).Should().Equal("/repo","/repo/app");
        plan.Projects.Should().OnlyContain(p=>p.Verification!.BaseWorkspace!.Value.Root=="/repo");
        plan.AdoptionEffects.Should().Contain(e=>e.Kind=="shared-output-root"&&e.ProjectPath=="/repo/app"&&e.Before=="/repo/app/.morphir/out"&&e.After=="/repo/.morphir/out");
    }
    [Test]
    public void AdoptionCannotExcuseUnrelatedInheritedSettingChanges()
    {
        var tree=new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/morphir.json"]="{name:'Root',exposedModules:[],pipeline:{backend:'root-specific'}}",
            ["/repo/app/morphir.json"]="{name:'App',exposedModules:[]}"
        });
        var plan=new WorkspaceMigrationService(tree).Plan("/repo");
        plan.CanApply.Should().BeFalse("a new shared output root cannot excuse an unrelated backend change");
        plan.Diagnostics.Should().Contain(d=>d.Code=="workspace.migrate_adoption_value");
    }
    [Test]
    public void DeclaredGlobsDoNotReadSkippedOutputsOrNestedIndependentMembers()
    {
        var tree=new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/morphir.toml"]="[workspace]\nmembers=['apps/**']\n",
            ["/repo/apps/a/morphir.json"]="{name:'A',exposedModules:[]}",
            ["/repo/apps/node_modules/pkg/morphir.json"]="{name:'Ignored',exposedModules:[]}",
            ["/repo/apps/nested/morphir.toml"]="[workspace]\nmembers=['inside']\n",
            ["/repo/apps/nested/inside/morphir.json"]="{name:'Inside',exposedModules:[]}"
        });
        var plan=new WorkspaceMigrationService(tree).Plan("/repo");
        plan.CanApply.Should().BeTrue(string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        plan.Projects.Select(p=>p.ProjectPath).Should().Equal("/repo/apps/a");
        tree.ObservedPaths.Should().NotContain(p=>p.StartsWith("/repo/apps/node_modules/",StringComparison.Ordinal)||p.StartsWith("/repo/apps/nested/inside",StringComparison.Ordinal));
    }
    [Test]
    public void SuppliedFiltersDoNotRemoveExistingRootProjectIdentity()
    {
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/morphir.json"]="{name:'Root',exposedModules:[]}",
            ["/repo/apps/a/morphir.json"]="{name:'A',exposedModules:[]}"
        })).Plan("/repo",["apps/**"]);
        plan.CanApply.Should().BeTrue(); plan.RootIsProject.Should().BeTrue(); plan.Members.Should().Equal("apps/a");
    }
}

public sealed class WorkspaceMigrationApplicationTests
{
    [Test]
    public void BatchPreservesSourcesAndEquivalentRepeatBytesTimestampsAndInventory()
    {
        using var fixture=new WorkspaceFixture();
        fixture.Write("a/.morphir/morphir.json","{name:'A',sourceDirectory:'../source',exposedModules:[]}");
        fixture.Write("b/morphir.json","{name:'B',exposedModules:[]}");
        var sources=Directory.GetFiles(fixture.Root,"*.json",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        var plan=fixture.Service.Plan(fixture.Root); plan.CanApply.Should().BeTrue(string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        fixture.Service.Apply(plan).Status.Should().Be(ConfigurationTransactionStatus.Written);
        var targets=Directory.GetFiles(fixture.Root,"*.toml",SearchOption.AllDirectories);
        var original=targets.ToDictionary(p=>p,File.ReadAllBytes);
        var time=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);
        foreach(var path in targets)File.SetLastWriteTimeUtc(path,time);
        var repeated=fixture.Service.Plan(fixture.Root); repeated.CanApply.Should().BeTrue(); repeated.Changed.Should().BeFalse();
        repeated.Inventory.Where(i=>i.Path.EndsWith("/a",StringComparison.Ordinal)||i.Path.EndsWith("/b",StringComparison.Ordinal)).Should().OnlyContain(i=>i.Kind==WorkspaceInventoryKind.AlreadyMigrated);
        fixture.Service.Apply(repeated).Status.Should().Be(ConfigurationTransactionStatus.Unchanged);
        foreach(var path in targets){File.ReadAllBytes(path).Should().Equal(original[path]);File.GetLastWriteTimeUtc(path).Should().Be(time);}
        foreach(var source in sources)File.ReadAllBytes(source.Key).Should().Equal(source.Value);
    }
    [Test]
    [Arguments("source")]
    [Arguments("target")]
    [Arguments("listing")]
    [Arguments("primary")]
    public void AggregateRechecksEveryRelevantPreconditionBeforeFirstReplacement(string drift)
    {
        using var fixture=new WorkspaceFixture();
        fixture.Write("a/morphir.json","{name:'A',exposedModules:[]}");
        fixture.Write("b/morphir.json","{name:'B',exposedModules:[]}");
        var preview=fixture.Service.Plan(fixture.Root); preview.CanApply.Should().BeTrue();
        switch(drift){case "source": fixture.Write("b/morphir.json","{name:'Changed',exposedModules:[]}");break;
        case "target":fixture.Write("a/morphir.toml","[custom]\nuser=true\n");break;
        case "listing":fixture.Write("new/morphir.json","{name:'New',exposedModules:[]}");break;
        default:fixture.Write("b/.config/morphir/config.toml","[custom]\nuser=true\n");break;}
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Conflict);
        File.Exists(fixture.Root+"/morphir.toml").Should().BeFalse();
        File.Exists(fixture.Root+"/b/morphir.toml").Should().BeFalse();
    }
    [Test]
    public void BlockedMemberPreventsEveryWriteAndReadonlyPlansCannotBeTransferred()
    {
        using var fixture=new WorkspaceFixture();
        fixture.Write("a/morphir.json","{name:'A',exposedModules:[]}");
        fixture.Write("b/morphir.json","{name:'B',exposedModules:[],dependencies:[{env:'secret-reference'}]}");
        var blocked=fixture.Service.Plan(fixture.Root);blocked.CanApply.Should().BeFalse();fixture.Service.Apply(blocked).Status.Should().Be(ConfigurationTransactionStatus.IoFailure);
        Directory.GetFiles(fixture.Root,"*.toml",SearchOption.AllDirectories).Should().BeEmpty();
        fixture.Write("b/morphir.json","{name:'B',exposedModules:[]}");
        var readOnly=new WorkspaceMigrationService(new FileSystemProjectTree(fixture.Root));
        var preview=readOnly.Plan(fixture.Root);preview.CanApply.Should().BeTrue();
        readOnly.Apply(preview).Code.Should().Be("workspace.migrate_unverified");fixture.Service.Apply(preview).Code.Should().Be("workspace.migrate_unverified");
    }
    [Test]
    public void ExistingEmptyVirtualWorkspaceIsUnchangedButUndeclaredEmptyRootCannotAdopt()
    {
        using var fixture=new WorkspaceFixture();
        fixture.Service.Plan(fixture.Root).CanApply.Should().BeFalse();
        fixture.Write("morphir.toml","# keep\n[workspace]\nmembers=[]\n");
        var preview=fixture.Service.Plan(fixture.Root);preview.CanApply.Should().BeTrue();preview.Changed.Should().BeFalse();
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Unchanged);
        File.ReadAllText(fixture.Root+"/morphir.toml").Should().Be("# keep\n[workspace]\nmembers=[]\n");
    }
}

internal sealed class WorkspaceFixture:IDisposable
{
    public string Root {get;}=ProjectPath.join(Path.GetTempPath(),"workspace-migration-"+Guid.NewGuid().ToString("N"));
    public WorkspaceMigrationService Service {get;}
    public WorkspaceFixture(){Directory.CreateDirectory(Root);Service=new(new FileSystemProjectTree(Root),new NativeConfigurationTransactionHost());}
    public void Write(string path,string text){path=ProjectPath.join(Root,path);Directory.CreateDirectory(ProjectPath.parent(path));File.WriteAllText(path,text);}
    public void Dispose()
    {
        if(Directory.EnumerateFileSystemEntries(Root,".morphir-edit-*",SearchOption.AllDirectories).Any())Console.Error.WriteLine("Retained workspace recovery evidence: "+Root);
        else Directory.Delete(Root,true);
    }
}

public sealed class WorkspaceMigrationBoundaryTests
{
    [Test]
    public void DiscoveredLinksAndReversedHostOrderDoNotChangeInventoryOrEscapeRoot()
    {
        var files=new Dictionary<string,string>{["/repo/a/morphir.json"]="{name:'A',exposedModules:[]}",["/repo/z/morphir.json"]="{name:'Z',exposedModules:[]}"};
        var first=new WorkspaceMigrationService(new OrderAndLinkTree(new InMemoryProjectTree("/",files),false)).Plan("/repo");
        var second=new WorkspaceMigrationService(new OrderAndLinkTree(new InMemoryProjectTree("/",files),true)).Plan("/repo");
        first.CanApply.Should().BeTrue();second.CanApply.Should().BeTrue();second.Inventory.Should().Equal(first.Inventory);second.Members.Should().Equal(first.Members);
        first.Inventory.Should().Contain(i=>i.RelativePath=="linked"&&i.Kind==WorkspaceInventoryKind.LinkSkipped);
    }
    [Test]
    [Arguments("../outside")]
    [Arguments("/outside")]
    [Arguments("")]
    public void InvalidFiltersAreStructuredAndCannotBroadenTheBoundary(string filter)
    {
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",new Dictionary<string,string>{["/repo/a/morphir.json"]="{name:'A',exposedModules:[]}"})).Plan("/repo",[filter]);
        plan.CanApply.Should().BeFalse();plan.Diagnostics.Should().Contain(d=>d.Code=="workspace.migrate_filter");
    }
    [Test]
    public void ExcludedRootClassicStillSuppliesActualBaseSettingsForAdoptedMembers()
    {
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/morphir.json"]="{name:'Root',exposedModules:[],pipeline:{backend:'root-specific'}}",
            ["/repo/app/morphir.json"]="{name:'App',exposedModules:[]}"
        })).Plan("/repo",["app"]);
        plan.CanApply.Should().BeFalse("retained root settings will inherit after adoption even if its conversion is filtered out");
        plan.Diagnostics.Should().Contain(d=>d.Code=="workspace.migrate_adoption_value");
    }
    [Test]
    public void ConfiguredOutputDirectoriesAreExcludedWithoutReadingTheirContents()
    {
        var tree=new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/morphir.toml"]="[workspace]\nmembers=[]\nout_dir='generated-artifacts'\n",
            ["/repo/app/morphir.json"]="{name:'App',exposedModules:[]}",
            ["/repo/generated-artifacts/fake/morphir.json"]="{name:'Generated',exposedModules:[]}"
        });
        var plan=new WorkspaceMigrationService(tree).Plan("/repo");
        plan.CanApply.Should().BeTrue();plan.Members.Should().Equal("app");
        tree.ObservedPaths.Should().NotContain(p=>p.StartsWith("/repo/generated-artifacts/",StringComparison.Ordinal));
        plan.Inventory.Should().Contain(i=>i.RelativePath=="generated-artifacts"&&i.Kind==WorkspaceInventoryKind.Excluded);
    }
    [Test]
    public void MissingDeclaredDefaultIsInvalidRatherThanSuccessfulNoop()
    {
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/morphir.toml"]="[workspace]\nmembers=[]\ndefault_member='absent'\n"
        })).Plan("/repo");
        plan.CanApply.Should().BeFalse();plan.Diagnostics.Should().Contain(d=>d.Code=="workspace.migrate_default");
    }
    [Test]
    public void GlobCharactersCannotPretendToBeAnExplicitLiteralMemberPath()
    {
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",new Dictionary<string,string>{["/repo/a?/morphir.json"]="{name:'A',exposedModules:[]}"})).Plan("/repo");
        plan.CanApply.Should().BeFalse();plan.Diagnostics.Should().Contain(d=>d.Code=="workspace.migrate_member_path");
    }
    private sealed class OrderAndLinkTree(IProjectTree inner,bool reverse):IProjectTree
    {
        public string Boundary=>inner.Boundary;
        public ProjectEntry Observe(string path)
        {
            if(!ProjectPath.contains("/repo",path))throw new InvalidOperationException("Scanned outside supplied root");
            if(path=="/repo/linked")return ProjectEntry.Link;
            var entry=inner.Observe(path);
            if(entry is not ProjectEntry.Directory directory)return entry;
            var children=directory.children.AsEnumerable();if(path=="/repo")children=children.Append("/repo/linked");
            if(reverse)children=children.Reverse();return ProjectEntry.NewDirectory(Microsoft.FSharp.Collections.ListModule.OfSeq(children));
        }
    }
}

public sealed class WorkspaceMigrationRecoveryPreviewTests
{
    [Test]
    public void PendingJournalMakesPreviewRecoveryRequiredBeforeAnyNewWrite()
    {
        using var fixture=new WorkspaceFixture();
        fixture.Write("a/morphir.json","{name:'A',exposedModules:[]}");
        var target=fixture.Root+"/a/morphir.toml";
        var initial=new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(fixture.Root)],
            [new ConfigurationFileChange(target,ConfigurationTransaction.Observe(target),System.Text.Encoding.UTF8.GetBytes("[project]\nname='A'\n"))]);
        var failed=new ConfigurationTransaction(fault:(phase,_)=>{if(phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback)throw new IOException("injected interrupted batch");}).Apply(initial);
        failed.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        var journal=failed.Artifacts.First(a=>a.Kind==ConfigurationArtifactKind.Journal).Path;
        try
        {
            var preview=fixture.Service.Plan(fixture.Root);
            preview.CanApply.Should().BeFalse("pending recovery makes the plan unavailable even in dry-run");
            preview.Diagnostics.Should().Contain(d=>d.Code=="workspace.migrate_recovery_required");
            File.Exists(fixture.Root+"/morphir.toml").Should().BeFalse();
        }
        finally
        {
            new ConfigurationTransaction().Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        }
    }
}

public sealed class WorkspaceMigrationDeclaredControlTests
{
    [Test]
    public void AdoptionCannotHideChangedProfileDeclarationsBehindDisabledActivation()
    {
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/morphir.json"]="{name:'Root',exposedModules:[],profiles:{default:['root-only']}}",
            ["/repo/app/morphir.json"]="{name:'App',exposedModules:[]}"
        })).Plan("/repo");
        plan.CanApply.Should().BeFalse("disabled activation must not hide newly inherited declaration controls");
        plan.Diagnostics.Should().Contain(d=>d.Code=="workspace.migrate_adoption_value");
    }
    [Test]
    public void EveryUnicodeKeySegmentStillUsesTheProjectLossLedger()
    {
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",new Dictionary<string,string>{
            ["/repo/a/morphir.json"]="{name:'A',exposedModules:[],custom:{'\\ud800':{nested:'retained'}}}"
        })).Plan("/repo");
        plan.CanApply.Should().BeFalse();plan.Projects.Should().ContainSingle();
        plan.Projects[0].Presentation!.Ledger.ToArray().Should().Contain(f=>f.Disposition=="blocking");
    }
}

public sealed class WorkspaceMigrationLayoutTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void AggregateReusesExistingSupportedTomlLayoutRatherThanCreatingAmbiguity(bool rootProject)
    {
        var prefix=rootProject?"":"app/";
        var files=new Dictionary<string,string>{
            ["/repo/"+prefix+".morphir/morphir.json"]="{name:'A',exposedModules:[]}",
            ["/repo/"+prefix+"morphir.toml"]="# keep\n[project]\nname='A'\n"
        };
        if(!rootProject)files["/repo/morphir.toml"]="[workspace]\nmembers=['app']\n";
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",files)).Plan("/repo");
        plan.CanApply.Should().BeTrue(string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        plan.Projects.Should().ContainSingle().Which.TargetPath.Should().Be("/repo/"+prefix+"morphir.toml");
        plan.Projects[0].ProjectPath.Should().Be(rootProject ? "/repo" : "/repo/app");
    }
}

public sealed class WorkspaceMigrationClassicSourcePolicyTests
{
    [Test]
    public void LegacyJson5PrimaryCannotBeMisreportedAsAlreadyMigrated()
    {
        var plan=new WorkspaceMigrationService(new InMemoryProjectTree("/repo",new Dictionary<string,string>{["/repo/a/morphir.json5"]="{name:'A',exposedModules:[]}"})).Plan("/repo");
        plan.CanApply.Should().BeFalse("batch cannot bypass the single-project source contract by calling legacy JSON5 migrated");
        plan.Diagnostics.Should().Contain(d=>d.Code=="migration.classic_required");
        plan.Inventory.Should().Contain(i=>i.RelativePath=="a"&&i.Kind==WorkspaceInventoryKind.Candidate);
    }
}
