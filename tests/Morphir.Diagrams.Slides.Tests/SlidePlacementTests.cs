// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Slides.Tests;

/// <summary>Shared helpers for the slide tests.</summary>
public static class SlideMother
{
    /// <summary>Parses any supported source.</summary>
    public static Diagram Parse(string source) =>
        DiagramSource.TryParse(source, out var document, out var errors) ? document.Diagram : throw new InvalidOperationException(string.Join(", ", errors));

    /// <summary>The layout of a source.</summary>
    public static DiagramLayout Layout(string source, SvgRenderOptions? options = null) =>
        DiagramExport.Layout(Parse(source), options ?? SvgRenderOptions.Default).Layout.ShouldNotBeNull();

    /// <summary>A flowchart twice as wide as it is tall, roughly.</summary>
    public const string Wide = "flowchart LR\n  A[Start] --> B[Middle] --> C[End]\n";

    /// <summary>A BPMN process whose DI places a task at x = -40.</summary>
    public const string NegativeBpmn = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" id="D" targetNamespace="urn:t">
          <bpmn:process id="P"><bpmn:task id="T" name="Check"/><bpmn:task id="U" name="Ship"/></bpmn:process>
          <bpmndi:BPMNDiagram id="DI"><bpmndi:BPMNPlane id="PL" bpmnElement="P">
            <bpmndi:BPMNShape id="S1" bpmnElement="T"><dc:Bounds x="-40" y="20" width="100" height="80"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S2" bpmnElement="U"><dc:Bounds x="200" y="20" width="100" height="80"/></bpmndi:BPMNShape>
          </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}

/// <summary>The scenarios of the slide-placement specification.</summary>
public class SlidePlacementTests
{
    [Test]
    public void TheDefaultSlideIsWidescreen()
    {
        SlidePlacement.Widescreen.Size.Width.ToEmu().ShouldBe(12_192_000L);
        SlidePlacement.Widescreen.Size.Height.ToEmu().ShouldBe(6_858_000L);
        SlidePlacement.Standard.Size.Width.ToEmu().ShouldBe(9_144_000L);
        SlidePlacement.Widescreen.Area.ShouldBe(SlideArea.Content);
        Should.Throw<ArgumentException>(() => SlideSize.Create(Distance.Zero, Distance.FromInches(1)));
    }

    [Test]
    public void TheContentAreaLeavesATwelfthOnEachSide()
    {
        var fit = SlidePlacement.Widescreen.Fit(SlideMother.Layout(SlideMother.Wide), SlideGrid.Emu);

        (fit.Area.X.ToEmu(), fit.Area.Y.ToEmu(), fit.Area.Width.ToEmu(), fit.Area.Height.ToEmu()).ShouldBe((1_016_000L, 571_500L, 10_160_000L, 5_715_000L));
    }

    [Test]
    public void AnAreaOutsideTheSlideIsRefused()
    {
        Should.Throw<ArgumentException>(() => SlidePlacement.Widescreen.WithArea(SlideArea.FromDistances(Distance.FromInches(10), Distance.Zero, Distance.FromInches(4), Distance.FromInches(1))));
        Should.Throw<ArgumentException>(() => SlideArea.FromFractions(Fraction.Create(0.5), Fraction.Zero, Fraction.Create(0.6), Fraction.Half));
    }

    [Test]
    public void AnAreaEmptyAfterRoundingIsRefused()
    {
        var placement = SlidePlacement.Widescreen.WithArea(SlideArea.FromDistances(Distance.FromEmu(10), Distance.FromEmu(10), Distance.FromQuarterEmu(30), Distance.FromEmu(100)));

        Should.Throw<ArgumentException>(() => placement.Fit(SlideMother.Layout(SlideMother.Wide), SlideGrid.Micrometre));
        placement.Fit(SlideMother.Layout(SlideMother.Wide), SlideGrid.Emu).Area.Width.ToEmu().ShouldBe(7L);
    }

    [Test]
    public void NegativeBpmnCoordinatesAreFitted()
    {
        var layout = SlideMother.Layout(SlideMother.NegativeBpmn);
        var fit = SlidePlacement.Widescreen.Fit(layout, SlideGrid.Emu);
        var task = DiagramLayoutFind(layout, "node=T");

        fit.Extent.X.ShouldBe(Distance.FromPixels(-40));
        fit.X(task.X).CompareTo(fit.Area.X).ShouldBeGreaterThanOrEqualTo(0);
        fit.X(task.Right).CompareTo(fit.Area.Right).ShouldBeLessThanOrEqualTo(0);
    }

