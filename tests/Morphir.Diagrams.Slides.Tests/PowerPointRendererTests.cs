// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Slides.Tests;

/// <summary>A stream that cannot seek.</summary>
public sealed class ForwardOnlyStream : MemoryStream
{
    public override bool CanSeek => false;
    public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    public override long Position { get => base.Position; set => throw new NotSupportedException(); }
}

/// <summary>Renders slide packages for the tests.</summary>
public static class SlideRendering
{
    /// <summary>The bytes and result of a render.</summary>
    public static (byte[] Bytes, RenderResult Result) Render(DiagramRenderer renderer, DegradationPolicy policy, params DiagramLayout[] pages)
    {
        using var output = new MemoryStream();
        var result = DiagramExport.RenderAsync(pages, renderer, output, policy, CancellationToken.None).GetAwaiter().GetResult();
        return (output.ToArray(), result);
    }

    /// <summary>The bytes of a render under allow-degraded, with no errors.</summary>
    public static byte[] Render(DiagramRenderer renderer, params DiagramLayout[] pages)
    {
        var (bytes, result) = Render(renderer, DegradationPolicy.AllowDegraded, pages);
        result.HasErrors.ShouldBeFalse(string.Join(", ", result.Diagnostics));
        return bytes;
    }

    /// <summary>The entries of a ZIP package by name.</summary>
    public static Dictionary<string, byte[]> Entries(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package));
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }, StringComparer.Ordinal);
    }

    /// <summary>An entry parsed as XML.</summary>
    public static XDocument Xml(byte[] package, string name) => XDocument.Parse(Encoding.UTF8.GetString(Entries(package)[name]));

    /// <summary>The degradations of an assessment.</summary>
    public static IReadOnlyList<Degradation> Degradations(Assessment assessment) =>
        assessment.Match(native: () => [], degraded: degradations => degradations, unsupported: _ => (IReadOnlyList<Degradation>)[]);
}

