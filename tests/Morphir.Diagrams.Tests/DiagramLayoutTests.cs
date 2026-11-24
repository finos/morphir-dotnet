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


    [Test]
    public void AParticipantIsACompositeElementThatOwnsItsLifeline()
    {
        var layout = Layout(DiagramMother.ExchangeWithSelfAndCreation);
        var children = ElementOf(Find(layout, "participant=A"));

        children.Length.ShouldBe(3);
        children.Count(IsLine).ShouldBe(1);
        children.Where(child => !IsLine(child)).Select(child => ShapeOf(child).Text!.Lines[0]).ShouldBe(["A", "A"]);
        // The lifeline paints before the messages and the head and foot after them.
        var lifeline = children.Single(IsLine);
        lifeline.PaintOrder.ShouldBeLessThan(Find(layout, "message=A/B/0").PaintOrder);
        children.Where(child => !IsLine(child)).ShouldAllBe(child => child.PaintOrder > Find(layout, "message=A/B/0").PaintOrder);
    }

    [Test]
    public void AMessageAnchorsAlongTheLifelinesAtItsRow()
    {
        var layout = Layout(DiagramMother.ExchangeWithSelfAndCreation);
        var message = LineOf(Find(layout, "message=A/B/0"));
        var lifeline = ElementOf(Find(layout, "participant=B")).Single(IsLine);
        var line = LineOf(lifeline);
        var expected = (message.Target.Point.Y - line.Source.Point.Y) / (line.Target.Point.Y - line.Source.Point.Y);

        message.Target.Binding!.Target.ShouldBe(lifeline.Id);
        message.Target.Binding.Anchor.Match(across: _ => -1.0, along: fraction => fraction.Value).ShouldBe(expected, 1e-9);
    }

    [Test]
    public void ASelfMessageBindsOneLifelineTwiceAtDifferentAnchors()
    {
        var layout = Layout(DiagramMother.ExchangeWithSelfAndCreation);
        var message = LineOf(Find(layout, "message=A/A/0"));

        message.Source.Binding!.Target.ShouldBe(message.Target.Binding!.Target);
        message.Source.Binding.Anchor.ShouldNotBe(message.Target.Binding.Anchor);
    }

    [Test]
    public void ACreationMessageBindsTheNewParticipantsHead()
    {
        var layout = Layout(DiagramMother.ExchangeWithSelfAndCreation);
        var message = LineOf(Find(layout, "message=A/C/0"));
        var head = ElementOf(Find(layout, "participant=C")).First(child => !IsLine(child));

        message.Target.Binding!.Target.ShouldBe(head.Id);
        // The head faces the sender on its left side.
        message.Target.Binding.Anchor.Match(across: point => point.X.Value, along: _ => -1.0).ShouldBe(0.0);
    }

    [Test]
    public void ADashedReplyKeepsItsDash()
    {
        var reply = LineOf(Find(Layout(DiagramMother.ExchangeWithSelfAndCreation), "message=B/A/0"));

        reply.Paint.Dash.ShouldNotBeEmpty();
        reply.Paint.StrokeWidth.ShouldBe(Distance.FromPixels(1.4));
    }

    [Test]
    public void ActivationsAndActorsHaveFamilyRoles()
    {
        var layout = Layout("sequenceDiagram\n  actor U\n  participant S\n  U->>S: go\n  activate S\n  S-->>U: done\n  deactivate S\n");
        var roles = All(layout.Drawables).Select(drawable => drawable.Kind.Match<FamilyRole?>(shape: shape => shape.Role, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null))
            .OfType<FamilyRole>().ToList();

        roles.ShouldContain(FamilyRole.Activation);
        roles.ShouldContain(FamilyRole.Actor);
    }

    [Test]
    public void ARailroadTrackHasNoReference()
    {
        var layout = Layout("@startebnf\nrule = \"a\", \"b\";\n@endebnf\n");
        var lines = All(layout.Drawables).Where(IsLine).ToList();

        lines.ShouldNotBeEmpty();
        lines.ShouldAllBe(line => line.Reference == null);
    }

    [Test]
    public void NestedDataTreeReferencesAreCountedOnce()
    {
        var layout = Layout("@startjson\n{\"a\": 1, \"b\": {\"c\": 2}}\n@endjson\n");
        var row = Find(layout, "node=$%2Fa");
        var cells = ElementOf(row);

        cells.Length.ShouldBe(2);
        cells.ShouldAllBe(cell => cell.Reference == null);
        cells.Select(cell => ShapeOf(cell).Text!.Lines[0]).ShouldBe(["a", "1"]);
        // The object b is drawn as a row of its parent and as its own table: one drawable for each.
        All(layout.Drawables).Count(drawable => drawable.Reference?.ToString() == "node=$%2Fb").ShouldBe(2);
    }


    [Test]
    public void FractionalBpmnCoordinatesAgree()
    {
        var task = ShapeOf(Find(Layout(DiagramMother.SalesPoolWithGatewayAndUserTask), "node=T"));

        task.Box.X.ShouldBe(Distance.FromPixels(100.02));
        task.Box.X.QuarterEmu.ShouldBe(3_810_762L);
        task.Box.Y.ShouldBe(Distance.FromPixels(70.5));
        task.Role.ShouldBe(FamilyRole.Task);
    }

    [Test]
    public void PoolsAndLanesAreContainersWithRotatedHeaders()
    {
        var layout = Layout(DiagramMother.SalesPoolWithGatewayAndUserTask);
        var pool = ContainerOf(Find(layout, "group=Pool"));
        var lane = ContainerOf(Find(layout, "group=Lane"));

        pool.Children.Select(child => child.Reference?.ToString()).ShouldContain("group=Lane");
        lane.Children.Select(child => child.Reference?.ToString()).ShouldContain("node=T");
        lane.Header!.Rotation.ShouldBe(270);
        lane.Header.Lines.ShouldBe(["Clerk"]);
        lane.Header.Origin.X.ShouldBe(Distance.FromPixels(59));
    }

    [Test]
    public void ABpmnGatewayIsADiamondWithItsRole()
    {
        var gateway = ElementOf(Find(Layout(DiagramMother.SalesPoolWithGatewayAndUserTask), "node=G")).Select(ShapeOf).First();

        gateway.Kind.ShouldBe(ShapeKind.Diamond);
        gateway.Role.ShouldBe(FamilyRole.Gateway);
    }

    [Test]
    public void ABpmnUserTaskIconKeepsItsRoundJoins()
    {
        var shapes = ElementOf(Find(Layout(DiagramMother.SalesPoolWithGatewayAndUserTask), "node=U"))
            .Select(child => child.Kind.Match<LayoutShape?>(shape: shape => shape, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null))
            .OfType<LayoutShape>().ToList();

        shapes.Skip(1).ShouldContain(shape => shape.Paint.Join.Equals(LineJoin.Round));
    }

    [Test]
    public void ABpmnWaypointOutsideItsTargetKeepsItsPoint()
    {
        var layout = Layout(DiagramMother.SalesPoolWithGatewayAndUserTask);
        var flow = LineOf(Find(layout, "edge=G/U/0"));

        flow.Target.Point.X.ShouldBe(Distance.FromPixels(348));
        flow.Target.Binding!.Target.ShouldBe(ElementOf(Find(layout, "node=U"))[0].Id);
        flow.Target.Binding.Anchor.Match(across: point => point.X.Value, along: _ => -1.0).ShouldBe(0.0);
    }

    private static DiagramLayout CheckoutWith(RdfDataset dataset) =>
        DiagramExport.Layout(Parse(DiagramMother.Checkout), SvgRenderOptions.Default.WithDocument(DiagramMother.CheckoutDocument)
            .WithMetadata(dataset, MetadataEmbedding.NQuads, NamedGraphHandling.Reject)).Layout.ShouldNotBeNull();

    [Test]
    public void LinksFollowTheProjectionsOrder()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Billing")], out var billing, out _).ShouldBeTrue();
        Morphir.Knowledge.KnowledgeLink<LinkEndpoint, LinkEndpoint> To(string id, string target) => new(
            Morphir.Knowledge.LinkId.Create(Morphir.Knowledge.Iri.Create(id)), LinkEndpoint.FromDiagram(billing),
            Morphir.Knowledge.Iri.Create("https://example.org/vocab/implementedBy"), LinkEndpoint.FromResource(Morphir.Knowledge.Iri.Create(target)));
        var dataset = MappingMother.Write(DiagramAnnotationSet.Create([], [To("urn:link:b", "urn:isbn:0451450523"), To("urn:link:a", "https://example.org/src/Billing.fs")], []));

        var links = Find(CheckoutWith(dataset), "node=Billing").Links;

        links.Select(link => (link.Target, link.IsWeb)).ShouldBe([("https://example.org/src/Billing.fs", true), ("urn:isbn:0451450523", false)]);
        links.ShouldAllBe(link => link.Predicate == "https://example.org/vocab/implementedBy");
    }

    [Test]
    public void AWebValuedPropertyIsNotALink() =>
        Find(CheckoutWith(PlayerAnnotationMother.SeeAlso("https://example.org/docs/ledger")), "node=Ledger").Links.ShouldBeEmpty();

    [Test]
    public void TwoRunsGiveEqualLayouts()
    {
        foreach (var (_, source) in Morphir.TestSupport.Fixtures.DiagramCorpus.Representatives())
        {
            var first = Layout(source.Source);
            var second = Layout(source.Source);

            first.ShouldBe(second);
            first.GetHashCode().ShouldBe(second.GetHashCode());
        }

        Layout(DiagramMother.Checkout).ShouldNotBe(Layout(DiagramMother.CheckoutWithRenamedLedger));
    }

    [Test]
    public void TextBoxesUseTheThemesTextWidthEstimate()
    {
        var text = ShapeOf(Find(Layout("flowchart LR\n  A[Payments]\n"), "node=A")).Text!;

        // The theme estimates monospace text at 0.62 of the font size per character, rounded up.
        text.Box.Width.ShouldBe(Distance.FromPixels((8 * 13 * 62 + 99) / 100));
        text.Size.ShouldBe(Distance.FromPixels(13));
        text.Align.ShouldBe(TextAlign.Middle);
        (text.Box.X + Distance.FromQuarterEmu(text.Box.Width.QuarterEmu / 2)).ShouldBe(text.Origin.X);
    }
}
