---
title: "morphir mck wasm-host (experimental)"
linkTitle: "mck wasm-host"
weight: 90
description: "Experimental and internal: run the crafted WASM host cases against a host's worker mode, and check the report"
---

# morphir mck wasm-host

> **Experimental and internal.** These commands exist to qualify WASM extension hosts while the
> WASM runtime is built (OpenSpec change `add-wasm-extension-host`, task 1.5). Their options, output
> and report format can change without notice, and the compatibility-kit work will fold them into a
> general `mck` command. Do not build on them. Contributors: see
> [WASM host qualification]({{< relref "/docs/contributing/qa/wasm-host-qualification" >}}).

## Synopsis

```console
morphir mck wasm-host run --kit <dir> --host <executable> --report <file>
    --offering <name> --platform <rid> --engine <text>
    [--host-arg <arg>]... [--host-profile <profile>]...
    [--deviations <file>]
    [--guest-kit-report <file> --guest-kit-host-sha256 <hex> --guest-kit-module-sha256 <hex>]

morphir mck wasm-host report-check --kit <dir> --report <file>
    [--expect-offering <name>] [--expect-platform <rid>]
```

## Description

`run` reads the `wasm-host` suite (`conformance/suites/wasm-host`) and the pinned corpus it is
generated from, and runs every case through the host's worker mode:

```console
<host> [--host-arg ...] extension worker --abi <abi> --module <wasm> --build-identity <id> [--request-bytes <n>] [--response-bytes <n>] [--instructions <n>] --json
```

The limits and the instruction budget are passed only for a case that sets them (`worker.requestBytes`,
`worker.responseBytes`, `worker.instructions`); otherwise the worker keeps the contract's defaults.
It reads the build identity once from `<host> extension worker --help --json`. Before a case runs,
it compares the module's SHA-256 with the digest the corpus records, and refuses the case when they
differ. It writes the case's frames on stdin, reads the stdout frames and the stderr events, and
compares them with the case's expectation. A worker still alive one call deadline per frame plus the
corpus margin (2 seconds) after its last frame has its process tree stopped. The runner knows
nothing about the host except its path and arguments.

`report-check` reads a report back, checks it against the same kit and recomputes every outcome from
the recorded observations. It never trusts a stored outcome.

Neither command computes a qualification verdict: the verdict belongs to parent task 1.4, which is
not done. A report holds per-case observations and outcomes, and says so in its
`qualificationNotice`.

## Options of run

| Option | Meaning |
| --- | --- |
| `--kit` | The suite directory, for example `conformance/suites/wasm-host`. The pinned corpus is found in the checkout that holds it. |
| `--host` | The host executable. |
| `--host-arg` | An argument before `extension worker`, repeatable. For example `--host dotnet --host-arg exec --host-arg worker.dll`. |
| `--host-profile` | A profile the host declares it runs, repeatable. `extism-kernel-v1` is always run; give `morphir-bytes-v1` for a host that runs the byte profile. A case for a profile the host does not declare is `not_applicable`. |
| `--offering`, `--platform`, `--engine` | Required. What the host declares about itself, for example `native-aot`, `linux-x64` and `wasmtime 50.0.1`. A worker reports none of them, so the runner is given them, and the report records them (corpus convention `identity`). The platform is the host executable's, which can differ from the machine's, as for an x64 build under emulation. Without one of them `run` exits with 2 before any case. |
| `--report` | The report file to write. |
| `--deviations` | A JSON file `{"deviations": [{"caseId", "tracking", "note"}]}`. A deviation is attached to its case in the report; it never changes an outcome. |
| `--guest-kit-report` and its two digests | A passing generic guest kit run through the same executable, cited by the report. The cited host digest must be the `--host` digest. |

## Outcomes

| Outcome | Meaning |
| --- | --- |
| `passed` | Every run satisfied every matcher of its expectation. For a run with alternatives, one alternative whose condition matches the host is enough, and the report names it in `satisfied`: the run, the alternative's number counted from 1 in corpus order, and its condition. For a runaway case this says which bound ended the call. |
| `failed` | A run satisfied no expectation that applies to the host. The report names, for each failure, the run, the alternative (`null` for a run without alternatives), the matcher and why; a run with alternatives lists the failures of every applicable alternative. |
| `error` | The runner could not judge the case, for example because no started event declared a bound the case depends on. Never a pass. |
| `not_applicable` | The case needs a profile the host does not declare. |
| `not_enforced` | The host's started events declare a bound of the case not enforced. |
| `refused` | The module's bytes differ from the recorded digest; no worker started. |

## Exit codes

| Code | `run` | `report-check` |
| --- | --- | --- |
| 0 | Every applicable case passed. A `not_enforced` case passes only for the instruction budget; the memory ceiling and the call deadline are required. | The report is the kit's and every recomputed outcome passes. |
| 1 | A case did not pass, or an operational error, such as a host that cannot give its build identity. | The report is invalid, or a recomputed outcome does not pass. |
| 2 | An invalid invocation: a missing option, the offering, platform and engine among them, a host that is not a file, an unknown profile, or a kit of another version. | An invalid invocation: a missing option or report, or a kit of another version. |

## Output

stdout carries one JSON document: the command, `"experimental": true`, the exit code, the report
path and digest, and the totals by outcome. Progress and diagnostics go to stderr.

```json
{"command":"mck wasm-host run","experimental":true,"exitCode":1,"report":"/work/.dev/wasm-host.json","reportSha256":"…","host":"/opt/morphir/bin/morphir","hostSha256":"…","totals":{"cases":59,"passed":57,"failed":2,"error":0,"refused":0,"notApplicable":0,"notEnforced":0},"qualificationNotice":"…"}
```

The report (`morphir/mck-wasm-host-report` `0.1.0-draft.2`) is indented JSON with a fixed member
order, so the same observations give the same bytes. It records the suite and corpus pin, the
contract version, the runner, the host executable's path, arguments and SHA-256, its build
identity, profiles, offering, platform and engine, what the started events declared about each
bound, and for each case its module digests, outcome, the alternatives it satisfied or its failures, and the observation of each run: the
worker arguments, stdout (base64), every stderr line with the time it was read, the exit status and
the times. `report-check` refuses a report that does not identify the host's offering, platform and
engine.

## Examples

```console
# The MoonBit native CLI, which also runs the byte profile.
morphir mck wasm-host run --kit conformance/suites/wasm-host \
  --host /path/to/morphir --host-profile morphir-bytes-v1 \
  --offering native-cli --platform win-x64 --engine "moonrockz/extism 0.3.0" \
  --report .dev/wasm-host.json

morphir mck wasm-host report-check --kit conformance/suites/wasm-host --report .dev/wasm-host.json

# A .NET executable, here the Native AOT morphir. It runs extism-kernel-v1 only, so no --host-profile.
morphir mck wasm-host run --kit conformance/suites/wasm-host \
  --host artifacts/executables/win-arm64/morphir.exe \
  --offering native-aot --platform win-arm64 --engine "wasmtime 50.0.1" \
  --report .dev/wasm-host-native-aot.json
```

## Related

- [conformance/README.md](https://github.com/finos/morphir-dotnet/blob/main/conformance/README.md): the suite and its generator.
- `tests/Fixtures/MepWasm/`: the pinned contract and corpus.
