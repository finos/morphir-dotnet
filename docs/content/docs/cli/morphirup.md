---
title: "morphirup"
linkTitle: "morphirup"
weight: 90
description: "The standalone toolchain manager: plan-only setup and project checks over local files"
---

# morphirup

`morphirup` is the standalone Morphir toolchain manager of the approved change `add-deployment-and-acquisition` (finos/morphir, decisions UP-1, UP-2 and LOCK-1). It is a Native AOT executable, so it runs on a machine with neither .NET nor Morphir installed.

**This build does not acquire, install or activate anything.** It reads the toolchain documents, resolves a request against a recipe file you give it, prints the plan, and checks a project's request and lock. Bootstrap, authenticated downloads, generations, activation, `restore`, `show`, `run`, the `morphir` proxy and shell setup are later work.

## Download and verify

Releases of morphir-dotnet attach `morphirup` for four platforms, beside the `morphir` assets on the
[releases page](https://github.com/finos/morphir-dotnet/releases):

| Platform | Executable | Archive |
| --- | --- | --- |
| Linux glibc x64 | `morphirup-linux-x64` | `morphirup-linux-x64-v<version>.tar.gz` |
| Linux glibc ARM64 | `morphirup-linux-arm64` | `morphirup-linux-arm64-v<version>.tar.gz` |
| Windows x64 | `morphirup-win-x64.exe` | `morphirup-win-x64-v<version>.tar.gz` |
| macOS ARM64 | `morphirup-osx-arm64` | `morphirup-osx-arm64-v<version>.tar.gz` |

A Linux executable needs glibc 2.35 or later from the machine, which Ubuntu 22.04, Debian 12 and later releases of both
have, and nothing else: no .NET, no ICU and no OpenSSL. It does not run on musl distributions such as Alpine.

Each archive holds the same executable under `<rid>/`, for example `linux-x64/morphirup`, with the repository's
`LICENSE` and `NOTICE` and the executable's `THIRD-PARTY-NOTICES.txt` beside it. The release also carries that file as
`morphirup-v<version>-THIRD-PARTY-NOTICES.txt`, for the bare executables: it lists the packages, the .NET runtime and
the Unicode data that Native AOT compiled in, with their licenses and notices. The file
`morphirup-v<version>.sha256` lists the SHA-256 of every `morphirup` asset of the release, the notices file included,
in the format that `sha256sum -c` reads. It shows that the file you have is the file the release published. It is not a signature:
verifying signed release metadata is later work.

On Linux:

```bash
version=0.4.0-alpha.4   # the release you want
base=https://github.com/finos/morphir-dotnet/releases/download/v$version
curl -fLO "$base/morphirup-linux-x64"
curl -fLO "$base/morphirup-v$version.sha256"
sha256sum -c --ignore-missing "morphirup-v$version.sha256"
chmod +x morphirup-linux-x64
./morphirup-linux-x64 --version
```

On macOS, compare the digest with its line in the checksum file:

```bash
shasum -a 256 morphirup-osx-arm64
grep ' morphirup-osx-arm64$' "morphirup-v$version.sha256"
```

On Windows, in PowerShell:

```powershell
(Get-FileHash .\morphirup-win-x64.exe -Algorithm SHA256).Hash.ToLowerInvariant()
Select-String -Path ".\morphirup-v$version.sha256" -Pattern ' morphirup-win-x64.exe$'
```

The release job checks each executable on a GitHub-hosted runner of its platform. It runs the published file from a
folder whose path has a space, with `PATH` reduced to the system folders and no `DOTNET_*` variable. The file must
print the release version and make a plan-only setup whose plan digest matches a recorded one.

A Native AOT executable needs the glibc of the machine that linked it, or newer. So the Linux executables are linked
in an `ubuntu:22.04` image pinned by digest, which has glibc 2.35, and not on the runner. The release job reads the
glibc versions each Linux executable needs (`readelf --version-info`) and fails if one is above 2.35. It then runs the
same commands in `debian:12-slim` (glibc 2.36) and in the `ubuntu:22.04` build image (glibc 2.35), after checking that
neither image has .NET, `libicu`, `libssl` or `libcrypto`. `ubuntu:22.04` ships OpenSSL (`libssl3`), so the check
removes that package from a copy of the image first. It also checks that each archive holds exactly the four
files above and that the notices name every package the compiler linked. Nothing more is checked: no other
distribution, glibc version or operating-system version is tested.

The executables are not code-signed, and the macOS executable is not notarized. On macOS, a file that a browser
downloads carries the quarantine attribute, and Gatekeeper refuses to open it because Apple cannot check it for
malicious software; a file that `curl` downloads has no quarantine attribute. After you have checked its digest, you
can remove the attribute with `xattr -d com.apple.quarantine morphirup-osx-arm64`. On Windows, SmartScreen can warn
about an unsigned file that a browser downloads. Neither behavior has been checked for these files on a Mac or a
Windows desktop.

This first release reads documents and plans. It does not download, install, update or activate anything, and it does
not verify signed metadata.

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

Check in both `.morphir/toolchain.toml` and `.morphir/toolchain.lock.json`. The lock records the exact host and providers for each platform, with no local paths. Its `requestSha256` is the SHA-256 of the request file's exact bytes, so reformatting the request, even adding a blank line, needs a new lock. A colleague on another platform will extend the lock with an explicit setup for that platform; this build does not implement that yet, and `morphirup setup --plan --platform <rid>` only shows the plan. Frozen restore never resolves a channel again.

## Commands

`setup --plan` and `doctor` write one manager result document (`0.1.0-draft.1`, see the [contract](https://github.com/finos/morphir/blob/1451c081f56cf5b74618302d315dcdbe573028a1/docs/spec/toolchain/manager-result.md), a draft in the unmerged [finos/morphir#1147](https://github.com/finos/morphir/pull/1147), at the commit this build is pinned to) to standard output and their progress to standard error. `--version`, `--help` and a command line that cannot be parsed write no result document. JSON is the only output format of this build; `--json` is accepted.

| Exit code | Meaning |
| --- | --- |
| 0 | The result is `planned` or `succeeded` |
| 1 | The result is `failed`: a document was refused or a check failed |
| 2 | A command line this build cannot run: an unknown or repeated option; a missing value; an invalid option value; `setup` without `--recipe` or `--repository-id`; a machine that is not one of the [supported platforms](#platforms-and-prerequisites); a downloaded artifact whose size is missing from `--artifact-sizes`. Nothing is written to standard output; the message is on standard error |
| 3 | **Provisional.** Internal error. Nothing is written to standard output. The result contract defines exit codes 0, 1 and 2 only |

A request, lock or recipe that is absent or cannot be read is not exit code 2: the command runs and fails with `document_unavailable` (`unsupported-contract`, naming the document, at the empty path), exit 1.

An option value never starts with `--`: `--project --plan` is refused as a missing value rather than read as a folder named `--plan`.

<!-- PENDING (manager-core worker, via the orchestrator): sentences on the plan digest and CRLF, `--repository-id`,
     the nesting bound and the discovery boundary go here. Do not write them here; they come from fix/morphirup-prerelease-review. -->
**Pending:** the sentences on the plan digest and CRLF line endings, `--repository-id`, the nesting bound and the
discovery boundary are still to come.

### Files

`morphirup` reads a request, lock, recipe or sizes file only when it is a regular file. A directory, device, pipe or socket is refused, and so is a symbolic link to one. A file that does not open within five seconds is refused. A refused request, lock or recipe is `document_unavailable`; an unreadable sizes file, which is this build's own input, is exit 2. The size bounds are provisional, until the contracts state their own:

| File | Largest size read |
| --- | --- |
| Request (`.morphir/toolchain.toml` or `--request`) | 1 MiB |
| Lock (`.morphir/toolchain.lock.json` or `--lock`) | 1 MiB |
| `--artifact-sizes` | 1 MiB |
| `--recipe` | 16 MiB |

A larger file is refused before it is parsed.

### Finding the project

Without `--request` or the selector options, `morphirup` finds the project request by discovery: the nearest `.morphir/toolchain.toml` in the start directory or one of its parents, up to and including the discovery boundary. Nothing above the boundary is read, and requests are never merged, so a workspace member with its own request uses it and a member without one uses the workspace root's. The start is `--project <dir>`, or the current directory; the boundary is `--boundary <dir>`, or by default the root of the start path, as for `morphir`'s own configuration discovery. A start outside the boundary has no project request. `doctor` reads the lock beside the request it found. `--project` and `--boundary` are provisional.

### `morphirup --version`

Prints the version and nothing else.

### `morphirup setup --plan`

```text
morphirup setup --plan --recipe <file> --repository-id <sha256> --artifact-sizes <file>
                [--project <dir> [--boundary <dir>] | --request <file> | <selector options>]
                [--platform <rid>] [--expect-plan <sha256>] [--recipe-target <path>]
```

Reads the request, resolves it against the recipe for every platform the lock would record (the request's `platforms` plus the platform being set up), and prints a `planned` result with the plan and its `planSha256`. The plan names the host artifact, each provider, where each comes from, its sizes, its update owner, the prerequisites that apply on its platform and its probe. It also carries the complete lock setup would write, with every platform's closure, so `planSha256` pins all of it. The host and every `process` or `dotnet-tool` provider are `process-with-user-rights`: setup's readiness checks would run that code with your rights. A built-in or WASM provider is `in-host`. Planning runs nothing.

Plan-only setup is the only setup in this build: without `--plan`, or with `--yes`, it exits with code 2 and changes nothing. Installing is not implemented yet.

| Option | Meaning |
| --- | --- |
| `--project <dir>` | **Provisional.** Where [discovery](#finding-the-project) starts. The default is the current directory |
| `--boundary <dir>` | **Provisional.** The discovery boundary. The default is the root of the start path |
| `--implementation`, `--deployment`, `--preset`, `--channel` or `--version` | The request as options, as in the approved design. The approved example given this way has the approved example file's identity |
| `--platform <rid>` | Plan for this platform instead of the current one |
| `--expect-plan <sha256>` | Refuse with a `failed` result (exit 1) when the plan's digest differs: `plan_changed` at `planSha256`, naming both digests. Nothing is changed |
| `--recipe <file>` | **Provisional.** The recipe document. Later builds take it from authenticated TUF metadata; this build trusts the file you give it |
| `--repository-id <sha256>` | **Provisional.** The repository that publishes the recipe and the host artifacts. A recipe provider bound to another repository is `repository_mismatch` |
| `--recipe-target <path>` | **Provisional.** The recipe's target path, by default `toolchains/recipes/<preset>/<version>.json`. A target that is not portable, such as one with a `con` segment, is `non_portable_path` before the recipe is read |
| `--artifact-sizes <file>` | **Provisional.** A JSON object from target path to `{"downloadBytes": n, "installedBytes": n}`. A plan needs both sizes for every downloaded artifact. Later builds take `downloadBytes` from the authenticated target length; the contract has not settled where `installedBytes` comes from |
| `--request <file>` | **Provisional.** Read this request file |

A request the recipe cannot satisfy gives a `failed` result with an `unsupported-artifact` diagnostic. The manager never substitutes another implementation, deployment, release, platform or WASM ABI. A missing platform lists the recipe's alternatives; a WASM provider whose ABI the host lacks is `abi_unavailable` unless the request chooses another ABI explicitly.

```powershell
morphirup setup --plan --recipe fsharp-1.0.0.json --repository-id <sha256> --artifact-sizes sizes.json
morphirup setup --plan --implementation dotnet --deployment native-aot --preset fsharp --channel stable `
    --recipe fsharp-1.0.0.json --repository-id <sha256> --artifact-sizes sizes.json --expect-plan <sha256>
```

### `morphirup doctor`

```text
morphirup doctor [--project <dir>] [--boundary <dir>] [--request <file>] [--lock <file>] [--platform <rid>]
                 [--recipe <file> --repository-id <sha256>]
```

Checks a project's request and lock without changing anything:

- both documents can be read (`document_unavailable` otherwise) and read as documents;
- the lock binds the exact request bytes, hashed as read (`request_changed` otherwise). When the bytes differ only by CRLF line endings, a UTF-8 byte order mark or both, the diagnostic's `cause` says so and its message names the `.gitattributes` entry, `.morphir/toolchain.toml text eol=lf`, that prevents it. A converted file is never accepted;
- the lock records the current platform (`platform_not_locked` otherwise). An explicit setup for that platform will extend the lock; this build does not implement it yet. The diagnostic says so and names only `morphirup setup --plan --platform <rid>`, which shows the plan;
- with `--recipe` and `--repository-id`, the lock names that repository (`repository_mismatch` otherwise) and every closure is one the recipe allows (`lock_recipe_mismatch`). `--recipe` needs `--repository-id`, because the lock-against-recipe check takes the repository as an input.

A recipe that is refused is reported even when the lock is refused too. The result is `succeeded` with no diagnostics, or `failed` with every diagnostic found. Installed generations are not checked yet. `--project`, `--boundary`, `--request`, `--lock`, `--recipe` and `--repository-id` are provisional.

## Platforms and prerequisites

`morphirup` uses the .NET runtime identifiers `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64` and `linux-musl-arm64`. A musl machine never gets a glibc closure. These are the platforms it can plan for; releases publish the executable for four of them, listed under [Download and verify](#download-and-verify).

The executable needs no .NET runtime and no ICU: it is built in invariant globalization mode, and it normalizes strings for the plan digest to NFC with its own Unicode 17.0.0 tables. So it runs on a Linux machine without the `libicu` package, and a plan digest is the same whatever Unicode version the machine has.

## Common errors

- `this build plans only`: pass `--plan`. Installing is not implemented yet.
- `document_unavailable`: the named request, lock or recipe does not exist, is over the bound in [Files](#files), or is not a regular file. Without `--request`, check where [discovery](#finding-the-project) looked.
- `request_changed` with `cause` `crlf-line-endings`: Git converted the request's line endings. Add `.morphir/toolchain.toml text eol=lf` and `.morphir/toolchain.lock.json text eol=lf` to `.gitattributes` and check the files out again.
- `the size of <target> is unknown`: add the target to the `--artifact-sizes` file.
- `unsupported_contract_version` at `schemaVersion`: this build reads `0.1.0-draft.1` documents only, never another draft or the released `0.1.0`.
- `credential_in_document`: no toolchain document may hold a member such as `token`, `password` or `apiKey`, at any depth and in any letter case. The result names the member, never its value.

## Related

- [Toolchain acquisition contracts](https://github.com/finos/morphir/blob/1451c081f56cf5b74618302d315dcdbe573028a1/docs/spec/toolchain/index.md) in finos/morphir: a draft, in the unmerged [finos/morphir#1147](https://github.com/finos/morphir/pull/1147), at the commit this build is pinned to
- `src/Morphir.Toolchain`, the document readers, resolution and planning this command uses
