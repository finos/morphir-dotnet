// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Xml.Linq;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Canvas.Tests;

/// <summary>Shared helpers for the canvas renderer tests.</summary>
public static class CanvasMother
{
    /// <summary>Parses any supported source.</summary>
    public static Diagram Parse(string source) =>
        DiagramSource.TryParse(source, out var document, out var errors) ? document.Diagram : throw new InvalidOperationException(string.Join(", ", errors));

    /// <summary>The layout of a source.</summary>
    public static DiagramLayout Layout(string source, SvgRenderOptions? options = null) =>
        DiagramExport.Layout(Parse(source), options ?? SvgRenderOptions.Default).Layout.ShouldNotBeNull();

    /// <summary>The bytes that a renderer writes for pages under allow-degraded.</summary>
    public static byte[] Render(DiagramRenderer renderer, params DiagramLayout[] pages)
    {
        using var output = new MemoryStream();
        var result = DiagramExport.RenderAsync(pages, renderer, output, DegradationPolicy.AllowDegraded, CancellationToken.None).GetAwaiter().GetResult();
        result.Diagnostics.ShouldNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error, string.Join(", ", result.Diagnostics));
        return output.ToArray();
    }

    /// <summary>Every corpus diagram that lays out.</summary>
    public static IEnumerable<(CorpusDiagram Source, DiagramLayout Layout)> Corpus() =>
        DiagramCorpus.Sources()
            .Select(source => (Source: source, Parsed: DiagramSource.TryParse(source.Source, out var document, out _) ? document : null))
            .Where(item => item.Parsed is not null)
            .Select(item => (item.Source, DiagramExport.Layout(item.Parsed!.Diagram).Layout))
            .Where(item => item.Layout is not null)
            .Select(item => (item.Source, item.Layout!));

    /// <summary>The degradations of an assessment.</summary>
    public static IReadOnlyList<Degradation> Degradations(Assessment assessment) =>
        assessment.Match(native: () => [], degraded: degradations => degradations, unsupported: _ => (IReadOnlyList<Degradation>)[]);
}

/// <summary>The scenarios of the drawio-export specification.</summary>
public class DrawIoRendererTests
{
    private static XDocument Render(string source, SvgRenderOptions? options = null) =>
        Parse(CanvasMother.Render(new DrawIoRenderer(), CanvasMother.Layout(source, options)));

    private static XDocument Parse(byte[] bytes)
    {
        bytes.Take(3).ShouldNotBe(new byte[] { 0xEF, 0xBB, 0xBF });
        return XDocument.Parse(Encoding.UTF8.GetString(bytes));
    }

    private static IEnumerable<XElement> Cells(XDocument file) => file.Descendants("mxCell").Where(cell => cell.Parent!.Name == "UserObject");

    private static string Id(XElement cell) => (string)cell.Parent!.Attribute("id")!;

    private static XElement Cell(XDocument file, string reference) =>
        Cells(file).First(cell => (string?)cell.Parent!.Attribute("morphir-ref") == reference);

    private static string Style(XElement cell) => (string)cell.Attribute("style")!;

    private static Dictionary<string, string> StyleOf(XElement cell) =>
        Style(cell).Split(';', StringSplitOptions.RemoveEmptyEntries).Select(entry => entry.Split('=', 2))
            .GroupBy(pair => pair[0]).ToDictionary(group => group.Key, group => group.First().Length > 1 ? group.First()[1] : "");

    [Test]
    public void TwoPagesGiveTwoDiagrams()
    {
        var file = Parse(CanvasMother.Render(new DrawIoRenderer(), CanvasMother.Layout("flowchart LR\n  A\n"), CanvasMother.Layout("flowchart LR\n  B\n")));

        file.Root!.Name.LocalName.ShouldBe("mxfile");
        file.Root.Elements("diagram").Select(diagram => (string)diagram.Attribute("name")!).ShouldBe(["Page 1", "Page 2"]);
        Cell(file, "node=B").Ancestors("diagram").Single().Attribute("name")!.Value.ShouldBe("Page 2");
    }

    [Test]
    public void EqualInputGivesEqualBytesAndNoMachineValues()
    {
        var layout = CanvasMother.Layout(DiagramCorpus.Representatives()[0].Diagram.Source);
        var first = CanvasMother.Render(new DrawIoRenderer(), layout);

        first.ShouldBe(CanvasMother.Render(new DrawIoRenderer(), layout));
        var root = Parse(first).Root!;
        foreach (var name in new[] { "modified", "etag", "agent", "version", "host" }) root.Attribute(name).ShouldBeNull();
        new DrawIoRenderer().MediaType.ShouldBe("application/vnd.jgraph.mxfile");
        new DrawIoRenderer().FileExtension.ShouldBe(".drawio");
    }

