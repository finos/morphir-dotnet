---
title: "PRD: Morphir extension model"
description: "An incremental roadmap for built-in, WASM and process extensions in morphir-dotnet, on the upstream Morphir Extension Protocol."
status: Approved
created: 2026-11-06
last_updated: 2026-11-07
---

# Morphir extension model

This PRD is a roadmap. It sets the order of delivery and the decisions that every step must keep. Each step becomes its own OpenSpec change with its own design, specs, tasks and Morphir Compatibility Kit (MCK) suite. No step in this PRD is approved for implementation until its OpenSpec change is approved.

## Why

People want to extend Morphir without changing morphir-dotnet: to add their own model rules, frontends and code generators, in the language they use. Morphir is cross-language, so the extension contract must not depend on .NET types.

morphir-dotnet ships two hosts, and they have different limits:

| Host | Package | Runtime | Can load .NET assemblies at runtime |
| --- | --- | --- | --- |
| `morphir` | `src/Morphir` | Native AOT, trimmed, single file | No |
| `dotnet-morphir` | `src/Morphir.Tool` | JIT dotnet tool | Yes |

An extension model that depends on loading assemblies (MEF, `AssemblyLoadContext`) cannot work in the AOT host. So the external extension runtimes are WASM and child processes, and both hosts use the same runtimes.

## Goals

- Deliver extension capabilities in small steps. Each step ships something a user can run. There is no big-bang delivery.
- Make WASM the cornerstone for external extensions. A WASM extension runs the same way in both hosts.
- Let a team write an extension in C# or F# and ship it in the first external release.
- Share the upstream design: the Morphir Extension Protocol (MEP), the extension store and the lock format of `finos/morphir`. An extension that the Rust or MoonBit CLI installs must work in morphir-dotnet, and the reverse.
- Show built-in and external extensions in one list, with the same metadata.
- Pass the trimming and Native AOT checks in every step, with zero IL2026 and IL3050 warnings and no suppressions.

## Non-goals

- `AssemblyLoadContext` plugins. They come later, only in `dotnet-morphir`, and only if people ask for them.
- MEF or reflection-based discovery.
- The WASM Component Model and WIT. Upstream will make a WIT world from the MEP contract later. Until then, morphir-dotnet uses the upstream core-module ABIs.
- Extension signing. It is an open question upstream.
- Remote or Git extension repositories.

## Anti-goals

The work fails, even if it runs, when any of these is true:

