# Morphir Model Convert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `morphir model convert` in the .NET CLI: a Morphir model converts between a single file and a document tree (IR 3.1.0 and 4, JSON), plain or name-compacted, on top of an extracted `Morphir.Files` library and a CLI-wide streaming `--json` convention.

**Architecture:** Three model layers in `Morphir.Models`: `DocumentAddress` (identity by Morphir names), `DocumentTree` (decoded typed documents, `split`/`join`), and `DocumentTreeStore` (the only code that sees `VFileTree`: paths, codecs, envelope, compaction). `VFile`/`VFileTree`/`PropertyBag` move to a new `Morphir.Files` that also targets `netstandard2.0` and implements `IFileProvider`. The command lives in `Morphir.Tooling` and does all disk I/O.

**Tech Stack:** .NET 11 SDK (`global.json` 11.0.100), F# for model code, C# for commands and test support, System.Text.Json, System.CommandLine 2.0.15, Expecto + FsCheck (F# tests), TUnit + Reqnroll (C# and Gherkin), Native AOT.

**Spec:** `docs/superpowers/specs/2026-11-16-model-convert-design.md`, `openspec/changes/add-model-convert/` and `openspec/changes/adopt-streaming-json/` (proposal, specs, design.html, tasks.md). Each task names the OpenSpec scenarios it proves; the scenario name is the test name.

## Global Constraints

- No Rust code changes, in either repository. Parent-side work is specs, schemas, example files, docs and `tools/cli-release.ts`, and adds no kit cases.
- Target frameworks come from `Directory.Build.props` properties, never literals: executables and tests `$(MorphirCurrentTargetFramework)` (`net11.0`); libraries `$(MorphirLibraryTargetFrameworks)` (`net11.0;net10.0`); `Morphir.Models` and `Morphir.Files` add `netstandard2.0`.
- `netstandard2.0` gaps are closed with the `Polyfill.fs` + `#if NETSTANDARD2_0` pattern of `Morphir.Models`; no new package for `System.Memory` or `System.Collections.Immutable`.
- `Morphir.Models` makes no file-system calls. Disk access is in `Morphir.Tooling` (and `VFileTree.loadFromDisk` in `Morphir.Files`).
- Zero IL2026/IL3050 warnings in the Native AOT `morphir`; `TreatWarningsAsErrors` stays on.
- Option values only with `=`: `--json[=stream|document]`, `--compact[=names]`.
- Diagnostics have stable dotted `snake_case` codes; refusals never leave partial output.
- `.NET` never defines the compaction envelope, the policy, or expected compacted bytes; they come from the parent, pinned with provenance.
- Use the `test-manager` skill before adding a test, feature file, step or driver; run the `test-manager-reviewer` agent on each milestone diff. Use the `fsharp-csharp-interop` skill (and its reviewer) for F# APIs called from C#.
- Feature files name cases by intent; no paths or inline JSON. Test data comes from object mothers; files on disk only through `FixtureCatalog`.
- Commits: conventional messages; **no AI co-author or "Generated with" lines**.
- Each milestone ends with a commit that builds and passes its tests plus a "what to try" note in `openspec/changes/add-model-convert/tasks.md`; stop for the maintainer's PR decision.
- Before the first pull request, draft the morphir-dotnet issue for each change (OpenSpec tasks 1.1) and file it only after the maintainer approves the text; link it in both proposals.
- Tick the matching OpenSpec `tasks.md` boxes in the same commit as the work they describe.

## Review Focus

1. **Names that escape to the same stem or collide case-insensitively** (`Invoice` vs `invoice`, `a-b` vs `a_b`): two documents must never map to one file; expected a refusal naming both. Test in Task 14.
2. **Paths at exactly the path budget and one over**, including deep dependency packages: exactly-at writes untruncated; one over truncates with `fileNames`. Test in Task 14.
3. **`--output` that is the input, inside the input, or a file where a directory is needed**: refuse before writing, input untouched. Test in Task 18.
4. **Windows reserved stems and trailing dots** (`con`, `aux`, `name.`) as Morphir names: the tree spec's escaping applies and the tree round-trips on Windows. Test in Task 14.
5. **An empty module (no types, no values) and a package with no modules**: both round-trip; the module file still exists. Test in Task 11 (split/join) and Task 14 (store).

---

## Agent split and waves

Up to four agents, each on its own task list, all committing to `feat/model-convert` (rebase before each commit; tasks touch disjoint files except where noted).