    [Test]
    public void AFlowchartWithBridgesIsDegradedOnlyInItsRoutes()
    {
        var layout = CanvasMother.Layout("flowchart TD\n  A --> C\n  B --> C\n  D --> E\n  A --> E\n  F --> A\n  A --> G\n  A --> H\n", SvgRenderOptions.Default.WithBridges(true));
        var degradations = CanvasMother.Degradations(DiagramExport.Assess([layout], new DrawIoRenderer()));

        degradations.ShouldNotBeEmpty();
        degradations.ShouldAllBe(degradation => degradation.Feature.Equals(LayoutFeature.ExactRoutes));
    }

    [Test]
    public void ALinkLeavingASubgraphIsReported()
    {
        var layout = CanvasMother.Layout("flowchart LR\n  subgraph S\n    A\n  end\n  A --> B\n");
        var degradations = CanvasMother.Degradations(DiagramExport.Assess([layout], new DrawIoRenderer()));

        degradations.ShouldContain(degradation => degradation.Feature.Equals(LayoutFeature.InterleavedContainers) && degradation.References.Contains(DiagramElementRef.Create("group=S")));
    }

    [Test]
    public void ANodeKeepsItsReferenceAndBox()
    {
        var file = Render("flowchart LR\n  A[Start]\n");
        var geometry = Cell(file, "node=A").Element("mxGeometry")!;
        var box = CanvasMother.Layout("flowchart LR\n  A[Start]\n").Drawables.Single().Kind.Match<LayoutBox?>(
            shape: shape => shape.Box, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null)!.Value;

        Cell(file, "node=A").Parent!.Attribute("label")!.Value.ShouldBe("Start");
        ((double)geometry.Attribute("x")!, (double)geometry.Attribute("y")!, (double)geometry.Attribute("width")!, (double)geometry.Attribute("height")!)
            .ShouldBe((box.X.ToPixels(), box.Y.ToPixels(), box.Width.ToPixels(), box.Height.ToPixels()));
    }

    [Test]
    public void AParticipantsPartsKeepPaintOrder()
    {
        var source = "sequenceDiagram\n  A->>B: hi\n";
        var file = Render(source);
        var order = Cells(file).ToList();
        var participant = order.Where(cell => (string?)cell.Parent!.Attribute("morphir-ref") == "participant=A").ToList();
        var message = order.IndexOf(Cell(file, "message=A/B/0"));

        CanvasMother.Degradations(DiagramExport.Assess([CanvasMother.Layout(source)], new DrawIoRenderer()))
            .ShouldNotContain(degradation => degradation.Feature.Equals(LayoutFeature.InterleavedPainting) || degradation.Feature.Equals(LayoutFeature.InterleavedContainers));
        participant.Count.ShouldBe(3);
        order.IndexOf(participant[0]).ShouldBeLessThan(message);
        participant.Skip(1).ShouldAllBe(cell => order.IndexOf(cell) > message);
    }

    [Test]
    public void AChildIsPlacedRelativeToItsContainer()
    {
        var file = Render("flowchart LR\n  subgraph S\n    A\n  end\n");
        var container = Cell(file, "group=S");
        var child = Cell(file, "node=A");
        var layout = CanvasMother.Layout("flowchart LR\n  subgraph S\n    A\n  end\n");
        var frame = layout.Drawables.Single().Kind.Match<LayoutContainer?>(shape: _ => null, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: c => c, group: _ => null)!;
        var node = frame.Children.Single().Kind.Match<LayoutShape?>(shape: s => s, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null)!;

        child.Attribute("parent")!.Value.ShouldBe(Id(container));
        StyleOf(container).ShouldContainKey("container");
        ((double)child.Element("mxGeometry")!.Attribute("x")!).ShouldBe((node.Box.X - frame.Box.X).ToPixels());
        ((double)child.Element("mxGeometry")!.Attribute("y")!).ShouldBe((node.Box.Y - frame.Box.Y).ToPixels());
    }

