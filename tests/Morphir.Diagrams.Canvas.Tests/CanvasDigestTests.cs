// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Morphir.Diagrams.Export;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Canvas.Tests;

/// <summary>
/// Pins the output of each canvas renderer for one diagram of each family. The files behind these digests were opened
/// in the applications before they were recorded (see the package README). The marker runs it on each operating
/// system in a pull request, so that line endings and formatting are compared across platforms.
/// </summary>
[Category("PlatformSpecific")]
public class CanvasDigestTests
{
    public static IEnumerable<Func<DiagramRenderer>> Renderers() =>
        [() => new DrawIoRenderer(), () => new ExcalidrawRenderer(), () => new ObsidianExcalidrawRenderer()];

    [Test]
    public void EachFamilyRendersItsPinnedBytes()
    {
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var renderer in Renderers().Select(create => create()))
            foreach (var (family, source) in DiagramCorpus.Representatives())
                actual.Add($"{family}{renderer.FileExtension}", DigestBaseline.Digest(CanvasMother.Render(renderer, CanvasMother.Layout(source.Source))));

        DigestBaseline.Differences(FixtureCatalog.DiagramExport.CanvasDigests, actual).ShouldBeEmpty();
    }
}
