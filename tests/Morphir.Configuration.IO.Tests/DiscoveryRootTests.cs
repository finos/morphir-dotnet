using FluentAssertions;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO.Tests;

public sealed class DiscoveryRootTests
{
    private static InMemoryProjectTree Tree(Dictionary<string, string> files, string boundary = "/repo") => new(boundary, files, ["/repo/app/src"]);

    [Test]
    public void SupplementalJsonLayersBelowOrdinaryJsonAndOtherFormats()
    {
        var result = ProjectSystem.Discover(Tree(new()
        {
            ["/repo/morphir-workspace.json"] = "{workspace:{members:[],out_dir:'old'},custom:{a:1}}",
            ["/repo/morphir.json5"] = "{workspace:{out_dir:'json'},custom:{b:2}}",
            ["/repo/morphir.yml"] = "workspace: {out_dir: yaml}",
            ["/repo/morphir.toml"] = "[workspace]\nout_dir='toml'"
        }), "/repo/app/src");
        result.Discovery.Root.Sources.Select(s => s.Source.Path).Should().Equal("/repo/morphir-workspace.json", "/repo/morphir.json5", "/repo/morphir.yml", "/repo/morphir.toml");
        ConfigTree.tryFind(ListModule.OfSeq(new[] { "workspace", "out_dir" }), result.Discovery.Root.Values).Value.Should().Be(ConfigValue.NewString("toml"));
    }

    [Test]
    [Arguments("morphir.json", "morphir.json5")]
    [Arguments("morphir.yaml", "morphir.yml")]
    [Arguments("morphir.toml", ".config/morphir/config.toml")]
    public void EveryGroupRejectsAmbiguity(string first, string second)
    {
        var text = first.EndsWith("toml") ? "[project]" : first.EndsWith("yaml") ? "project: {}" : "{project:{}}";
        var result = ProjectSystem.Discover(Tree(new() { ["/repo/" + first] = text, ["/repo/" + second] = text }), "/repo");
        result.Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.ambiguous" && d.Message.Contains(first) && d.Message.Contains(second));
    }

    [Test]
    public void NearestWorkspaceStopsBeforeOuterReads()
    {
        var tree = Tree(new()
        {
            ["/repo/morphir.toml"] = "invalid TOML!",
            ["/repo/app/morphir.toml"] = "[workspace]\nmembers=[]"
        });
        ProjectSystem.Discover(tree, "/repo/app/src").Discovery.Location.Should().Be("/repo/app");
        tree.ObservedPaths.Should().NotContain("/repo/morphir.toml");
    }

    [Test]
    public void StandaloneFallbackDoesNotMergeAncestors()
    {
        var tree = Tree(new() { ["/repo/morphir.json"] = "{custom:{outer:true}}", ["/repo/app/morphir.json"] = "{project:{name:'App'}}" });
        var result = ProjectSystem.Discover(tree, "/repo/app/src");
        result.Discovery.Location.Should().Be("/repo/app");
        result.Discovery.Root.Sources.Select(s => s.Source.Path).Should().Equal("/repo/app/morphir.json");
    }

    [Test]
    public void GitAndEmptyHiddenDirectoriesLeaveStartAndOptionalMissEvidence()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>(), ["/repo/.git", "/repo/.morphir", "/repo/app/src"]);
        var result = ProjectSystem.Discover(tree, "/repo/app/src");
        result.Discovery.Location.Should().Be("/repo/app/src");
        result.Discovery.Workspace.Should().BeNull();
        result.Discovery.Sources.ToArray().Should().Contain(s => s.Status.IsMissing);
    }

    [Test]
    public void MountedBoundaryIsInclusiveAndNeverReadAbove()
    {
        var tree = new InMemoryProjectTree("/repo/app", new Dictionary<string, string> { ["/repo/morphir.toml"] = "[workspace]", ["/repo/app/morphir.json"] = "{project:{}}" }, ["/repo/app/src"]);
        ProjectSystem.Discover(tree, "/repo/app/src").Discovery.Location.Should().Be("/repo/app");
        tree.ObservedPaths.Should().OnlyContain(p => p.StartsWith("/repo/app/"));
    }

    [Test]
    public void OutsideStartIsRejectedWithoutHostAccess()
    {
        var tree = Tree(new());
        var result = ProjectSystem.Discover(tree, "/other");
        tree.ObservedPaths.Should().BeEmpty();
        result.Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.boundary");
    }

    private sealed class FaultTree(ProjectEntry entry) : IProjectTree
    {
        public string Boundary => "/repo";
        public ProjectEntry Observe(string path) => path == "/repo/morphir.toml" ? entry : path == "/repo" ? ProjectEntry.NewDirectory(FSharpList<string>.Empty) : ProjectEntry.Missing;
    }

    [Test]
    public void InaccessibleCandidateIsAFailureRatherThanAnOptionalMiss()
    {
        var result = ProjectSystem.Discover(new FaultTree(ProjectEntry.NewFailure("access denied")), "/repo");
        result.Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.read_error");
        result.Discovery.Sources.ToArray().Should().Contain(s => s.Source.Path.EndsWith("morphir.toml") && s.Status.IsFailed);
    }

    [Test]
    public void DiscoveredLinkIsSkippedAndDoesNotDeclareWorkspace()
    {
        var result = ProjectSystem.Discover(new FaultTree(ProjectEntry.Link), "/repo");
        result.Discovery.Workspace.Should().BeNull();
        result.Discovery.Sources.ToArray().Should().Contain(s => s.Status.IsSkipped && s.Detail.Contains("link"));
    }

    [Test]
    public void DirectoryAtPrimaryPathIsDiagnosed()
    {
        var result = ProjectSystem.Discover(new FaultTree(ProjectEntry.NewDirectory(FSharpList<string>.Empty)), "/repo");
        result.Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.invalid_candidate");
    }
}
