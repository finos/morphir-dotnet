// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Morphir.Knowledge.Rdf;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>The scenarios of the diagram-layout specification.</summary>
public class DiagramLayoutTests
{
    /// <summary>Parses any supported source.</summary>
    public static Diagram Parse(string source) =>
        DiagramSource.TryParse(source, out var document, out var errors) ? document.Diagram : throw new InvalidOperationException(string.Join(", ", errors));

    /// <summary>The layout of a source with the default options.</summary>
    public static DiagramLayout Layout(string source) => DiagramExport.Layout(Parse(source)).Layout.ShouldNotBeNull();

    /// <summary>Every drawable of a layout, depth first.</summary>
    public static IEnumerable<Drawable> All(IEnumerable<Drawable> drawables) =>
        drawables.SelectMany(drawable => All(drawable.Kind.Children).Prepend(drawable));

    /// <summary>The drawable whose reference has this text.</summary>
    public static Drawable Find(DiagramLayout layout, string reference) =>
        All(layout.Drawables).First(drawable => drawable.Reference?.ToString() == reference);

    public static LayoutShape ShapeOf(Drawable drawable) =>
        drawable.Kind.Match<LayoutShape?>(shape: shape => shape, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null)
            .ShouldNotBeNull(drawable.ToString());

    public static LayoutLine LineOf(Drawable drawable) =>
        drawable.Kind.Match<LayoutLine?>(shape: _ => null, line: line => line, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null)
            .ShouldNotBeNull(drawable.ToString());

    public static bool IsLine(Drawable drawable) =>
        drawable.Kind.Match(shape: _ => false, line: _ => true, text: _ => false, image: _ => false, element: _ => false, container: _ => false, group: _ => false);

    [Test]
    public void AStateDiagramIsLaidOut()
    {
        var layout = Layout("stateDiagram-v2\n  [*] --> Idle\n  Idle --> Busy\n  Busy --> [*]\n");
        var drawables = All(layout.Drawables).ToList();

        drawables.Count(IsLine).ShouldBe(3);
        Find(layout, "node=Idle");
        Find(layout, "node=Busy");
    }

    [Test]
    public void InvalidMetadataIsRejectedAsInSvg()
    {
        var diagram = Parse(DiagramMother.Checkout);
        // RDF/XML cannot hold the review graph, and the caller did not allow dropping it.
        var options = SvgRenderOptions.Default.WithDocument(DiagramMother.CheckoutDocument)
            .WithMetadata(CarriageMother.WithReviewGraph(), MetadataEmbedding.RdfXml, NamedGraphHandling.Reject);

        var rendered = SvgRenderer.Render(diagram, options);
        var laid = DiagramExport.Layout(diagram, options);

        rendered.Output.ShouldBeNull();
        laid.Layout.ShouldBeNull();
        laid.Diagnostics.Select(diagnostic => diagnostic.ToString()).ShouldBe(rendered.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Test]
    public void AutoModeUsesTheLightPalette()
    {
        var layout = Layout(DiagramMother.Checkout);
        var light = DiagramThemes.Archify.Light;

        layout.Background.ShouldBe(light.Background);
        ShapeOf(Find(layout, "node=Cart")).Paint.Stroke!.Color.ShouldBe(light.Family(ColorFamily.Backend).Stroke);
    }

    [Test]
    public void TheLayoutsSvgMatchesTheRenderersInTheLightMode()
    {
        var diagram = Parse(DiagramMother.Checkout);

        DiagramExport.Layout(diagram).Layout!.Svg.Render()
            .ShouldBe(SvgRenderer.Render(diagram, SvgRenderOptions.Create(DiagramThemes.Archify, ThemeMode.Light)).Output);
    }

    [Test]
    public void ANamedBoxColourIsResolvedWithItsSourceAndEffectiveOpacity()
    {
        var layout = Layout("sequenceDiagram\n  box Aqua Team\n  participant A\n  end\n  A->>A: hi\n");
        var box = All(layout.Drawables).Select(drawable => drawable.Kind.Match<LayoutShape?>(shape: shape => shape, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null))
            .OfType<LayoutShape>().First();

        box.Paint.Fill!.Color.ShouldBe(Color.FromRgba(0, 255, 255, 0.12m));
        box.Paint.Fill.Source.ShouldBe(ColorSource.Authored);
    }

    [Test]
    public void AnUnknownColourIsReported()
    {
        var result = DiagramExport.Layout(Parse("sequenceDiagram\n  box #zz Payments\n  participant A\n  end\n  A->>A: hi\n"));

        var warning = result.Diagnostics.ShouldHaveSingleItem();
        warning.Code.ShouldBe("layout/unresolved-colour");
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
        warning.Message.ShouldContain("#zz");
        warning.Message.ShouldContain("Payments");
        All(result.Layout!.Drawables).Select(drawable => drawable.Kind.Match<LayoutShape?>(shape: shape => shape, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null))
            .OfType<LayoutShape>().First().Paint.Fill.ShouldBeNull();
    }
}
