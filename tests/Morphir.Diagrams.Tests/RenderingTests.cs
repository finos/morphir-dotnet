// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>A renderer defined in the test assembly: it records the plan it receives and writes a fixed text.</summary>
public sealed class RecordingRenderer(LayoutFeatures features, RenderMode? mode = null, string extension = ".test") : DiagramRenderer
{
    public override string MediaType => "text/plain";

    public override string FileExtension => extension;

    public override LayoutFeatures NativeFeatures => features;

    public override RenderMode Mode => mode ?? RenderMode.Drawables;

    /// <summary>The plan of the last render.</summary>
    public RenderPlan? Plan { get; private set; }

    /// <summary>Diagnostics the renderer returns.</summary>
    public ImmutableArray<Diagnostic> Returns { get; init; } = [];

    public override async Task<ImmutableArray<Diagnostic>> RenderAsync(RenderPlan plan, Stream output, CancellationToken cancellationToken)
    {
        plan.EnsureFor(this);
        Plan = plan;
        await output.WriteAsync(Encoding.UTF8.GetBytes($"{plan.Pages.Length} pages"), cancellationToken);
        return Returns;
    }

    /// <summary>The first page of the last plan.</summary>
    public DiagramLayout Page => Plan.ShouldNotBeNull().Pages[0];
}

/// <summary>A stream that cannot seek and records whether it was closed.</summary>
public sealed class ForwardOnlyStream : MemoryStream
{
    public bool Closed { get; private set; }

    public override bool CanSeek => false;

    public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();

    public override long Position { get => base.Position; set => throw new NotSupportedException(); }

    protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
}

/// <summary>The scenarios of the diagram-renderers specification: fallbacks, assessments, policies and the contract.</summary>
public class RenderingTests
{
    private static DiagramLayout Layout(string source, SvgRenderOptions? options = null) =>
        DiagramExport.Layout(DiagramLayoutTests.Parse(source), options ?? SvgRenderOptions.Default).Layout.ShouldNotBeNull();

    private static async Task<(RenderResult Result, RecordingRenderer Renderer)> Render(DiagramLayout page, LayoutFeatures features, DegradationPolicy? policy = null)
    {
        var renderer = new RecordingRenderer(features);
        var result = await DiagramExport.RenderAsync([page], renderer, new MemoryStream(), policy ?? DegradationPolicy.AllowDegraded, CancellationToken.None);
        return (result, renderer);
    }

    private static IEnumerable<Drawable> All(DiagramLayout layout) => DiagramLayoutTests.All(layout.Drawables);

    private static ImmutableArray<Degradation> Degradations(Assessment assessment) =>
        assessment.Match(native: () => ImmutableArray<Degradation>.Empty, degraded: degradations => degradations, unsupported: _ => []);

    private static bool IsPath(ShapeKind kind) =>
        kind.Match(rectangle: () => false, roundedRectangle: _ => false, ellipse: () => false, diamond: () => false, cylinder: _ => false, hexagon: _ => false, path: _ => true);

    private static LayoutFeatures AllBut(params LayoutFeature[] missing) =>
        missing.Aggregate(LayoutFeatures.All, (set, feature) => set.Remove(feature));

    private static Drawable Find(DiagramLayout layout, string reference) => DiagramLayoutTests.Find(layout, reference);

    // Shape kinds --------------------------------------------------------------------------------------------------

    [Test]
    public async Task ACylinderFallsBackToAPath()
    {
        var (_, renderer) = await Render(Layout("flowchart LR\n  C[(Ledger)]\n"), LayoutFeatures.Of(LayoutFeature.Paths));
        var shape = DiagramLayoutTests.ShapeOf(Find(renderer.Page, "node=C"));

        IsPath(shape.Kind).ShouldBeTrue();
        shape.Box.ShouldBe(DiagramLayoutTests.ShapeOf(Find(Layout("flowchart LR\n  C[(Ledger)]\n"), "node=C")).Box);
    }