    [Test]
    public void EveryNamedEndIsAVertexAcrossEveryCorpus()
    {
        var failures = new List<string>();

        foreach (var (source, layout) in CanvasMother.Corpus())
        {
            var file = Parse(CanvasMother.Render(new DrawIoRenderer(), layout));

            foreach (var diagram in file.Root!.Elements("diagram"))
            {
                var vertices = diagram.Descendants("mxCell").Where(cell => (string?)cell.Attribute("vertex") == "1").Select(Id).ToHashSet(StringComparer.Ordinal);

                foreach (var edge in diagram.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1"))
                {
                    var geometry = edge.Element("mxGeometry")!;

                    foreach (var (end, point) in new[] { ("source", "sourcePoint"), ("target", "targetPoint") })
                    {
                        var named = (string?)edge.Attribute(end);
                        var ok = named is null
                            ? geometry.Elements("mxPoint").Any(element => (string?)element.Attribute("as") == point)
                            : vertices.Contains(named);
                        if (!ok) failures.Add($"{source} {Id(edge)} {end}");
                    }

                    var parent = (string)edge.Attribute("parent")!;
                    if (parent != "1" && !vertices.Contains(parent)) failures.Add($"{source} {Id(edge)} parent {parent}");
                }
            }
        }

        failures.ShouldBeEmpty(string.Join("\n", failures.Take(10)));
    }

    [Test]
    public void AFreeLineHasPoints()
    {
        var file = Render("@startebnf\nrule = \"a\", \"b\";\n@endebnf\n");
        var edges = file.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1").ToList();

        edges.ShouldNotBeEmpty();
        edges.ShouldAllBe(edge => edge.Attribute("source") == null && edge.Attribute("target") == null);
        edges.ShouldAllBe(edge => edge.Element("mxGeometry")!.Elements("mxPoint").Count() == 2);
    }

    [Test]
    public void AnEdgeCrossingContainerBoundariesBelongsToTheLayer()
    {
        var source = "flowchart LR\n  subgraph S\n    A\n  end\n  subgraph T\n    B\n  end\n  A --> B\n";
        var edge = Cell(Render(source), "edge=A/B/0");
        var line = DiagramLayoutLines(CanvasMother.Layout(source)).Single();

        edge.Attribute("parent")!.Value.ShouldBe("1");
        var points = edge.Element("mxGeometry")!.Element("Array")?.Elements("mxPoint").Select(point => ((double)point.Attribute("x")!, (double)point.Attribute("y")!)).ToList() ?? [];
        points.ShouldBe(line.Waypoints.Skip(1).Take(line.Waypoints.Length - 2).Select(point => (point.X.ToPixels(), point.Y.ToPixels())).ToList());
    }

    private static IEnumerable<LayoutLine> DiagramLayoutLines(DiagramLayout layout)
    {
        IEnumerable<Drawable> All(IEnumerable<Drawable> drawables) => drawables.SelectMany(drawable => All(drawable.Kind.Children).Prepend(drawable));
        return All(layout.Drawables).Select(drawable => drawable.Kind.Match<LayoutLine?>(shape: _ => null, line: line => line, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null))
            .OfType<LayoutLine>();
    }

    [Test]
    public void AnOpenArrowUsesTheOpenMarker()
    {
        var style = StyleOf(Cell(Render("flowchart LR\n  A --> B\n  B --o C\n"), "edge=B/C/0"));
        var filled = StyleOf(Cell(Render("flowchart LR\n  A --> B\n"), "edge=A/B/0"));

        style["endArrow"].ShouldBe("oval");
        (filled["endArrow"], filled["endFill"], filled["startArrow"]).ShouldBe(("block", "1", "none"));

        var open = StyleOf(Cell(Render("sequenceDiagram\n  A-)B: async\n"), "message=A/B/0"));
        open["endArrow"].ShouldBe("open");
    }

    [Test]
    public void ABpmnGatewayUsesTheBpmnSymbol()
    {
        var bpmn = DiagramCorpus.Representatives().Single(item => item.Family == "bpmn").Diagram.Source;
        var file = Render(bpmn);

        Cells(file).ShouldContain(cell => Style(cell).Contains("shape=mxgraph.bpmn.gateway2", StringComparison.Ordinal));
    }

    [Test]
    public void MarkupInALabelStaysText()
    {
        var cell = Cell(Render("flowchart LR\n  A[\"<b>x</b> & y\"]\n"), "node=A");
        var label = cell.Parent!.Attribute("label")!.Value;

        label.ShouldBe("&lt;b&gt;x&lt;/b&gt; &amp; y");
        System.Net.WebUtility.HtmlDecode(label).ShouldBe("<b>x</b> & y");
        StyleOf(cell)["html"].ShouldBe("1");
    }
}
