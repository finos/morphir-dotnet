// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class WorkspaceMigrationNoopFormatTests
{
    [Test]
    [Category("PlatformSpecific")]
    [Arguments("morphir.yaml", "workspace:\n  members: [app]\ncustom:\n  enabled: false\n")]
    [Arguments("morphir.yml", "workspace:\n  members: [app]\n")]
    [Arguments("morphir.json", "{workspace:{members:['app']}}")]
    [Arguments("morphir.json5", "{workspace:{members:['app']}}")]
    [Arguments("morphir-workspace.json", "{workspace:{members:['app']}}")]
    [Arguments(".config/morphir/config.yaml", "workspace:\n  members: [app]\n")]
    public void ExistingNonTomlWorkspaceWithNoConversionsDoesNotCreateAnEmptyPrimary(string primary, string text)
    {
        using var fixture = new WorkspaceFixture();
        fixture.Write(primary, text);
        fixture.Write("app/morphir.toml", "# preserve member\n[project]\nname='App'\n");
        var source = File.ReadAllBytes(ProjectPath.join(fixture.Root, primary));
        var preview = fixture.Service.Plan(fixture.Root);
        preview.CanApply.Should().BeTrue(string.Join(",", preview.Diagnostics.Select(d => d.Code)));
        preview.Changed.Should().BeFalse();
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Unchanged);
        File.Exists(fixture.Root + "/morphir.toml").Should().BeFalse();
        File.ReadAllBytes(ProjectPath.join(fixture.Root, primary)).Should().Equal(source);
    }
}

