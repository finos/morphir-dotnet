# Proposal

## Why

The Morphir Extension Protocol (MEP) `0.2.0-draft.1` carries rich models as binary Ion, and 137 of the 139 `irVersion` entries in its reference cases are `4.0.0`. morphir-dotnet cannot read Ion at all and has no IR v4 model, so it cannot act as a MEP host or extension for those models. Reading comes first because every later step, including negotiation and generation, needs a checked model to work on.

## What Changes

- Read Ion, text or binary, into a new lossless immutable value type, under limits on bytes, depth and nodes. Malformed input returns a typed error.
- Read the Ion IR format `ionVersion` `0.1.0-draft.1` from a single file, as a datagram or a collapsed `morphir::` record, for IR `3.0.0`, `3.1.0` and `4.0.0`.
- Read the Ion document tree (`manifest.ion`, `module.ion`, `*.type.ion`, `*.value.ion`) from a `VFileTree`.
- Read the MEP `morphir_model` document. Keep the original bytes, `enrichments` and `provenance`, and decode `ir` into the typed model for its IR version.
- Add a typed IR v4 model to `Morphir.Models`, with a JSON reader, so a v4 document read from Ion can be compared with the same document read from JSON.
- Move the classic JSON decoder out of `Morphir.CodeGeneration.FSharp` into a public `Morphir.Models` reader that also decodes dependencies, keeping the transpiler's existing diagnostics.
- **BREAKING**: Add a `Specs` case to the classic `Distribution` for IR `3.1.0`. `Distribution.packageDefinition` returns an option. No release tag contains this type.
- Fix the `Eleven19.IonDotnet` defects listed under Impact, publish a new package version, and depend on it through central package management.

This change does not write Ion, negotiate MEP sessions, handle JSON-RPC, check payload base64 or digests, check evidence, or add an MCK adapter. The CLI and the F# transpiler do not read Ion yet.

## Capabilities

### New Capabilities

- `ion-document-reading`: Lossless, bounded reading of Ion text and binary into immutable values, with typed errors.
- `ion-ir-reading`: Reading a single-file Ion IR distribution into typed classic or v4 IR, with the refusals the draft names.
- `ion-ir-document-trees`: Reading an Ion document tree, with the path supplying package, module and name.
- `mep-model-reading`: Reading and retaining a `morphir_model` document, with a typed view of its IR.
- `ir-v4-model`: The immutable IR v4 semantic model and its JSON reader.
- `classic-ir-distributions`: The classic `Specs` distribution and a public classic JSON reader.

### Modified Capabilities

None. The existing transpiler and pipeline requirements do not change. The Ion parser plugs into the existing `Parser<'T>` contract.

## Tracking

- Partly covers [finos/morphir-dotnet#59](https://github.com/finos/morphir-dotnet/issues/59), which asks for an Ion reader but predates the Morphir Ion format. Reconcile the issue when this lands. [#58](https://github.com/finos/morphir-dotnet/issues/58) asks for a writer and stays open.
- Implements the reader side of [finos/morphir#946](https://github.com/finos/morphir/issues/946) for .NET. The contract is `docs/design/draft/ir/ion.md` and `spec/mep/model-transport.md` in finos/morphir.
- MEP runtime work follows [finos/morphir#965](https://github.com/finos/morphir/issues/965) and [#967](https://github.com/finos/morphir/issues/967). No morphir-dotnet issue exists for MEP support. Link one if it is created. Do not invent an issue number.

## Impact

- **Code:** A new `src/Morphir.Ion` project and `tests/Morphir.Ion.Tests`. New `IR/V4` and JSON reader modules in `src/Morphir.Models`. Classic decoding moves out of `src/Morphir.CodeGeneration.FSharp/Decode.fs`.
- **Dependencies:** `Morphir.Ion` adds `Eleven19.IonDotnet`. `Morphir.Models` gains no new dependency. Lock files change for projects that reference `Morphir.Ion`.
- **Public API:** New `Morphir.Ion` and `Morphir.IR.V4` namespaces. A breaking change to the classic `Distribution`.
- **`Eleven19/ion-dotnet`:** A probe of `Eleven19.IonDotnet` 0.1.0 read all the Morphir fixtures it was given and handled annotations, big integers, decimal scale, duplicate fields and symbol escapes correctly. It also found these gaps:
  - Invalid UTF-8 in a binary string becomes U+FFFD with no error.
  - `BigDecimal` keeps its coefficient and scale internal.
  - `IonDatagram.IsEquivalentTo` returns false for two identical `{a:1}` datagrams.
- **Licensing:** The 0.1.0 nuspec has no licence element, because `PackageLicense` is not an MSBuild property NuGet reads. The repository holds two MIT files with different copyright holders. Set `PackageLicenseExpression` and confirm the terms before FINOS takes the dependency.
- **Docs:** A `Morphir.Ion` README, `CHANGELOG.md`, and a provenance file for vendored fixtures.
