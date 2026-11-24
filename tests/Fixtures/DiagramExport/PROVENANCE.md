# Provenance

Digests of diagram output, recorded by the tests of `add-diagram-canvas-export` on the code before the SVG writers
gained the layout sink. They pin output that the change must not alter: a digest that changes is a defect to fix,
not a baseline to record again. To record a file, set `MORPHIR_RECORD_BASELINES` to this folder and run the test
named below; the test then writes the file instead of comparing.

| File | Recorded by | SHA-256 |
| --- | --- | --- |
| `svg-digests.json` | `Morphir.Diagrams.Tests.SvgBaselineTests`: one digest per pinned corpus diagram, over its SVG under the default options, each other built-in theme, the dark and light modes, the rounded and curved connector styles and bridges | `b01892cf062c67f446a7c07563554010eb26205eb89bb1f5bbb88c4e184ea512` |
| `player-digests.json` | `Morphir.Diagrams.Tests.PlayerBaselineTests`: for one representative diagram of each family (`DiagramCorpus.Representatives`), the checkout flowchart with embedded metadata and a railroad trace, the offline player, the SVG and the connector bindings | `8186bd245c538d15fe18ca26de78ff0fd8084d3fc459c6b7e5e3215819a72cb8` |
| `raster-digests.json` | `Morphir.Diagrams.Raster.Tests.RasterBaselineTests`: for the same representatives, the PNG as its size and decoded pixels, the JPEG and GIF bytes, and the raster player with each embedded PNG replaced by its pixel digest | `7cd513eb35e4bddc0d6cc2f5db8fc67e44844ea46803499faf6588f5d1e15417` |
| `public-api.txt` | `Morphir.Diagrams.Tests.PublicApiTests`: each public type of Morphir.Diagrams and its public and protected members. A reviewed API change records it again. | `0f4c8270bd9ba0755ed48d461b8e16887f2e15cfb6a2105f25c3d40aa9989734` |
