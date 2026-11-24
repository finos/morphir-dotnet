// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Morphir.Diagrams.Export;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Slides.Tests;

/// <summary>
/// Pins the output of each slide renderer for one diagram of each family, over the names and contents of the package's
/// entries rather than its compressed bytes, so that the same digests hold on every runtime and operating system. The
/// files behind these digests were opened in LibreOffice before they were recorded (see the package README). The
/// marker runs it on each operating system in a pull request, so that the digests are compared across runtimes; the
/// pixels of PNG fallbacks are pinned across runtimes by the raster baselines.
/// </summary>
[Category("PlatformSpecific")]
public class SlideDigestTests
{
    public static IEnumerable<(string Name, Func<DiagramRenderer> Create)> Renderers() =>
    [
        ("exact", () => new PowerPointRenderer()),
        ("glued", () => new PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Glued)),
        ("impress", () => new OpenDocumentRenderer()),
        ("requests", () => new GoogleSlidesRenderer(GoogleSlidesOptions.Create(SlidePlacement.Widescreen, SlideObjectNamespace.Create("morphir")))),
    ];

    /// <summary>
    /// The digest of an output: for a package, each entry's name and the digest of its contents, in order; for a single
    /// text file, its bytes.
    /// </summary>
    public static string EntryDigest(byte[] package)
    {
        if (package is not [0x50, 0x4B, ..]) return DigestBaseline.Digest(package);

        var text = new StringBuilder();

        foreach (var (name, contents) in SlideRendering.Entries(package))
            text.Append(name).Append(' ').Append(DigestBaseline.Digest(contents)).Append('\n');

        return DigestBaseline.Digest(text.ToString());
    }

    [Test]
    public void EachFamilyRendersItsPinnedEntries()
    {
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, create) in Renderers())
            foreach (var (family, source) in DiagramCorpus.Representatives())
            {
                var renderer = create();
                actual.Add($"{family}.{name}{renderer.FileExtension}", EntryDigest(SlideRendering.Render(renderer, DegradationPolicy.AllowDegraded, SlideMother.Layout(source.Source)).Bytes));
            }

        DigestBaseline.Differences(FixtureCatalog.DiagramExport.SlideDigests, actual).ShouldBeEmpty();
    }
}
