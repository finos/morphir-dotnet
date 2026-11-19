// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;

namespace Morphir.Svg.Tests;

public class SvgNamespaceTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Test]
    public void QualifiedElementsAndAttributesDeclareTheirNamespaceOnce()
    {
        var markup = SvgNamespaceMother.DocumentWithRdf().Render();

        var svg = XElement.Parse(markup);
        svg.Element(Svg + "rect").ShouldNotBeNull("unqualified SVG elements keep the SVG namespace");
        var rdf = svg.Element(Svg + "metadata")!.Element(XName.Get("RDF", Rdf)).ShouldNotBeNull();
        var description = rdf.Element(XName.Get("Description", Rdf)).ShouldNotBeNull();
        ((string?)description.Attribute(XName.Get("about", Rdf))).ShouldBe("https://example.org/diagrams/checkout");
        Regex.Count(markup, Regex.Escape($"xmlns:rdf=\"{Rdf}\"")).ShouldBe(1);
    }

    [Test]
    [Arguments("xmlns", "x", "https://example.org/ns")]
    [Arguments("xml", "lang", "https://example.org/not-xml")]
    [Arguments("x", "lang", "http://www.w3.org/XML/1998/namespace")]
    public void ReservedPrefixesAreTypedFailures(string prefix, string local, string ns)
    {
        SvgName.TryParseQualified(prefix, local, ns, out _, out var failure).ShouldBeFalse();
        failure.ShouldBe(SvgNameError.ReservedPrefix);
        Should.Throw<ArgumentException>(() => SvgName.Qualified(prefix, local, ns));
    }

    [Test]
    public void TheXmlPrefixMayNameItsOwnNamespace()
    {
        var name = SvgName.Qualified("xml", "lang", "http://www.w3.org/XML/1998/namespace");
        name.Prefix.ShouldBe("xml");
        name.LocalName.ShouldBe("lang");
        name.Value.ShouldBe("xml:lang");
    }

    [Test]
    [Arguments("rdf", "1bad", Rdf, "Invalid")]
    [Arguments("bad prefix", "RDF", Rdf, "InvalidPrefix")]
    [Arguments("rdf", "RDF", "", "MissingNamespace")]
    public void InvalidQualifiedNamesAreTypedFailures(string prefix, string local, string ns, string expected)
    {
        SvgName.TryParseQualified(prefix, local, ns, out _, out var failure).ShouldBeFalse();
        failure.ToString().ShouldNotBeEmpty();
        failure.Equals(expected switch
        {
            "Invalid" => SvgNameError.Invalid,
            "InvalidPrefix" => SvgNameError.InvalidPrefix,
            _ => SvgNameError.MissingNamespace,
        }).ShouldBeTrue();
    }

    [Test]
    public void TwoPrefixesForOneNamespaceOnAnElementNameBothPrefixes()
    {
        var name = SvgName.Qualified("rdf", "Description", Rdf);
        var about = SvgAttribute.Create(SvgName.Qualified("r", "about", Rdf), "https://example.org/s");

        SvgNode.TryCreateElement(name, [about], [], out _, out var error).ShouldBeFalse();
        error.ToString().ShouldContain("'r'");
        error.ToString().ShouldContain("'rdf'");
        error.Match(
            duplicateAttribute: _ => Array.Empty<string>(),
            prefixesForNamespace: (_, prefixes) => prefixes.ToArray(),
            namespacesForPrefix: (_, _) => Array.Empty<string>()).ShouldBe(["r", "rdf"]);
        Should.Throw<ArgumentException>(() => SvgNode.CreateElement(name, [about], []));
    }

    [Test]
    public void SettingAnAttributeKeepsTheElementChecks()
    {
        var description = SvgNode.CreateElement(
            SvgName.Qualified("rdf", "Description", Rdf),
            [SvgAttribute.Create(SvgName.Qualified("rdf", "about", Rdf), "https://example.org/a")],
            []);

        Should.Throw<ArgumentException>(() => description.WithAttribute(SvgAttribute.Create(SvgName.Qualified("r", "about", Rdf), "x")));
        Should.Throw<ArgumentException>(() => description.WithAttribute(SvgAttribute.Create(SvgName.Qualified("rdf", "x", "urn:other"), "x")));
        description.WithAttribute(SvgAttribute.Create(SvgName.Qualified("rdf", "about", Rdf), "https://example.org/b"))
            .Attributes.ShouldHaveSingleItem().Value.ShouldBe("https://example.org/b");
    }

    [Test]
    public void MetadataIsTheFirstChildOfTheRoot()
    {
        var svg = XElement.Parse(SvgNamespaceMother.DocumentWithRdf().Render());

        svg.Elements().First().Name.ShouldBe(Svg + "metadata");
        svg.Elements().Select(element => element.Name.LocalName).ShouldBe(["metadata", "style", "defs", "rect"]);
    }

    [Test]
    public void ATextPayloadWithMarkupCharactersReadsBackExactly()
    {
        const string payload = "<urn:s> <urn:p> \"a & b ]]> c\" .\n";
        var document = SvgDocument.Create([], []).WithMetadata([SvgNode.CreateText(payload)]);

        var svg = XElement.Parse(document.Render(), LoadOptions.PreserveWhitespace);
        svg.Element(Svg + "metadata")!.Value.ShouldBe(payload);
    }

    [Test]
    public void ADocumentWithoutMetadataHasNoMetadataElement()
    {
        var document = SvgDocument.Create([], [SvgNode.CreateElement("rect", [], [])]);

        document.HasMetadata.ShouldBeFalse();
        XElement.Parse(document.Render()).Element(Svg + "metadata").ShouldBeNull();
    }
}

internal static class SvgNamespaceMother
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>An SVG with a style sheet, definitions and a rectangle, and RDF/XML metadata.</summary>
    public static SvgDocument DocumentWithRdf()
    {
        var description = SvgNode.CreateElement(
            SvgName.Qualified("rdf", "Description", Rdf),
            [SvgAttribute.Create(SvgName.Qualified("rdf", "about", Rdf), "https://example.org/diagrams/checkout")],
            []);
        var rdf = SvgNode.CreateElement(SvgName.Qualified("rdf", "RDF", Rdf), [], [description]);

        return SvgDocument.Create(
                [SvgAttribute.Create("viewBox", "0 0 10 10")],
                [
                    SvgNode.CreateElement("style", [], [SvgNode.CreateText("rect { fill: red; }")]),
                    SvgNode.CreateElement("defs", [], []),
                    SvgNode.CreateElement("rect", [SvgAttribute.Create("width", "10")], []),
                ])
            .WithMetadata([rdf]);
    }
}
