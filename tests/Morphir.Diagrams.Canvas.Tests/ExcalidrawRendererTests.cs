// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Shouldly;

namespace Morphir.Diagrams.Canvas.Tests;

/// <summary>The scenarios of the excalidraw-export specification, for scenes and Obsidian notes.</summary>
public partial class ExcalidrawRendererTests
{
    private static JsonElement Scene(string source, ExcalidrawRenderer? renderer = null) =>
        Parse(CanvasMother.Render(renderer ?? new ExcalidrawRenderer(), CanvasMother.Layout(source)));

    private static JsonElement Parse(byte[] bytes)
    {
        bytes.Take(3).ShouldNotBe(new byte[] { 0xEF, 0xBB, 0xBF });
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    private static JsonElement[] Elements(JsonElement scene) => [.. scene.GetProperty("elements").EnumerateArray()];

    private static JsonElement Referenced(JsonElement scene, string reference, string type) =>
        Elements(scene).First(element => element.GetProperty("type").GetString() == type
            && element.TryGetProperty("customData", out var data) && data.GetProperty("morphirRef").GetString() == reference);

    private static string Id(JsonElement element) => element.GetProperty("id").GetString()!;

    private static string[] Bound(JsonElement element) =>
        element.GetProperty("boundElements").ValueKind == JsonValueKind.Null ? [] : [.. element.GetProperty("boundElements").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];

    [Test]
    public void AFlowchartBecomesAScene()
    {
        var scene = Scene("flowchart LR\n  A[Start] --> B{Approve?}\n");

        scene.GetProperty("type").GetString().ShouldBe("excalidraw");
        scene.GetProperty("version").GetInt32().ShouldBe(2);
        scene.GetProperty("appState").GetProperty("viewBackgroundColor").GetString().ShouldBe(CanvasMother.Layout(CanvasMother.SingleNode).Background.ToCss());
        Elements(scene).Select(element => element.GetProperty("type").GetString()).ShouldBe(["arrow", "rectangle", "text", "diamond", "text"]);
        Elements(scene).ShouldAllBe(element => Regex.IsMatch(Id(element), "^[a-z0-9]{8}$"));
        new ExcalidrawRenderer().MediaType.ShouldBe("application/vnd.excalidraw+json");
        new ExcalidrawRenderer().FileExtension.ShouldBe(".excalidraw");
    }

    [Test]
    public void EqualInputGivesEqualBytes()
    {
        var layout = CanvasMother.Layout(Morphir.TestSupport.Fixtures.DiagramCorpus.Representatives()[0].Diagram.Source);
        CanvasMother.Render(new ExcalidrawRenderer(), layout).ShouldBe(CanvasMother.Render(new ExcalidrawRenderer(), layout));
        CanvasMother.Render(new ObsidianExcalidrawRenderer(), layout).ShouldBe(CanvasMother.Render(new ObsidianExcalidrawRenderer(), layout));
    }

    [Test]
    public void ADatabaseCylinderIsDegraded()
    {
        var degradations = CanvasMother.Degradations(DiagramExport.Assess([CanvasMother.Layout("flowchart LR\n  C[(Ledger)]\n")], new ExcalidrawRenderer()));

        degradations.ShouldContain(degradation => degradation.Feature.Equals(LayoutFeature.Cylinders) && degradation.Fallback == "rectangle"
            && degradation.References.Contains(DiagramElementRef.Create("node=C")));
    }

    [Test]
    public void AnArrowIsBoundToBothShapes()
    {
        var scene = Scene(CanvasMother.OneLink);
        var arrow = Referenced(scene, "edge=A/B/0", "arrow");
        var a = Referenced(scene, "node=A", "rectangle");
        var b = Referenced(scene, "node=B", "rectangle");

        arrow.GetProperty("startBinding").GetProperty("elementId").GetString().ShouldBe(Id(a));
        arrow.GetProperty("endBinding").GetProperty("elementId").GetString().ShouldBe(Id(b));
        Bound(a).ShouldContain(Id(arrow));
        Bound(b).ShouldContain(Id(arrow));
        arrow.GetProperty("endArrowhead").GetString().ShouldBe("triangle");
    }

    [Test]
    public void ALineWithoutHeadsCanStillBeBound()
    {
        var arrow = Referenced(Scene("flowchart LR\n  A --- B\n"), "edge=A/B/0", "arrow");

        arrow.GetProperty("startArrowhead").ValueKind.ShouldBe(JsonValueKind.Null);
        arrow.GetProperty("endArrowhead").ValueKind.ShouldBe(JsonValueKind.Null);
        arrow.GetProperty("startBinding").ValueKind.ShouldBe(JsonValueKind.Object);
        arrow.GetProperty("endBinding").ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Test]
    public void TextInAShapeIsBoundAndAnImagesLabelIsFreeText()
    {
        var scene = Scene("flowchart LR\n  A[Start]\n  B[/Lean/]\n");
        var shape = Referenced(scene, "node=A", "rectangle");
        var label = Elements(scene).Single(element => element.GetProperty("type").GetString() == "text" && element.GetProperty("text").GetString() == "Start");
        var image = Referenced(scene, "node=B", "image");
        var lean = Elements(scene).Single(element => element.GetProperty("type").GetString() == "text" && element.GetProperty("text").GetString() == "Lean");

        label.GetProperty("containerId").GetString().ShouldBe(Id(shape));
        Bound(shape).ShouldContain(Id(label));
        lean.GetProperty("containerId").ValueKind.ShouldBe(JsonValueKind.Null);
        var groups = image.GetProperty("groupIds").EnumerateArray().Select(group => group.GetString()).ToList();
        groups.ShouldNotBeEmpty();
        lean.GetProperty("groupIds").EnumerateArray().Select(group => group.GetString()).ShouldBe(groups);
        scene.GetProperty("files").GetProperty(image.GetProperty("fileId").GetString()!).GetProperty("mimeType").GetString().ShouldBe("image/svg+xml");
    }

    [Test]
    public void AnOpenArrowUsesTheArrowHeadAndACircleTheDot()
    {
        Referenced(Scene(CanvasMother.AsyncMessage), "message=A/B/0", "arrow").GetProperty("endArrowhead").GetString().ShouldBe("arrow");
        Referenced(Scene("flowchart LR\n  A --o B\n"), "edge=A/B/0", "arrow").GetProperty("endArrowhead").GetString().ShouldBe("dot");
    }

    [Test]
    public void ACompositeElementsLinkReachesEachOfItsParts()
    {
        var scene = Parse(CanvasMother.Render(new ExcalidrawRenderer(), CanvasMother.Linked(CanvasMother.LinkedParticipant, "participant=A")));
        var parts = Elements(scene).Where(element => element.TryGetProperty("customData", out var data) && data.GetProperty("morphirRef").GetString() == "participant=A").ToList();

        parts.Count.ShouldBeGreaterThan(1);
        parts.ShouldAllBe(element => element.GetProperty("link").GetString() == "https://example.org");
    }

    [Test]
    public void ANodeKeepsItsReferenceAndElementsArePainted()
    {
        var scene = Scene(CanvasMother.Message);
        var participant = Elements(scene).Where(element => element.TryGetProperty("customData", out var data) && data.GetProperty("morphirRef").GetString() == "participant=A").ToList();
        var message = Array.IndexOf(Elements(scene), Referenced(scene, "message=A/B/0", "arrow"));

        participant.Count.ShouldBeGreaterThanOrEqualTo(3);
        Array.IndexOf(Elements(scene), participant[0]).ShouldBeLessThan(message);
        participant.Select(element => element.GetProperty("groupIds")[0].GetString()).Distinct().ShouldHaveSingleItem();
    }

    [Test]
    public void RoughnessCanBeChosen()
    {
        var scene = Scene(CanvasMother.OneLink, new ExcalidrawRenderer(1));

        Elements(scene).Where(element => element.GetProperty("type").GetString() is "rectangle" or "arrow").ShouldAllBe(element => element.GetProperty("roughness").GetInt32() == 1);
        Elements(Scene(CanvasMother.OneLink)).ShouldAllBe(element => element.GetProperty("roughness").GetInt32() == 0);
        Should.Throw<ArgumentOutOfRangeException>(() => new ExcalidrawRenderer(3));
    }

    [Test]
    public void DashedLinesAreDashedAndTextUsesTheCodeFont()
    {
        var scene = Scene("flowchart LR\n  A -.-> B\n");

        Referenced(scene, "edge=A/B/0", "arrow").GetProperty("strokeStyle").GetString().ShouldBe("dashed");
        Elements(scene).Where(element => element.GetProperty("type").GetString() == "text").ShouldAllBe(element => element.GetProperty("fontFamily").GetInt32() == 3);
    }

    // Obsidian notes ------------------------------------------------------------------------------------------------

    private static string Note(params DiagramLayout[] pages) => Encoding.UTF8.GetString(CanvasMother.Render(new ObsidianExcalidrawRenderer(), pages));

    private static string Drawing(string note) => DrawingBlock().Match(note).Groups["json"].Value;

    private static string TextElements(string note) => TextSection().Match(note).Groups["texts"].Value;

    [Test]
    public void TheNoteHoldsTheScene()
    {
        var layout = CanvasMother.Layout("flowchart LR\n  A[Start] --> B[End]\n");
        var note = Note(layout);

        note.ShouldStartWith("---\n\nexcalidraw-plugin: parsed\n");
        Drawing(note).ShouldBe(Encoding.UTF8.GetString(CanvasMother.Render(new ExcalidrawRenderer(), layout)).TrimEnd('\n'));
        var scene = Parse(Encoding.UTF8.GetBytes(Drawing(note)));
        foreach (var text in Elements(scene).Where(element => element.GetProperty("type").GetString() == "text"))
            TextElements(note).ShouldContain($"{text.GetProperty("text").GetString()} ^{Id(text)}\n\n");
        new ObsidianExcalidrawRenderer().FileExtension.ShouldBe(".excalidraw.md");
        new ObsidianExcalidrawRenderer().MediaType.ShouldBe("text/markdown");
    }

    [Test]
    public void MultiLineAndEmptyNotes()
    {
        var multi = Note(CanvasMother.Layout("flowchart LR\n  A[\"one<br>two\"]\n"));
        var empty = Note(CanvasMother.Layout("flowchart LR\n  A(( ))\n"));

        TextElements(multi).ShouldMatch(@"one\ntwo \^[a-z0-9]{8}\n\n");
        TextElements(empty).ShouldBe("");
    }

    [Test]
    public void ALabelIsListedWithItsIdentifier() =>
        TextElements(Note(CanvasMother.Layout(CanvasMother.StartNode))).ShouldMatch(@"^Start \^[a-z0-9]{8}\n\n$");

    [Test]
    public void ALabelThatEndsLikeABlockReferenceIsLeftToTheScene()
    {
        var note = Note(CanvasMother.Layout("flowchart LR\n  A[\"Total ^abcd1234\"]\n"));

        TextElements(note).ShouldBe("");
        Drawing(note).ShouldContain("Total ^abcd1234");
    }

    [Test]
    public void PathsDegradeInObsidianNotes()
    {
        var degradations = CanvasMother.Degradations(DiagramExport.Assess([CanvasMother.Layout("flowchart LR\n  B[/Lean/]\n")], new ObsidianExcalidrawRenderer()));

        degradations.ShouldContain(degradation => degradation.Feature.Equals(LayoutFeature.Paths) && degradation.Fallback == "rectangle");
        new ObsidianExcalidrawRenderer().NativeFeatures.Contains(LayoutFeature.SvgImages).ShouldBeFalse();
    }

    [GeneratedRegex("## Drawing\n```json\n(?<json>.*)\n```\n%%\n$", RegexOptions.Singleline)]
    private static partial Regex DrawingBlock();

    [GeneratedRegex("## Text Elements\n(?<texts>.*?)%%\n", RegexOptions.Singleline)]
    private static partial Regex TextSection();
}
