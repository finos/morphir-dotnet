---
title: "morphirup"
linkTitle: "morphirup"
weight: 90
description: "The standalone toolchain manager: plan-only setup and project checks over local files"
---

# morphirup

`morphirup` is the standalone Morphir toolchain manager of the approved change `add-deployment-and-acquisition` (finos/morphir, decisions UP-1, UP-2 and LOCK-1). It is a Native AOT executable, so it runs on a machine with neither .NET nor Morphir installed.

**This build does not acquire, install or activate anything.** It reads the toolchain documents, resolves a request against a recipe file you give it, prints the plan, and checks a project's request and lock. Bootstrap, authenticated downloads, generations, activation, `restore`, `show`, `run`, the `morphir` proxy and shell setup are later work.

## Implementation, deployment and toolset

A request names three things, and the manager never infers one from another:

- the **implementation** is which Morphir host, such as `dotnet`;
- the **deployment** is how that host is delivered: `native-aot`, `self-contained`, `framework-dependent` or `dotnet-tool`. Each is a separate offering with its own artifacts, so `native-aot` is never replaced by `self-contained`;
- the **toolset** is the preset of language operations, such as `fsharp`, which needs F# compilation and F# generation. Each operation gets one provider: the host's built-in, or an independently released `process`, `wasm` or `dotnet-tool` provider.

The project request is `.morphir/toolchain.toml`:

```toml
schemaVersion = "0.1.0-draft.1"
[host]
implementation = "dotnet"
deployment = "native-aot"
channel = "stable"
[toolset]
preset = "fsharp"
```

It is a separate file from `morphir.toml`. The `[toolchain.<name>]` task adapters in `morphir.toml` keep their meaning, and `morphirup` never reads or writes them.

Check in both `.morphir/toolchain.toml` and `.morphir/toolchain.lock.json`. The lock records the exact host and providers for each platform, with no local paths. Its `requestSha256` is the SHA-256 of the request file's exact bytes, so reformatting the request, even adding a blank line, needs a new lock. A colleague on another platform extends the lock with an explicit `morphirup setup --platform <rid>`; frozen restore never resolves a channel again.

## Commands

Every command writes one manager result document (`0.1.0-draft.1`, see the [contract](https://github.com/finos/morphir/blob/main/docs/spec/toolchain/manager-result.md)) to standard output and its progress to standard error. JSON is the only output format of this build; `--json` is accepted.

| Exit code | Meaning |
| --- | --- |
| 0 | The result is `planned` or `succeeded` |
| 1 | The result is `failed`: a document was refused or a check failed |
| 2 | Invocation or file error. Nothing is written to standard output |

### `morphirup --version`

Prints the version and nothing else.

### `morphirup setup --plan`

```text
morphirup setup --plan --recipe <file> --repository-id <sha256> --artifact-sizes <file>
                [--project <dir> | --request <file> | <selector options>]
                [--platform <rid>] [--expect-plan <sha256>] [--recipe-target <path>]
```

Reads the request, resolves it against the recipe for every platform the lock would record (the request's `platforms` plus the platform being set up), and prints a `planned` result with the plan and its `planSha256`. The plan names the host artifact, each provider, where each comes from, its sizes, its update owner, its prerequisites and its readiness probe. A `process` or `dotnet-tool` provider's probe is `process-with-user-rights`: setup would run that code with your rights. Planning runs nothing.

Plan-only setup is the only setup in this build: without `--plan`, or with `--yes`, it exits with code 2 and changes nothing.

| Option | Meaning |
| --- | --- |
| `--project <dir>` | Read `<dir>/.morphir/toolchain.toml`. The default is the current directory; there is no upward discovery yet |
| `--implementation`, `--deployment`, `--preset`, `--channel` or `--version` | The request as options, as in the approved design. The approved example given this way has the approved example file's identity |
| `--platform <rid>` | Plan for this platform instead of the current one |
| `--expect-plan <sha256>` | Refuse with a `failed` result (`inconsistent_document` at `planSha256` of the result) when the plan's digest differs |
| `--recipe <file>` | **Provisional.** The recipe document. Later builds take it from authenticated TUF metadata; this build trusts the file you give it |
| `--repository-id <sha256>` | **Provisional.** The repository that publishes the recipe and the host artifacts |
| `--recipe-target <path>` | **Provisional.** The recipe's target path, by default `toolchains/recipes/<preset>/<version>.json` |
| `--artifact-sizes <file>` | **Provisional.** A JSON object from target path to `{"downloadBytes": n, "installedBytes": n}`. A plan needs both sizes for every downloaded artifact, and a recipe does not carry them |
| `--request <file>` | **Provisional.** Read this request file |

A request the recipe cannot satisfy gives a `failed` result with an `unsupported-artifact` diagnostic. The manager never substitutes another implementation, deployment, release, platform or WASM ABI. A missing platform lists the recipe's alternatives; a WASM provider whose ABI the host lacks is `abi_unavailable` unless the request chooses another ABI explicitly.

```powershell
morphirup setup --plan --recipe fsharp-1.0.0.json --repository-id <sha256> --artifact-sizes sizes.json
morphirup setup --plan --implementation dotnet --deployment native-aot --preset fsharp --channel stable `
    --recipe fsharp-1.0.0.json --repository-id <sha256> --artifact-sizes sizes.json --expect-plan <sha256>
```

### `morphirup doctor`

```text
morphirup doctor [--project <dir>] [--request <file>] [--lock <file>] [--recipe <file>] [--platform <rid>]
```

Checks a project's request and lock without changing anything: both documents read, the lock binds the exact request bytes (`request_changed` otherwise), the lock records the current platform (`platform_not_locked`, with the `morphirup setup --platform <rid>` instruction), and, with `--recipe`, every closure is one the recipe allows (`lock_recipe_mismatch`). The result is `succeeded` with no diagnostics, or `failed` with every diagnostic found. Installed generations are not checked yet. `--request` and `--lock` are provisional.

## Platforms and prerequisites

`morphirup` uses the .NET runtime identifiers `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64` and `linux-musl-arm64`. A musl machine never gets a glibc closure.

The executable needs no .NET runtime. It uses the operating system's ICU to normalize strings for the plan digest: it is part of Windows 10 1903 and later and of macOS, and on Linux it is the `libicu` package.

## Common errors

- `this build plans only`: pass `--plan`. Installing is not implemented yet.
- `the size of <target> is unknown`: add the target to the `--artifact-sizes` file.
- `unsupported_contract_version` at `schemaVersion`: this build reads `0.1.0-draft.1` documents only, never another draft or the released `0.1.0`.
- `credential_in_document`: no toolchain document may hold a member such as `token`, `password` or `apiKey`, at any depth and in any letter case. The result names the member, never its value.

## Related

- [Toolchain acquisition contracts](https://github.com/finos/morphir/blob/main/docs/spec/toolchain/index.md) in finos/morphir
- `src/Morphir.Toolchain`, the document readers, resolution and planning this command uses
