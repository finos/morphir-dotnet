# `morphir model convert`: single file and document tree, toward name compaction

Date: 16 November 2026. Branch: `feat/model-convert` in finos/morphir-dotnet.

Origin: [finos/morphir discussion #214](https://github.com/finos/morphir/discussions/214), "Morphir File based layout", and the document-tree intent in finos/morphir (`docs/design/draft/document-tree-protocol/`, `docs/spec/ir/schemas/v3/document-tree-files.md`, `docs/spec/ir/schemas/v4/document-tree-files.md`). The name-compaction work follows `ir/name-compaction` in the parent change `add-bounded-model-access`.

The repository's own design form is an OpenSpec change. Planning turns this document into the change `add-model-convert` (proposal, `design.html`, delta specs, tasks); this file is the approved brainstorming record behind it.

## Intent

#214 asked for a Morphir model stored as a directory of small files that people can browse with ordinary tools, with one format defining both the files and their contents. The parent repository has since specified that layout for IR 3.1.0 and 4 and written compatibility-kit cases for it, but the CLI that actually ships, the .NET `morphir`, can neither read nor write a JSON tree. `compile` refuses `ir.layout = "document-tree"`, and `generate` and `validate` refuse JSON trees.

The first step is a lossless rewrite between the two layouts, exposed as a command and built as a library, so that later work (compile output, generation input, bounded model access for WASM extensions) reuses the same code.

The maintainer also wants each tree file treated as a **Morphir file**: a document with its own format version that can carry context. The first use of context is a file-local naming table that interns long names. Smaller files are part of why trees matter for WASM extensions with tight memory.

## Decisions already made

| Decision | Choice |
| --- | --- |
| Command group | `morphir model` replaces `morphir ir`. A model is the IR plus decorators, metadata and later additions. `morphir ir verify` becomes `morphir model verify`. No `ir` alias: the project is pre-1.0, and the CHANGELOG states the rename. |
| Command | `morphir model convert`. A layout change keeps the version and the content, so it is a conversion, not a migration. `project migrate` and `workspace migrate` stay as they are. |
| Versions and profile | IR 3.1.0 and IR 4, JSON profile only. |
| Build approach | Convert through the typed model (approach A below). |
| Context identifier | A schema URL in `$schema`, following the `https://morphir.finos.org/schemas/<family>/<version>/<document>.schema.json` convention, instead of the `contextVersion` member that `ir/name-compaction` names today. |
| Delivery | One branch with three inspectable milestones that build toward reading and writing compacted files. The maintainer decides after each milestone whether it becomes its own PR. |
| Compaction scope | Whole-file expansion and compaction in memory. Milestones 2 and 3 meet the correctness requirements of `ir/name-compaction`: exact round trip, typed name positions only, the refusals, deterministic output, and the table-size and name-length limits. The bounded requirements stay with `add-bounded-model-access`: resolving one name without building the document, a context placed after the document in bounded memory, and byte pass-through of unknown optional members. |
| Compacted bytes | Compacted files are written in RFC 8785 canonical JSON (JCS): sorted keys, non-ASCII written raw, the JCS number form, no whitespace. The policy measures size as the UTF-8 byte length of that form. The parent's fixed cases are worked out by hand from the policy text and reviewed there; no implementation produces them. Plain tree files keep the typed writers' output and do not adopt JCS in this work. |

## Goals

1. `morphir model convert` converts a single-file model to a document tree and back, for IR 3.1.0 and IR 4 in the JSON profile. A model converted to a tree and back is semantically equal to the original.
2. Every tree the command writes is valid under the parent tree specs and meets the expectations of the kit's tree cases, used as reference data (see Kit independence).
3. The conversion is a library in `Morphir.Models`, usable by `compile`, `generate` and `validate` later.
4. Every file passes through one Morphir-file layer (format version, optional schema URL, optional context, document), so name compaction arrives without changes to the tree code.
5. Milestone 2 reads compacted files and expands them. Milestone 3 writes them under a named, deterministic policy. Compaction applies to both layouts: a single-file model (v3 or v4) can be compacted and stay one file, and every file of a tree can be compacted. `ir/name-compaction` already requires the envelope for single-file distributions and every tree file kind, with classic v3 slots expanding to classic arrays.

## Non-goals

- The YAML profile, and writing Ion trees. (Reading the draft Ion tree stays as it is.)
- Changing a model's IR version, for example from 3 to 4. A later `--ir-version` option on the same command can add it.
- Tree support in `compile`, `generate` and `validate`. The library is shaped for it; wiring it in is follow-up work.
- Bounded or paged reading, snapshots and MEP access sessions (`add-bounded-model-access` groups 2 to 7).
- The daemon side of the document-tree design (VFS manager, propose and commit).
- MoonBit. It adopts the same contracts separately.
- Moving v3 decorator sidecar files. The command converts the distribution document.

## Anti-goals

These would make the work a failure even if the command runs:

- Writing a tree that the parent specs or the kit reject.
- Dropping content silently. Anything the typed model cannot carry is refused with a named diagnostic, or dropped with a notice that names it, never discarded unreported.
- .NET defining the compaction envelope or policy on its own. The parent contract states them first or in the same step, and the .NET tests pin the parent's fixtures.
- Any trim or AOT warning, or a suppression, in the Native AOT `morphir`.

## Approaches considered

| Approach | Trade-off | Outcome |
| --- | --- | --- |
| **A. Through the typed model.** Decode the single file with the existing `ClassicJson` or `V4Json` codecs, write tree files from the typed model, and read a tree back into the same typed model. | The input is checked and there is one source of truth for the model. `compile`, `generate` and `validate` can reuse the reader and writer. Output is canonical, so the input's key order and formatting are not kept byte for byte; equality is semantic, which is what the kit checks. | **Chosen.** |
| B. Move JSON nodes without decoding, as #214 first suggested. | Faster, and keeps each definition's bytes. But tree files are their own format (per-file `formatVersion`, name lists in module files, `fileNames`, access, dependency directories), so the tree parts would be assembled by hand. It duplicates schema knowledge, needs position-based surgery for v3's arrays, can write invalid trees, and nothing else could reuse it. | Rejected. |
| C. Port the Rust tree code (`morphir-common/src/ir_transport/document_tree.rs`, `morphir-core/src/ir/v4/tree_files.rs`) line by line. | Proven behaviour, but its structure does not match the .NET typed models, and the Rust CLI is being retired. | Rejected as an approach. It is the reference for edge cases inside A. |

For compaction, three scopes were considered. 2A leaves room only. 2B also reads compacted files. 2C also writes them. The choice is all three, in that order, as milestones on one branch.

## The command

```text
morphir model convert --input <file-or-directory> --output <path>
                      [--layout document-tree|single-file]
                      [--compact[=<mode>]]         # milestone 3; modes: names
                      [--json[=stream|document]]
```

- The input layout is detected from the input. A file is a single-file model. A directory holding `manifest.json` is a document tree. A directory holding both `manifest.json` and `manifest.yaml` is refused as ambiguous, as the spec requires. Whether the input is compacted is detected from its envelope.
- The output is described by two independent choices: the layout (`--layout`, defaulting to the input's layout) and the encoding (compacted with `--compact`, plain otherwise). Every combination works for IR 3.1.0 and IR 4, so a v3 or v4 single file can be compacted and stay one file, and a compacted single file or tree can be expanded in place of its layout.
- A request that changes neither the layout nor the encoding is a usage error (exit 2) that names the input's state.
- `--compact` takes an optional mode naming which compactions to apply, leaving room for tailored compactions later. This work ships one mode, `names`: name interning under the parent's writer policy. A bare `--compact` means `names`; an unknown mode is a usage error that lists the known ones. Each mode maps to a named parent policy, and the `--json` result records both the mode and the policy, so output stays reproducible if a later release adds modes. Whether a bare `--compact` later widens to more modes is decided when the second mode arrives; the CHANGELOG states any change. A mode is given only with `=` (`--compact=names`), as for `--json` below; `--compact names` is a usage error.
- The IR version comes from the input and does not change. This includes v3 inputs whose version label differs from the tree's, covered below.
- `--json` follows the CLI-wide convention of the companion change `adopt-streaming-json` (below). A bare `--json` streams JSON Lines to stdout: one compact object per line with an `event` member, flushed when the event occurs (`stageStarted`, `diagnostics`, `fileWritten`, `stageCompleted`), ending with exactly one `{"event":"completed","report":{…}}`. `--json=document` writes only the report, as one document. The report holds success, input and output paths, the layouts, the IR version, the compaction mode and policy, the files written and all diagnostics. Stderr stays human-readable in every mode, and nothing but JSON goes to stdout.
- An option value is given only with `=`. A bare `--json` or `--compact` never takes the next argument, so `--json document` is a usage error that suggests `--json=document`, and on commands with positional arguments `--json model.json` keeps `model.json` as the input.
- Exit codes follow the existing commands: 0 on success, 1 when the model is refused, 2 for usage errors.
- `morphir model verify` is today's `ir verify`, unchanged apart from the name.

## How it is built

### The Morphir-file layer

Every document the command reads or writes, whether a manifest, module, type or value file or a whole single-file model, goes through one layer:

```fsharp
type MorphirFile<'doc> =
    { Schema: Uri option          // None for a plain file
      Context: FileContext option // name tables from milestone 2
      Document: 'doc }
```

A plain file has no schema URL and no context. A compacted file is:

```json
{
  "$schema": "https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json",
  "context": { "fqNames": { "a": "my-org/my-project:crm/contact-details#postal-address" } },
  "document": { "formatVersion": 4, "...": "...", "billingAddress": { "$name": "a" } }
}
```

The URL above is illustrative. The parent contract fixes the exact URL, the tables (`names`, `packagePaths`, `modulePaths`, `fqNames`), the `{"$name": "<id>"}` reference form, and the `critical` list. Tree code reads and writes `MorphirFile` values and never sees a name table.

### Document tree types

The model never sees `VFileTree`. Three layers, all in `Morphir.Models`, with only the bottom one referencing `Morphir.Files`:

```fsharp
// 1. Where a document sits in the model: typed Morphir names, never escaped stems or file paths.
type ModuleDocumentAddress =
    | Module of ModulePath
    | TypeDef of ModulePath * Name
    | ValueDef of ModulePath * Name

type DocumentAddress =
    | Manifest
    | Own of ModuleDocumentAddress                         // pkg/…; the manifest names the package
    | Dependency of PackageName * ModuleDocumentAddress   // deps/<package>/@/…

// 2. The model split into decoded, typed documents: no files, bytes, envelopes or name tables.
//    One instance for v3.1 (classic documents) and one for v4.
type DocumentTree<'manifest, 'module, 'typ, 'value> =
    { Manifest: 'manifest
      Documents: Map<DocumentAddress, TreeDocument<'module, 'typ, 'value>> }   // sorted address order

module DocumentTree =
    val split : Distribution -> DocumentTree                 // single file to tree, pure
    val join  : DocumentTree -> Result<Distribution, TreeError>

// 3. The storage binding: the only code that sees Morphir.Files.
module DocumentTreeStore =
    val read  : ReadOptions  -> VFileTree -> Result<DocumentTree * ReadReport, StoreError>
    val write : WriteOptions -> DocumentTree -> VFileTree    // profile, path budget, compaction
```

- `DocumentAddress` is logical identity and maps one to one onto the daemon's `morphir://` URIs. It has no package version: the v4 model carries none, so the store writes the bare `@` segment and refuses a versioned one (decision 0015). A version joins `Dependency` when package versioning lands.
- `DocumentTree` holds decoded, typed documents (decision 10A). `convert` decodes every document to check it anyway, and bounded reading is a separate later reader (3A), so nothing here is lazy.
- `DocumentTreeStore` owns every storage rule: address to path (escaping, path budget, truncation, `fileNames`, dependency segments, extension), the per-document `ClassicJson` or `V4Json` codecs, and the Morphir-file envelope with compaction as a write option. On read it expands compacted files and reports which were compacted. A zip, the Ion tree or the daemon VFS can become further stores without touching layers 1 and 2.
- `convert` is a store read, a `split` or `join`, and a store write; the single-file side uses the existing codecs plus the Morphir-file envelope.

### Tree layout

- The logical paths, name escaping, path budget, truncated stems with `fileNames`, and dependency directories (`deps/<package>/@/…`) follow the v4 tree spec. The v3 tree uses the same layout.
- `src/Morphir.Ion/Ir/TreeStems.fs` already implements escaping, stem parsing, truncation matching and dependency directories for the Ion tree. It moves to `Morphir.Models` and serves the JSON tree, the Ion tree and the conversion, rather than being copied.
- A v4 tree uses the `V4Json` codecs for each definition. A v3 tree holds classic v3 JSON in each `def` or `spec`, written with `ClassicJson`/`ClassicJsonWriter`.
- Modules are written and read in sorted logical-path order, as the v3 spec requires.
- A top-level `$meta` member is reserved: the reader ignores it and the writer never emits one (decision 0014).

### The v3 version label

A v3 tree exists only at 3.1.0, and every v3 tree file must say `"formatVersion": "3.1.0"`. The parent spec and the kit already fix both directions:

- A single-file v3 `Library` (`"formatVersion": 3`) converts to a tree whose files all say `"3.1.0"`.
- A `Library` tree converts back to a single file with `"formatVersion": 3`, as kit cases `document-tree-0011`, `0012`, `0014`, `0015` and `0017` expect.
- A `Specs` tree stays at `"3.1.0"` (`document-tree-0013`).

A `Library` round trip therefore returns the original label, and no relabel notice is needed. `ClassicJsonWriter` writes only integer versions and refuses `Specs` today, so milestone 1 extends it to write a 3.1.0 `Specs` single file.

### Manifest members the single file cannot hold

A v4 tree manifest may carry `version`, `created` and `layout`, and the typed v4 distribution has no place for them. **Builder's call:**

- `layout` and `pathBudget` describe the tree being converted away, so they are dropped. Writing a tree uses a path budget of 4000 (the default profile) unless the caller gives another.
- `version` and `created` are dropped with a notice in the result that names each one.

Either way nothing is lost without the result saying so. The anti-goal reads "refuse or report, never drop in silence".

### Tests and evidence

- Red, green, refactor: each behaviour starts as a failing test.
- The kit's tree cases (`spec/ir/mck/document-tree.feature`, 17 cases covering v3 and v4, and the tree cases in `distributions.feature` and `versions.feature`) are pinned as **reference data** from one finos/morphir commit, with a `PROVENANCE.md` and hash checks, as `tests/Fixtures/MorphirIon/` does. Their inputs, canonical spellings and named diagnostics feed .NET-owned scenarios; the kit's runner, step grammar and adapter protocol are not used.
- Most kit cases are spelled in the YAML profile. `FixtureCatalog` translates each YAML spelling to JSON with a YAML parser used only in tests. Both profiles hold the same values (kit case `document-tree-0006`), so the product gains no YAML support.
- Round-trip property: for the kit's single-file models and the repository's existing IR fixtures, `single-file → tree → single-file` gives a model equal to the input, at 3.1.0 and 4.
- Refusals each have a test and a named diagnostic: an ambiguous profile, a v3 manifest that is not `"3.1.0"`, an unknown member, an over-budget path, a missing module or definition file, a file outside `pkg/` and `deps/` that claims to be part of the tree, and an output directory that already holds files.
- The black-box CLI tests cover `model convert` and `model verify` against the published executables, in the existing `tests/Morphir.BlackBox.Tests` project.
- The Native AOT publish of `morphir` keeps zero IL2026 and IL3050 warnings.

### Acceptance and BDD coverage

Every requirement in this design is an OpenSpec requirement with at least one `WHEN`/`THEN` scenario, and every OpenSpec scenario has an executable test **with the same name**. Where the `test-manager` skill classifies the test as integration, journey or conformance, that test is a Gherkin scenario; where it classifies it as unit or property (a law over generated inputs, a corpus loop), it is an Expecto or TUnit test, and the OpenSpec scenario names it. Each change's `tasks.md` ends with a traceability table (OpenSpec scenario → feature file and scenario, or test name), and no milestone is done while a row is empty. The `test-manager` skill is used before adding any test, feature file, step or driver, and the `test-manager-reviewer` agent reviews each milestone's diff.

**OpenSpec capabilities.**

| Change | Capability | Covers |
| --- | --- | --- |
| `add-model-convert` | `virtual-files` (new) | `Morphir.Files`: `VFileTree` as `IFileProvider`, building from a provider, the `netstandard2.0` target |
| `add-model-convert` | `model-document-tree` (new) | addresses, `split`/`join`, store rules (paths, escaping, budget, `fileNames`, `@` segment, version labels, `$meta`), the Morphir-file envelope, compaction read and write, JCS |
| `add-model-convert` | `cli-model-conversion` (new) | `model convert`: detection, layout and encoding choices, refusals, exit codes, no partial output, `--compact[=<mode>]` |
| `add-model-convert` | the existing `ir verify` spec, modified | the `model` group, `model verify`, `ir` removed |
| `adopt-streaming-json` | `cli-json-output` (new) | `--json[=stream\|document]` on every command, the record shape, values only with `=` |
| `adopt-streaming-json` | `cli-model-compilation` and the other specs that define `--json`, modified | one report becomes the `document` mode; compile's `--json-lines` removed |

**Kit independence.** The parent kit (`spec/ir/mck`) is due for a redesign because it is tied to the Rust implementation (its runner, step grammar, adapter protocol and CI gates), so this delivery depends on none of that. The kit's cases are reference data only: `ModelConversionKitCases.feature` (black-box, below) has one .NET-owned scenario per kit tree case, named with the kit id (`document-tree-0011 …`), whose expectations come from the pinned kit data through `FixtureCatalog`. The parent's compaction contract (tasks 1.1 and 1.4) is published as plain spec artifacts, a JSON Schema plus example files and hand-derived expected bytes under `docs/spec` and `website/static/schemas`, not as kit scenarios; the kit redesign can absorb both later without changing the product.

**Black-box integration (Gherkin), `tests/Morphir.BlackBox.Tests/Integration/`, `@integration`.** A new capability `@needs-model-conversion` with its driver interface and `ConversionReport`, per rule 10; the feature files name intents, never command lines.

- `ModelConversion.feature` (milestone 1):
  - a v4 single file becomes a tree, and the tree becomes a single file equal to the original;
  - a v3 `Library` round trip keeps `"formatVersion": 3`, and a v3 `Specs` tree stays `"3.1.0"`;
  - a v4 tree manifest's `version` and `created` are dropped with notices naming them;
  - the input layout is detected; a directory with both `manifest.json` and `manifest.yaml` is refused as ambiguous;
  - a request that changes neither layout nor encoding exits 2 naming the input's state;
  - each refusal (unknown member, over-budget path, missing module or definition file, file outside `pkg/` and `deps/`, non-empty output directory, versioned `deps/` segment) exits 1 with its named diagnostic and leaves no output;
  - a compacted input is refused with `model.unsupported_context` (until milestone 2 replaces this scenario).
- `ModelConversionKitCases.feature` (milestone 1): one scenario per pinned kit tree case, named with its kit id. Each converts the case's input through the driver and checks the result against the kit's canonical spelling or named diagnostic, read from `FixtureCatalog.Kit`. The scenarios are .NET-owned: when the kit is redesigned, only the catalog's source changes.
- `ModelConversionCompaction.feature` (milestones 2 and 3):
  - compacted v3 and v4 single files and compacted trees expand to plain output in either layout;
  - `--compact` keeps a v3 or a v4 single file as one compacted file, and compacts a tree file by file;
  - a bare `--compact` uses mode `names`, and the result names the mode and the policy;
  - `--compact=bogus` exits 2 listing the known modes; `--compact names` exits 2 suggesting `--compact=names`;
  - a context after the document, an unknown alias, a duplicate table entry and an over-limit table or name are each refused with a named diagnostic.
- `ModelVerification.feature` (milestone 1): its scenarios run `model verify`; a new scenario shows that `ir verify` is no longer a command. The `@needs-model-verification` description in `TestEligibility.cs` and the skill names `model verify`.
- `JsonOutput.feature` (`adopt-streaming-json`), with scenario outlines over the commands:
  - a bare `--json` writes only JSON Lines to stdout, each line with an `event`, ending with exactly one `completed` record;
  - `--json=document` writes exactly one JSON document, equal to the `completed` record's `report`;
  - `--json model.json` keeps `model.json` as the input; `--json document` on a command without positional arguments exits 2 suggesting `--json=document`;
  - stderr stays human-readable and stdout never holds anything but JSON;
  - compile's `--json-lines` is no longer an option.

**In-process command tests (Gherkin), `tests/Morphir.Tooling.Tests/Features/ModelConvert/ModelConvert.feature`.** The timing and stream laws that need the command in the test process: each record is flushed before the next stage starts (a slow fake store), `fileWritten` records name every file the result lists, and the diagnostics of all records equal the report's.


**Unit and property tests (Expecto), `tests/Morphir.Models.Tests` and a new `tests/Morphir.Files.Tests`.**

- Laws over generated models and the repository's IR fixtures, at 3.1.0 and 4: `join (split d) = d`; `read (write t) = t`; `expand (compact f) = f`; compacting twice gives the same bytes; address to path is one to one within the path budget.
- JCS against the RFC 8785 examples, and the size measure on a non-ASCII name.
- Architecture tests: only `DocumentTreeStore` refers to `Morphir.Files` types, and `DocumentAddress` holds only `PackageName`, `ModulePath` and `Name` values.
- `Morphir.Files`: the moved `VFileTree` tests, `IFileProvider` contract tests (existing and missing files, directory listings, a no-op `Watch`), and building a tree from a physical and an embedded provider. The tests run on `net11.0`; the `netstandard2.0` target is held to a zero-warning build, as `Morphir.Models` is today.

### Test structure: drivers and object mothers

Tests never reach the product or a fixture directly. Two patterns give them structure:

**Test drivers** state what a test does, in the language of the domain, and own how it happens.

| Driver | Where | Used by |
| --- | --- | --- |
| `IModelConversionDriver` / `ModelConversionDriver`, with `ConversionOptions` and `ConversionReport` | `tests/Morphir.TestSupport/Drivers/`, beside `ModelVerificationDriver`, with tests in `tests/Morphir.TestSupport.Tests/Drivers` | black-box `ModelConversion*.feature` (`ConvertAsync(model, layout, compact)`; the command line is built only here) |
| `ModelVerificationDriver` | existing; now builds `model verify` | `ModelVerification.feature` |
| `JsonOutputAssertions` | `tests/Morphir.TestSupport/Drivers/`, beside `JsonCommandReport` | `JsonOutput.feature` and every report check: parses the stream, checks one last `completed`, returns the report, so no step parses stdout itself |
| An in-process `ModelConvertCommandDriver` with a controllable fake store | `tests/Morphir.Tooling.Tests/Features/ModelConvert/` | the timing and stream laws |
| `DocumentTreeStoreDriver` | `tests/Morphir.Models.TestSupport` | unit tests: writes a tree to a `VFileTree`, reads it back, and reports files and diagnostics by `DocumentAddress`, so a test never assembles paths |

**Object mothers** are the only source of models, trees and files, each named for the case it represents:

- `DistributionMother` (F#, in a new `tests/Morphir.Models.TestSupport` library that `Morphir.Models.Tests`, `Morphir.Files.Tests`, and `Morphir.Tooling.Tests` reference). It has named cases such as `v4LibraryWithOneModule`, `v4LibraryWithDependency`, `v3Library`, `v3Specs`, `nameUsedOnce`, `nameUsedOften`, `nonAsciiName` and `pathAtTheBudget`, built on a test-data builder (`aV4Library |> withModule … |> withDependency …`) for the variations a single test needs.
- `DocumentTreeMother` and `MorphirFileMother`: plain and compacted trees and files derived from `DistributionMother` cases, including the invalid shapes behind each refusal (`withUnknownMember`, `withMissingDefinitionFile`, `withVersionedDependencySegment`, `withContextAfterDocument`).
- `FixtureCatalog` (C#, `tests/Morphir.TestSupport`): the one owner of files on disk. Kit pins, the parent's examples and fixed bytes, and the repository's existing IR fixtures are reached by intent (`FixtureCatalog.Kit.DocumentTree`, `FixtureCatalog.Models.V3Library`), each with its `PROVENANCE.md` entry and hash. Black-box tests materialise mother cases through it into the scenario's workspace.
- The FsCheck generators for the property laws live beside the mothers and use the same builder, so generated and named cases share one shape.

Rules the review checks:
- A feature file names a case by intent (`Given a v3 Library model with a dependency`), never a path or inline JSON. A step maps the phrase to a mother case.
- Inline JSON appears in tests only where the kit or the parent's policy defines the exact bytes, and then it is read from `FixtureCatalog`, not pasted.
- Each fixture file has exactly one owner in `FixtureCatalog` and one provenance entry; an unowned file under `tests/Fixtures` fails a guard test.
- The existing `ModelVerification.feature` moves from fixture paths to the same intent phrases when it is renamed.

## Milestones

Each milestone ends with a commit on `feat/model-convert` that builds and passes its tests, plus a short note in the OpenSpec change saying what to try. The maintainer decides after each whether it ships as a PR.

### Step zero: extract `Morphir.Files`

- `PropertyBag`, `VFile` and `VFileTree` (`Property.fs`, `File.fs`, `FileTree.fs`, about 1,640 lines) move out of `Morphir.IR.Pipeline` into a new library, `Morphir.Files`, targeting `$(MorphirLibraryTargetFrameworks);netstandard2.0`. `Morphir.IR.Pipeline` keeps the processor and pipeline builder and references it, as do `Morphir.Models` and the current consumers (`Morphir.Ion`, `Morphir.Tooling`, `Morphir.CodeGeneration.FSharp`, `Morphir.IR.Pipeline.Plugins`).
- The `netstandard2.0` port follows the `Polyfill.fs` and `#if NETSTANDARD2_0` pattern of `Morphir.Models`. A trial build found under 20 distinct gaps (`ReadOnlyMemory`, `ImmutableDictionary`, `HashCode`, the three-argument `File.Move`, `ReadAtLeast`, `DirectoryInfo.LinkTarget`, `OperatingSystem.IsWindows`, `Path.TrimEndingDirectorySeparator`, `Convert.ToHexString`, char overloads). `System.Memory` and `System.Collections.Immutable` come from the compiler and MSBuild hosts.
- `VFileTree` implements `IFileProvider` (`Microsoft.Extensions.FileProviders.Abstractions`, already in the dependency graph): `GetFileInfo`, `GetDirectoryContents`, and a no-op `Watch`. A `VFileTree` can also be built from any `IFileProvider` (physical, embedded, composite). Writing stays on `VFileTree`, since `IFileProvider` is read-only. `System.IO.Abstractions` is not used: it is third-party and models a mutable disk.
- Existing tests move with the code and pass on `net11.0`; the `netstandard2.0` build has zero warnings.

### Companion change: `adopt-streaming-json`

A separate morphir-dotnet OpenSpec change, so the convention is reviewed on its own. `model convert` is built on it from milestone 1 and never ships the single-document default.

- `--json[=stream|document]` on every command; a bare `--json` streams. A command with nothing to stream writes only its `completed` line, which is also one valid JSON document.
- Compile's `--json-lines` is removed and its event stream moves under `--json`. The conformance adapter (`src/Morphir.Conformance/CliAdapter.fs`) and the specs and docs that say `--json` writes one report are updated. The CHANGELOG states the change (pre-1.0, no alias).
- Values only with `=`: the CLI rewrites a bare `--json` (before any `--`, and after response-file expansion) to `--json=stream` before parsing. Probed against System.CommandLine 2.0.15: without the rewrite, `--json model.json` takes `model.json` as the JSON mode and the input goes missing; with it, `model.json` stays the input, and on a command without positional arguments `--json document` fails as an unrecognized argument. Tests pin these cases.
- A finos/morphir change makes `tools/cli-release.ts` read the `completed` line, landing with the pin move that carries the change.

### Milestone 1: trees (2A)

- `morphir model` group; `ir verify` becomes `model verify`. In this repository that covers the docs pages and `tests/Morphir.TestSupport/Drivers/ModelVerificationDriver.cs`.
- **Builder's call, a companion finos/morphir change:** the parent's release qualification runs `morphir ir verify` as its `packaged-input` check (`tools/cli-release.ts`), and `INSTALLING.md` and `docs/getting-started/morphir-cli.md` name it. The change updates all three and lands together with the next move of the morphir-dotnet pin that includes the rename. Otherwise that check fails on every platform.
- `morphir model convert` for v3.1 and v4 JSON, both directions, plain files.
- The `MorphirFile` layer, with `Schema` and `Context` always empty.
- A compacted input is refused with `model.unsupported_context`, naming the file and its schema URL.
- Shared tree-stem code; kit fixtures pinned; round-trip and refusal tests; black-box tests; CHANGELOG entry.

To try it:

```text
morphir model convert --input morphir-ir.json --output tree --layout document-tree --json
morphir model convert --input tree --output roundtrip.json --layout single-file --json
```

### Milestone 2: read compacted files (2B)

- **Parent first:** a finos/morphir branch edits `ir/name-compaction` to identify the envelope by schema URL instead of `contextVersion`. It also completes task 1.1: the envelope grammar, the tables, the `$name` form and `critical`, with its JSON Schema published under `website/static/schemas` and valid and invalid examples. The same edit marks which `ir/name-compaction` requirements a whole-file reader and writer meet and which belong to bounded reading, and records tasks 2.2 and 2.3 of `add-bounded-model-access` as partly done when milestones 2 and 3 land.
- The .NET branch pins those examples, with provenance, from that branch's commit, and moves the pin when the parent merges.
- **.NET:**
  - `MorphirFile` reads the envelope.
  - `{"$name": …}` resolves only at positions the schema defines as names, never inside strings, docs or literals.
  - An unknown alias, a duplicate table entry, an unknown `critical` path, a table over its entry limit or an expanded name over its length limit is refused with a named diagnostic.
  - The whole file is read and expanded in memory. A context placed after the document is refused with a named diagnostic, not spilled; bounded reading adds it later.
  - Context bytes count toward the input limits.
  - `convert` writes the expanded result, so a compacted tree becomes a plain tree or a plain single file, and a compacted single file becomes a plain single file or a plain tree.
  - v3 and v4 single files and every tree file kind are read: a classic v3 FQName slot expands to classic package, module and name arrays, a v4 slot to the canonical FQName string, and the enclosed `formatVersion` is never relabelled.

To try it (with the parent's examples):

```text
morphir model convert --input compacted-v3.json --output plain-v3.json --json
morphir model convert --input compacted-tree --output plain.json --layout single-file --json
```

### Milestone 3: write compacted files (2C)

- **Parent:** task 1.4 on the same finos/morphir branch adds the named writer policy (working name `morphir.compact/0.1.0-draft.1`) and its fixed cases. The policy states that compacted output is RFC 8785 JCS and that every size comparison uses its UTF-8 byte length. The expected bytes are derived by hand from the policy and reviewed in the parent PR. They cover:
  - a v3 single file, a v4 single file, and v3 and v4 tree files of each kind (manifest, module, type, value);
  - which names get aliases, net of table overhead, so a name used once stays spelled out;
  - a non-ASCII name, so the byte count of the raw UTF-8 form is pinned;
  - how alias ids are allocated, in first-use order;
  - how ties break, and the order of the tables;
  - the expected bytes, including literals that look like aliases and the case where the overhead makes compaction a loss. Cases for a context after the document and for unknown-member pass-through belong to bounded reading and are not part of this milestone.
- **.NET:**
  - A small JCS writer in `Morphir.Models`, AOT-safe, with tests against the RFC 8785 examples.
  - `--compact` (mode `names`) writes compacted files under that policy, matching the fixed expected bytes byte for byte.
  - `--compact` works with either layout: a single file stays one file, and a tree is compacted file by file with tables local to each file.
  - `expand(compact(file))` equals the original for every fixture, single files and tree files alike.
  - The result document names the mode and the policy.
  - Tests cover a bare `--compact`, `--compact names`, `--compact=names`, and an unknown mode refused with exit 2.

To try it:

```text
morphir model convert --input morphir-ir.json --output compacted.json --compact --json
morphir model convert --input morphir-ir.json --output tree --layout document-tree --compact --json
```

## Constraints

- Built with the .NET 11 SDK pinned in `global.json` (`11.0.100`), using the target-framework properties in `Directory.Build.props` rather than literal frameworks. The `morphir` executable and the tests target `$(MorphirCurrentTargetFramework)` (`net11.0`). The tests run on `net11.0`, as the repository's tests do.
- The new model code goes in `Morphir.Models`, which ships `$(MorphirLibraryTargetFrameworks);netstandard2.0` (`net11.0;net10.0;netstandard2.0`). It keeps `netstandard2.0` because the C# Roslyn source generator and the MSBuild task load it into the compiler or MSBuild process. The code therefore:
  - builds for all three targets, using `Polyfill.fs` and the `#if NETSTANDARD2_0` pattern the project already uses. `TreeStems` moves in from `Morphir.Ion` and needs two such shims: `SHA256.HashData` and `Convert.ToHexString` are .NET 5 and later.
  - stays free of direct file-system calls. The tree reader and writer work over `VFileTree` from `Morphir.Files` (step zero below), and `Morphir.Tooling` loads it from disk and writes it back. This keeps disk access out of code paths a compiler host runs, and matches the document-tree design's virtual file system.
  - is checked for trimming and Native AOT on its `net11.0` and `net10.0` targets, where the project already runs those analyzers.
- F# for the model layer, C# for the command where the existing CLI code is C#. F# code called from C# follows the `fsharp-csharp-interop` skill and gets its reviewer agent.
- No Rust code changes, in finos/morphir or anywhere else. The Rust tree code is a reference for edge cases only. The parent-side work is specs, schemas, example files, docs and `tools/cli-release.ts` (TypeScript). It adds no kit cases, so the parent's Rust gates are untouched.
- Typed errors, immutable models, no new runtime dependency in the product for YAML.
- Repository PR rules apply: link the GitHub issue, no AI co-author lines, and explicit approval before merge.
- PR size: each milestone aims to be reviewable on its own, about five hours of work, even when several ship in one PR.
- The work can be split across up to four agents. The plan splits by milestone and part: tree layout, v4 files, v3 files, the command and black-box tests, and the parent contract.

## Left to the builder

- Module placement inside `Morphir.Models`, and the names of types and functions beyond `MorphirFile`.
- The exact diagnostic codes, beyond `model.unsupported_context`, following the existing `snake_case` and dotted conventions.
- The shape of the `--json` result beyond the members listed above.
- Whether `convert` writes to a temporary directory and renames it into place, as long as a failed run leaves no partial output at `--output`.
- Which existing IR fixtures beyond the kit join the round-trip test set.
