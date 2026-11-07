# Proposal

## Why

Morphir .NET can read every IR version and generate F# and C# from a model, but it cannot produce a model from source. There is no frontend, no `morphir compile` command and no complete IR writer for any version. This steel thread adds the first end-to-end path: F# source to Morphir IR, then optionally to generated code. It builds the parts that the coming Morphir Extension Protocol (MEP) integration will reuse. It is also the first proof that morphir-dotnet can provide the default Morphir CLI and host the Morphir Compatibility Kit (MCK), the conformance oracle that the Rust-based tooling now has with known flaws.

## What Changes

- Add an F# source frontend for a defined core subset of F#: namespaces and modules; type aliases, records, unions and generics; literals, `let`/`let rec`, lambdas, application, `if`, `match` with the core patterns, tuples, lists, records and record update; `option`/`Result`/`list`; and SDK-mapped operators and pipes.
- Make the frontend refuse every construct outside that subset with a specific `fsharp.*` error diagnostic, with a source range in the MEP diagnostic shape. Refused constructs include `#if` conditional compilation and `.fsi` signature files. When the frontend refuses, it produces no model.
- Add complete IR writers for the classic format versions 1, 2 and 3 and for IR v4. Each writer must round-trip through the existing readers. Format versions are full Morphir format versions, never bare integers:
  - integer classic versions;
  - SemVer releases;
  - SemVer pre-releases such as `4.1.0-draft.1`.

  A writer serves only the exact versions it declares.
- Lower once and build each family natively. The frontend's lowering is written once, against an IR builder interface. Classic and v4 each implement that interface natively, so v3 is never derived from v4 and no stage converts between families. Content a family cannot hold is refused with `morphir.unrepresentable`. Classic models still go directly to the backends' classic paths.
- Add language-neutral **project-file sources**. A build-tool project file named by the `--project-file` flag, or by the `[frontend.<language>].project_file` key of the same name, expands to an ordered source set. Every language table uses the same key. `--input` keeps its ecosystem meaning (files or one directory). The first provider evaluates any MSBuild project (`.fsproj`, `.csproj` and others) with `dotnet msbuild -getItem:Compile`. It never builds or restores. The provider runs only when the user names a project file; the command never finds one by itself. The result is the ordered source set that MEP already defines, so no frontend or protocol changes for it.
- Add `morphir compile` with the ecosystem command shape:
  - `--language`;
  - `--input`: files or one directory;
  - `--project-file`: one build-tool project file;
  - `--package-name`;
  - `--ir-version` with any format version, default `4`;
  - `-o/--output`, `--json`, `--project` and `--config`.

  Settings come from the existing project discovery (`project.*`, `ir.format_version`, `frontend.*`). CLI values take precedence.
- Write the ecosystem compile output and task record (`<out>/compile.dest/` plus `compile.json`, including tombstones and locks). Then `morphir generate` without `--input` reads the IR of the most recent compile. **This reverses** the `add-generate-command` exclusion of implicit prior-compile input.
- Add `morphir compile --target <id> --generate-output <dir>`. It sends the compiled model in memory to the registered generation backend, the same backend `morphir generate` uses. The result must equal `compile` followed by `generate --input`.
- Run compilation as a stream of events (stage started, diagnostics reported, stage completed, completed). The single report is the fold of that stream. Human mode writes each diagnostic to stderr as its stage finishes.
- Add `--json-lines` to `morphir compile` with the flag's intended meaning: one compact JSON record per line on stdout, written as each event occurs, ending with one `completed` record that holds the full `--json` report. `--json` stays one report, and MEP is unchanged. Watch mode, a daemon and editor integrations can later use the same stream. The Rust CLI currently writes one compact report line for `--json-lines`. That seems to be an incorrect implementation, so this change sets the intended behavior and asks upstream to align.
- Write the frontend's conformance corpus as the first **Morphir Compatibility Kit** suite (`conformance/suites/frontend-fsharp/`). Its cases and expectations are written independently and do not depend on .NET types. A runner checks any implementation through `library`, `cli` and `mep-guest` adapters, and writes reproducible verdict reports. The case data is the oracle, not any implementation. Upstream transcripts and examples are reviewed inputs, and where upstream tools are wrong, the kit is the reference and the deviation is recorded with evidence.
- Put frontends behind one contract with two adapters. The selection rules are the same for both:
  1. **First:** an in-process F# frontend, registered only in the JIT `dotnet-morphir` tool. It needs no MEP and lets the F# frontend mature first.
  2. **Second:** a MEP frontend adapter for the latest upstream contract, `0.2.0-draft.1`, and only that version. It uses `morphir.frontend.compileModel` with the JSON `morphir-ir`/`morphir-json` tuples and the `MorphirModel` envelope `0.1.0-draft.1`. It has a framework-dependent F# frontend guest executable and an AOT-safe host client in the `morphir` executable. No local protocol member is added, and legacy `0.1` is not implemented. Without a configured extension, the AOT `morphir` executable reports that the F# frontend is not available in this build.

This delivery does not include:

- `ProjectReference` or NuGet reference resolution, and dependency packages other than the Morphir SDK;
- `#if` defines;
- incremental compilation or `typesOnly`;
- Ion output, document-tree layout, and Specs or Application distributions;
- the `morphir-model`/Ion transport and the evidence and guarantee admission rules;
- extension installation or discovery, and backend guests;
- `morphir build`, and more than one `--target` in one run.
- a shared read-only view of classic and v4 models for backends, so that each backend is written once. This is planned as a separate change.

