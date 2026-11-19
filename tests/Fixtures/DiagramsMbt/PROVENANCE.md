# Provenance

Test inputs and expected results from
[moonrockz/diagrams.mbt](https://github.com/moonrockz/diagrams.mbt/tree/60a8dd19f386b6fc65e9ddf5ddccec213c622d07)
at commit `60a8dd19f386b6fc65e9ddf5ddccec213c622d07`, which `Morphir.Diagrams` and `Morphir.Bpmn` migrate.
`scripts/pin-diagrams-mbt-fixtures.fsx` writes each file from a checkout of that commit. Each file lists the
upstream path, SHA-256 and exact text of every `.mmd`, `.puml`, `.bpmn`, `.json`, `.txt` and `.graph` file below
its folders. No grammar or program source is copied: not PlantUML's Java, not Mermaid's or ZenUML's TypeScript.

| File | Upstream folders | Files | SHA-256 |
| --- | --- | --- | --- |
| `mermaid-sequence.json` | `tests/corpus/mermaid-12.2.0` | 76 | `7cd477db928f6a841c776c0c27c090d9f20fe6fe63be360877e3915333729605` |
| `mermaid-flowcharts.json` | `tests/corpus/mermaid-flowcharts` | 158 | `da3b94ad1aae41a70c100aa0262018d9e7a8921b5e17107b1e26d4863bb67426` |
| `mermaid-states.json` | `tests/corpus/mermaid-states` | 58 | `f9b8acc9efc8429d2ca8109ab40540c13028e7a9b6f7a128ca24d6e7cb8180ce` |
| `mermaid-swimlanes.json` | `tests/corpus/mermaid-swimlanes` | 43 | `aa6df8e9ecc434022ab278f31a4f3f94ec22ac9c91819239ec092a5967e8cac6` |
| `mermaid-architecture.json` | `tests/architecture-corpus` | 20 | `7a31ac39512bc195e4dc8df3db7ab411e54d932e9b3001070b5fcf01a69fac52` |
| `zenuml.json` | `tests/zenuml-corpus` | 2 | `5151feea04dc965d591bdee6d008231e9f40189a3d3ac18a1c543a82b441a9b7` |
| `plantuml-activity.json` | `tests/corpus/plantuml-activity` | 101 | `eb8042306a84f9434d6cacedaca8c836dedee8a097b9b7178ac250132594430a` |
| `plantuml-state.json` | `tests/corpus/plantuml-state` | 127 | `3706af83fa431d5fd00d39b9655389c7e2334bd75377aea14e3e468be91b4ffe` |
| `plantuml.json` | `tests/corpus/plantuml-1.2026.9` | 272 | `6937882e83a81962d78e2dd75067ada54922f104db9a894d465b464764933266` |
| `bpmn.json` | `tests/corpus/bpmn-model`, `tests/traversal-corpus`, `tests/query-corpus` | 39 | `8199636723b4ee0f413bdf25d07c1aa244ae675055f6530fdd0e409982b19acb` |

## Licences

diagrams.mbt is licensed under the Apache License 2.0. The examples inside the corpora keep the terms that
diagrams.mbt records for them in each corpus's README or NOTICE: the Mermaid examples come from the Mermaid
documentation (MIT, © 2014–2022 Knut Sveidqvist), the ZenUML examples from ZenUML (MIT, © 2022 Peng Xiao), and
the PlantUML examples from the PlantUML documentation (© the PlantUML project, <https://plantuml.com>). The BPMN
documents are written by the diagrams.mbt project under Apache-2.0; they are not OMG examples.
