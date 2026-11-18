# Proposal

## Why

A Morphir model is one large JSON file today. finos/morphir discussion #214 asked for the same model as a directory of small files (a document tree) that people can browse with ordinary tools and that a WASM extension with tight memory can read one piece at a time. The parent repository has since specified those trees for IR 3.1.0 and IR 4, and a name-compaction encoding that interns long names per file, but morphir-dotnet can neither write nor read them: `compile` refuses `ir.layout = "document-tree"`, and `generate` and `validate` refuse JSON trees.

This change gives the .NET `morphir` CLI the first working path:

```console
$ morphir model convert --input morphir-ir.json --output tree --layout document-tree --json
$ morphir model convert --input tree --output roundtrip.json --layout single-file --json
$ morphir model convert --input morphir-ir.json --output compacted.json --compact --json
```

It also renames the `ir` command group to `model`, because a Morphir model is more than its IR (decorators, metadata and later additions).

## What Changes

- **BREAKING:** the `morphir ir` command group is replaced by `morphir model`. `morphir ir verify` becomes `morphir model verify` with the same behavior; there is no `ir` alias (pre-1.0).
- `morphir model verify` checks every stored form of a model: single files at IR versions 1 to 3 as today, plus IR v4 single files, v3 and v4 document trees (per-file schemas where the parent publishes them, the tree rules, and each document), and compacted files after expansion.
- New command `morphir model convert`: converts a model between a single file and a document tree, in both directions, for IR 3.1.0 and IR 4 in the JSON profile, keeping the IR version. `--layout document-tree|single-file` chooses the layout and `--compact[=<mode>]` the encoding (mode `names`); option values are given only with `=`.
- Name compaction: every file of a model, single file or tree file, can carry a Morphir-file envelope (a schema URL, a file-local naming table, the document). The command reads compacted files (expanding them) and writes them under the parent's named policy, in RFC 8785 canonical JSON.
- New model-layer types for document trees: typed document addresses, a document tree of decoded typed documents with `split` and `join`, and a store that is the only code mapping a tree to files.
- `VFile`, `VFileTree` and `PropertyBag` move out of `Morphir.IR.Pipeline` into a new library, `Morphir.Files`, that also targets `netstandard2.0` and interoperates with Microsoft's `IFileProvider`.
- The classic writer gains a declared 3.1.0 writer for `Specs` distributions, which a v3 `Specs` tree converts back to.
- Test structure: a model-conversion test driver and capability, object mothers and one fixture catalog as the only source of test models, trees and fixture files, and a traceability table from every OpenSpec scenario to its test.
- No Rust code changes anywhere. The parent's compatibility kit is used as pinned reference data only.

## Capabilities

### New Capabilities

- `virtual-file-providers`: the virtual file library as a reusable package on every Morphir target, including `netstandard2.0`, and its interoperation with Microsoft's `IFileProvider` in both directions.
- `model-document-trees`: typed document addresses, the document tree of a model, `split` and `join`, and the storage rules that map a tree to and from files.
- `model-name-compaction`: the Morphir-file envelope, reading compacted files, writing them under a named policy, and canonical bytes.
- `cli-model-conversion`: the `morphir model convert` command.
- `cli-model-commands`: the `morphir model` command group and `morphir model verify`, replacing `morphir ir`, including verification of IR v4 single files, document trees and compacted files.

### Modified Capabilities

- `ir-model-writing`: a declared writer for classic format version `3.1.0` `Specs` distributions.
- `test-driver-contract`: the verification report scenario names `model verify`, and the model-conversion capability gets its driver and report.
- `test-authoring-guidance`: the skill's example names `model verify`, and test data comes from object mothers and one fixture catalog.

## Tracking

- Origin: finos/morphir discussion #214, "Morphir File based layout", and the parent change `add-bounded-model-access` (capability `ir/name-compaction`, tasks 1.1, 1.4, 2.2 and 2.3).
- Design record: `docs/superpowers/specs/2026-11-16-model-convert-design.md`.
- A morphir-dotnet GitHub issue is filed and linked before the first pull request.
- Companion changes: `adopt-streaming-json` in this repository; in finos/morphir, the name-compaction contract edits (schema URL instead of `contextVersion`, envelope examples, the writer policy and its fixed bytes) and the `model verify` update to `tools/cli-release.ts`, `INSTALLING.md` and `docs/getting-started/morphir-cli.md`.

## Impact

- Code: `src/Morphir.Models` (document trees, store, envelope, compaction, canonical JSON, the 3.1.0 `Specs` writer, `TreeStems` moved from `Morphir.Ion`); new `src/Morphir.Files`; `src/Morphir.IR.Pipeline` (keeps the processor and builder); `src/Morphir.Ion`, `src/Morphir.Tooling`, `src/Morphir.CodeGeneration.FSharp`, `src/Morphir.IR.Pipeline.Plugins` (references); `src/Morphir.Tooling` and `src/Morphir` (the `model` group and `convert`).
- Tests: new `tests/Morphir.Files.Tests` and `tests/Morphir.Models.TestSupport`; `tests/Morphir.Models.Tests`, `tests/Morphir.Tooling.Tests`, `tests/Morphir.TestSupport` (drivers, fixture catalog), `tests/Morphir.BlackBox.Tests` (new and renamed features).
- Dependencies: `Microsoft.Extensions.FileProviders.Abstractions` (already in the dependency graph) becomes a direct reference of `Morphir.Files`.
- Users: scripts that call `morphir ir verify` must call `morphir model verify`. The parent's release qualification changes with the pin move that brings the rename.
- Native AOT: zero trim and AOT warnings in `morphir`.
