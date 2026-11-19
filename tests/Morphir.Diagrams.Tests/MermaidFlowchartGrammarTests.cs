// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>The Mermaid 12.2.0 flowchart grammar beyond the first subset, checked against the pinned reference.</summary>
public class MermaidFlowchartGrammarTests
{
    [Test]
    public void EveryReferenceFixtureIsAcceptedOrRefusedAsMermaidDoesWithTheSameNodesAndEdges()
    {
        using var reference = JsonDocument.Parse(PinnedCorpus.File(FixtureCatalog.DiagramsMbt.Flowcharts, "/reference.json"));
        var mismatches = new List<string>();
        var count = 0;

        foreach (var fixture in reference.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            count++;
            var name = fixture.GetProperty("name").GetString()!;
            var accepted = fixture.GetProperty("accepted").GetBoolean();
            var read = Mermaid.TryParse(fixture.GetProperty("source").GetString(), out var document, out var diagnostics);

            if (read != accepted)
            {
                mismatches.Add($"{name}: accepted {read}, Mermaid {accepted} {string.Join(", ", diagnostics)}");
                continue;
            }

            if (!accepted)
            {
                continue;
            }

            var normalized = fixture.GetProperty("normalized");
            var expectedNodes = normalized.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("id").GetString()).Order(StringComparer.Ordinal);
            var flowchart = DiagramMother.FlowchartOf(document);
            var nodes = flowchart.Nodes.Select(node => node.Id.Value).Order(StringComparer.Ordinal);

            if (!nodes.SequenceEqual(expectedNodes) || flowchart.Edges.Length != normalized.GetProperty("edges").GetArrayLength())
            {
                mismatches.Add($"{name}: nodes [{string.Join(", ", nodes)}], {flowchart.Edges.Length} edges");
            }
        }

        count.ShouldBe(148);
        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void ClassesAndCallbacksAreAcceptedReportedAndNeverRun()
    {
        Mermaid.TryParse("flowchart LR\n  A --> B\n  classDef hot fill:#f00\n  class A hot\n  click A callback\n", out var document, out _)
            .ShouldBeTrue();

        document!.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(Enumerable.Repeat("mermaid/unapplied-presentation", 3));
        document.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
    }

    [Test]
    public void AnInitDirectiveDoesNotChangeTheCallersTheme()
    {
        var cobalt = SvgRenderOptions.Create(DiagramThemes.Cobalt, ThemeMode.Dark);

        var directed = Mermaid.RenderSvg("%%{init: {\"theme\": \"forest\"}}%%\nflowchart LR\n  A --> B\n", cobalt);

        directed.Output.ShouldBe(Mermaid.RenderSvg("flowchart LR\n  A --> B\n", cobalt).Output);
        directed.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("mermaid/unapplied-directive");
    }

    [Test]
    [Arguments("A@{ shape: cyl, label: \"Orders\" }", "cylinder", "Orders")]
    [Arguments("A@{\n  shape: doc\n  label: Spec\n  metadata:\n    label: nested\n}", "document", "Spec")]
    [Arguments("A[Classic]@{ shape: fork }", "bar", "Classic")]
    [Arguments("A[\"#quot;Hi#quot; #9829;<br>there\"]", "rectangle", "\"Hi\" ♥\nthere")]
    [Arguments("A[\"`**Bold**\n  text`\"]", "rectangle", "**Bold**\ntext")]
    public void ShapeDataAndLabelTextAreRead(string statement, string shape, string label)
    {
        Mermaid.TryParse($"flowchart LR\n{statement}\n", out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));

        var node = DiagramMother.FlowchartOf(document).Nodes.ShouldHaveSingleItem();
        node.Shape.WireValue.ShouldBe(shape);
        node.Label.ShouldBe(label);
    }

    [Test]
    public void AnUnknownShapeIsRefused()
    {
        Mermaid.TryParse("flowchart LR\n  A@{ shape: blob }\n", out _, out var diagnostics).ShouldBeFalse();

        diagnostics.ShouldHaveSingleItem().Message.ShouldContain("blob");
    }

    [Test]
    public void ALinkToASubgraphEndsOnTheSubgraphsBox()
    {
        var svg = FlowchartGeometry.Render("flowchart LR\n  Start --> one\n  subgraph one [My group]\n    A --> B\n  end\n  one --> End\n");

        var box = FlowchartGeometry.Group(svg, "one");
        var into = FlowchartGeometry.Route(svg, "Start", "one");
        var outOf = FlowchartGeometry.Route(svg, "one", "End");

        into[^1].X.ShouldBe(box.Left, 1);
        outOf[0].X.ShouldBe(box.Right, 1);
        DiagramMother.FlowchartOf(Parsed("flowchart LR\n  Start --> one\n  subgraph one\n    A\n  end\n")).Nodes
            .Select(node => node.Id.Value).ShouldBe(["Start", "A"]);
    }

    [Test]
    public void ARepeatedSubgraphAddsToTheFirstAndKeepsItsTitle()
    {
        var flowchart = DiagramMother.FlowchartOf(Parsed("flowchart LR\n  subgraph s [First]\n    A\n  end\n  subgraph s [Second]\n    B\n  end\n"));

        var group = flowchart.Groups.ShouldHaveSingleItem();
        group.Label.ShouldBe("First");
        group.NodeIds.Select(id => id.Value).ShouldBe(["A", "B"]);
    }

    [Test]
    public void EdgeIdentifiersAndOpenLabelledLinksAreRead()
    {
        var flowchart = DiagramMother.FlowchartOf(Parsed("flowchart LR\n  A e1@--> B\n  e1@{ animate: true }\n  B-- text ---C\n"));

        flowchart.Nodes.Select(node => node.Id.Value).ShouldBe(["A", "B", "C"]);
        var open = flowchart.Edges[1];
        open.Label.ShouldBe("text");
        open.EndHead.ShouldBe(EdgeHead.None);
    }

    private static DiagramDocument Parsed(string source)
    {
        Mermaid.TryParse(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return document!;
    }
}
