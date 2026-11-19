// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Shouldly;

namespace Morphir.Diagrams.Raster.Tests;

/// <summary>PNG, JPEG and GIF export of the library's SVG, its limits, its fonts and the player downloads.</summary>
public partial class DiagramRasterTests
{
    private const string Flow = "flowchart LR\n  A[Order] --> B{Valid?}\n  B -->|yes| C[(Ship)]";

    private static string Svg(string source, DiagramTheme theme, ThemeMode mode) =>
        Mermaid.RenderSvg(source, SvgRenderOptions.Create(theme, mode)).Output!;

    private static string Hash(RasterImage image) => Convert.ToHexStringLower(SHA256.HashData(image.Bytes.AsSpan()));

    /// <summary>The RGBA pixel at a place of an 8-bit RGBA PNG that this library wrote (every row uses the Sub filter).</summary>
    private static (int R, int G, int B, int A) Pixel(byte[] png, int x, int y)
    {
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        using var idat = new MemoryStream();
        var at = 8;

        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (System.Text.Encoding.ASCII.GetString(png, at + 4, 4) == "IDAT") idat.Write(png, at + 8, length);
            at += 12 + length;
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var stride = width * 4 + 1;
        var row = raw.ToArray().AsSpan(y * stride, stride).ToArray();
        row[0].ShouldBe((byte)1);
        for (var index = 5; index < stride; index++) row[index] = (byte)(row[index] + row[index - 4]);
        return (row[1 + x * 4], row[2 + x * 4], row[3 + x * 4], row[4 + x * 4]);
    }

    /// <summary>The number of frames of a GIF, by walking its blocks.</summary>
    private static int Frames(byte[] gif)
    {
        var at = 13 + (gif[10] >= 0x80 ? 3 * (1 << ((gif[10] & 7) + 1)) : 0);
        var frames = 0;

        while (gif[at] != 0x3B)
        {
            if (gif[at] == 0x21)
            {
                at += 2;
            }
            else
            {
                frames++;
                at += 10;
                at++;
            }

            while (gif[at] != 0) at += gif[at] + 1;
            at++;
        }

        return frames;
    }

