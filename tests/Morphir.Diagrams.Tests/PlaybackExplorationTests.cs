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
