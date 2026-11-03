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
        preview.Plan!.Ledger.ToArray().Should().NotBeEmpty();
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
    [Arguments("app", false)]
    [Arguments(null, false)]
    [Arguments("app/src", false)]
    [Arguments("app", true)]
    [Arguments(null, true)]
    public void UnlistedDirectoryCannotSelectAnotherProject(string? input, bool rootProject)
    {
        var files = new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['other']\n",
            ["/repo/app/morphir.json"] = Classic,
            ["/repo/other/morphir.json"] = "{name:'Other',exposedModules:[]}"
        };
        if (rootProject) files["/repo/morphir.json"] = "{name:'Root',exposedModules:[]}";
        var tree = new InMemoryProjectTree("/repo", files, ["/repo/app/src"]);
        var preview = new ProjectMigrationService(tree).Plan(input is null ? "/repo/app" : "/repo", input);
        preview.CanApply.Should().BeFalse("an unlisted project must not fall back to a sibling or root project");
        preview.Diagnostics.Should().Contain(d => d.Code == "migration.selection");
        files.Keys.Should().NotContain("/repo/other/morphir.toml");
    }

    [Test]
    [Arguments("/repo")]
    [Arguments("/repo/other/src")]
    public void WorkspaceRootAndDeclaredMemberSubdirectoriesStillSelectTheMember(string start)
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['other']\n",
            ["/repo/other/morphir.json"] = Classic
        }, ["/repo/other/src"]);
        var preview = new ProjectMigrationService(tree).Plan(start);
        preview.CanApply.Should().BeTrue();
        preview.ProjectPath.Should().Be("/repo/other");
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
        preview.ProjectPath.Should().Be(fixture.Root, "explicit primary files retain their project location anchor");
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
        encoding.GetString(bytes.AsSpan(2)).Should().Contain("# keep é\r\n[project]\r\nname='App' # keep\r\n").And.Contain("a=1 # keep");
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

public sealed class ProjectMigrationFailureTests
{
    [Test]
    [Arguments("../outside", null)]
    [Arguments("missing", null)]
    [Arguments("", null)]
    [Arguments(null, "")]
    public void InvalidInvocationBlocksWithoutWriteAccess(string? input, string? output)
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.json"] = "{name:'App',exposedModules:[]}" });
        var result = new ProjectMigrationService(tree).Plan("/repo", input, output);
        result.CanApply.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Test]
    public void AlternatePrimaryAndCanonicalOnlyJsonAreRejected()
    {
        var files = new Dictionary<string, string> { ["/repo/morphir.json"] = "{project:{name:'Canonical'}}" };
        new ProjectMigrationService(new InMemoryProjectTree("/repo", files)).Plan("/repo").CanApply.Should().BeFalse();
        files["/repo/morphir.json"] = "{name:'Classic',exposedModules:[]}";
        files["/repo/.morphir/morphir.toml"] = "[project]\nname='Classic'\n";
        var preview = new ProjectMigrationService(new InMemoryProjectTree("/repo", files)).Plan("/repo");
        preview.Diagnostics.Should().Contain(d => d.Code == "migration.ambiguous_output");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void WriteFaultsReportRollbackOrExplicitOwnedRecovery(bool refuseRollback)
    {
        var root = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "migration-recovery-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var target = root + "/morphir.toml";
        const string source = "{name:'App',exposedModules:[]}\n";
        const string original = "# retained target\n[project]\nname='App'\n";
        var resolved = false;
        try
        {
            File.WriteAllText(root + "/morphir.json", source);
            File.WriteAllText(target, original);
            var host = new FaultHost(refuseRollback);
            var service = new ProjectMigrationService(new FileSystemProjectTree(root), host);
            var preview = service.Plan(root);
            preview.CanApply.Should().BeTrue();
            var result = service.Apply(preview);
            File.ReadAllText(root + "/morphir.json").Should().Be(source);
            if (!refuseRollback)
            {
                result.Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
                result.Artifacts.Should().BeEmpty();
                File.ReadAllText(target).Should().Be(original);
            }
            else
            {
                result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
                result.Artifacts.Should().Contain(a => a.Kind == ConfigurationArtifactKind.Backup)
                    .And.Contain(a => a.Kind == ConfigurationArtifactKind.Journal);
                var backup = result.Artifacts.Single(a => a.Kind == ConfigurationArtifactKind.Backup);
                File.ReadAllText(backup.Path).Should().Be(original);
                result.Artifacts.Select(a => a.TransactionId).Distinct().Should().ContainSingle();
                // Explicit fixture recovery after verifying backup identity and installed candidate.
                File.ReadAllText(target).Should().Contain("checkpoint");
                File.WriteAllBytes(target, File.ReadAllBytes(backup.Path));
                foreach (var artifact in result.Artifacts.Where(a => a.Kind != ConfigurationArtifactKind.Directory)) File.Delete(artifact.Path);
                File.ReadAllText(target).Should().Be(original);
            }
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained migration fault evidence: " + root);
        }
    }

    private sealed class FaultHost(bool refuseRollback) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost inner = new();
        private int installs;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => inner.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => inner.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => inner.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string destination) => inner.CreateParentDirectories(destination);
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => inner.Stage(destination, content, transactionId, role);
        public void Install(string stagedPath, string destination, bool overwrite)
        {
            installs++;
            if (installs > 1 && refuseRollback) throw new IOException("Injected rollback failure");
            inner.Install(stagedPath, destination, overwrite);
            if (installs == 1) throw new IOException("Injected post-install failure");
        }
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);
    }
}

