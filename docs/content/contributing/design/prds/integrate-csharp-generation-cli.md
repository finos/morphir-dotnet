# Integrate C# generation into the CLI

Status: In Progress. Issue: [#481](https://github.com/finos/morphir-dotnet/issues/481).

## Problem and intended behavior

The C# generator is available as an in-memory library, but `morphir generate`
does not register it. Users need its typed behavior through the established
loading, reporting and safe publication flow. A successful invocation produces
immutable C# sources without requiring a compiler. Consumers compile those
sources separately against `Morphir.SDK.CSharp`.

## Scope and contracts

- Exact target `csharp`, provider identity `morphir.csharp`, registered statically
  beside F# through `IGenerationBackend`. Preserve the generic resolver.
- Classic Library versions 1, 2, 3, 3.0.0, 3.1.0 and v4 Library versions 4,
  4.0.0 only. Reject unsupported required semantics and distributions.
- Repeated `--option` settings retain last-occurrence precedence. Numeric JSON
  `language-version` accepts 14 and 15, default 14. Optional `namespace` accepts
  a validated C# namespace. Unknown keys, aliases, wrong kinds and invalid names
  are invocation failures before emission.
- Direct-library and provider paths produce identical relative paths and UTF-8
  bytes for equivalent models and options. Preserve the typed classic/v4 input,
  source version and immutable SDK equality protocol.
- Reuse JSON, Ion text, binary and tree loading and the existing ownership
  manifest. Preserve unowned files, unchanged timestamps and prior successful
  output after semantic, operational or cancellation failures. Remove only
  stale owned files. Reject unsafe paths and input/output overlap.
- JSON mode writes one report to stdout and diagnostics to stderr. Exit codes
  are 0 success, 1 semantic failure, 2 invocation/operational failure, 130 cancel.
- Execute real trimmed and NativeAOT CLI builds, then compile and run the
  emitted business models separately under SDK 10.0.403/C# 14 and isolated
  SDK 11.0.100-rc.2.26504.105/C# 15.

The production SourceGenerator adapter, compiler installation during generation,
new orchestration paths, runtime changes and configuration semantics are excluded.

## Feature status tracking

| Feature | Status | Evidence |
| --- | --- | --- |
| Verified main prerequisites and contribution issue | Implemented | Clean base 80d516e2; seven exact PRs MERGED to main; #481 |
| Real-provider BDD RED and registration | Implemented | Observed classic/v4 CLI unknown-target RED 2/2; all 444 Tooling cases passed in full native run |
| Immutable validated options and core namespace parity | In progress | Public API compile RED observed before core; provider RED 7/40 then GREEN 40/40; core namespace 43/43 including compiled consumers |
| Model/source/diagnostic adaptation across wire forms | Planned | No acceptance claimed |
| Safe publication and failure preservation | Planned | No acceptance claimed |
| Real CLI JSON, stderr and 0/1/2/130 observations | Planned | No acceptance claimed |
| Pinned compiled business consumers | Planned | New CLI evidence required |
| Executed trimmed and NativeAOT CLI | Planned | New CLI evidence required |
| Executed command reference and fresh consumers | Planned | No acceptance claimed |
| Full tests, meaningful coverage, package and format checks | In progress | New native full run 3,912 framework + 15 cold + 71 inspection passed; adapter 47/48 lines, whole core 3,051/3,261; strict and format checks passed; package checks pending |
| Landed main, archived specs and checkout cleanup | Planned | Coordinator owns these delivery gates |

## Test plan

Write BDD scenarios first and observe failure against the actual CLI before
production changes. Add public boundary tests for registration, immutable
options, capabilities, refusal mapping, cancellation and exact direct/provider
parity. Use independently authored business expectations and frozen SDK cases.
Exercise the real CLI for every input representation and reporting category.
Extend publication cases with real sources and verify file content and metadata.
Compile emitted sources without modification in fresh ordinary consumers.

Serialize restore/build/test/coverage/pack/publish through the allocated slot.
Bound processors to two, MSBuild to one and disable shared compilation/node
reuse. Run native `build.ps1 --target Test`, package/build checks, direct hook
checks, strict `mise run openspec:verify`, frozen contract/link audits and focused
coverage of new production scope, at least 90%. Preserve any unrelated
configuration failure without changing, suppressing or filtering its semantics.

## Implementation notes

### Start gate, 2026-11-03

The orchestrator prepared `t3-79f2b566` on
`t3/integrate-csharp-generation-cli`. Initial status was clean; HEAD and refreshed
origin/main equal `80d516e2e4e62dfd6d69e41c07bae8cfeff4a670`, 0/0.
Exact prerequisite PRs #462, #463, #469, #470, #472, #478, #480 report MERGED
to main, and all supplied landed SHAs are reachable. OpenSpec reports repo-local
`html-design`, ready, 0/9 tasks. Runtime is Codex gpt-6.1-sol/high.

The landed `GenerationOptions` record exposes only `LanguageVersion`.
The approved namespace requirement needs a source-compatible core extension.
The approved proposal retains the existing options constructor and three-argument API,
adds a validated namespace value and overload, and threads naming explicitly
through emission. The coordinator granted the exact core/shared leases. No source text
rewriting, IR migration, alternative equality protocol or ambient state is allowed.

## Open questions

- Namespace API and exact core/shared leases approved by the coordinator.
- Hosted review/landing and archival follow-up occur after implementation handoff.

## Evidence and ownership

New proof root: `C:/Temp/morphir-csharp-cli-79f2b566-20261103`.
Prior generator/archive proof roots are read-only dependencies owned by root.
The lead is the only Git writer. No auxiliary, helper or extra checkout has been
started. Exact commands, outcomes, artifacts and releases will be recorded in
this change's validation and handoff records before handback.
