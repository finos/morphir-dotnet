// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>How sequence diagrams draw created and destroyed lifelines, boxes, central connections and kinds.</summary>
public class SequenceSymbolTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static XElement Render(string source, SvgRenderOptions? options = null) =>
        XElement.Parse(Mermaid.RenderSvg(source, options ?? SvgRenderOptions.Default).Output.ShouldNotBeNull());

    private static XElement Element(XElement svg, string reference) =>
        svg.Descendants(Svg + "g").Single(g => (string?)g.Attribute("data-ref") == reference);

    private static int Y(XElement element) => int.Parse((string)element.Attribute("y")!);

    [Test]
    public void ACreatedParticipantStandsOnTheRowOfTheMessageThatCreatesItAndADestroyedOneEndsWithACross()
    {
        var svg = Render("sequenceDiagram\n  participant A\n  create participant B\n  A->>B: new\n  destroy B\n  A->>B: stop\n");

        var line = Element(svg, "message=A/B/0").Descendants(Svg + "path").First();
        var row = int.Parse(((string)line.Attribute("d")!).Split(',', ' ')[1]);
        var lane = Element(svg, "participant=B");
        var boxes = lane.Descendants(Svg + "rect").Where(rect => (string?)rect.Attribute("class") == "box").ToList();

        Y(boxes.ShouldHaveSingleItem()).ShouldBe(row - 21);
        lane.Descendants(Svg + "path").ShouldContain(path => (string?)path.Attribute("class") == "destroy");
        Element(svg, "participant=A").Descendants(Svg + "rect").Count(rect => (string?)rect.Attribute("class") == "box").ShouldBe(2);
    }

    [Test]
    public void ABoxIsDrawnBehindItsParticipantsWithItsTitle()
    {
        var svg = Render("sequenceDiagram\n  box Purple Team\n  participant A\n  participant B\n  end\n  participant C\n  A->>C: hi\n");

        var box = svg.Descendants(Svg + "rect").Single(rect => (string?)rect.Attribute("class") == "participant-box");
        ((string?)box.Attribute("fill")).ShouldBe("Purple");
        svg.Descendants(Svg + "text").ShouldContain(text => text.Value == "Team");
    }

    [Test]
    public void ACentralConnectionIsMarkedOnTheLifeline()
    {
        var svg = Render("sequenceDiagram\n  A->>()B: in\n");

        Element(svg, "message=A/B/0").Descendants(Svg + "circle").ShouldHaveSingleItem();
    }

    [Test]
    [Arguments("boundary", "frontend")]
    [Arguments("control", "cloud")]
    [Arguments("entity", "backend")]
    [Arguments("database", "database")]
    [Arguments("collections", "database")]
    [Arguments("queue", "messagebus")]
    public void EachParticipantKindHasItsColourFamily(string kind, string family)
    {
        var svg = Render($"sequenceDiagram\n  participant P@{{ \"type\": \"{kind}\" }}\n");

        svg.Descendants(Svg + "g").ShouldContain(g => (string?)g.Attribute("class") == $"participant f-{family}");
    }

    [Test]
    public void SequencesWithoutTheNewFeaturesKeepTheirOutput()
    {
        var source = "sequenceDiagram\n  Alice->>Bob: Hi\n  Bob-->>Alice: Hello\n";

        Render(source).Descendants(Svg + "rect").ShouldNotContain(rect => (string?)rect.Attribute("class") == "participant-box");
        Render(source).Descendants(Svg + "path").ShouldNotContain(path => (string?)path.Attribute("class") == "destroy");
    }
}
