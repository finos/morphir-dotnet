// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using Morphir.Knowledge;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class DiagramSelectionTests
{
    private static readonly DocumentId Checkout = DiagramMother.CheckoutDocument;

    [Test]
    public void TheDerivedRevisionHashesTheExactSource()
    {
        var first = DiagramMother.Snapshot(DiagramMother.Checkout);
        var again = DiagramMother.Snapshot(DiagramMother.Checkout);
        var changed = DiagramMother.Snapshot(DiagramMother.CheckoutWithRenamedLedger);

        var expected = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DiagramMother.Checkout)));
        first.Version.Revision.Value.ShouldBe(expected);
        again.Version.Equals(first.Version).ShouldBeTrue();
        changed.Version.Revision.Equals(first.Version.Revision).ShouldBeFalse();
        first.Version.Document.Equals(Checkout).ShouldBeTrue();
    }

    [Test]
    public void ASuppliedRevisionIsKept()
    {
        var version = new DocumentVersion(Checkout, RevisionId.Create("4be1c0de"));
        DiagramSnapshot.TryCreate(version, DiagramMother.Document(DiagramMother.Checkout), out var snapshot, out _).ShouldBeTrue();

        snapshot.Version.Equals(version).ShouldBeTrue();
    }

    [Test]
    public void ElementIrisResolveOnlyToElementsOfTheSnapshot()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var billing = DiagramElementRef.Create("node=Billing");

        var iri = snapshot.ElementIri(billing);
        iri.Value.ShouldBe("https://example.org/diagrams/checkout#node=Billing");
        snapshot.TryResolve(iri, out var resolved).ShouldBeTrue();
        resolved.Equals(billing).ShouldBeTrue();
        snapshot.TryResolve(Iri.Create("https://example.org/diagrams/checkout#node=Legacy"), out _).ShouldBeFalse();
        snapshot.TryResolve(Iri.Create("https://example.org/diagrams/other#node=Billing"), out _).ShouldBeFalse();
        snapshot.Elements.ShouldContain(billing);
    }

    [Test]
    public void ADocumentIriWithAFragmentGivesNoSnapshot()
    {
        var withFragment = DocumentId.Create(Iri.Create("https://example.org/diagrams/checkout#main"));

        DiagramSnapshot.TryFromSource(withFragment, DiagramMother.Document(DiagramMother.Checkout), out _, out var error).ShouldBeFalse();
        error.Match(
            empty: () => "", unknownKind: _ => "", invalidKey: _ => "",
            documentHasFragment: iri => iri.Value, notInDocument: _ => "").ShouldBe("https://example.org/diagrams/checkout#main");
    }

    [Test]
    public void ASelectionIsDeduplicatedAndOrderedByKindThenSource()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        var requested = new[] { "node=Billing", "edge=Cart/Billing/0", "node=Cart", "node=Billing" }.Select(DiagramElementRef.Create);

        DiagramSelection.TryCreate(snapshot, requested, out var selection, out _).ShouldBeTrue();
        selection.Elements.Select(reference => reference.ToString()).ShouldBe(["node=Cart", "node=Billing", "edge=Cart/Billing/0"]);
        selection.Version.Equals(snapshot.Version).ShouldBeTrue();
    }

    [Test]
    public void AnEmptySelectionAndAnUnknownElementAreTypedFailures()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);

        DiagramSelection.TryCreate(snapshot, [], out _, out var empty).ShouldBeFalse();
        empty.Equals(DiagramSelectionError.Empty).ShouldBeTrue();

        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Legacy")], out _, out var unknown).ShouldBeFalse();
        unknown.Match(empty: () => "", unknownElement: reference => reference.ToString(), versionMismatch: () => "").ShouldBe("node=Legacy");
    }

    [Test]
    public void ASelectionResolvesOnlyAgainstItsOwnRevision()
    {
        var first = DiagramMother.Snapshot(DiagramMother.Checkout);
        var second = DiagramMother.Snapshot(DiagramMother.CheckoutWithAudit);
        DiagramSelection.TryCreate(first, [DiagramElementRef.Create("node=Billing")], out var selection, out _).ShouldBeTrue();

        selection.TryRead(first, out var elements, out _).ShouldBeTrue();
        elements.Select(reference => reference.ToString()).ShouldBe(["node=Billing"]);
        selection.TryRead(second, out _, out var mismatch).ShouldBeFalse();
        mismatch.Equals(DiagramSelectionError.VersionMismatch).ShouldBeTrue();
        selection.Version.Equals(first.Version).ShouldBeTrue();
    }

    [Test]
    public void SelectionsWithTheSameElementsAreEqual()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Cart"), DiagramElementRef.Create("node=Billing")], out var first, out _).ShouldBeTrue();
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Billing"), DiagramElementRef.Create("node=Cart")], out var second, out _).ShouldBeTrue();

        first.Equals(second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }
}

