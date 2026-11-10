# Morphir Compatibility Kit seed

The F# frontend suite is the first seed of the portable Morphir Compatibility
Kit. Cases and expectations are product data, independent of the implementation
under test. Do not record or regenerate expectations from compiler output.

- [Vocabulary 1.1](steps/steps-1.md) defines every supported step and the
  value codecs of call observations.
- [Vocabulary 1.2](steps/steps-1.2.md) adds the steps of the `wasm-host` suite
  and changes nothing in version 1.1. [Vocabulary 1.3](steps/steps-1.3.md),
  the one the suite uses, adds the instruction-budget step and lets two
  alternatives of a case share a condition.
- [F# frontend suite](suites/frontend-fsharp/) covers four IR versions and three
  source input kinds.
- [WASM host suite](suites/wasm-host/) holds the crafted cases that qualify a
  WASM extension host, generated from a pinned upstream corpus (see
  [WASM host suite](#wasm-host-suite)).
- [Deviations](deviations.md) records the upstream compatibility decisions.
- [Runner source](../src/Morphir.Conformance/) implements the official Gherkin
  reader, linter, matrix checks, comparison and deterministic JSON reports.

Run the fast reader/runner tests:

```console
dotnet run --project tests/Morphir.Frontend.FSharp.Tests -- --sequenced
```

The `library` adapter accepts an implementation identity and a function from
compile input to an observation or operational failure. It receives no expected
outcome. `FrontendAdapter.create` binds the adapter to any in-process frontend of
the compile contract (`IFrontend`) and writes a successful model with the writer
of the case's IR version. A directory row gives the documents in ordinal path
order. The adapter has no project-file provider, so a project-file row keeps the
document order of the case; the `cli` adapter checks real project-file expansion.

The `cli` adapter (`CliAdapter.create`) drives any `morphir` executable, for
example `dotnet-morphir`, through `compile --json-lines`. It uses only the
documented command line (`docs/content/docs/cli/compile.md`). For each case it
writes a new project directory with a `morphir.toml` (`project.name` and
`project.exposed_modules`) and the documents under `src/`, and it gives the
sources by input kind: the files with `--input` in case order, the `src`
directory with one `--input`, or an `.fsproj` whose `Compile` items keep the
case order with `--project-file`. It reads the report of the one, last
`completed` record and the IR file that `output` names, and it maps each
diagnostic URI from `sources.root` back to the case document. It ignores
unknown records and members. Tests give it a process runner that holds the
shared `MORPHIR_TEST_MAX_PROCESSES` gate.

The frontend tests check the adapter against a scripted fake executable that
accepts only the documented options and changes everything that the
documentation leaves open (unknown records and members, an absolute root above
the documents, another IR path). They then run the complete suite through the
`library` and `cli` adapters against `dotnet-morphir`, and a sample through
`compile --target fsharp` and `compile --target csharp`:

```console
dotnet build Morphir.slnx
dotnet run --project tests/Morphir.Frontend.FSharp.Tests --no-build -- --sequenced --filter "MCK cli adapter (tasks 6.3 and 6.4)"
```

Set `MORPHIR_MCK_REPORT_DIR` to a directory to keep the verdict reports
(`library.json`, `cli.json`, `cli-target-fsharp.json` and
`cli-target-csharp.json`). Set `MORPHIR_DOTNET_TOOL_CLI` to qualify a published
`dotnet-morphir` executable instead of the solution build.

The suite has one refusal case for each code of the F# admission catalog, grouped
by construct family, and semantic cases. A semantic case has expected IR written
by hand for each IR version under `suites/frontend-fsharp/expected/`, call
observations, or both. The frontend tests run the whole suite with the in-process
F# frontend and the .NET call checker, which generates F# from each compiled
model with the F# backend and runs the calls in FSI:

```console
dotnet run --project tests/Morphir.Frontend.FSharp.Tests -- --sequenced --filter "F# corpus through the library adapter (MCK-1)"
```

Check the AOT boundary on macOS ARM64:

```console
dotnet publish tests/Morphir.Conformance.Smoke -c Release -r osx-arm64 -p:PublishAot=true -p:TrimmerSingleWarn=false -o /tmp/morphir-conformance-aot
/tmp/morphir-conformance-aot/Morphir.Conformance.Smoke conformance
```

The frontend tests also run the IR → F# → IR round trip, a morphir-dotnet check
that is not a kit verdict. For each successful case, the F# backend generates F#
from the compiled IR, and the frontend compiles that F# again. The two models
must be equal. For IR v4, the source spans are removed before the comparison,
because the generated F# has other lines and columns.

The smoke program deliberately supplies an incorrect implementation. It exits
zero only when the implementation fails qualification and every planned variant
has a verdict. It writes the unqualified report to stdout. It validates the
runner, not the frontend.

The compiler and Core pins remain unchanged. The smoke's expanded FSharp.Core
warnings are compared with the existing AOT CLI baseline in the feature PRD;
no new warning is suppressed. Other RIDs can run the same smoke using their own
runtime identifier.

## WASM host suite

`suites/wasm-host/` holds the crafted cases of the WASM host qualification kit
(`morphir/mep-wasm-host-kit` `0.1.0-draft.1`, OpenSpec change
`add-wasm-extension-host`, task 1.5). Each case names one module that does one
thing, the frames to send to a worker that runs it, and what the worker must
show: its exit status, its stdout frames and its stderr events. The suite uses
[vocabulary 1.3](steps/steps-1.3.md). At the pin, finos/morphir `8b3c4c35`, it
has 59 cases and 51 modules: 49 cases for `extism-kernel-v1`, which apply to
every host, and 10 for `morphir-bytes-v1`.

The features are generated; never edit one by hand. While finos/morphir
publishes the corpus as JSON, the copy pinned in
`tests/Fixtures/MepWasm/wasm-host.cases.json` (commit and digests in its
`PROVENANCE.md`) is the hand-reviewed source, and the features are generated
from it. An expectation changes only when the corpus changes upstream and the
pin moves. After the pin moves, generate the suite again:

```console
dotnet fsi scripts/generate-wasm-host-cases.fsx
```

The script writes one feature for each group of upstream case ids and the id
map `suites/wasm-host/case-ids.json`, which gives each upstream case id its
stable number. A scenario is named `wasm-host-NNNN <upstream case id>`. A number
never changes and is never reused: a new case takes the next unused number, and
a removed case keeps its number, retired, in the map. Output is byte-stable, with
LF line endings.

The `lint` job of `.github/workflows/development.yml` runs
`dotnet fsi scripts/generate-wasm-host-cases.fsx --check`, which fails, naming
each file and case, when the committed features or id map differ from what the
pin generates: stale features or a hand edit. The reader of the suite
(`Morphir.Conformance.WasmHost.FeatureReader`) refuses the same differences,
and a step outside the vocabulary with its file and line. The tests compare
the pinned JSON and the features case by case:

```console
dotnet build tests/Morphir.Conformance.Tests -c Release
dotnet exec tests/Morphir.Conformance.Tests/bin/Release/net10.0/Morphir.Conformance.Tests.dll
```

The reader of the F# frontend suite (`Corpus.read`) reads only the features
under `suites/<suite>/` of the suite it is given, so the two suites sit side by
side.

The runner drives a host's worker mode with these cases: the experimental, internal
`morphir mck wasm-host run` and `report-check` (see
[the command reference](../docs/content/docs/cli/mck-wasm-host.md)). Its worker adapter,
comparer, judge and report are in `src/Morphir.Conformance/WasmHost/`. The tests drive it
with a scripted fake worker, `tests/Morphir.Conformance.FakeWorker`.

Kit manifests, profiles, claims planning, additional adapters, provenance review
records, report checking and literate documentation belong to the full
compatibility-kit change. The seed keeps those additions on the same reader,
content model and runner.
