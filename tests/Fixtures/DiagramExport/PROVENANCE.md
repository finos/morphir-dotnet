# Provenance

Digests of diagram output, recorded by the tests of `add-diagram-canvas-export` on the code before the SVG writers
gained the layout sink. They pin output that the change must not alter: a digest that changes is a defect to fix,
not a baseline to record again. To record a file, set `MORPHIR_RECORD_BASELINES` to this folder and run the test
named below; the test then writes the file instead of comparing.

| File | Recorded by | SHA-256 |
| --- | --- | --- |
| `svg-digests.json` | `Morphir.Diagrams.Tests.SvgBaselineTests`: one digest per pinned corpus diagram, over its SVG under the default options, each other built-in theme, the dark and light modes, the rounded and curved connector styles and bridges | `b01892cf062c67f446a7c07563554010eb26205eb89bb1f5bbb88c4e184ea512` |
| `player-digests.json` | `Morphir.Diagrams.Tests.PlayerBaselineTests`: for one representative diagram of each family (`DiagramCorpus.Representatives`), the checkout flowchart with embedded metadata and a railroad trace, the offline player, the SVG and the connector bindings | `549f90d9c3423fbaf26e6b9a0a17241ef2e3aef13a454f860b2632ad649f0fc1` |
| `raster-digests.json` | `Morphir.Diagrams.Raster.Tests.RasterBaselineTests`: for the same representatives, the PNG as its size and decoded pixels, the JPEG and GIF bytes, and the raster player with each embedded PNG replaced by its pixel digest | `a079363eb645dc27c10f720ea95aa0fbd08b4ae459c4f4a1f66b96972b5cbd49` |
