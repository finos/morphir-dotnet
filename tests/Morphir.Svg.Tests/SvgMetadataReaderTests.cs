// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Shouldly;

namespace Morphir.Svg.Tests;

public class SvgMetadataReaderTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    [Test]
    public void EmbeddedRdfXmlComesBackWithItsNamespaceBindings()
    {
        var bytes = Encoding.UTF8.GetBytes(SvgNamespaceMother.DocumentWithRdf().Render());

        SvgMetadataReader.TryRead(bytes, out var result, out _).ShouldBeTrue();
        result.TryGetContent(out var content).ShouldBeTrue();
        var rdf = content.ElementsIn(Rdf).ShouldHaveSingleItem();
        rdf.Name.LocalName.ShouldBe("RDF");
        rdf.GetPrefixOfNamespace(Rdf).ShouldBe("rdf");
        content.Elements.ShouldHaveSingleItem().ShouldBeSameAs(rdf);
    }

    [Test]
    public void ATextPayloadIsReturnedExactly()
    {
        const string payload = "<urn:s> <urn:p> \"a & b ]]> c\" .\r\n<urn:s> <urn:q> \"\t\" .\n";
        var document = SvgDocument.Create([], []).WithMetadata([SvgNode.CreateText(payload)]);

        SvgMetadataReader.TryRead(Encoding.UTF8.GetBytes(document.Render()), out var result, out _).ShouldBeTrue();
        result.TryGetContent(out var content).ShouldBeTrue();
        content.Text.ShouldBe(payload);
        content.Elements.ShouldBeEmpty();
    }

    [Test]
    public void AnSvgWithoutMetadataIsNotAnError()
    {
        var bytes = Encoding.UTF8.GetBytes(SvgDocument.Create([], [SvgNode.CreateElement("rect", [], [])]).Render());

        SvgMetadataReader.TryRead(bytes, out var result, out _).ShouldBeTrue();
        result.TryGetContent(out _).ShouldBeFalse();
        result.Match(found: _ => "found", notFound: () => "none").ShouldBe("none");
    }

    [Test]
    public void ADocumentTypeIsRefusedWithoutResolvingItsEntities()
    {
        var bytes = Encoding.UTF8.GetBytes(SvgMetadataMother.ExternalEntity);

        SvgMetadataReader.TryRead(bytes, out _, out var error).ShouldBeFalse();
        error.Kind.ShouldBe(SvgReadErrorKind.DocumentTypeDeclaration);
        error.Kind.Code.ShouldBe("svg.doctype");
        error.TryGetLocation(out var location).ShouldBeTrue();
        location.Line.ShouldBe(2);
    }

    [Test]
    public void ADocumentTypeInUtf16IsRefusedAsADocumentType()
    {
        var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(SvgMetadataMother.ExternalEntity.Replace("<?xml version=\"1.0\"?>", "<?xml version=\"1.0\" encoding=\"utf-16\"?>")))
            .ToArray();

        SvgMetadataReader.TryRead(bytes, out _, out var error).ShouldBeFalse();
        error.Kind.ShouldBe(SvgReadErrorKind.DocumentTypeDeclaration);
    }

    [Test]
    public void DocumentTypeTextInsideContentIsNotADocumentType()
    {
        const string markup = "<svg xmlns=\"http://www.w3.org/2000/svg\"><metadata><![CDATA[<!DOCTYPE x>]]></metadata>\n<rect></svg>";

        SvgMetadataReader.TryRead(Encoding.UTF8.GetBytes(markup), out _, out var error).ShouldBeFalse();
        error.Kind.ShouldBe(SvgReadErrorKind.MalformedXml);
    }

    [Test]
    public void TheCharacterLimitCountsUtf16Characters()
    {
        var text = SvgNamespaceMother.DocumentWithRdf().Render();
        var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();

        SvgMetadataReader.TryRead(bytes, text.Length - 1, out _, out var error).ShouldBeFalse();
        error.Kind.ShouldBe(SvgReadErrorKind.LimitExceeded);
        SvgMetadataReader.TryRead(bytes, text.Length, out _, out _).ShouldBeTrue();
    }

    [Test]
    public void InputBeyondTheCharacterLimitIsRefused()
    {
        var bytes = Encoding.UTF8.GetBytes(SvgNamespaceMother.DocumentWithRdf().Render());

        SvgMetadataReader.TryRead(bytes, 64, out _, out var error).ShouldBeFalse();
        error.Kind.ShouldBe(SvgReadErrorKind.LimitExceeded);
        Should.Throw<ArgumentOutOfRangeException>(() => SvgMetadataReader.TryRead(bytes, 0, out _, out _));
    }

    [Test]
    public void MalformedInputNamesItsLine()
    {
        var bytes = Encoding.UTF8.GetBytes(SvgMetadataMother.UnclosedOnLineFour);

        SvgMetadataReader.TryRead(bytes, out _, out var error).ShouldBeFalse();
        error.Kind.ShouldBe(SvgReadErrorKind.MalformedXml);
        error.TryGetLocation(out var location).ShouldBeTrue();
        location.Line.ShouldBe(4);
        error.ToString().ShouldContain("4:");
    }

    [Test]
    public void ARootOtherThanSvgIsAnError()
    {
        SvgMetadataReader.TryRead("<html/>"u8.ToArray(), out _, out var error).ShouldBeFalse();
        error.Kind.ShouldBe(SvgReadErrorKind.NotSvg);
    }
}

internal static class SvgMetadataMother
{
    /// <summary>A document type with a file and a network entity, on line 2.</summary>
    public const string ExternalEntity = """
        <?xml version="1.0"?>
        <!DOCTYPE svg [ <!ENTITY secret SYSTEM "file:///etc/passwd"> <!ENTITY remote SYSTEM "https://example.org/x"> ]>
        <svg xmlns="http://www.w3.org/2000/svg"><metadata>&secret;&remote;</metadata></svg>
        """;

    /// <summary>The metadata element opened on line 2 is closed by the svg end tag on line 4.</summary>
    public const string UnclosedOnLineFour = """
        <svg xmlns="http://www.w3.org/2000/svg">
          <metadata>
            <rect/>
        </svg>
        """;
}
