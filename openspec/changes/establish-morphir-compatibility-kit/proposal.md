# Proposal

## Why

The current Morphir conformance oracle in `finos/morphir` has flaws:

- **Expectations come from an implementation.** The generic guest kit's expected IR was recorded from the published Python 0.4.2 guest, so a bug in that guest becomes an expectation.
- **Comparison rules hide differences.** Its rules ignore list order and accept alternative spellings without stating why.
- **Qualification is coupled to things outside the contract.** It requires a legacy MEP `0.1` stage and a guest that is both frontend and backend.
- **The reference CLI has known defects.** For example, its `--json-lines` writes one report line, not a stream.

Morphir .NET already keeps independently sourced conformance evidence (`dotnet-sdk-conformance`). `add-fsharp-compile` starts a portable case corpus and runner for compilation (design decision MCK-1 there). This change makes morphir-dotnet the home of the **Morphir Compatibility Kit (MCK)**. The oracle is reviewed case data, never an implementation, and any Morphir implementation can run the kit.

## What Changes

- **A versioned, portable case corpus** under `conformance/`. A case expresses:
  - its sources and settings;
  - whether it applies to each exact format version, and the reason when it does not;
  - independently written expectations: IR for each exact format version, diagnostics, CLI observations and MEP exchanges;
  - a classification (`required`, `exclusion`, `knownGap`), a `basis`, and evidence and provenance links.

  The corpus has its own kit version, is checked by machine, and does not depend on .NET types. It reuses the vocabulary of `dotnet-sdk-conformance`.
- **Gherkin is the normative case format** (decision MCK-FMT):
  - `.feature` files sit under `conformance/suites/<suite>/`, and every scenario name starts with a stable id `<suite>-NNNN`. This follows the upstream IR v4 kit pinned in `tests/Fixtures/MorphirIon/kit` (tags such as `@semantic`/`@spelling`, and content-typed docstrings such as `"""fsharp` and `"""json`).
  - Steps come from a closed, versioned MCK step vocabulary (`conformance/steps/steps-1.md`). A linter rejects any step outside it, and each runner implements the vocabulary. Official Gherkin parsers make the files portable.
  - Features name no adapter.
  - Matrices use `Scenario Outline` with `Examples`. Excluded rows are in tagged `@excluded` Examples blocks with a `reason` column.
  - Small artifacts are inline docstrings. Large expectations, such as expected IR, are sidecar files that a step references.
  - Provenance and review use tags and sidecar review files.
  - Literate Markdown pages are generated from the features for the documentation site, never edited by hand, and a check fails when they are stale.
- **Explicit comparison rules.** Exact canonical bytes are the default where canonical output is required. A semantic comparison applies only through named, documented normalizations that a case lists. Nothing is lenient by default.
- **Oracle independence rules.** An expectation is never recorded from an implementation under test without independent review, and its `basis` says where it came from. Upstream kits and transcripts are imported as reviewed inputs with provenance. Each imported case is admitted, corrected through a recorded supersession with evidence, or excluded with a reason. Each supersession becomes an upstream issue.
- **Suites:**
  - IR codecs and writers;
  - frontends for each language (the F# suite from `add-fsharp-compile` is the first);
  - generation;
  - CLI behavior (compile, generate, task records, `--json`, `--json-lines`, exit codes);
  - MEP host;
  - MEP guest.

  A plan selects suites only from the capabilities that an implementation claims. A frontend-only guest is not required to be a backend, and a guest that speaks only `0.2.0-draft.1` is not required to speak legacy `0.1`.
- **Implementation adapters**, each with recorded identity:
  - `cli`: any `morphir` executable, observed through `--json` and `--json-lines`;
  - `mep-guest`: any stdio guest on MEP `0.2.0-draft.1`;
  - `library`: in-process .NET.
- **A `morphir mck` command** in the Native AOT CLI:
  - `run` executes a kit against an implementation and writes a qualification report;
  - `report-check` re-derives every verdict from the recorded observations with the same kit;
  - `list` shows suites, cases and profiles.

  Exit codes match upstream `morphir mck`: `0` qualified, `1` failed or operational error, `2` invalid invocation.
- **Integrity of qualification reports.** A report records the kit version and case-set SHA-256, the runner identity, the executable path, its SHA-256 and arguments, the identity files, every request and observation, and every verdict. A stored "qualified" flag is never trusted.
- **Running against other implementations.** The kit can qualify the Rust CLI and other MEP guests. Their reports and gaps are published as evidence.

This change does not include:

- moving the existing SDK conformance corpus (`tests/Fixtures/MorphirSDK`) or the Ion reader kit (`tests/Fixtures/MorphirIon/kit`) into the kit format. Later changes do that through the import rules defined here.
- a kit embedded in the `morphir` executable. The kit is an explicit `--kit` input.
- WASM guest hosting, and changes to the MEP protocol definition, which stays upstream.

## Capabilities

### New Capabilities

- `conformance-case-format`: The versioned, portable case and suite data, its schema, classifications and explicit comparison rules.
- `conformance-evidence`: Oracle independence, the basis and evidence of each expectation, and the import and supersession of upstream kits and transcripts.
- `conformance-execution`: Plans from claimed capabilities, implementation adapters, isolation, deadlines and the recording of identities.
- `conformance-reports`: The content of a qualification report, its reproducibility, and report checking.
- `cli-conformance-command`: The `morphir mck` command, with its subcommands, options, output and exit codes.

### Modified Capabilities

None. `dotnet-sdk-conformance` keeps its requirements. A later change moves its corpus into the kit format.

## Tracking

- **Depends on `add-fsharp-compile`.** That change lands first. It creates the `conformance/` corpus, the `Morphir.Conformance` runner and the `cli`, `mep-guest` and `library` adapters for the F# compile suite (its MCK-1). This change generalizes them and does not create a second runner.
- **Related:** the default-CLI decision record and PRD that propose morphir-dotnet as the default Morphir CLI. MCK results are part of the evidence that decision needs.
- **No tracking issue exists yet.** Open an MCK epic and a feature PRD before production changes.
- **Upstream baseline:** `finos/morphir` at `cde7722eb`, covering `spec/mep/mck/` (kits, `model-transport.cases.json`, the README rules) and the Smithy transcripts. The MEP protocol definition stays upstream. Contract defects found by the kit are reported upstream.

## Impact

- **New:**
  - `conformance/`: the kit manifest, the step vocabulary, the Gherkin suites with their sidecar files, review records, imports and `KIT.md`. A dependency on the official `Gherkin` parser package (the one Reqnroll already uses).
  - `src/Morphir.Conformance`: generalized from `add-fsharp-compile`. It is AOT-safe and uses source-generated JSON.
  - `src/Morphir.Tooling/Features/Mck/`: the `morphir mck` command, registered in both CLI hosts.
  - Tests that run every suite through the same runner.
- **Existing:** `tests/Fixtures/MorphirIon/mep/` pins stay. Their model-transport cases become the first imported upstream input. The SDK and Ion corpora are not changed.
- **Documentation:** a kit reference, an authoring guide (how to write an independent expectation), a supersession log and a `morphir mck` CLI reference under `docs/content/docs/`.
- **Release:** each CLI release publishes a versioned kit archive with its SHA-256.
