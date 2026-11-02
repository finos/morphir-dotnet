# Tasks

Tracking: [#59](https://github.com/finos/morphir-dotnet/issues/59), [finos/morphir#946](https://github.com/finos/morphir/issues/946). Every implementation task follows red, green, refactor: write the failing test named by the spec scenario, then the code. Before each group's final commit, run `dotnet format` and that group's test project.

## 1. Fix and publish Eleven19.IonDotnet

Work in the `ion-dotnet` Orca repo. Publishing is outward-facing, so get the maintainer's approval first.

- [x] 1.1 Make binary string and symbol reads throw on invalid UTF-8, using a strict `UTF8Encoding` in `RawBinaryReader`, and verify with a new test that reads `E0 01 00 EA 82 FF FE` and expects an `IonException`.
- [x] 1.2 Expose `BigDecimal`'s coefficient and scale publicly, and verify with tests on `123.4500`, `1.5d2`, `-0d0` and `0d-3`.
- [x] 1.3 Fix `IonDatagram.IsEquivalentTo`, and verify with a test that two identical `{a:1}` datagrams are equivalent.
- [x] 1.4 Set `PackageLicenseExpression`, correct the README's status, confirm the terms across the two MIT files, and verify by inspecting the packed nuspec.
- [x] 1.5 Publish the new version after approval, and verify the existing suite, including `ion-tests`, passes in CI and NuGet lists the version.

## 2. Bounded, lossless Ion reading

Spec: `ion-document-reading`. Design: ION-1, ION-2, ION-3.

- [x] 2.1 Add `src/Morphir.Ion` and `tests/Morphir.Ion.Tests` to `Morphir.slnx`, pin the package in `Directory.Packages.props`, and verify `dotnet restore --locked-mode` succeeds after updating lock files.
- [x] 2.2 Implement `IonValue`, `IonReadLimits` and the reader, and verify with a test for every scenario in the spec, including text and binary equality on `rich-model`.
- [x] 2.3 Convert every library exception into a typed error at the reader boundary, and verify with the malformed-input scenarios that no exception escapes.
- [x] 2.4 Publish a small `Morphir.Ion` consumer with `PublishTrimmed` and `PublishAot`, using `@skill aot-guru` for any finding, and verify the binary runs and the build reports no IL warnings from `Morphir.Ion` or `Eleven19.IonDotnet`. FSharp.Core reports its own for any AOT F# program.
- [x] 2.5 Vendor the `spec/mep/mck` corpus to `tests/Fixtures/MorphirIon` with a `PROVENANCE.md`, and verify with a test that checks every file's SHA-256 against the corpus manifest.

## 3. Classic distributions and JSON reader

Spec: `classic-ir-distributions`. Design: CLS-1.

- [x] 3.1 Move the decoder from `Decode.fs` to a public `Morphir.Models` reader that returns `Result`, keep `Decode.fs` as an adapter, and verify `Morphir.CodeGeneration.FSharp.Tests` passes with no expected output changed.
- [x] 3.2 Add dependency specification decoding, and verify with `Morphir.Models.Tests` covering opaque, custom and derived types and `literal_out_of_range`.
- [x] 3.3 Add the `Specs` case, make `packageDefinition` return an option, update callers, and verify `dotnet build Morphir.slnx` succeeds and the `Specs` scenarios pass, including the transpiler's refusal.
- [x] 3.4 Record the breaking change in `CHANGELOG.md`, and verify the entry names `Distribution.packageDefinition`.

## 4. IR v4 model and JSON reader

Spec: `ir-v4-model`. Design: V4-1, V4-2, V4-3.

- [x] 4.1 Add strict canonical parsers for names, paths and FQNames, and verify with the initialism and FQName scenarios plus `Name` round-trip tests against decision 0001's examples.
- [x] 4.2 Add the `Morphir.IR.V4` types, `OrderedMap` and `DocumentValue`, and verify with tests for both equality rules and for decimal and float literal identity.
- [x] 4.3 Implement the JSON reader for types, values, patterns and literals, and verify against the kit's accepted JSON spellings extracted from `values.feature` by a checked-in script.
- [x] 4.4 Implement the JSON reader for definitions, specifications, modules and distributions, and verify by reading vendored `complete-example.json` and `books-and-records-example.json` and the refusal scenarios.

## 5. MEP model reading

Spec: `mep-model-reading`. Design: MEP-1. Needs groups 1 to 4.

- [x] 5.1 Implement `ModelDocument.readPayload` and `readFile` with shape checks and retained bytes, and verify the retention, annotation and SHA-256 scenarios on `rich-model.ionb`.
- [x] 5.2 Implement the JSON projection and `decodeIr`, and verify that the IR equals `plain-ir.json`, plus the version-mismatch and non-JSON scenarios.
- [x] 5.3 Run the corpus limit cases `model-0011` to `model-0014` where they apply to decoded reading, and verify each gives its expected code. List the cases left to later MEP work in `PROVENANCE.md`.
- [x] 5.4 Write the `Morphir.Ion` README section on `morphir_model`, and verify its code sample compiles as an Expecto test.

## 6. Ion IR single-file reading

Spec: `ion-ir-reading`. Design: IRF-1 to IRF-4.

- [x] 6.1 Generate JSON and Ion pairs from `website/static/ir/examples` and `daily-rate-v3.json` with the `morphir` v0.4.0-beta.12 release, checking its published SHA-256, vendor them with provenance, and verify the hash test covers them.
- [x] 6.2 Implement header reading, version selection, the `critical` rule and layout normalization with merge rules, and verify the layout, version, critical and merge scenarios.
- [x] 6.3 Implement the v3 decoder, and verify the v3 reference pairs equal the classic JSON reader's output, plus the refusal scenarios.
- [x] 6.4 Implement v4 type and value decoding, including shorthands and attributes, and verify against the Ion spellings from `values.feature`, plus a test that decodes a value nested to exactly the default depth limit without a stack overflow.
- [x] 6.5 Implement v4 definitions, specifications, annotations, document literals and entry points, and verify the v4 reference pairs equal the v4 JSON reader's output.
- [x] 6.6 Add hand-written vectors for v4 collapsed records, marked as such in `PROVENANCE.md`, and verify each equals its datagram form and that the pinned CLI reads each to the same JSON as that datagram.
- [x] 6.7 Document the error codes in the README, marking local draft codes as such, and verify every code in the source appears there with a test that scans both.

## 7. Ion document trees and pipeline parser

Spec: `ion-ir-document-trees`. Design: TREE-1, IRF-4. Needs group 6.

- [x] 7.1 Implement stem and path handling from decisions 0001, 0012 and 0015, and verify with tests for every row of the draft's "names that look like tree files" table and for cut stems.
- [x] 7.2 Implement manifest discovery and tree-to-datagram assembly with one shared limit budget, and verify the manifest, merge, footer and limit scenarios with in-memory `VFileTree` values.
- [x] 7.3 Compare trees from the reference CLI with their single-file forms, and verify results are equal, ignoring module and member order.
- [x] 7.4 Add a `Parser<IrDocument>` that handles Ion files and trees and returns `UnsupportedInput` for anything else, and verify with a `MorphirProcessor` test that a JSON parser after it still runs on JSON input, and that an error reaches the tree's messages with its path and rule.
- [x] 7.5 Document parser usage in the README, and verify its sample compiles as a test.

## 8. Integration

- [x] 8.1 Run `dotnet format` and `./build.sh --target Test`, and verify all tests pass and `Morphir.Ion` and the new `Morphir.Models` code meet the 80% coverage rule.
- [x] 8.2 Read a v3 and a v4 reference distribution from a disk-loaded tree through a pipeline run with stream sources, and verify the end-to-end test passes.
- [ ] 8.3 Confirm every Ion spec issue found during implementation was discussed with the maintainer and corrected both here and in finos/morphir (design SPEC-1), including the `match` row and record shape, and verify `PROVENANCE.md` links each upstream change. Raise an issue as soon as it is found; do not hold it for this task.
- [x] 8.4 Run `openspec validate add-morphir-ion-reading --strict`, and verify it passes.

## Workflow follow-up

- Reconcile [#59](https://github.com/finos/morphir-dotnet/issues/59) when this lands, and leave [#58](https://github.com/finos/morphir-dotnet/issues/58) open for the writer.
- Follow AGENTS.md for review, archive, tracker closure and release approval.
- Later changes: writing Ion, MEP sessions and payload admission, and Ion input to the CLI and F# transpiler.
