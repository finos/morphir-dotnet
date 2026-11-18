# Proposal

## Why

Morphir knowledge bundles need a .NET validator for CI, and .NET consumers need source-aware document trees to attach reviews and dictionary links to the text people actually read. A reviewer should be able to comment on two passages in one revision of a concept and link those passages to the RDF statement they support.

## What Changes

- Add lossless OKF parsing with a single document root for YAML frontmatter and Markdown, a full AST and CST, and common cursor-based visitors.
- Load and validate OKF v0.2 and v0.3 knowledge bundles through the library and `morphir knowledge validate`. Provide text and JSON diagnostics and an opt-in Morphir knowledge-base policy.
- Add reusable contracts for document and revision identity, disjoint text selections, typed annotations and directional links.
- Add RDF 1.1 graphs, datasets and flat quads, with graph and statement targets and N-Quads reading and writing.
- Publish `Morphir.Knowledge`, `Morphir.Knowledge.Okf` and `Morphir.Knowledge.Rdf` to NuGet.org through the repository's library release process. Consumers can install the shared contracts alone.

## Capabilities

### New Capabilities

- `okf-documents` covers unified parsing, lossless syntax, semantic nodes, source locations and cursor traversal.
- `okf-bundle-validation` covers knowledge bundle loading, versioned conformance, Morphir policy and the validation command.
- `knowledge-annotations` covers versioned selections, extensible annotations and directional links.
- `rdf-datasets` covers RDF terms, graphs, statement and graph targets, and N-Quads interchange.
- `knowledge-nuget-packages` covers independent library consumption and NuGet.org delivery.

### Modified Capabilities

## Tracking

This change is tracked in [finos/morphir-dotnet#682](https://github.com/finos/morphir-dotnet/issues/682).

## Impact

- Add three packable F# libraries and a C# command in the shared `Morphir.Tooling` host. Use `Morphir.Files` for virtual bundle inputs.
- Add package dependencies for Markdown and YAML only to the OKF library. RDF storage, query engines and optional interchange adapters can build on the public types independently.
- Extend the existing library package inventory, with restored F# and C# consumer coverage and a native host check for the new command.
