# Tasks

Delivered in [finos/morphir-dotnet#692](https://github.com/finos/morphir-dotnet/pull/692). Each group is test-first, follows the `test-manager` skill, runs the `fsharp-csharp-interop-reviewer` when it changes public F# API, ends with a commit and a push that leaves CI green, and updates the migration PRD's feature table and the `Morphir.Diagrams` README for what it delivers.

## 1. Foundations

- [x] 1.1 Pin upstream diagrams.mbt fixtures and expected outputs at 60a8dd1 under `tests/Fixtures/DiagramsMbt/` with `PROVENANCE.md` rows and a `FixtureCatalog.DiagramsMbt` owner; verify `FixtureOwnershipTests` passes.
- [x] 1.2 Share the source checks of every entry point (null, source limit, characters SVG cannot hold, byte order mark, diagnostic cap) and keep the exact source on `DiagramDocument` as its lossless reproduction; verify `DiagramSourceTests`.
- [x] 1.3 Add the shared limits (depth, diagnostics, source) with `-limit` diagnostic codes distinct from invalid input; verify a 10,000-level nesting test returns the depth diagnostic without a stack overflow.
- [x] 1.4 Add `DiagramSource` with family detection for Mermaid headers, PlantUML `@start` lines and BPMN markup, and `unsupported-kind` for known families such as `classDiagram` and class `@startuml`; verify the dispatch tests, including keywords inside comments.
- [x] 1.5 Give `Diagram` a case per family. Each family draws through a projection of nodes, groups and edges that element references, carriage, layout and the player share, with marker shapes for pseudostates, bars, notes, documents and events; verify every existing diagram test passes unchanged.

## 2. Connectors

- [x] 2.1 Add elbow, rounded and curved connector styles as a render option, applied to the routed points of every graph family; verify path tests for each style and that the default output is unchanged.
- [x] 2.2 Add bridges, off by default, where a connector crosses an earlier one that shares no endpoint, owned by the later connector in paint order; verify bridge tests.
- [x] 2.3 Make the player follow the final path, including bridges; verify a played bridged connector follows the arch in Chromium.
- [x] 2.4 Document connector options in the README; verify `ReadmeTests` compile the examples.

## 3. Complete Mermaid flowchart and sequence syntax

- [x] 3.1 Read the rest of the flowchart grammar (shape catalogue, shape data, invisible links, classes, styles, link styles, click, accessibility, title, subgraph direction) with inert presentation diagnostics; verify against the pinned flowchart corpus.
- [x] 3.2 Read the rest of the sequence grammar (participant kinds, create and destroy, boxes, critical, break, rect colours, half-arrows, links, central connections, autonumber start and step) with `mermaid/destroyed-participant` and `mermaid/inactive-deactivation`; verify against the pinned sequence corpus.
- [x] 3.3 Render the new sequence features (creation rows, destruction crosses, boxes, half-arrows, participant kind colour families); verify SVG tests and a Chromium check in every theme.
- [x] 3.4 Add canonical printers for flowcharts and sequences; verify print, read, print gives identical text for the corpus.

## 4. State diagrams

- [x] 4.1 Add the state model with regions, typed pseudostates, transition identity and structural validation; verify model and validation tests.
- [x] 4.2 Read and print Mermaid `stateDiagram` and `stateDiagram-v2`; verify the pinned Mermaid state corpus, including scoped initial states.
- [x] 4.3 Lay out and paint state diagrams on the shared engine, with collapsed composites; verify SVG and geometry tests.
- [x] 4.4 Add state element references, colour families, carriage and player support; verify an annotated state diagram in the player in Chromium across all themes.

## 5. PlantUML envelope and state diagrams

- [x] 5.1 Read the `@start`/`@end` envelope with closer, multiple-block and trailing-content diagnostics, family detection of `@startuml` bodies, and preprocessor refusal without I/O; verify envelope tests.
- [x] 5.2 Read and print PlantUML state syntax, including history, entry and exit points, pins and `||` regions, into the state model; verify the pinned PlantUML state corpus.

## 6. Swimlanes

- [x] 6.1 Add the swimlane model with lanes, typed control roles and notes; verify model tests.
- [x] 6.2 Read and print Mermaid `swimlane-beta`, including repeated subgraph merging; verify the pinned swimlane corpus and the same-body-two-headers test.
- [x] 6.3 Lay out and paint lane bands on the shared engine; verify SVG tests and a Chromium check.
- [x] 6.4 Add swimlane references, colour families, carriage and player support; verify in Chromium.

## 7. PlantUML activity diagrams

- [x] 7.1 Read and print the modern activity dialect; verify the pinned modern corpus.
- [x] 7.2 Read and print the legacy dialect and dialect detection; verify the pinned legacy corpus and mixed-dialect diagnostics.
- [x] 7.3 Convert both dialects to structured topology with distinct finals, loop returns and exits; verify topology tests, including `break` and terminated branches.

## 8. Icon catalogue

- [x] 8.1 Write the catalogue script that checks upstream digests, normalises assets and writes the embedded resource and key table; verify a test regenerates them identically.
- [x] 8.2 Record SVG Repo licences per technology logo and draw original symbols for disallowed logos, `externalsystem`, the architecture defaults and the generic fallback; verify every registry key resolves without a fallback warning.
- [x] 8.3 Add `THIRD-PARTY-NOTICES.md` and the trademark notice to the package; verify the packed `.nupkg` contains them.
- [ ] 8.4 Render only used symbols as ID-scoped definitions with accessible names; verify two inlined diagrams do not share IDs and symbols are visible in every theme.

## 9. ZenUML and sequence symbols

- [x] 9.1 Read and print ZenUML into the sequence model with try, catch and finally sections, and annotators as participant kinds (others drawn as plain participants with a warning); verify the pinned ZenUML corpus.
- [x] 9.2 Paint participant symbols and exception frames; verify SVG tests and that sequences without them keep their snapshots.

## 10. Architecture diagrams

- [ ] 10.1 Add the architecture model and validation, including constraint findings; verify model tests.
- [ ] 10.2 Read and print `architecture-beta`; verify the pinned architecture corpus.
- [ ] 10.3 Place and route architecture diagrams with ports, group boundaries and alignment; verify geometry tests.
- [ ] 10.4 Add architecture references, colour families, carriage and player support; verify in Chromium.

## 11. Railroad diagrams

- [ ] 11.1 Add the regex and EBNF railroad models with occurrence identity; verify model tests, including the unexpanded large bound.
- [ ] 11.2 Read and print PlantUML regex and EBNF; verify the pinned corpora.
- [ ] 11.3 Lay out and paint railroad diagrams; verify SVG tests.
- [ ] 11.4 Add trace values, validation and highlight channels; verify highlight tests and that rendering without a trace is unchanged.
- [ ] 11.5 Add railroad references, colour families, carriage and player support; verify in Chromium.

## 12. Data trees

- [ ] 12.1 Add the data-tree model and read and print PlantUML JSON and YAML with highlights; verify the pinned corpora, including exact numbers and duplicate members.
- [ ] 12.2 Lay out and paint data trees with safe highlights; verify SVG tests.
- [ ] 12.3 Add data-tree references, colour families, carriage and player node visits; verify in Chromium.

## 13. BPMN documents

- [ ] 13.1 Create `Morphir.Bpmn` with a secure namespace-aware XML reader (DTD prohibited, no resolver) and a lossless element tree that keeps every element, attribute and text node; verify the prohibited-declaration test and reproduction tests.
- [ ] 13.2 Add the typed BPMN 2.0.2 view of processes, collaborations, flow elements, flows, lanes, artifacts and DI over the element tree, keeping unknown and extension content; verify against the pinned BPMN corpus.
- [ ] 13.3 Add DI, extensions, validation and reference resolution; verify the pinned BPMN corpus.
- [ ] 13.4 Add canonical and pretty writing; verify canonical round trips are byte-identical.
- [ ] 13.5 Add traversal styles and bounded graph queries; verify the 10,000-deep and cycle tests.
- [ ] 13.6 Add `Morphir.Bpmn` to the solution, test plan, package validation and AOT smoke tests; verify the build and AOT smoke pass.

## 14. BPMN diagrams

- [ ] 14.1 Add view selection and conversion to the BPMN diagram model; verify selection and ambiguity tests.
- [ ] 14.2 Paint every BPMN process and collaboration symbol; verify SVG tests per symbol.
- [ ] 14.3 Add the supplied-geometry policies and automatic layout on the shared engine; verify policy tests.
- [ ] 14.4 Add BPMN references, colour families, carriage and player support; verify in Chromium.

## 15. Tree queries

- [ ] 15.1 Create `Morphir.Diagrams.Query` with the version 1 compiler, diagnostics and limits; verify compile tests, including unsupported features.
- [ ] 15.2 Add the evaluator, ordering, projections, batch cursors and the BPMN adapter; verify match-order and batch-size tests.

## 16. Playback exploration

- [x] 16.1 Add the connection graph to the motion data for every family; verify motion data tests.
- [x] 16.2 Add route and reachability queries in F# and in `Player.js` with direction rules and limits; verify F# tests and matching player answers in Chromium.
- [x] 16.3 Add explicit scenarios and node visits; verify scenario tests and playback in Chromium.
- [x] 16.4 Add the exploration panel with keyboard, narrow and print behaviour; verify with the frontend-design skill and Chromium at desktop and 420 px.

## 17. Raster export

- [ ] 17.1 Create `Morphir.Diagrams.Raster` with the TrueType reader and embedded Noto fonts under OFL; verify glyph and missing-glyph tests.
- [ ] 17.2 Add the fixed-point rasteriser for the library's SVG and PNG encoding; verify golden PNG bytes on all CI operating systems.
- [ ] 17.3 Add JPEG and animated GIF encoding with validated options and limits; verify golden bytes and the pixel-limit test.
- [ ] 17.4 Add player PNG, JPEG and GIF downloads; verify in Chromium that downloads ignore zoom and leave playback unchanged.
- [ ] 17.5 Add the package to the solution, test plan and AOT smoke tests; verify the build and AOT smoke pass.

## 18. Integration

- [ ] 18.1 Run `./build.sh --target Test` and `openspec validate --strict` for both open changes; verify all suites pass.
- [ ] 18.2 Check every family in every built-in theme, dark and light, at desktop and 420 px in Chromium; verify no overlaps, contrast failures or horizontal scroll.
- [ ] 18.3 Update the CHANGELOG, the PRD feature table and the pull request description, including the icon catalogue note for FINOS; verify the description lists each group.
- [ ] 18.4 Merge PR #692 once every required check is green.

## Workflow follow-up

- Archive `add-diagram-linked-metadata` and `complete-diagrams-mbt-parity` after the merge, and verify the archived specs.
