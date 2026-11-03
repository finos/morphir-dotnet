// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class ProjectMigrationTests
{
    private const string Classic = "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Main'],custom:{token:'hidden'}}";

    [Test]
    [Arguments("morphir.toml")]
    [Arguments(".morphir/morphir.toml")]
    [Arguments(".config/morphir/config.toml")]
    public void ReadOnlyHostReturnsVerifiedPlanForEverySameProjectLayout(string output)
    {
        var files = new Dictionary<string, string> { ["/repo/morphir.json"] = Classic };
        var tree = new InMemoryProjectTree("/repo", files);
        var preview = new ProjectMigrationService(tree).Plan("/repo", outputPath: output);
        preview.CanApply.Should().BeTrue();
        preview.TargetPath.Should().Be("/repo/" + output);
        preview.Verification!.Verified.Should().BeTrue();
        preview.Plan!.Ledger.Should().NotBeEmpty();
        tree.ObservedPaths.Should().NotContain(p => p.Contains(".user.", StringComparison.Ordinal));
        files.Should().HaveCount(1);
    }

    [Test]
    [Arguments("../morphir.toml")]
    [Arguments("nested/morphir.toml")]
    [Arguments("morphir.user.toml")]
    [Arguments("morphir.yaml")]
    public void InvalidOutputsBlockWithoutAnApplyPlan(string output)
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.json"] = Classic });
        var preview = new ProjectMigrationService(tree).Plan("/repo", outputPath: output);
        preview.CanApply.Should().BeFalse();
        preview.Diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Test]
    public void MemberPreviewKeepsBaseWorkspaceAndIgnoresPersonalProfileAndSiblings()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['orders','other']\nout_dir='build'\n[pipeline]\nbackend='shared'\n",
            ["/repo/orders/morphir.json"] = Classic,
            ["/repo/other/morphir.json"] = "{name:'Other',exposedModules:[]}",
            ["/repo/orders/morphir.user.toml"] = "[project]\nsource_directory='WRONG'\n",
            ["/repo/orders/morphir.secret.toml"] = "[project]\nsource_directory='WRONG'\n"
        });
        var preview = new ProjectMigrationService(tree).Plan("/repo/orders");
        preview.CanApply.Should().BeTrue();
        preview.ProjectPath.Should().Be("/repo/orders");
        preview.Verification!.Verified.Should().BeTrue();
        tree.ObservedPaths.Should().NotContain(p => p.Contains(".user.", StringComparison.Ordinal));
    }
}

public sealed class ProjectMigrationApplicationTests
{
    [Test]
    [Arguments("morphir.json", "morphir.toml")]
    [Arguments(".morphir/morphir.json", ".morphir/morphir.toml")]
    [Arguments(".config/morphir/config.json", ".config/morphir/config.toml")]
    public void DefaultOutputIsAdjacentAndRepeatLeavesExactBytesAndTimestamp(string sourceName, string targetName)
    {
        using var fixture = new Fixture();
        var source = fixture.Write(sourceName, "{name:'Orders',sourceDirectory:'../shared/elm',exposedModules:[],custom:{nested:{keep:true}}}\r\n");
        var bytes = File.ReadAllBytes(source);
        var preview = fixture.Service.Plan(fixture.Root, source);
        preview.CanApply.Should().BeTrue(string.Join(",", preview.Diagnostics.Select(d => d.Code)));
        preview.TargetPath.Should().Be(ProjectPath.join(fixture.Root, targetName));
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
        File.ReadAllBytes(source).Should().Equal(bytes);
        var target = preview.TargetPath!;
        var original = File.ReadAllBytes(target);
        var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(target, timestamp);
        var repeated = fixture.Service.Plan(fixture.Root);
        repeated.CanApply.Should().BeTrue();
        repeated.Changed.Should().BeFalse();
        fixture.Service.Apply(repeated).Status.Should().Be(ConfigurationTransactionStatus.Unchanged);
        File.GetLastWriteTimeUtc(target).Should().Be(timestamp);
        File.ReadAllBytes(target).Should().Equal(original);
        File.ReadAllBytes(source).Should().Equal(bytes);
    }

