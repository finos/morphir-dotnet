// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Morphir.Bpmn;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>BPMN process and collaboration views drawn with BPMN notation.</summary>
public class BpmnDiagramTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private const string Head = """<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" id="D" targetNamespace="urn:t">""";

    private static string Corpus(string path)
    {
        using var document = JsonDocument.Parse(FixtureCatalog.ReadText(FixtureCatalog.DiagramsMbt.Bpmn));
        return document.RootElement.GetProperty("files").EnumerateArray()
            .Single(file => file.GetProperty("path").GetString()!.EndsWith(path, StringComparison.Ordinal))
            .GetProperty("text").GetString()!;
    }

    private static BpmnDocument Document(string body)
    {
        BpmnDocument.TryRead(Head + body + "</bpmn:definitions>", out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return document;
    }

    private static XDocument Render(BpmnDiagram diagram) => XDocument.Parse(SvgRenderer.Render(Diagram.NewBpmn(diagram)).Output!);

    private static BpmnDiagram Create(BpmnDocument document, BpmnGeometry geometry, string? view = null)
    {
        BpmnDiagram.TryCreate(document, view, geometry, out var diagram, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return diagram;
    }

    private static XElement Element(XDocument svg, string reference) =>
        svg.Descendants().Single(element => (string?)element.Attribute("data-ref") == reference);

    [Test]
    public void TwoProcessesAndNoCollaborationAreAnAmbiguityThatRendersNothing()
    {
        var rendered = DiagramSource.RenderSvg(Head + """<bpmn:process id="A"/><bpmn:process id="B"/></bpmn:definitions>""");

        rendered.Output.ShouldBeNull();
        rendered.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("bpmn/ambiguous-view");
        Create(Document("""<bpmn:process id="A"><bpmn:task id="T"/></bpmn:process><bpmn:process id="B"/>"""), BpmnGeometry.Automatic, "A").View.Id.ShouldBe("A");
    }

    [Test]
    public void AChoreographyIsAnUnsupportedView()
    {
        BpmnDiagram.TryCreate(BpmnDocument.Read(Corpus("traversal-corpus/fixtures/choreography.bpmn")).ResultValue, null, BpmnGeometry.Automatic, out _, out var diagnostics)
            .ShouldBeFalse();

        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("bpmn/unsupported-view");
    }

    [Test]
    public void MissingGeometryFailsWhenRequiredAndFallsBackWholeWhenPreferred()
    {
        var document = Document("""<bpmn:process id="P"><bpmn:task id="T"/></bpmn:process>""");

        BpmnDiagram.TryCreate(document, null, BpmnGeometry.RequireSupplied, out _, out var errors).ShouldBeFalse();
        errors.ShouldHaveSingleItem().Code.ShouldBe("bpmn/missing-geometry");
        var preferred = Create(document, BpmnGeometry.PreferSupplied);
        preferred.UsesSuppliedGeometry.ShouldBeFalse();
        preferred.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("bpmn/automatic-geometry");
    }

    [Test]
    public void CompleteGeometryIsKeptAsGiven()
    {
        var document = Document("""
            <bpmn:process id="P"><bpmn:task id="T" name="Work"/><bpmn:endEvent id="E"/><bpmn:sequenceFlow id="F" sourceRef="T" targetRef="E"/></bpmn:process>
            <bpmndi:BPMNDiagram id="Dia"><bpmndi:BPMNPlane id="Pl" bpmnElement="P">
              <bpmndi:BPMNShape id="T_di" bpmnElement="T"><dc:Bounds x="10.5" y="20" width="100" height="80"/></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="E_di" bpmnElement="E"><dc:Bounds x="200" y="42" width="36" height="36"/></bpmndi:BPMNShape>
              <bpmndi:BPMNEdge id="F_di" bpmnElement="F"><di:waypoint x="110.5" y="60"/><di:waypoint x="200" y="60"/></bpmndi:BPMNEdge>
            </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
            """);
        var diagram = Create(document, BpmnGeometry.RequireSupplied);
        var svg = Render(diagram);

        diagram.UsesSuppliedGeometry.ShouldBeTrue();
        var box = Element(svg, "node=T").Element(Svg + "rect")!;
        (box.Attribute("x")!.Value, box.Attribute("y")!.Value, box.Attribute("width")!.Value).ShouldBe(("10.5", "20", "100"));
        Element(svg, "edge=T/E/0").Elements(Svg + "path").First().Attribute("d")!.Value.ShouldBe("M110.5,60 L200,60");
    }

    [Test]
    public void ANonInterruptingTimerBoundaryEventIsADashedDoubleCircleOnTheTaskBorder()
    {
        var svg = Render(Create(Document("""
            <bpmn:process id="P"><bpmn:task id="T"/><bpmn:boundaryEvent id="B" attachedToRef="T" cancelActivity="false"><bpmn:timerEventDefinition/></bpmn:boundaryEvent></bpmn:process>
            """), BpmnGeometry.Automatic));

        var circles = Element(svg, "node=B").Elements(Svg + "circle").ToList();
        circles.Count.ShouldBe(3);
        circles.Take(2).ShouldAllBe(circle => (string?)circle.Attribute("stroke-dasharray") == "4 3");
        var task = Element(svg, "node=T").Element(Svg + "rect")!;
        var bottom = double.Parse(task.Attribute("y")!.Value, CultureInfo.InvariantCulture) + double.Parse(task.Attribute("height")!.Value, CultureInfo.InvariantCulture);
        double.Parse(circles[0].Attribute("cy")!.Value, CultureInfo.InvariantCulture).ShouldBe(bottom);
    }

    [Test]
    public void EachTaskIsDrawnInsideItsLaneAndTheLanesStackInsideThePool()
    {
        var svg = Render(Create(Document("""
            <bpmn:collaboration id="C"><bpmn:participant id="Pool" name="Shop" processRef="P"/></bpmn:collaboration>
            <bpmn:process id="P">
              <bpmn:laneSet><bpmn:lane id="Sales" name="Sales"><bpmn:flowNodeRef>A</bpmn:flowNodeRef></bpmn:lane><bpmn:lane id="Billing" name="Billing"><bpmn:flowNodeRef>B</bpmn:flowNodeRef></bpmn:lane></bpmn:laneSet>
              <bpmn:task id="A"/><bpmn:task id="B"/><bpmn:sequenceFlow id="F" sourceRef="A" targetRef="B"/>
            </bpmn:process>
            """), BpmnGeometry.Automatic));

        (double X, double Y, double Right, double Bottom) Bounds(string reference)
        {
            var rect = Element(svg, reference).Element(Svg + "rect")!;
            double Number(string name) => double.Parse(rect.Attribute(name)!.Value, CultureInfo.InvariantCulture);
            return (Number("x"), Number("y"), Number("x") + Number("width"), Number("y") + Number("height"));
        }

        bool Inside((double X, double Y, double Right, double Bottom) inner, (double X, double Y, double Right, double Bottom) outer) =>
            inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

        Inside(Bounds("node=A"), Bounds("group=Sales")).ShouldBeTrue();
        Inside(Bounds("node=B"), Bounds("group=Billing")).ShouldBeTrue();
        Inside(Bounds("group=Sales"), Bounds("group=Pool")).ShouldBeTrue();
        Inside(Bounds("group=Billing"), Bounds("group=Pool")).ShouldBeTrue();
        Bounds("group=Billing").Y.ShouldBe(Bounds("group=Sales").Bottom);
    }

    [Test]
    [Arguments("corpus/bpmn-model/activities.bpmn")]
    [Arguments("corpus/bpmn-model/events.bpmn")]
    [Arguments("corpus/bpmn-model/collaboration.bpmn")]
    [Arguments("traversal-corpus/fixtures/process.bpmn")]
    public void EveryGraphicalElementOfACorpusViewIsDrawnWithItsReference(string path)
    {
        var document = BpmnDocument.Read(Corpus(path)).ResultValue;
        var diagram = Create(document, BpmnGeometry.Automatic);
        var references = Render(diagram).Descendants().Select(element => (string?)element.Attribute("data-ref")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        string[] drawn = ["task", "userTask", "serviceTask", "sendTask", "receiveTask", "manualTask", "businessRuleTask", "scriptTask", "callActivity", "startEvent", "endEvent",
            "intermediateCatchEvent", "intermediateThrowEvent", "boundaryEvent", "exclusiveGateway", "inclusiveGateway", "parallelGateway", "complexGateway", "eventBasedGateway",
            "dataObjectReference", "dataStoreReference", "textAnnotation"];

        var missing = diagram.View.Kind == "collaboration"
            ? document.Elements.Where(element => element.Kind == "participant").Where(pool => !references.Contains($"group={pool.Id}")).Select(pool => pool.Id)
            : document.Elements.Where(element => drawn.Contains(element.Kind) && element.Id is not null)
                .Where(element => !references.Contains($"node={element.Id}") && !references.Contains($"group={element.Id}")).Select(element => element.Id);

        missing.ShouldBeEmpty();
    }

    [Test]
    public void AProhibitedDeclarationIsRefusedThroughTheSourceEntryPoint() =>
        DiagramSource.Parse("<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY e \"x\">]><d>&e;</d>").ErrorValue.ShouldHaveSingleItem().Code
            .ShouldBe("bpmn/xml-prohibited-declaration");

    [Test]
    public void ThePlayerPlaysSequenceFlows()
    {
        using var data = AnimatedDiagramTests.ReadMotionData(DiagramSource.RenderAnimated(Corpus("traversal-corpus/fixtures/process.bpmn")).Output!);

        data.RootElement.GetProperty("steps").GetArrayLength().ShouldBeGreaterThan(0);
        data.RootElement.GetProperty("connections").EnumerateArray().Select(connection => connection.GetProperty("ref").GetString()).ShouldContain("edge=Start/Check/0");
    }
}
