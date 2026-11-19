// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Xml.Linq;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>Mermaid swimlanes: the flowchart grammar with subgraphs as lanes, drawn as parallel bands.</summary>
public class SwimlaneTests
{
    private const string Order = """
        swimlane-beta
          subgraph Customer
            start((Start)) --> order[Place order]
          end
          subgraph Sales
            check{In stock?}
          end
          subgraph Warehouse
            pick[Pick items] --> ship[Ship]
          end
          order --> check
          check -->|yes| pick
        """;

    private static IEnumerable<JsonElement> Fixtures()
    {
        using var reference = JsonDocument.Parse(PinnedCorpus.File(FixtureCatalog.DiagramsMbt.Swimlanes, "/reference.json"));
        return reference.RootElement.GetProperty("fixtures").EnumerateArray().Select(fixture => fixture.Clone()).ToList();
    }

    [Test]
    public void EveryReferenceFixtureGivesTheReferenceStepsLanesAndConnectors()
    {
        var mismatches = new List<string>();

        foreach (var fixture in Fixtures())
        {
            var normalized = fixture.GetProperty("normalized");

            if (!Mermaid.TryParse(fixture.GetProperty("source").GetString(), out var document, out var diagnostics))
            {
                mismatches.Add($"{fixture.GetProperty("name")}: {string.Join(", ", diagnostics)}");
                continue;
            }

            var model = DiagramMother.Of<SwimlaneDiagram>(document);
            var lanes = normalized.GetProperty("groups").EnumerateArray().Select(group =>
                $"{group.GetProperty("id").GetString()}:{string.Join(",", group.GetProperty("members").EnumerateArray().Select(member => member.GetString()).Order(StringComparer.Ordinal))}");
            var ours = model.Lanes.Select(lane =>
                $"{lane.Id}:{string.Join(",", model.Steps.Where(step => step.Lane?.Equals(lane.Id) == true).Select(step => step.Id.Value).Order(StringComparer.Ordinal))}");

            if (!model.Steps.Select(step => step.Id.Value).Order(StringComparer.Ordinal).SequenceEqual(normalized.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("id").GetString()).Order(StringComparer.Ordinal))
                || model.Connectors.Length != normalized.GetProperty("edges").GetArrayLength()
                || model.Lanes.Length != normalized.GetProperty("groups").GetArrayLength())
            {
                mismatches.Add($"{fixture.GetProperty("name")}: {string.Join(" ", ours)} / {string.Join(" ", lanes)}");
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void TheSameBodyGivesLanesUnderOneHeaderAndGroupsUnderTheOther()
    {
        var body = "\n  subgraph Sales\n    a --> b\n  end\n";

        DiagramSource.TryParse("swimlane-beta" + body, out var lanes, out _).ShouldBeTrue();
        DiagramSource.TryParse("flowchart TB" + body, out var groups, out _).ShouldBeTrue();

        DiagramMother.Of<SwimlaneDiagram>(lanes).Lanes.ShouldHaveSingleItem().Label.ShouldBe("Sales");
        DiagramMother.FlowchartOf(groups).Groups.ShouldHaveSingleItem().Label.ShouldBe("Sales");
    }

    [Test]
    public void ARepeatedLaneGathersItsStepsAndStepsHaveRoles()
    {
        DiagramSource.TryParse("swimlane-beta\n  subgraph A [First]\n    x((go))\n  end\n  subgraph A [Second]\n    y{ok?}\n  end\n", out var document, out _).ShouldBeTrue();
        var model = DiagramMother.Of<SwimlaneDiagram>(document);

        model.Lanes.ShouldHaveSingleItem().Label.ShouldBe("First");
        model.Steps.Select(step => (step.Id.Value, step.Role.ToString(), step.Lane!.Value)).ShouldBe([("x", "action", "A"), ("y", "decision", "A")]);
    }

    [Test]
    public void LanesAreParallelBandsOfEqualLengthThatHoldTheirSteps()
    {
        var svg = XElement.Parse(DiagramSource.RenderSvg(Order).Output!);
        var boxes = new[] { "Customer", "Sales", "Warehouse" }.Select(lane => FlowchartGeometry.Group(svg, lane)).ToList();

        boxes.Select(box => (box.Top, box.Bottom)).Distinct().ShouldHaveSingleItem();
        boxes.Zip(boxes.Skip(1)).ShouldAllBe(pair => pair.First.Right < pair.Second.Left);
        FlowchartGeometry.Node(svg, "pick").Left.ShouldBeGreaterThan(boxes[2].Left);
        FlowchartGeometry.Node(svg, "order").Right.ShouldBeLessThan(boxes[0].Right);
    }

    [Test]
    public void PrintingWhatWasPrintedGivesTheSameTextForEveryFixture()
    {
        var failures = new List<string>();

        foreach (var fixture in Fixtures())
        {
            Mermaid.TryParse(fixture.GetProperty("source").GetString(), out var document, out _).ShouldBeTrue();

            if (!Mermaid.TryPrint(document!.Diagram, out var first, out var diagnostics)
                || !Mermaid.TryParse(first, out var reread, out _)
                || !Mermaid.TryPrint(reread!.Diagram, out var second, out _)
                || second != first)
            {
                failures.Add($"{fixture.GetProperty("name")}: {string.Join(", ", diagnostics)}\n{first}");
            }
        }

        failures.ShouldBeEmpty();
    }
}
