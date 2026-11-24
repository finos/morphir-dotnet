// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Shouldly;

namespace Morphir.Diagrams.Slides.Tests;

/// <summary>The scenarios of the google-slides-export specification.</summary>
public class GoogleSlidesRendererTests
{
    private static readonly SlideObjectNamespace Flow = SlideObjectNamespace.Create("flow");

    private static GoogleSlidesRenderer Renderer(SlideObjectNamespace? objects = null, SlideObjectId? existing = null)
    {
        var options = GoogleSlidesOptions.Create(SlidePlacement.Widescreen, objects ?? Flow);
        return new GoogleSlidesRenderer(existing is null ? options : options.WithExistingSlide(existing));
    }

    /// <summary>The requests of a render, as (kind, body) pairs in order.</summary>
    private static List<(string Kind, JsonElement Body)> Requests(byte[] output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("requests").EnumerateArray()
            .Select(request => request.EnumerateObject().Single())
            .Select(property => (property.Name, property.Value.Clone()))
            .ToList();
    }

    private static byte[] Render(string source) => SlideRendering.Render(Renderer(), SlideMother.Layout(source));

    [Test]
    public void AFlowchartBecomesRequests()
    {
        var output = Render("flowchart LR\n  A[Start] --> B[End]\n");
        var requests = Requests(output);

        output.AsSpan(0, 3).ToArray().ShouldNotBe(new byte[] { 0xEF, 0xBB, 0xBF });
        requests.Count(request => request.Kind == "createShape").ShouldBe(2);
        requests.Count(request => request.Kind == "createLine").ShouldBe(1);
        requests.ShouldContain(request => request.Kind == "insertText" && request.Body.GetProperty("text").GetString() == "Start");
        Renderer().FileExtension.ShouldBe(".gslides.json");
        Renderer().MediaType.ShouldBe("application/json");
    }

    [Test]
    public void AnExistingSlideTakesOnePage()
    {
        var renderer = Renderer(existing: SlideObjectId.Create("p"));
        var (bytes, result) = SlideRendering.Render(renderer, DegradationPolicy.AllowDegraded, SlideMother.Layout(SlideMother.Wide), SlideMother.Layout(SlideMother.Wide));

        bytes.ShouldBeEmpty();
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Message.Contains('2'));

