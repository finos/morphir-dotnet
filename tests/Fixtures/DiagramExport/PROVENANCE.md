# Provenance

Digests of diagram output, recorded by the tests of `add-diagram-canvas-export` on the code before the SVG writers
gained the layout sink. They pin output that the change must not alter: a digest that changes is a defect to fix,
not a baseline to record again. To record a file, set `MORPHIR_RECORD_BASELINES` to this folder and run the test
named below; the test then writes the file instead of comparing.

| File | Recorded by | SHA-256 |
| --- | --- | --- |
| `svg-digests.json` | `Morphir.Diagrams.Tests.SvgBaselineTests`: one digest per pinned corpus diagram, over its SVG under the default options, each other built-in theme, the dark and light modes, the rounded and curved connector styles and bridges | `b01892cf062c67f446a7c07563554010eb26205eb89bb1f5bbb88c4e184ea512` |
