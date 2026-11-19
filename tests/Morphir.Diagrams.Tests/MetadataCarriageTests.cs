// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Xml.Linq;
using Morphir.Knowledge;
using Morphir.Knowledge.Rdf;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class MetadataCarriageTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public static IEnumerable<Func<MetadataEmbedding>> Embeddings() =>
        [() => MetadataEmbedding.RdfXml, () => MetadataEmbedding.NQuads, () => MetadataEmbedding.NTriples];

    [Test]
    [MethodDataSource(nameof(Embeddings))]
    public void ASidecarDatasetComesBackFromTheSvg(MetadataEmbedding embedding)
    {
        var sidecar = CarriageMother.Sidecar(embedding);

        var svg = CarriageMother.Render(DiagramMother.Checkout, sidecar, embedding);

        DiagramMetadata.TryRead(Encoding.UTF8.GetBytes(svg), out var extracted, out var error).ShouldBeTrue(error?.ToString());
        CarriageMother.Canonical(extracted).ShouldBe(CarriageMother.Canonical(sidecar));
    }

    [Test]
    public void WithoutADatasetTheSvgHasReferencesButNoMetadata()
    {
        var rendered = Mermaid.RenderSvg(DiagramMother.Checkout, SvgRenderOptions.Default.WithDocument(DiagramMother.CheckoutDocument));
        var markup = rendered.Output.ShouldNotBeNull();

        XElement.Parse(markup).Element(Svg + "metadata").ShouldBeNull();
        markup.ShouldNotContain(RdfNamespaces.Rdf);
        markup.ShouldContain("data-ref=\"node=Billing\"");
        DiagramMetadata.TryRead(Encoding.UTF8.GetBytes(markup), out var extracted, out var error).ShouldBeTrue(error?.ToString());
        extracted.Quads.ShouldBeEmpty();
    }

    [Test]
    public void ADatasetWithoutADocumentIsAnError()
    {
        var options = SvgRenderOptions.Default.WithMetadata(CarriageMother.CommentDataset(), MetadataEmbedding.RdfXml, NamedGraphHandling.Reject);

        var rendered = Mermaid.RenderSvg(DiagramMother.Checkout, options);

        rendered.Output.ShouldBeNull();
        rendered.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("metadata/missing-document");
    }

    [Test]
    public void ANamedGraphWithTheDefaultEmbeddingIsAnError()
    {
        var rendered = Mermaid.RenderSvg(DiagramMother.Checkout, CarriageMother.Options(CarriageMother.WithReviewGraph(), MetadataEmbedding.RdfXml));

        rendered.Output.ShouldBeNull();
        var diagnostic = rendered.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("metadata/named-graphs");
        diagnostic.Message.ShouldContain(CarriageMother.ReviewGraph.Value);
    }

    [Test]
    public void NQuadsEmbeddingKeepsTheNamedGraph()
    {
        var dataset = CarriageMother.WithReviewGraph();
        var svg = Mermaid.RenderSvg(DiagramMother.Checkout, CarriageMother.Options(dataset, MetadataEmbedding.NQuads)).Output.ShouldNotBeNull();

        DiagramMetadata.TryRead(Encoding.UTF8.GetBytes(svg), out var extracted, out var error).ShouldBeTrue(error?.ToString());
        extracted.Names.ShouldHaveSingleItem().ShouldBe(GraphName.FromIri(CarriageMother.ReviewGraph));
        CarriageMother.Canonical(extracted).ShouldBe(CarriageMother.Canonical(dataset));
    }

    [Test]
    public void DroppingNamedGraphsIsExplicitAndReported()
    {
        var options = SvgRenderOptions.Default.WithDocument(DiagramMother.CheckoutDocument)
            .WithMetadata(CarriageMother.WithReviewGraph(), MetadataEmbedding.RdfXml, NamedGraphHandling.DropNamedGraphs);

        var rendered = Mermaid.RenderSvg(DiagramMother.Checkout, options);

        var svg = rendered.Output.ShouldNotBeNull();
        var warning = rendered.Diagnostics.ShouldHaveSingleItem();
        warning.Code.ShouldBe("metadata/dropped-graph");
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
        warning.Message.ShouldContain(CarriageMother.ReviewGraph.Value);
        DiagramMetadata.TryRead(Encoding.UTF8.GetBytes(svg), out var extracted, out var error).ShouldBeTrue(error?.ToString());
        extracted.Names.ShouldBeEmpty();
        CarriageMother.Canonical(extracted).ShouldBe(CarriageMother.Canonical(CarriageMother.CommentDataset()));
    }

    [Test]
    public void ACommentOnARemovedNodeWarnsAndIsStillEmbedded()
    {
        var dataset = CarriageMother.CommentDataset("node=Legacy");

        var rendered = Mermaid.RenderSvg(DiagramMother.Checkout, CarriageMother.Options(dataset, MetadataEmbedding.RdfXml));

        var svg = rendered.Output.ShouldNotBeNull();
        var warning = rendered.Diagnostics.ShouldHaveSingleItem();
        warning.Code.ShouldBe("metadata/unknown-element");
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
        warning.Message.ShouldContain(CarriageMother.LegacyNode.Value);
        DiagramMetadata.TryRead(Encoding.UTF8.GetBytes(svg), out var extracted, out var error).ShouldBeTrue(error?.ToString());
        CarriageMother.Canonical(extracted).ShouldBe(CarriageMother.Canonical(dataset));
    }

    [Test]
    [MethodDataSource(nameof(Embeddings))]
    public void ALiteralThatXmlCannotHoldIsAnErrorNotAnException(MetadataEmbedding embedding)
    {
        var rendered = Mermaid.RenderSvg(DiagramMother.Checkout, CarriageMother.Options(CarriageMother.NonCharacterLiteral(), embedding));

        rendered.Output.ShouldBeNull();
        rendered.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("metadata/not-embeddable");
    }

    [Test]
    public void APredicateWithoutAnXmlLocalNameIsAnErrorUnderRdfXml()
    {
        var rendered = Mermaid.RenderSvg(DiagramMother.Checkout, CarriageMother.Options(CarriageMother.PredicateWithoutLocalName(), MetadataEmbedding.RdfXml));

        rendered.Output.ShouldBeNull();
        rendered.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("metadata/not-embeddable");
        Should.Throw<ArgumentException>(() => SvgRenderer.BuildScene(
            DiagramMother.Read(DiagramMother.Checkout), CarriageMother.Options(CarriageMother.PredicateWithoutLocalName(), MetadataEmbedding.RdfXml)));
    }

    [Test]
    public void AnEmptyNamedGraphIsRejectedOrDroppedExplicitly()
    {
        var dataset = CarriageMother.WithEmptyGraph();

        var rejected = Mermaid.RenderSvg(DiagramMother.Checkout, CarriageMother.Options(dataset, MetadataEmbedding.NQuads));
        rejected.Output.ShouldBeNull();
        rejected.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("metadata/not-embeddable");

        var dropped = Mermaid.RenderSvg(DiagramMother.Checkout, SvgRenderOptions.Default.WithDocument(DiagramMother.CheckoutDocument)
            .WithMetadata(dataset, MetadataEmbedding.NQuads, NamedGraphHandling.DropNamedGraphs));
        dropped.Output.ShouldNotBeNull();
        dropped.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("metadata/dropped-graph");
    }

    [Test]
    public void TheSameStatementsInAnotherOrderRenderIdenticalBytes()
    {
        var first = CarriageMother.CommentDataset();
        var reordered = CarriageMother.Read(CarriageMother.CommentNQuadsReordered);

        var options = (RdfDataset dataset) => CarriageMother.Options(dataset, MetadataEmbedding.RdfXml);
        Mermaid.RenderSvg(DiagramMother.Checkout, options(reordered)).Output.ShouldNotBeNull()
            .ShouldBe(Mermaid.RenderSvg(DiagramMother.Checkout, options(first)).Output.ShouldNotBeNull());
        Mermaid.RenderAnimated(DiagramMother.Checkout, options(reordered)).Output.ShouldNotBeNull()
            .ShouldBe(Mermaid.RenderAnimated(DiagramMother.Checkout, options(first)).Output.ShouldNotBeNull());
    }
}

