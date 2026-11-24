// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
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

    public static LayoutContainer ContainerOf(Drawable drawable) =>
        drawable.Kind.Match<LayoutContainer?>(shape: _ => null, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: container => container, group: _ => null)
            .ShouldNotBeNull(drawable.ToString());

    public static ImmutableArray<Drawable> ElementOf(Drawable drawable) =>
        drawable.Kind.Match(shape: _ => default, line: _ => default, text: _ => default, image: _ => default, element: children => children, container: _ => default, group: _ => default);

    [Test]
    public void ASubgraphIsAContainerAndEdgesPaintBeforeItsNodes()
    {
        var layout = Layout("flowchart LR\n  subgraph checkout\n    A[Cart] --> B[Pay]\n  end\n");
        var container = ContainerOf(Find(layout, "group=checkout"));

        string.Join(" ", container.Header!.Lines).ShouldBe("checkout");
        container.Children.Select(child => child.Reference?.ToString()).ShouldBe(["node=A", "node=B"]);
        var line = Find(layout, "edge=A/B/0");
        line.PaintOrder.ShouldBeLessThan(Find(layout, "node=A").PaintOrder);
        line.PaintOrder.ShouldBeLessThan(Find(layout, "node=B").PaintOrder);
    }

    [Test]
    public void NestedSubgraphsNestTheirContainers()
    {
        var layout = Layout(DiagramMother.NestedDecision);
        var outer = ContainerOf(Find(layout, "group=Outer"));

        outer.Children.Select(child => child.Reference?.ToString()).ShouldContain("group=Inner");
        ContainerOf(Find(layout, "group=Inner")).Children.Select(child => child.Reference?.ToString()).ShouldBe(["node=D", "node=E"]);
    }

    [Test]
    public void ALinkNamesItsEndpointsAndALinkToASubgraphBindsTheContainer()
    {
        var layout = Layout("flowchart LR\n  A --> B\n  subgraph S\n    C\n  end\n  B --> S\n");
        var link = LineOf(Find(layout, "edge=A/B/0"));

        link.Source.Binding!.Target.ShouldBe(Find(layout, "node=A").Id);
        link.Target.Binding!.Target.ShouldBe(Find(layout, "node=B").Id);
        link.Target.Head.ShouldBe(EdgeHead.Arrow);
        link.Source.Head.ShouldBe(EdgeHead.None);
        LineOf(Find(layout, "edge=B/S/0")).Target.Binding!.Target.ShouldBe(Find(layout, "group=S").Id);
    }

    [Test]
    public void ADecisionNodeIsADiamondWithItsText()
    {
        var shape = ShapeOf(Find(Layout("flowchart LR\n  B{Approve?}\n"), "node=B"));

        shape.Kind.ShouldBe(ShapeKind.Diamond);
        shape.Text!.Lines.ShouldBe(["Approve?"]);
    }

    [Test]
    public void ACylinderStandsForItsBodyAndItsTopEllipse()
    {
        var layout = Layout("flowchart LR\n  C[(Ledger)]\n");
        var shape = ShapeOf(Find(layout, "node=C"));

        shape.Kind.Match(rectangle: () => false, roundedRectangle: _ => false, ellipse: () => false, diamond: () => false, cylinder: _ => true, hexagon: _ => false, path: _ => false).ShouldBeTrue();
        All(layout.Drawables).Count(drawable => drawable.Kind.Match(shape: candidate => candidate.Kind.Equals(ShapeKind.Ellipse), line: _ => false, text: _ => false, image: _ => false, element: _ => false, container: _ => false, group: _ => false))
            .ShouldBe(0);
        LayoutAgreementTests.FromLayout(layout).Count(item => item.Description.StartsWith("ellipse", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Test]
    public void ASubroutineKeepsItsBarsAsAPathInTheNodesElement()
    {
        var children = ElementOf(Find(Layout("flowchart LR\n  D[[Sub]]\n"), "node=D"));

        ShapeOf(children[0]).Kind.ShouldBe(ShapeKind.Rectangle);
        ShapeOf(children[1]).Kind.Match(rectangle: () => false, roundedRectangle: _ => false, ellipse: () => false, diamond: () => false, cylinder: _ => false, hexagon: _ => false, path: _ => true).ShouldBeTrue();
    }

    [Test]
    public void ABridgeIsKeptInTheExactPathAndNotInTheWaypoints()
    {
        var diagram = Parse("flowchart TD\n  A --> C\n  B --> C\n  D --> E\n  A --> E\n  F --> A\n  A --> G\n  A --> H\n");
        var layout = DiagramExport.Layout(diagram, SvgRenderOptions.Default.WithBridges(true)).Layout!;
        var bridged = All(layout.Drawables).Where(IsLine).Select(LineOf).Where(line => line.ExactPath!.HasArcs).ToList();

        bridged.ShouldNotBeEmpty();
        bridged.ShouldAllBe(line => line.Waypoints.Length >= 2);
        bridged[0].ExactPath!.Commands.Count(command => command.Match(_ => false, _ => false, (_, _, _) => false, (_, _) => false, (_, _, _, _, _, _) => true, () => false)).ShouldBeGreaterThan(0);
    }

    [Test]
    public void AFlowchartIconIsAnImageInTheNodesElement()
    {
        var layout = Layout("architecture-beta\n  service db(database)[Orders]\n");
        var children = ElementOf(Find(layout, "node=db"));
        var image = children.Select(child => child.Kind.Match<LayoutImage?>(shape: _ => null, line: _ => null, text: _ => null, image: image => image, element: _ => null, container: _ => null, group: _ => null)).OfType<LayoutImage>().ShouldHaveSingleItem();

        children.Length.ShouldBe(3);
        image.SvgText.ShouldContain("viewBox=");
        image.SvgText.ShouldContain("width=\"28\"");
        image.SvgText.ShouldNotContain("currentColor");
        image.SvgText.ShouldNotContain("href=\"#");
    }

    [Test]
    public void StateMarkersHaveFamilyRoles()
    {
        var layout = Layout("stateDiagram-v2\n  [*] --> Idle\n  Idle --> [*]\n");
        var roles = All(layout.Drawables).Select(drawable => drawable.Kind.Match<FamilyRole?>(shape: shape => shape.Role, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null))
            .OfType<FamilyRole>().ToList();

        roles.ShouldContain(FamilyRole.Initial);
        roles.ShouldContain(FamilyRole.Final);
    }
}
