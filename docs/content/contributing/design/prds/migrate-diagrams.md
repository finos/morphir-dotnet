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
| Extended Mermaid flowchart and sequence syntax and upstream corpus parity | In Progress |
| Remaining Mermaid syntax and canonical printing | Planned |
| PlantUML activity, state, regex, EBNF, JSON, and YAML | Planned |
| BPMN XML reading and process and collaboration rendering | Planned |
| Shared SVG scene and authored-order offline playback | Implemented |
| Finite scenarios with repeated and concurrent connector occurrences | Implemented |
| Offline viewport zoom, fit, pan, and fullscreen | Implemented |
| Archify classic theme with adaptive dark and light palettes and colour families | Implemented |
| Custom themes, Monokai, Cobalt, Solarized and Gruvbox built-ins, and fixed dark or light output | Implemented |
| Playback route exploration, rounded and bridged connector styling, trace evidence, and raster export | Planned |

## Implementation notes

- The reference is [diagrams.mbt at 60a8dd1](https://github.com/moonrockz/diagrams.mbt/tree/60a8dd19f386b6fc65e9ddf5ddccec213c622d07). Its Draw.io and Graphviz DOT readers are planned upstream.
- The first increment exercises the full source-to-SVG path from both F# and C# library APIs. It establishes a shared diagnostic and document contract for subsequent format work.
- Standalone SVG uses a native layout with the archify classic theme of the upstream renderer. Its palette values and the light-theme text shades match the upstream output. Every theme has hand-set dark and light palettes; `ThemePalette.DeriveLight` helps custom themes that supply one mode. A fixed mode writes one block of CSS variables rather than upstream's literal presentation attributes, so host pages can still override the variables. Loading themes from JSON files is deferred. See the [library guide](../../../../../src/Morphir.Diagrams/README.md) for the supported syntax.
- The migration proceeds in native F# by diagram family, starting with Mermaid flowcharts and sequences. The Mermaid sequence model now retains nested frame sections and activation events. Numbered messages carry their resolved numbers.
- A local render check of the pinned upstream inputs produced well-formed SVG for 24 of the 38 sequence documentation examples and 16 of the 34 flowchart corpus cases. This check exercises reading and rendering. The upstream semantic and layout assertions still need to be ported.
- [Morphir.Svg](../../../../../src/Morphir.Svg/README.md) owns immutable SVG authoring and serialization. `Morphir.Diagrams` captures the final path geometry when it emits each headed flowchart edge or sequence message. The standalone player uses that geometry for its moving overlay and embeds the canonical SVG for byte-exact downloads.
- Finite scenarios schedule connector bindings from the same scene. Equal start times and overlapping intervals preserve concurrency, and repeated references create distinct playback occurrences.
- Viewport controls transform the SVG and its motion overlay together. Fullscreen uses the browser API with an HTML dialog fallback, and print styles restore the fitted static diagram.
- [The upstream animation specification](https://github.com/moonrockz/diagrams.mbt/blob/60a8dd19f386b6fc65e9ddf5ddccec213c622d07/openspec/specs/diagram-animation/spec.md) defines the remaining playback and structural exploration behavior. Matching the rounded and bridged connector styling is part of rendering parity.
