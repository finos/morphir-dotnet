// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO.Tests;

[Binding]
public sealed class InspectionSteps
{
    private InMemoryProjectTree _tree = null!;
    private ResolutionDetails _details = null!;
    private InspectionResult _result = null!;
    private int _reads;
    private static FSharpList<string> PathOf(params string[] path) => ListModule.OfSeq(path);

    [Given("a resolved inspection workspace and member")]
    public void Resolve()
    {
        _tree = new("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['app']\n[custom]\nworkspace_only='root'\ntargets=['one','two']\n[custom.reference]\ncommand=['never-run-this']\n",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'\n[custom]\ntargets=[]\nempty=''\n[custom.\"literal.dot\"]\nenabled=true\n"
        });
        var discovery = ProjectSystem.Discover(_tree, "/repo/app");
        _details = ProjectSystem.LoadDetailed(_tree, discovery.Discovery, discovery.Selection.Projects.Single());
        _reads = _tree.ObservedPaths.Count;
    }

    [When("the embedded host inspects source scopes")]
    public void Scopes() => _result = Inspection.get(QueryScope.Project, PathOf("custom", "workspace_only"), _details);

    [Then("workspace and member values remain separate without fallback")]
    public void Scoped()
    {
        _result.Found.Should().BeFalse();
        Inspection.get(QueryScope.Workspace, PathOf("custom", "workspace_only"), _details).Value.Value.Should().Be(ConfigValue.NewString("root"));
        Inspection.get(QueryScope.Effective, PathOf("custom", "workspace_only"), _details).Found.Should().BeTrue();
        NoReads();
    }

    [When("the embedded host lists member settings")]
    public void List() => _result = Inspection.list(QueryScope.Project, _details);

    [Then("entries are ordered and empty arrays and literal dots remain addressable")]
    public void Entries()
    {
        _result.Entries.Select(e => e.Key).Should().Equal("custom.empty", "custom.\"literal.dot\".enabled", "custom.targets", "project.name");
        Inspection.get(QueryScope.Project, PathOf("custom", "targets"), _details).Value.Value.Should().Be(ConfigValue.NewArray(FSharpList<ConfigValue>.Empty));
        var parsed = KeyPath.parse("custom.\"literal.dot\".enabled");
        parsed.IsOk.Should().BeTrue();
        Inspection.get(QueryScope.Project, parsed.ResultValue, _details).Value.Value.Should().Be(ConfigValue.NewBoolean(true));
        NoReads();
    }

    [When("the embedded host explains the targets array")]
    public void Explain() => _result = Inspection.explain(QueryScope.Effective, PathOf("custom", "targets"), _details);

    [Then("both contributions identify the member as the array winner")]
    public void Winner()
    {
        _result.Contributions.Select(c => c.Winner).Should().Equal(false, true);
        _result.Contributions.Last().Source.Path.Should().Be("/repo/app/morphir.toml");
        NoReads();
    }

    [When("the embedded host shows the document and paths")]
    public void Show() => _result = Inspection.show(QueryScope.Effective, _details);

    [Then("secrets are redacted and missing source reports remain visible")]
    public void Safe()
    {
        ConfigTree.tryFind(PathOf("custom", "reference"), _result.Value.Value).Value.Should().Be(ConfigValue.NewString("[REDACTED]"));
        Inspection.path(QueryScope.Effective, _details).Sources.ToArray().Should().Contain(s => s.Status.IsMissing);
        NoReads();
    }

    private void NoReads() => _tree.ObservedPaths.Count.Should().Be(_reads);
}
