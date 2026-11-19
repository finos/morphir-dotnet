// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class ArchifyThemeTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private const string FontStack =
        "'JetBrains Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, 'DejaVu Sans Mono', 'Liberation Mono', monospace";

    [Test]
    public void SequenceUsesTheAdaptiveClassicPaletteWithRootOverrides()
    {
        var svg = Render("sequenceDiagram\n  Client->>API: GET /forecast");
        var id = ((string?)svg.Attribute("id")).ShouldNotBeNull();
        var style = svg.Descendants(Svg + "style").ShouldHaveSingleItem().Value;

        // Zero specificity, so that a host page can override any variable with plain CSS.
        style.ShouldContain($":where(svg#{id}) {{\n  --bg: #020617;");
        style.ShouldContain($"@media (prefers-color-scheme: light) {{\n  :where(svg#{id}) {{\n    --bg: #f4f5f7;");
        style.ShouldContain($":where(svg#{id}[data-theme=\"dark\"]) {{");
        style.ShouldContain($":where(svg#{id}[data-theme=\"light\"]) {{");
        // Light accents are darkened until text has 4.5:1 contrast on the background and masks.
        style.ShouldContain("--frontend-ink: #077995;");
        style.ShouldContain("--messagebus-ink: #c3490a;");

        svg.Elements(Svg + "rect").First().Attribute("fill")!.Value.ShouldBe("var(--bg)");
        svg.Descendants(Svg + "text").ShouldAllBe(text => (string?)text.Attribute("font-family") == FontStack);
    }

    [Test]
    public void SequenceParticipantsAndMessagesUseTheirColourFamilies()
    {
        var svg = Render("""
            sequenceDiagram
              actor User
              participant Client
              participant API
              User->>Client: Open
              Client->>API: Submit
              API-->>Client: Accepted
              Client-xAPI: Cancel
            """);

        Participant(svg, "User").Descendants().ShouldContain(e => (string?)e.Attribute("stroke") == "var(--frontend-stroke)");
        Box(Participant(svg, "Client")).Attribute("fill")!.Value.ShouldBe("var(--frontend-fill)");
        Box(Participant(svg, "API")).Attribute("fill")!.Value.ShouldBe("var(--backend-fill)");

        var lines = svg.Descendants(Svg + "path").Where(path => path.Attribute("data-sender") is not null).ToArray();
        lines.Select(line => (string?)line.Attribute("stroke"))
            .ShouldBe(["var(--frontend-stroke)", "var(--frontend-stroke)", "var(--arrow)", "var(--security-stroke)"]);
        ((string?)lines[2].Attribute("stroke-dasharray")).ShouldBe("4 4");

        Label(svg, "Submit").Attribute("fill")!.Value.ShouldBe("var(--frontend-ink)");
        Label(svg, "Accepted").Attribute("fill")!.Value.ShouldBe("var(--external-ink)");
        Label(svg, "Client").Attribute("fill")!.Value.ShouldBe("var(--text)");

        var lifeline = svg.Descendants(Svg + "path").First(path => (string?)path.Attribute("class") == "lifeline");
        ((string?)lifeline.Attribute("stroke-dasharray")).ShouldBe("3 7");
    }

    [Test]
    public void FlowchartNodesUseTheFamilyOfTheirShape()
    {
        var svg = Render("""
            flowchart LR
              subgraph Intake
                Read[Read request] --> Valid{Valid?}
              end
              Valid -->|yes| Store[(Orders)]
              Store --> Done((Done))
            """);
        var style = svg.Descendants(Svg + "style").ShouldHaveSingleItem().Value;
        // Flowchart text must also pass over the node fill composed on the background.
        style.ShouldContain("--frontend-ink: #07718b;");
        style.ShouldContain("--cloud-ink: #9d5705;");

        Outline(svg, "Read").Attribute("fill")!.Value.ShouldBe("var(--backend-fill)");
        Outline(svg, "Valid").Attribute("stroke")!.Value.ShouldBe("var(--cloud-stroke)");
        Outline(svg, "Store").Attribute("fill")!.Value.ShouldBe("var(--database-fill)");
        Outline(svg, "Done").Attribute("fill")!.Value.ShouldBe("var(--messagebus-fill)");
        Label(svg, "Read request").Attribute("fill")!.Value.ShouldBe("var(--backend-ink)");

        var group = svg.Descendants(Svg + "g").Single(g => (string?)g.Attribute("data-group") == "Intake");
        var outline = group.Elements().First(element => element.Name.LocalName is "rect" or "path");
        ((string?)outline.Attribute("stroke")).ShouldBe("var(--frame)");
        ((string?)outline.Attribute("stroke-dasharray")).ShouldBe("6 4");

        var connectors = svg.Descendants(Svg + "path").Where(path => path.Attribute("data-source") is not null);
        connectors.ShouldAllBe(path => (string?)path.Attribute("stroke") == "var(--arrow)");
    }

    [Test]
    public void GeneratedIdentifiersAreDeterministicAndScopedToTheDiagram()
    {
        const string first = "flowchart LR\n  A --> B";
        var once = Render(first);
        var again = Render(first);
        var other = Render("flowchart LR\n  A --> C");

        var id = ((string?)once.Attribute("id")).ShouldNotBeNull();
        id.ShouldBe((string?)again.Attribute("id"));
        id.ShouldNotBe((string?)other.Attribute("id"));

        var marker = once.Descendants(Svg + "marker").ShouldHaveSingleItem();
        id.ShouldEndWith("-diagram");
        ((string?)marker.Attribute("id")).ShouldNotBeNull().ShouldStartWith(id[..^"diagram".Length]);
        once.Descendants(Svg + "path")
            .Where(path => path.Attribute("marker-end") is not null)
            .ShouldAllBe(path => (string?)path.Attribute("marker-end") == $"url(#{(string?)marker.Attribute("id")})");
    }

    private static XElement Render(string source) =>
        XElement.Parse(Mermaid.RenderSvg(source).Output.ShouldNotBeNull());

    private static XElement Participant(XElement svg, string id) =>
        svg.Descendants(Svg + "g").First(g => (string?)g.Attribute("data-participant") == id);

    private static XElement Box(XElement participant) =>
        participant.Elements(Svg + "rect").Single(rect => (string?)rect.Attribute("class") == "box");

    private static XElement Outline(XElement svg, string id) =>
        svg.Descendants(Svg + "g").Single(g => (string?)g.Attribute("data-node") == id)
            .Elements().Single(element => (string?)element.Attribute("class") == "outline");

    private static XElement Label(XElement svg, string text) =>
        svg.Descendants(Svg + "text").First(element => element.Value == text);
}
