# Proposal

## Why

The CLI has two machine-output flags today: `--json` writes one report on every command, and `compile` alone adds `--json-lines`, which streams events and cannot be combined with `--json`. Two flags that exclude each other are awkward, and a single report makes a long command hold everything back until it ends. Tools such as `claude -p --output-format stream-json`, `codex exec --json`, `cargo --message-format=json` and `go test -json` stream JSON Lines on stdout and keep stderr for people, and `jq` reads such a stream directly.

This change makes streaming the default machine output of the .NET `morphir` CLI, with one option that can still ask for a single document:

```console
$ morphir model convert --input morphir-ir.json --output tree --layout document-tree --json
{"event":"stageStarted","stage":"read"}
{"event":"fileWritten","path":"tree/manifest.json"}
…
{"event":"completed","report":{"success":true,"exitCode":0,…}}

$ morphir validate --input model.json --json=document | jq .exitCode
0
```

## What Changes

- **BREAKING:** on every `morphir` and `dotnet-morphir` command that has machine output, a bare `--json` writes JSON Lines to stdout: one compact object per line with an `event` member, flushed when the event occurs, ending with exactly one `{"event":"completed","report":{…}}`. A command with nothing to stream writes only its `completed` line.
- **BREAKING:** `--json=document` writes only the report, as one JSON document, which is what `--json` wrote before.
- **BREAKING:** `compile --json-lines` is removed; its event stream is now `compile --json`.
- **BREAKING:** an option value is given only with `=`. A bare `--json` never takes the next argument, so `--json model.json` keeps `model.json` as an argument of the command, and `--json document` is refused with a hint where it cannot be an argument. `--json=true` and `--json=false` are no longer accepted.
- Where a spec names members of a command's JSON output, they are members of its report in either mode.

## Capabilities

### New Capabilities

- `cli-json-output`: the machine-output convention of the CLI: the `--json[=stream|document]` option, the record shape, the report, stream and value-parsing rules.

### Modified Capabilities

- `cli-model-compilation`: compile's report requirement, streamed diagnostics, and the JSON Lines requirements move under `--json` (stream) and `--json=document`; `--json-lines` is removed.
- `model-analyzers`: `validate` with `--json=document` writes one report; the pipeable example uses `--json=document`.
- `cli-csharp-generation`: the C# generation report under the convention.
- `test-driver-contract`: the standard-output assertion distinguishes a JSON Lines stream and one document.

## Tracking

- Origin: the `model convert` design (`docs/superpowers/specs/2026-11-16-model-convert-design.md`), which builds on this convention from its first milestone. Change `add-model-convert` depends on it.
- A morphir-dotnet GitHub issue is filed and linked before the first pull request.
- Coordination: the in-flight change `establish-morphir-compatibility-kit` specifies `morphir mck run --json-lines`; it adopts this convention when it is next revised.

## Impact

- Code: `src/Morphir.Tooling` (a shared JSON output writer and the bare-flag rewrite; the commands in `Features/Compile`, `Configuration`, `Extensions`, `Generate`, `ProjectMigration`, `Validate`, `WorkspaceMigration`, and `Scenarios/IrToolingScenario.cs`); `src/Morphir.Conformance` (`CliAdapter.fs`, `WorkerSessions.fs`, `WasmHost/Worker.fs` read the new output).
- Tests: `tests/Morphir.TestSupport` (`JsonOutputAssertions`, `JsonCommandReport`, every driver's report reading), the black-box features that read `--json` output, and a new `JsonOutput.feature`.
- Docs: every CLI page that shows `--json` output, and the CHANGELOG.
- Parent repository: `tools/cli-release.ts` reads the `completed` line; that finos/morphir change lands with the pin move that carries this change.
- Not in scope: `morphirup`'s own `--json`, which follows in a later change.
