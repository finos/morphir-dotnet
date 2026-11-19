// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using Morphir.Diagrams;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class MermaidTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Test]
    public void ReadsAndRendersABranchingFlowchart()
    {
        var source = "flowchart LR\r\n  Read[Read request] --> Valid{Valid?}\r\n  Valid -->|yes| Save[Save]\r\n  Valid -->|no| Read\r\n";

        if (!Mermaid.TryParse(source, out var document, out var diagnostics))
            throw new InvalidOperationException(string.Join("\n", diagnostics));

        document.Source.ShouldBe(source);
        var flowchart = document.Diagram.Match(
            flowchart: graph => graph,
            sequence: _ => throw new InvalidOperationException("Expected a flowchart."));
        flowchart.Direction.ShouldBe(FlowDirection.LeftToRight);
        flowchart.Nodes.Select(node => node.Id).ShouldBe(
            [DiagramId.Create("Read"), DiagramId.Create("Valid"), DiagramId.Create("Save")]);
        flowchart.Edges.Select(edge => edge.Label).ShouldBe([null, "yes", "no"]);

        var rendered = SvgRenderer.Render(document.Diagram);
        rendered.MediaType.ShouldBe("image/svg+xml");
        var svg = XElement.Parse(rendered.Output.ShouldNotBeNull());
        svg.Name.ShouldBe(Svg + "svg");
        svg.Descendants(Svg + "text").Select(text => text.Value).ShouldContain("Valid?");
    }

    [Test]
    public void ReadsSequenceAliasesAndMessageStyles()
    {
        const string source = """
            sequenceDiagram
              actor Alice as Customer
              participant API as Orders API
              Alice->>API: Place order
              API-->>Alice: Accepted
              Alice-)Alice: Record receipt
            """;

        Mermaid.TryParse(source, out var document, out var diagnostics).ShouldBeTrue();
        diagnostics.ShouldBeEmpty();
        var sequence = document.ShouldNotBeNull().Diagram.Match(
            flowchart: _ => throw new InvalidOperationException("Expected a sequence."),
            sequence: model => model);

        sequence.Participants.Select(participant => participant.Label).ShouldBe(["Customer", "Orders API"]);
        sequence.Participants[0].Kind.ShouldBe(ParticipantKind.Actor);
        sequence.Messages.Select(message => message.IsDashed).ShouldBe([false, true, false]);
        sequence.Messages[2].ReceiverHead.ShouldBe(EdgeHead.Async);
        var svg = XElement.Parse(SvgRenderer.Render(document.Diagram).Output.ShouldNotBeNull());
        svg.Descendants(Svg + "text").Select(text => text.Value).ShouldContain("Record receipt");
    }

    [Test]
    public void InvalidFlowchartReportsASourceRangeAndNoSvg()
    {
        const string source = "flowchart TD\r\n  A[\"🥵\" --> B";
        var result = Mermaid.RenderSvg(source);

        result.Output.ShouldBeNull();
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.Code.ShouldBe("mermaid/flowchart-statement");
        var span = diagnostic.Span.ShouldNotBeNull();
        span.Start.Line.ShouldBe(2);
        span.Start.Column.ShouldBe(3);
        span.End.Offset.ShouldBe(source.Length);
    }

    [Test]
    public void SvgTreatsSourceLabelsAsText()
    {
        const string source = """
            graph TD
              A["<script>evil()</script> & text"] --> B
            """;

        var svg = XElement.Parse(Mermaid.RenderSvg(source).Output.ShouldNotBeNull());
        svg.Descendants(Svg + "script").ShouldBeEmpty();
        svg.Descendants(Svg + "text").Select(text => text.Value).ShouldContain("<script>evil()</script> & text");
    }

    [Test]
    public void RetainsFlowchartSubgroupsAndTheirNodeMembership()
    {
        const string source = """
            flowchart LR
              subgraph Validation
                Read[Read request] --> Valid{Valid?}
              end
              Valid -->|yes| Save[Save]
            """;

        if (!Mermaid.TryParse(source, out var document, out var diagnostics))
            throw new InvalidOperationException(string.Join("\n", diagnostics));

        var flowchart = document.Diagram.Match(
            flowchart: model => model,
            sequence: _ => throw new InvalidOperationException("Expected a flowchart."));
        var group = flowchart.Groups.ShouldHaveSingleItem();
        group.Label.ShouldBe("Validation");
        group.NodeIds.ShouldBe([DiagramId.Create("Read"), DiagramId.Create("Valid")]);

        var svg = XElement.Parse(SvgRenderer.Render(document.Diagram).Output.ShouldNotBeNull());
        svg.Descendants(Svg + "g").ShouldContain(node => (string?)node.Attribute("data-group") == group.Id.Value);
    }

    [Test]
    public void DrawsSequenceNotesBetweenMessages()
    {
        const string source = """
            sequenceDiagram
              Alice->>API: Submit
              Note over Alice, API: Validation started
              API-->>Alice: Accepted
            """;

        var svg = XElement.Parse(Mermaid.RenderSvg(source).Output.ShouldNotBeNull());
        svg.Descendants(Svg + "text")
            .Select(text => text.Value)
            .Where(text => text != "Alice" && text != "API")
            .ShouldBe(["Submit", "Validation started", "Accepted"]);
    }

    [Test]
    public void KeepsNestedSequenceFramesAndAlternativeSectionsInEventOrder()
    {
        const string source = """
            sequenceDiagram
              alt Authorized
                loop Retry pending request
                  Client->>API: Submit
                end
              else Unrecognized
                Note over Client: Request a new token
              end
            """;

        if (!Mermaid.TryParse(source, out var document, out var diagnostics))
            throw new InvalidOperationException(string.Join("\n", diagnostics));

        var sequence = document.Diagram.Match(
            flowchart: _ => throw new InvalidOperationException("Expected a sequence."),
            sequence: model => model);
        var alternative = FrameOf(sequence.Events.ShouldHaveSingleItem());
        alternative.Kind.ShouldBe(SequenceFrameKind.Alternative);
        alternative.Sections.Select(section => section.Label).ShouldBe(["Authorized", "Unrecognized"]);
        FrameOf(alternative.Sections[0].Events.ShouldHaveSingleItem()).Kind.ShouldBe(SequenceFrameKind.Loop);
        sequence.Messages.ShouldHaveSingleItem().Text.ShouldBe("Submit");
        sequence.Notes.ShouldHaveSingleItem().Text.ShouldBe("Request a new token");

        var svg = XElement.Parse(SvgRenderer.Render(document.Diagram).Output.ShouldNotBeNull());
        svg.Descendants(Svg + "g").Attributes("data-frame").Select(attribute => attribute.Value)
            .ShouldBe(["alt", "loop"]);
    }

    [Test]
    public void DrawsNestedExplicitAndMessageShorthandActivations()
    {
        const string source = """
            sequenceDiagram
              Client->>+API: Submit
              activate API
              API-->>-Client: Accepted
              deactivate API
              Client->>API: Archive
            """;

        var svg = XElement.Parse(Mermaid.RenderSvg(source).Output.ShouldNotBeNull());
        var bars = svg.Descendants(Svg + "g")
            .Where(element => (string?)element.Attribute("data-activation") == "API")
            .OrderBy(element => (int)element.Attribute("data-depth")!).ToArray();
        bars.Select(element => (int)element.Attribute("data-depth")!).ShouldBe([1, 2]);
        Bottom(bars[1]).ShouldBeLessThan(Bottom(bars[0]));
        var archive = svg.Descendants(Svg + "text").Single(text => text.Value == "Archive");
        Bottom(bars[0]).ShouldBeLessThan((int)archive.Attribute("y")!);

        static int Bottom(XElement bar)
        {
            var rectangle = bar.Elements(Svg + "rect").ShouldHaveSingleItem();
            return (int)rectangle.Attribute("y")! + (int)rectangle.Attribute("height")!;
        }
    }

    [Test]
    [Arguments("deactivate API", "mermaid/inactive-deactivation", 2)]
    [Arguments("loop Retry\n  Client->>API: Submit", "mermaid/missing-end", 2)]
    [Arguments("loop Retry\nelse Failure\nend", "mermaid/misplaced-section", 3)]
    public void ReportsInvalidSequenceControlStatements(string body, string code, int line)
    {
        var rendered = Mermaid.RenderSvg("sequenceDiagram\n" + body);
        rendered.Output.ShouldBeNull();
        var diagnostic = rendered.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe(code);
        diagnostic.Span.ShouldNotBeNull().Start.Line.ShouldBe(line);
    }

    [Test]
    public void ReadsAnInlineLabelOnADottedFlowchartLink()
    {
        const string source = "flowchart LR\n  Read -. retry .-> Validate";
        if (!Mermaid.TryParse(source, out var document, out var diagnostics))
            throw new InvalidOperationException(string.Join("\n", diagnostics));

        var chart = document.Diagram.Match(
            flowchart: model => model,
            sequence: _ => throw new InvalidOperationException("Expected a flowchart."));
        var link = chart.Edges.ShouldHaveSingleItem();
        link.Line.ShouldBe(FlowLine.Dotted);
        link.Label.ShouldBe("retry");
        link.EndHead.ShouldBe(EdgeHead.Arrow);
    }

    [Test]
    public void AutonumberCanPauseResumeAndResetAcrossFrames()
    {
        const string source = """
            sequenceDiagram
              autonumber 10 2
              Client->>API: Submit
              autonumber off
              API-->>Client: Received
              loop Retry
                autonumber
                Client->>API: Try again
              end
              autonumber 5 .5
              API->>API: Validate
              API-->>Client: Accepted
            """;

        if (!Mermaid.TryParse(source, out var document, out var diagnostics))
            throw new InvalidOperationException(string.Join("\n", diagnostics));

        var sequence = document.Diagram.Match(
            flowchart: _ => throw new InvalidOperationException("Expected a sequence."),
            sequence: model => model);
        sequence.Messages.Select(message => message.Number).ShouldBe([10m, null, 12m, 5m, 5.5m]);

        var svg = XElement.Parse(SvgRenderer.Render(document.Diagram).Output.ShouldNotBeNull());
        svg.Descendants(Svg + "path").Attributes("data-number").Select(attribute => attribute.Value)
            .ShouldBe(["10", "12", "5", "5.5"]);
    }

    private static SequenceFrame FrameOf(SequenceEvent sequenceEvent) => sequenceEvent.Match(
        message: _ => throw new InvalidOperationException("Expected a frame."),
        note: _ => throw new InvalidOperationException("Expected a frame."),
        activation: _ => throw new InvalidOperationException("Expected a frame."),
        frame: frame => frame);

    [Test]
    public void DiagramIdentifiersUseOrdinalEqualityAndRejectBlankInput()
    {
        if (DiagramId.TryParse(null, out _, out var failure))
            throw new InvalidOperationException("Expected a missing identifier to fail.");

        failure.Code.ShouldBe("diagram/identifier");
        DiagramId.Create("Read").Equals(DiagramId.Create("read")).ShouldBeFalse();
    }

    [Test]
    public void UnterminatedDirectivePreventsRendering()
    {
        var rendered = Mermaid.RenderSvg("flowchart TD\n  A --> B\n%%{");
        rendered.Output.ShouldBeNull();
        rendered.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("mermaid/unclosed-directive");
    }
}