    [Test]
    public async Task ACylinderFallsBackToARectangleInOneDegradation()
    {
        var original = Find(Layout("flowchart LR\n  C[(Ledger)]\n"), "node=C");
        var (result, renderer) = await Render(Layout("flowchart LR\n  C[(Ledger)]\n"), LayoutFeatures.Of(LayoutFeature.SvgImages));
        var lowered = Find(renderer.Page, "node=C");
        var shape = DiagramLayoutTests.ShapeOf(lowered);
        var before = DiagramLayoutTests.ShapeOf(original);

        shape.Kind.ShouldBe(ShapeKind.Rectangle);
        (lowered.Id, lowered.PaintOrder, shape.Box, shape.Text!.Lines[0]).ShouldBe((original.Id, original.PaintOrder, before.Box, "Ledger"));
        shape.Paint.Stroke!.ShouldBe(before.Paint.Stroke);
        LayoutFeature[] kinds = [LayoutFeature.Cylinders, LayoutFeature.Paths, LayoutFeature.SvgImages, LayoutFeature.RoundedRectangles];
        var degradation = Degradations(result.Assessment).Single(item => item.Drawables.Contains(original.Id) && kinds.Contains(item.Feature));
        (degradation.Feature, degradation.Fallback).ShouldBe((LayoutFeature.Cylinders, "rectangle"));
    }

    // Structure, text, style and links -----------------------------------------------------------------------------

    [Test]
    public async Task ASubgraphBecomesAGroup()
    {
        var original = Find(Layout("flowchart LR\n  subgraph checkout\n    A --> B\n  end\n"), "group=checkout");
        var (_, renderer) = await Render(Layout("flowchart LR\n  subgraph checkout\n    A --> B\n  end\n"), LayoutFeatures.None);
        var group = Find(renderer.Page, "group=checkout");
        var children = group.Kind.Match(shape: _ => default, line: _ => default, text: _ => default, image: _ => default, element: _ => default, container: _ => default, group: items => items);

        group.Id.ShouldBe(original.Id);
        children.IsDefault.ShouldBeFalse();
        children.ShouldContain(child => child.Kind.Match(shape: _ => false, line: _ => false, text: text => text.Lines[0] == "checkout", image: _ => false, element: _ => false, container: _ => false, group: _ => false));
        children.Select(child => child.Reference?.ToString()).ShouldContain("node=A");
        children.Select(child => child.Reference?.ToString()).ShouldContain("node=B");
    }

    [Test]
    public async Task ATranslucentFillBecomesOpaque()
    {
        var layout = Layout("flowchart LR\n  A[x]\n");
        var fill = DiagramLayoutTests.ShapeOf(Find(layout, "node=A")).Paint.Fill!;
        var (result, renderer) = await Render(layout, LayoutFeatures.None);

        fill.IsOpaque.ShouldBeFalse();
        DiagramLayoutTests.ShapeOf(Find(renderer.Page, "node=A")).Paint.Fill!.Color.ShouldBe(fill.Color.CompositeOver(layout.Background));
        Degradations(result.Assessment).ShouldContain(item => item.Feature.Equals(LayoutFeature.FillOpacity));
    }

    [Test]
    public async Task ACustomThemesTranslucentStrokeBecomesOpaque()
    {
        var family = FamilyColors.Create(Color.Create("#eeeeee"), Color.FromRgba(0, 0, 255, 0.5m));
        var light = ThemePalette.Create(Color.Create("#ffffff"), Color.Create("#111111"), Color.Create("#555555"), Color.Create("#ffffff"), Color.Create("#333333"),
            family, family, family, family, family, family, family);
        var layout = Layout("flowchart LR\n  A --> B\n", SvgRenderOptions.Create(DiagramTheme.Create("translucent", DiagramThemes.Archify.Dark, light), ThemeMode.Light));
        var (result, renderer) = await Render(layout, LayoutFeatures.Of(LayoutFeature.FillOpacity));

        All(renderer.Page).Select(drawable => drawable.Kind.Match<LayoutPaint?>(shape: shape => shape.Paint, line: line => line.Paint, text: _ => null, image: _ => null, element: _ => null, container: container => container.Paint, group: _ => null))
            .OfType<LayoutPaint>().Where(paint => paint.Stroke is not null).ShouldAllBe(paint => paint.Stroke!.IsOpaque);
        Degradations(result.Assessment).ShouldContain(item => item.Feature.Equals(LayoutFeature.StrokeAndTextOpacity));
    }