internal static class CarriageMother
{
    /// <summary>A comment on the given element of the checkout, and an owner fact about Ledger, as N-Quads.</summary>
    public static string CommentNQuads(string reference = "edge=Cart/Billing/0") => $"""
        <urn:review:1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://www.w3.org/ns/oa#Annotation> .
        <urn:review:1> <http://www.w3.org/ns/oa#motivatedBy> <http://www.w3.org/ns/oa#commenting> .
        <urn:review:1> <http://www.w3.org/ns/oa#bodyValue> "Is this call idempotent? <script>alert(1)</script>" .
        <urn:review:1> <http://www.w3.org/ns/oa#hasTarget> _:target .
        _:target <http://www.w3.org/ns/oa#hasSource> <https://example.org/diagrams/checkout> .
        _:target <http://www.w3.org/ns/oa#hasSelector> _:selector .
        _:selector <http://www.w3.org/1999/02/22-rdf-syntax-ns#value> "{reference}" .
        <https://example.org/diagrams/checkout#node=Ledger> <https://example.org/vocab/owner> "payments-team" .

        """;

    /// <summary>The same statements as <see cref="CommentNQuads"/>, in another line order and with other blank-node labels.</summary>
    public const string CommentNQuadsReordered = """
        <https://example.org/diagrams/checkout#node=Ledger> <https://example.org/vocab/owner> "payments-team" .
        _:b9 <http://www.w3.org/1999/02/22-rdf-syntax-ns#value> "edge=Cart/Billing/0" .
        <urn:review:1> <http://www.w3.org/ns/oa#hasTarget> _:b4 .
        _:b4 <http://www.w3.org/ns/oa#hasSelector> _:b9 .
        <urn:review:1> <http://www.w3.org/ns/oa#bodyValue> "Is this call idempotent? <script>alert(1)</script>" .
        _:b4 <http://www.w3.org/ns/oa#hasSource> <https://example.org/diagrams/checkout> .
        <urn:review:1> <http://www.w3.org/ns/oa#motivatedBy> <http://www.w3.org/ns/oa#commenting> .
        <urn:review:1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://www.w3.org/ns/oa#Annotation> .

        """;

