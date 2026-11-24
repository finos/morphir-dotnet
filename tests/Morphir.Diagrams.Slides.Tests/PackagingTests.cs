// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using Shouldly;

namespace Morphir.Diagrams.Slides.Tests;

/// <summary>The package writer and the PNG fallback rules.</summary>
public class PackagingTests
{
    private static RasterAttempt Drawn(int width) => RasterAttempt.Drawn([1], width, width);

    private static RasterAttempt Refused(string code) => RasterAttempt.Refused(code, "refused");

    private static Func<string, int, RasterAttempt> Draw(Func<int, RasterAttempt> draw) => (_, width) => draw(width);

    private static readonly Func<string, int, RasterAttempt> Raster = (svg, width) => Packaging.rasterize(svg, width);

    [Test]
    public void APngIsDrawnAtTwoPixelsPerCssPixel()
    {
        PngFallbacks.TryDraw(Draw(Drawn), "<svg/>", 100.0, "image d1", out var png, out _).ShouldBeTrue();

        (png!.Width, png.Density).ShouldBe((200, 2));
    }

    [Test]
    [Arguments("raster/pixel-limit")]
    [Arguments("raster/work-limit")]
    public void ALimitAtTwoStepsDownToOne(string code)
    {
        PngFallbacks.TryDraw(Draw(width => width > 100 ? Refused(code) : Drawn(width)), "<svg/>", 100.0, "image d1", out var png, out _).ShouldBeTrue();

        (png!.Width, png.Density).ShouldBe((100, 1));
    }

    [Test]
    public void ARasterFailureAtOneIsAnError()
    {
        PngFallbacks.TryDraw(Draw(_ => Refused("raster/pixel-limit")), "<svg/>", 100.0, "image d1", out _, out var failure).ShouldBeFalse();

        failure!.Code.ShouldBe("slides/raster-failed");
        failure.Severity.ShouldBe(DiagnosticSeverity.Error);
        failure.Message.ShouldContain("image d1");
        failure.Message.ShouldContain("raster/pixel-limit");
    }

    [Test]
    public void ARealRasterHonoursTheDensityStep()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10" width="10" height="10"><rect width="10" height="10" fill="#ff0000"/></svg>""";
        PngFallbacks.TryDraw(Raster, svg, 10.0, "image", out var drawn, out _).ShouldBeTrue();

        (drawn!.Width, drawn.Height).ShouldBe((20, 20));
        // At 6,000 by 2,000 CSS pixels, two pixels per CSS pixel exceeds the 40 million pixel limit and one does not.
        var wide = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 6000 2000" width="6000" height="2000"><rect width="6000" height="2000" fill="#ff0000"/></svg>""";
        PngFallbacks.TryDraw(Raster, wide, 6000.0, "image", out var stepped, out _).ShouldBeTrue();
        (stepped!.Width, stepped.Density).ShouldBe((6000, 1));
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

        var warning = Packaging.approximated(named, "page 1").ShouldNotBeNull();
        warning.Code.ShouldBe("slides/png-approximated");
        warning.Message.ShouldContain("page 1");
        warning.Message.ShouldContain("Aqua");
        Packaging.approximated(gradient, "page 1").ShouldNotBeNull().Message.ShouldContain("gradient");
        Packaging.approximated(plain, "page 1").ShouldBeNull();
    }

    [Test]
    // The ZIP writer compresses with the runtime's native zlib.
    [Category("PlatformSpecific")]
    public void APackageHasItsEntriesInOrderWithFixedTimes()
    {
        PackageEntry[] entries =
        [
            new PackageEntry("mimetype", "application/x-test"u8.ToArray(), true),
            new PackageEntry("content.xml", "<a/>"u8.ToArray(), false),
        ];
        var bytes = Packaging.zip(entries);

        using var archive = new ZipArchive(new MemoryStream(bytes));
        archive.Entries.Select(entry => entry.FullName).ShouldBe(["mimetype", "content.xml"]);
        archive.Entries.ShouldAllBe(entry => entry.LastWriteTime.Year == 1980);
        bytes.ShouldBe(Packaging.zip(entries));
    }
}
