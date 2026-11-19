// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Morphir.Knowledge;
using Morphir.Knowledge.Rdf;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class DiagramAnnotationMappingTests
{
    private static readonly Iri ImplementedBy = Iri.Create("https://example.org/vocab/implementedBy");

    [Test]
    public void ACommentOnSeveralElementsRoundTrips()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var comment = MappingMother.Comment(snapshot, "edge=Cart/Billing/0", "node=Billing");

        var dataset = MappingMother.Write(DiagramAnnotationSet.Create([comment], []));
        var back = MappingMother.Read(dataset, snapshot);

        back.Comments.ShouldHaveSingleItem().Equals(comment).ShouldBeTrue();
        back.Links.ShouldBeEmpty();
        back.Other.ShouldBeEmpty();
    }

    [Test]
    public void ACommentUsesTheWebAnnotationVocabulary()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var dataset = MappingMother.Write(DiagramAnnotationSet.Create([MappingMother.Comment(snapshot, "edge=Cart/Billing/0")], []));
        var predicates = dataset.Quads.Select(quad => quad.Predicate.Value).ToHashSet();

        predicates.ShouldContain("http://www.w3.org/ns/oa#bodyValue");
        predicates.ShouldContain("http://www.w3.org/ns/oa#hasTarget");
        predicates.ShouldContain("http://www.w3.org/ns/oa#hasSelector");
        predicates.ShouldContain("http://purl.org/dc/terms/creator");
        predicates.ShouldContain(RdfNamespaces.MorphirDiagram + "revision");
        dataset.Quads.ShouldContain(quad => quad.Predicate.Value == "http://www.w3.org/1999/02/22-rdf-syntax-ns#value"
            && quad.Object.Equals(RdfObject.FromLiteral(RdfLiteral.CreateString("edge=Cart/Billing/0"))));
    }

    [Test]
    public void ALinkToASnippetRoundTripsAndHasTheDirectStatement()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var (link, code) = MappingMother.SnippetLink(snapshot);

        var dataset = MappingMother.Write(DiagramAnnotationSet.Create([], [link]), code);
        dataset.Quads.ShouldContain(quad =>
            quad.Subject.Equals(RdfSubject.FromIri(Iri.Create("https://example.org/diagrams/checkout#node=Billing")))
            && quad.Predicate.Equals(ImplementedBy)
            && quad.Object.Equals(RdfObject.FromIri(Iri.Create("https://example.org/src/Billing.fs"))));

        var back = MappingMother.Read(dataset, snapshot, code);
        back.Links.ShouldHaveSingleItem().Equals(link).ShouldBeTrue();
        back.Other.ShouldBeEmpty("the direct statement belongs to the link");
    }

    [Test]
    public void TextPositionsCountCodePointsAfterAnAstralCharacter()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var (link, code) = MappingMother.SnippetLink(snapshot, "// 🌃 night\nlet charge amount = amount\n");

        var dataset = MappingMother.Write(DiagramAnnotationSet.Create([], [link]), code);
        var starts = dataset.Quads.Where(quad => quad.Predicate.Value == "http://www.w3.org/ns/oa#start").ToArray();
        // "// 🌃 night\n" is 12 UTF-16 code units but 11 code points.
        starts.ShouldHaveSingleItem().Object.Match(iri => "", blank => "", literal => literal.LexicalForm).ShouldBe("11");

        MappingMother.Read(dataset, snapshot, code).Links.ShouldHaveSingleItem().Equals(link).ShouldBeTrue();
    }

    [Test]
    public void StatementsWithUnknownPredicatesAreKept()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var owner = new Quad(
            RdfSubject.FromIri(Iri.Create("https://example.org/diagrams/checkout#node=Ledger")),
            Iri.Create("https://example.org/vocab/owner"),
            RdfObject.FromLiteral(RdfLiteral.CreateString("payments-team")),
            GraphLocation.DefaultGraph);
        var dataset = MappingMother.Write(DiagramAnnotationSet.Create([MappingMother.Comment(snapshot, "node=Billing")], [])).Add(owner);

        var back = MappingMother.Read(dataset, snapshot);
        back.Comments.Length.ShouldBe(1);
        back.Other.ShouldHaveSingleItem().Equals(owner).ShouldBeTrue();
    }

    [Test]
    public void TheReadmeExampleDatasetReadsAsOneCommentOneLinkAndOneOtherStatement()
    {
        var example = ReadmeTests.Blocks("n-triples").ShouldHaveSingleItem();
        NTriples.TryRead(Encoding.UTF8.GetBytes(example), out var dataset, out var readError).ShouldBeTrue(readError?.ToString());
        DiagramSnapshot.TryCreate(
                new DocumentVersion(DiagramMother.CheckoutDocument, RevisionId.Create("r1")),
                DiagramMother.Document(DiagramMother.Checkout), out var snapshot, out _)
            .ShouldBeTrue();
        var code = TextSnapshot.Create(
            new DocumentVersion(DocumentId.Create(Iri.Create("https://example.org/src/Billing.fs")), RevisionId.Create("4be1")),
            "module Billing\nlet charge amount = amount\n");

        var annotations = MappingMother.Read(dataset, snapshot, code);

        annotations.Comments.ShouldHaveSingleItem().Body.ShouldBe("Is this call idempotent?");
        annotations.Links.ShouldHaveSingleItem().Predicate.Value.ShouldBe("https://example.org/vocab/implementedBy");
        annotations.Other.ShouldHaveSingleItem().Predicate.Value.ShouldBe("https://example.org/vocab/owner");
    }

    [Test]
    public void ASelectionOfAnUnresolvedRevisionIsATypedError()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var dataset = MappingMother.Write(DiagramAnnotationSet.Create([MappingMother.Comment(snapshot, "node=Billing")], []));

        DiagramAnnotations.TryRead(dataset, _ => null, _ => null, out _, out var error).ShouldBeFalse();
        error.Match(
            unresolvedDocument: version => version.Document.Iri.Value, invalidSelection: _ => "", invalidTextSelection: _ => "",
            malformed: (_, _) => "", duplicateId: _ => "").ShouldBe("https://example.org/diagrams/checkout");
    }

    [Test]
    public void CommentsAndLinksWithOneIdentityAreRejected()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var comment = MappingMother.Comment(snapshot, "node=Billing");

        DiagramAnnotationSet.TryCreate([comment, comment], [], [], out _, out var error).ShouldBeFalse();
        error.Match(_ => "", _ => "", _ => "", (_, _) => "", iri => iri.Value).ShouldBe("urn:review:1");
    }

    [Test]
    public void AnAnnotationThatDoesNotFollowTheMappingStaysInOther()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var foreign = MappingMother.ForeignAnnotation();
        var dataset = MappingMother.Write(DiagramAnnotationSet.Create([MappingMother.Comment(snapshot, "node=Billing")], []));
        foreach (var quad in foreign.Quads)
            dataset = dataset.Add(quad);

        var back = MappingMother.Read(dataset, snapshot);

        back.Comments.Length.ShouldBe(1);
        back.Other.Length.ShouldBe(foreign.Quads.Length);
    }

    [Test]
    public void OtherStatementsAreWrittenBackUnchanged()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var owner = MappingMother.OwnerFact();
        var set = DiagramAnnotationSet.Create([MappingMother.Comment(snapshot, "node=Billing")], [], [owner]);

        var back = MappingMother.Read(MappingMother.Write(set), snapshot);

        back.Other.ShouldHaveSingleItem().Equals(owner).ShouldBeTrue();
    }

    [Test]
    public void ATextResolverThatReturnsOtherTextIsATypedError()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var (link, code) = MappingMother.SnippetLink(snapshot);
        var shorter = TextSnapshot.Create(code.Version, "module");

        DiagramAnnotations.TryWrite(DiagramAnnotationSet.Create([], [link]), _ => shorter, out _, out var error).ShouldBeFalse();
        error.Match(_ => "", _ => "", _ => "invalid text selection", (_, _) => "", _ => "").ShouldBe("invalid text selection");
    }

    [Test]
    public void ARangeThatSplitsASurrogatePairIsATypedError()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var (link, code) = MappingMother.SplitSurrogateLink(snapshot);

        DiagramAnnotations.TryWrite(DiagramAnnotationSet.Create([], [link]), _ => code, out _, out var error).ShouldBeFalse();
        error.Match(_ => "", _ => "", _ => "", (_, reason) => reason, _ => "").ShouldContain("surrogate");
    }

    [Test]
    public void ACreationTimeWithoutAZoneIsUniversalTime()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var dataset = MappingMother.WithCreated(
            MappingMother.Write(DiagramAnnotationSet.Create([MappingMother.Comment(snapshot, "node=Billing")], [])), "2026-11-18T09:00:00");

        MappingMother.Read(dataset, snapshot).Comments.ShouldHaveSingleItem().Provenance.CreatedAt
            .ShouldBe(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
    }
}

