// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.Configuration.IO.Tests;

[Category("WorkspaceMigrationDiscovery")]
public sealed class WorkspaceMigrationDiscoveryTests
{
    [Test]
    public void ExistingGlobCrossingSkippedOrNestedBoundariesBlocksBeforeAnyWrite()
    {
        using var fixture = new NativeFixture();
        fixture.Write("morphir.toml", "# retain declaration\n[workspace]\nmembers=['apps/**']\n");
        fixture.WriteBoundedTree();
        var original = fixture.Snapshot();

        var preview = fixture.Service.Plan(fixture.Root);

        preview.CanApply.Should().BeFalse("the retained glob would discover independent or skipped projects after application");
        preview.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "workspace.migrate_membership_boundary");
        fixture.Service.Apply(preview).Code.Should().Be("workspace.migrate_unverified");
        fixture.AssertUnchanged(original);
        AssertHardBoundaries(fixture.Tree.ObservedPaths, fixture.Root);
    }

    [Test]
    public void DeclaredExclusionsKeepTheRetainedGlobEquivalentToActualNativeDiscovery()
    {
        using var fixture = new NativeFixture();
        const string workspace = "# retain declaration and exclusions\n[workspace]\nmembers=['apps/**']\nexclude=['apps/node_modules/**','apps/nested/**']\n";
        fixture.Write("morphir.toml", workspace);
        fixture.WriteBoundedTree();
        var original = fixture.Snapshot();

        var preview = fixture.Service.Plan(fixture.Root);
        preview.CanApply.Should().BeTrue(string.Join(",", preview.Diagnostics.Select(diagnostic => diagnostic.Code)));
        preview.Members.Should().Equal("apps/**");
        preview.Projects.Select(project => project.ProjectPath).Should().Equal(fixture.Root + "/apps/a");
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);

        var actualTree = new TrackingTree(new FileSystemProjectTree(fixture.Root));
        var actual = ProjectSystem.Discover(actualTree, fixture.Root);
        actual.Discovery.Projects.ToArray().Should().OnlyContain(project => project.IsValid);
        actual.Discovery.Projects.Select(project => project.Path).Should().Equal(preview.Projects.Select(project => project.ProjectPath));
        actual.Selection.Projects.Select(project => project.Path).Should().Equal(fixture.Root + "/apps/a");
        actual.Discovery.Diagnostics.ToArray().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        fixture.AssertOriginalBytes(original);
        File.ReadAllText(fixture.Root + "/morphir.toml").Should().Be(workspace);
        File.Exists(fixture.Root + "/apps/a/morphir.toml").Should().BeTrue();
        File.Exists(fixture.Root + "/apps/node_modules/pkg/morphir.toml").Should().BeFalse();
        File.Exists(fixture.Root + "/apps/nested/inside/morphir.toml").Should().BeFalse();
        AssertHardBoundaries(fixture.Tree.ObservedPaths, fixture.Root);
        AssertHardBoundaries(actualTree.ObservedPaths, fixture.Root);
    }

    [Test]
    [Arguments("old", "old")]
    [Arguments("retained/old", "retained")]
    public void SuppliedExclusionsRetainDeclaredMemberDefaultAndExactPrimary(string member, string exclusion)
    {
        using var fixture = new NativeFixture();
        fixture.Write("morphir.toml", $"# retain default\n[workspace]\nmembers=['{member}']\ndefault_member='{member}'\nout_dir='shared'\n");
        const string existing = "# preserve this primary byte for byte\n[project]\nname='Old'\nsource_directory='old-src'\n";
        fixture.Write(member + "/morphir.toml", existing);
        fixture.Write("new/morphir.json", "{name:'New',sourceDirectory:'elm-src',exposedModules:[]}\n");
        var source = File.ReadAllBytes(fixture.Root + "/new/morphir.json");
        var oldPath = fixture.Root + "/" + member + "/morphir.toml";
        var time = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(oldPath, time);

        var preview = fixture.Service.Plan(fixture.Root, excludes: [exclusion]);
        preview.CanApply.Should().BeTrue("supplied adoption filters cannot remove an already declared member or default: "
            + string.Join(",", preview.Diagnostics.Select(diagnostic => diagnostic.Code)));
        preview.Members.Should().Equal(member, "new");
        preview.Projects.Should().ContainSingle().Which.ProjectPath.Should().Be(fixture.Root + "/new");
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);

        var actual = ProjectSystem.Discover(new FileSystemProjectTree(fixture.Root), fixture.Root);
        actual.Discovery.Projects.ToArray().Should().OnlyContain(project => project.IsValid);
        actual.Discovery.Projects.Select(project => project.Path).Should().BeEquivalentTo(new[] { fixture.Root + "/" + member, fixture.Root + "/new" });
        actual.Selection.Projects.Select(project => project.Path).Should().Equal(fixture.Root + "/" + member);
        actual.Discovery.Diagnostics.ToArray().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        File.ReadAllText(fixture.Root + "/morphir.toml").Should().Contain($"default_member='{member}'").And.Contain("out_dir='shared'");
        File.ReadAllText(oldPath).Should().Be(existing);
        File.GetLastWriteTimeUtc(oldPath).Should().Be(time);
        File.ReadAllBytes(fixture.Root + "/new/morphir.json").Should().Equal(source);
        File.Exists(fixture.Root + "/new/morphir.toml").Should().BeTrue();
    }

    private static void AssertHardBoundaries(IEnumerable<string> observed, string root)
    {
        var skipped = root + "/apps/node_modules/";
        var nested = root + "/apps/nested";
        var boundaryPrimaries = DiscoveryPolicy.candidates(nested).ToHashSet(StringComparer.Ordinal);
        observed.Should().NotContain(path => path.StartsWith(skipped, StringComparison.Ordinal));
        observed.Should().NotContain(path => path.StartsWith(nested + "/", StringComparison.Ordinal) && !boundaryPrimaries.Contains(path));
    }

    private sealed class TrackingTree(IProjectTree inner) : IProjectTree
    {
        public string Boundary => inner.Boundary;
        public List<string> ObservedPaths { get; } = [];
        public ProjectEntry Observe(string path)
        {
            ObservedPaths.Add(path);
            return inner.Observe(path);
        }
    }

    private sealed class NativeFixture : IDisposable
    {
        public string Root { get; } = ProjectPath.join(Path.GetTempPath(), "workspace-migration-discovery-" + Guid.NewGuid().ToString("N"));
        public TrackingTree Tree { get; }
        public WorkspaceMigrationService Service { get; }
        public NativeFixture()
        {
            Directory.CreateDirectory(Root);
            Tree = new(new FileSystemProjectTree(Root));
            Service = new(Tree, new NativeConfigurationTransactionHost());
        }
        public void Write(string relative, string text)
        {
            var path = ProjectPath.join(Root, relative);
            Directory.CreateDirectory(ProjectPath.parent(path));
            File.WriteAllText(path, text);
        }
        public void WriteBoundedTree()
        {
            Write("apps/a/morphir.json", "{name:'A',sourceDirectory:'elm-src',exposedModules:[]}\n");
            Write("apps/node_modules/pkg/morphir.json", "{name:'Dependency',exposedModules:[]}\n");
            Write("apps/nested/morphir.toml", "[workspace]\nmembers=['inside']\n");
            Write("apps/nested/inside/morphir.json", "{name:'Independent',exposedModules:[]}\n");
        }
        public Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        public void AssertOriginalBytes(IReadOnlyDictionary<string, byte[]> original)
        {
            foreach (var file in original) File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        }
        public void AssertUnchanged(IReadOnlyDictionary<string, byte[]> original)
        {
            Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(original.Keys);
            AssertOriginalBytes(original);
        }
        public void Dispose()
        {
            if (Directory.EnumerateFileSystemEntries(Root, ".morphir-edit-*", SearchOption.AllDirectories).Any())
                Console.Error.WriteLine("Retained workspace discovery recovery evidence: " + Root);
            else Directory.Delete(Root, true);
        }
    }
}