public class DiagramAnnotationTests
{
    private static readonly Iri Reviewer = Iri.Create("urn:reviewer:one");
    private static readonly Iri ImplementedBy = Iri.Create("https://example.org/vocab/implementedBy");

    [Test]
    public void ACommentOnAnEdgeCarriesTheEdgeBodyAndProvenance()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("edge=Cart/Billing/0")], out var edge, out _).ShouldBeTrue();
        var created = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

        var comment = new Annotation<DiagramSelection, string>(
            AnnotationId.Create(Iri.Create("urn:review:1")), edge, "Is this call idempotent?", new AnnotationProvenance(Reviewer, created));

        comment.Target.Elements.ShouldHaveSingleItem().ToString().ShouldBe("edge=Cart/Billing/0");
        comment.Body.ShouldBe("Is this call idempotent?");
        comment.Provenance.Author.Equals(Reviewer).ShouldBeTrue();
        comment.Provenance.CreatedAt.ShouldBe(created);
    }

    [Test]
    public void ANodeLinksToACodeSnippetOfARevision()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Billing")], out var billing, out _).ShouldBeTrue();
        var code = TextSnapshot.Create(
            new DocumentVersion(DocumentId.Create(Iri.Create("https://example.org/src/Billing.fs")), RevisionId.Create("4be1")),
            "module Billing\nlet charge amount = amount\n");
        TextSelection.TryCreate(code, [Morphir.Knowledge.SourceSpan.Create(TextOffset.Create(15), TextOffset.Create(41))], out var snippet, out _).ShouldBeTrue();

        var link = new KnowledgeLink<DiagramSelection, TextSelection>(LinkId.Create(Iri.Create("urn:link:billing-impl")), billing, ImplementedBy, snippet);

        link.Source.Elements.ShouldHaveSingleItem().ToString().ShouldBe("node=Billing");
        link.Predicate.Equals(ImplementedBy).ShouldBeTrue();
        link.Target.Version.Document.Iri.Value.ShouldBe("https://example.org/src/Billing.fs");
        link.Target.Version.Revision.Value.ShouldBe("4be1");
        link.Target.TryRead(code, out var passages, out _).ShouldBeTrue();
        passages.ShouldHaveSingleItem().ShouldBe("let charge amount = amount");
    }

    [Test]
    public void ANodeLinksToAnIri()
    {
        var snapshot = DiagramMother.Snapshot(DiagramMother.Checkout);
        DiagramSelection.TryCreate(snapshot, [DiagramElementRef.Create("node=Ledger")], out var ledger, out _).ShouldBeTrue();

        var link = new KnowledgeLink<DiagramSelection, Iri>(
            LinkId.Create(Iri.Create("urn:link:ledger-docs")), ledger, Iri.Create("http://www.w3.org/2000/01/rdf-schema#seeAlso"),
            Iri.Create("https://example.org/docs/ledger"));

        link.Target.Value.ShouldBe("https://example.org/docs/ledger");
        snapshot.ElementIri(link.Source.Elements[0]).Value.ShouldBe("https://example.org/diagrams/checkout#node=Ledger");
    }

    [Test]
    public void AStepLinksToEveryParticipantOfAnotherDiagram()
    {
        var flow = DiagramMother.Snapshot(DiagramMother.Checkout);
        var sequence = DiagramMother.ExchangeSnapshot();
        DiagramSelection.TryCreate(flow, [DiagramElementRef.Create("node=Billing")], out var step, out _).ShouldBeTrue();
        var participants = sequence.Elements.Where(reference => reference.Kind.Equals(DiagramElementKind.Participant));
        DiagramSelection.TryCreate(sequence, participants, out var lifelines, out _).ShouldBeTrue();

        var link = new KnowledgeLink<DiagramSelection, DiagramSelection>(
            LinkId.Create(Iri.Create("urn:link:billing-detail")), step, Iri.Create("https://example.org/vocab/expandsTo"), lifelines);

        link.Source.TryRead(flow, out var source, out _).ShouldBeTrue();
        link.Target.TryRead(sequence, out var target, out _).ShouldBeTrue();
        source.Select(reference => reference.ToString()).ShouldBe(["node=Billing"]);
        target.Select(reference => reference.ToString()).ShouldBe(["participant=Api", "participant=Db"]);
        link.Target.Version.Document.Equals(link.Source.Version.Document).ShouldBeFalse();
    }
}