Flags, keys and project-file items for any of these fail explicitly, or are reported as ignored. They do not partly work.

## Capabilities

### New Capabilities

- `fsharp-source-frontend`: Turn ordered F# source documents into a Morphir library model for the supported subset, with exact refusal diagnostics for everything else.
- `ir-model-writing`: Produce a requested IR family and exact format version from a typed model as canonical JSON that the existing readers accept. Build each family natively from one frontend lowering, and refuse content the family cannot hold.
- `project-file-sources`: Expand an explicitly named build-tool project file into an ordered, root-relative source set, through registered providers that do not depend on the source language.
- `cli-model-compilation`: Resolve compile settings from the CLI and the project configuration, select sources and a frontend, report results, publish IR, and optionally continue in memory into generation.
- `compile-task-records`: Keep the ecosystem compile output directory, task record, tombstone and lock rules, and let `generate` read the most recent compile.
- `mep-frontend-sessions`: Run frontends through MEP `0.2.0-draft.1` `compileModel` sessions as both host and guest, following the pinned upstream contract.

### Modified Capabilities

None. `cli-model-generation` is still owned by the in-flight `add-generate-command` change and has no main spec yet, so `compile-task-records` owns the new prior-compile input behavior. When both changes are archived, fold that requirement into `cli-model-generation`.

## Tracking

- [#499](https://github.com/finos/morphir-dotnet/issues/499) tracks the F# frontend and `morphir compile`, with links to [#363](https://github.com/finos/morphir-dotnet/issues/363) and [#368](https://github.com/finos/morphir-dotnet/issues/368). The [feature PRD](../../../docs/content/contributing/design/prds/fsharp-source-frontend.md) separates tool-path, MEP-path and deferred milestones. `tasks.md` records progress.
- Upstream contract baseline: `finos/morphir` at `cde7722eb`. This covers:
  - the MEP `0.2.0-draft.1` contract, model transport and transcripts;
  - `docs/cli/compile.md` and `docs/design/out-directory.md`.

  Pinned copies keep provenance, the same way the Ion fixtures do.
- Related planning: `establish-morphir-compatibility-kit` (the full MCK), and a decision record with a PRD that proposes morphir-dotnet as the default Morphir CLI. This change supplies their first evidence: verdict reports, AOT size and start time, and documented deviations.
- Upstream requests that come from this change, filed as follow-ups:
  - update the MEP guest kit (`generic-json-library-v1`) to the current contract. It should qualify frontend-only guests and guests that speak only the model tier, without a mandatory legacy `0.1` stage. The F# guest seeks kit qualification only after that, and its design does not change to fit the current kit.
  - a shared `[frontend.<language>].project_file` key convention;
  - align the Rust CLI's `--json-lines` with the event-stream meaning that this change implements. The Rust CLI writes one compact report line, which seems to be an incorrect implementation of the flag. Include the record format from design.html STRM-1 as the proposal.
  - a MEP diagnostics notification (`0.2.0-draft.1` has none).
- The design notes from 2025-12-31 in `docs/design/` (`PRD-fsharp-frontend.md`, `fsharp-language-coverage.md`) are input material. This change replaces their `morphir fsharp parse` command and `MORPHIR001` codes with the ecosystem `morphir compile` command and `fsharp.<rule>` codes. Fable and Fantomas are prior art (see design.html FSF-6); neither becomes a frontend dependency.

## Impact

- **New projects:**
  - `src/Morphir.Compilation`: compile contracts, the IR builder interface with native classic and v4 builders, the frontend, builder, writer and project-file provider registries. AOT-safe.
  - `src/Morphir.Frontend.FSharp`: FSharp.Compiler.Service lowering. JIT only.
  - `src/Morphir.Mep`: the MEP `0.2.0-draft.1` codec, model envelope, framing, host client and guest loop. AOT-safe, with source-generated JSON.
  - `src/Morphir.Frontend.FSharp.Mep`: the guest executable.
  - `src/Morphir.Conformance`: the MCK runner with `library`, `cli` and `mep-guest` adapters. AOT-compatible.
  - The test projects for each.
- **Models:** add writers next to the readers in `src/Morphir.Models/Json/`, keyed by the existing `Versioning.FormatVersion`. The existing `TypeCodec`/`LiteralCodec` encoders and their v1/v2 tag bugs are not changed. The readers, the public IR types and the wire formats do not change.
- **Tooling:**
  - New `Features/Compile/` (command, handler, settings resolver, source selection, frontend resolver, record store), reusing the generation backend resolver, the publisher and the report conventions.
  - The MSBuild project-file provider, which starts `dotnet` as a child process and so is AOT-safe.
  - `generate` settings get record-based input.
  - The `[frontend.fsharp]` keys `project_file` and `extension` are added to the schema.
- **Hosts:** `dotnet-morphir` gets its own composition, which adds the in-process frontend scenario. The AOT `morphir` executable stays free of FSharp.Compiler.Service and passes the existing trim/AOT checks. Project-file expansion needs a .NET SDK 8 or later on the machine.
- **Packaging:** a new framework-dependent F# frontend guest package. FSharp.Compiler.Service is already pinned in `Directory.Packages.props`.
- **Conformance:** a new `conformance/` product directory: the case schema, the `frontend-fsharp` suite and `deviations.md`.
- **Documentation:**
  - `docs/content/docs/cli/compile.md`
  - an F# frontend language-coverage reference generated from the rule catalog
  - a project-file sources reference
  - updates to `generate.md` and to the troubleshooting page
