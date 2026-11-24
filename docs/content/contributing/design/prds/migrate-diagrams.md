# Migrate diagrams.mbt to .NET

Status: In Progress

## Requirements

- Provide a packable F# library for reading diagram text, inspecting its model, and rendering it to standalone SVG.
- Make the library usable from C# with nullable annotations, immutable collections, and exhaustive dispatch over diagram families.
- Retain the original source and report errors with stable codes and UTF-16 source positions. Invalid input must not produce a partial SVG.
- Record coverage against the upstream reader and renderer contracts. Test a representative source for each supported syntax and include regression cases for malformed input.
- Provide an immutable, composable SVG scene library with declarative F# builders and C# factories. Diagram layout emits a scene with bindings to the final connector geometry.
- Match the upstream Archify playback behavior across supported diagram families. Static SVG and playback must share geometry, and downloading from playback must return the static SVG bytes. The player must operate offline, track authored occurrences, honor reduced motion, and pause its clock while hidden.

## Feature status tracking

| Capability | Status |
| --- | --- |
| Shared diagnostics, initial Mermaid flowcharts and sequences, SVG, and C# API | Implemented |
| Extended Mermaid flowchart and sequence syntax and upstream corpus parity | Implemented |
| Remaining Mermaid syntax (state, swimlane, architecture, ZenUML) and canonical printing | Implemented |
| PlantUML activity, state, regex, EBNF, JSON, and YAML | Implemented |
| BPMN XML reading and process and collaboration rendering (`Morphir.Bpmn`) | Implemented |
| Shared SVG scene and authored-order offline playback | Implemented |
| Finite scenarios with repeated and concurrent connector occurrences | Implemented |
| Offline viewport zoom, fit, pan, and fullscreen | Implemented |
| Archify classic theme with adaptive dark and light palettes and colour families | Implemented |
| Custom themes, Monokai, Cobalt, Solarized, Gruvbox, Ocean, Darcula and Material built-ins, and fixed dark or light output | Implemented |
| Element references, snapshots and selections of a diagram revision | Implemented |
| Comments, links and statements in RDF, embedded in the SVG and shown in the offline player | Implemented |
| Playback route exploration, rounded and bridged connector styling, trace evidence, and raster export (`Morphir.Diagrams.Raster`) | Implemented |
| Tree queries over BPMN documents (`Morphir.Diagrams.Query`) | Implemented |
| A renderer-neutral layout, layout features with fallbacks, and renderers from other assemblies (`DiagramExport`) | Implemented |
| Editable draw.io, Excalidraw and Obsidian Excalidraw export (`Morphir.Diagrams.Canvas`) | Implemented |
| Slide export: PowerPoint, OpenDocument, Google Slides and reveal.js (`Morphir.Diagrams.Slides`) | Implemented ([#706](https://github.com/finos/morphir-dotnet/issues/706)) |

## Implementation notes

- The reference is [diagrams.mbt at 60a8dd1](https://github.com/moonrockz/diagrams.mbt/tree/60a8dd19f386b6fc65e9ddf5ddccec213c622d07). Its Draw.io and Graphviz DOT readers are planned upstream.
- The first increment exercises the full source-to-SVG path from both F# and C# library APIs. It establishes a shared diagnostic and document contract for subsequent format work.
- Standalone SVG uses a native layout with the archify classic theme of the upstream renderer. Its palette values and the light-theme text shades match the upstream output. Every theme has hand-set dark and light palettes; `ThemePalette.DeriveLight` helps custom themes that supply one mode. A fixed mode writes one block of CSS variables rather than upstream's literal presentation attributes, so host pages can still override the variables. Loading themes from JSON files is deferred. See the [library guide](../../../../../src/Morphir.Diagrams/README.md) for the supported syntax.
- The migration proceeds in native F# by diagram family, starting with Mermaid flowcharts and sequences. The Mermaid sequence model now retains nested frame sections and activation events. Numbered messages carry their resolved numbers.
- A local render check of the pinned upstream inputs produced well-formed SVG for 24 of the 38 sequence documentation examples and 16 of the 34 flowchart corpus cases. This check exercises reading and rendering. The upstream semantic and layout assertions still need to be ported.
- [Morphir.Svg](../../../../../src/Morphir.Svg/README.md) owns immutable SVG authoring and serialization. `Morphir.Diagrams` captures the final path geometry when it emits each headed flowchart edge or sequence message. The standalone player uses that geometry for its moving overlay and embeds the canonical SVG for byte-exact downloads.
- Finite scenarios schedule connector bindings from the same scene. Equal start times and overlapping intervals preserve concurrency, and repeated references create distinct playback occurrences.
- Viewport controls transform the SVG and its motion overlay together. Fullscreen uses the browser API with an HTML dialog fallback, and print styles restore the fitted static diagram.
- Diagram-linked metadata ([#689](https://github.com/finos/morphir-dotnet/issues/689)) gives each element a reference from its authored identifier and a per-kind ordinal, so references survive layout changes. A dataset about the diagram's document is embedded canonically (RDFC-1.0) in the SVG's `metadata`, so the same inputs give identical bytes. The player receives a JSON projection of it and never reads RDF.
- [The upstream animation specification](https://github.com/moonrockz/diagrams.mbt/blob/60a8dd19f386b6fc65e9ddf5ddccec213c622d07/openspec/specs/diagram-animation/spec.md) defines the remaining playback and structural exploration behavior. Matching the rounded and bridged connector styling is part of rendering parity.
- The layout of a page ([#705](https://github.com/finos/morphir-dotnet/issues/705)) comes from the same drawing pass as its SVG: the final pass records hints beside its SVG tree, and the layout sink turns each painted primitive into part of a drawable, so the SVG is unchanged and a test checks that every corpus layout paints what its SVG paints. Renderers declare the layout features they draw, and the library lowers the rest through fallbacks in a fixed order before a renderer receives a plan. See [Layouts and renderers](../../../../../src/Morphir.Diagrams/README.md#layouts-and-renderers) and [Morphir.Diagrams.Canvas](../../../../../src/Morphir.Diagrams.Canvas/README.md).
- Slide export ([#706](https://github.com/finos/morphir-dotnet/issues/706)) builds on the same layouts. One placement fits each page onto a slide with an exact scale and one rounding on the format's grid, and four renderers write PowerPoint, OpenDocument, Google Slides requests and reveal.js decks. The office renderers embed each SVG image with the light palette in place of its CSS variables, which office applications do not read, and a PNG fallback from Morphir.Diagrams.Raster. See [Morphir.Diagrams.Slides](../../../../../src/Morphir.Diagrams.Slides/README.md).
