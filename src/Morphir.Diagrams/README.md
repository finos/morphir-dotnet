# Morphir.Diagrams

Read Mermaid into immutable F# models and render them as standalone SVG on
.NET 10 or .NET 11. The library preserves the original source text, including
its line endings and comments. Parse errors have stable codes and carry
half-open UTF-16 source ranges when a location is available.

## F#

```fsharp
open Morphir.Diagrams

let source = "flowchart LR\n  Read[Read request] --> Valid{Valid?}"

match Mermaid.Parse source with
| Ok document ->
    let rendered = SvgRenderer.Render document.Diagram
    rendered.Output |> Option.ofObj |> Option.iter (printfn "%s")
| Error diagnostics ->
    for diagnostic in diagnostics do
        eprintfn "%O" diagnostic
```

## C#

```csharp
using Morphir.Diagrams;

var source = "sequenceDiagram\n  Customer->>API: Place order";
if (Mermaid.TryParse(source, out var document, out var diagnostics))
{
    var itemCount = document.Diagram.Match(
        flowchart: graph => graph.Nodes.Length,
        sequence: sequence => sequence.Messages.Length);
    var svg = SvgRenderer.Render(document.Diagram).Output;
}
```

For rendering directly from source, call `Mermaid.RenderSvg(source)`. The
result reports the media type, output, and diagnostics. An input error leaves
`Output` empty and includes a diagnostic.

## Offline playback

Call `Mermaid.RenderAnimated(source)` and save the `text/html` output.
The player starts paused and illustrates connections in authored order.
It provides play, pause, restart, step, seek, speed, and SVG download.
Reduced-motion mode supports stepping and seeking, and hidden pages suspend
the playback clock.

`SvgRenderer.BuildScene(diagram)` exposes the immutable
[SVG document](../Morphir.Svg/README.md) and the connector occurrences for
inspection. `AnimatedRenderer.Render(scene)` uses those exact path
definitions, including sequence self-messages and reversed flowchart
connections. Download SVG saves the bytes of the static scene.

Pass a schedule to `AnimatedRenderer.Render(scene, steps)` for finite
playback. Each `PlaybackStep.Create` takes a connector from that scene, a
start time in milliseconds, and a positive duration. Schedule the same
connector more than once for repetition, and overlap intervals for
concurrent playback. A schedule from a different scene is rejected.

Use the viewport toolbar to zoom from 25% to 800%, fit the full diagram, or
enter fullscreen. Drag with a pointer or focus the stage and use the arrow
keys to pan at zoom levels above 100%. Fullscreen uses a page dialog when
native fullscreen is unavailable. Printing lays out the full static diagram.
Style the viewport per player on the `[data-diagram-viewer]` root with the
`--diagram-viewer-height`, `--diagram-viewer-canvas-bg`,
`--diagram-viewer-toolbar-bg`, `--diagram-viewer-border`,
`--diagram-viewer-control-radius`, and `--diagram-viewer-focus` properties.

Route exploration, the Archify theme and connector styling, and raster
exports are tracked in the migration PRD.

## Migration coverage

| Mermaid family | Supported syntax |
| --- | --- |
| Flowcharts | `flowchart`, `graph`, and `flowchart-elk` headers, all four directions, common bracket shapes, chained and grouped node connections, link heads and labels including dotted inline labels, and nested subgraphs with node membership |
| Sequences | Participant and actor declarations, aliases and implicit participants, paired and single-ended arrows, self-messages, notes, nested activation bars, nested frames and their sections, and `autonumber` |

The sequence model exposes ordered `Events` at the root and in each frame
section. `Messages` and `Notes` include nested items in source order.
`par_over` sections use the same vertical layout as `par`.

The native .NET layout produces deterministic, script-free SVG. It uses a
separate layout and theme from the upstream SVG renderer. Source preservation
includes comments, front matter and directives, while the current layout
uses its own presentation.

Mermaid style and configuration statements, subgraph endpoints and direction
overrides, and sequence participant metadata, groups, creation, destruction,
central connections, and links are pending.
The [migration PRD](../../docs/content/contributing/design/prds/migrate-diagrams.md)
tracks the other formats and renderer features.

The reference implementation is [diagrams.mbt](https://github.com/moonrockz/diagrams.mbt/tree/60a8dd19f386b6fc65e9ddf5ddccec213c622d07),
licensed under Apache-2.0.
