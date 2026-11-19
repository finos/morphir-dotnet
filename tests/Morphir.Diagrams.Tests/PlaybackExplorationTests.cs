// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>Structural routes and reachability, the connection graph in the motion data, scenarios and node visits.</summary>
public partial class PlaybackExplorationTests
{
    private static Diagram Read(string source)
    {
        DiagramSource.TryParse(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return document!.Diagram;
    }

    private static DiagramId Id(string value) => DiagramId.Create(value);

    private static string Text(ExplorationResult result) => result.ToString();

    [Test]
    public void ARouteFollowsArrowheadsAndAnAcyclicFlowchartHasNoWayBack()
    {
        var diagram = Read("flowchart LR\n  Start --> Check{Ok?}\n  Check -->|yes| Done\n  Check -->|no| Start\n");

        Text(DiagramExploration.Route(diagram, Id("Start"), Id("Done"), false)).ShouldBe("Structural route Start → Check → Done");
        DiagramExploration.Route(Read("flowchart LR\n  Start --> A --> Done\n"), Id("Done"), Id("Start"), false)
            .ShouldBe(ExplorationResult.Unreachable);
    }

    [Test]
    public void ALinkWithoutHeadsIsFollowedOnlyWhenUndirected()
    {
        var diagram = Read("flowchart LR\n  A --- B\n");

        DiagramExploration.Route(diagram, Id("A"), Id("B"), false).ShouldBe(ExplorationResult.Unreachable);
        Text(DiagramExploration.Route(diagram, Id("A"), Id("B"), true)).ShouldBe("Structural route A → B");
    }

    [Test]
    public void ABidirectionalLinkIsFollowedBothWays() =>
        Text(DiagramExploration.Route(Read("flowchart LR\n  A <--> B\n"), Id("B"), Id("A"), false)).ShouldBe("Structural route B → A");

    [Test]
    public void ReachabilityRunsDownstreamOrUpstreamAndEndsOnCycles()
    {
        var diagram = Read("flowchart LR\n  A --> B --> C --> A\n  C --> D\n  E --> B\n");

        Text(DiagramExploration.Reachable(diagram, Id("B"), true, false)).ShouldBe("Structurally reachable B → C → A → D");
        Text(DiagramExploration.Reachable(diagram, Id("B"), false, false)).ShouldBe("Structurally reachable B → A → E → C");
    }

    [Test]
    public void AnElementOutsideTheDiagramIsInvalid() =>
        DiagramExploration.Route(Read("flowchart LR\n  A --> B\n"), Id("A"), Id("Z"), false)
            .Match(_ => "", _ => "", () => "", reason => reason, () => "").ShouldBe("Z is not an element of the diagram.");

    [Test]
    public void SequenceMessagesAreConnectionsBetweenParticipants() =>
        Text(DiagramExploration.Route(Read("sequenceDiagram\n  A->>B: one\n  B->>C: two\n"), Id("A"), Id("C"), false))
            .ShouldBe("Structural route A → B → C");

    [Test]
    public void TheMotionDataCarriesTheConnectionGraphWithElementReferences()
    {
        var data = MotionData(Mermaid.RenderAnimated("flowchart LR\n  A --> B\n  B --- C\n").Output!);

        data.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("ref").GetString()).ShouldBe(["node=A", "node=B", "node=C"]);
        data.GetProperty("connections").EnumerateArray()
            .Select(connection => $"{connection.GetProperty("ref").GetString()}:{connection.GetProperty("forward").GetBoolean()}")
            .ShouldBe(["edge=A/B/0:True", "edge=B/C/0:False"]);
    }

    private static JsonElement MotionData(string html)
    {
        var json = MotionScript().Match(html).Groups[1].Value;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [GeneratedRegex("data-motion-data>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex MotionScript();
}
