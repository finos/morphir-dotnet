// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO.Tests;

public sealed class InspectionRegressionTests
{
    [Test]
    public void SelectedMemberContainingNaNRemainsAValidDiscoverySelection()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['app']\n",
            ["/repo/app/morphir.json5"] = "{project:{name:'App'},custom:{nan:NaN}}"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo/app");
        var selected = discovery.Selection.Projects.Single();
        var result = ProjectSystem.LoadDetailed(tree, discovery.Discovery, selected);
        result.Resolution.Diagnostics.ToArray().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var value = ConfigTree.tryFind(ListModule.OfSeq(new[] { "custom", "nan" }), result.Resolution.Effective).Value;
        double.IsNaN(((ConfigValue.Number)value).Item).Should().BeTrue();
    }
}