| Agent | Tasks | Starts |
| --- | --- | --- |
| **A, files and model** | 1, 2, 3, then 13, 11, 12, 14, 15, then 22, 23, 25, 26 | wave 1 |
| **B, test structure** | 4, 5, then 16 (drivers), 19 (black-box features), 24-tests | wave 1 |
| **C, CLI** | 6, 7, 8, 9, then 17, 17b (after A's 15), 18, then 27 | wave 1 |
| **D, parent repository** | 10, 20, 21 (finos/morphir branch `feat/model-file-context`) | wave 1 |

Waves: wave 1 = tasks 1-10 in parallel. Milestone 1 = tasks 11-19 and 17b (A after 3; B after 5; C after 9). Milestone 2 = tasks 20, 22, 23 (A after D's 20). Milestone 3 = tasks 21, 24, 25, 26 (A after D's 21). Task 27 closes.

---

## Step zero: Morphir.Files (milestone 0)

### Task 1: Extract Morphir.Files

**Files:**
- Create: `src/Morphir.Files/Morphir.Files.fsproj`, `src/Morphir.Files/README.md`
- Move (git mv): `src/Morphir.IR.Pipeline/{Property.fs,File.fs,FileTree.fs}` → `src/Morphir.Files/`
- Modify: `src/Morphir.IR.Pipeline/Morphir.IR.Pipeline.fsproj`; `Morphir.Ion`, `Morphir.Tooling`, `Morphir.CodeGeneration.FSharp`, `Morphir.IR.Pipeline.Plugins` project files and their `open` lines; the solution file
- Move tests: the `VFile`/`VFileTree`/`PropertyBag` tests → new `tests/Morphir.Files.Tests` (Expecto, like `tests/Morphir.Models.Tests`)

**Interfaces:**
- Produces: namespace `Morphir.Files` holding the moved types unchanged in shape (`PropertyBag`, `Property<'T>`, `VFile`, `VFileContent`, `StreamSource`, `ContentError`, `VFileTree`, `TreeNode`, `TreeConfig`, `TreeLoadError` and their modules). `Morphir.IR.Pipeline` keeps `Processor.fs`, `PipelineBuilder.fs` and references `Morphir.Files`.

- [ ] **Step 1:** `git mv` the three files and the tests; set `<TargetFrameworks>$(MorphirLibraryTargetFrameworks)</TargetFrameworks>` for now (Task 2 adds `netstandard2.0`); change `namespace Morphir.IR.Pipeline` to `namespace Morphir.Files` in the moved files and add `open Morphir.Files` at consumers.
- [ ] **Step 2:** Build. Run: `dotnet build -nologo`. Expected: 0 errors, 0 warnings.
- [ ] **Step 3:** Run: `dotnet run --project tests/Morphir.Files.Tests` and the existing pipeline/Ion/codegen test projects. Expected: all pass unchanged (scenario "Existing pipeline behavior is unchanged").
- [ ] **Step 4:** Commit `refactor: extract Morphir.Files from Morphir.IR.Pipeline`.

### Task 2: Port Morphir.Files to netstandard2.0

**Files:**
- Modify: `src/Morphir.Files/Morphir.Files.fsproj` (add `netstandard2.0`, the trim/AOT analyzer condition and NETSDK1207 settings copied from `Morphir.Models.fsproj`)
- Create: `src/Morphir.Files/Polyfill.fs` (internal shims, compiled only for `netstandard2.0`)
- Create: `tests/Morphir.Files.Tests/NetStandardConsumer/` (a tiny `netstandard2.0` F# project referencing `Morphir.Files`, built by a test)

**Interfaces:**
- Produces: the same public API on all three targets.

- [ ] **Step 1: Write the failing test** `"A netstandard2.0 model library uses virtual file trees"`: builds `NetStandardConsumer` with `dotnet build` and asserts exit code 0 and no `warning` lines; the consumer builds a tree from two in-memory files and its output (run through a `net11.0` host) lists both locations.
- [ ] **Step 2:** Run it. Expected: FAIL (no `netstandard2.0` target).
- [ ] **Step 3:** Add the target and shims for: `ReadOnlyMemory` (via `System.Memory` from the host), `ImmutableDictionary`, `HashCode`, 3-argument `File.Move` (delete then move, under `#if`), `Stream.ReadAtLeast`, `DirectoryInfo.LinkTarget` (attribute check `FileAttributes.ReparsePoint`), `OperatingSystem.IsWindows` (`RuntimeInformation.IsOSPlatform`), `Path.TrimEndingDirectorySeparator`, `Convert.ToHexString`, char overloads of `Split`/`Contains`/`StartsWith`.
- [ ] **Step 4:** Run: `dotnet build src/Morphir.Files -nologo` and the test. Expected: 0 warnings on all three targets; PASS.
- [ ] **Step 5:** Commit `build: target netstandard2.0 in Morphir.Files`.

### Task 3: IFileProvider in both directions

**Files:**
- Create: `src/Morphir.Files/FileProviders.fs`
- Modify: `src/Morphir.Files/Morphir.Files.fsproj` (reference `Microsoft.Extensions.FileProviders.Abstractions` via `Directory.Packages.props`)
- Modify: `src/Morphir.Models/Morphir.Models.fsproj` (reference `Morphir.Files`)
- Test: `tests/Morphir.Files.Tests/FileProviderTests.fs`

**Interfaces:**
- Produces:
  ```fsharp
  module Morphir.Files.VFileProviders
  val asFileProvider : VFileTree -> Microsoft.Extensions.FileProviders.IFileProvider
  val ofFileProvider : Microsoft.Extensions.FileProviders.IFileProvider -> VFileTree   // content as VFileContent.Stream, opened on demand
  ```

- [ ] **Step 1: Write the failing tests**, one per scenario: `"A file is found by its location"` (`Exists`, not `IsDirectory`, `Length` = byte count, stream bytes equal); `"A missing file is reported as not existing"` (`Exists = false`); `"A directory lists its entries"` (only direct children of `pkg/acme/demo`); `"Watching never signals a change"` (`HasChanged = false`, callback never invoked); `"Embedded fixtures become a tree"` (an `EmbeddedFileProvider` over the test assembly; one file per resource, bytes equal); `"Content is not read while building"` (a counting `IFileProvider` wrapper records zero `CreateReadStream` calls after `ofFileProvider`).
- [ ] **Step 2:** Run: `dotnet run --project tests/Morphir.Files.Tests -- --filter "FileProviders"`. Expected: FAIL (module missing).
- [ ] **Step 3:** Implement; `Watch` returns `NullChangeToken.Singleton`; locations use `/`.
- [ ] **Step 4:** Run the tests; also `dotnet build` the C# generator and run its package/build tests (generator still loads in the compiler host with the new assemblies). Expected: PASS.
- [ ] **Step 5:** Write `src/Morphir.Files/README.md` (purpose, both adapters, one example) and a CHANGELOG line; commit `feat: VFileTree as IFileProvider`. Milestone 0 note in tasks.md.

## Test structure foundation

### Task 4: Models.TestSupport and DistributionMother

**Files:**
- Create: `tests/Morphir.Models.TestSupport/Morphir.Models.TestSupport.fsproj` (references `Morphir.Models`, `Morphir.Files`, FsCheck)
- Create: `tests/Morphir.Models.TestSupport/DistributionBuilder.fs`, `DistributionMother.fs`, `Generators.fs`
- Test: `tests/Morphir.Models.Tests/TestSupport/MotherTests.fs`

**Interfaces:**
- Produces:
  ```fsharp
  module Morphir.Models.TestSupport.DistributionBuilder
  val aV4Library : PackageName -> V4LibraryBuilder
  val aV3Library : PackageName -> V3LibraryBuilder
  val withModule : ModulePath -> (ModuleBuilder -> ModuleBuilder) -> builder -> builder
  val withType : Name -> TypeShape -> ModuleBuilder -> ModuleBuilder
  val withValue : Name -> ValueShape -> ModuleBuilder -> ModuleBuilder
  val withDependency : PackageName -> (ModuleBuilder -> ModuleBuilder) -> builder -> builder
  val buildV4 : V4LibraryBuilder -> Morphir.IR.V4.Distribution
  val buildV3 : V3LibraryBuilder -> Morphir.IR.Classic.Distribution.Distribution<unit, Type<unit>>

  module Morphir.Models.TestSupport.DistributionMother   // each a built distribution
  val v4LibraryWithOneModule; val v4LibraryWithDependency; val v4Specs; val v4Application
  val v3Library; val v3Specs; val emptyModule; val noModules
  val nameUsedOnce; val nameUsedOften; val nonAsciiName; val pathAtTheBudget; val pathOverTheBudget
  val collidingStems; val windowsReservedStems

  module Morphir.Models.TestSupport.Generators
  val v4Distribution : Gen<Morphir.IR.V4.Distribution>; val v3Distribution : Gen<…>   // built on the builder
  ```
- [ ] **Step 1: Write the failing test** `"every mother case builds and encodes"`: for each case, the existing writer (`V4JsonWriter.writeDistribution` / `ClassicJsonWriter.write`) returns `Ok`.
- [ ] **Step 2:** Run: `dotnet run --project tests/Morphir.Models.Tests -- --filter "Mothers"`. Expected: FAIL.
- [ ] **Step 3:** Implement builder, mother and generators (`nonAsciiName` uses a name containing `é`; `pathAtTheBudget` sized against budget 4000 by computing its logical path length).
- [ ] **Step 4:** Run. Expected: PASS, plus an FsCheck property `"generated distributions encode"` with 200 cases.
- [ ] **Step 5:** Commit `test: add distribution object mother and generators`.

### Task 5: FixtureCatalog, pinned kit data, guard test, skill

**Files:**
- Create: `tests/Morphir.TestSupport/Fixtures/FixtureCatalog.cs`, `FixtureEntry.cs`, `YamlToJson.cs` (YamlDotNet, tests only)
- Create: `tests/Fixtures/MorphirTrees/kit/{document-tree.feature,distributions.feature,versions.feature}` and `tests/Fixtures/MorphirTrees/PROVENANCE.md` (copied unchanged from one finos/morphir commit, `spec/ir/mck/`; record commit and SHA-256 per file)
- Create: `tests/Morphir.Build.Tests/Helpers/FixtureOwnershipTests.cs`
- Modify: `.agents/skills/test-manager/SKILL.md`, `AGENTS.md` (object mothers, fixture catalog, `model verify` example)

**Interfaces:**
- Produces:
  ```csharp
  public static class FixtureCatalog {
      public static class Kit { public static IReadOnlyList<KitTreeCase> DocumentTree { get; } }  // id, title, input files, canonical JSON, diagnostic
      public static class Models { public static FixtureEntry V3Library { get; } … }           // existing IR fixtures by intent
      public static IReadOnlyCollection<FixtureEntry> All { get; }                               // every owned file
  }
  public sealed record FixtureEntry(string Intent, string RelativePath, string Sha256, string Provenance);
  public sealed record KitTreeCase(string Id, string Title, IReadOnlyDictionary<string, string> Files, string? CanonicalSingleFile, string? Diagnostic);
  ```
  `KitTreeCase.Files` maps logical paths to JSON text (YAML spellings translated).
- [ ] **Step 1: Write failing tests:** `"pinned kit files match their hashes"`; `"document-tree-0006 YAML and JSON load equal"`; `"An unowned fixture fails a guard test"` (guard enumerates `tests/Fixtures/**` minus README/PROVENANCE and asserts each is in `FixtureCatalog.All` or an existing owner list; a planted temp file makes it fail and names the file).
- [ ] **Step 2:** Run: `dotnet test tests/Morphir.TestSupport.Tests` and `tests/Morphir.Build.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement. The kit case reader extracts each scenario's id, title, file fences and canonical spelling; it raises on a step form it does not know (as `KitCases.fs` does). Register every existing fixture directory as an owner so the guard passes today.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Update the skill and AGENTS.md; run the `test-manager-reviewer` agent on the diff (scenarios "The skill gives the pattern", "A feature file names a case by intent", "A test that starts the CLI for one capability"). Commit `test: add FixtureCatalog and pinned kit tree data`.

## Streaming JSON (adopt-streaming-json)

### Task 6: The --json option and the bare-flag rewrite

**Files:**
- Create: `src/Morphir.Tooling/Cli/JsonOutputOption.cs`, `src/Morphir.Tooling/Cli/OptionValueRewrite.cs`
- Modify: the CLI entry point that builds the root command (`src/Morphir/Program.cs` or the shared root in `Morphir.Tooling`)
- Test: `tests/Morphir.Tooling.Tests/Cli/OptionValueRewriteTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum JsonOutputMode { None, Stream, Document }
  public static class JsonOutputOption {
      public static Option<string> Create();                      // "--json", ZeroOrOne, AcceptOnlyFromAmong("stream","document")
      public static JsonOutputMode ModeOf(ParseResult result);     // None when absent
      public static JsonOutputMode ScanTokens(IReadOnlyList<string> args); // for parse-error rendering
  }
  public static class OptionValueRewrite {
      // rewrites bare flags to "--flag=<default>" before "--"; runs after response-file expansion
      public static string[] Apply(string[] args, IReadOnlyDictionary<string, string> bareDefaults); // {"--json":"stream","--compact":"names"}
      public static string? HintFor(string unrecognizedToken, string flag, IReadOnlyCollection<string> modes); // "--json=document"
  }
  ```
- [ ] **Step 1: Write failing tests** for every row of design JSON-3: `["--json"]`→Stream; `["--json=document"]`→Document; `["--json","model.json"]`→Stream + positional `model.json`; `["--json=document","model.json"]`; `["--json","document"]` on a no-positional command → error + hint `--json=document`; `["--json=bogus"]` → error listing `stream`,`document`; `["--json:document"]`→Document; `["--json=true"]` → error; `["--","--json"]` → `--json` stays a positional; response file containing `--json` → Stream.
- [ ] **Step 2:** Run: `dotnet test tests/Morphir.Tooling.Tests --treenode-filter "/*/*/OptionValueRewriteTests/*"`. Expected: FAIL.
- [ ] **Step 3:** Implement; hook `Apply` into the entry point before `Parse`.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat: --json takes an optional mode given with =`.

### Task 7: Shared JSON output writer and JsonOutputAssertions

**Files:**
- Create: `src/Morphir.Tooling/Cli/JsonOutputWriter.cs` (generalised from compile's JSON Lines writer in `Features/Compile/CompileReport.cs`)
- Modify: `src/Morphir.Tooling/Features/Compile/CompileReport.cs` (use the shared writer)
- Create: `tests/Morphir.TestSupport/Drivers/JsonOutputAssertions.cs`; Modify `JsonCommandReport.cs` to read reports through it
- Test: `tests/Morphir.Tooling.Tests/Cli/JsonOutputWriterTests.cs`, `tests/Morphir.TestSupport.Tests/Drivers/JsonOutputAssertionsTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class JsonOutputWriter<TReport>(TextWriter stdout, JsonOutputMode mode, JsonTypeInfo<TReport> reportInfo) {
      public void Event(string name, Action<Utf8JsonWriter> members);  // stream: one compact line, flushed; document: no-op
      public void Completed(TReport report);                            // stream: {"event":"completed","report":…}; document: indented report
  }
  public static class JsonOutputAssertions {
      public static JsonElement ReportOfStream(CommandResult result);   // asserts JSON Lines, each with "event", exactly one last "completed"; returns report
      public static JsonElement ReportOfDocument(CommandResult result); // asserts exactly one document
      public static IReadOnlyList<JsonElement> Events(CommandResult result);
  }
  ```
- [ ] **Step 1: Write failing tests:** writer — two events then completed give three lines, each flushed before the next call (a `TextWriter` spy records flush order), document mode writes only the report; assertions — scenarios "A JSON command writes one document", "A log line on standard output fails", "A stream gives its report" (no `completed`, two `completed`, `completed` not last, a non-JSON line each fail with the line shown).
- [ ] **Step 2:** Run both test projects. Expected: FAIL.
- [ ] **Step 3:** Implement with source-generated `JsonTypeInfo` only (AOT).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat: shared JSON output writer`.

### Task 8: Compile adopts the convention; --json-lines removed

**Files:**
- Modify: `src/Morphir.Tooling/Features/Compile/CompileCommand.cs` (remove `_jsonLines`, use `JsonOutputOption`, `JsonOutputWriter`)
- Modify: compile command tests in `tests/Morphir.Tooling.Tests/Features/Compile/`, black-box compile features, `docs/content/docs/cli/compile.md`
- Modify: `src/Morphir.Conformance/CliAdapter.fs` (drive `compile --json`)

- [ ] **Step 1: Update tests first** to the `cli-model-compilation` delta scenarios: "Events in order", "Failure still ends with completed", "Both JSON flags" (`--json --json-lines` → one `completed` with invocation failure naming `--json-lines`, exit 2), "Cancellation" (exit 130, failure kind `cancelled`), "JSON mode is not streamed" (`--json=document`), "Unsupported construct in JSON mode" (`--json=document`), and `cli-json-output` "An early warning is visible before a slow stage ends" (slow fake frontend; warning record precedes frontend completion).
- [ ] **Step 2:** Run the compile tests. Expected: FAIL.
- [ ] **Step 3:** Implement; keep event names `stageStarted`, `diagnostics`, `stageCompleted`, `completed`.
- [ ] **Step 4:** Run compile tests, the frontend conformance suites (`dotnet run --project tests/Morphir.Frontend.FSharp.Tests -- --sequenced`). Expected: PASS.
- [ ] **Step 5:** Update `compile.md` (JSON section: stream default, `=document`, drop the Rust difference note's `--json-lines` wording); commit `feat!: compile streams under --json; remove --json-lines`.

### Task 9: Every other command adopts the convention

**Files:**
- Modify: `Features/Validate/ValidateCommand.cs`, `Features/Generate/GenerateCommand.cs`, `Features/Configuration/ConfigurationCommand.cs`, `ConfigurationEditingCommand.cs`, `Features/Extensions/{ExtensionListCommand,ExtensionRuntimesCommand,WorkerInvocation}.cs`, `Features/ProjectMigration/ProjectMigrationCommand.cs`, `Features/WorkspaceMigration/WorkspaceMigrationCommand.cs`, `Scenarios/IrToolingScenario.cs`
- Modify: `src/Morphir.Conformance/GuestKit/WorkerSessions.fs`, `src/Morphir.Conformance/WasmHost/Worker.fs`
- Create: `tests/Morphir.BlackBox.Tests/Integration/JsonOutput.feature` + steps using `JsonOutputAssertions`
- Modify: each command's CLI page under `docs/content/docs/cli/`, the CLI index page, CHANGELOG

- [ ] **Step 1: Write `JsonOutput.feature`** with a scenario outline over `validate`, `generate`, `config show`, `extension list`, `extension runtimes`, `project migrate --dry-run`, `workspace migrate --dry-run`, `ir verify`, covering every `cli-json-output` scenario by name ("A bare flag streams", "Unknown mode", "Every line parses", "A usage error still ends with completed", "A command with nothing to stream", "The two modes carry one report", "A document is pipeable", "The next argument stays the command's", "A separated mode is refused with a hint", "A log line goes to stderr", "A named member in both modes" with `extension list`'s `host`, "The old flag is unknown").
- [ ] **Step 2:** Run: `dotnet test tests/Morphir.BlackBox.Tests --treenode-filter "/*/*/JsonOutputFeature/*"`. Expected: FAIL.
- [ ] **Step 3:** Move each command onto `JsonOutputOption` + `JsonOutputWriter` (no events: `Completed` only). Update conformance worker readers.
- [ ] **Step 4:** Run the black-box suite, the WASM host and frontend conformance suites, and the `model-analyzers` / `cli-csharp-generation` tests on `--json=document`. Expected: PASS.
- [ ] **Step 5:** Docs and CHANGELOG (**BREAKING**: `--json` streams; `--json=document` for one report); add the coordination note to `openspec/changes/establish-morphir-compatibility-kit/proposal.md` and run `openspec validate establish-morphir-compatibility-kit`; commit `feat!: every command streams under --json`.

### Task 10 (agent D): Parent release check reads the new output and the model group

**Files (finos/morphir, branch `feat/model-file-context`):**
- Modify: `tools/cli-release.ts` (packaged-input check runs `morphir model verify`; reads the `completed` line's `report` when `--json` is used), its tests in `tools/`
- Modify: `INSTALLING.md`, `docs/getting-started/morphir-cli.md`

- [ ] **Step 1:** Update the tools test to expect `model verify` and to parse a JSON Lines stream ending with `completed`. Run: `bun test tools/cli-release.test.ts`. Expected: FAIL.
- [ ] **Step 2:** Implement; update the two docs.
- [ ] **Step 3:** Run: `bun test tools/`. Expected: PASS. No Rust file touched (`git diff --stat -- '*.rs' 'Cargo.*'` is empty).
- [ ] **Step 4:** Commit; do not open a PR until the maintainer says the morphir-dotnet pin move is ready.

## Milestone 1: document trees and the command

### Task 11: Addresses and v4 split/join

**Files:**
- Create: `src/Morphir.Models/IR/Trees/DocumentAddress.fs`, `DocumentTree.fs`, `V4DocumentTree.fs`
- Test: `tests/Morphir.Models.Tests/IR/Trees/DocumentAddressTests.fs`, `V4DocumentTreeTests.fs`

**Interfaces:**
- Consumes: `DistributionMother`, `Generators` (Task 4); `TreeStems` escaping (Task 13, which runs first).
- Produces:
  ```fsharp
  namespace Morphir.IR.Trees
  type ModuleDocumentAddress = Module of ModulePath | TypeDef of ModulePath * Name | ValueDef of ModulePath * Name
  type DocumentAddress = Manifest | Own of ModuleDocumentAddress | Dependency of PackageName * ModuleDocumentAddress
  module DocumentAddress =
      val logicalPath : DocumentAddress -> string   // "manifest", "pkg/<pkg>/<mod>/module", ".../<stem>.type", "deps/<pkg>/@/<mod>/…"; untruncated, no extension
  type TreeDocument<'module, 'typ, 'value> = ModuleDoc of 'module | TypeDoc of 'typ | ValueDoc of 'value
  type DocumentTree<'manifest, 'module, 'typ, 'value> =
      { Manifest: 'manifest; Documents: Map<DocumentAddress, TreeDocument<'module, 'typ, 'value>> }
  type TreeNotice = DroppedManifestMember of name: string
  type TreeError = { Code: string; Address: DocumentAddress option; Message: string }

  // V4DocumentTree.fs
  type V4Manifest = { Kind: V4DistributionKind; Package: PackageName; Dependencies: PackageName list; EntryPoints: (string * EntryPoint) list }
  and V4DistributionKind = LibraryKind | SpecsKind | ApplicationKind
  type V4ModuleDocument = { Access: Access option; Doc: string option; Annotations: Annotation list; TypeNames: Name list; ValueNames: Name list }
  type V4TypeDocument = DefinedType of AccessControlled<Documented<TypeDefinition>> | SpecifiedType of Documented<TypeSpecification>
  type V4ValueDocument = DefinedValue of AccessControlled<Documented<ValueDefinition>> | SpecifiedValue of Documented<ValueSpecification>
  type V4DocumentTree = DocumentTree<V4Manifest, V4ModuleDocument, V4TypeDocument, V4ValueDocument>
  module V4DocumentTree =
      val split : Morphir.IR.V4.Distribution -> V4DocumentTree
      val join  : V4DocumentTree -> Result<Morphir.IR.V4.Distribution, TreeError list>   // modules in DocumentAddress.logicalPath ordinal order
  ```
- [ ] **Step 1: Write failing tests:** "Two spellings of one name have one address" (`Name.fromString "Invoice line"` from a split and from a parsed address are equal; `logicalPath` contains no extension); "A dependency document is addressed by its package"; "A v4 model round-trips" (FsCheck `join (split d) = Ok d`, 200 cases, plus every v4 mother case); `emptyModule` and `noModules` round-trip (Review Focus 5); `join` of a tree whose module lists a type with no `TypeDoc` → `Error` with code `tree.missing_definition`.
- [ ] **Step 2:** Run: `dotnet run --project tests/Morphir.Models.Tests -- --filter "Trees"`. Expected: FAIL.
- [ ] **Step 3:** Implement. Equality of the round trip uses the model's own equality (dependency order excluded, as `Distribution` documents).
- [ ] **Step 4:** Run. Expected: PASS on `net11.0`; `dotnet build src/Morphir.Models` 0 warnings on all targets.
- [ ] **Step 5:** Commit `feat: document addresses and v4 document trees`.

### Task 12: Classic v3 trees and the 3.1.0 Specs writer

**Files:**
- Create: `src/Morphir.Models/IR/Trees/ClassicDocumentTree.fs`
- Modify: `src/Morphir.Models/Json/Classic/ClassicJsonWriter.fs` (declare a `3.1.0` writer for `Specs`; existing writers unchanged), its writer-selection tests
- Test: `tests/Morphir.Models.Tests/IR/Trees/ClassicDocumentTreeTests.fs`, `tests/Morphir.Models.Tests/Json/ClassicJsonWriterTests.fs`

**Interfaces:**
- Produces: `ClassicDocumentTree` with the same shape as Task 11 over classic types with `JsonElement` attributes kept verbatim (`Distribution<JsonElement, JsonElement>` from `ClassicJson.readDistribution`); `ClassicManifest = { Kind: LibraryKind | SpecsKind; Package: PackageName; Dependencies: PackageName list; SingleFileVersion: ClassicLabel }` where `ClassicLabel = Integer3 | SemVer310` records which label `join` must produce (Library → `Integer3`, Specs → `SemVer310`).
- [ ] **Step 1: Write failing tests:** "A v3 model round-trips" (every attribute slot preserved, v3 mothers + FsCheck); "A classic Specs distribution at 3.1.0" (writes `"formatVersion": "3.1.0"` and the Specs tag; `ClassicJson.readDistribution` reads it back equal); "Unsupported output version" extended (Specs at `3` still refused, naming the supported set including `3.1.0` Specs).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat: classic v3 document trees and a 3.1.0 Specs writer`.

### Task 13: Move TreeStems into the model layer

**Files:**
- Move: `src/Morphir.Ion/Ir/TreeStems.fs` → `src/Morphir.Models/IR/Trees/TreeStems.fs` (namespace `Morphir.IR.Trees`, public)
- Modify: `src/Morphir.Models/Polyfill.fs` (shims `SHA256.HashData`, `Convert.ToHexString`), `src/Morphir.Ion/Ir/IonIrTree.fs` (use the moved module)

- [ ] **Step 1:** Move; run the Ion tree tests. Expected: PASS unchanged.
- [ ] **Step 2:** Build `Morphir.Models` for all targets. Expected: 0 warnings.
- [ ] **Step 3:** Commit `refactor: TreeStems moves to Morphir.Models`.

### Task 14: DocumentTreeStore for v4

**Files:**
- Create: `src/Morphir.Models/IR/Trees/DocumentTreeStore.fs`, `V4TreeFiles.fs` (encode/decode of manifest, module, type and value files with `V4Json`/`V4JsonWriter`)
- Create: `tests/Morphir.Models.TestSupport/DocumentTreeMother.fs`, `DocumentTreeStoreDriver.fs`
- Test: `tests/Morphir.Models.Tests/IR/Trees/V4StoreTests.fs`

**Interfaces:**
- Consumes: `VFileTree` (Task 1), `TreeStems` (Task 13), `V4DocumentTree` (Task 11).
- Produces:
  ```fsharp
  type WriteOptions = { PathBudget: int; Compact: CompactMode option }      // defaults: 4000, None
  and CompactMode = Names
  type ReadOptions = { MaxFiles: int; MaxFileBytes: int64 }
  type ReadReport = { Notices: TreeNotice list; CompactedFiles: DocumentAddress list }
  type StoreError = { Code: string; File: string option; Message: string }
  type StoredTree = V4Tree of V4DocumentTree | ClassicTree of ClassicDocumentTree
  module DocumentTreeStore =
      val write : WriteOptions -> StoredTree -> Result<VFileTree, StoreError list>
      val read  : ReadOptions -> VFileTree -> Result<StoredTree * ReadReport, StoreError list>
  // test support
  module DocumentTreeStoreDriver =
      val roundTrip : StoredTree -> Result<StoredTree * ReadReport, StoreError list>
      val fileAt : DocumentAddress -> VFileTree -> string option   // physical path the store chose
  ```
- [ ] **Step 1: Write failing tests:** law `read (write t) = t` (FsCheck over v4 generators); bijection law (distinct addresses → distinct physical paths, case-insensitively); "A long name is truncated and recorded" (`pathOverTheBudget`: truncated stem + `fileNames` entry); `pathAtTheBudget` untruncated (Review Focus 2); "A dependency is written under a bare version segment" (`deps/morphir/sdk/@/`); "Directory order does not change the model" (reverse the `VFileTree` file order); "Version and created are reported" (`DroppedManifestMember "version"`, `"created"`; `layout`/`pathBudget` dropped silently); "A meta member does not change the model"; `collidingStems` refused with code `tree.stem_collision` naming both names (Review Focus 1); `windowsReservedStems` round-trip (Review Focus 4); "The same tree from two sources" (disk via `VFileTree.loadFromDisk` in a temp dir marked `PlatformSpecific`, and `VFileProviders.ofFileProvider` over an embedded provider).
- [ ] **Step 2:** Run: `dotnet run --project tests/Morphir.Models.Tests -- --filter "Store"`. Expected: FAIL.
- [ ] **Step 3:** Implement; extension `.json`; modules sorted by `DocumentAddress.logicalPath` ordinal; top-level `$meta` ignored on read, never written.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat: document tree store for IR v4`.

### Task 15: v3 store, refusals, architecture tests

**Files:**
- Create: `src/Morphir.Models/IR/Trees/ClassicTreeFiles.fs`
- Modify: `DocumentTreeStore.fs`, `DocumentTreeMother.fs` (one invalid shape per refusal)
- Test: `tests/Morphir.Models.Tests/IR/Trees/ClassicStoreTests.fs`, `RefusalTests.fs`, `ArchitectureTests.fs`

- [ ] **Step 1: Write failing tests:** "A v3 Library returns its original label" (tree files `"3.1.0"`, joined `3`); "A v3 Specs tree keeps 3.1.0"; refusals with codes — "Ambiguous profile" `tree.ambiguous_profile` (names both manifests), `tree.v3_version` (manifest not `"3.1.0"`), `tree.unknown_member`, `tree.path_over_budget`, "Missing definition file" `tree.missing_file` (names module file and `Invoice`), `tree.outside_tree` (a `.json` beside `pkg/` claiming membership), "Versioned dependency segment" `tree.versioned_dependency`; each refusal returns `Error` and no tree. Architecture: reflection over `Morphir.Models` finds `Morphir.Files` types only in `DocumentTreeStore`-family modules; `DocumentAddress` case fields are only `ModulePath`, `Name`, `PackageName`, `ModuleDocumentAddress`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the whole `Morphir.Models.Tests`. Expected: PASS.
- [ ] **Step 5:** Commit `feat: classic tree store and tree refusals`.

### Task 16 (agent B): Model-conversion driver capability

**Files:**
- Create: `tests/Morphir.TestSupport/Drivers/ModelConversionDriver.cs`, `ConversionReport.cs`
- Modify: `tests/Morphir.TestSupport/Drivers/TestEligibility.cs` (`DriverCapability.ModelConversion`, tag `@needs-model-conversion`, in the command set), `ModelVerificationDriver.cs` (`model verify`)
- Test: `tests/Morphir.TestSupport.Tests/Drivers/ModelConversionDriverTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum ModelLayout { SingleFile, DocumentTree }
  public sealed record ConversionOptions { public ModelLayout? Layout { get; init; } public string? Compact { get; init; } public JsonOutputMode Json { get; init; } = JsonOutputMode.Stream; }
  public interface IModelConversionDriver { Task<CommandResult> ConvertAsync(WorkspaceFile input, string output, ConversionOptions? options = null, CancellationToken cancellation = default); }
  public sealed class ConversionReport : JsonCommandReport { public bool Success; public int ExitCode; public string InputLayout, OutputLayout, IrVersion; public string? CompactMode, CompactPolicy; public IReadOnlyList<string> FilesWritten; public IReadOnlyList<ReportDiagnostic> Diagnostics, Notices; }
  ```
  Arguments built: `model convert --input <in> --output <out> [--layout=document-tree|single-file] [--compact|--compact=<mode>] --json[=document]`.
- [ ] **Step 1: Write failing tests:** "A conversion report" (success read from report and compared with exit code); "A feature file states intents only" (driver builds the arguments above from options); "A target without the capability skips the test".
- [ ] **Step 2:** Run: `dotnet test tests/Morphir.TestSupport.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `test: model conversion driver capability`.

### Task 17 (agent C): The model group replaces ir

**Files:**
- Modify: `src/Morphir.Tooling/Scenarios/IrToolingScenario.cs` → rename to `ModelToolingScenario.cs` (group `model`, command `verify`)
- Modify: `tests/Morphir.BlackBox.Tests/Integration/ModelVerification.feature`, `Steps/VerificationSteps.cs` (intent phrases via `FixtureCatalog.Models`), `docs/content/docs/cli/ir-verify.md` → `model-verify.md`, CLI index, CHANGELOG
- Modify (skill text already in Task 5): `@needs-model-verification` description in `TestEligibility.cs`

- [ ] **Step 1: Update `ModelVerification.feature`** to intent phrases ("Given a valid IR v3 model") and add scenarios "The group lists its commands" and "The former command is unknown" (no `morphir-ir` on the search path; non-zero exit; output names `ir`).
- [ ] **Step 2:** Run: `dotnet test tests/Morphir.BlackBox.Tests --treenode-filter "/*/*/ModelVerificationFeature/*"`. Expected: FAIL.
- [ ] **Step 3:** Implement the rename; no alias. Change the `ir verify` row of `JsonOutput.feature` (Task 9) to `model verify`.
- [ ] **Step 4:** Run. Expected: PASS, including "A valid v3 model is valid" and "A file without a format version is not valid".
- [ ] **Step 5:** Commit `feat!: morphir model replaces morphir ir`.

### Task 17b (agent C): model verify checks v4 files and document trees

**Files:**
- Create: `src/Morphir.Tooling/Infrastructure/Schemas/{morphir-ir-v4.json,morphir-ir-v4-document-tree-files.json}` (copied unchanged from finos/morphir `website/static/schemas/` at one commit; add to `Schemas/PROVENANCE.md` with SHA-256)
- Modify: `src/Morphir.Tooling/Features/VerifyIR/{VerifyIR.cs,VersionDetector.cs}` (move to `Features/ModelVerify/`), `Infrastructure/JsonSchema/SchemaLoader.cs` (load v4 and tree-file schemas; select a `$defs` entry by file kind)
- Modify: `tests/Morphir.BlackBox.Tests/Integration/ModelVerification.feature`, `Steps/VerificationSteps.cs`, `tests/Morphir.TestSupport/Drivers/ModelVerificationDriver.cs` (accept a directory), `docs/content/docs/cli/model-verify.md`

**Interfaces:**
- Consumes: `DocumentTreeStore.read`, `VFileTree.loadFromDisk`, the `tree.*` codes (Tasks 14, 15).
- Produces: verification report members added: `layout` (`single-file` | `document-tree`), `filesChecked` (int), `compactedFiles` (list, empty until Task 23); each error gains `file` (tree-relative path, absent for single files) beside its JSON path. Version detection: integer `formatVersion` 1 to 4, or `"3.1.0"` → v3. New diagnostic `verify.schema_version_conflict` (exit 2).

- [ ] **Step 1: Write failing scenarios** in `ModelVerification.feature` ("Given a valid IR v4 model", "Given the tree that model convert wrote from a v4 Library with a dependency", mother-built trees through `FixtureCatalog`): "A valid v4 model is valid", "An invalid v4 model names its error", "A valid v4 tree is valid" (counts every file), "A valid v3 tree is valid", "An error inside one tree file names the file", "A broken tree rule is an error" (`missing_member`, naming the missing file), "A schema version that contradicts the manifest" (exit 2, names 3 and 4).
- [ ] **Step 2:** Run: `dotnet test tests/Morphir.BlackBox.Tests --treenode-filter "/*/*/ModelVerificationFeature/*"`. Expected: FAIL.
- [ ] **Step 3:** Implement: a directory input loads a `VFileTree`; v4 files are schema-checked per kind (manifest → `DistributionManifestFile`, `module.json` → `ModuleManifestFile`, `*.type.json` → `TypeDefinitionFile`, `*.value.json` → `ValueDefinitionFile`), then `DocumentTreeStore.read` runs on the whole tree; all errors are collected, not first-only.
- [ ] **Step 4:** Run the feature, the existing v1 to v3 scenarios and `ModelVerificationBudgetTests`. Expected: PASS, existing behaviour unchanged.
- [ ] **Step 5:** Update `model-verify.md` (v4, trees, `--schema-version=4`); commit `feat: model verify checks v4 models and document trees`.

### Task 18 (agent C): The convert command

**Files:**
- Create: `src/Morphir.Tooling/Features/ModelConvert/{ModelConvertCommand.cs,ConvertReport.cs,ConvertJsonContext.cs}`
- Create: `tests/Morphir.Tooling.Tests/Features/ModelConvert/{ModelConvert.feature,ModelConvertSteps.cs,ModelConvertCommandDriver.cs}`
- Modify: `ModelToolingScenario.cs` (register `convert`)

**Interfaces:**
- Consumes: `DocumentTreeStore.read/write` (Task 14/15), `V4DocumentTree`/`ClassicDocumentTree` `split`/`join`, `ClassicJson`/`V4Json` readers and writers, `JsonOutputWriter` (Task 7), `OptionValueRewrite` (`--compact` default `names`).
- Produces: report members `success`, `exitCode`, `input`, `output`, `inputLayout`, `outputLayout`, `inputEncoding`, `outputEncoding`, `irVersion`, `compactMode`, `compactPolicy`, `filesWritten`, `diagnostics`, `notices`; events `stageStarted`/`stageCompleted` for `read`, `convert`, `write`, and `fileWritten` `{path}`.
- Diagnostic codes: `model.nothing_to_do` (exit 2), `model.output_not_empty` (exit 1), `model.output_overlaps_input` (exit 1), `model.unsupported_context` (exit 1; replaced in Task 23), plus the `tree.*` codes passed through (exit 1).
- [ ] **Step 1: Write the failing in-process feature:** events flushed before the next stage (fake store that blocks `write` until the test has read the `read` stage records); `fileWritten` names every file the report lists; streamed diagnostics equal the report's; `--output` equal to, inside, or a file where a directory is needed → refused before writing, input unchanged (Review Focus 3); a refusal leaves `--output` absent (staging directory + atomic rename).
- [ ] **Step 2:** Run: `dotnet test tests/Morphir.Tooling.Tests --treenode-filter "/*/*/ModelConvertFeature/*"`. Expected: FAIL.
- [ ] **Step 3:** Implement: detect input (file → single file; directory with `manifest.json` → tree; both manifests → `tree.ambiguous_profile`); default `--layout` to the input's; compute output; write to a sibling staging directory, rename into place.
- [ ] **Step 4:** Run; then `dotnet publish src/Morphir -c Release -p:PublishAot=true`. Expected: PASS; 0 IL2026/IL3050.
- [ ] **Step 5:** Commit `feat: morphir model convert`.

### Task 19 (agent B): Black-box conversion features and docs

**Files:**
- Create: `tests/Morphir.BlackBox.Tests/Integration/{ModelConversion.feature,ModelConversionKitCases.feature}`, `Steps/ModelConversionSteps.cs`
- Create: `docs/content/docs/cli/model-convert.md`; Modify CHANGELOG; add the milestone 1 note to `openspec/changes/add-model-convert/tasks.md`

- [ ] **Step 1: Write the features** (tags `@integration @needs-model-conversion`): "A directory is read as a tree", "The IR version is kept", "Nothing to do", "A refused tree writes nothing", "A non-empty output is refused", "Every written file is reported", "Notices are in the report", "A v3 Library returns its original label", "Version and created are reported", "Ambiguous profile", and one scenario per `FixtureCatalog.Kit.DocumentTree` case named `<kit id> <title>` (scenario outline with examples generated from the catalog's ids; the step loads the case by id).
- [ ] **Step 2:** Run: `dotnet test tests/Morphir.BlackBox.Tests --treenode-filter "/*/*/ModelConversion*/*"`. Expected: PASS against the development build (Task 18 done); run again on the Native AOT executable target.
- [ ] **Step 3:** Write `model-convert.md` with the try-it commands; run them as written in a temp directory. Expected: they succeed.
- [ ] **Step 4:** Run the `test-manager-reviewer` agent on the milestone 1 diff; fix findings. Commit `test: black-box model conversion`. **Stop: maintainer decides on a milestone 1 PR.**

## Milestone 2: read compacted files

### Task 20 (agent D): Parent envelope contract

**Files (finos/morphir, branch `feat/model-file-context`):**
- Modify: `openspec/changes/add-bounded-model-access/specs/ir/name-compaction/spec.md` (identify by `$schema` URL `https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json`; mark whole-file vs bounded requirements), its `tasks.md` (1.1 done; 2.2, 2.3 partly done when .NET lands)
- Create: `website/static/schemas/ir-context/0.1.0-draft.1/document.schema.json`; `docs/spec/ir/context/examples/{valid,invalid}/*.json` (v3 and v4 single files, each v3 and v4 tree file kind; invalid: unknown alias, duplicate entry, unknown critical path, over-limit table, over-long name, context after document, undeclared URL) with `README.md` stating limits (entry count, expanded name length)

- [ ] **Step 1:** Write the schema and examples; validate every valid example against the schema and every invalid one fails (a Bun script in `tools/`, with a test). Run: `bun test tools/`. Expected: PASS.
- [ ] **Step 2:** `openspec validate add-bounded-model-access --strict`. Expected: valid. No Rust change.
- [ ] **Step 3:** Commit; report the commit SHA to agent A.

### Task 22: Read and expand compacted files

**Files:**
- Create: `src/Morphir.Models/IR/Files/{MorphirFile.fs,NameContext.fs,ContextExpansion.fs}`
- Create: `tests/Morphir.Models.TestSupport/MorphirFileMother.fs`; pin Task 20's examples under `tests/Fixtures/MorphirContext/` with `PROVENANCE.md` (commit SHA, hashes) in `FixtureCatalog.Context`
- Modify: `DocumentTreeStore.fs` (expand on read; `ReadReport.CompactedFiles`)
- Test: `tests/Morphir.Models.Tests/IR/Files/ExpansionTests.fs`

**Interfaces:**
- Produces:
  ```fsharp
  namespace Morphir.IR.Files
  type FileContext = { Names: Map<string, Name>; PackagePaths: Map<string, Path>; ModulePaths: Map<string, Path>; FqNames: Map<string, FQName>; Critical: string list }
  type MorphirFile<'doc> = { Schema: Uri option; Context: FileContext option; Document: 'doc }
  type ContextLimits = { MaxEntries: int; MaxNameLength: int }   // values from the parent README
  module MorphirFile =
      val declaredSchemas : Uri list
      val readEnvelope : ContextLimits -> ReadOnlyMemory<byte> -> Result<MorphirFile<JsonElement>, StoreError list>   // plain file → Schema = None
      val expand : IrFamily -> MorphirFile<JsonElement> -> Result<JsonElement, StoreError list>                       // IrFamily = Classic3 | V4
  ```
- [ ] **Step 1: Write failing tests** from the pinned examples: "A compacted v3 single file expands to v3", "A compacted tree value file expands in place", "A literal that looks like an alias stays a literal", "Classic and v4 slots expand differently"; each valid example expands to its plain twin.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement; expansion walks the JSON with the schema's typed name positions only; `formatVersion` untouched.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat: read compacted Morphir files`.

### Task 23: Context refusals and convert expansion

**Files:**
- Modify: `ContextExpansion.fs`, `ModelConvertCommand.cs` (remove `model.unsupported_context`), `Features/ModelVerify/` (expand before checking; fill `compactedFiles`), `ModelVerification.feature` ("A compacted tree is verified after expansion", "An invalid context fails verification" with `context.unknown_alias`)
- Create: `tests/Morphir.BlackBox.Tests/Integration/ModelConversionCompaction.feature` (reading scenarios)
- Test: `tests/Morphir.Models.Tests/IR/Files/ContextRefusalTests.fs`

- [ ] **Step 1: Write failing tests** with codes: "An unknown envelope is refused" `context.undeclared_schema`; "Unknown alias" `context.unknown_alias`; `context.duplicate_entry`; `context.unknown_critical`; `context.too_many_entries`; `context.name_too_long`; "Context after the document" `context.after_document`; context bytes counted toward `ReadOptions.MaxFileBytes`. Black-box: "A compacted tree becomes a plain single file", compacted v3/v4 single files to plain.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run model and black-box tests. Expected: PASS.
- [ ] **Step 5:** Docs + CHANGELOG + milestone 2 note; commit `feat: model convert expands compacted files`. **Stop: maintainer decides on a milestone 2 PR.**

## Milestone 3: write compacted files

### Task 21 (agent D): Parent writer policy and fixed bytes

**Files (finos/morphir, same branch):**
- Modify: `name-compaction/spec.md` and `tasks.md` (task 1.4): policy `morphir.compact/0.1.0-draft.1` — output is RFC 8785 JCS; sizes are UTF-8 byte lengths of JCS; alias a name only when its occurrences' saved bytes exceed its table entry's bytes; ids allocated in first-use order (`a`, `b`, … `z`, `aa`, …); ties broken by ordinal order of the expanded spelling; tables written in the order `names`, `packagePaths`, `modulePaths`, `fqNames`
- Create: `docs/spec/ir/context/policy/morphir.compact-0.1.0-draft.1/cases/<case>/{input.json,expected.json,README.md}` for: v3 single file, v4 single file, v3 and v4 files of each tree kind, name used once, non-ASCII name, alias allocation order, tie, table order, look-alike literal, loss case; each README shows the hand computation

- [ ] **Step 1:** Write the cases by hand; a Bun check verifies only that `expected.json` is valid JCS (sorted keys, no whitespace) and validates against the Task 20 schema — never by running a compactor. Run: `bun test tools/`. Expected: PASS.
- [ ] **Step 2:** Commit; request review of the hand computations in the parent PR; report the SHA to agent A.

### Task 24 (agent B): Compaction test data

**Files:**
- Pin Task 21's cases under `tests/Fixtures/MorphirContext/policy/` with provenance in `FixtureCatalog.Context.Policy`; extend `MorphirFileMother` with plain inputs for each case

- [ ] **Step 1:** Pin and register; hash test passes. Commit `test: pin compaction policy cases`.

### Task 25: JCS writer

**Files:**
- Create: `src/Morphir.Models/Json/CanonicalJson.fs`
- Test: `tests/Morphir.Models.Tests/Json/CanonicalJsonTests.fs` (RFC 8785 appendix vectors as test data through `FixtureCatalog`)

**Interfaces:**
- Produces: `module Morphir.Json.CanonicalJson` — `val write : JsonElement -> byte array`; `val byteLength : string -> int` (UTF-8 bytes of a JCS string literal including quotes and escapes).
- [ ] **Step 1: Write failing tests:** RFC vectors byte-equal; "A non-ASCII name is measured as UTF-8" (`byteLength "é"` counts `é` as 2 bytes; output contains raw `é`); number serialisation of `1e21`, `-0`, `0.1`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement: keys sorted by UTF-16 code units; ECMAScript number form; escapes only per RFC 8785.
- [ ] **Step 4:** Run on `net11.0`; build all targets. Expected: PASS, 0 warnings.
- [ ] **Step 5:** Commit `feat: RFC 8785 canonical JSON writer`.

### Task 26: Writer policy and --compact

**Files:**
- Create: `src/Morphir.Models/IR/Files/CompactionPolicy.fs`
- Modify: `DocumentTreeStore.fs` (`WriteOptions.Compact`), `ModelConvertCommand.cs` (`--compact[=names]`, report `compactMode`, `compactPolicy`), `ModelConversionCompaction.feature` (writing scenarios), docs, CHANGELOG
- Test: `tests/Morphir.Models.Tests/IR/Files/CompactionTests.fs`

**Interfaces:**
- Produces: `module CompactionPolicy` — `val id : string` (`"morphir.compact/0.1.0-draft.1"`); `val compact : IrFamily -> JsonElement -> Result<byte array * bool, StoreError list>` (bytes and whether any alias was worth its cost).
- [ ] **Step 1: Write failing tests:** "The parent's fixed cases match byte for byte" (every pinned case); "Expand after compact" (FsCheck over generators + every mother, single file and tree); determinism (compact twice → identical bytes); "A name used once stays spelled out"; "Compaction that would be a loss is not applied" (plain bytes returned, flag false); black-box "A single file is compacted and stays one file", "Bare compact means names", "Unknown mode", "A separated mode is refused with a hint".
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement exactly the policy text from Task 21.
- [ ] **Step 4:** Run model, tooling and black-box tests; AOT publish. Expected: PASS; 0 IL2026/IL3050.
- [ ] **Step 5:** Docs + CHANGELOG + milestone 3 note; commit `feat: model convert writes compacted files`. **Stop: maintainer decides on a milestone 3 PR.**

## Close

### Task 27: Integration checks and pins

- [ ] **Step 1:** When the parent branch merges, move every `PROVENANCE.md` pin to the merged commit; hash tests pass.
- [ ] **Step 2:** Native AOT publish and full black-box run on it. Expected: PASS, 0 IL2026/IL3050.
- [ ] **Step 3:** Fill both changes' traceability tables with the final test names; every row resolves to a passing test. Run: `openspec validate --all --strict`. Expected: all valid.
- [ ] **Step 4:** Tick both `tasks.md` files to match; commit `docs: close add-model-convert and adopt-streaming-json tasks`.