    private static LayoutBox DiagramLayoutFind(DiagramLayout layout, string reference)
    {
        IEnumerable<Drawable> All(IEnumerable<Drawable> drawables) => drawables.SelectMany(drawable => All(drawable.Kind.Children).Prepend(drawable));
        return All(layout.Drawables).Where(drawable => drawable.Reference?.ToString() == reference)
            .Select(drawable => drawable.Kind.Match<LayoutBox?>(shape: shape => shape.Box, line: _ => null, text: _ => null, image: _ => null,
                element: children => children.Select(child => child.Kind.Match<LayoutBox?>(shape: shape => shape.Box, line: _ => null, text: _ => null, image: _ => null, element: _ => null, container: _ => null, group: _ => null)).FirstOrDefault(box => box is not null),
                container: container => container.Box, group: _ => null))
            .First(box => box is not null)!.Value;
    }

    [Test]
    public void TheExtentHoldsTheBounds()
    {
        // A layout always has its page bounds, so its extent is never smaller than them.
        var layout = SlideMother.Layout("flowchart LR\n  A\n");
        SlidePlacement.Extent(layout).ShouldBe(layout.Bounds);
    }

    [Test]
    public void AWideDiagramFillsTheWidth()
    {
        var square = SlideArea.FromDistances(Distance.FromInches(1), Distance.FromInches(1), Distance.FromInches(5), Distance.FromInches(5));
        var layout = SlideMother.Layout(SlideMother.Wide);
        var fit = SlidePlacement.Widescreen.WithArea(square).Fit(layout, SlideGrid.Emu);
        var top = fit.Y(fit.Extent.Y) - fit.Area.Y;
        var bottom = fit.Area.Bottom - fit.Y(fit.Extent.Bottom);

        (fit.X(fit.Extent.Right) - fit.X(fit.Extent.X)).ShouldBe(fit.Area.Width);
        Math.Abs((top - bottom).ToEmu()).ShouldBeLessThanOrEqualTo(1L);
    }

    [Test]
    public void AMaximumScaleCapsTheGrowth()
    {
        var layout = SlideMother.Layout("flowchart LR\n  A\n");
        var fit = SlidePlacement.Widescreen.WithMaximumScale(Scale.One).Fit(layout, SlideGrid.Emu);

        fit.Factor.ShouldBe(1.0);
        (fit.X(fit.Extent.Right) - fit.X(fit.Extent.X)).ShouldBe(fit.Extent.Width);
        Math.Abs(((fit.X(fit.Extent.X) - fit.Area.X) - (fit.Area.Right - fit.X(fit.Extent.Right))).ToEmu()).ShouldBeLessThanOrEqualTo(1L);
    }

    [Test]
    public void LengthsScaleByTheFactor()
    {
        var layout = SlideMother.Layout("flowchart LR\n  A\n");
        var fit = SlidePlacement.Widescreen.WithMaximumScale(Scale.Create(2)).Fit(layout, SlideGrid.Emu);

        fit.Factor.ShouldBe(2.0);
        fit.Length(Distance.FromPixels(13)).ShouldBe(Distance.FromPixels(26));
    }

    /// <summary>Corpus layouts and fractional 4:3 areas, on each grid.</summary>
    public static IEnumerable<Func<SlideGrid>> Grids() => [() => SlideGrid.Emu, () => SlideGrid.Micrometre];

    [Test]
    [MethodDataSource(nameof(Grids))]
    public void RoundingNeverLeavesTheArea(SlideGrid grid)
    {
        var step = grid.Step.QuarterEmu;
        var areas = new[] { (1.0 / 7, 1.0 / 9, 5.0 / 7, 4.0 / 9), (0.0, 0.0, 1.0, 1.0), (1.0 / 3, 1.0 / 3, 1.0 / 3, 1.0 / 3), (0.1, 0.2, 0.7, 0.3) };
        var failures = new List<string>();

        foreach (var source in DiagramCorpus.Sources().Where((_, index) => index % 7 == 0))
        {
            if (!DiagramSource.TryParse(source.Source, out var document, out _) || DiagramExport.Layout(document.Diagram).Layout is not { } layout) continue;

            foreach (var (left, top, width, height) in areas)
            {
                var placement = SlidePlacement.Standard.WithArea(SlideArea.FromFractions(Fraction.Create(left), Fraction.Create(top), Fraction.Create(width), Fraction.Create(height)));
                var fit = placement.Fit(layout, grid);
                var edges = new[] { fit.X(fit.Extent.X), fit.X(fit.Extent.Right), fit.Y(fit.Extent.Y), fit.Y(fit.Extent.Bottom) };

                if (edges.Any(edge => edge.QuarterEmu % step != 0)) failures.Add($"{source} off the grid");
                if (edges[0].CompareTo(fit.Area.X) < 0 || edges[1].CompareTo(fit.Area.Right) > 0 || edges[2].CompareTo(fit.Area.Y) < 0 || edges[3].CompareTo(fit.Area.Bottom) > 0)
                    failures.Add($"{source} outside {fit.Area}");
            }
        }

        failures.ShouldBeEmpty(string.Join("\n", failures.Take(5)));
    }
}
