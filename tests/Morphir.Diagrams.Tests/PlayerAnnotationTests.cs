// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Morphir.Knowledge;
using Morphir.Knowledge.Rdf;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class PlayerAnnotationTests
{
    [Test]
    public void ThePlayerDataGroupsCommentsLinksAndPropertiesByElement()
    {
        using var payload = PlayerAnnotationMother.Payload(PlayerAnnotationMother.Annotated());
        var annotations = payload.RootElement.GetProperty("annotations").EnumerateArray().ToArray();

        annotations.Select(entry => entry.GetProperty("ref").GetString())
            .ShouldBe(["node=Billing", "node=Ledger", "edge=Cart/Billing/0"]);

        var comment = PlayerAnnotationMother.Items(annotations[2]).ShouldHaveSingleItem();
        comment.GetProperty("kind").GetString().ShouldBe("comment");
        comment.GetProperty("author").GetString().ShouldBe("urn:reviewer:one");
        comment.GetProperty("time").GetString().ShouldBe("2026-11-18T15:00:00Z");
        comment.GetProperty("body").GetString().ShouldBe(PlayerAnnotationMother.MarkupBody);

        var link = PlayerAnnotationMother.Items(annotations[0]).ShouldHaveSingleItem();
        link.GetProperty("kind").GetString().ShouldBe("link");
        link.GetProperty("predicate").GetString().ShouldBe("https://example.org/vocab/implementedBy");
        link.GetProperty("target").GetString().ShouldBe("https://example.org/src/Billing.fs");
        link.GetProperty("isWeb").GetBoolean().ShouldBeTrue();

        var property = PlayerAnnotationMother.Items(annotations[1]).ShouldHaveSingleItem();
        property.GetProperty("kind").GetString().ShouldBe("property");
        property.GetProperty("predicate").GetString().ShouldBe("https://example.org/vocab/owner");
        property.GetProperty("value").GetString().ShouldBe("payments-team");
        property.GetProperty("isWeb").GetBoolean().ShouldBeFalse();
    }

    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("file:///etc/passwd")]
    [Arguments("urn:isbn:0451450523")]
    public void OnlyHttpAndHttpsTargetsAreWeb(string target)
    {
        using var payload = PlayerAnnotationMother.Payload(PlayerAnnotationMother.SeeAlso(target));
        var item = PlayerAnnotationMother.Items(payload.RootElement.GetProperty("annotations")[0]).ShouldHaveSingleItem();

        item.GetProperty("value").GetString().ShouldBe(target);
        item.GetProperty("isWeb").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public void MarkupInABodyIsEscapedInThePage()
    {
        var html = PlayerAnnotationMother.Html(PlayerAnnotationMother.Annotated());
        using var payload = PlayerAnnotationMother.MotionData(html);

        html.ShouldNotContain(PlayerAnnotationMother.MarkupBody);
        html.ShouldNotContain("<script>alert(1)");
        var comment = PlayerAnnotationMother.Items(payload.RootElement.GetProperty("annotations")[2]).ShouldHaveSingleItem();
        comment.GetProperty("body").GetString().ShouldBe(PlayerAnnotationMother.MarkupBody);
    }

    [Test]
    public void ThePlayerWritesNoMarkupFromDataAndPrintHidesTheNotes()
    {
        var html = PlayerAnnotationMother.Html(PlayerAnnotationMother.Annotated());
        var start = html.IndexOf("@media print", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var print = html[start..];

        foreach (var sink in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "DOMParser" })
            html.ShouldNotContain(sink);
        print[..print.IndexOf("</style>", StringComparison.Ordinal)].ShouldContain(".annotation-layer, .annotation-panel, .explore { display: none; }");
    }

    [Test]
    public void APlayerWithoutMetadataHasNoAnnotations()
    {
        var html = Mermaid.RenderAnimated(DiagramMother.Checkout).Output.ShouldNotBeNull();
        using var payload = PlayerAnnotationMother.MotionData(html);

        payload.RootElement.GetProperty("annotations").GetArrayLength().ShouldBe(0);
    }
}

internal static class PlayerAnnotationMother
{
    public const string MarkupBody = "Is this call idempotent? </script><script>alert(1)</script>";

    /// <summary>A comment on the first Cart → Billing edge, a code link from Billing, and an owner fact about Ledger.</summary>
    public static RdfDataset Annotated()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Billing")], out var billing, out _).ShouldBeTrue();
        var comment = MappingMother.CommentWithBody(snapshot, MarkupBody, "edge=Cart/Billing/0");
        var link = new KnowledgeLink<LinkEndpoint, LinkEndpoint>(
            LinkId.Create(Iri.Create("urn:link:billing-impl")), LinkEndpoint.FromDiagram(billing),
            Iri.Create("https://example.org/vocab/implementedBy"), LinkEndpoint.FromResource(Iri.Create("https://example.org/src/Billing.fs")));
        return MappingMother.Write(DiagramAnnotationSet.Create([comment], [link], [MappingMother.OwnerFact()]));
    }

    /// <summary>An rdfs:seeAlso statement from Ledger to the target IRI.</summary>
    public static RdfDataset SeeAlso(string target) =>
        RdfDataset.Empty.Add(new Quad(
            RdfSubject.FromIri(Iri.Create("https://example.org/diagrams/checkout#node=Ledger")),
            Iri.Create("http://www.w3.org/2000/01/rdf-schema#seeAlso"), RdfObject.FromIri(Iri.Create(target)), GraphLocation.DefaultGraph));

    public static string Html(RdfDataset dataset) =>
        Mermaid.RenderAnimated(
                DiagramMother.Checkout,
                SvgRenderOptions.Default.WithDocument(DiagramMother.CheckoutDocument)
                    .WithMetadata(dataset, MetadataEmbedding.NQuads, NamedGraphHandling.Reject))
            .Output.ShouldNotBeNull();

    public static JsonDocument Payload(RdfDataset dataset) => MotionData(Html(dataset));

    public static JsonDocument MotionData(string html) => AnimatedDiagramTests.ReadMotionData(html);

    public static JsonElement[] Items(JsonElement entry) => entry.GetProperty("items").EnumerateArray().ToArray();
}