public sealed class WorkspaceMigrationApiBoundaryTests
{
    [Test]
    [Arguments(null, "workspace.migrate_boundary")]
    [Arguments("", "workspace.migrate_boundary")]
    [Arguments("relative", "workspace.migrate_boundary")]
    [Arguments("/outside", "workspace.migrate_boundary")]
    [Arguments("/repo/missing", "workspace.migrate_root")]
    [Arguments("/repo/app/morphir.json", "workspace.migrate_root")]
    public void InvalidInvocationHasAStructuredBoundaryFailure(string? root, string code)
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/app/morphir.json"] = "{name:'App',exposedModules:[]}" });
        var preview = new WorkspaceMigrationService(tree).Plan(root!);
        preview.CanApply.Should().BeFalse();
        preview.Diagnostics.Should().Contain(d => d.Code == code);
    }

    [Test]
    [Arguments("root", "workspace.migrate_boundary")]
    [Arguments("candidate", "workspace.migrate_membership")]
    public void DeclaredWorkspaceBaseCannotBroadenTheOperationOrSilentlyAbsorbOtherRoots(string mode, string code)
    {
        var files = new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=[]\nroot='" + (mode == "root" ? "../outside" : "sub") + "'\n",
            ["/repo/app/morphir.json"] = "{name:'App',exposedModules:[]}"
        };
        var preview = new WorkspaceMigrationService(new InMemoryProjectTree("/repo", files, ["/repo/sub"])).Plan("/repo");
        preview.CanApply.Should().BeFalse();
        preview.Diagnostics.Should().Contain(d => d.Code == code);
    }

    [Test]
    [Arguments("listing", "workspace.migrate_listing")]
    [Arguments("directory", "workspace.migrate_inventory")]
    [Arguments("link-root", "workspace.migrate_root")]
    [Arguments("io", "workspace.migrate_io")]
    [Arguments("changing", "workspace.migrate_stale")]
    public void BrokenOrChangingMountedEvidenceNeverBecomesApplicable(string mode, string code)
    {
        var preview = new WorkspaceMigrationService(new FaultTree(mode)).Plan("/repo");
        preview.CanApply.Should().BeFalse();
        preview.Diagnostics.Should().Contain(d => d.Code == code);
        preview.Diagnostics.Should().OnlyContain(d => !d.Message.Contains("private-host-detail", StringComparison.Ordinal));
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments(false)]
    [Arguments(true)]
    public void StorageDriftOrReadFailureIsStructuredBeforeAnyWrite(bool drift)
    {
        using var fixture = new WorkspaceFixture();
        fixture.Write("app/morphir.json", "{name:'App',exposedModules:[]}");
        var service = new WorkspaceMigrationService(new FileSystemProjectTree(fixture.Root), new ReadBoundaryHost(drift));
        var preview = service.Plan(fixture.Root);
        preview.CanApply.Should().BeFalse();
        preview.Diagnostics.Should().Contain(d => d.Code == (drift ? "workspace.migrate_stale" : "workspace.migrate_io"));
        Directory.GetFiles(fixture.Root, "*.toml", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Test]
    [Category("PlatformSpecific")]
    public void BatchRetainsFullJson5EscapesAndOpaqueArrayReferences()
    {
        using var fixture = new WorkspaceFixture();
        const string source = "{name:'App',exposedModules:[],custom:{escaped:'\\b\\f\\n\\r\\t',refs:[{env:'PRIVATE_REFERENCE'}]}}";
        fixture.Write("app/morphir.json", source);
        var preview = fixture.Service.Plan(fixture.Root);
        preview.CanApply.Should().BeTrue(string.Join(",", preview.Diagnostics.Select(d => d.Code)));
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
        File.ReadAllText(fixture.Root + "/app/morphir.json").Should().Be(source);
        var result = ConfigurationReader.ReadFile(new SourceIdentity(fixture.Root + "/app/morphir.toml", DocumentFormat.Toml, SourceRole.MemberPrimary, 0, null));
        var document = ((SourceContent.Available)result.Content).Item;
        ConfigTree.tryFind(Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "custom", "escaped" }), document.Values)!.Value.Should().Be(ConfigValue.NewString("\b\f\n\r\t"));
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments("utf8")]
    [Arguments("utf16le")]
    [Arguments("utf16be")]
    [Arguments("utf32le")]
    [Arguments("utf32be")]
    public void WorkspaceMembershipMergeRetainsTheExistingEncodingAndBom(string kind)
    {
        using var fixture = new WorkspaceFixture();
        System.Text.Encoding encoding = kind switch
        {
            "utf16le" => new System.Text.UnicodeEncoding(false, true, true),
            "utf16be" => new System.Text.UnicodeEncoding(true, true, true),
            "utf32le" => new System.Text.UTF32Encoding(false, true, true),
            "utf32be" => new System.Text.UTF32Encoding(true, true, true),
            _ => new System.Text.UTF8Encoding(true, true)
        };
        var path = fixture.Root + "/morphir.toml";
        const string original = "# retain é\r\n[workspace]\r\nmembers=[]\r\n";
        File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(original)]);
        fixture.Write("app/morphir.json", "{name:'App',exposedModules:[]}");
        var preview = fixture.Service.Plan(fixture.Root);
        preview.CanApply.Should().BeTrue();
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
        var bytes = File.ReadAllBytes(path);
        bytes.Take(encoding.GetPreamble().Length).Should().Equal(encoding.GetPreamble());
        encoding.GetString(bytes.AsSpan(encoding.GetPreamble().Length)).Should().Contain("# retain é\r\n");
    }

    [Test]
    public void ExpensiveFilterEvaluationReturnsAStructuredFailureWithoutThrowing()
    {
        var name = new string('a', 100) + "b" + new string('a', 49);
        var pattern = string.Concat(Enumerable.Repeat("*a", 50)) + "b" + string.Concat(Enumerable.Repeat("*a", 50));
        var service = new WorkspaceMigrationService(new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/" + name + "/morphir.json"] = "{name:'App',exposedModules:[]}"
        }));
        WorkspaceMigrationPreview? preview = null;
        Action plan = () => preview = service.Plan("/repo", [pattern]);
        plan.Should().NotThrow("bounded matching failures must remain scriptable structured outcomes");
        preview!.CanApply.Should().BeFalse();
    }

    private sealed class FaultTree(string mode) : IProjectTree
    {
        private readonly IProjectTree inner = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/app/morphir.json"] = "{name:'App',exposedModules:[]}" });
        private int reads;
        public string Boundary => "/repo";
        public ProjectEntry Observe(string path)
        {
            if (mode == "link-root" && path == Boundary) return ProjectEntry.Link;
            if (mode == "listing" && path == Boundary) return ProjectEntry.NewDirectory(Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "/repo/nested/not-immediate" }));
            if (mode == "directory" && path == "/repo/app") return ProjectEntry.NewFailure("private-host-detail");
            if (mode == "io" && path == "/repo/app/morphir.json") throw new IOException("private-host-detail");
            if (mode == "changing" && path == "/repo/app/morphir.json" && ++reads > 1)
                return ProjectEntry.NewFile(ConfigurationReader.ReadText(path, "{name:'Changed',exposedModules:[]}").Content);
            return inner.Observe(path);
        }
    }

    private sealed class ReadBoundaryHost(bool drift) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost inner = new();
        private int observations;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null)
        {
            var observed = inner.Observe(path, excludedPaths);
            return drift && path.EndsWith("morphir.json", StringComparison.Ordinal) && ++observations > 1 ? observed with { Identity = "changed" } : observed;
        }
        public byte[] ReadFile(string path) => drift ? inner.ReadFile(path) : throw new IOException("private-host-detail");
        public IDisposable Acquire(IReadOnlyList<string> paths) => throw new InvalidOperationException("Preview acquired a writer");
        public IReadOnlyList<string> CreateParentDirectories(string destination) => throw new InvalidOperationException("Preview created directories");
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => throw new InvalidOperationException("Preview staged files");
        public void Install(string stagedPath, string destination, bool overwrite) => throw new InvalidOperationException("Preview installed files");
        public void DeleteFile(string path) => throw new InvalidOperationException("Preview deleted files");
        public void DeleteDirectory(string path) => throw new InvalidOperationException("Preview deleted directories");
    }
}
