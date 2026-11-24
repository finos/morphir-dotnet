// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>
/// Pins the SVG of every pinned corpus diagram, so that moving the writers onto the layout model changes no byte.
/// A changed digest is a defect to fix, not a baseline to record again.
/// </summary>
public class SvgBaselineTests
{
    /// <summary>
    /// The option sets that each diagram is rendered with: the defaults, then each other built-in theme, each fixed
    /// mode, each other connector style, and bridges.
    /// </summary>
    public static IReadOnlyList<(string Name, SvgRenderOptions Options)> Variants { get; } =
    [
        ("default", SvgRenderOptions.Default),
        .. DiagramThemes.All.Skip(1).Select(theme => (theme.Name, SvgRenderOptions.Create(theme, ThemeMode.Auto))),
        ("dark", SvgRenderOptions.Create(DiagramThemes.Archify, ThemeMode.Dark)),
        ("light", SvgRenderOptions.Create(DiagramThemes.Archify, ThemeMode.Light)),
        ("rounded", SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Rounded)),
        ("curved", SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Curved)),
        ("bridges", SvgRenderOptions.Default.WithBridges(true)),
    ];

    /// <summary>Every corpus source that parses, with its diagram.</summary>
    public static IReadOnlyList<(CorpusDiagram Source, Diagram Diagram)> Diagrams { get; } =
    [
        .. DiagramCorpus.Sources()
            .Select(source => (Source: source, Parsed: DiagramSource.TryParse(source.Source, out var document, out _) ? document : null))
            .Where(parsed => parsed.Parsed is not null)
            .Select(parsed => (parsed.Source, parsed.Parsed!.Diagram)),
    ];

    /// <summary>One digest for each diagram, over the SVG (or the error codes) of every variant in order.</summary>
    public static IReadOnlyDictionary<string, string> Digests()
    {
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (source, diagram) in Diagrams)
        {
            var output = new StringBuilder();

            foreach (var (name, options) in Variants)
            {
                var rendered = SvgRenderer.Render(diagram, options);
                var text = rendered.Output ?? "errors " + string.Join(" ", rendered.Diagnostics.Select(diagnostic => diagnostic.Code));
                output.Append(name).Append(' ').Append(DigestBaseline.Digest(text)).Append('\n');
            }

            digests.Add(source.ToString(), DigestBaseline.Digest(output.ToString()));
        }

        return digests;
    }

    [Test]
    public void EveryCorpusDiagramRendersTheBaselineSvg()
    {
        Diagrams.Count.ShouldBeGreaterThan(400);
        DigestBaseline.Differences(FixtureCatalog.DiagramExport.SvgDigests, Digests()).ShouldBeEmpty();
    }
}
