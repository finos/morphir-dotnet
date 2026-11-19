// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Xml.Linq;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class AnimatedDiagramTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Test]
    public void SequencePlaybackUsesTheStaticPathsAndRetainsExactSvgForDownload()
    {
        const string source = """
            sequenceDiagram
              Reader->>API: Start
              API->>API: Interpret </script><script>bad()</script>
              API-->>Reader: Complete
            """;

        if (!Mermaid.TryParse(source, out var document, out var diagnostics))
            throw new InvalidOperationException(string.Join("\n", diagnostics));

        var scene = SvgRenderer.BuildScene(document.Diagram);
        var canonical = SvgRenderer.Render(document.Diagram).Output.ShouldNotBeNull();
        scene.Document.Render().ShouldBe(canonical);

        var rendered = AnimatedRenderer.Render(scene);
        rendered.MediaType.ShouldBe("text/html");
        var html = rendered.Output.ShouldNotBeNull();
        html.ShouldContain("data-control=\"seek\"");
        html.ShouldContain("Download SVG");
        html.ShouldNotContain("<script>bad()</script>");

        using var payload = ReadMotionData(html);
        payload.RootElement.GetProperty("svg").GetString().ShouldBe(canonical);
        var paths = payload.RootElement.GetProperty("paths").EnumerateArray().ToArray();
        var svg = XElement.Parse(canonical);
        var staticPaths = svg.Descendants(Svg + "path")
            .Where(path => path.Attribute("data-sender") is not null)
            .Select(path => (string?)path.Attribute("d"));
        paths.Select(path => path.GetProperty("d").GetString()).ShouldBe(staticPaths);
    }

    [Test]
    public void FlowchartPlaybackReversesSourceHeadedLinksAndOmitsInvisibleLinks()
    {
        const string source = "flowchart LR\n  A o--- B\n  B ~~~ C\n  C --> A";
        var html = Mermaid.RenderAnimated(source).Output.ShouldNotBeNull();
        using var payload = ReadMotionData(html);
        var paths = payload.RootElement.GetProperty("paths").EnumerateArray().ToArray();
        paths.Select(path => path.GetProperty("reversed").GetBoolean()).ShouldBe([true, false]);
    }

    [Test]
    public void ExplicitScenarioRetainsConcurrentAndRepeatedOccurrences()
    {
        var scene = ReadScene("sequenceDiagram\n  A->>B: Submit\n  B->>C: Record");
        var submitted = scene.Connectors[0];
        var recorded = scene.Connectors[1];
        var html = AnimatedRenderer.Render(scene,
            [
                PlaybackStep.Create(submitted, 0, 400),
                PlaybackStep.Create(recorded, 0, 200),
                PlaybackStep.Create(submitted, 400, 300),
            ]).Output.ShouldNotBeNull();

        using var payload = ReadMotionData(html);
        var data = payload.RootElement;
        data.GetProperty("repeat").GetBoolean().ShouldBeFalse();
        data.GetProperty("durationMs").GetInt64().ShouldBe(700);
        var steps = data.GetProperty("steps").EnumerateArray().ToArray();
        steps.Select(step => step.GetProperty("connector").GetInt32()).ShouldBe([0, 1, 0]);
        steps.Select(step => step.GetProperty("startMs").GetInt64()).ShouldBe([0, 0, 400]);
    }

    [Test]
    public void ExplicitScenarioRejectsConnectorsFromAnotherScene()
    {
        var first = ReadScene("sequenceDiagram\n  A->>B: First");
        var second = ReadScene("sequenceDiagram\n  A->>B: Second");
        var result = AnimatedRenderer.Render(second, [PlaybackStep.Create(first.Connectors[0], 0, 1000)]);

        result.Output.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("animation/plan-mismatch");
    }

    [Test]
    public void StaticDiagramHasAnEnhanceableViewportEvenWhenThereAreNoMessages()
    {
        var canonical = Mermaid.RenderSvg("flowchart LR\n  A[Review]").Output.ShouldNotBeNull();
        var html = Mermaid.RenderAnimated("flowchart LR\n  A[Review]").Output.ShouldNotBeNull();

        html.ShouldContain(canonical);
        html.ShouldContain("data-viewport-toolbar hidden");
        html.ShouldContain("data-view-control=\"zoom-in\"");
        html.ShouldContain("data-view-control=\"zoom-out\"");
        html.ShouldContain("data-view-control=\"fit\"");
        html.ShouldContain("data-view-control=\"fullscreen\"");
    }

    private static DiagramScene ReadScene(string source)
    {
        if (Mermaid.TryParse(source, out var document, out var diagnostics))
            return SvgRenderer.BuildScene(document.Diagram);

        throw new InvalidOperationException(string.Join("\n", diagnostics));
    }

    private static JsonDocument ReadMotionData(string html)
    {
        const string opening = "<script type=\"application/json\" data-motion-data>";
        var openingIndex = html.IndexOf(opening, StringComparison.Ordinal);
        openingIndex.ShouldBeGreaterThanOrEqualTo(0);
        var start = openingIndex + opening.Length;
        var finish = html.IndexOf("</script>", start, StringComparison.Ordinal);
        return JsonDocument.Parse(html[start..finish]);
    }
}
