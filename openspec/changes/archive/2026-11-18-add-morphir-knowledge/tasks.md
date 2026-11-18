# Tasks

Apply the repository's test-manager guidance before changing tests and its F# and C# interop guidance before exposing public APIs. Follow the required reviewer steps for those diffs. Add failing tests before behavior changes and verify green before refactoring.

## 1. Shared knowledge contracts

- [x] 1.1 Add `Morphir.Knowledge` with the repository library target-framework property. Implement typed absolute IRIs, document and revision IDs, readonly UTF-16 offsets and half-open source spans. Verify ordinal identity, invalid construction, zero values and whole-source bounds in library tests.
- [x] 1.2 Add immutable text snapshots and selections with grouped, ordered, disjoint ranges. Verify input ordering, empty, overlapping and out-of-bounds selections, and historical resolution after a path move or new revision.
- [x] 1.3 Add typed annotation and directional link envelopes and review and concept-mention examples. Verify a caller-defined payload and a dictionary IRI preserve the original text and can be inspected by a caller. Document the identities, lifetimes and both language entry points.

## 2. OKF document syntax and navigation

- [x] 2.1 Add `Morphir.Knowledge.Okf`, one immutable source and a unified document parse result. Reproduce the disposable CST proof as a repository test against the selected package versions, including bad UTF-8, strict fence recognition, nested metadata, recoverable YAML and a header-only table at EOF.
- [x] 2.2 Project YAML and Markdown into the complete public AST and CST, retaining unknown fields, trivia, typed tokens, table delimiters, links, both referenced and unused footnotes and malformed regions. Verify the supported dialect against pinned conformance cases and assert exact byte emission and ordered leaf coverage for representative source fixtures.
- [x] 2.3 Add shared cursor navigation and preorder visiting on both trees. Verify parent, child and sibling navigation across frontmatter and body, whole-document ranges, subtree boundaries, skip and stop. Document how consumers read original source and reach the AST and CST.

## 3. Knowledge bundles and CLI

- [x] 3.1 Load physical and virtual bundles with root-relative document and asset lookup and per-member failures. Verify parity between input providers, safe physical containment and deterministic ordering with the same contents.
- [x] 3.2 Implement version selection and typed document views, OKF v0.2 and v0.3 conformance, and version-specific link resolution. Verify ordinary Markdown classification, reserved files, nested frontmatter, declared and overridden versions, and internal, missing and external targets against pinned spec cases and a representative Morphir bundle.
- [x] 3.3 Add opt-in Morphir policy and source-located findings for local links, root indexes, index descriptions and citations. Verify that base OKF conformance and policy outcomes remain independently visible.
- [x] 3.4 Register `morphir knowledge validate` in the shared CLI composition and document examples for text and JSON. Verify help, version override, policy, JSON-only stdout and exit codes 0, 1 and 2 through black-box integration scenarios on the test drivers.

## 4. RDF datasets and N-Quads

- [x] 4.1 Add `Morphir.Knowledge.Rdf` with typed RDF 1.1 terms, a flat quad and triple conversions, and default and named graphs. Verify valid term positions, literal lexical forms, set membership and graph-local equality.
- [x] 4.2 Add versioned graph and statement references and separate authored claim identity. Verify empty graph annotation, evidence links targeting one graph among two with the same triple, and independent provenance on two claims about one quad. Document the addressing and blank-node scope.
- [x] 4.3 Implement UTF-8 N-Quads reading and typed findings. Verify grammar, escapes, graph label types and per-input blank-node scope against pinned W3C cases and local integration examples.
- [x] 4.4 Implement N-Quads writing and the explicit empty named graph omission option. Verify dataset isomorphism on round-trip, cross-graph blank nodes and no bytes emitted for an empty named graph unless omission is allowed. Document file exchange and diagnostics.

## 5. Package delivery and integration

- [x] 5.1 Register all three packages in dependency order in `LibraryPackages` and use the existing release metadata and version. Verify `PackLibs` yields the expected IDs, target-framework assets and dependency declarations. Document the NuGet.org package IDs and which package to install.
- [x] 5.2 Restore the locally packed libraries into fresh F# and C# consumers on net10.0 and net11.0 through the packaged-consumer test drivers. Run selection, bundle validation and N-Quads examples and inspect the restored dependency graph for each package installed alone.
- [x] 5.3 Run the knowledge validation scenarios against the packaged Native AOT CLI and verify the publish introduces no trim or AOT warnings. Run the repository's relevant build, test and OpenSpec verification targets before submitting the change.

## Workflow follow-up

- Link the morphir-dotnet tracking issue before the first pull request. Follow repository review, archive and release approval rules.
- Before the first approved release with these packages, verify that the NuGet trusted-publishing policy for `deployment.yml` covers first publication under all three IDs.
- Through that approved release, publish using `PublishLibs` and verify that all three package IDs resolve at the released version on NuGet.org.