        var one = Requests(SlideRendering.Render(renderer, SlideMother.Layout(SlideMother.Wide)));
        one.ShouldNotContain(request => request.Kind == "createSlide");
        one.Where(request => request.Kind is "createShape" or "createLine")
            .ShouldAllBe(request => request.Body.GetProperty("elementProperties").GetProperty("pageObjectId").GetString() == "p");
    }

    [Test]
    public void NewSlidesForSeveralPages()
    {
        var requests = Requests(SlideRendering.Render(Renderer(), SlideMother.Layout(SlideMother.Wide), SlideMother.Layout(SlideMother.SingleNode)));
        var slides = requests.Where(request => request.Kind == "createSlide").Select(request => request.Body.GetProperty("objectId").GetString()).ToList();

        slides.ShouldBe(["flow_0_s0", "flow_1_s0"]);
        requests.Where(request => request.Kind == "createSlide")
            .ShouldAllBe(request => request.Body.GetProperty("slideLayoutReference").GetProperty("predefinedLayout").GetString() == "BLANK");
        var pages = requests.Where(request => request.Kind == "createShape").Select(request => request.Body.GetProperty("elementProperties").GetProperty("pageObjectId").GetString()).Distinct().ToList();
        pages.ShouldBe(["flow_0_s0", "flow_1_s0"]);
    }

    [Test]
    public void ExistingSlideIdsAreParsed()
    {
        SlideObjectId.TryParse("slide 1", out _, out var failure).ShouldBeFalse();
        failure.ShouldNotBeNull();
        SlideObjectId.TryParse(null, out _, out _).ShouldBeFalse();
        SlideObjectId.TryParse(new string('a', 51), out _, out _).ShouldBeFalse();
        SlideObjectId.TryParse("g1a2:b-c_d", out var id, out _).ShouldBeTrue();
        id!.Value.ShouldBe("g1a2:b-c_d");
        SlideObjectId.Create("p").ToString().ShouldBe("p");
        Should.Throw<ArgumentException>(() => SlideObjectId.Create(""));
        SlideObjectId.Create("p").Equals(SlideObjectId.Create("p")).ShouldBeTrue();
    }

    [Test]
    public void NamespacesAreParsed()
    {
        foreach (var text in new[] { "a", "é1", "ab cd", "ab.cd", "-ab", ":ab", new string('a', 31), "" })
            SlideObjectNamespace.TryParse(text, out _, out _).ShouldBeFalse(text);

        SlideObjectNamespace.TryParse(null, out _, out var failure).ShouldBeFalse();
        failure.ShouldNotBeNull();
        foreach (var text in new[] { "ab", "_a", "9z", "flow:chart-1_x", new string('a', 30) })
            SlideObjectNamespace.TryParse(text, out _, out _).ShouldBeTrue(text);

        SlideObjectNamespace.Create("flow").Value.ShouldBe("flow");
        SlideObjectNamespace.Create("flow").Equals(SlideObjectNamespace.Create("flow")).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => SlideObjectNamespace.Create("a"));
    }

    [Test]
    public void ObjectIdsAreDeterministicAndValid()
    {
        var pages = new[] { SlideMother.Layout(SlideMother.Chain), SlideMother.Layout(SlideMother.Message) };

        foreach (var objects in new[] { SlideObjectNamespace.Create("ab"), SlideObjectNamespace.Create(new string('n', 30)) })
        {
            var first = SlideRendering.Render(Renderer(objects), pages);
            var second = SlideRendering.Render(Renderer(objects), pages);
            first.ShouldBe(second);

            var ids = Requests(first).Where(request => request.Kind is "createSlide" or "createShape" or "createLine")
                .Select(request => request.Body.GetProperty("objectId").GetString()!).ToList();
            ids.ShouldNotBeEmpty();
            ids.Distinct().Count().ShouldBe(ids.Count);
            ids.ShouldAllBe(id => id.StartsWith(objects.Value + "_", StringComparison.Ordinal) && id.Length >= 5 && id.Length <= 50);
        }
    }

    [Test]
    public void ALinkPaintedBeforeItsNodesConnectsToThem()
    {
        var requests = Requests(Render(SlideMother.Chain));
        int Created(string id) => requests.FindIndex(request => request.Kind == "createShape" && request.Body.GetProperty("objectId").GetString() == id);
        var connections = requests.Select((request, index) => (request, index))
            .Where(item => item.request.Kind == "updateLineProperties" && item.request.Body.GetProperty("lineProperties").TryGetProperty("startConnection", out _)).ToList();

        connections.Count.ShouldBe(2);

        foreach (var (request, index) in connections)
        {
            var properties = request.Body.GetProperty("lineProperties");
            foreach (var end in new[] { "startConnection", "endConnection" })
            {
                var created = Created(properties.GetProperty(end).GetProperty("connectedObjectId").GetString()!);
                created.ShouldBeGreaterThanOrEqualTo(0);
                created.ShouldBeLessThan(index);
            }
        }
    }

    [Test]
    public void TooManyPagesAreRefused()
    {
        var page = SlideMother.Layout(SlideMother.SingleNode);
        var (bytes, result) = SlideRendering.Render(Renderer(), DegradationPolicy.AllowDegraded, Enumerable.Repeat(page, 10_000).ToArray());

        bytes.ShouldBeEmpty();
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Message.Contains("9,999 pages"));
    }

    [Test]
    public void ConnectorsAreAttachedAtConnectionSites()
    {
        var requests = Requests(Render(SlideMother.OneLink));
        string ShapeOf(string text) =>
            requests.Single(request => request.Kind == "insertText" && request.Body.GetProperty("text").GetString() == text).Body.GetProperty("objectId").GetString()!;
        var line = requests.Single(request => request.Kind == "updateLineProperties" && request.Body.GetProperty("lineProperties").TryGetProperty("startConnection", out _))
            .Body.GetProperty("lineProperties");

        line.GetProperty("startConnection").GetProperty("connectedObjectId").GetString().ShouldBe(ShapeOf("A"));
        line.GetProperty("endConnection").GetProperty("connectedObjectId").GetString().ShouldBe(ShapeOf("B"));
        // A rectangle's sites are top, left, bottom and right, as in ECMA-376.
        line.GetProperty("startConnection").GetProperty("connectionSiteIndex").GetInt32().ShouldBe(3);
        line.GetProperty("endConnection").GetProperty("connectionSiteIndex").GetInt32().ShouldBe(1);
        requests.Where(request => request.Kind == "updateLineProperties").Select(request => request.Body.GetProperty("lineProperties"))
            .Where(properties => properties.TryGetProperty("endArrow", out _)).Select(properties => properties.GetProperty("endArrow").GetString())
            .ShouldBe(["FILL_ARROW"]);
    }

    [Test]
    public void TheRendererDeclaresItsFeatures()
    {
        var features = Renderer().NativeFeatures;

        foreach (var feature in new[] { LayoutFeature.RoundedRectangles, LayoutFeature.Cylinders, LayoutFeature.AttachedConnectors, LayoutFeature.FillOpacity, LayoutFeature.Links, LayoutFeature.Pages, LayoutFeature.InterleavedPainting })
            features.Contains(feature).ShouldBeTrue(feature.ToString());

        foreach (var feature in new[] { LayoutFeature.Paths, LayoutFeature.Polylines, LayoutFeature.MessageHeads, LayoutFeature.DashPatterns, LayoutFeature.SvgImages, LayoutFeature.RotatedText })
            features.Contains(feature).ShouldBeFalse(feature.ToString());
    }

    [Test]
    public void ANodeIsPlacedInEmuInsideTheContentArea()
    {
        var shape = Requests(Render("flowchart LR\n  A[Start]\n")).Single(request => request.Kind == "createShape").Body.GetProperty("elementProperties");
        var size = shape.GetProperty("size");
        var transform = shape.GetProperty("transform");

        size.GetProperty("width").GetProperty("unit").GetString().ShouldBe("EMU");
        transform.GetProperty("unit").GetString().ShouldBe("EMU");
        var (x, y) = (transform.GetProperty("translateX").GetInt64(), transform.GetProperty("translateY").GetInt64());
        var (width, height) = (size.GetProperty("width").GetProperty("magnitude").GetInt64(), size.GetProperty("height").GetProperty("magnitude").GetInt64());

        x.ShouldBeGreaterThanOrEqualTo(1_016_000L);
        y.ShouldBeGreaterThanOrEqualTo(571_500L);
        (x + width).ShouldBeLessThanOrEqualTo(1_016_000L + 10_160_000L);
        (y + height).ShouldBeLessThanOrEqualTo(571_500L + 5_715_000L);
    }

    [Test]
    public void ACompositeElementsLinkReachesEachOfItsParts()
    {
        var requests = Requests(SlideRendering.Render(Renderer(), SlideMother.Linked(SlideMother.LinkedParticipant, "participant=A")));
        var linked = requests.Where(request => request.Kind is "updateShapeProperties" or "updateLineProperties")
            .Select(request => request.Body.GetProperty(request.Kind == "updateShapeProperties" ? "shapeProperties" : "lineProperties"))
            .Count(properties => properties.TryGetProperty("link", out _));

        // The participant's two boxes, its lifeline and its name texts.
        linked.ShouldBeGreaterThan(2);
    }

    [Test]
    public void AFillKeepsItsAlphaAndALinkIsKept()
    {
        var requests = Requests(SlideRendering.Render(Renderer(), SlideMother.LinkedNode()));
        var properties = requests.Single(request => request.Kind == "updateShapeProperties").Body.GetProperty("shapeProperties");

        properties.GetProperty("shapeBackgroundFill").GetProperty("solidFill").GetProperty("alpha").GetDouble().ShouldBeLessThan(1.0);
        properties.GetProperty("link").GetProperty("url").GetString().ShouldBe("https://example.org");
        Encoding.UTF8.GetString(SlideRendering.Render(Renderer(), SlideMother.LinkedNode())).ShouldNotContain("groupObjects");
    }
}