    /// <summary>The named graph of review discussion.</summary>
    public static readonly Iri ReviewGraph = Iri.Create("https://example.org/graphs/review");

    /// <summary>The element IRI of a node that the checkout no longer has.</summary>
    public static readonly Iri LegacyNode = Iri.Create(DiagramMother.CheckoutDocument.Iri.Value + "#node=Legacy");

    public static RdfDataset Read(string nquads) =>
        NQuads.TryRead(Encoding.UTF8.GetBytes(nquads), out var dataset, out var error)
            ? dataset
            : throw new InvalidOperationException(error.ToString());

    public static RdfDataset CommentDataset(string reference = "edge=Cart/Billing/0") => Read(CommentNQuads(reference));

    /// <summary>A statement whose literal has U+FFFE, which RDF allows and XML 1.0 forbids.</summary>
    public static RdfDataset NonCharacterLiteral() =>
        RdfDataset.Empty.Add(new Quad(
            RdfSubject.FromIri(Iri.Create("https://example.org/diagrams/checkout#node=Ledger")), Iri.Create("https://example.org/vocab/owner"),
            RdfObject.FromLiteral(RdfLiteral.CreateString("pay\uFFFEments")), GraphLocation.DefaultGraph));

    /// <summary>A statement whose predicate ends with a slash, so it has no XML local name.</summary>
    public static RdfDataset PredicateWithoutLocalName() =>
        RdfDataset.Empty.Add(new Quad(
            RdfSubject.FromIri(Iri.Create("https://example.org/diagrams/checkout#node=Ledger")), Iri.Create("https://example.org/vocab/"),
            RdfObject.FromLiteral(RdfLiteral.CreateString("x")), GraphLocation.DefaultGraph));

    /// <summary>The comment dataset with an empty named graph, which N-Quads cannot write.</summary>
    public static RdfDataset WithEmptyGraph() => CommentDataset().WithNamedGraph(GraphName.FromIri(ReviewGraph), RdfGraph.Empty);

    /// <summary>The comment dataset with one statement in a named review graph.</summary>
    public static RdfDataset WithReviewGraph() =>
        CommentDataset().Add(new Quad(
            RdfSubject.FromIri(Iri.Create("urn:review:1")), Iri.Create("http://purl.org/dc/terms/subject"),
            RdfObject.FromLiteral(RdfLiteral.CreateString("idempotency")),
            GraphLocation.Named(GraphName.FromIri(ReviewGraph))));

    /// <summary>The comment dataset as a sidecar file in the format that matches the embedding, read back.</summary>
    public static RdfDataset Sidecar(MetadataEmbedding embedding)
    {
        var dataset = CommentDataset();
        if (embedding.Equals(MetadataEmbedding.RdfXml))
        {
            RdfXml.TryWrite(dataset, NamedGraphHandling.Reject, out var output, out var error).ShouldBeTrue(error?.ToString());
            RdfXml.TryRead(output.Bytes, out var read, out var readError).ShouldBeTrue(readError?.ToString());
            return read;
        }

        if (embedding.Equals(MetadataEmbedding.NTriples))
        {
            NTriples.TryWrite(dataset, NamedGraphHandling.Reject, out var output, out var error).ShouldBeTrue(error?.ToString());
            NTriples.TryRead(output.Bytes, out var read, out var readError).ShouldBeTrue(readError?.ToString());
            return read;
        }

        NQuads.TryWrite(dataset, EmptyGraphHandling.Reject, out var quads, out var quadsError).ShouldBeTrue(quadsError?.ToString());
        NQuads.TryRead(quads.Bytes, out var back, out var backError).ShouldBeTrue(backError?.ToString());
        return back;
    }

    public static SvgRenderOptions Options(RdfDataset dataset, MetadataEmbedding embedding) =>
        SvgRenderOptions.Default.WithDocument(DiagramMother.CheckoutDocument)
            .WithMetadata(dataset, embedding, NamedGraphHandling.Reject);

    public static string Render(string source, RdfDataset dataset, MetadataEmbedding embedding)
    {
        var rendered = Mermaid.RenderSvg(source, Options(dataset, embedding));
        rendered.Diagnostics.ShouldBeEmpty();
        return rendered.Output.ShouldNotBeNull();
    }

    /// <summary>Canonical N-Quads, which are equal exactly when the datasets are isomorphic.</summary>
    public static string Canonical(RdfDataset dataset)
    {
        RdfCanonicalization.TryCanonicalize(dataset, CanonicalizationOptions.Default, out var canonical, out var error).ShouldBeTrue(error?.ToString());
        NQuads.TryWrite(canonical, out var output, out var writeError).ShouldBeTrue(writeError?.ToString());
        return Encoding.UTF8.GetString(output.Bytes);
    }
}
