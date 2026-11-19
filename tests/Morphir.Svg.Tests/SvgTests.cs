// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using Shouldly;

namespace Morphir.Svg.Tests;

public class SvgTests
{
    [Test]
    public void BuildsAnSvgDocumentWithEscapedMixedContent()
    {
        var document = SvgSceneMother.LabelWithMixedContent();
        var svg = XElement.Parse(document.Render());
        XNamespace ns = "http://www.w3.org/2000/svg";
        svg.Name.ShouldBe(ns + "svg");
        var text = svg.Elements(ns + "text").ShouldHaveSingleItem();
        text.Value.ShouldBe("A < B & C > D");
        ((string?)text.Attribute("aria-label")).ShouldBe("A & B");
    }

    [Test]
    public void LaterModifiersReplaceAnAttributeWithoutMutatingTheScene()
    {
        var first = SvgSceneMother.RedRectangle();
        var second = first.WithAttribute(SvgAttribute.Create("fill", "blue"));

        first.Attributes.ShouldHaveSingleItem().Value.ShouldBe("red");
        second.Attributes.ShouldHaveSingleItem().Value.ShouldBe("blue");
    }

    [Test]
    public void MixedContentStartingWithAnElementKeepsItsText()
    {
        var document = SvgSceneMother.LabelStartingWithASpan();
        XNamespace ns = "http://www.w3.org/2000/svg";
        XElement.Parse(document.Render(), LoadOptions.PreserveWhitespace)
            .Elements(ns + "text").ShouldHaveSingleItem().Value.ShouldBe("A & B");
    }

    [Test]
    public void NamesUseOrdinalIdentityAndRejectQualifiedMarkup()
    {
        if (SvgName.TryParse("svg:rect", out _, out var failure))
            throw new InvalidOperationException("Expected a qualified name to fail.");

        failure.ShouldBe(SvgNameError.Invalid);
        SvgName.Create("viewBox").Equals(SvgName.Create("viewbox")).ShouldBeFalse();
    }
}

internal static class SvgSceneMother
{
    public static SvgDocument LabelWithMixedContent()
    {
        var subject = SvgNode.CreateElement("text", [SvgAttribute.Create("aria-label", "A & B")],
            [
                SvgNode.CreateText("A < "),
                SvgNode.CreateElement("tspan", [], [SvgNode.CreateText("B & C")]),
                SvgNode.CreateText(" > D"),
            ]);
        return SvgDocument.Create([SvgAttribute.Create("viewBox", "0 0 100 40")], [subject]);
    }

    public static SvgNode RedRectangle() =>
        SvgNode.CreateElement("rect", [SvgAttribute.Create("fill", "red")], []);

    public static SvgDocument LabelStartingWithASpan()
    {
        var tspan = SvgNode.CreateElement("tspan", [], [SvgNode.CreateText("A")]);
        var text = SvgNode.CreateElement("text", [], [tspan, SvgNode.CreateText(" & B")]);
        return SvgDocument.Create([], [text]);
    }
}