    [Test]
    public async Task AHalfHeadReachesTheBaseline()
    {
        var (result, renderer) = await Render(Layout("sequenceDiagram\n  A-\\\\B: half\n"), LayoutFeatures.None);
        var message = Find(renderer.Page, "message=A/B/0");

        DiagramLayoutTests.LineOf(message).Target.Head.ShouldBe(EdgeHead.Arrow);
        Degradations(result.Assessment).ShouldContain(item => item.Feature.Equals(LayoutFeature.MessageHeads) && item.Drawables.Contains(message.Id));
    }

    [Test]
    public async Task ALostMessageKeepsItsCross()
    {
        var (_, renderer) = await Render(Layout("sequenceDiagram\n  A-xB: lost\n"), LayoutFeatures.Of(LayoutFeature.MessageHeads));
        DiagramLayoutTests.LineOf(Find(renderer.Page, "message=A/B/0")).Target.Head.ShouldBe(EdgeHead.Cross);
    }

    [Test]
    public async Task RotatedTextArrivesRotated()
    {
        var layout = Layout(DiagramMother.SalesPoolWithClerkLane);
        var (_, rotated) = await Render(layout, LayoutFeatures.Of(LayoutFeature.RotatedText, LayoutFeature.Containers));
        var (_, upright) = await Render(layout, LayoutFeatures.Of(LayoutFeature.Containers));

        DiagramLayoutTests.ContainerOf(Find(rotated.Page, "group=Lane")).Header!.Rotation.ShouldBe(270);
        DiagramLayoutTests.ContainerOf(Find(upright.Page, "group=Lane")).Header!.Rotation.ShouldBe(0);
    }

    // Bindings and routes ------------------------------------------------------------------------------------------

    [Test]
    public async Task ABindingToALoweredContainerIsRemoved()
    {
        var layout = Layout("flowchart LR\n  A --> B\n  subgraph S\n    C\n  end\n  B --> S\n");
        var original = DiagramLayoutTests.LineOf(Find(layout, "edge=B/S/0"));
        var (result, renderer) = await Render(layout, LayoutFeatures.Of(LayoutFeature.AttachedConnectors, LayoutFeature.Polylines));
        var lowered = Find(renderer.Page, "edge=B/S/0");
        var line = DiagramLayoutTests.LineOf(lowered);

        line.Target.Binding.ShouldBeNull();
        line.Target.Point.ShouldBe(original.Target.Point);
        line.Source.Binding.ShouldNotBeNull();
        Degradations(result.Assessment).ShouldContain(item => item.Feature.Equals(LayoutFeature.Containers) && item.Drawables.Contains(lowered.Id));
    }

    [Test]
    public async Task LifelineBindingsNeedLineTargets()
    {
        var (result, renderer) = await Render(Layout("sequenceDiagram\n  A->>B: hi\n"), LayoutFeatures.Of(LayoutFeature.AttachedConnectors));
        var message = Find(renderer.Page, "message=A/B/0");
        var line = DiagramLayoutTests.LineOf(message);

        (line.Source.Binding, line.Target.Binding).ShouldBe((null, null));
        Degradations(result.Assessment).ShouldContain(item => item.Feature.Equals(LayoutFeature.LineTargets) && item.Drawables.Contains(message.Id));
    }

    private const string Bends = "flowchart TD\n  A --> B\n  A --> C\n  A --> D\n";