internal static class MappingMother
{
    public static Annotation<DiagramSelection, string> Comment(DiagramSnapshot snapshot, params string[] references) =>
        CommentWithBody(snapshot, "Is this call idempotent?\nCheck <retries> & \"timeouts\".", references);

    /// <summary>The reviewer's comment with another body.</summary>
    public static Annotation<DiagramSelection, string> CommentWithBody(DiagramSnapshot snapshot, string body, params string[] references)
    {
        DiagramSelection.TryCreate(snapshot, references.Select(DiagramElementRef.Create), out var selection, out var error)
            .ShouldBeTrue(error?.ToString());
        return new Annotation<DiagramSelection, string>(
            AnnotationId.Create(Iri.Create("urn:review:1")), selection, body,
            new AnnotationProvenance(Iri.Create("urn:reviewer:one"), new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(-6))));
    }

    /// <summary>A link from the Billing node to the charge function of a Billing.fs revision.</summary>
    public static (KnowledgeLink<LinkEndpoint, LinkEndpoint> Link, TextSnapshot Code) SnippetLink(
        DiagramSnapshot snapshot, string text = "module Billing\nlet charge amount = amount\n")
    {
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Billing")], out var billing, out _).ShouldBeTrue();
        var code = TextSnapshot.Create(
            new DocumentVersion(DocumentId.Create(Iri.Create("https://example.org/src/Billing.fs")), RevisionId.Create("4be1")), text);
        var start = text.IndexOf("let", StringComparison.Ordinal);
        TextSelection.TryCreate(code, [Morphir.Knowledge.SourceSpan.Create(TextOffset.Create(start), TextOffset.Create(text.Length - 1))], out var snippet, out _)
            .ShouldBeTrue();
        var link = new KnowledgeLink<LinkEndpoint, LinkEndpoint>(
            LinkId.Create(Iri.Create("urn:link:billing-impl")), LinkEndpoint.FromDiagram(billing),
            Iri.Create("https://example.org/vocab/implementedBy"), LinkEndpoint.FromText(snippet));
        return (link, code);
    }

    /// <summary>An annotation from another tool: its target is a plain IRI and its body is an oa:hasBody node.</summary>
    public static RdfDataset ForeignAnnotation()
    {
        var subject = RdfSubject.FromIri(Iri.Create("urn:other:7"));
        Quad Statement(string predicate, RdfObject value) => new(subject, Iri.Create(predicate), value, GraphLocation.DefaultGraph);
        return RdfDataset.Empty
            .Add(Statement("http://www.w3.org/1999/02/22-rdf-syntax-ns#type", RdfObject.FromIri(Iri.Create("http://www.w3.org/ns/oa#Annotation"))))
            .Add(Statement("http://www.w3.org/ns/oa#motivatedBy", RdfObject.FromIri(Iri.Create("http://www.w3.org/ns/oa#commenting"))))
            .Add(Statement("http://www.w3.org/ns/oa#hasBody", RdfObject.FromIri(Iri.Create("https://example.org/notes/7"))))
            .Add(Statement("http://www.w3.org/ns/oa#hasTarget", RdfObject.FromIri(Iri.Create("https://example.org/diagrams/checkout"))));
    }

    /// <summary>A fact about Ledger in a vocabulary the mapping does not interpret.</summary>
    public static Quad OwnerFact() => new(
        RdfSubject.FromIri(Iri.Create("https://example.org/diagrams/checkout#node=Ledger")), Iri.Create("https://example.org/vocab/owner"),
        RdfObject.FromLiteral(RdfLiteral.CreateString("payments-team")), GraphLocation.DefaultGraph);

    /// <summary>A link to a range that starts between the two halves of a surrogate pair.</summary>
    public static (KnowledgeLink<LinkEndpoint, LinkEndpoint> Link, TextSnapshot Code) SplitSurrogateLink(DiagramSnapshot snapshot)
    {
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Billing")], out var billing, out _).ShouldBeTrue();
        var code = TextSnapshot.Create(
            new DocumentVersion(DocumentId.Create(Iri.Create("https://example.org/src/Night.fs")), RevisionId.Create("1")), "a🌃b");
        TextSelection.TryCreate(code, [Morphir.Knowledge.SourceSpan.Create(TextOffset.Create(2), TextOffset.Create(4))], out var split, out _).ShouldBeTrue();
        return (new KnowledgeLink<LinkEndpoint, LinkEndpoint>(
            LinkId.Create(Iri.Create("urn:link:split")), LinkEndpoint.FromDiagram(billing), Iri.Create("https://example.org/vocab/mentions"),
            LinkEndpoint.FromText(split)), code);
    }

    /// <summary>The dataset with every dcterms:created value replaced by the lexical form.</summary>
    public static RdfDataset WithCreated(RdfDataset dataset, string lexical)
    {
        var created = Iri.Create("http://purl.org/dc/terms/created");
        return dataset.Quads.Aggregate(RdfDataset.Empty, (result, quad) => result.Add(quad.Predicate.Equals(created)
            ? new Quad(quad.Subject, quad.Predicate,
                RdfObject.FromLiteral(RdfLiteral.CreateTyped(lexical, Iri.Create("http://www.w3.org/2001/XMLSchema#dateTime"))), quad.Graph)
            : quad));
    }

    public static RdfDataset Write(DiagramAnnotationSet annotations, TextSnapshot? code = null)
    {
        DiagramAnnotations.TryWrite(annotations, version => code is not null && code.Version.Equals(version) ? code : null, out var dataset, out var error)
            .ShouldBeTrue(error?.ToString());
        return dataset;
    }

    public static DiagramAnnotationSet Read(RdfDataset dataset, DiagramSnapshot snapshot, TextSnapshot? code = null)
    {
        DiagramAnnotations.TryRead(
                dataset,
                version => snapshot.Version.Equals(version) ? snapshot : null,
                version => code is not null && code.Version.Equals(version) ? code : null,
                out var annotations, out var error)
            .ShouldBeTrue(error?.ToString());
        return annotations;
    }
}
