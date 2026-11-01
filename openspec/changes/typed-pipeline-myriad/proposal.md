# Proposal

## Why

`Morphir.IR.Pipeline` has no production users, and the first real backend bypassed it because its contracts are built on `obj`. The Myriad backend then grew its own file, diagnostic and publication types alongside the pipeline's. Putting a real pipeline behind Myriad replaces that duplication with one model and exposes what the pipeline actually needs, while nothing outside this repository depends on the API yet: no release tag contains these types.

## What Changes

- **BREAKING**: Replace every `obj` in the pipeline's public contracts with generics. Parsers, plugins, compilers and processors become generic over the parsed model, and plugins that are stubs today call their real logic.
- **BREAKING**: Processing returns a `VFileTree`, so one input can produce many outputs. A run accepts a file or a directory and produces one parsed model. A separate entry point runs once per file for per-file transforms.
- **BREAKING**: Replace the string-keyed `obj` dictionaries with a typed property bag whose function API works with `|>`, `<|` and `>>`.
- **BREAKING**: Replace `VFile.Content: obj option` with a typed content union of text, bytes or a re-openable stream, so file content does not have to be held in memory. `loadFromDisk` stops reading files up front, and binary files survive a round trip.
- Add tree-level diagnostics, and let a diagnostic name its file and logical location, so failures with no single file have somewhere to go.
- Move the Myriad build task's safety behaviour into the pipeline as one publish operation: path containment, symlink rejection, case-insensitive collision detection, staged all-or-nothing publication, a publication lock, no rewriting of identical files, and manifest-based removal of obsolete files.
- Route Myriad generation through a real pipeline of parse, scope validation, type validation, optimization and F# compilation. **BREAKING** for `Morphir.CodeGeneration.FSharp` callers: `GeneratedModule` and `Diagnostic` are removed. The packaged consumer's build behaviour is unchanged except where optimization simplifies generated source.
- Make the optimizer and type validator safe to run on real models: match SDK functions by fully qualified name, fold only where the result matches generated runtime behaviour, and report only definite type errors.
- Keep the two existing behaviours this builds on: a failed generation still blocks compilation, and the Myriad host still runs once per module.

Out of scope: streaming JSON decoding or module-at-a-time processing, typed plugin chains that change the model type mid-run, IR 4+, document-tree inputs and a Morphir CLI command.

## Capabilities

### New Capabilities

- `pipeline-processing`: Typed parsing, transformation and compilation, run input and output shapes, and diagnostics at file and tree level.
- `typed-property-bags`: Strongly typed, pipeline-friendly properties for file, tree, configuration and processor data.
- `vfile-content-sources`: File content as text, bytes or a re-openable stream, read only when needed.
- `safe-tree-publication`: Writing a file tree to disk without escaping the output root, losing files or exposing partial output.
- `myriad-pipeline-generation`: The classic model to F# path running through a real pipeline with validation and optimization, while keeping its consumer build contract.

### Modified Capabilities

None. The repository has no main OpenSpec specs yet. The unarchived `add-myriad-morphir-fsharp` change owns the consumer build contract. This change keeps that behaviour and supersedes its decision not to route through the object-based pipeline, because the pipeline is no longer object-based.

## Tracking

- Builds on [#376](https://github.com/finos/morphir-dotnet/issues/376), whose work is in commit `1048f93` on this branch and is not yet merged. Both land together as one PR.
- Covers the per-file purpose of [#375](https://github.com/finos/morphir-dotnet/issues/375) through the per-file entry point, with a different shape from that issue's `TreeProcessor`. Reconcile the issue when this lands.
- No new issue exists for the Myriad integration. Link one during implementation if it is created. Do not invent an issue number.

## Impact

- **Code:** `src/Morphir.IR.Pipeline`, `src/Morphir.IR.Pipeline.Plugins`, `src/Morphir.CodeGeneration.FSharp`, `src/Morphir.Myriad` and `src/Morphir.Myriad.Build`, with their test projects.
- **Dependencies:** `Morphir.CodeGeneration.FSharp` gains project references to the pipeline and its plugins. Those assemblies join the `Morphir.Myriad` package's plugin and task folders. No new third-party packages.
- **Public API:** Breaking changes to the pipeline and code generation libraries, none of which appear in a release tag. The `MorphirModel` build item, output layout and manifest location do not change.
- **Docs:** The pipeline README, `docs/content/docs/pipeline/plugin-development-guide.md`, `docs/design/PRD-fsharp-backend.md`, `docs/design/unified-file-architecture.md` and `CHANGELOG.md`.
