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

Route exploration, rounded and bridged connector routing, and raster
exports are tracked in the migration PRD.

## Themes

Every theme has a dark and a light palette. The built-in themes are
`DiagramThemes.Archify` (the default), `DiagramThemes.Monokai`,
`DiagramThemes.Cobalt`, `DiagramThemes.Solarized`, `DiagramThemes.Gruvbox`,
`DiagramThemes.Ocean`, `DiagramThemes.Darcula` and `DiagramThemes.Material`. Archify is the classic preset of archify, as
diagrams.mbt adapts it. Pass a theme and a mode in `SvgRenderOptions`:

```fsharp
let options = SvgRenderOptions.Create(DiagramThemes.Cobalt, ThemeMode.Dark)
let svg = Mermaid.RenderSvg(source, options)
```

```csharp
var options = SvgRenderOptions.Default.WithTheme(DiagramThemes.Monokai);
var html = Mermaid.RenderAnimated(source, options).Output;
```

`ThemeMode.Auto`, the default, writes both palettes. The diagram is dark
unless the viewer prefers light, and a `data-theme="dark"` or
`data-theme="light"` attribute on the root `<svg>` overrides the preference.
`ThemeMode.Dark` and `ThemeMode.Light` write only that palette. The offline
player page takes its background, text, surface and border colours from the
same palette and mode. The `id` and the marker identifiers derive from the
output, so several diagrams can share a page and the same diagram always
produces the same bytes.

### Custom themes

A palette has a background, text, muted text, a mask for the opaque chips
behind labels, an arrow colour, and a fill and stroke for each of the seven
`ColorFamily` values. `Color.Parse` and `Color.TryParse` read `#rgb`,
`#rrggbb`, `rgb(r, g, b)` and `rgba(r, g, b, a)`.

```csharp
FamilyColors Accent(string stroke, string fill) => FamilyColors.Create(Color.Create(fill), Color.Create(stroke));

var dark = ThemePalette.Create(
    background: Color.Create("#0b1d2a"), text: Color.Create("#f5f7fa"),
    muted: Color.Create("#9db0c2"), mask: Color.Create("#08151f"), arrow: Color.Create("#9db0c2"),
    frontend: Accent("#5ad1e6", "rgba(90, 209, 230, 0.18)"),
    backend: Accent("#7bd88f", "rgba(123, 216, 143, 0.18)"),
    database: Accent("#b39df3", "rgba(179, 157, 243, 0.18)"),
    cloud: Accent("#f2c14e", "rgba(242, 193, 78, 0.18)"),
    security: Accent("#f2727f", "rgba(242, 114, 127, 0.18)"),
    messageBus: Accent("#f59e5b", "rgba(245, 158, 91, 0.18)"),
    external: Accent("#8a9bab", "rgba(138, 155, 171, 0.2)"));

if (DiagramTheme.TryCreate("Harbour", dark, ThemePalette.DeriveLight(dark), out var theme, out var diagnostics))
    html = Mermaid.RenderAnimated(source, SvgRenderOptions.Default.WithTheme(theme)).Output;
```

F# callers use `DiagramTheme.Validate(name, dark, light)`, which returns a
`Result`. `DiagramTheme.Create` throws
instead. A theme is rejected with `theme/name` when its name is blank and
with `theme/text-contrast` when either palette's text has less than 4.5:1
contrast with its background. To vary a built-in theme, pass its palettes
to `DiagramTheme.Create`, changing a family with `ThemePalette.WithFamily`.

A family's text colour is derived unless `FamilyColors.Create(fill, stroke,
ink)` sets it. The derived colour is the stroke, darkened on a light
background or lightened on a dark one in 1 percent HSL lightness steps until
it has 4.5:1 contrast with the background and the mask. Flowchart labels sit
on the node fill, so their text must also pass over the fill. An explicit ink
is written unchanged. `ThemePalette.DeriveLight` makes a light palette from
a dark one for themes that supply one mode. The built-in light palettes are
set by hand.

### CSS variables

The SVG references its colours as CSS custom properties. These names are a
public contract:

| Variable | Use |
| --- | --- |
| `--bg` | Canvas and group backgrounds |
| `--text` | Participant names, group titles, edge labels and notes |
| `--muted` | Group outlines |
| `--mask` | Opaque chips behind labels, participant boxes and activation bars |
| `--arrow` | Connectors, lifelines, frames and dotted replies |
| `--{family}-fill` | Fills of the family's shapes |
| `--{family}-stroke` | Outlines and message lines of the family |
| `--{family}-ink` | Labels of the family |

`{family}` is the `ColorFamily.WireValue`: `frontend`, `backend`,
`database`, `cloud`, `security`, `messagebus` or `external`. The renderer's
selectors have zero specificity (`:where(svg#id)`), so any host rule that
matches the inline `<svg>` overrides a variable:

```css
.docs svg[data-diagram] { --bg: transparent; --frontend-stroke: #0969da; }
```

Flowchart nodes take a colour family from their shape: decisions and
hexagons are `cloud`, cylinders are `database`, circles are `messagebus`,
and other shapes are `backend`. Groups are dashed outlines. Sequence actors
are `frontend`, and plain participants cycle through `frontend`, `backend`,
`database`, `cloud` and `messagebus`. Requests use their sender's family,
dotted replies use `external` with an `--arrow` line, and messages with a
cross head use `security`. Text uses a monospace font stack and the SVG
embeds no font data.

## Migration coverage

| Mermaid family | Supported syntax |
| --- | --- |
| Flowcharts | `flowchart`, `graph`, and `flowchart-elk` headers, all four directions, common bracket shapes, chained and grouped node connections, link heads and labels including dotted inline labels, and nested subgraphs with node membership |
| Sequences | Participant and actor declarations, aliases and implicit participants, paired and single-ended arrows, self-messages, notes, nested activation bars, nested frames and their sections, and `autonumber` |

The sequence model exposes ordered `Events` at the root and in each frame
section. `Messages` and `Notes` include nested items in source order.
`par_over` sections use the same vertical layout as `par`.

The native .NET layout produces deterministic, script-free SVG. It shares the
upstream theme but uses its own, simpler layout. Source preservation
includes comments, front matter and directives.

Mermaid style and configuration statements, subgraph endpoints and direction
overrides, and sequence participant metadata, groups, creation, destruction,
central connections, and links are pending.
The [migration PRD](../../docs/content/contributing/design/prds/migrate-diagrams.md)
tracks the other formats and renderer features.

The reference implementation is [diagrams.mbt](https://github.com/moonrockz/diagrams.mbt/tree/60a8dd19f386b6fc65e9ddf5ddccec213c622d07),
licensed under Apache-2.0.