- morphir-dotnet defines a protocol member that conflicts with MEP or is not in MEP. A missing contract need becomes an upstream issue (see [MEP decisions](#decisions)).
- An extension that works in one host does not work in the other host.
- The store, catalog or global lock format differs from upstream, so the CLIs cannot share installed extensions. (The level files and staged folders are an additive superset. Upstream CLIs ignore them. See [step 3](#step-3-installation).)
- A WASM guest writes files, reads the network or reads the environment without a grant from the host. (A process guest runs with the rights of the user, as any installed tool does. See [Trust](#trust).)
- A step writes logs, progress or install messages to stdout. Stdout carries command output only.

## Decisions

These decisions come from the product owner. Each step must keep them.

| Area | Decision |
| --- | --- |
| Model | There are built-in extensions and external extensions. One contract covers both. |
| External runtime | WASM is the cornerstone. The `process` runtime (MEP over stdio) is also in the first external release, for C# and F# authors. |
| First capability | An **analyzer**: it reads a model and returns diagnostics. It does not change the model and does not write files. |
| Model format | Extensions receive IR V4 as JSON first. IR V4 Ion comes later. |
| Discovery | A user can drop an extension file into a `staged` folder under `.morphir/` at each discovery level (system, global, workspace, project). Morphir installs it. |
| Staged formats | A `staged/` folder accepts a bare `.wasm` module and the extension package archive: one zip or tar gzip archive of the loose release assets (the release descriptor, the artifacts and their `.sha256` files). See [Extension package archive](#extension-package-archive). |
| Install mode | A setting controls the install mode. With `auto`, Morphir installs staged files on the next command. With `manual`, it installs them only on `morphir extension install`. The default is `auto` before 1.0. The default can change to `manual` when the extension tooling is stable. |
| Upstream | morphir-dotnet shares the upstream MEP, store and lock format. It reviews the upstream pattern for .NET needs and takes the differences upstream as proposals. |
| JSON-RPC | Research AOT-safe JSON-RPC libraries before writing our own layer. A spike decides (see [step 1](#step-1-mep-core-for-net)). |
| Built-ins | The built-in route stays open for C# extensions. The F# frontend cannot be a built-in in the AOT host, because FSharp.Compiler.Service is not AOT-safe. |

MEP decisions that already apply in this repository:

- Target the latest upstream MEP contract, `0.2.0-draft.1`, and its model transport. Do not implement the legacy `0.1` protocol.
- Do not add protocol members. If MEP lacks something, raise an upstream issue and wait for the contract, or deliver only the in-process part.
- Where upstream behavior is wrong, implement the intended behavior, record the deviation with evidence and raise an upstream issue. Do not copy a defect and do not diverge without a record.

## Concepts

### Extension, capability and delivery

An **extension** has an identity (`ExtensionInfo`) and claims one or more **capabilities** (MEP capability claims). The MEP extension types are `frontend`, `backend`, `transform`, `validator` and `workspace`.

The user-facing name of the first capability is "analyzer". On the wire it is the MEP `validator` type and the `morphir.validator.*` methods. Do not add a new extension type.

A **delivery** is one way to run an extension. One extension can have more than one delivery:

| Delivery | Runs in | Example |
| --- | --- | --- |
| Built-in, both hosts | `morphir` and `dotnet-morphir` | The C# and F# generators. A C# extension with an AOT-clean dependency set. |
| Built-in, JIT host only | `dotnet-morphir` | The F# frontend, because of FSharp.Compiler.Service. |
| Process guest | both hosts | The F# frontend guest `morphir-fsharp-frontend`. Third-party C# and F# analyzers. |
| WASM guest | both hosts | Rust and MoonBit analyzers. |

The language does not decide whether an extension can be a built-in. The dependency set decides: a built-in for the AOT host must pass the AOT checks.

### Selection rule

For each capability request, the host selects one delivery:

1. If the user configured an extension for the capability, use it.
2. If there is no configured extension, use the built-in for this host.
3. If there is no built-in in this host, fail with a diagnostic that says "not available in this build" and names the guest to install.

The `add-fsharp-compile` change already uses this rule for frontends. The rule applies to every capability where one provider serves a request: a frontend for a language, a backend for a target.

**Analyzers are additive (builder's call).** For the `validator` capability, all enabled analyzers run, together with the enabled built-in analyzers. Their diagnostics merge into one result. `extensions.<name>.enabled = false` turns one off. The host does not read the capability from configuration. It calls `morphir.extension.describe` on each configured extension and uses the capabilities that the extension claims.

### Trust

- **WASM guests** run in a sandbox. They get no file system, network or environment access unless the host grants it (step 5).
- **Process guests** run with the rights of the user, as any installed dotnet tool or executable does. morphir-dotnet does not add an OS sandbox. The documentation must say this clearly.
- **Built-ins** are part of morphir-dotnet.

### Runtimes

| Runtime | Transport | Framing |
| --- | --- | --- |
| `process` | JSON-RPC 2.0 over stdio | `Content-Length` headers, as in LSP. Stdout carries frames only; stderr carries logs. |
| `wasm` | One JSON-RPC message in and one out for each `handle` call | No stream. The host drops guest notifications. |

Upstream limits that the host must apply:

- `process`: a payload of at most 64 MiB and headers of at most 8192 bytes (the Rust host, `crates/morphir-host-native/src/process/frame.rs` in `finos/morphir-rust`).
- `wasm`: 256 MiB of memory, 30 s for each call, and a fuel limit. Upstream sources do not agree on fuel (100M in the proposal, 1B in the Rust policy). See [open points](#open-points).
- Request ids are an unsigned 64-bit number or a string. Keep the full u64 range.

Upstream WASM ABIs (`finos/morphir-moonbit`, `pkgs/morphir-host/worker/wasm/README.mbt.md`):

- `morphir-bytes-v1`: a plain core WASM module. It imports only `morphir_bytes_v1.input_len`, `input_byte` and `output_byte`. The `handle` export returns a positive response length, 0 for exit, -1 for failure and -2 for a notification.
- `extism-kernel-v1`: Extism imports. The `handle` export takes no arguments.

morphir-dotnet hosts `morphir-bytes-v1` first, because any WASM runtime can host it. It adds `extism-kernel-v1` later, so that the published upstream bundles run (step 5).

### Discovery levels and installation

morphir-dotnet uses the upstream content-addressed store. It adds levels and staged folders in the same way that NuGet and `dotnet tool` work: binaries go in one shared store, and each level keeps only selection and lock files.

| Item | Location | Owner |
| --- | --- | --- |
| Store | `MORPHIR_HOME/store/extensions/sha256/<digest>/<file>` | Upstream layout |
| Catalog | `MORPHIR_HOME/catalog/extensions.json` | Upstream layout |
| Global locks | `MORPHIR_HOME/locks/extensions/<id>.json` | Upstream layout |
| Interprocess lock | `MORPHIR_HOME/locks/extensions.state.lock` | Upstream layout |
| Staged folder | `<level>/.morphir/extensions/staged/` | New. Proposed upstream. |
| Level selection and lock | `<level>/.morphir/extensions/` | New. Proposed upstream. |

`MORPHIR_HOME` is `~/.morphir`, or the value of `$MORPHIR_HOME`. The levels are system, global, workspace and project. `ProjectSystem` and `MachineLocations` in `src/Morphir.Configuration.IO` already find them. Remove the second location implementation in `src/Morphir.Tooling/Configuration/OsPaths.cs` when step 3 lands.

Rules:

- Morphir never runs a file from a `staged/` folder.
- Each `staged/` folder acts as an implicit local repository. Install reads the extension's `describe` result, checks the MEP contract version, computes the SHA-256 digest, copies the file into the store atomically, and writes the lock entry at the level where the file was staged. The provenance kind is `staged`.
- A `staged/` folder accepts a bare `.wasm` module and the [extension package archive](#extension-package-archive) in zip or tar gzip format. A process guest must come in an extension package archive, because only the descriptor names the executable for each target. Install ignores other files and reports each one with a warning on stderr.
- For a bare `.wasm` module, the identity, version and capabilities come only from `describe`. For an extension package archive, they come from the release descriptor, and `describe` must agree with it.
- If the same extension id is selected at more than one level, the nearest level wins: project, then workspace, then global, then system. This matches configuration resolution.
- `extensions.<name>.enabled = false` in configuration turns an extension off at that level. The `extensions` table already exists in `src/Morphir.Configuration/Schema.fs`, with `path`, `url`, `command`, `args`, `enabled` and `config`.
- Install writes use the same stage-then-move pattern as `ConfigurationTransaction` and `FileTree` publication. Concurrent CLI runs take turns through the interprocess lock.
- Install messages go to stderr. `--json` output on stdout stays parseable.

Install mode setting (builder's call on the name; the `extensions` table cannot hold it, because each key there is an extension name):

```toml
[extension_install]
staged = "auto"   # or "manual"
```

### Extension package archive

The extension package archive is one archive of the loose assets of one upstream extension release (decided 2026-11-07). It has two formats with the same content: zip (`.zip`) and tar gzip (`.tar.gz` or `.tgz`). Upstream publishes these assets as separate files, and it has no single-file form yet:

- A WASM release (for example `extension/avro/v0.4.0` in `finos/morphir-extensions`) has `morphir-avro-0.4.0.release.json`, `morphir-avro-0.4.0.wasm` and a `.sha256` file for each.
- A process release (for example `extension/elm-classic/v0.6.0` in `finos/morphir-elm`) has a `*.bundle.release.json` descriptor and one archive for each target (`.tgz`, or `.zip` for Windows) with a `.sha256` file for each. Each target archive holds only the executable.

Layout at the root of the archive:

| Entry | Required |
| --- | --- |
| The release descriptor (`*.release.json`, descriptor `2.0.0-draft.2`) | Yes, exactly one |
| The descriptor's `.sha256` file | Yes |
| Each artifact that the descriptor names (a `.wasm` module, or a target archive) | At least the artifact for the host's target |
| Each artifact's `.sha256` file | Yes, for each artifact present |

The archive can leave out the target archives for other platforms. The file names stay the same as the upstream release asset names, so a user can make the archive from a release page without renaming files. The outer format does not have to match the format of the target archives inside it. For example, a `.zip` package can hold `.tgz` target archives.

Install reads the format from the file content, not from the file name: a zip starts with the bytes `50 4B 03 04`, and a gzip stream starts with `1F 8B`. A gzip stream must hold a tar archive. Install does not accept a gzip stream that holds only one file, or a tar archive with no compression (builder's call; the upstream proposal can add more formats). Both formats use only the base class library: `System.IO.Compression.ZipArchive`, and `System.Formats.Tar.TarReader` over `GZipStream`. The step 3 change confirms that both publish with no trim or AOT warnings.

Install of an extension package archive:

1. Read the archive without extracting to the destination. Reject an entry with an absolute path, a `..` segment, a drive letter, or a duplicate name. Accept only regular file entries and folder entries. For tar, this rejects symbolic links, hard links, device files and FIFOs, and it applies the path rules after extended (PAX or GNU long name) headers change the path of an entry. Apply limits to the entry count, the size of each entry and the total extracted size, and reject a compression ratio above the limit. The limits apply to the zip compression and to the gzip stream in the same way. The builder sets the limits and records them.
2. Check the descriptor against its `.sha256` file. Check each artifact that install uses against its `.sha256` file and against the digest that the descriptor claims. Any mismatch stops the install, and nothing goes into the store.
3. Select the artifact. A WASM release has one artifact. A process release has one artifact for each target. Install maps the host's .NET runtime identifier to the upstream target triple (for example `win-x64` to `x86_64-pc-windows-msvc`) and selects that artifact. If there is no artifact for the host's target, install stops with an error that names the targets the archive has.
4. For a target archive (`.zip` or `.tgz`), extract the executable with the rules of step 1, check its digest if the descriptor claims one, and set the execute permission on Unix. Install does not copy permission bits or owners from tar entries.
5. Run `describe` on the artifact with the limits of its runtime. The id, version and capabilities must agree with the descriptor.
6. Copy the artifact and the descriptor into the store in the upstream layout, with stage-then-move. Write the lock entry with provenance kind `staged` and the SHA-256 digest of the archive.

Upstream accepts no archive today ("does not accept a raw `.wasm` file, an archive, a URL, or an unregistered directory"). The extension package archive is part of the documented superset, and the step 3 proposal to upstream includes it.

## Delivery steps

Each step merges on its own. External runtimes stay behind an experimental flag until the first external release. Steps 2a and 2b ship together as the first external release.

### Step 0: Extension registry

- Built-ins register with MEP `ExtensionInfo` and capability claims, through static DI registration. There is no assembly scanning.
- The first built-ins are the C# and F# generators. The plugins in `src/Morphir.IR.Pipeline.Plugins` register in step 1 (decided 2026-11-07): no CLI command runs them, so an entry for them would claim a capability that nothing can use.
- The registry domain is the F# project `src/Morphir.Extensions`, with parsed types and closed unions; the CLI edge stays C# (decided 2026-11-07). Its rules for C# callers are in the `fsharp-csharp-interop` skill.
- Extension ids are one or more lowercase portable tokens joined by dots, such as `morphir.csharp`. Each token follows the upstream rule; the dots are a superset of upstream (decided 2026-11-07), proposed upstream in [finos/morphir#1137](https://github.com/finos/morphir/issues/1137) and [finos/morphir-rust#329](https://github.com/finos/morphir-rust/issues/329).
- `morphir extension list` shows the built-ins, with their deliveries and the host they run in. The other `extension` commands stay as they are until step 3.
- No other behavior changes.
- This step does not wait for `Morphir.Mep` (builder's call). The registry uses its own types in the shape of `ExtensionInfo` and capability claims, and maps them to the `Morphir.Mep` types when those land.

### Step 1: MEP core for .NET

This step is mostly the `Morphir.Mep` project that the [`add-fsharp-compile`](https://github.com/finos/morphir-dotnet/pull/495) change plans: the MEP `0.2.0-draft.1` types with source-generated JSON, `Content-Length` framing, the session core, the host client and the guest loop. This PRD does not plan a second MEP layer. It adds these items:

- **Result (2026-11-07, [JSON-RPC spike](../spikes/extension-model/jsonrpc.md)):** `Morphir.Mep` writes its own minimal JSON-RPC 2.0 layer on System.Text.Json. StreamJsonRpc 2.26 has 10 AOT warnings, adds 2.6 MB and closes the connection on a u64 id above `long.MaxValue`. CurlyRpc 0.9.9 has no warnings but cannot bind a params object with exactly one member, such as `DescribeParams`. Neither dispatches a single message without a connection, which the WASM path needs.
- **JSON-RPC spike, before `Morphir.Mep` writes its framing and session core.** Publish two small AOT console apps, one with CurlyRpc and one with StreamJsonRpc. Record the IL warnings and the binary size. The research found:
  - StreamJsonRpc 2.26: its `SystemTextJsonFormatter` has `RequiresDynamicCode` and `RequiresUnreferencedCode` attributes, numeric ids are `long`, there is no single-message dispatch, and its dependencies are large. Zero warnings is not possible without suppressions.
  - CurlyRpc 0.9.9: marked AOT-compatible and trimmable, with reflection-free typed handlers, `Content-Length` framing and `$/cancelRequest`. It is pre-1.0, from one contributor, and has no single-message dispatch.
  - The ModelContextProtocol C# SDK: AOT-compatible, but its session engine is internal and its stdio framing is newline-delimited. It is useful prior art for the message converter.
  - OmniSharp, EdjCase and Polhem are not suitable. No maintained AOT-safe F# library was found.
  - If neither library passes, write a minimal layer on System.Text.Json (estimate: 800–1,100 lines with tests): messages with a hand-written converter, a dispatcher with `DispatchAsync(message bytes) → response bytes`, a `Content-Length` framer, and a connection with a pending-request table, `$/cancelRequest` and progress notifications. The WASM path always needs our own dispatcher, because no library has single-message dispatch.
  - Research notes: the "JSON-RPC research" section in [Evidence](#evidence).
- **Generate the MEP types from the contract model** if the generators can do it: `spec/mep/generated/mep.ir.json` in `finos/morphir` is Morphir IR (package `morphir/mep`). If the generators cannot do it yet, record the gap and write the types by hand against the pinned contract.
- **The built-in `TypeValidator` runs through an in-process validator contract, and registers as a `validator` extension entry.** This proves the analyzer shape without a runtime risk. The in-process diagnostic shape follows the MEP `Diagnostic` type and the pipeline `VMessage` fields (severity, message, location, rule id, source).
- **Write the upstream proposal for the validate payload.** morphir-dotnet writes the MEP change in `finos/morphir` (decided 2026-11-06). Step 2 can build against the upstream draft PR before it is published. Pin the draft by commit, record its provenance, and move to the published version when it lands. MEP has the `validator` type, the `morphir.validator.validate` method name and the `ValidationError` code (-32003), but no request or result type, no validator capability record, and no `validateModel` model-transport operation. `spec/mep/README.md` lists "validate and transform payloads" as not covered yet. Step 2 needs this contract.
- **MEP version moves (builder's call, from the "latest only" rule).** When upstream publishes the contract version that has the validate payload, all MEP parts in morphir-dotnet move to that version together: `Morphir.Mep`, the host client, the F# frontend guest and the .NET guest SDK. morphir-dotnet does not keep `0.2.0-draft.1` beside it, because drafts match only exactly.

### Spikes before step 2

- **WASM runtime in both hosts.** Host a `morphir-bytes-v1` guest in the AOT single-file binary and in a RID-specific `dotnet-morphir` tool. Compare Wasmtime for .NET, Extism for .NET and a pure managed interpreter. Measure: AOT warnings, native library handling in a single file, binary size, fuel and timeout support, and start time. Both Wasmtime and Extism wrap native libraries.
  - **Result (2026-11-07, [WASM runtime spike](../spikes/extension-model/wasm-runtime.md)):** Wasmtime for .NET (`Wasmtime` 50.0.1). It is the only candidate that enforces the memory ceiling, fuel and the deadline, with zero AOT warnings. `morphir` links `libwasmtime.a` statically and stays one file (about +10.6 MB, so about 28.8 MB in total). `dotnet-morphir` becomes a RID-specific tool. Extism's .NET SDK cannot return the result of `handle`; the managed interpreter (WACS) cannot stop an endless loop. The static link is tested on osx-arm64 only.
  - **Size:** the WASM runtime can take the `morphir` binary past the 20 MB guideline in `.agents/aot-optimization.md` (decided 2026-11-06). Record the measured size. Size alone does not reject a native runtime.
  - **Packaging:** `dotnet-morphir` can become a RID-specific dotnet tool (decided 2026-11-06). This changes how users install it, so the change that does it must update the install documentation and the release notes.
- **.NET guest toolchains.** Confirm the current state of C# and F# compiled to WASM: toolchain maturity, module size and F# support. This decides whether the .NET guest SDK targets WASM now or only `process`.
  - **Result (2026-11-07, [.NET guest toolchain spike](../spikes/extension-model/dotnet-guest-toolchains.md)):** step 2b targets `process` only; WASM guests for C# and F# come later. No supported toolchain makes a core module with only the three `morphir-bytes-v1` imports; NativeAOT-LLVM is experimental and has no macOS arm64 host package. On Linux, the C# and F# guests link only as WASI Preview 2 modules (2.1 MB and 4.1 MB) with about 30 WASI imports, and the closing step fails, so no call ran.
- **Roslyn in the AOT host.** Check whether a C# frontend built on Roslyn passes the AOT checks. This decides whether a C# frontend can be a built-in in `morphir`.
  - **Result (2026-11-07, [Roslyn AOT spike](../spikes/extension-model/roslyn-aot.md)):** feasible. With only `Microsoft.CodeAnalysis` and `Microsoft.CodeAnalysis.CSharp` (no Workspaces, emit or analyzer loading), the AOT results equal JIT, with 6 known Roslyn warnings, and one file starts in 21 ms against 380 ms under JIT. It adds about 31 to 38 MB to `morphir`. **Decided 2026-11-07: a C# frontend ships as an optional extension for now, not as a built-in**, so users who do not use C# do not pay the size. The spike shows it can become an AOT built-in later.

### Step 2a: WASM analyzers

- The host runs `morphir-bytes-v1` guests in both hosts, with the memory, time and fuel limits from [Runtimes](#runtimes). The guest gets no file system, network or environment access.
- The guest receives IR V4 JSON through the MEP model transport (`morphir-json` codec, `morphir-ir` profile) and returns MEP diagnostics.
- **Command (builder's call):** `morphir validate --input <file>`, which is the command name that the upstream Rust CLI reserves (`crates/morphir/src/commands/validate.rs`, a stub now). It reads an IR file in the same way that `morphir generate --input` does. A later change can run analyzers as a stage of `morphir compile` and read the most recent compile record.
- **Classic IR (builder's call):** external analyzers receive V4 only. For a classic model (v1 to v3), the command skips external analyzers and reports one diagnostic that says so. It does not convert between IR families. Built-in analyzers that support classic IR still run.
- Analyzer diagnostics merge with the built-in diagnostics. An `error` fails the run, as the pipeline rules say.
- The user names the extension in configuration: `extensions.<name>.path`. There is no folder discovery in this step. Configuration layering already covers every level.
- A sample analyzer in Rust or MoonBit, with an MCK suite.
- **Reference guest from upstream (noted by the user 2026-11-07).** `finos/morphir-elm` (vnext) publishes `morphir-bytes-v1` WASM releases under `extension/elm/*`; `v0.5.0-preview.2` answers `morphir.extension.describe` with its `release.json` claims under `wasmtime` (see the [WASM runtime spike](../spikes/extension-model/wasm-runtime.md#reference-guest-morphir-elm)). The host tests run it for the session lifecycle (`describe`, `initialize`, `shutdown`, `exit`) and the limits, and later for `compileModel` and `generateModel`. A guest that morphir-dotnet did not write is the best check that the host follows MEP.
- Depends on: the upstream validate payload, and the WASM runtime spike.

### Step 2b: Process analyzers

- The host client from `Morphir.Mep` grows from `compileModel` to the validator methods.
- A .NET guest SDK for C# and F# authors. It hides framing, the handshake and dispatch.
- Sample analyzers in C# and in F#, packaged as framework-dependent dotnet tools, with an MCK suite.
- **Reference guest from upstream.** `finos/morphir-elm` publishes `process` builds of the same extension under `extension/elm/*` (to `v0.4.1`) and `extension/elm-classic/*`, for each platform. The process host tests run them for the session lifecycle and model transport, in the same way as the WASM reference guest.
- The user names the extension in configuration: `extensions.<name>.command` and `args`.
- Depends on: step 1 and the upstream validate payload.

### Step 3: Installation

- Use the upstream store, catalog and lock files under `MORPHIR_HOME`.
- Add selection and lock files at the system, global, workspace and project levels.
- Add the `staged/` folders and the `[extension_install] staged` setting, with the default `auto`.
- Accept a bare `.wasm` module and the [extension package archive](#extension-package-archive) (zip or tar gzip) in `staged/`. `morphir extension install <file>` accepts the same formats, so a user can install an archive without the staged folder.
- Replace the not-implemented `morphir extension install/use/remove/which` commands (#492) in `src/Morphir.Tooling/Scenarios/ManagementScenario.cs` with real implementations. Update `docs/content/docs/cli/management.md`, which describes a layout with `<platform>` folders that WASM does not need.
- Refuse to write a dotted extension id into the shared upstream store, catalog or locks until upstream accepts dotted ids. Built-ins never go there.
- **Ship the levels as a documented superset (decided 2026-11-06).** Step 3 does not wait for upstream. Extensions go into the shared store, catalog and global locks in the upstream format, so upstream CLIs see global installs. The level selection and lock files and the `staged/` folders are additions that upstream CLIs ignore. A project-level or workspace-level selection is visible only to morphir-dotnet until upstream adopts the proposal. Document this difference in the CLI documentation.
- Send the levels, the staged folders, the extension package archive (both formats) and the install mode to upstream as a proposal against `docs/design/draft/extensions/distribution-and-acquisition.md`. Upstream currently supports only user-global installs from a registered repository, "does not accept a raw `.wasm` file", and does not download during compile or generate "without an explicit install policy". The install mode setting is that policy.

### Step 4: Generator, then transformer

- **Generator first.** MEP already defines `morphir.backend.generateModel`, so this capability needs no new contract. The guest returns artifacts with relative paths. The host checks them and writes them through the safe-tree publication path. The guest never writes to disk.
- **Transformer second.** It needs the transform payload from upstream, as the validator did.

### Step 5: Upstream bundles and host grants

- Host the `extism-kernel-v1` ABI, so that the published upstream bundles (avro, openapi, python, rust in `.config/published-extension-bundles.toml` of `finos/morphir`) run in morphir-dotnet.
- Add host functions as grants that the user gives one at a time, for example logging and reading configuration. Follow the upstream handshake for permission grants when it exists.

### Later

- IR V4 Ion. Ion uses the `morphir-model` profile (`ion-binary` codec, a wrapper with `ir`, `enrichments` and `provenance`). This is a different profile, not only a different codec.
- Analyzer diagnostics in `dotnet build` and the IDE, through `Morphir.Myriad.Build`, in MSBuild diagnostic format.
- Extension signing, when upstream decides on it.
- `AssemblyLoadContext` plugins in `dotnet-morphir`, only if people ask for them.

## Feature status tracking

| Feature | Status | Step | Completion evidence |
| --- | --- | --- | --- |
| Extension registry and built-in list | Landed in [#503](https://github.com/finos/morphir-dotnet/pull/503); `add-extension-registry` archived, spec `extension-registry` | 0 | `morphir extension list` shows built-ins in both hosts; MCK suite still to come |
| JSON-RPC spike | Done: own layer ([report](../spikes/extension-model/jsonrpc.md)) | 1 | AOT warning count and size for each candidate; decision recorded |
| MEP core (`Morphir.Mep`) | Planned in `add-fsharp-compile` | 1 | That change's tasks |
| In-process validator contract | Planned | 1 | `TypeValidator` diagnostics through the contract |
| Upstream validate payload | Not started | 1 | Upstream draft PR by morphir-dotnet; pinned commit; published contract version |
| WASM runtime spike | Done: Wasmtime ([report](../spikes/extension-model/wasm-runtime.md)) | Spike | Decision recorded with measurements |
| .NET guest toolchain spike | Done: `process` only; Linux run confirms ([report](../spikes/extension-model/dotnet-guest-toolchains.md)) | Spike | Decision recorded |
| Roslyn AOT spike | Done: feasible; ships as an optional extension for now ([report](../spikes/extension-model/roslyn-aot.md)) | Spike | Decision recorded |
| WASM analyzers | Planned | 2a | MCK suite passes in both hosts |
| Process analyzers and .NET guest SDK | Planned | 2b | C# and F# samples pass the MCK suite in both hosts |
| Installation, levels and staged folders | Planned | 3 | MCK suite; an upstream CLI reads a global install; level files documented as a superset |
| Extension package archive (zip and tar gzip) | Planned | 3 | MCK suite installs the avro WASM release and the elm-classic process release from a `.zip` and from a `.tar.gz` package; the unsafe-archive cases fail with no store change in both formats |
| Generator capability | Planned | 4 | MCK suite |
| Transformer capability | Planned | 4 | MCK suite |
| Upstream bundles and grants | Planned | 5 | A published upstream bundle runs in both hosts |

## Testing approach

- Follow test-driven development in each step.
- Each step adds an MCK suite in the Gherkin format with the closed step vocabulary. The case data is the oracle, not an implementation.
- Each step runs its tests against the published `morphir` AOT executable and against `dotnet-morphir`. The extension behaves the same in both.
- Each step keeps the AOT closure check: the `morphir` executable must not contain FSharp.Compiler.Service or any other dependency that is not AOT-safe.
- Guest failure tests: a guest that writes to stdout outside a frame, a guest that hangs, a guest that uses too much memory, a guest that returns a malformed message, and a guest that claims a capability it does not implement.
- Extension package archive failure tests, in both formats: an entry with `..` or an absolute path, a symbolic link entry, a duplicate entry, a tar hard link, device or FIFO entry, a tar extended header that changes an entry path to one with `..`, a gzip stream that does not hold a tar archive, an archive over the size or ratio limit, a digest mismatch, a missing descriptor, no artifact for the host's target, and a `describe` result that does not agree with the descriptor. Each case stops the install and leaves the store, catalog and locks unchanged.

## Open points

| Point | Who decides | Note |
| --- | --- | --- |
| Fuel limit: 100M or 1B | Upstream issue | The proposal and the Rust policy do not agree. Use the Rust policy value until upstream decides, and record it. |
| A project lock that refers to a staged file | Step 3 design | A second machine has no repository to get the bytes from. Options include a vendored folder in the repository or a required publish step. |
| Auto-install of files from a cloned repository | Step 3 design | With `auto`, cloning a repository that has `staged/` files and running a command installs them. The WASM limits reduce the risk. A process guest has no sandbox. The step 3 design must decide whether `auto` applies to process guests. |
| The validate payload shape | morphir-dotnet proposes, upstream accepts | Step 2 can build against the draft PR. |
| Memory limit report | Step 2a design | Wasmtime refuses growth past the ceiling by returning -1 from `memory.grow`, so the guest may not trap. The host needs a rule for when it reports `extension.memory_limit`. |
| A file name suffix for the extension package archive | Upstream proposal | A plain `.zip` or `.tgz` is ambiguous, because the target archives use the same suffixes. Install tells them apart by the descriptor at the root. Suffixes such as `.morphir-extension.zip` and `.morphir-extension.tar.gz` would make the format clear to users. |
| Signing of the extension package archive | Upstream | The `.sha256` files prove integrity, not origin. This follows the "Extension signing" item in [Later](#later). |

## Calls left to the builder

- The exact name and location of the install mode setting.
- The name of the .NET guest SDK package.
- The language of the samples for step 2a (Rust or MoonBit).
- How to split each step into OpenSpec changes, if one step is too large for one change.

## Evidence

Upstream (`finos/morphir`, default branch, read 2026-11-06):

- `spec/mep/README.md`: the contract, versioning, wire rules and scope ("validate and transform payloads" not covered).
- `spec/mep/model-transport.md`: codecs, profiles and payload rules.
- `docs/adr/ADR-0003-morphir-extension-protocol.md`: `Content-Length` framing for native extensions. Status: proposed.
- `docs/design/proposals/wasm-extension-runtime-and-avro-backend.md`: Extism with MEP envelopes; WIT possible later. Status: accepted, not released.
- `docs/spec/webassembly/wasm-components.md`: marked historical.
- `docs/design/draft/extensions/distribution-and-acquisition.md` and `README.mdx`: user-global store, repository-only install, no raw `.wasm`.
- `docs/design/draft/extensions/protocol.md`: framing rules, capability methods, `morphir.validator.validate`.

Upstream implementations:

- `finos/morphir-rust`: `crates/morphir-host-native/src/process/frame.rs` (framing and limits), `src/extism/` (WASM host and host functions), `crates/morphir-extension-sdk/src/protocol/request_id.rs` (u64 or string ids), `crates/morphir-common/src/home.rs` (store layout).
- `finos/morphir-moonbit`: `pkgs/morphir-host/worker/wasm/README.mbt.md` (the two ABIs and limits).
- `finos/morphir`: `crates/morphir/src/commands/extension/bundle.rs` (a release bundle is a directory) and `.config/published-extension-bundles.toml` (the pinned WASM artifacts and process archives with their digests).
- `finos/morphir-rust`: `crates/morphir-distribution/src/repository_authoring.rs` (publish reads only a bundle directory).

Upstream release assets (read 2026-11-07):

- `finos/morphir-extensions` release `extension/avro/v0.4.0`: loose `.release.json`, `.wasm` and `.sha256` assets.
- `finos/morphir-elm` release `extension/elm-classic/v0.6.0`: one `.tgz` or `.zip` for each target, each holding only the executable, with `.sha256` assets and a separate descriptor.

Spikes (2026-11-07; probes built and run under Native AOT on osx-arm64; sources in `spikes/extension-model/`): [JSON-RPC](../spikes/extension-model/jsonrpc.md), [WASM runtime](../spikes/extension-model/wasm-runtime.md), [.NET guest toolchains](../spikes/extension-model/dotnet-guest-toolchains.md), [Roslyn under AOT](../spikes/extension-model/roslyn-aot.md).

JSON-RPC research (2026-11-06; sources and code read, nothing built under AOT):

- [StreamJsonRpc Native AOT documentation](https://microsoft.github.io/vs-streamjsonrpc/docs/nativeAOT.html) and [issue 1407](https://github.com/microsoft/vs-streamjsonrpc/issues/1407).
- [CurlyRpc on NuGet](https://www.nuget.org/packages/CurlyRpc).
- [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
- [LanguageServer.Framework](https://github.com/CppCXY/LanguageServer.Framework): an AOT-capable LSP server with a JSON-RPC layer of about 540 lines.

This repository:

- `src/Morphir.IR.Pipeline` and `docs/content/docs/pipeline/plugin-development-guide.md`: the typed in-process plugin contract.
- `src/Morphir.Tooling/Scenarios/ManagementScenario.cs`: the `extension` command stubs.
- `src/Morphir.Configuration/Schema.fs`: the `extensions` configuration table.
- `src/Morphir.Configuration.IO/MachineLocations.cs` and `ProjectSystem`: level discovery.
- PR [#495](https://github.com/finos/morphir-dotnet/pull/495), `openspec/changes/add-fsharp-compile`: `Morphir.Mep`, the F# frontend deliveries and the selection rule.
