using FluentAssertions;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO.Tests;

public sealed class DiscoveryMemberTests
{
    private static Dictionary<string, string> Files(string workspace = "members=['packages/**']\nexclude=['**/skip']") => new()
    {
        ["/repo/morphir.toml"] = "[workspace]\n" + workspace,
        ["/repo/packages/a/morphir.json"] = "{project:{name:'Same'}}",
        ["/repo/packages/a/inner/morphir.toml"] = "[project]\nname='Inner'",
        ["/repo/packages/b/morphir.yml"] = "project: {name: Same}",
        ["/repo/packages/skip/morphir.toml"] = "[project]\nname='Skip'",
        ["/repo/.config/morphir/config.user.toml"] = "[project]\nname='Hidden'"
    };
    private static ProjectDiscoveryResult Discover(Dictionary<string, string> files, string start = "/repo", string? selector = null) => ProjectSystem.Discover(new InMemoryProjectTree("/repo", files), start, selector);

    [Test]
    public void GlobsExcludeMembersAndRetainDeterministicOrder()
    {
        var result = Discover(Files());
        result.Discovery.Projects.Select(p => p.Path).Should().Equal("/repo/packages/a", "/repo/packages/a/inner", "/repo/packages/b");
    }

    [Test]
    public void RecursiveGlobNeverTurnsVirtualRootOrHiddenConfigDirectoriesIntoProjects()
    {
        var files = Files("members=['**']\nexclude=['**/skip']");
        files["/repo/.morphir/morphir.user.toml"] = "[project]\nname='Hidden'";
        var result = Discover(files);
        result.Discovery.Projects.Select(p => p.Path).Should().Equal("/repo/packages/a", "/repo/packages/a/inner", "/repo/packages/b");
    }

    [Test]
    public void RootProjectIsEligibleEvenWithoutAGlobMatch()
    {
        var files = Files("members=[]");
        files["/repo/morphir.toml"] += "\n[project]\nname='Root'";
        Discover(files).Discovery.Projects.Select(p => p.Path).Should().Equal("/repo");
    }

    [Test]
    public void WorkspaceRootAnchorsMembersAndOutput()
    {
        var result = Discover(Files("root='packages'\nmembers=['a','b']\nout_dir='generated'"));
        result.Discovery.Projects.Select(p => p.Path).Should().Equal("/repo/packages/a", "/repo/packages/b");
        result.Discovery.Workspace!.Value.Root.Should().Be("/repo/packages");
        result.Discovery.Workspace.Value.OutputDirectory!.Value.Should().Be("/repo/packages/generated");
    }

    [Test]
    public void MissingAndBrokenLiteralMembersRemainVisible()
    {
        var files = Files("members=['missing','empty','bad']");
        files["/repo/empty/source.elm"] = "";
        files["/repo/bad/morphir.toml"] = "[not valid";
        var result = Discover(files);
        result.Discovery.Projects.Select(p => p.Path).Should().Equal("/repo/bad", "/repo/empty", "/repo/missing");
        result.Discovery.Projects.ToArray().Should().OnlyContain(p => !p.IsValid);
        result.Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.missing_member" && d.Severity == DiagnosticSeverity.Warning)
            .And.Contain(d => d.Code == "discovery.member_configuration").And.Contain(d => d.Code == "config.parse_error");
    }

    [Test]
    public void MemberAmbiguityFailsDiscovery()
    {
        var files = Files("members=['packages/a']");
        files["/repo/packages/a/morphir.json5"] = "{project:{name:'Other'}}";
        Discover(files).Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.ambiguous" && d.Message.Contains("morphir.json5"));
    }

    [Test]
    public void DuplicateNamesAreDiagnosedAndNameSelectionFails()
    {
        var result = Discover(Files(), selector: "Same");
        result.Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.duplicate_name");
        result.Selection.Projects.ToArray().Should().BeEmpty();
        result.Selection.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.ambiguous_selector");
    }

    [Test]
    public void ExactPathWinsBeforeADifferentProjectsName()
    {
        var files = Files();
        files["/repo/packages/b/morphir.yml"] = "project: {name: packages/a}";
        Discover(files, selector: "packages/a").Selection.Projects.Select(p => p.Path).Should().Equal("/repo/packages/a");
    }

    [Test]
    public void DeepestContainingMemberWins()
    {
        var files = Files(); files["/repo/packages/a/inner/src/file.elm"] = "";
        Discover(files, "/repo/packages/a/inner/src/file.elm").Selection.Projects.Select(p => p.Path).Should().Equal("/repo/packages/a/inner");
    }

    [Test]
    public void DefaultMemberUsesPathBeforeNames()
    {
        Discover(Files("members=['packages/*']\nexclude=['**/skip']\ndefault_member='packages/b'"))
            .Selection.Projects.Select(p => p.Path).Should().Equal("/repo/packages/b");
    }

    [Test]
    [Arguments("unknown")]
    [Arguments("Same")]
    public void InvalidDefaultIsAnErrorEvenWhenInvocationContainsAMember(string value)
    {
        var files = Files("members=['packages/*']\nexclude=['**/skip']\ndefault_member='" + value + "'");
        var result = Discover(files, "/repo/packages/a");
        result.Selection.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.invalid_default");
    }

    [Test]
    public void UnknownExplicitSelectorIsAnError()
    {
        Discover(Files(), selector: "unknown").Selection.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.unknown_selector");
    }

    [Test]
    public void NoSelectorAtRootReturnsAllValidProjects()
    {
        Discover(Files()).Selection.Projects.Select(p => p.Path).Should().Equal("/repo/packages/a", "/repo/packages/a/inner", "/repo/packages/b");
    }

    [Test]
    public void OutsideMemberOrWorkspaceRootIsRejectedWithoutEscapingReads()
    {
        var tree = new InMemoryProjectTree("/repo", Files("members=['../outside']"));
        var result = ProjectSystem.Discover(tree, "/repo");
        result.Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.boundary");
        tree.ObservedPaths.Should().OnlyContain(p => p == "/repo" || p.StartsWith("/repo/"));
    }
}
