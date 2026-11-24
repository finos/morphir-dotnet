// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Raster.Tests;

/// <summary>
/// Pins the PNG, JPEG, GIF and raster player output of one diagram of each family, so that moving the writers onto
/// the layout model changes no picture. PNG compression comes from the runtime's zlib, so a PNG is pinned by its
/// decoded pixels, and the player by its HTML with each embedded PNG replaced by its pixel digest. The JPEG and GIF
/// encoders are the library's own, so their bytes are pinned.
/// </summary>
[Category("PlatformSpecific")]
public partial class RasterBaselineTests
{
    /// <summary>The width, height and RGBA rows of an 8-bit RGBA PNG, unfiltered.</summary>
    public static (int Width, int Height, byte[] Pixels) Decode(ReadOnlySpan<byte> png)
    {
        var width = BinaryPrimitives.ReadInt32BigEndian(png[16..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(png[20..]);
        png[24].ShouldBe((byte)8);
        png[25].ShouldBe((byte)6);
        using var idat = new MemoryStream();

        for (var at = 8; at < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[at..]);
            if (Encoding.ASCII.GetString(png.Slice(at + 4, 4)) == "IDAT") idat.Write(png.Slice(at + 8, length));
            at += 12 + length;
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var data = raw.ToArray();
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            var filter = data[y * (stride + 1)];
            var source = data.AsSpan(y * (stride + 1) + 1, stride);
            var row = pixels.AsSpan(y * stride, stride);
            ReadOnlySpan<byte> above = y > 0 ? pixels.AsSpan((y - 1) * stride, stride) : new byte[stride];

            for (var x = 0; x < stride; x++)
            {
                int left = x >= 4 ? row[x - 4] : 0, up = above[x], corner = x >= 4 ? above[x - 4] : 0;
                var predicted = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, corner),
                    _ => throw new InvalidDataException($"PNG filter {filter} is not defined."),
                };
                row[x] = (byte)(source[x] + predicted);
            }
        }

        return (width, height, pixels);
    }

    private static int Paeth(int left, int up, int corner)
    {
        var estimate = left + up - corner;
        int toLeft = Math.Abs(estimate - left), toUp = Math.Abs(estimate - up), toCorner = Math.Abs(estimate - corner);
        return toLeft <= toUp && toLeft <= toCorner ? left : toUp <= toCorner ? up : corner;
    }

    /// <summary>The digest of a PNG's size and decoded pixels.</summary>
    public static string PixelDigest(ReadOnlySpan<byte> png)
    {
        var (width, height, pixels) = Decode(png);
        return DigestBaseline.Digest($"{width}x{height} {DigestBaseline.Digest(pixels)}");
    }

    /// <summary>A raster player's HTML with each embedded PNG replaced by its pixel digest.</summary>
    public static string PlayerText(string html) =>
        EmbeddedPng().Replace(html, match => match.Groups[1].Value + "pixels:" + PixelDigest(Convert.FromBase64String(match.Groups[2].Value)) + "\"");

    [Test]
    public void TheRasterOutputOfEachFamilyKeepsItsBaseline()
    {
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (family, source) in DiagramCorpus.Representatives())
        {
            DiagramSource.TryParse(source.Source, out var document, out var errors).ShouldBeTrue(string.Join(", ", errors));
            var scene = SvgRenderer.BuildScene(document!.Diagram);
            var png = DiagramRaster.Render(scene, RasterOptions.Png);
            var jpeg = DiagramRaster.Render(scene, RasterOptions.Jpeg(90));
            var gif = DiagramRaster.RenderGif(scene, GifOptions.Create(1000, 4));

            digests.Add(family + " png", png.Succeeded ? PixelDigest(png.Bytes.AsSpan()) : "failed");
            digests.Add(family + " jpeg", jpeg.Succeeded ? DigestBaseline.Digest(jpeg.Bytes.AsSpan()) : "failed");
            digests.Add(family + " gif", gif.Succeeded ? DigestBaseline.Digest(gif.Bytes.AsSpan()) : "failed");
            digests.Add(family + " player", DigestBaseline.Digest(PlayerText(DiagramRaster.RenderPlayer(scene).Html)));
        }

        digests.Values.ShouldNotContain("failed");
        DigestBaseline.Differences(FixtureCatalog.DiagramExport.RasterDigests, digests).ShouldBeEmpty();
    }

    [GeneratedRegex("(\"png\":\\{[^}]*\"data\":\")([^\"]+)\"")]
    private static partial Regex EmbeddedPng();
}