    private static (Drawable Drawable, LayoutLine Line) Bent(DiagramLayout layout) =>
        All(layout).Where(DiagramLayoutTests.IsLine).Select(drawable => (drawable, DiagramLayoutTests.LineOf(drawable))).First(item => item.Item2.Waypoints.Length > 2);

    [Test]
    public async Task ACurveBecomesAPolyline()
    {
        var layout = Layout(Bends, SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Curved));
        var (drawable, original) = Bent(layout);
        var (_, renderer) = await Render(layout, LayoutFeatures.Of(LayoutFeature.Polylines));
        var line = DiagramLayoutTests.LineOf(DiagramLayoutTests.All(renderer.Page.Drawables).Single(item => item.Id.Equals(drawable.Id)));

        line.ExactPath.ShouldBeNull();
        line.Waypoints.ShouldBe(original.Waypoints);
    }

    [Test]
    public async Task ExactRoutesKeepACurveExact()
    {
        var layout = Layout(Bends, SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Curved));
        var (drawable, original) = Bent(layout);
        var (_, renderer) = await Render(layout, LayoutFeatures.Of(LayoutFeature.ExactRoutes));

        DiagramLayoutTests.LineOf(DiagramLayoutTests.All(renderer.Page.Drawables).Single(item => item.Id.Equals(drawable.Id))).ExactPath.ShouldBe(original.ExactPath);
    }

    [Test]
    public async Task ASmoothCurveIsNotSplitAndIsNotADegradation()
    {
        var layout = Layout(Bends, SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Curved));
        var (drawable, original) = Bent(layout);
        var (result, renderer) = await Render(layout, LayoutFeatures.Of(LayoutFeature.SmoothCurves));
        var line = DiagramLayoutTests.LineOf(DiagramLayoutTests.All(renderer.Page.Drawables).Single(item => item.Id.Equals(drawable.Id)));

        (line.Route, line.ExactPath).ShouldBe((RouteStyle.Curved, null));
        line.Waypoints.ShouldBe(original.Waypoints);
        Degradations(result.Assessment).ShouldNotContain(item => item.Drawables.Contains(drawable.Id) && (item.Feature.Equals(LayoutFeature.ExactRoutes) || item.Feature.Equals(LayoutFeature.Polylines)));
    }

    [Test]
    public async Task ABoundLineKeepsItsBinding()
    {
        var layout = Layout(Bends);
        var (drawable, _) = Bent(layout);
        var (_, renderer) = await Render(layout, LayoutFeatures.Of(LayoutFeature.AttachedConnectors));
        var line = DiagramLayoutTests.LineOf(DiagramLayoutTests.All(renderer.Page.Drawables).Single(item => item.Id.Equals(drawable.Id)));

        line.Waypoints.Length.ShouldBe(2);
        line.Source.Binding.ShouldNotBeNull();
        line.Target.Binding.ShouldNotBeNull();
    }

    [Test]
    public async Task AnUnboundPolylineIsSplit()
    {
        var layout = Layout(Bends);
        var (drawable, original) = Bent(layout);
        var (_, renderer) = await Render(layout, LayoutFeatures.None);
        var group = DiagramLayoutTests.All(renderer.Page.Drawables).Single(item => item.Id.Equals(drawable.Id));
        var segments = group.Kind.Match(shape: _ => default, line: _ => default, text: _ => default, image: _ => default, element: _ => default, container: _ => default, group: items => items);

        group.Reference.ShouldBe(drawable.Reference);
        segments.Length.ShouldBe(original.Waypoints.Length - 1);
        DiagramLayoutTests.LineOf(segments[^1]).Target.Head.ShouldBe(original.Target.Head);
        DiagramLayoutTests.LineOf(segments[0]).Target.Head.ShouldBe(EdgeHead.None);
    }

    [Test]
    public void AStraightLinkWithoutExactRoutesIsNative()
    {
        var assessment = DiagramExport.Assess([Layout("flowchart LR\n  A --> B\n  B --> C\n")], new RecordingRenderer(AllBut(LayoutFeature.ExactRoutes)));
        Degradations(assessment).ShouldNotContain(item => item.Feature.Equals(LayoutFeature.ExactRoutes));
    }

    /// <summary>Every combination of the four line features, for a curved, an orthogonal, a bridged and a straight line.</summary>
    public static IEnumerable<Func<(int Combination, string Line)>> Combinations() =>
        from combination in Enumerable.Range(0, 16) from line in new[] { "curved", "orthogonal", "bridged", "straight" } select (Func<(int, string)>)(() => (combination, line));

    [Test]
    [MethodDataSource(nameof(Combinations))]
    public async Task EachLineGetsTheFirstRouteThatApplies(int combination, string kind)
    {
        LayoutFeature[] lineFeatures = [LayoutFeature.ExactRoutes, LayoutFeature.SmoothCurves, LayoutFeature.Polylines, LayoutFeature.AttachedConnectors];
        var declared = LayoutFeatures.Of([.. lineFeatures.Where((_, index) => (combination & (1 << index)) != 0)]);
        var layout = kind switch
        {
            "curved" => Layout(Bends, SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Curved)),
            "bridged" => Layout("flowchart TD\n  A --> C\n  B --> C\n  D --> E\n  A --> E\n  F --> A\n  A --> G\n  A --> H\n", SvgRenderOptions.Default.WithBridges(true)),
            "straight" => Layout("flowchart LR\n  A --> B\n"),
            _ => Layout(Bends),
        };
        var (drawable, original) = kind switch
        {
            "bridged" => All(layout).Where(DiagramLayoutTests.IsLine).Select(item => (item, DiagramLayoutTests.LineOf(item))).First(item => item.Item2.ExactPath!.HasArcs),
            "straight" => All(layout).Where(DiagramLayoutTests.IsLine).Select(item => (item, DiagramLayoutTests.LineOf(item))).First(),
            _ => Bent(layout),
        };
        var (result, renderer) = await Render(layout, declared);
        var lowered = DiagramLayoutTests.All(renderer.Page.Drawables).Single(item => item.Id.Equals(drawable.Id));
        var degraded = Degradations(result.Assessment).Where(item => item.Drawables.Contains(drawable.Id)).Select(item => item.Feature).ToList();
        var bound = declared.Contains(LayoutFeature.AttachedConnectors);
        var differs = kind is "curved" or "bridged";

        // A line keeps its first degradation; losing a binding comes before the route.
        var unbound = !bound && (original.Source.Binding is not null || original.Target.Binding is not null);

        if (declared.Contains(LayoutFeature.ExactRoutes))
        {
            DiagramLayoutTests.LineOf(lowered).ExactPath.ShouldBe(original.ExactPath);
            degraded.Any().ShouldBe(unbound);
        }
        else if (declared.Contains(LayoutFeature.SmoothCurves) && original.Route.Equals(RouteStyle.Curved))
        {
            (DiagramLayoutTests.LineOf(lowered).Route, DiagramLayoutTests.LineOf(lowered).ExactPath).ShouldBe((RouteStyle.Curved, null));
            degraded.Any().ShouldBe(unbound);
        }
        else if (declared.Contains(LayoutFeature.Polylines))
        {
            DiagramLayoutTests.LineOf(lowered).Waypoints.ShouldBe(original.Waypoints);
            degraded.Any().ShouldBe(unbound || differs);
            if (!unbound) degraded.Contains(LayoutFeature.ExactRoutes).ShouldBe(differs);
        }
        else if (bound || original.Waypoints.Length <= 2)
        {
            DiagramLayoutTests.LineOf(lowered).Waypoints.Length.ShouldBe(2);
            degraded.Any().ShouldBe(unbound || original.Waypoints.Length > 2 || differs);
        }
        else
        {
            DiagramLayoutTests.IsLine(lowered).ShouldBeFalse();
            degraded.ShouldNotBeEmpty();
        }
    }

    // Paint order --------------------------------------------------------------------------------------------------

    [Test]
    public void AParticipantIsPaintedTogether()
    {
        var layout = Layout("sequenceDiagram\n  A->>B: hi\n");
        var degradations = Degradations(DiagramExport.Assess([layout], new RecordingRenderer(AllBut(LayoutFeature.InterleavedPainting))));

        degradations.ShouldContain(item => item.Feature.Equals(LayoutFeature.InterleavedPainting) && item.References.Contains(DiagramElementRef.Create("participant=A")));
    }

    [Test]
    public void ANativeContainerIsPaintedTogether()
    {
        var layout = Layout("flowchart LR\n  subgraph S\n    A\n  end\n  A --> B\n");
        var degradations = Degradations(DiagramExport.Assess([layout], new RecordingRenderer(AllBut(LayoutFeature.InterleavedContainers))));

        degradations.ShouldContain(item => item.Feature.Equals(LayoutFeature.InterleavedContainers) && item.References.Contains(DiagramElementRef.Create("group=S")));
        degradations.ShouldNotContain(item => item.Feature.Equals(LayoutFeature.InterleavedPainting));
    }

    // Assessment ---------------------------------------------------------------------------------------------------

    [Test]
    public void ABpmnDataObjectLosesItsRole()
    {
        var layout = Layout(DiagramMother.TaskWithDataObject);
        var assessment = DiagramExport.Assess([layout], new RecordingRenderer(AllBut(LayoutFeature.FamilyRoles)));

        Degradations(assessment).ShouldContain(item => item.Feature.Equals(LayoutFeature.FamilyRoles) && item.References.Contains(DiagramElementRef.Create("node=Doc")));
    }

    [Test]
    public void SeveralPagesNeedPages()
    {
        var page = Layout("flowchart LR\n  A\n");
        var assessment = DiagramExport.Assess([page, page], new RecordingRenderer(AllBut(LayoutFeature.Pages)));

        assessment.Match(native: () => "", degraded: _ => "", unsupported: features => string.Join(",", features)).ShouldBe("pages");
        DiagramExport.Assess([page], new RecordingRenderer(LayoutFeatures.All)).ShouldBe(Assessment.Native);
        DiagramExport.Assess([page], new RecordingRenderer(LayoutFeatures.None)).ShouldBe(DiagramExport.Assess([page], new RecordingRenderer(LayoutFeatures.None)));
    }

    // Policies -----------------------------------------------------------------------------------------------------

    [Test]
    public async Task StrictRefusesADegradedRender()
    {
        var output = new MemoryStream();
        var result = await DiagramExport.RenderAsync([Layout("flowchart LR\n  C[(x)]\n")], new RecordingRenderer(LayoutFeatures.None), output, DegradationPolicy.Strict, CancellationToken.None);

        output.Length.ShouldBe(0);
        result.Written.ShouldBeFalse();
        result.Diagnostics.ShouldNotBeEmpty();
        result.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Code == "layout/degraded");
    }

    [Test]
    public async Task UnsupportedPagesWriteNothingUnderAnyPolicy()
    {
        var page = Layout("flowchart LR\n  A\n");

        foreach (var policy in new[] { DegradationPolicy.Strict, DegradationPolicy.AllowDegraded, DegradationPolicy.PictureFallback })
        {
            var output = new MemoryStream();
            var result = await DiagramExport.RenderAsync([page, page], new RecordingRenderer(LayoutFeatures.None), output, policy, CancellationToken.None);

            output.Length.ShouldBe(0);
            result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("layout/unsupported");
        }
    }

    [Test]
    public async Task ADegradationIsAWarning()
    {
        var (result, _) = await Render(Layout("flowchart LR\n  C[(Ledger)]\n"), AllBut(LayoutFeature.Cylinders));

        result.Written.ShouldBeTrue();
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "layout/degraded" && diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.Message.Contains("cylinders") && diagnostic.Message.Contains("node=C"));
    }

    [Test]
    public async Task PictureFallbackEmbedsThePage()
    {
        var layout = Layout("flowchart LR\n  C[(Ledger)]\n");
        var (result, renderer) = await Render(layout, LayoutFeatures.Of(LayoutFeature.SvgImages), DegradationPolicy.PictureFallback);
        var image = renderer.Page.Drawables.ShouldHaveSingleItem().Kind.Match<LayoutImage?>(shape: _ => null, line: _ => null, text: _ => null, image: image => image, element: _ => null, container: _ => null, group: _ => null);

        image.ShouldNotBeNull().SvgText.ShouldBe(layout.Svg.Render());
        image.Box.ShouldBe(layout.Bounds);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "layout/pictured");
    }

    [Test]
    public async Task PictureFallbackWithoutImagesIsAllowDegraded()
    {
        var (result, renderer) = await Render(Layout("flowchart LR\n  C[(Ledger)]\n"), LayoutFeatures.None, DegradationPolicy.PictureFallback);

        Find(renderer.Page, "node=C").ShouldNotBeNull();
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "layout/degraded");
    }

    // The contract -------------------------------------------------------------------------------------------------

    [Test]
    public async Task ARendererFromAnotherAssemblyRendersAndReports()
    {
        var renderer = new RecordingRenderer(LayoutFeatures.All)
        {
            Returns = [Diagnostic.Error("test/failed", "The test renderer failed."), Diagnostic.Warning("test/approximated", "A curve became a polyline.")],
        };
        var output = new MemoryStream();

        var result = await DiagramExport.RenderAsync([Layout("flowchart LR\n  A --> B\n")], renderer, output, DegradationPolicy.AllowDegraded, CancellationToken.None);

        Encoding.UTF8.GetString(output.ToArray()).ShouldBe("1 pages");
        renderer.Plan.ShouldNotBeNull().Mode.ShouldBe(RenderMode.Drawables);
        result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Message)).ShouldBe([
            ("test/failed", DiagnosticSeverity.Error, "The test renderer failed."),
            ("test/approximated", DiagnosticSeverity.Warning, "A curve became a polyline.")]);
    }

    [Test]
    public async Task AForwardOnlyStreamGetsTheSameBytesAndStaysOpen()
    {
        var page = Layout("flowchart LR\n  A --> B\n");
        var seekable = new MemoryStream();
        var forward = new ForwardOnlyStream();

        await DiagramExport.RenderAsync([page], new RecordingRenderer(LayoutFeatures.All), seekable, DegradationPolicy.Strict, CancellationToken.None);
        await DiagramExport.RenderAsync([page], new RecordingRenderer(LayoutFeatures.All), forward, DegradationPolicy.Strict, CancellationToken.None);

        forward.ToArray().ShouldBe(seekable.ToArray());
        forward.Closed.ShouldBeFalse();
        seekable.CanWrite.ShouldBeTrue();
    }

    private sealed class ThrowingRenderer : DiagramRenderer
    {
        public override string MediaType => "text/plain";
        public override string FileExtension => ".fail";
        public override LayoutFeatures NativeFeatures => LayoutFeatures.All;

        public override async Task<ImmutableArray<Diagnostic>> RenderAsync(RenderPlan plan, Stream output, CancellationToken cancellationToken)
        {
            await output.WriteAsync("partial"u8.ToArray(), cancellationToken);
            throw new InvalidDataException("The renderer failed after writing.");
        }
    }

    [Test]
    public async Task ARendererThatThrowsAfterWriting()
    {
        var output = new MemoryStream();

        var thrown = await Should.ThrowAsync<InvalidDataException>(() =>
            DiagramExport.RenderAsync([Layout("flowchart LR\n  A\n")], new ThrowingRenderer(), output, DegradationPolicy.AllowDegraded, CancellationToken.None));

        thrown.Message.ShouldBe("The renderer failed after writing.");
        Encoding.UTF8.GetString(output.ToArray()).ShouldBe("partial");
    }

    [Test]
    public async Task CancellationBeforeTheStartWritesNothing()
    {
        var output = new MemoryStream();
        var renderer = new RecordingRenderer(LayoutFeatures.All);

        var task = DiagramExport.RenderAsync([Layout("flowchart LR\n  A\n")], renderer, output, DegradationPolicy.AllowDegraded, new CancellationToken(canceled: true));

        await Should.ThrowAsync<TaskCanceledException>(() => task);
        task.IsCanceled.ShouldBeTrue();
        output.Length.ShouldBe(0);
        renderer.Plan.ShouldBeNull();
    }

    [Test]
    public async Task APlanMadeForAnotherRendererIsRefused()
    {
        var wide = new RecordingRenderer(LayoutFeatures.All);
        await DiagramExport.RenderAsync([Layout("flowchart LR\n  C[(x)]\n")], wide, new MemoryStream(), DegradationPolicy.AllowDegraded, CancellationToken.None);
        var output = new MemoryStream();

        await Should.ThrowAsync<ArgumentException>(() => new RecordingRenderer(LayoutFeatures.None).RenderAsync(wide.Plan!, output, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => new RecordingRenderer(LayoutFeatures.None, RenderMode.OriginalSvg).RenderAsync(wide.Plan!, output, CancellationToken.None));
        output.Length.ShouldBe(0);
    }

    [Test]
    public async Task AnOriginalSvgRendererGetsTheSvgUnchanged()
    {
        var layout = Layout("flowchart LR\n  subgraph S\n    C[(x)]\n  end\n");
        var renderer = new RecordingRenderer(LayoutFeatures.None, RenderMode.OriginalSvg);

        var result = await DiagramExport.RenderAsync([layout], renderer, new MemoryStream(), DegradationPolicy.Strict, CancellationToken.None);

        result.Assessment.ShouldBe(Assessment.Native);
        renderer.Page.ShouldBe(layout);
        renderer.Plan!.Features.ShouldBe(LayoutFeatures.None);
    }

    private sealed class ShapeCounter : LayoutVisitor
    {
        public int Shapes { get; private set; }

        public override void VisitShape(Drawable drawable, LayoutShape shape) => Shapes++;
    }

    [Test]
    public void DefaultVisitingReachesNestedShapes()
    {
        var counter = new ShapeCounter();
        counter.Visit(Layout("flowchart LR\n  subgraph S\n    A\n    B\n  end\n"));
        counter.Shapes.ShouldBe(2);
    }

    // The registry -------------------------------------------------------------------------------------------------

    [Test]
    public void TheLongestExtensionWins()
    {
        var markdown = new RecordingRenderer(LayoutFeatures.None, extension: ".md");
        var obsidian = new RecordingRenderer(LayoutFeatures.None, extension: ".excalidraw.md");
        var renderers = DiagramRenderers.Empty.Add(markdown).Add(obsidian);

        renderers.TryFind("flow.EXCALIDRAW.md", out var found).ShouldBeTrue();
        found.FileExtension.ShouldBe(".excalidraw.md");
        renderers.TryFind("notes.md", out var plain).ShouldBeTrue();
        plain.ShouldBeSameAs(markdown);
        renderers.TryFind("flow.drawio", out var missing).ShouldBeFalse();
        missing.ShouldBeNull();
        renderers.Find(null).ShouldBeNull();
    }

    [Test]
    public void AUsersRendererReplacesABuiltInOne()
    {
        var builtIn = new RecordingRenderer(LayoutFeatures.All, extension: ".drawio");
        var own = new RecordingRenderer(LayoutFeatures.None, extension: ".DRAWIO");
        var renderers = DiagramRenderers.Empty.Add(builtIn).Add(own);

        renderers.Find("flow.drawio").ShouldBeSameAs(own);
        renderers.Renderers.Count.ShouldBe(1);
        DiagramRenderers.Empty.Renderers.ShouldBeEmpty();
    }
}
