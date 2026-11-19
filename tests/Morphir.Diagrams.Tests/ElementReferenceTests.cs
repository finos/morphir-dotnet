// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using Morphir.Knowledge;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class ElementReferenceTests
{
    [Test]
    public void EveryFlowchartElementHasAReferenceInKindThenSourceOrder()
    {
        var references = DiagramElementRef.Enumerate(DiagramMother.Read(DiagramMother.Checkout))
            .Select(reference => reference.ToString());

        references.ShouldBe([
            "node=Cart", "node=Billing", "node=Ledger",
            "group=payments",
            "edge=Cart/Billing/0", "edge=Cart/Billing/1", "edge=Billing/Ledger/0",
            "step=0", "step=1", "step=2",
        ]);
    }

    [Test]
    public void EverySequenceElementHasAReferenceInKindThenSourceOrder()
    {
        var references = DiagramElementRef.Enumerate(DiagramMother.Read(DiagramMother.Exchange))
            .Select(reference => reference.ToString());

        references.ShouldBe([
            "participant=Api", "participant=Db",
            "message=Api/Db/0", "message=Db/Api/0", "message=Api/Db/1",
            "note=0", "note=1",
            "frame=0", "frame=1",
            "step=0", "step=1", "step=2",
        ]);
    }

    [Test]
    public void AnUnrelatedInsertionKeepsReferences()
    {
        var before = DiagramElementRef.Enumerate(DiagramMother.Read(DiagramMother.Checkout));
        var after = DiagramElementRef.Enumerate(DiagramMother.Read(DiagramMother.CheckoutWithAudit));

        foreach (var kept in new[] { "node=Billing", "edge=Cart/Billing/0", "edge=Cart/Billing/1", "edge=Billing/Ledger/0" })
            after.Select(reference => reference.ToString()).ShouldContain(kept);
        before.Length.ShouldBeLessThan(after.Length);
    }

    [Test]
    [Arguments("a/b=c%d é#?", "node=a%2Fb%3Dc%25d%20é%23?")]
    [Arguments("Ω", "node=Ω")]
    [Arguments("tab\there", "node=tab%09here")]
    public void IdentifiersArePercentEncodedAndRoundTrip(string identifier, string expected)
    {
        var reference = DiagramElementRef.Node(DiagramId.Create(identifier));

        reference.ToString().ShouldBe(expected);
        DiagramElementRef.TryParse(expected, out var parsed, out _).ShouldBeTrue();
        parsed.Equals(reference).ShouldBeTrue();
        parsed.Match(
            node: id => id.Value, group: _ => "", edge: (_, _, _) => "", participant: _ => "",
            message: (_, _, _) => "", note: _ => "", frame: _ => "", step: _ => "").ShouldBe(identifier);
    }

    [Test]
    [Arguments("")]
    [Arguments("node")]
    [Arguments("vertex=Cart")]
    [Arguments("edge=Cart/Billing")]
    [Arguments("edge=Cart/Billing/01")]
    [Arguments("note=-1")]
    [Arguments("node=%ZZ")]
    [Arguments("node=%FF")]
    [Arguments("node=a%2fb")]
    [Arguments("node=%41")]
    [Arguments("node=a b")]
    public void MalformedReferencesAreTypedFailures(string text)
    {
        DiagramElementRef.TryParse(text, out _, out var error).ShouldBeFalse();
        error.ToString().ShouldNotBeEmpty();
    }

    [Test]
    public void AnElementIriJoinsTheDocumentIriAndTheReference()
    {
        var document = Iri.Create("https://example.org/diagrams/checkout");
        var reference = DiagramElementRef.Edge(DiagramId.Create("Cart"), DiagramId.Create("Billing"), 0);

        reference.TryToIri(document, out var iri, out _).ShouldBeTrue();
        iri.Value.ShouldBe("https://example.org/diagrams/checkout#edge=Cart/Billing/0");
        DiagramElementRef.TryParseIri(document, iri, out var parsed, out _).ShouldBeTrue();
        parsed.Equals(reference).ShouldBeTrue();
    }

    [Test]
    public void ADocumentIriWithAFragmentIsRejected()
    {
        var document = Iri.Create("https://example.org/diagrams/checkout#main");
        var reference = DiagramElementRef.Node(DiagramId.Create("Billing"));

        reference.TryToIri(document, out _, out var error).ShouldBeFalse();
        error.Match(
            empty: () => "", unknownKind: _ => "", invalidKey: _ => "",
            documentHasFragment: iri => iri.Value, notInDocument: _ => "").ShouldBe(document.Value);
    }

    [Test]
    [Arguments(DiagramMother.Checkout)]
    [Arguments(DiagramMother.Exchange)]
    public void RenderedElementsCarryTheirReferences(string source)
    {
        var diagram = DiagramMother.Read(source);
        var svg = XElement.Parse(SvgRenderer.Render(diagram).Output.ShouldNotBeNull());

        var rendered = svg.Descendants().Attributes("data-ref").Select(attribute => attribute.Value).ToArray();
        rendered.ShouldBeUnique();
        var expected = DiagramElementRef.Enumerate(diagram)
            .Where(reference => !reference.Kind.Equals(DiagramElementKind.Step))
            .Select(reference => reference.ToString());
        rendered.ShouldBe(expected, ignoreOrder: true);
        svg.Descendants().Where(element => element.Attribute("data-ref") is not null)
            .ShouldAllBe(element => element.Name.LocalName == "g");
    }

    [Test]
    public void ALiteralCreatesTheSameReferenceAsItsFactory()
    {
        DiagramElementRef.Create("edge=Cart/Billing/1")
            .Equals(DiagramElementRef.Edge(DiagramId.Create("Cart"), DiagramId.Create("Billing"), 1)).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => DiagramElementRef.Create("vertex=Cart"));
        new[] { DiagramElementRef.Create("node=B"), DiagramElementRef.Create("node=A") }.Order()
            .Select(reference => reference.ToString()).ShouldBe(["node=A", "node=B"]);
    }

    [Test]
    public void OrdinalsMustNotBeNegative()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DiagramElementRef.Note(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => DiagramElementRef.Message(DiagramId.Create("A"), DiagramId.Create("B"), -1));
    }
}