    [Test]
    [Arguments("morphir.json")]
    [Arguments("morphir.toml")]
    [Arguments("../morphir.toml")]
    [Arguments(".morphir/morphir.toml")]
    public void SourceTargetWorkspaceAndAlternatePrimaryDriftPreventsInstallation(string changed)
    {
        using var fixture = new Fixture();
        fixture.Write("morphir.toml", "[workspace]\nmembers=['app']\nout_dir='before'\n");
        fixture.Write("app/morphir.json", "{name:'App',exposedModules:[]}\n");
        fixture.Write("app/morphir.toml", "# preserve\n[project]\nname='App'\n");
        var preview = fixture.Service.Plan(ProjectPath.join(fixture.Root, "app"));
        preview.CanApply.Should().BeTrue();
        var target = File.ReadAllBytes(preview.TargetPath!);
        fixture.Write("app/" + changed, changed.EndsWith(".json", StringComparison.Ordinal)
            ? "{name:'Changed',exposedModules:[]}\n" : "[custom]\ndrift=true\n");
        var afterDrift = File.ReadAllBytes(preview.TargetPath!);
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Conflict);
        File.ReadAllBytes(preview.TargetPath!).Should().Equal(afterDrift);
    }

    [Test]
    public void BlockedAndReadonlyPlansCannotInvokeWriterAndCannotBeTransferredToAnotherService()
    {
        using var fixture = new Fixture();
        fixture.Write("morphir.json", "{name:'App',exposedModules:[],decorations:{lost:true}}\n");
        var blocked = fixture.Service.Plan(fixture.Root);
        blocked.CanApply.Should().BeFalse();
        fixture.Service.Apply(blocked).Code.Should().Be("migration.unverified_plan");
        File.Exists(ProjectPath.join(fixture.Root, "morphir.toml")).Should().BeFalse();
        fixture.Write("morphir.json", "{name:'App',exposedModules:[]}\n");
        var readOnlyService = new ProjectMigrationService(new FileSystemProjectTree(fixture.Root));
        var readOnly = readOnlyService.Plan(fixture.Root);
        readOnly.CanApply.Should().BeTrue();
        readOnlyService.Apply(readOnly).Status.Should().Be(ConfigurationTransactionStatus.IoFailure);
        fixture.Service.Apply(readOnly).Code.Should().Be("migration.unverified_plan");
    }

    [Test]
    public void ExistingTargetAddsOnlyMissingEntriesAndRetainsBomAndComments()
    {
        using var fixture = new Fixture();
        fixture.Write("morphir.json", "{name:'App',exposedModules:[],custom:{a:1,b:2}}\n");
        var target = ProjectPath.join(fixture.Root, "morphir.toml");
        const string original = "# keep é\r\n[project]\r\nname='App' # keep\r\n[custom]\r\na=1 # keep\r\n";
        var encoding = new System.Text.UnicodeEncoding(false, true, true);
        File.WriteAllBytes(target, [.. encoding.GetPreamble(), .. encoding.GetBytes(original)]);
        var preview = fixture.Service.Plan(fixture.Root);
        preview.CanApply.Should().BeTrue();
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
        var bytes = File.ReadAllBytes(target);
        bytes.Take(2).Should().Equal(encoding.GetPreamble());
        encoding.GetString(bytes.AsSpan(2)).Should().StartWith("# keep é\r\n[project]\r\nname='App' # keep\r\n").And.Contain("a=1 # keep");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "migration-application-" + Guid.NewGuid().ToString("N")));
        public ProjectMigrationService Service { get; }
        public Fixture() { Directory.CreateDirectory(Root); Service = new(new FileSystemProjectTree(Root), new NativeConfigurationTransactionHost()); }
        public string Write(string path, string text)
        {
            path = ProjectPath.join(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