public sealed class ProjectMigrationHostBoundaryTests
{
    [Test]
    public void ReadonlyExistingTargetAndPresentationRetainSafeCompletePlan()
    {
        var tree = Tree("[project]\nname='App'\n[custom]\nother=true\n");
        var preview = new ProjectMigrationService(tree).Plan("/repo");
        preview.CanApply.Should().BeTrue();
        preview.Presentation!.Edits.ToArray().Should().Contain(e => e.Value.Equals(ConfigValue.NewString("[REDACTED]")));
        ConfigTree.tryFind(Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "custom", "password" }), preview.Presentation.Candidate)!.Value
            .Should().Be(ConfigValue.NewString("[REDACTED]"));
        new ProjectMigrationService(tree).Plan("relative").Presentation.Should().BeNull();
        new ProjectMigrationService(tree).Plan("/outside").CanApply.Should().BeFalse();
        new ProjectMigrationService(tree).Plan("/repo", "morphir-ir.json").CanApply.Should().BeFalse();
    }

    [Test]
    public void MalformedAndChangingMountedTargetsBlockWithStructuredErrors()
    {
        var tree = Tree("[project]\nname='App'\n");
        foreach (var mode in new[] { "link", "invalid", "null" })
        {
            var preview = new ProjectMigrationService(new ChangingTree(tree, mode)).Plan("/repo");
            preview.CanApply.Should().BeFalse(mode);
            preview.Diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
        }
        var malformed = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.json"] = "{name:'App',project:{name:'conflict'},exposedModules:[]}" });
        new ProjectMigrationService(malformed).Plan("/repo").CanApply.Should().BeFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void StorageReadFailureAndUnstableObservationNeverCreateApplicablePlans(bool unstable)
    {
        var root = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "migration-evidence-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        const string source = "{name:'App',exposedModules:[]}\n";
        try
        {
            File.WriteAllText(root + "/morphir.json", source);
            var preview = new ProjectMigrationService(new FileSystemProjectTree(root), new EvidenceHost(unstable)).Plan(root);
            preview.CanApply.Should().BeFalse();
            preview.Diagnostics.Should().Contain(d => d.Code == (unstable ? "migration.stale" : "migration.io"));
            File.ReadAllText(root + "/morphir.json").Should().Be(source);
            Directory.GetFiles(root).Should().ContainSingle();
        }
        finally { Directory.Delete(root, true); }
    }

    private static InMemoryProjectTree Tree(string target) => new("/repo", new Dictionary<string, string>
    {
        ["/repo/morphir.json"] = "{name:'App',exposedModules:[],custom:{password:'private-value'}}",
        ["/repo/morphir.toml"] = target
    });

    private sealed class ChangingTree(IProjectTree inner, string mode) : IProjectTree
    {
        private int targetReads;
        public string Boundary => inner.Boundary;
        public ProjectEntry Observe(string path)
        {
            if (path != "/repo/morphir.toml") return inner.Observe(path);
            targetReads++;
            if (mode == "link") return ProjectEntry.Link;
            if (mode == "invalid" && targetReads > 1) return ProjectEntry.NewFile(ConfigurationReader.ReadText(path, "[invalid").Content);
            if (mode == "null")
            {
                // Synthetic mounted hosts can supply typed values a TOML parser cannot produce.
                // Retaining such an unrelated target value must fail rendering rather than drop it.
                var parsed = (SourceContent.Available)ConfigurationReader.ReadText(path, "[project]\nname='App'\n").Content;
                var raw = ConfigTree.set(Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "custom", "unrepresentable" }), ConfigValue.Null, parsed.Item.Raw);
                return ProjectEntry.NewFile(SourceContent.NewAvailable(Normalization.normalize(parsed.Item.Source, raw,
                    Microsoft.FSharp.Collections.MapModule.Empty<Microsoft.FSharp.Collections.FSharpList<string>, SourcePosition>())));
            }
            return inner.Observe(path);
        }
    }

    private sealed class EvidenceHost(bool unstable) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost inner = new();
        private int observations;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null)
        {
            var result = inner.Observe(path, excludedPaths);
            if (path.EndsWith("/morphir.json", StringComparison.Ordinal) && ++observations > 1 && unstable)
                return result with { Identity = "changed" };
            return result;
        }
        public byte[] ReadFile(string path) => unstable ? inner.ReadFile(path) : throw new IOException("Injected read failure");
        public IDisposable Acquire(IReadOnlyList<string> paths) => throw new InvalidOperationException("Preview acquired writer coordination");
        public IReadOnlyList<string> CreateParentDirectories(string destination) => throw new InvalidOperationException();
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => throw new InvalidOperationException();
        public void Install(string stagedPath, string destination, bool overwrite) => throw new InvalidOperationException();
        public void DeleteFile(string path) => throw new InvalidOperationException();
        public void DeleteDirectory(string path) => throw new InvalidOperationException();
    }
}