    [Test]
    public void APngOfAMonokaiFlowchartHasItsSizeAndTheDarkPalette()
    {
        var svg = Svg(Flow, DiagramThemes.Monokai, ThemeMode.Dark);
        var width = int.Parse(SvgWidth().Match(svg).Groups[1].Value);
        var background = BackgroundVariable().Match(svg).Groups[1].Value;

        var png = DiagramRaster.Render(svg, RasterOptions.Png.WithTheme(RasterTheme.Dark));

        png.Succeeded.ShouldBeTrue();
        (png.MediaType, png.Width).ShouldBe(("image/png", width));
        png.Bytes.Take(8).ShouldBe(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var (r, g, b, a) = Pixel(png.Bytes.ToArray(), 1, 1);
        $"#{r:x2}{g:x2}{b:x2}".ShouldBe(background);
        a.ShouldBe(255);
    }

    [Test]
    public void AScaleOf2DoublesBothSides()
    {
        const string Plain = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 400" width="800" height="400"><rect width="800" height="400" fill="#123456"/></svg>""";

        var png = DiagramRaster.Render(Plain, RasterOptions.Png.WithScale(2));

        (png.Width, png.Height).ShouldBe((1600, 800));
        DiagramRaster.Render(Plain, RasterOptions.Png.WithWidth(400)).Height.ShouldBe(200);
    }

    [Test]
    public void AJpegIsBaselineOnAnOpaqueBackgroundAndItsQualityIsValidated()
    {
        var jpeg = DiagramRaster.Render(Svg(Flow, DiagramThemes.Archify, ThemeMode.Light), RasterOptions.Jpeg(80));

        jpeg.MediaType.ShouldBe("image/jpeg");
        jpeg.Bytes.Take(2).ShouldBe(new byte[] { 0xFF, 0xD8 });
        jpeg.Bytes.TakeLast(2).ShouldBe(new byte[] { 0xFF, 0xD9 });
        Should.Throw<ArgumentOutOfRangeException>(() => RasterOptions.Jpeg(0));
    }

    [Test]
    public void ATwoSecondPlaybackAtTenFramesASecondHasTwentyFrames()
    {
        Mermaid.TryParse("sequenceDiagram\n  A->>B: one\n  B-->>A: two", out var document, out _).ShouldBeTrue();

        var gif = DiagramRaster.RenderGif(SvgRenderer.BuildScene(document!.Diagram), GifOptions.Create(2000, 10));

        gif.MediaType.ShouldBe("image/gif");
        Frames(gif.Bytes.ToArray()).ShouldBe(20);
        Should.Throw<ArgumentOutOfRangeException>(() => GifOptions.Create(2000, 0));
    }

    [Test]
    public void ACharacterNoFontCoversStopsTheExport()
    {
        var image = DiagramRaster.Render(Svg("flowchart LR\n  A[Ship 😀]", DiagramThemes.Archify, ThemeMode.Light), RasterOptions.Png);

        image.Succeeded.ShouldBeFalse();
        image.Bytes.ShouldBeEmpty();
        image.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("raster/missing-glyph");
    }

    [Test]
    public void TooManyPixelsFailBeforeAllocating()
    {
        var image = DiagramRaster.Render(Svg(Flow, DiagramThemes.Archify, ThemeMode.Light), RasterOptions.Png.WithWidth(100_000).WithHeight(100_000));

        image.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("raster/pixel-limit");
    }

    /// <summary>Golden digests: equal input gives these bytes on every operating system, under JIT and Native AOT.</summary>
    [Test]
    public void EqualInputGivesTheGoldenBytes()
    {
        var svg = Svg(Flow, DiagramThemes.Archify, ThemeMode.Light);
        Mermaid.TryParse("sequenceDiagram\n  A->>B: one", out var document, out _).ShouldBeTrue();

        var png = DiagramRaster.Render(svg, RasterOptions.Png);
        Hash(png).ShouldBe(Hash(DiagramRaster.Render(svg, RasterOptions.Png)));
        Hash(png).ShouldBe(GoldenPng);
        Hash(DiagramRaster.Render(svg, RasterOptions.Jpeg(90))).ShouldBe(GoldenJpeg);
        Hash(DiagramRaster.RenderGif(SvgRenderer.BuildScene(document!.Diagram), GifOptions.Create(1000, 4))).ShouldBe(GoldenGif);
    }

    [Test]
    public void ThePlayerOffersEachFormatMadeAtNaturalSize()
    {
        Mermaid.TryParse(Flow, out var document, out _).ShouldBeTrue();
        var scene = SvgRenderer.BuildScene(document!.Diagram);

        var player = DiagramRaster.RenderPlayer(scene);

        player.Diagnostics.ShouldBeEmpty();
        player.Html.ShouldContain("data-control=\"download-format\"");
        RasterData().Match(player.Html).Groups[1].Value.ShouldBe(Convert.ToBase64String(DiagramRaster.Render(scene, RasterOptions.Png).Bytes.AsSpan()));
        player.Html.IndexOf("data-raster", StringComparison.Ordinal).ShouldBeLessThan(player.Html.IndexOf("data-motion-data", StringComparison.Ordinal));
    }

    private const string GoldenPng = "90318334fdd67dc0fa733f20bb1f010f5e9df31f4deff415973a6b0099c0d9ae";
    private const string GoldenJpeg = "1d6413e166eb0063079e7feb4a119c37bdb2392ba3a33d2dcfa3a87c5ba9564a";
    private const string GoldenGif = "57499146da84f03ef70d3c0bac8a383ddf510fcd4ace8993273fca9410b94cc1";

    [GeneratedRegex("""<svg[^>]* width="(\d+)" """)]
    private static partial Regex SvgWidth();

    [GeneratedRegex("--bg: (#[0-9a-f]{6});")]
    private static partial Regex BackgroundVariable();

    [GeneratedRegex("\"png\":\\{[^}]*\"data\":\"([^\"]+)\"")]
    private static partial Regex RasterData();
}
