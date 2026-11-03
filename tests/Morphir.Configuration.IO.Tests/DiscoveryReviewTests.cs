using FluentAssertions;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO.Tests;

public sealed class DiscoveryReviewTests
{
    private sealed class FailingMemberTree(string pattern, string exclude, string allowed) : IProjectTree
    {
        private readonly InMemoryProjectTree _inner = new("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['" + pattern + "']\nexclude=['" + exclude + "']",
            ["/repo/packages/" + allowed + "/morphir.toml"] = "[project]\nname='Good'",
            ["/repo/packages/good/deep/locked/file.txt"] = "",
            ["/repo/packages/excluded/locked/file.txt"] = ""
        });
        public string Boundary => "/repo";
        public List<string> Observed { get; } = [];
        public ProjectEntry Observe(string path)
        {
            Observed.Add(path);
            return path.EndsWith("/locked", StringComparison.Ordinal)
                ? ProjectEntry.NewFailure("Unrelated directory is inaccessible") : _inner.Observe(path);
        }
    }

    [Test]
    [Arguments("packages/*", "packages/excluded", "good")]
    [Arguments("packages/*/leaf", "packages/excluded/**", "good/leaf")]
    public void InventoryPrunesExcludedAndImpossibleDescendantsBeforeReading(string pattern, string exclude, string allowed)
    {
        var tree = new FailingMemberTree(pattern, exclude, allowed);
        var result = ProjectSystem.Discover(tree, "/repo");
        result.Discovery.Projects.Select(p => p.Path).Should().Equal("/repo/packages/" + allowed);
        result.Discovery.Diagnostics.ToArray().Should().NotContain(d => d.Code == "discovery.read_error");
        tree.Observed.Should().NotContain(p => p == "/repo/packages/excluded"
            || p.StartsWith("/repo/packages/excluded/", StringComparison.Ordinal)
            || p.StartsWith("/repo/packages/good/deep", StringComparison.Ordinal));
    }

    [Test]
    public void DiscoveredMemberSourcesResolveDirectlyWithTheirOwnIdentityAndLegacyDefaults()
    {
        const string memberPath = "/repo/member/morphir.json";
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['member']\n[project]\nname='Root'",
            [memberPath] = "{name:'Member',exposedModules:['Api']}",
            ["/repo/member/src/input.elm"] = ""
        });
        var discovery = ProjectSystem.Discover(tree, "/repo/member/src").Discovery;
        var member = discovery.Projects.Single(p => p.Path == "/repo/member");
        member.Location.Sources.ToArray().Should().OnlyContain(s => s.Source.Role == SourceRole.MemberPrimary);
        member.Location.Sources.Select(s => ((SourceContent.Available)s.Content).Item.Source.Role)
            .Should().OnlyContain(role => role == SourceRole.MemberPrimary);
        discovery.Sources.Where(s => s.Source.Path == memberPath).Should().NotBeEmpty()
            .And.OnlyContain(s => s.Source.Role == SourceRole.MemberPrimary);

        var input = new ResolutionInput(ListModule.OfSeq(discovery.Root.Sources.Concat(member.Location.Sources)),
            MapModule.Empty<string, string>(), DocumentDefaults.emptyValue,
            new ProfileOptions(ProfileSelection.Disabled, 32, 4096), ProjectScope.Member,
            discovery.Workspace, FSharpList<string>.Empty);
        var result = Resolver.resolve(input);
        ProjectLoadingTests.Value(result, "project", "name").Should().Be(ConfigValue.NewString("Member"));
        ProjectLoadingTests.Value(result, "project", "exposed_modules").Should()
            .Be(ConfigValue.NewArray(ListModule.OfSeq(new[] { ConfigValue.NewString("Api") })));
        ProjectLoadingTests.Value(result, "pipeline", "backend").Should().Be(ConfigValue.NewString("checkpoint"));
    }
}
