# Tasks

Decision ids (REF-1 … PKG-1) refer to [design.html](design.html). Before adding or moving any test, load the `test-manager` skill. After each group that changes public F# API, run the `fsharp-csharp-interop-reviewer` subagent on its diff.

## 1. Tracking and fixtures

- [x] 1.1 Open a GitHub issue for this change, link it from `proposal.md` under Tracking, and check that the link resolves ([#689](https://github.com/finos/morphir-dotnet/issues/689))
- [ ] 1.2 Pin the W3C RDF 1.1 XML test suite under `tests/Fixtures/RdfXml/` and the W3C RDFC-1.0 suite under `tests/Fixtures/RdfCanon/`, each with a `PROVENANCE.md` like `tests/Fixtures/RdfNQuads/PROVENANCE.md` (source commit, licence, and RDF/XML tests excluded for DTD processing with the reason). Verify that a fixture catalog test lists the expected test counts

## 2. N-Triples (rdf-datasets, RDF-2)

- [ ] 2.1 Write failing TUnit tests in `tests/Morphir.Knowledge.Rdf.Tests` for a default-graph round trip, the fourth-term syntax error, and blank-node identity across inputs
- [ ] 2.2 Implement `NTriples` `Read`/`TryRead`/`Write`/`TryWrite` by reusing the N-Quads grammar with graph terms disabled, and verify that the tests from 2.1 and the existing N-Quads tests pass
- [ ] 2.3 Add `NamedGraphHandling` (reject, or default graph only, with the omitted graphs reported) and its tests for N-Triples, and verify the "Named graph to N-Triples" and "Explicit omission" scenarios
- [ ] 2.4 Document N-Triples and named-graph handling, in F# and C#, in `src/Morphir.Knowledge.Rdf/README.md`, and verify that the samples compile in a test

## 3. Canonicalization (rdf-datasets, RDF-2)

- [ ] 3.1 Write failing tests that run the pinned RDFC-1.0 evaluation tests, the isomorphic-inputs scenario, and the work-limit scenario
- [ ] 3.2 Implement `RdfCanonicalization.Canonicalize(dataset, limits)` per RDFC-1.0, with a typed limit failure, and verify that the tests from 3.1 pass
- [ ] 3.3 Add a canonical option to the N-Quads and N-Triples writers, with the default output unchanged, and verify byte-identical output for two reads of the same input
- [ ] 3.4 Document canonical output in the RDF README, and verify the sample in a test

## 4. RDF/XML writer (rdf-datasets, RDF-2)

- [ ] 4.1 Write failing tests for the striped output form, preservation of datatypes and language tags, `rdf:nodeID` blank nodes, the predicate without a local name, named-graph rejection, and deterministic bytes
- [ ] 4.2 Implement `RdfXml.Write` (document) and `RdfXml.WriteElement(XmlWriter, …)` (embedding), with fixed well-known and generated prefixes, and verify that the tests from 4.1 pass
- [ ] 4.3 Document RDF/XML writing in the RDF README, and verify the sample in a test

## 5. RDF/XML reader (rdf-datasets, RDF-2)

- [ ] 5.1 Write failing tests that run the pinned W3C RDF/XML positive, negative and evaluation tests, plus DOCTYPE refusal with no file or network access, and errors with line and column
- [ ] 5.2 Implement `RdfXml.Read` over bytes and over an `XElement` with inherited namespace bindings, covering `parseType` Resource, Literal and Collection, `xml:base`, `rdf:li`, `rdf:ID` reification and property attributes, with DTDs prohibited and no resolver. Verify the suite, except the listed DTD exclusions
- [ ] 5.3 Add a writer-to-reader round-trip property test over generated default graphs, and verify that the results are isomorphic
- [ ] 5.4 Document RDF/XML reading and its security settings in the RDF README, and verify the sample in a test

## 6. SVG namespaces and metadata (svg-metadata, SVG-1)

- [ ] 6.1 Write failing tests in `tests/Morphir.Svg.Tests` for qualified element and attribute names, a single namespace declaration, reserved-prefix and duplicate-prefix failures, the metadata element as first child, and a text payload with `<`, `&` and `]]>`
- [ ] 6.2 Extend `SvgName` with `Qualified(prefix, local, namespace)`, add the optional `Metadata` node to `SvgDocument`, and update the writer. Verify that the tests from 6.1 and all existing SVG tests pass
- [ ] 6.3 Write failing tests, then implement `SvgMetadataReader.TryRead`: XML and text content, the no-metadata result, DOCTYPE refusal, a character limit, and malformed-input line and column
- [ ] 6.4 Document namespaced content and the reader in `src/Morphir.Svg/README.md`, and verify the samples in a test

## 7. Element references (diagram-linked-metadata, REF-1, REF-2)

- [ ] 7.1 Write failing tests in `tests/Morphir.Diagrams.Tests` for the reference of every element kind, parallel-edge ordinals, references that survive an unrelated insertion, percent-encoding round trips, and rejection of a document IRI with a fragment
- [ ] 7.2 Implement `DiagramElementRef` (`Parse`/`TryParse`/`ToString`) and the reference enumeration for flowcharts and sequence diagrams, and verify that the tests from 7.1 pass
- [ ] 7.3 Emit `data-ref` on the outer group of every element in the flowchart and sequence painters, update the golden outputs, and verify the "Rendered elements carry references" scenario
- [ ] 7.4 Document the reference grammar in `src/Morphir.Diagrams/README.md`, and verify that its examples match the test fixtures

## 8. Snapshots and selections (diagram-linked-metadata, SEL-1)

- [ ] 8.1 Write failing tests for `DiagramSnapshot.Create`/`FromSource` (sha256 revision stability and sensitivity), `ElementIri`/`TryResolve`, and `DiagramSelection.TryCreate` (empty, unknown, deduplicated, ordered, version mismatch)
- [ ] 8.2 Add the `Morphir.Knowledge` reference, implement the snapshot and selection types with C# `TryCreate` overloads, and verify that the tests from 8.1 pass
- [ ] 8.3 Add tests showing an `Annotation<DiagramSelection, string>` and `KnowledgeLink`s to a `TextSelection`, to an `Iri` and to another `DiagramSelection`, and verify the three link scenarios
- [ ] 8.4 Document selections, comments and links in F# and C# in the Diagrams README, and verify the samples in a test

## 9. RDF mapping (diagram-linked-metadata, RDF-1)

- [ ] 9.1 Write failing tests for comment round trips (identity, body, provenance, several elements), link round trips with the direct triple, conversion of code-point offsets for astral characters, and preservation of unknown statements
- [ ] 9.2 Add the `Morphir.Knowledge.Rdf` reference, and implement `DiagramAnnotations.Write`/`Read` with the Web Annotation and `md:` vocabularies. Verify that the tests from 9.1 pass
- [ ] 9.3 Document the vocabulary mapping, with the worked example from the design, in the Diagrams README, and verify that the example dataset parses in a test

## 10. Sidecar and embedded carriage (diagram-linked-metadata, CAR-1)

- [ ] 10.1 Write failing tests for a sidecar-to-SVG-to-extraction round trip in N-Quads, N-Triples and RDF/XML; no metadata without a dataset; a dataset without a document; named graphs under each embedding; dangling-target warnings; and byte determinism across statement order and blank-node identity
- [ ] 10.2 Add `WithDocument` and `WithMetadata` to `SvgRenderOptions`, embed the canonical dataset in `<metadata>`, and implement `DiagramMetadata.TryRead` with format detection. Verify that the tests from 10.1 pass
- [ ] 10.3 Update `tests/Morphir.Diagrams.Smoke` to render with metadata and extract it under Native AOT, and verify that managed and Linux Native AOT outputs are identical
- [ ] 10.4 Document carriage, the embedding formats and the suggested sidecar naming in the Diagrams README, and verify the samples in a test

## 11. Player presentation (diagram-linked-metadata, UI-1)

Start only after the player control redesign on the diagrams branch has landed.

- [ ] 11.1 Write failing tests for the `annotations` projection in the motion-data JSON (comments, links and properties, the `isWeb` flag, escaping)
- [ ] 11.2 Implement the projection in `AnimatedRenderer`, and the markers, panel, highlighting and keyboard handling in `Player.js`, using the `frontend-design` skill and the theme variables. Verify that the tests from 11.1 pass
- [ ] 11.3 Verify in Chromium, in light and dark mode and at narrow width, that opening by keyboard works, that `<script>` in a body stays literal text, that `javascript:` and `file:` targets are not links, that unknown-vocabulary properties are listed, and that print hides the markers
- [ ] 11.4 Document the player annotation layer in the Diagrams README, and verify the offline demo page in the docs

## 12. Integration

- [ ] 12.1 Run the full repository test build (`./build.sh --target Test`), and verify that every suite passes
- [ ] 12.2 Update `CHANGELOG.md` and the diagrams PRD (`docs/content/contributing/design/prds/migrate-diagrams.md`) feature status, and verify that `openspec validate add-diagram-linked-metadata --strict` passes

## Workflow follow-up

- Follow AGENTS.md for review, archive, tracker closure and release approval.
- Archive the change after approval, and verify that the main `rdf-datasets`, `svg-metadata` and `diagram-linked-metadata` specs contain the new requirements.
