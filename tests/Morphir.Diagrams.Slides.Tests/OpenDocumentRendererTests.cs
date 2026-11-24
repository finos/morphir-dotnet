// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Slides.Tests;

/// <summary>The scenarios of the opendocument-presentation-export specification.</summary>
public class OpenDocumentRendererTests
{
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Presentation = "urn:oasis:names:tc:opendocument:xmlns:presentation:1.0";
    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    private static byte[] Render(string source) => SlideRendering.Render(new OpenDocumentRenderer(), SlideMother.Layout(source));

    private static XDocument Content(byte[] package) => SlideRendering.Xml(package, "content.xml");

    private static XElement Named(XDocument content, string name) =>
        content.Descendants().First(element => (string?)element.Attribute(Draw + "name") == name);

    /// <summary>The graphic style of a shape, from the automatic styles of the content.</summary>
    private static XElement GraphicProperties(XDocument content, XElement shape) =>
        content.Descendants(Style + "style").Single(style => (string?)style.Attribute(Style + "name") == (string?)shape.Attribute(Draw + "style-name"))
            .Element(Style + "graphic-properties")!;

    [Test]
    public void ThePackageStartsWithItsMediaType()
    {
        var package = Render(SlideMother.Wide);
        var header = package.AsSpan();
        const string mediaType = "application/vnd.oasis.opendocument.presentation";

        BinaryPrimitives.ReadUInt32LittleEndian(header).ShouldBe(0x04034b50u);
        (BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) & 0x0008).ShouldBe(0, "no data descriptor");
        BinaryPrimitives.ReadUInt16LittleEndian(header[8..]).ShouldBe((ushort)0, "stored");
        BinaryPrimitives.ReadUInt16LittleEndian(header[26..]).ShouldBe((ushort)8);
        BinaryPrimitives.ReadUInt16LittleEndian(header[28..]).ShouldBe((ushort)0, "no extra field");
        Encoding.ASCII.GetString(header.Slice(30, 8)).ShouldBe("mimetype");
        Encoding.ASCII.GetString(header.Slice(38, mediaType.Length)).ShouldBe(mediaType);
        new OpenDocumentRenderer().MediaType.ShouldBe(mediaType);
        new OpenDocumentRenderer().FileExtension.ShouldBe(".odp");
    }

    [Test]
    public async Task EqualInputGivesEqualBytesForAnyStream()
    {
        var layout = SlideMother.Layout(DiagramCorpus.Representatives()[0].Diagram.Source);
        var seekable = new MemoryStream();
        var forward = new ForwardOnlyStream();

        await DiagramExport.RenderAsync([layout], new OpenDocumentRenderer(), seekable, DegradationPolicy.AllowDegraded, CancellationToken.None);
        await DiagramExport.RenderAsync([layout], new OpenDocumentRenderer(), forward, DegradationPolicy.AllowDegraded, CancellationToken.None);

        forward.ToArray().ShouldBe(seekable.ToArray());
        seekable.Length.ShouldBeGreaterThan(0);
    }

    [Test]
    public void EachLayoutIsAPageOfTheSlideSize()
    {
        var package = SlideRendering.Render(new OpenDocumentRenderer(), SlideMother.Layout(SlideMother.Wide), SlideMother.Layout("flowchart LR\n  A\n"));
        var styles = SlideRendering.Xml(package, "styles.xml");
        var page = styles.Descendants(Style + "page-layout-properties").Single();

        var content = Content(package);
        content.Descendants(Draw + "page").Count().ShouldBe(2);
        var background = content.Descendants(Style + "style").Single(style => (string?)style.Attribute(Style + "name") == (string?)content.Descendants(Draw + "page").First().Attribute(Draw + "style-name"));
        ((string?)background.Descendants(Style + "drawing-page-properties").Single().Attribute(Draw + "fill-color")).ShouldBe("#f4f5f7");
        ((string?)page.Attribute(XName.Get("page-width", "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"))).ShouldBe("338.667mm");
        ((string?)page.Attribute(XName.Get("page-height", "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"))).ShouldBe("190.5mm");
        SlideRendering.Xml(package, "META-INF/manifest.xml").Descendants(Manifest + "file-entry")
            .Select(entry => (string?)entry.Attribute(Manifest + "full-path")).ShouldBe(["/", "content.xml", "styles.xml"]);
    }

    [Test]
    public void ACylinderHasItsOwnGeometry()
    {
        var shape = Named(Content(Render("flowchart LR\n  D[(Orders)]\n")), "node=D");
        var geometry = shape.Element(Draw + "enhanced-geometry").ShouldNotBeNull();

        shape.Name.ShouldBe(Draw + "custom-shape");
        // Impress replaces the geometry and text area of a preset it recognises, so every shape is non-primitive.
        ((string?)geometry.Attribute(Draw + "type")).ShouldBe("non-primitive");
        ((string?)geometry.Attribute(Svg + "viewBox")).ShouldNotBeNull().ShouldStartWith("0 0 ");
        var path = (string)geometry.Attribute(Draw + "enhanced-path")!;
        path.ShouldContain("C");
        // The body is closed and filled; the front edge of the lid is an open, unfilled second path.
        path.ShouldContain("Z N");
        path.ShouldEndWith("F N");
        string.Concat(shape.Descendants(Text + "span").Select(span => span.Value)).ShouldBe("Orders");
    }

    [Test]
    public void ABpmnIconWithArcsUsesOnlyEnhancedPathCommands()
    {
        var bpmn = DiagramCorpus.Representatives().Single(item => item.Family == "bpmn").Diagram.Source;
        var paths = Content(Render(bpmn)).Descendants(Draw + "enhanced-geometry").Select(geometry => (string)geometry.Attribute(Draw + "enhanced-path")!).ToList();

        paths.ShouldNotBeEmpty();
        paths.SelectMany(path => Regex.Matches(path, "[A-Za-z]").Select(match => match.Value)).Distinct().Order(StringComparer.Ordinal)
            .ShouldAllBe(letter => "CFLMNQZ".Contains(letter, StringComparison.Ordinal));
        paths.ShouldContain(path => path.Contains('C'));
    }

    [Test]
    public void AConnectorIsGluedToBothShapes()
    {
        var content = Content(Render("flowchart LR\n  A --> B\n"));
        var connector = Named(content, "edge=A/B/0");
        string Id(string name) => (string)Named(content, name).Attribute(Draw + "id")!;

        connector.Name.ShouldBe(Draw + "connector");
        ((string?)connector.Attribute(Draw + "start-shape")).ShouldBe(Id("node=A"));
        ((string?)connector.Attribute(Draw + "end-shape")).ShouldBe(Id("node=B"));
        // The default glue points are top, right, bottom and left.
        ((string?)connector.Attribute(Draw + "start-glue-point")).ShouldBe("1");
        ((string?)connector.Attribute(Draw + "end-glue-point")).ShouldBe("3");
        ((string?)connector.Attribute(Svg + "d")).ShouldNotBeNull().ShouldStartWith("M");
        ((string?)GraphicProperties(content, connector).Attribute(Draw + "marker-end")).ShouldNotBeNull();
    }

    [Test]
    public void ACrossHeadIsAFilledOutline()
    {
        var package = Render("sequenceDiagram\n  A-xB: lost\n");
        var content = Content(package);
        // The lifelines are connectors too; the message is the one with an end marker.
        var line = content.Descendants(Draw + "connector").Single(connector => GraphicProperties(content, connector).Attribute(Draw + "marker-end") is not null);
        var marker = (string)GraphicProperties(content, line).Attribute(Draw + "marker-end")!;
        var definition = SlideRendering.Xml(package, "styles.xml").Descendants(Draw + "marker").Single(element => (string?)element.Attribute(Draw + "name") == marker);
        var d = (string)definition.Attribute(Svg + "d")!;

        // The union of the cross's two strokes at the marker stroke width is one closed outline of twelve corners.
        Regex.Matches(d, "[A-Za-z]").Select(match => match.Value).ShouldBe(["M", .. Enumerable.Repeat("L", 11), "Z"]);
        ((string?)GraphicProperties(content, line).Attribute(Draw + "marker-end-center")).ShouldBe("true");
    }

    [Test]
    public void EveryHeadIsAClosedMarkerOutline()
    {
        var package = Render("sequenceDiagram\n  A->>B: arrow\n  A-)B: async\n  A-xB: cross\n  A-|\\B: upper filled\n  A-|/B: lower filled\n  A-\\\\B: upper\n  A-//B: lower\n");
        var markers = SlideRendering.Xml(package, "styles.xml").Descendants(Draw + "marker").ToList();

        markers.Count.ShouldBeGreaterThanOrEqualTo(6);
        markers.ShouldAllBe(marker => ((string)marker.Attribute(Svg + "d")!).TrimEnd().EndsWith('Z'));
        // The SVG arrow M-9,-4 L0,0 L-9,4 Z, turned so that the line arrives along +y, in hundredths of a pixel.
        ((string)markers.Single(marker => (string?)marker.Attribute(Draw + "name") == "Morphir_arrow").Attribute(Svg + "d")!).ShouldStartWith("M-400 900 L0 0 L400 900 Z");
        // Each view box is centred on the tip, so that a centred marker puts the tip at the line's end.
        foreach (var marker in markers)
        {
            var box = ((string)marker.Attribute(Svg + "viewBox")!).Split(' ').Select(int.Parse).ToArray();
            (box[0] * 2 + box[2]).ShouldBe(0);
            (box[1] * 2 + box[3]).ShouldBe(0);
        }
    }

    [Test]
    public void TheRendererDeclaresItsFeatures()
    {
        var features = new OpenDocumentRenderer().NativeFeatures;

        foreach (var feature in new[] { LayoutFeature.Polylines, LayoutFeature.ExactRoutes, LayoutFeature.AttachedConnectors, LayoutFeature.MessageHeads, LayoutFeature.FillOpacity, LayoutFeature.SvgImages, LayoutFeature.Pages })
            features.Contains(feature).ShouldBeTrue(feature.ToString());

        features.Contains(LayoutFeature.StrokeAndTextOpacity).ShouldBeFalse();
        features.Contains(LayoutFeature.Containers).ShouldBeFalse();
    }

    [Test]
    public void OnlyASubgraphDegradesAFlowchart()
    {
        var degradations = SlideRendering.Degradations(DiagramExport.Assess([SlideMother.Layout("flowchart LR\n  subgraph S\n    A --> B\n  end\n  B --> S\n  C --> D\n")], new OpenDocumentRenderer()));

        degradations.ShouldNotBeEmpty();
        degradations.ShouldAllBe(degradation => degradation.Feature.Equals(LayoutFeature.Containers) || degradation.Feature.Equals(LayoutFeature.AttachedConnectors));
        degradations.ShouldContain(degradation => degradation.Feature.Equals(LayoutFeature.Containers));
    }

    [Test]
    public void AFillKeepsItsOpacityAndALinkOpensOnClick()
    {
        var content = Content(SlideRendering.Render(new OpenDocumentRenderer(), SlideMother.LinkedNode()));
        var shape = Named(content, "node=A");

        ((string?)GraphicProperties(content, shape).Attribute(Draw + "opacity")).ShouldNotBeNull().ShouldEndWith("%");
        var listener = shape.Descendants(Presentation + "event-listener").ShouldHaveSingleItem();
        ((string?)listener.Attribute(XLink + "href")).ShouldBe("https://example.org");
    }

    [Test]
    public void AnImageHasAPngFallback()
    {
        var (package, result) = SlideRendering.Render(new OpenDocumentRenderer(), DegradationPolicy.PictureFallback, SlideMother.Layout("flowchart LR\n  subgraph S\n    A\n  end\n"));
        var frame = Content(package).Descendants(Draw + "frame").ShouldHaveSingleItem();
        var images = frame.Elements(Draw + "image").Select(image => (string?)image.Attribute(XLink + "href")).ToList();

        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "layout/pictured");
        images.ShouldBe(["Pictures/image1.svg", "Pictures/image1.png"]);
        var entries = SlideRendering.Entries(package);
        Encoding.UTF8.GetString(entries["Pictures/image1.svg"]).ShouldNotContain("var(");
        entries["Pictures/image1.png"].AsSpan(1, 3).ToArray().ShouldBe("PNG"u8.ToArray());
        SlideRendering.Xml(package, "META-INF/manifest.xml").Descendants(Manifest + "file-entry")
            .Select(entry => (string?)entry.Attribute(Manifest + "full-path")).ShouldContain("Pictures/image1.png");
    }

    [Test]
    public void ARasterFailureWritesNothing()
    {
        var placement = SlidePlacement.Create(SlideSize.Create(Distance.FromInches(200), Distance.FromInches(200))).WithArea(SlideArea.Full);
        var (package, result) = SlideRendering.Render(new OpenDocumentRenderer(placement), DegradationPolicy.PictureFallback, SlideMother.Layout("flowchart LR\n  subgraph S\n    A --> B\n  end\n"));

        package.ShouldBeEmpty();
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "slides/raster-failed");
    }
}
