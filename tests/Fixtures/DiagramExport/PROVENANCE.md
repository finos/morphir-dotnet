# Provenance

Digests of diagram output that `add-diagram-canvas-export` pins. No external contract defines these bytes; they
guard the library's own output while its writers gain the layout sink, so that a refactor changes no SVG, player,
raster or canvas byte. During that change a digest that changes is a defect to fix, not a baseline to record again.
After it, a change that alters this output on purpose records the files again and says why in its commit.

The SVG, player and raster files were recorded before the layout sink was added in commit `ff126fbe`. The swimlane
rows of `player-digests.json` and `raster-digests.json` were recorded later, when the representative swimlane became a
diagram with lanes; recording them again on `76399312`, the commit before `ff126fbe`, gives identical files. `public-api.txt` and
`canvas-digests.json` pin the new API and the canvas renderers' output, and `slide-digests.json`, from
`add-diagram-slide-export`, pins the slide renderers' output.

To record a file, set `MORPHIR_RECORD_BASELINES` to this folder and run the test named below; the test writes the file
there and fails, so that the variable is never left set by accident.

| File | Recorded by | SHA-256 |
| --- | --- | --- |
| `svg-digests.json` | `Morphir.Diagrams.Tests.SvgBaselineTests`: one digest per pinned corpus diagram, over its SVG under the default options, each other built-in theme, the dark and light modes, the rounded and curved connector styles and bridges | `b01892cf062c67f446a7c07563554010eb26205eb89bb1f5bbb88c4e184ea512` |
| `player-digests.json` | `Morphir.Diagrams.Tests.PlayerBaselineTests`: for one representative diagram of each family (`DiagramCorpus.Representatives`), the checkout flowchart with embedded metadata and a railroad trace, the offline player, the SVG and the connector bindings | `8186bd245c538d15fe18ca26de78ff0fd8084d3fc459c6b7e5e3215819a72cb8` |
| `raster-digests.json` | `Morphir.Diagrams.Raster.Tests.RasterBaselineTests`: for the same representatives, the PNG as its size and decoded pixels, the JPEG and GIF bytes, and the raster player with each embedded PNG replaced by its pixel digest | `7cd513eb35e4bddc0d6cc2f5db8fc67e44844ea46803499faf6588f5d1e15417` |
| `public-api.txt` | `Morphir.Diagrams.Tests.PublicApiTests`: each public type of Morphir.Diagrams and its public and protected members. A reviewed API change records it again. | `0f4c8270bd9ba0755ed48d461b8e16887f2e15cfb6a2105f25c3d40aa9989734` |
| `canvas-digests.json` | `Morphir.Diagrams.Canvas.Tests.CanvasDigestTests`: the draw.io, Excalidraw and Obsidian output of each representative, after the files were checked in draw.io 32.7.5, Excalidraw 0.18.0 and Obsidian with the Excalidraw plugin 2.29.4 | `8c1fea9aee84d198d0e3cbb935983053d4acfb77d6658947c583ea56da0c44d5` |
| `slide-digests.json` | `Morphir.Diagrams.Slides.Tests.SlideDigestTests`: for each representative and each slide renderer, the names and contents of the package's entries (not its compressed bytes), after the packages were opened in LibreOffice 26.8 and compared with the SVG, the Google Slides request bodies were checked against the Slides API v1 discovery schema, and the reveal.js decks were opened in Chromium | `4854ab6569c0413a9c06a5553f40f1ba27622ca9a521643a4f19879e2e574a55` |
