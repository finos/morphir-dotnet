// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>A layout reports exactly the features that its drawables use.</summary>
public class LayoutFeatureTests
{
    private static LayoutFeatures Features(string source, SvgRenderOptions? options = null) =>
        DiagramExport.Layout(DiagramLayoutTests.Parse(source), options ?? SvgRenderOptions.Default).Layout.ShouldNotBeNull().Features;

    private const string Lane = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" id="D" targetNamespace="urn:t">
          <bpmn:collaboration id="C"><bpmn:participant id="Pool" name="Sales" processRef="P"/></bpmn:collaboration>
          <bpmn:process id="P"><bpmn:task id="T" name="Check the order before it ships"/></bpmn:process>
          <bpmndi:BPMNDiagram id="DI"><bpmndi:BPMNPlane id="PL" bpmnElement="C">
            <bpmndi:BPMNShape id="S0" bpmnElement="Pool" isHorizontal="true"><dc:Bounds x="10" y="10" width="400" height="160"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S1" bpmnElement="T"><dc:Bounds x="100" y="50" width="100" height="80"/></bpmndi:BPMNShape>
          </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    /// <summary>For each feature, a diagram that uses it.</summary>
    public static IEnumerable<Func<(LayoutFeature Feature, string Source, SvgRenderOptions? Options)>> Uses()
    {
        // A custom theme whose family strokes are half transparent.
        var family = FamilyColors.Create(Color.Create("#eeeeee"), Color.FromRgba(0, 0, 255, 0.5m));
        var light = ThemePalette.Create(Color.Create("#ffffff"), Color.Create("#111111"), Color.Create("#555555"), Color.Create("#ffffff"), Color.Create("#333333"),
            family, family, family, family, family, family, family);
        var translucent = DiagramTheme.Create("translucent", DiagramThemes.Archify.Dark, light);

        yield return () => (LayoutFeature.RoundedRectangles, "flowchart LR\n  A(x)\n", null);
        yield return () => (LayoutFeature.Ellipses, "flowchart LR\n  A((x))\n", null);
        yield return () => (LayoutFeature.Diamonds, "flowchart LR\n  A{x}\n", null);
        yield return () => (LayoutFeature.Cylinders, "flowchart LR\n  A[(x)]\n", null);
        yield return () => (LayoutFeature.Hexagons, "flowchart LR\n  A{{x}}\n", null);
        yield return () => (LayoutFeature.Paths, "flowchart LR\n  A[/x/]\n", null);
        yield return () => (LayoutFeature.Polylines, "flowchart TD\n  A --> B\n  A --> C\n  A --> D\n", null);
        yield return () => (LayoutFeature.ExactRoutes, "flowchart TD\n  A --> B\n  A --> C\n  A --> D\n", SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Rounded));
        yield return () => (LayoutFeature.SmoothCurves, "flowchart TD\n  A --> B\n", SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Curved));
        yield return () => (LayoutFeature.AttachedConnectors, "flowchart LR\n  A --> B\n", null);
        yield return () => (LayoutFeature.LineTargets, "sequenceDiagram\n  A->>B: hi\n", null);
        yield return () => (LayoutFeature.OpenAndCircleHeads, "flowchart LR\n  A --o B\n", null);
        yield return () => (LayoutFeature.MessageHeads, "sequenceDiagram\n  A-xB: lost\n", null);
        yield return () => (LayoutFeature.WrappedText, Lane, null);
        yield return () => (LayoutFeature.RotatedText, Lane, null);
        yield return () => (LayoutFeature.Containers, "flowchart LR\n  subgraph S\n    A\n  end\n", null);
        yield return () => (LayoutFeature.FamilyRoles, "stateDiagram-v2\n  [*] --> A\n", null);
        yield return () => (LayoutFeature.InterleavedPainting, "sequenceDiagram\n  A->>B: hi\n", null);
        yield return () => (LayoutFeature.InterleavedContainers, "flowchart LR\n  subgraph S\n    A --> B\n  end\n", null);
        yield return () => (LayoutFeature.FillOpacity, "flowchart LR\n  A\n", null);
        yield return () => (LayoutFeature.StrokeAndTextOpacity, "flowchart LR\n  A\n", SvgRenderOptions.Create(translucent, ThemeMode.Light));
        yield return () => (LayoutFeature.DashPatterns, "flowchart LR\n  A -.-> B\n", null);
        yield return () => (LayoutFeature.SvgImages, "architecture-beta\n  service db(database)[Orders]\n", null);
    }

    [Test]
    [MethodDataSource(nameof(Uses))]
    public void AFeatureIsReportedWhenADrawableUsesIt(LayoutFeature feature, string source, SvgRenderOptions? options) =>
        Features(source, options).Contains(feature).ShouldBeTrue(Features(source, options).ToString());

    [Test]
    public void APlainNodeUsesOnlyItsTranslucentFill() =>
        Features("flowchart LR\n  A[x]\n").ShouldBe(LayoutFeatures.Of(LayoutFeature.FillOpacity));

    [Test]
    public void AFlowchartReportsWhatItUses()
    {
        var features = Features("flowchart TD\n  subgraph S\n    A --> B{ok?}\n  end\n  B --> C\n  B --> D\n");

        features.Contains(LayoutFeature.Diamonds).ShouldBeTrue();
        features.Contains(LayoutFeature.Containers).ShouldBeTrue();
        features.Contains(LayoutFeature.AttachedConnectors).ShouldBeTrue();
        features.Contains(LayoutFeature.Polylines).ShouldBeTrue();
        features.Contains(LayoutFeature.Cylinders).ShouldBeFalse();
    }

    [Test]
    public void ASequenceDiagramReportsInterleavedPaintingAndDashPatterns()
    {
        var features = Features("sequenceDiagram\n  A->>B: hi\n  B-->>A: ok\n");

        features.Contains(LayoutFeature.InterleavedPainting).ShouldBeTrue();
        features.Contains(LayoutFeature.DashPatterns).ShouldBeTrue();
    }

    [Test]
    public void TheSetIsClosedAndHoldsNoBaselineFeature()
    {
        LayoutFeature.All.Count.ShouldBe(25);
        LayoutFeature.All.Select(feature => feature.WireValue).ShouldBeUnique();
        LayoutFeatures.All.Count.ShouldBe(25);
        LayoutFeatures.None.IsEmpty.ShouldBeTrue();
        LayoutFeatures.Of(LayoutFeature.Pages, LayoutFeature.Links).ToString().ShouldBe("links, pages");
        LayoutFeatures.Of(LayoutFeature.Links).IsSubsetOf(LayoutFeatures.All).ShouldBeTrue();
        LayoutFeatures.All.Except(LayoutFeatures.Of(LayoutFeature.Links)).Contains(LayoutFeature.Links).ShouldBeFalse();
    }
}