/// <summary>The scenarios of the powerpoint-export specification.</summary>
public class PowerPointRendererTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static byte[] Render(string source, PowerPointRenderer? renderer = null) =>
        SlideRendering.Render(renderer ?? new PowerPointRenderer(), SlideMother.Layout(source));

    private static XDocument Slide(byte[] package, int number = 1) => SlideRendering.Xml(package, $"ppt/slides/slide{number}.xml");

    private static string ShapeName(XElement shape) => (string?)shape.Descendants(P + "cNvPr").First().Attribute("name") ?? "";

    /// <summary>The drawn heads of lines: custom shapes named after their line.</summary>
    private static IEnumerable<XElement> Heads(XContainer container) =>
        container.Descendants(P + "sp").Where(shape => ShapeName(shape).EndsWith(" head", StringComparison.Ordinal));

    /// <summary>The exact lines: unfilled freeform shapes that are not heads.</summary>
    private static IEnumerable<XElement> Lines(XContainer container) =>
        container.Descendants(P + "sp").Where(shape =>
            !ShapeName(shape).EndsWith(" head", StringComparison.Ordinal) && shape.Descendants(A + "path").Any(path => (string?)path.Attribute("fill") == "none"));

    private static XElement Named(XDocument slide, string name) =>
        slide.Descendants().First(element => element.Name.LocalName == "cNvPr" && (string?)element.Attribute("name") == name).Parent!.Parent!;

    private static string[] Validate(byte[] package, FileFormatVersions version)
    {
        using var document = PresentationDocument.Open(new MemoryStream(package), false);
        return [.. new OpenXmlValidator(version).Validate(document).Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}")];
    }

    [Test]
    public void TwoPagesGiveTwoSlides()
    {
        var package = SlideRendering.Render(new PowerPointRenderer(), SlideMother.Layout(SlideMother.SingleNode), SlideMother.Layout("flowchart LR\n  B\n"));
        var presentation = SlideRendering.Xml(package, "ppt/presentation.xml");

        presentation.Descendants(P + "sldId").Count().ShouldBe(2);
        Named(Slide(package, 1), "node=A");
        Named(Slide(package, 2), "node=B");
        presentation.Descendants(P + "sldSz").Single().Attribute("cx")!.Value.ShouldBe("12192000");
        new PowerPointRenderer().FileExtension.ShouldBe(".pptx");
        new PowerPointRenderer().MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.presentationml.presentation");
    }

    [Test]
    // The ZIP writer compresses with the runtime's native zlib.
    [Category("PlatformSpecific")]
    public async Task EqualInputGivesEqualBytesForAnyStream()
    {
        var layout = SlideMother.Layout(DiagramCorpus.Representatives()[0].Diagram.Source);
        var seekable = new MemoryStream();
        var forward = new ForwardOnlyStream();

        await DiagramExport.RenderAsync([layout], new PowerPointRenderer(), seekable, DegradationPolicy.AllowDegraded, CancellationToken.None);
        await DiagramExport.RenderAsync([layout], new PowerPointRenderer(), forward, DegradationPolicy.AllowDegraded, CancellationToken.None);

        forward.ToArray().ShouldBe(seekable.ToArray());
        using var archive = new ZipArchive(new MemoryStream(seekable.ToArray()));
        archive.Entries.ShouldAllBe(entry => entry.LastWriteTime.Year == 1980);
    }

    [Test]
    public void APackageWithAnSvgImageValidatesForCurrentAndOlderOffice()
    {
        var (package, result) = SlideRendering.Render(new PowerPointRenderer(), DegradationPolicy.PictureFallback,
            SlideMother.Layout("flowchart LR\n  subgraph S\n    A --> B\n  end\n  B --> C\n"), SlideMother.Layout(SlideMother.Wide));

        result.HasErrors.ShouldBeFalse(string.Join(", ", result.Diagnostics));
        Validate(package, FileFormatVersions.Microsoft365).ShouldBeEmpty();
        Validate(package, FileFormatVersions.Office2010).ShouldBeEmpty();
    }

    [Test]
    public void EveryCorpusPackageValidates()
    {
        var failures = new List<string>();

        foreach (var source in DiagramCorpus.Sources().Where((_, index) => index % 4 == 0))
        {
            if (!DiagramSource.TryParse(source.Source, out var document, out _) || DiagramExport.Layout(document.Diagram).Layout is not { } layout) continue;
            var (package, result) = SlideRendering.Render(new PowerPointRenderer(), DegradationPolicy.AllowDegraded, layout);
            if (result.HasErrors) { failures.Add($"{source}: {string.Join(", ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))}"); continue; }
            var errors = Validate(package, FileFormatVersions.Microsoft365);
            if (errors.Length > 0) failures.Add($"{source}: {errors[0]}");
        }

        failures.ShouldBeEmpty(string.Join("\n", failures.Take(5)));
    }

    [Test]
    public void ADecisionNodeIsADiamondWithText()
    {
        var shape = Named(Slide(Render("flowchart LR\n  B{Approve?}\n")), "node=B");

        shape.Descendants(A + "prstGeom").Single().Attribute("prst")!.Value.ShouldBe("diamond");
        string.Concat(shape.Descendants(A + "t").Select(text => text.Value)).ShouldBe("Approve?");
    }

    [Test]
    public void ABpmnIconIsCustomGeometry()
    {
        var bpmn = DiagramCorpus.Representatives().Single(item => item.Family == "bpmn").Diagram.Source;
        var slide = Slide(Render(bpmn));

        slide.Descendants(A + "custGeom").ShouldNotBeEmpty();
        slide.Descendants(A + "cubicBezTo").ShouldNotBeEmpty();
    }

    [Test]
    public void AQuadraticCurveIsAQuadraticBezier()
    {
        var tree = Slide(Render("@startjson\n{\"a\": 1, \"b\": {\"c\": [1,2]}}\n@endjson\n"));
        tree.Descendants(A + "quadBezTo").ShouldNotBeEmpty();
    }

    [Test]
    public void ExactConnectorsDetachLinks()
    {
        var degradations = SlideRendering.Degradations(DiagramExport.Assess([SlideMother.Layout(SlideMother.Chain)], new PowerPointRenderer()));

        degradations.ShouldNotBeEmpty();
        degradations.ShouldAllBe(degradation => degradation.Feature.Equals(LayoutFeature.AttachedConnectors));
    }

    [Test]
    public void GluedConnectorsAttachToShapes()
    {
        var slide = Slide(Render(SlideMother.OneLink, new PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Glued)));
        var connector = Named(slide, "edge=A/B/0");
        string Id(string name) => (string)Named(slide, name).Descendants().First(element => element.Name.LocalName == "cNvPr").Attribute("id")!;

        connector.Descendants(A + "stCxn").Single().Attribute("id")!.Value.ShouldBe(Id("node=A"));
        connector.Descendants(A + "endCxn").Single().Attribute("id")!.Value.ShouldBe(Id("node=B"));
        connector.Descendants(A + "prstGeom").Single().Attribute("prst")!.Value.ShouldBe("straightConnector1");
    }

    [Test]
    public void GluedConnectorsUseTheSitesOfEachPreset()
    {
        // The site orders of ECMA-376 presetShapeDefinitions.xml: an ellipse has eight sites from the top, anticlockwise;
        // a can has the lid's lower edge first; a hexagon starts at the right.
        var slide = Slide(Render("flowchart LR\n  A((One)) --> B[(Two)] --> C{{Three}}\n  D[Four] --> A\n", new PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Glued)));
        (string Start, string End) Sites(string name)
        {
            var connector = Named(slide, name);
            return ((string)connector.Descendants(A + "stCxn").Single().Attribute("idx")!, (string)connector.Descendants(A + "endCxn").Single().Attribute("idx")!);
        }

        Sites("edge=A/B/0").ShouldBe(("6", "2"));
        Sites("edge=B/C/0").ShouldBe(("4", "3"));
        Sites("edge=D/A/0").ShouldBe(("3", "2"));
    }

    [Test]
    public void ATypefaceThatXmlCannotHoldIsRefused()
    {
        foreach (var typeface in new string?[] { null, " ", "Mono\u0001", "Mono\uD800" })
        {
            Should.Throw<ArgumentException>(() => new PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Exact, typeface));
            Should.Throw<ArgumentException>(() => new OpenDocumentRenderer(SlidePlacement.Widescreen, typeface));
        }

        new PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Glued, "Cascadia Mono").ToString().ShouldBe("PowerPointRenderer(glued, Cascadia Mono)");
    }

    [Test]
    public void EachOptionDeclaresItsLineFeatures()
    {
        var exact = new PowerPointRenderer().NativeFeatures;
        var glued = new PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Glued).NativeFeatures;

        exact.Contains(LayoutFeature.ExactRoutes).ShouldBeTrue();
        exact.Contains(LayoutFeature.MessageHeads).ShouldBeTrue();
        exact.Contains(LayoutFeature.AttachedConnectors).ShouldBeFalse();
        glued.Contains(LayoutFeature.AttachedConnectors).ShouldBeTrue();
        glued.Contains(LayoutFeature.MessageHeads).ShouldBeFalse();
        glued.Contains(LayoutFeature.ExactRoutes).ShouldBeFalse();
    }

    [Test]
    public void ExactConnectorsKeepTheirRoute()
    {
        var source = "flowchart TD\n  A --> C\n  B --> C\n  D --> E\n  A --> E\n  F --> A\n  A --> G\n  A --> H\n";
        var slide = Slide(Render(source, new PowerPointRenderer()));

        // An exact line is a freeform shape, because an application may re-route a connector between the corners of its box.
        slide.Descendants(P + "cxnSp").ShouldBeEmpty();
        Lines(slide).SelectMany(line => line.Descendants(A + "lnTo")).ShouldNotBeEmpty();
        // The bridged exact paths have their arcs as cubic curves.
        var bridged = SlideRendering.Render(new PowerPointRenderer(), SlideMother.Layout(source, SvgRenderOptions.Default.WithBridges(true)));
        Lines(Slide(bridged)).SelectMany(line => line.Descendants(A + "cubicBezTo")).ShouldNotBeEmpty();
    }

    [Test]
    public void ACrossHeadIsDrawnAsAShapeGroupedWithItsLine()
    {
        var slide = Slide(Render(SlideMother.LostMessage));
        var group = slide.Descendants(P + "grpSp").ShouldHaveSingleItem();

        Lines(group).ShouldHaveSingleItem();
        Heads(group).ShouldHaveSingleItem().Descendants(A + "custGeom").ShouldNotBeEmpty();
    }

    [Test]
    public void HalfHeadsAreDrawnOnTheirSide()
    {
        var slide = Slide(Render("sequenceDiagram\n  A-|\\B: upper filled\n  A-|/B: lower filled\n  A-\\\\B: upper\n  A-//B: lower\n"));
        var heads = slide.Descendants(P + "grpSp").Select(group => Heads(group).Single()).ToList();

        heads.Count.ShouldBe(4);
        // Each message runs left to right, so an upper head lies above its line and a lower head below it.
        double Y(XElement shape, string corner) =>
            double.Parse(shape.Descendants(A + "off").First().Attribute("y")!.Value) + (corner == "bottom" ? double.Parse(shape.Descendants(A + "ext").First().Attribute("cy")!.Value) : 0);
        double LineY(XElement shape) => double.Parse(Lines(shape.Parent!).Single().Descendants(A + "off").First().Attribute("y")!.Value);

        Y(heads[0], "bottom").ShouldBeLessThanOrEqualTo(LineY(heads[0]) + 1);
        Y(heads[1], "top").ShouldBeGreaterThanOrEqualTo(LineY(heads[1]) - 1);
        Y(heads[2], "bottom").ShouldBeLessThanOrEqualTo(LineY(heads[2]) + 1);
        Y(heads[3], "top").ShouldBeGreaterThanOrEqualTo(LineY(heads[3]) - 1);
    }

    [Test]
    public void ElementsAndContainersAreSeparateShapesInPaintOrder()
    {
        var slide = Slide(Render("flowchart LR\n  subgraph S[Group]\n    A --> B\n  end\n"));
        var names = slide.Descendants(P + "cNvPr").Select(element => (string)element.Attribute("name")!).ToList();

        // A flowchart has no drawn heads, so it has no PowerPoint groups, and the frame of the subgraph is under its nodes.
        slide.Descendants(P + "grpSp").ShouldBeEmpty();
        names.IndexOf("group=S").ShouldBeGreaterThanOrEqualTo(0);
        names.IndexOf("group=S").ShouldBeLessThan(names.IndexOf("node=A"));
        names.IndexOf("node=A").ShouldBeLessThan(names.LastIndexOf("node=B"));
    }

    [Test]
    public void AFillKeepsItsOpacity()
    {
        const string source = SlideMother.SingleNode;
        var fill = SlideMother.Layout(source).Drawables.SelectMany(drawable => drawable.Kind.Children.Prepend(drawable))
            .Where(drawable => drawable.Reference?.ToString() == "node=A")
            .Select(drawable => drawable.Kind.Match<LayoutColor?>(shape: shape => shape.Paint.Fill, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null))
            .First(color => color is not null)!;
        var slide = Slide(Render(source));

        // The theme fills a node with a translucent colour.
        fill.Color.Opacity.ShouldBeLessThan(1m);
        Named(slide, "node=A").Element(P + "spPr")!.Element(A + "solidFill")!.Descendants(A + "alpha").Single().Attribute("val")!.Value
            .ShouldBe(((long)Math.Round(fill.Color.Opacity * 100000m)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public void ALinkOpensOnClick()
    {
        var package = SlideRendering.Render(new PowerPointRenderer(), SlideMother.LinkedNode());
        var rid = Named(Slide(package), "node=A").Descendants(A + "hlinkClick").Single().Attribute(R + "id")!.Value;
        var rels = SlideRendering.Xml(package, "ppt/slides/_rels/slide1.xml.rels");

        rels.Root!.Elements().Single(rel => (string?)rel.Attribute("Id") == rid).Attribute("Target")!.Value.ShouldBe("https://example.org");
    }

    [Test]
    public void ACompositeElementsLinkReachesEachOfItsParts()
    {
        var package = SlideRendering.Render(new PowerPointRenderer(), SlideMother.Linked(SlideMother.LinkedParticipant, "participant=A"));
        var parts = Slide(package).Descendants(P + "cNvPr").Where(element => (string?)element.Attribute("name") == "participant=A").ToList();

        parts.Count.ShouldBeGreaterThan(1);
        parts.ShouldAllBe(element => element.Element(A + "hlinkClick") != null);
    }

    [Test]
    public void APictureFallbackPageHasBothImages()
    {
        var (package, result) = SlideRendering.Render(new PowerPointRenderer(), DegradationPolicy.PictureFallback, SlideMother.Layout(SlideMother.SubgraphNode));
        var entries = SlideRendering.Entries(package);
        var picture = Slide(package).Descendants(P + "pic").ShouldHaveSingleItem();

        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "layout/pictured");
        entries.Keys.ShouldContain("ppt/media/image1.png");
        entries.Keys.ShouldContain("ppt/media/image1.svg");
        // Office applications read no CSS variables, so the SVG carries the light palette's values.
        var svg = Encoding.UTF8.GetString(entries["ppt/media/image1.svg"]);
        svg.ShouldContain("<svg");
        svg.ShouldNotContain("var(");
        picture.Descendants().Any(element => element.Name.LocalName == "svgBlip").ShouldBeTrue();
    }

    [Test]
    public void ANamedColourOutsideThePaintersTableIsReported()
    {
        var (_, result) = SlideRendering.Render(new PowerPointRenderer(), DegradationPolicy.PictureFallback, SlideMother.Layout("sequenceDiagram\n  box Aqua Team\n  participant A\n  end\n  A-xA: hi\n"));

        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "slides/png-approximated" && diagnostic.Message.Contains("Aqua"));
    }

    [Test]
    public void ALargeImageIsDrawnAtALowerDensity()
    {
        // About 30 million CSS pixels: two pixels per CSS pixel exceeds the raster's limit of 40 million, one does not.
        var placement = SlidePlacement.Create(SlideSize.Create(Distance.FromInches(78), Distance.FromInches(41))).WithArea(SlideArea.Full);
        var (package, result) = SlideRendering.Render(new PowerPointRenderer(placement), DegradationPolicy.PictureFallback, SlideMother.Layout(SlideMother.PicturedSubgraph));

        result.HasErrors.ShouldBeFalse(string.Join(", ", result.Diagnostics));
        var png = SlideRendering.Entries(package)["ppt/media/image1.png"];
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        var placed = double.Parse(Slide(package).Descendants(P + "pic").Single().Element(P + "spPr")!.Descendants(A + "ext").First().Attribute("cx")!.Value) / 9525;

        Math.Abs(width - Math.Ceiling(placed)).ShouldBeLessThanOrEqualTo(1);
    }

    [Test]
    public void ARasterFailureWritesNothing()
    {
        var placement = SlidePlacement.Create(SlideSize.Create(Distance.FromInches(200), Distance.FromInches(200))).WithArea(SlideArea.Full);
        var (package, result) = SlideRendering.Render(new PowerPointRenderer(placement), DegradationPolicy.PictureFallback, SlideMother.Layout(SlideMother.PicturedSubgraph));

        package.ShouldBeEmpty();
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "slides/raster-failed");
    }

    [Test]
    public void CancellationIsObservedBetweenImages()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Should.Throw<OperationCanceledException>(() => DiagramExport.RenderAsync([SlideMother.Layout(SlideMother.Wide)], new PowerPointRenderer(), new MemoryStream(), DegradationPolicy.PictureFallback, cancel.Token).GetAwaiter().GetResult());
    }
}
