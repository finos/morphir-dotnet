// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using Microsoft.FSharp.Core;
using Shouldly;

namespace Morphir.Diagrams.Slides.Tests;

/// <summary>The package writer and the PNG fallback rules.</summary>
public class PackagingTests
{
    private static FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>> Drawn(int width) =>
        FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>>.NewOk(Tuple.Create(new byte[] { 1 }, width, width));

    private static FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>> Refused(string code) =>
        FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>>.NewError(Tuple.Create(code, "refused"));

    private static FSharpFunc<string, FSharpFunc<int, FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>>>> Draw(Func<int, FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>>> draw) =>
        FuncConvert.FromFunc<string, int, FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>>>((_, width) => draw(width));

    [Test]
    public void APngIsDrawnAtTwoPixelsPerCssPixel()
    {
        var result = Packaging.fallback(Draw(Drawn), "<svg/>", 100.0, "image d1");

        result.IsOk.ShouldBeTrue();
        (result.ResultValue.Width, result.ResultValue.Density).ShouldBe((200, 2));
    }

    [Test]
    [Arguments("raster/pixel-limit")]
    [Arguments("raster/work-limit")]
    public void ALimitAtTwoStepsDownToOne(string code)
    {
        var result = Packaging.fallback(Draw(width => width > 100 ? Refused(code) : Drawn(width)), "<svg/>", 100.0, "image d1");

        (result.ResultValue.Width, result.ResultValue.Density).ShouldBe((100, 1));
    }

    [Test]
    public void ARasterFailureAtOneIsAnError()
    {
        var result = Packaging.fallback(Draw(_ => Refused("raster/pixel-limit")), "<svg/>", 100.0, "image d1");

        result.IsError.ShouldBeTrue();
        result.ErrorValue.Code.ShouldBe("slides/raster-failed");
        result.ErrorValue.Severity.ShouldBe(DiagnosticSeverity.Error);
        result.ErrorValue.Message.ShouldContain("image d1");
    }

    [Test]
    public void ARealRasterHonoursTheDensityStep()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10" width="10" height="10"><rect width="10" height="10" fill="#ff0000"/></svg>""";
        var drawn = Packaging.fallback(FuncConvert.FromFunc<string, int, FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>>>((svg, width) => Packaging.rasterize(svg, width)), svg, 10.0, "image");

        (drawn.ResultValue.Width, drawn.ResultValue.Height).ShouldBe((20, 20));
        // At 6,000 by 2,000 CSS pixels, two pixels per CSS pixel exceeds the 40 million pixel limit and one does not.
        var wide = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 6000 2000" width="6000" height="2000"><rect width="6000" height="2000" fill="#ff0000"/></svg>""";
        var stepped = Packaging.fallback(FuncConvert.FromFunc<string, int, FSharpResult<Tuple<byte[], int, int>, Tuple<string, string>>>((svg, width) => Packaging.rasterize(svg, width)), wide, 6000.0, "image");
        (stepped.ResultValue.Width, stepped.ResultValue.Density).ShouldBe((6000, 1));
    }

    [Test]
    public void APortableSvgHasTheLightPaletteInPlaceOfItsVariables()
    {
        var adaptive = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><style>
            svg { --fill: rgba(255, 0, 0, 0.5); --ink: var(--stroke); --stroke: #000000; }
            @media (prefers-color-scheme: light) { svg:not([data-theme="dark"]) { --fill: #ffffff; } }
            svg[data-theme="light"] { --fill: rgba(0, 0, 255, 0.25); --stroke: #00ff00; }
            </style><rect width="10" height="10" fill="var(--fill)" fill-opacity="0.5" stroke="var(--ink)"/><text fill="var(--missing, #123456)">x</text></svg>
            """;

        var portable = System.Xml.Linq.XDocument.Parse(Packaging.portable(adaptive));
        var ns = (System.Xml.Linq.XNamespace)"http://www.w3.org/2000/svg";
        var rect = portable.Descendants(ns + "rect").Single();

        portable.ToString().ShouldNotContain("var(");
        portable.ToString().ShouldNotContain("--fill");
        ((string?)rect.Attribute("fill")).ShouldBe("#0000ff");
        ((string?)rect.Attribute("fill-opacity")).ShouldBe("0.125");
        ((string?)rect.Attribute("stroke")).ShouldBe("#00ff00");
        ((string?)portable.Descendants(ns + "text").Single().Attribute("fill")).ShouldBe("#123456");
    }

    [Test]
    public void ANamedColourOutsideThePaintersTableAndAGradientAreReported()
    {
        var named = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="Aqua"/></svg>""";
        var gradient = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><defs><linearGradient id="g"><stop offset="0" stop-color="#fff"/></linearGradient></defs><rect width="10" height="10" fill="url(#g)"/></svg>""";
        var plain = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#ff0000" stroke="none"/></svg>""";

        var warning = Packaging.approximated(named, "page 1").ShouldNotBeNull().Value;
        warning.Code.ShouldBe("slides/png-approximated");
        warning.Message.ShouldContain("page 1");
        warning.Message.ShouldContain("Aqua");
        Packaging.approximated(gradient, "page 1").ShouldNotBeNull().Value.Message.ShouldContain("gradient");
        Packaging.approximated(plain, "page 1").ShouldBeNull();
    }

    [Test]
    public void APackageHasItsEntriesInOrderWithFixedTimes()
    {
        var bytes = Packaging.zip(Microsoft.FSharp.Collections.ListModule.OfSeq([
            new PackageEntry("mimetype", "application/x-test"u8.ToArray(), true),
            new PackageEntry("content.xml", "<a/>"u8.ToArray(), false)]));

        using var archive = new ZipArchive(new MemoryStream(bytes));
        archive.Entries.Select(entry => entry.FullName).ShouldBe(["mimetype", "content.xml"]);
        archive.Entries.ShouldAllBe(entry => entry.LastWriteTime.Year == 1980);
        bytes.ShouldBe(Packaging.zip(Microsoft.FSharp.Collections.ListModule.OfSeq([
            new PackageEntry("mimetype", "application/x-test"u8.ToArray(), true),
            new PackageEntry("content.xml", "<a/>"u8.ToArray(), false)])));
    }
}
