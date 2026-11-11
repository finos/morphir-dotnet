# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Internal extension-family reader increment with pinned, offline hash-checked schemas and case data from parent `0439c4c0`. The focused Windows SharedReaders suite passes 19/19, including six parent release/schema cases and two I-JSON refusal cases with valid parse controls. Full claim/package semantics, state codecs, inventory, probes and command lifecycle remain unfinished; the 137 identity and 66 package cases have not all been executed ([#558](https://github.com/finos/morphir-dotnet/issues/558)).
- Extension installer security corrections: descriptor-bound single-file publication, interprocess run-directory ownership coordination, directory/owner identity checks, and gzip metadata/padding budgets with cancellation below the tar reader. Windows regression evidence is 131 passed with three Unix-only cases ignored. Independent security/interop re-review of `b6936ba5` found no concrete remaining finding under the approved cooperating Home boundary; Unix runs and AOT remain pending. Recorded the approved selection, command, provenance and explicit developer-consent decisions; inherited default staged `auto` grants no automatic install consent. Shared readers use parent source `0439c4c0`; portable pin/overlay formats remain parent toolchain work ([#558](https://github.com/finos/morphir-dotnet/issues/558)).
- Extension installation library foundations: portable distribution identifiers over the existing domain types, RID/descriptor/index mappings and exact implemented MEP eligibility within the supported 0.2 line. Includes an Expecto suite and C# probe; CLI installation and Native AOT qualification remain unfinished ([#558](https://github.com/finos/morphir-dotnet/issues/558)).
- Extension installation I/O: a single-file content-addressed store that hashes while copying, rehashes reuse and refuses corrupt objects without replacement; ZIP and tar-gzip inspection with the recorded path, type and resource limits; and exclusive ownership and stale cleanup of .NET run directories. Windows tests and the C# JIT probe cover these APIs. Package formats, extraction, inventory, activation and CLI installation are unfinished; Unix filesystem runs and Native AOT qualification remain pending ([#558](https://github.com/finos/morphir-dotnet/issues/558)).
- `morphir mck <args>` runs the separately installed Morphir Compatibility Kit, `morphir-mck <args>`, a development tool from finos/morphir, in every entry point (`morphir`, the managed `morphir`, `dotnet-morphir`). The arguments, working directory, environment and standard streams are passed on unchanged, and the exit code is the tool's. `MORPHIR_MCK` names the tool's absolute path; otherwise only absolute `PATH` folders are searched, never the current directory, and on Windows only `morphir-mck.exe` is a candidate. With no tool, the CLI writes a diagnostic to stderr and exits with 127. On Windows, killing `morphir` also stops the tool and its children (a kill-on-close job object); on Linux and macOS, SIGTERM or SIGHUP stops the tool's tree. `mck` is the only command routed to another executable, and the CLI includes no kit of its own ([#589](https://github.com/finos/morphir-dotnet/issues/589))
- `morphir extension list --json` and `dotnet-morphir extension list --json` say which offering is running and what it needs from the machine: `deployment` (`native-aot`, `self-contained`, `framework-dependent` or `dotnet-tool`), set at build time by the named release profile and never inferred from how the process runs; `execution` (`native-aot` or `jit`); `platform` (the runtime identifier, left out for the portable tool package); and `prerequisites` (`runtime`, `sdk` with its `phase`, and `native`). A build that no named profile produced, such as `dotnet run`, a test host or the legacy `morphir-<rid>` single-file assets, leaves out `deployment`, `platform` and `prerequisites`. Each extension entry gains `deployments`, `prerequisites` and, when it is not available, a `reason`. The `host` member keeps its meaning as the command name ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- The built-in F# frontend is listed as `morphir-dotnet.fsharp`, available in the self-contained and framework-dependent `morphir` and in `dotnet-morphir`. The Native AOT `morphir` lists it as not available, with the reason; the registry and `compile` take the claim from the same declaration ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- `Morphir.Toolchain`: the recipe binding checks of the toolset recipe page, as pure functions over a recipe, the authenticated targets and their bytes. `RecipeBindings.check` runs every host row (a `tool-release` offering's v1 release descriptor is authenticated, not revoked, served with its length and digest and readable, and lists each bound artifact for its platform with matching `tool-artifact` metadata) and every provider row (the extension release descriptor is bound, served, readable and names the provider's identity; each artifact or package is bound and listed with the delivery's runtime, digest and platform; and the claims name the capability kind, MEP version, language or target and model tuple), each with the row's code and path. It reads tool release metadata v1 release descriptors (`ToolReleaseDocument`), extension release descriptors at `2.0.0-draft.2` (`ExtensionReleaseDocument`) and capability claim sets at `0.1.0-draft.2` (`ClaimSetDocument`), and nothing earlier. `ToolResolver.resolve` selects a tool release by the v1 channel, CLI compatibility and platform rules. No command uses them, nothing is downloaded, and none of it has had a security review ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- `morphirup setup --dry-run`: the same request as `setup --plan`, with the same plan, `planSha256`, result, refusals and exit codes, and nothing created, changed or removed. `--plan` is unchanged; both may be given; neither permits `--yes`. Every later `morphirup` command that creates, updates, repairs or removes persistent state will have `--dry-run` on its own planning and validation path ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- `Morphir.Toolchain`: `Comparator.allMet`, which says whether an installed version meets a list of comparators by the Rust `semver` and npm rule (a prerelease matches only when a comparator names its own major, minor and patch with a prerelease; an empty list is always met), and `Discovery.requestIn` with `Discovery.PathCase`, which compares names without case on Windows, and `Discovery.within`. The suite also runs the contract's `nfc`, `setup-platforms`, `prerequisite-versions` and `version` binding cases and its Windows discovery case ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- `Morphir.Toolchain`: the verification part of a TUF client for the tool release metadata v1 profile, as pure functions over supplied bytes and one trusted start time: strict readers for root, timestamp, snapshot and targets metadata, Ed25519 threshold verification with BouncyCastle, root rotation with both thresholds and a retry after 32 roots, timestamp, snapshot and targets checks with rollback, link and expiry, and typed failures mapped to the stable codes. A refresh returns a `RefreshResult` whose every outcome (`Refreshed`, `NothingNew` for a timestamp at the trusted version, `RootRotationPaused` and `Failed`) carries the newest trust state accepted, so a root accepted before a failure is kept. No exception leaves the client: a defect is a `Failed` with `TufFailure.Internal`. A trust anchor carries its purpose, and a `Production` anchor must be the repository identity the caller names; every root accepted later meets the same rules. Text inside `signed` that is not NFC, a member differing from a known member only in letter case, a prerelease `spec_version` and an integer above 2^53 - 1 are refused. It does no I/O and no command uses it. It has not had a security review and must not be used to authenticate a production release until it has. Where the profile is silent it takes the stricter reading, marked provisional ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- `morphir generate --extension` and the configuration key `[codegen.<target>].extension` select a MEP backend extension (a command name, a path, or a `.dll`, as for `compile --extension`), for example `morphir-fsharp-backend`. The option wins over the key; without either, the built-in backend runs as before. Every entry point has the option. The generate report names the extension by its own id and adds `backendTransport` (`mep`) and `backendVersion`. Failures have the codes `extension.unavailable`, `extension.protocol_error`, `extension.timeout`, `extension.tuple_unavailable`, `extension.not_admitted`, `extension.unsafe_artifact` and `invalid-extension`. An extension cannot be selected by an installed extension id yet
- `MepGenerationBackend` in `Morphir.Tooling`: an `IGenerationBackend` over a MEP guest's `morphir.backend.generateModel`. It sends the model's JSON IR (IR `4.0.0` or `3.0.0`) only within the limits that the session agreed (`MepBackendOptions.OfferLimits`), accepts at most 10,000 artifacts in one result, and checks every returned artifact path before publication, including Windows-equivalent names, reserved device names and file/directory conflicts. `MepBackendOptions.Environment` sets the guest's whole environment. A guest that gives the id of a built-in backend is refused
- `MepGuestProcess` in `Morphir.Compilation.Mep`: the guest-process lifecycle (start, initialize, describe, request with a deadline, shutdown or kill of the process tree) that `MepFrontend` and `MepGenerationBackend` share. `MepGuestOptions.Environment`, and an additive `MepFrontend.ConnectAsync` overload, set the guest's whole environment
- `Morphir.CodeGeneration.FSharp`, for C# callers: the public union `FSharpFormatting` (`Applied`, `Off`, `Unavailable`, with `WireValue`), which `Transpiler.formatting` now records; `FSharpGeneration.TryGetFormatting(tree, out formatting)` and `FSharpGeneration.WithRequiredSemantics(context, semantics)`; and `GenerationOptions.WithOptimize` and `WithFormat`. The project now sets nullable metadata, warnings as errors, and the trim and AOT properties
- `morphir generate --target fsharp --option format=false`: the F# backend option `format` (Boolean, `true` by default, beside `optimize`) turns Fantomas formatting off. The files are the generator's own text, with LF line endings only and exactly one final LF, the same bytes on every operating system, under JIT and Native AOT, and in every culture. The external backend `morphir-fsharp-backend` accepts the same option through the `generateModel` options. `GenerationOptions.Format` and `Transpiler.formatting` carry it in `Morphir.CodeGeneration.FSharp`
- The generate report member `formatting`: `applied`, `off` or `unavailable`, written for a built-in backend only: an external backend (`backendTransport` `mep`) has no `formatting` member, even with `format=false`, because a MEP generate result does not say whether the guest formatted. `compile --target` writes the same member in its `generation` report member. Under the default, a host that cannot run the formatter (the Native AOT `morphir`) writes a warning on stderr from `generate` and from `compile --target`, never on stdout or in the JSON output; its bytes are unchanged

### Changed
- Build: the Native AOT publish of `morphir` treats IL3050 as an error. The F# formatter (`Emit.formatCore`, Fantomas) is `[RequiresDynamicCode]` and is called only behind `RuntimeFeature.IsDynamicCodeSupported`, so if that guard goes, ILC refuses the publish instead of putting Fantomas (about 20 MB) into an image that cannot run it
- Release process: finos/morphir now builds, qualifies and publishes the official Morphir CLI from a pinned commit of this repository. The deployment workflow here publishes the library packages and the F# extension tools, and builds and publishes the CLI (the `morphir-<rid>` executables, the `morphirup-<rid>` assets and the `Morphir.Tool` package) only when the repository variable `MORPHIR_DOTNET_CLI_PUBLICATION` is `enabled`, as a rollback. The reusable workflow `cli-offering.yml` builds one of the four offerings for one platform at a given commit with `PackRelease`, starts the produced executable (or the installed `dotnet-morphir`) with `--version`, and uploads the file, its launch metadata and its SHA-256. `PublishTool` is now `PublishCliTool` plus `PublishExtensionTools`, and `PublishTool` and `PublishAll` publish what they did. See `docs/contributing/cli-build-interface.md` ([#589](https://github.com/finos/morphir-dotnet/issues/589))
- In the Native AOT `morphir`, the hint of `compile.frontend_unavailable` for F# now names the offerings that have a built-in F# frontend and the process delivery `morphir-fsharp-frontend` ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- `Morphir.Toolchain` and `morphirup` follow the finos/morphir toolchain contract at `9e1f853b7` (the draft finos/morphir#1147), re-pinned from `1451c081f`. The same input now gets another code or path:
  - a MEP version (`protocolVersion`, an offering's `protocolVersions`) that is not a version in canonical SemVer spelling, such as `0.1`, `v0.2.0` or one past the version bound, is `invalid_value` instead of `unsupported_case`. A SemVer version off the 0.2 line, such as `0.1.0`, stays `unsupported_case`;
  - a recipe provider with `delivery` `built-in` is `inconsistent_document` instead of `unsupported_case`;
  - in a receipt, a workflow check without `workflow` and a `probed` claim check without `probeSource` are `invalid_document` instead of `inconsistent_document`, and a component whose `readiness` is `workflow-qualified` is `inconsistent_document` instead of `unsupported_case`;
  - in a result, a plan without `planSha256` is `invalid_document` instead of `inconsistent_document`, and a host component whose source names another artifact is refused at `source.target` instead of `source`
- Documents that were read before are now refused with `inconsistent_document`: a built-in with `protocolVersion` or `modelTuple` in a recipe, lock or receipt, at that member; an offering that lists a MEP version or a WASM ABI twice, at the repetition; a plan whose host source is `recipe-digest` or whose provider source is `repository-target`, at its `source`; and a diagnostic with `document` or `path` but not both, at the one present
- `morphirup` results: an `unsupported-artifact` diagnostic from `setup --plan` names the request member it concerns with `document` `request` and `path`, such as `toolset.providers[1]`, and a `repository_mismatch` diagnostic names the recipe member (`setup --plan`) or lock member (`doctor`) that names another repository. A recipe, lock or request that `0.4.0-alpha.4` accepted and that is now refused turns a `planned` or `succeeded` result (exit 0) into a `failed` one (exit 1); for example, a recipe that lists a MEP version twice in one offering. Exit code 2 (the command was not run) and exit code 3 (an internal error) mean what they did, and the plan digest of any input both read is unchanged. The version bound (256 characters, 15 digits per numeric identifier) is the contract's rule and no longer provisional
- `Morphir.Toolchain` API, for F# callers: `DiagnosticDetail.UnsupportedArtifact` and `DiagnosticDetail.TrustFailure` have a last field `concerns: DocumentMember option`; `BindingFailure.RepositoryMismatch` carries the document whose member its path names; `Diagnostics.ofTargetRefusal` takes that document; and `RecipeTargets.checkLockedGeneration` takes the generation's recipe as a function `unit -> Result<Recipe, TargetRefusal>`, asked for only for the host release row (a refusal it returns is the check's, and an exception it throws is `invalid_document` at `recipe.target`), and refuses a generation whose recipe target the trust state does not list with `target_not_authenticated` at `recipe.target`, before reading the recipe ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- `morphir generate --input` now loads the project configuration (from `--project`, `--config` or discovery) to read `[codegen.<target>].extension` when `--extension` is absent. With `--project` or `--config`, a configuration that fails to load fails the command with exit code 2. With neither, a discovered configuration that does not load is a warning on stderr and the built-in backend runs, as before
- `compile --extension`, from the shared guest-process lifecycle: a user cancellation (Ctrl+C) while the guest initializes is now a cancelled compile (exit code 130) instead of `extension.unavailable`, and a guest that honors `$/cancelRequest` at the deadline now gives `extension.timeout` instead of `extension.protocol_error`
- `compile --target` does not run a backend extension: it runs the built-in backend of the target, as before. When the project configuration sets `[codegen.<target>].extension` for that target, it now prints a note on stderr saying so
- `GenerationOptions` (`Morphir.CodeGeneration.FSharp`) has the new field `Format`, so a record expression that sets every field needs it, and C# that calls its two-argument constructor must pass both; `{ GenerationOptions.Default with Optimize = ... }` and `GenerationOptions.Default.WithOptimize(...)` keep working

### Fixed
- Configuration: a member of `[frontend]` that is not a table (for example `[frontend]` `note = "x"`) is no longer refused as an invalid language table. Only an object-valued member is a `[frontend.<language>]` table with `project_file` and `extension`; any other member stays unconstrained, as the upstream schema leaves it
- `compile --extension`: a frontend guest that never answers `compileModel` now fails with `extension.timeout` at the deadline, and a guest that does not answer `shutdown` or take `exit` within the deadline (for example one whose handler blocks its read loop) has its process tree stopped. Before, the host could wait indefinitely in both cases. The same holds for `generate --extension`, so a timed-out request ends within about two deadlines
- A frontend or backend guest that fails to start or initialize no longer leaves its stderr reader or process handle behind
- `compile`: for a language id with a dot, `[frontend."acme.lang"].extension` and `project_file` are now read from that quoted table; before, the id was split at the dot, so the keys were looked up under `[frontend.acme.lang]` and the quoted table was ignored. A project file key that is not read for lack of a language is now named with the quotes too

### Security
- `compile --extension`, `generate --extension` and the `extension` configuration keys refuse a guest that reports an extension id that starts with `morphir.`, which is reserved for the built-in frontends and backends, including built-ins that do not ship yet (`extension.unavailable`). Before, `generate` refused only the id of a registered built-in backend, and `compile` checked no id. `org.finos.morphir.*` ids are not reserved. `Morphir.Mep.ReservedExtensionIds` and `ExtensionId.ReservedForBuiltIns` hold the rule
- `compile --extension`, `generate --extension` and the `extension` configuration keys: a command name without a directory (for example `morphir-fsharp-backend`) is now searched only in the absolute `PATH` directories, and the resolved absolute path starts. Before, process start also looked in the directory of `morphir` and in the current directory first (CreateProcess on Windows, and .NET on Unix), so a checkout could plant a binary of that name and have it run. A `.dll` extension's `dotnet` host is resolved the same way when `DOTNET_HOST_PATH` is not set. A name that is not on `PATH` now fails with `extension.unavailable` before any process starts. `MepFrontendExtensionConnector.Resolve` throws `FileNotFoundException` for such a name instead of returning it unchanged

## [0.4.0-alpha.4] - 2026-11-10

### Added
- `morphirup`, the standalone Native AOT toolchain manager, first slice: `morphirup --version`, plan-only `morphirup setup --plan` and `morphirup doctor` over local files. It needs neither .NET, ICU nor OpenSSL: it runs in invariant globalization mode, normalizes to NFC with its own Unicode 17.0.0 tables and computes SHA-256 with BouncyCastle's managed implementation. Setup resolves a `.morphir/toolchain.toml` request against a recipe file for every platform the lock would record and prints the plan with its `planSha256`. The plan carries the complete lock setup would write, so the digest pins every platform's closure and the request's exact bytes; `--expect-plan` refuses another plan with `plan_changed`. `--repository-id` is copied into the plan and lock as given and not checked, and the plan's sources are labels, not verified provenance. Without `--request`, the project request is discovered from the start directory up to the discovery boundary, by default the root of the file system; discovery stops at a `.morphir/toolchain.toml` in any form, and anything there that is not a readable regular file is `document_unavailable`. Doctor reads the lock beside the request it found or was given, and checks that the lock binds the request bytes, records the current platform and, with `--recipe` and `--repository-id`, names that repository (`repository_mismatch`) and is a closure the recipe allows. A request whose bytes differ from the locked ones only by CRLF line endings or a byte order mark is `request_changed` naming that cause. `setup --plan` and `doctor` write one manager result document to stdout. Files are read only when they are regular files, up to 1 MiB for a request, lock or sizes file and 16 MiB for a recipe, with five seconds to open and thirty seconds for each read; read errors are reported in `morphirup`'s own words, and a request, lock or recipe that cannot be read is `document_unavailable`. A document nested deeper than 64 levels is `invalid_document`. Acquisition, installation and activation are not implemented. Exit code 2 means the command was not run, for example an unknown option, a missing `--recipe` or `--repository-id`, an invalid option value, a machine that is not one of the eight platforms, or an artifact with no size in `--artifact-sizes`; it writes nothing to stdout. Provisional: `--project`, `--boundary`, `--recipe`, `--repository-id`, `--recipe-target`, `--artifact-sizes`, `--request` and `--lock`; the file bounds; the version bound (256 characters, 15 digits per numeric identifier, refused as `invalid_value`); and exit code 3 (internal error), which writes nothing to stdout ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- Releases attach `morphirup` for `linux-x64`, `linux-arm64`, `win-x64` and `osx-arm64`: the executable `morphirup-<rid>` (`morphirup-win-x64.exe` on Windows), the archive `morphirup-<rid>-v<version>.tar.gz` with the executable, `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES.txt` under `<rid>/`, the same notices as `morphirup-v<version>-THIRD-PARTY-NOTICES.txt`, and the checksum file `morphirup-v<version>.sha256` in `sha256sum -c` format. The notices are generated from the assemblies the Native AOT compiler linked, with each package's license and notice files; the build fails when a linked package has no license text. The release job builds each with Native AOT on a runner of its platform and runs the published file, not a source build, from a folder whose path has a space, with `PATH` reduced to the system folders and no `DOTNET_*` variable: it must print the release version and make a plan-only setup with the recorded plan digest. The Linux executables are linked in `ubuntu:22.04`, pinned by digest, so they need glibc 2.35 or later (Ubuntu 22.04, Debian 12 or later); the release job fails if one needs a newer glibc version, and runs each in `debian:12-slim` and `ubuntu:22.04` (with its `libssl3` package removed), with no .NET, `libicu`, `libssl` or `libcrypto`. A Linux executable needs nothing else from the machine. They do not run on musl distributions such as Alpine. Nothing else is checked. The files are not code-signed and the macOS file is not notarized, so Gatekeeper can refuse a copy that a browser downloaded until its quarantine attribute is removed; this was not checked on a Mac. The release does not download, install, update or activate a toolchain and does not verify signed metadata. The pull request workflow `morphirup` runs the same build and check on all four platforms ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- `Morphir.Toolchain`: readers and writers for the `0.1.0-draft.1` toolset recipe, project toolchain request, portable lock, installation receipt and manager result of finos/morphir, with pure resolution, lock, plan and project discovery functions, pinned to finos/morphir `1451c081f`. A provider's `protocolVersion` is a MEP version on the supported 0.2 line (every prerelease of `0.2.0` and every final `0.2.x`); MEP `0.1` is refused, and an external provider carries one `modelTuple` and no `modelInput` or `modelOutput`. Plan digests and target paths are normalized to NFC with the library's own Unicode 17.0.0 tables, generated by `scripts/generate-nfc-tables.fsx` and checked against all of `NormalizationTest.txt`, so neither depends on the machine's ICU. It runs every case of the pinned finos/morphir contract manifests that is a pure function of documents and authenticated metadata. The cases that need a TUF client to verify metadata, or the installation work of later slices, are skipped by name with their reason ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- The F# frontend extension `morphir-fsharp-frontend` has a self-contained process delivery: a folder with the guest, the F# compiler service and its own .NET runtime, published with the `SelfContainedProcess` profile or the build target `PublishFSharpFrontendProcess --rid <rid>`, which also writes a zip or `.tar.gz` archive (files at the root, ordinal order, fixed time stamp and modes) and keeps the publish's lock file beside it. A host such as the native `morphir` starts it with `--extension`. Checked on Windows ARM64 only, in an environment that hides the machine's .NET (no `dotnet` on `PATH`, no `DOTNET_*` variables, an empty NuGet folder and no global install): the automated test runs the guest there, and the native `morphir` compiled F# through it there by hand. It was not run on a machine with no .NET at all. Releases do not publish it yet. The framework-dependent dotnet tool is unchanged
- `morphir-fsharp-backend`: the F# generator as a MEP `0.2.0-draft.1` guest, packaged as the framework-dependent dotnet tool `Morphir.CodeGeneration.FSharp.Extension` with the extension id `org.finos.morphir.fsharp-backend`. It serves `morphir.backend.generateModel` for the target `fsharp` with JSON model tuples for IR `4.0.0` and `3.0.0`, for hosts that have no built-in F# backend. The built-in `morphir.fsharp` stays the backend of `morphir generate`, which does not start the extension. Releases publish the package next to `Morphir.Tool`
- `Morphir.Mep` backend messages: the pinned `generateModel` request, generate result and artifact types, and `GenerateModel.admit`, the guest check of a request. Compatibility note for F# code that opens `Morphir.Mep`: like the other wire types, the new records `GenerateModelRequest`, `GenerateResult`, `Artifact` and `AdmittedGenerateRequest` are at the top level of the namespace, and their labels (`Model`, `Input`, `Target`, `Options`, `Path`, `Content`, `Binary`, `Success`, `Artifacts`, `Diagnostics`, `Request`, `Guarantees`) can change which record type F# infers for an unannotated record expression or member access. Annotate the record type where that happens. The type name `Artifact` also exists as `Morphir.Conformance.Artifact`; qualify it when both namespaces are open
- `MorphirApplication` in `Morphir.Tooling`: the shared command layer (the scenarios, `info` and `--version`) that every entry point runs. `Morphir.Program.Run` and `Morphir.Program.CreateApp` keep their signatures and delegate to it
- A managed `morphir` entry point (`src/Morphir.Managed`) and the managed composition it shares with `dotnet-morphir` (`src/Morphir.Tooling.Managed`), which holds the in-process F# frontend. It is not packaged or released yet
- Build: named release profiles for the four .NET deployment offerings. `PublishRelease` and `PackRelease` with `--deployment native-aot|self-contained|framework-dependent|dotnet-tool` and `--rid` produce `artifacts/release/morphir-dotnet-<release>-<deployment>-<rid>.<zip|tar.gz>` (or `Morphir.Tool.<release>.nupkg`) with a build-side `.build-launch.json` beside it, for win-x64, win-arm64, osx-x64, osx-arm64, linux-x64 and linux-arm64. The managed profiles are untrimmed folders with the in-process F# frontend. The same publish folder always gives the same archive bytes (fixed time stamp, ordinal entry order, mode 0755 for the launch file and `createdump`, 0644 otherwise). `PackRelease` fails when the publish folder contradicts the launch metadata, and checks the tool package as `PackTool` does. A release with build metadata (`1.2.3+abc`) is refused. Releases do not publish these archives yet, and no platform is qualified yet ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- E2E: the four offerings are executable types (`native-aot`, `self-contained`, `framework-dependent`, `dotnet-tool`, or `offerings` for all four) that run from their packaged archives, installed into a new folder whose path contains a space. `Features/Packaged/PackagedArtifacts.feature` checks integrity, layout, clean-folder startup, core commands, the F# frontend, reading an input file, and, from the .NET host trace, that `native-aot` and `self-contained` load no installed runtime. "No installed .NET runtime" is skipped with the reason where the machine's runtime cannot be hidden, and on Linux it runs in a clean container when `MORPHIR_E2E_RUNTIME_CONTAINER` names one. These are checks from packaged artifacts of a local build, not qualification: they pass on one win-arm64 build machine, and the other five platforms have not been run ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- Build: `PackRelease` writes `<archive>.build-contents.json` with every file the artifact contains and its size, the release, the SHA-256 and the commit and time it was packed, and `artifacts/release/size-report.md` over every artifact packed there, with the number of satellite resource folders of each and other releases listed apart ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- Docs: the installation guide describes the four release archives, which are not published yet, and what to do when the framework-dependent `morphir` cannot find .NET; the released standalone `morphir` is described as the managed, trimmed single-file build that it is

### Changed
- `dotnet-morphir` runs the shared command layer with the managed composition and no longer references the AOT-compatible `morphir` project, so the tool package no longer contains `morphir.dll`. Its commands and its F# frontend are unchanged
- Build: `PublishExecutable`, `PublishSingleFile` and `PublishSingleFileUntrimmed` take their settings from the release profile model. `PublishSingleFile` keeps the settings and names of the legacy release assets `morphir-<rid>[.exe]` and `morphir-<rid>-v<version>.tar.gz`. `PublishExecutable` now leaves out debug symbols and documentation files, and finds `morphir` (not `Morphir`) on Linux and macOS. Size figures of `PublishExecutable` output from before this change included the symbol file, so they are not directly comparable with new figures ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- Build: publishes for one runtime identifier write their NuGet lock files under `obj/publish-lock/<rid>/` (the `MorphirRidSpecificPublish` property), so they no longer add RID sections to the committed `packages.lock.json` files ([#558](https://github.com/finos/morphir-dotnet/issues/558))
- Build: `src/Morphir.Managed` fails to build with `PublishAot` or `PublishTrimmed`, and a project whose references resolve two different `morphir.dll` assemblies (`src/Morphir` and `src/Morphir.Managed`) fails to build
- `just publish-executable`, `just publish-single-file` and `just publish-single-file-untrimmed` run the Nuke targets; their `VERSION` argument is gone, as the version comes from `CHANGELOG.md`. `scripts/measure-compile-aot.sh` publishes with the native-aot profile's settings
- E2E: `scripts/run-e2e-tests.cs` prints the release, commit, SHA-256 and pack time of each packaged artifact and refuses one packed from another commit than `HEAD`, unless `MORPHIR_REUSE_PACKAGED_ARTIFACTS=true` ([#558](https://github.com/finos/morphir-dotnet/issues/558))

### Removed
- `scripts/publish-single-file.cs` and `scripts/publish-single-file-untrimmed.cs`, which called a removed `codegen write` command. Use the `PublishSingleFile` and `PublishSingleFileUntrimmed` targets ([#558](https://github.com/finos/morphir-dotnet/issues/558))

### Fixed
- E2E: the Manual AOT Testing lanes ran no test: their filters `*/AOT*` and `*/Trimming*` were not full TUnit tree paths and selected nothing. They now select the Native AOT and trimming features and exclude the `@packaged` scenarios
- The F# frontend takes its framework references from the framework's runtime assembly list, so in a self-contained folder it no longer gives the compiler the application's own assemblies (`Morphir.*`, the compiler service). The references of the dotnet tool and the in-process frontend are unchanged. It reads the deps files that the host lists (`APP_CONTEXT_DEPS_FILES`, joined with `;` on every platform). When it cannot give the compiler its references (no file location, a self-contained folder without a framework list, no trusted platform assemblies, or a listed deps file that cannot be read), compilation fails with one `fsharp.references_unavailable` error instead of an exception
- `Morphir.Myriad.SystemTextJson` build targets: an absolute `IntermediateOutputPath` (for example with `--artifacts-path`) no longer gives an invalid output folder for the generated JSON contexts

## [0.4.0-alpha.3] - 2026-11-09

### Added
- Docs: [Compile F# to Morphir IR](https://finos.github.io/morphir-dotnet/docs/getting-started/compile-fsharp/), a five-minute start, and two tutorials: car rental pricing from F# to C#, and compiling F# with the native `morphir` executable. The finished example is in `examples/car-rental-pricing` ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- Releases publish the F# frontend extension `Morphir.Frontend.FSharp.Extension` (command `morphir-fsharp-frontend`) to NuGet next to `Morphir.Tool` ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- `morphir compile`: compiles F# sources to Morphir IR with the in-process F# frontend of the dotnet tool `dotnet-morphir`. Sources come from `--input` files in order, one `--input` directory in lexical order, `--project-file`, `[frontend.<language>].project_file`, or `project.source_directory`. `--ir-version` and `ir.format_version` keep the exact format version, for example `4.0.0`. `--json` writes one report with setting origins; `--json-lines` writes the compile events and ends with the report. Human mode writes each stage's diagnostics to stderr when the stage completes. Exit codes are 0, 1, 2 and 130. The AOT executable `morphir` has no built-in F# frontend; it compiles F# through the MEP guest with `--extension` ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- `morphir compile --extension <command, path or .dll>` and `[frontend.<language>].extension`: run a frontend as a MEP `0.2.0-draft.1` guest process. The AOT `morphir` compiles F# through `morphir-fsharp-frontend` for IR versions 1 to 4; classic versions 1 and 2 are compiled at `3.0.0` and written at the requested version. Extension failures are exit code 2 with `extension.unavailable`, `extension.not_admitted`, `extension.tuple_unavailable`, `extension.protocol_error` or `extension.timeout` ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- `morphir compile --target <id> --generate-output <dir> [--option KEY=VALUE]`: continue into code generation on the compiled IR, as `generate` does ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- Compile task records: `compile` writes `compile.json` under the out root (`--out-dir`, `MORPHIR_OUT_DIR` or `workspace.out_dir`), holds `compile.lock` while it runs, leaves a tombstone when it fails, and installs a copy of the IR with `-o/--output` without changing files it did not write ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- `morphir generate` without `--input` reads the IR of the most recent compile of the project, and fails with `compile.record_missing` when there is none ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- `morphir.unrepresentable`: a classic IR version that cannot hold a value of the model (an integer outside the 64-bit range, or a character outside the Basic Multilingual Plane) fails with exit code 1 and writes no document ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- Configuration: `ir.format_version` accepts a SemVer string such as `"4.1.0-draft.1"`, and every `[frontend.<language>]` table accepts `project_file` and `extension` ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- Generation JSON reports: a diagnostic can have an optional `range`. Reports without ranges are unchanged ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- `Morphir.Mep` foundation: parsed protocol versions, generated describe params, bounded `Content-Length` framing, pinned-contract tests and a C# consumer verified under Native AOT ([#512](https://github.com/finos/morphir-dotnet/issues/512)).
- Myriad System.Text.Json record wire rules: typed scalar, empty-collection and static-provider defaults for missing members, an explicit JSON-literal fallback, empty-collection omission, flattened extension data, exact versioned-member migration and critical-path allowlists. Existing encodings remain unchanged unless annotated.
- `Morphir.FSharp.Annotations`: `[<MorphirName("...")>]` sets the Morphir name of an F# value, type, union case, record field or module, in canonical Morphir name syntax, for example to name an operator. For `netstandard2.0`, with no dependency ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- `morphir-fsharp-frontend`: the F# frontend as a MEP `0.2.0-draft.1` guest, packaged as the framework-dependent dotnet tool `Morphir.Frontend.FSharp.Extension`. It serves `morphir.frontend.compileModel` with JSON model tuples for IR `4.0.0` and `3.0.0` ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- `Morphir.Mep` frontend messages: the `compileModel` request types, the result sum, the reserved options, the request check, RFC 8785 canonical JSON and the request context digest ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- The F# generator writes more Morphir SDK operations: `Basics.integerDivide`, `negate`, `abs`, `min`, `max`, `and`, `or` and `not`; `Decimal.add`, `sub` and `mul`; `List.singleton`, `append`, `length`, `head` and `cons`; `Maybe.andThen` and `Result.andThen`; `String.append`, `length` and `join`; and the `Int.Int64` type ([#499](https://github.com/finos/morphir-dotnet/issues/499))

### Changed
- The F# generator gives each union case field the name of its constructor argument (`Large of seats: int`), so the names stay in the generated F#. A case whose argument names repeat keeps positional fields ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- The F# generator renames a parameter or local value (`__morphirLocal_` prefix) only when it hides a module value that its definition uses. It renamed every local with the name of a module value before ([#499](https://github.com/finos/morphir-dotnet/issues/499))
- The F# generator writes `List.cons` with two arguments as `head :: tail`, not as an applied lambda ([#499](https://github.com/finos/morphir-dotnet/issues/499))

### Fixed
- Custom Myriad plugin scaffolds build and load with Myriad 0.85.0, using Fantomas syntax types and copied runtime dependencies. Myriad and its coupled code generation dependencies now have their own Renovate update group, including the bundled host pins.

## [0.4.0-alpha.2] - 2026-11-07

### Added
- `morphir extension list` (and `dotnet-morphir extension list`) lists the built-in extensions, the C# and F# generators, with their id, name, version, types, delivery and hosts, as a table or with `--json`. New package `Morphir.Extensions` with the extension registry domain ([#503](https://github.com/finos/morphir-dotnet/pull/503), [#492](https://github.com/finos/morphir-dotnet/issues/492))
- `Morphir.Myriad.SystemTextJson`: a Myriad plugin that generates trim-safe and Native AOT-safe System.Text.Json converters and a `JsonSerializerContext` for F# records, unions and enums at build time. The union encodings, flags and presets give the same JSON as FSharp.SystemTextJson; parsed values, cases chosen by JSON token kind, exact numbers and member rules are added. See the Myriad JSON guide ([#511](https://github.com/finos/morphir-dotnet/issues/511))
- `Morphir.Myriad.SystemTextJson.Annotations`: the attributes and options of the generator, for `netstandard2.0`, with no dependency ([#511](https://github.com/finos/morphir-dotnet/issues/511))
- `Morphir.Myriad` is published to NuGet.org with each release; it was only packed into a local feed before ([#511](https://github.com/finos/morphir-dotnet/issues/511))

### Changed
- The car rental example and the Morphir-to-F# article use `Morphir.Myriad` from NuGet.org with the release version in `MorphirMyriadVersion`, not a local feed ([#511](https://github.com/finos/morphir-dotnet/issues/511))

### Fixed
- `Morphir.Models` has F# nullness annotations, so C# callers see which values may be null ([#508](https://github.com/finos/morphir-dotnet/issues/508))
- `morphir ir verify` accepts `"doc": null` on IR v3 modules, which the Morphir tools write ([#510](https://github.com/finos/morphir-dotnet/pull/510))

### Removed
- The internal stub `json-codec` Myriad generator, which returned no code; use `Morphir.Myriad.SystemTextJson` ([#511](https://github.com/finos/morphir-dotnet/issues/511))

## [0.4.0-alpha.1] - 2026-11-06

### Fixed
- The global Morphir home is no longer discovered as a project. Walking up from a directory under the home folder used to find `~/.morphir/morphir.toml` through the `.morphir/` layout and load it as both a global source and the project's primary. It now loads only as a global source ([#494](https://github.com/finos/morphir-dotnet/issues/494))
- A native Windows `MORPHIR_HOME` such as `C:\Users\me\.morphir` works. It was treated as relative and joined onto the current directory, so every `config` command failed with `discovery.read_error` ([#494](https://github.com/finos/morphir-dotnet/issues/494))
- Every Morphir assembly now takes its version from CHANGELOG.md, so assemblies no longer reference each other as `1.0.0.0`. An explicit `-p:Version` or `-p:PackageVersion` still wins, for local and test packages. Previously the packed `dotnet-morphir` tool failed `generate` with `Could not load file or assembly 'Morphir.Models, Version=1.0.0.0'`
- `Morphir.Tooling` and `Morphir` can now be installed from NuGet: releases also publish their dependencies `Morphir.Configuration`, `Morphir.Configuration.IO`, `Morphir.IR.Pipeline`, `Morphir.IR.Pipeline.Plugins`, `Morphir.Ion` and `Morphir.CodeGeneration.FSharp`. A build test now fails if a packed package depends on a Morphir package that is not packed
- GitHub releases now include the `morphir-{rid}-v{version}.tar.gz` archives that the proto plugin downloads, so `proto install morphir` works again
- The deployment workflow now requires the release version to be the latest release in CHANGELOG.md, which is the version the build stamps. The ignored `--version` arguments and the `--version-override` build parameter, which had no effect, are removed
- `morphir ir verify --json` no longer crashes in the standalone executables with `Reflection-based serialization has been disabled`; it now uses source-generated JSON serialization
- The deployment workflow's end-to-end tests run again. Their test filter was malformed and selected no tests, which hid an invalid step definition, a duplicate step definition and the `ir verify --json` crash
- `PublishLocalTool` installs from the local package directory again; it no longer fails on the repository's package source mapping
- The documentation site builds again: Docsy is pinned to v0.13.0 and Hugo to 0.169.0, after Docsy v0.18 moved its theme out of the module root
- F# `Decimal.fromString` roundtrips invariant SDK output under comma-decimal cultures while retaining unambiguous localized input; package migration checks also exercise a later release version ([#465](https://github.com/finos/morphir-dotnet/issues/465)).
- F# SDK arithmetic detects covered Int32 overflow; minimum-Int remainder/modulus by -1 return zero, direct string comparison uses ordinal UTF-16 order, dictionary union keeps the first value, and Decimal text is invariant without trailing fractional zeros. Added checked Float-to-Int rounding and optional `Decimal.div` while retaining existing public entry points ([#465](https://github.com/finos/morphir-dotnet/issues/465)).
- `VFileTree.writeToDisk` no longer writes outside the output directory. It returns `TreeWriteError.InvalidTree` and writes nothing for rooted or `..` paths, files with no path, and duplicate locations, and it no longer adds the root tree path to output paths (#376)
- `VFileTree.toFileMap` and `toStringMap` keys are now relative to the tree root and use forward slashes on every platform (#376)
- `VFileTree.findFile` and `updateFile` accept a file's full location, and `updateFile` no longer changes same-named files in other directories (#376)
- Claude Code skills now load correctly - renamed all `skill.md` files to `SKILL.md` (uppercase, case-sensitive requirement)
- Updated all skill documentation references to use correct `SKILL.md` filename
- **CRITICAL**: Removed contradictory Claude co-author example from AGENTS.md and strengthened CLA compliance warnings in both AGENTS.md and CLAUDE.md (issue #270)

### Added
- `MORPHIR_DISCOVERY_BOUNDARY` and `MORPHIR_ISOLATED` environment variables mirror `--discovery-boundary` and `--isolated` for the `config` commands, and `MORPHIR_DISCOVERY_BOUNDARY` also bounds `project migrate` and `workspace migrate`, which gains `--discovery-boundary`. A flag wins over its variable, which wins over the default. `--isolated` now accepts `true` or `false`, so a command can turn off isolation set in the environment, and `config path --json` reports the effective controls and their sources ([#494](https://github.com/finos/morphir-dotnet/issues/494))
- Shared test sandbox, child-process runner and machine tripwire in `tests/Morphir.TestSupport`, so tests stay off the developer's real Morphir configuration ([#494](https://github.com/finos/morphir-dotnet/issues/494))
- Synchronized both landed C# CLI deltas into the main specifications and archived all nine checked tasks; archive PR landing and coordinator cleanup remain pending ([#481](https://github.com/finos/morphir-dotnet/issues/481), [#482](https://github.com/finos/morphir-dotnet/pull/482)).
- Added exact `csharp` CLI generation with numeric 14/15 profiles, a validated optional namespace and the existing owned publication/reporting flow ([#481](https://github.com/finos/morphir-dotnet/issues/481)).
- Synchronized the landed C# generator's eight requirements and 19 scenarios into the main specification and prepared its 13-task OpenSpec archive; archive follow-up landing and coordinator cleanup remain pending ([#474](https://github.com/finos/morphir-dotnet/issues/474), [#478](https://github.com/finos/morphir-dotnet/pull/478)).
- Reusable in-memory C# generation for typed classic/v4 libraries with numeric 14/15 profiles, immutable structural values, executable functions/patterns/record operations, public SDK bindings, located whole-model refusals and traversal cancellation; compiled business/SDK observations and bounded modern package-host/trimming verification ([#474](https://github.com/finos/morphir-dotnet/issues/474)).
- Synchronized and archived the landed C# and F# SDK runtime specifications, with stable API handoff paths and separate runtime delivery evidence; archive PR #472 landed; empty archive-directory cleanup remains pending ([#464](https://github.com/finos/morphir-dotnet/issues/464), [#465](https://github.com/finos/morphir-dotnet/issues/465)).
- Independent immutable Morphir.SDK.CSharp runtime for the bounded .NET SDK contract, with checked scalars, persistent collections and public package consumer verification ([#464](https://github.com/finos/morphir-dotnet/issues/464))
- Canonical `Morphir.SDK.FSharp` runtime package and dependency-only `Morphir.SDK` compatibility package, preserving existing runtime identities, with packaged source/binary checks and the frozen F# conformance adapter ([#465](https://github.com/finos/morphir-dotnet/issues/465))
- Shared .NET SDK conformance contract, pinned source evidence, tagged offline corpus and fixture validator for the C# and F# SDK deliveries ([#460](https://github.com/finos/morphir-dotnet/issues/460))
- `Morphir.Ion` library project: reads Ion 1.0 text and binary into immutable values, and reads the MEP `morphir_model` document with `ModelDocument.readPayload`, `readFile` and `decodeIr`. Reading keeps the exact bytes, annotations, numeric exactness and unknown data, and decodes the classic or v4 IR only on request
- `IonIr.read` and `IonIrTree.read` read the draft Ion IR format `ionVersion` `0.1.0-draft.1` for IR 3.0.0, 3.1.0 and 4.0.0, from a single file or a document tree. `IonIrParser` plugs them into a pipeline
- `Morphir.IR.V4`, an immutable IR v4 model, with the JSON reader `V4Json` and strict name parsing in `CanonicalNames`
- `ClassicJson.readDistribution`, a public reader for complete classic IR JSON, including dependency specifications
- `VFileTree` usage and morphir-elm migration guide in the `Morphir.IR.Pipeline` README (#376)
- `VFileTree.fromFileMap` now infers nested directories from map keys (#376)
- `VFileTree.loadFromDisk` loads a directory into a tree, returning `Result<VFileTree, TreeLoadError>` (#376)
- `TreeWriteError`, `TreeWriteProblem` and `TreeLoadError` union types for file tree I/O failures, with `describe` functions for display (#376)
- `ParseError` union type, which keeps the rule, location and source position of malformed input on the resulting diagnostic
- Strongly typed pipeline: `Parser<'T>`, `Plugin<'T>`, `Compiler<'T>` and `MorphirProcessor<'T>` replace the `obj`-based contracts, and a run returns a `VFileTree`, so one input can produce many output files
- `MorphirProcessor.run` accepts a file or a directory, and `MorphirProcessor.runEachFile` runs once per file in a tree
- `Parser.forFile`, `Parser.ofParseable` and `Plugin.focus` for building typed stages
- `Property<'T>` and `PropertyBag`, a typed replacement for string-keyed data on files, trees, tree configuration and processors, with functions that work with `|>`, `<|` and `>>`
- `VFileContent` for text, bytes or a re-openable `StreamSource`, so file content does not have to be held in memory. `VFileTree.loadFromDisk` no longer reads files up front, and binary files survive a round trip
- Tree-level diagnostics on `VFileTree`, and `Path` and `Location` on `VMessage`, with `VMessage.describe` for display
- `VFileTree.publish`, `validatePublish`, `readManifest` and `declaredOrder`: path containment, symbolic link rejection, case-insensitive collision detection, staged publication, no rewriting of identical files, and manifest-based removal of obsolete files, moved from the Myriad build task so that every backend gets them
- `Morphir.CodeGeneration.FSharp` now runs as a real pipeline through `Transpiler.generate`, with scope validation, type validation and constant folding. Definite type mismatches now fail the build, and folded literals appear in generated source. `GeneratedModule` and `Diagnostic` are removed in favour of `VFileTree` and `VMessage`
- `Optimizer` and `TypeValidator` now process real packages: the optimizer matches SDK functions by fully qualified name and folds only where the result matches run-time behaviour, and the validator reports only definite mismatches
- **Morphir.SDK F# Runtime Library** (`Morphir.SDK` NuGet package)
  - F# runtime library providing Morphir SDK types and functions for generated F# code
  - 14 core modules: Basics, Maybe, Result, List, String, Int, Bool, Char, Dict, Set, Tuple, LocalDate, LocalTime, Decimal
  - Type aliases for F# built-ins (Option, Result, Map, Set, DateOnly, TimeOnly)
  - 175 tests (125 unit + 50 property-based tests with FsCheck)
  - Comprehensive XML documentation and README
  - AOT-compatible (no reflection)
  - Foundation for F# code generation backend (Phase 0 of #363)
- Comprehensive skills troubleshooting guide (`.claude/skills/TROUBLESHOOTING.md`)
- Cross-platform skill invocation documentation in `.agents/skills-reference.md` with platform-specific guidance for Claude Code, GitHub Copilot, Cursor, Windsurf, and JetBrains AI
- Skill alias documentation (documentation-only feature, not functionally supported)
- Session restart requirements documentation for Claude Code skills
- Agent capabilities comparison table showing feature support across platforms
- **CLA compliance guidance** in AGENTS.md with remediation steps for accidental Claude co-author inclusion
- **GitHub issue #270** for F# script to automatically remove Claude co-author from commit history

### Removed
- **BREAKING** (Morphir.Tooling API): `Morphir.Tooling.Configuration.ConfigResolver`, `WorkspaceDiscovery` and `TomlParser` are removed. They walked to the filesystem root and treated any `.git` or `.morphir` directory as a workspace, including the global Morphir home, and could not be contained. No restorable package release ever exposed them. Use `ProjectSystem.Discover` / `ProjectSystem.LoadDetailed` and the shared resolver; replace the legacy CI mode with a named profile selected by `--profiles` or `MORPHIR_PROFILES` ([#494](https://github.com/finos/morphir-dotnet/issues/494))

### Changed
- The `dist`, `tool` and `extension` sub-commands now fail with "not implemented yet", exit code 1 and a link to their tracking issue ([#490](https://github.com/finos/morphir-dotnet/issues/490), [#491](https://github.com/finos/morphir-dotnet/issues/491), [#492](https://github.com/finos/morphir-dotnet/issues/492)). Previously they printed a message and exited 0 without doing anything
- **BREAKING**: The classic `Distribution` has a `Specs` case for IR 3.1.0, and `Distribution.packageDefinition` returns an option because a `Specs` distribution has no definitions
- The classic JSON decoder moved from `Morphir.CodeGeneration.FSharp` to `Morphir.Models`. F# generation keeps its acceptance rules and diagnostics
- Ignore Reqnroll-generated `*.feature.cs` files per Reqnroll best practices
- Configure `ReqnrollUseIntermediateOutputPathForCodeBehind` in test projects to generate code-behind files to obj/ directory
- Refactored AGENTS.md skill invocation section - replaced verbose content (155 lines) with concise summary (29 lines) and link to detailed guide in skills-reference.md
- Enhanced skill template (`.claude/skills/template/SKILL.md`) with optional alias documentation in YAML frontmatter
- **Strengthened CLA warnings** in AGENTS.md and CLAUDE.md - made CRITICAL sections highly visible with remediation steps and reference to automated script (issue #270)

## [0.3.0-rc.2] - 2025-12-18

### Added
- Ionide.KeepAChangelog integration for changelog-driven versioning
- ChangelogHelper class for parsing and manipulating CHANGELOG.md
- PrepareRelease target for automated release preparation
- **Proto WASM Plugin**: Tool manager plugin for proto (moonrepo.dev/proto)
  - Enables `proto install morphir` for cross-platform installation
  - Platform support: Linux (x64, arm64), macOS (x64, arm64), Windows (x64)
  - Automatic version detection and platform-specific executable downloads
  - Released as separate plugin-v0.1.0 on GitHub
- **GitHub Release Automation**: Deployment workflow now creates GitHub releases
  - PackPlatformTarballs Nuke target for creating platform-specific tarballs
  - CreateGitHubRelease Nuke target for automated release publishing
  - Release notes with proto installation instructions
  - Platform-specific executable tarballs as release assets
- **Build System Improvements**:
  - RunRustCommand helper with intelligent stderr categorization
  - Proper log levels for Rust/Cargo output (info, warning, error)
  - PluginVersion parameter for proto plugin versioning

## [0.3.0-rc.1] - 2025-12-18

### Added

- **NOTE**: This is a pre-release version. Stable v0.3.0 pending LanguageExt.Core 5.0 stable release.
- Serilog logging infrastructure for CLI tools (Serilog, Serilog.Extensions.Hosting, Serilog.Sinks.Console)
- CLI logging standards documentation in AGENTS.md and CLAUDE.md
- `.morphir/out/` directory to .gitignore
- New Morphir.Tool project as dedicated dotnet tool package (#214)
- Build.CI.cs with DevWorkflow, CILint, CITest targets for local CI simulation (#214)
- Comprehensive XML documentation for all 23+ build targets (#214)
- Morphir package now published to NuGet/GitHub releases (#214)
- Comprehensive Phase 1 test plan in docs/content/contributing/qa/
- **QA Testing Framework**: Claude Code skill with F# automation scripts
  - smoke-test.fsx - Quick 2-minute smoke test
  - regression-test.fsx - Full 10-15 minute regression suite
  - validate-packages.fsx - NuGet package structure validation
- **Release Management Framework**: Claude Code skill with F# automation scripts (#220)
  - prepare-release.fsx - Automated pre-flight checks and version suggestion
  - monitor-release.fsx - Real-time workflow monitoring with issue updates
  - validate-release.fsx - Post-release package and installation validation
  - resume-release.fsx - Recovery from failed releases
  - Comprehensive release playbook and tracking templates
- **Cross-Agent Guidance**: `.agents/` directory for specialized topics
  - .agents/qa-testing.md - Comprehensive QA testing guidance for all AI agents
  - .agents/release-management.md - Release workflow and automation guidance
  - .agents/README.md - Navigation and contribution guidelines
- **Multi-Agent Pointer Files**: Configuration for all major AI coding assistants
  - .cursorrules - Cursor AI pointer file
  - .github/copilot-instructions.md - GitHub Copilot instructions
  - .windsurf/rules/morphir.md - Windsurf AI rules and workflows
  - .idea/ai-assistant-rules.md - JetBrains AI Assistant configuration
- **AGENTS.md Enhancements**:
  - Quick Navigation section (links to .agents/, CLAUDE.md, docs/)
  - Section 18: Specialized Guidance (detailed links to .agents/ topics)
  - Section 19: Resources and References (comprehensive resource index)
- **Comprehensive Developer Documentation**:
  - F# Coding Guide with active patterns, railway-oriented programming, and best practices
  - Native AOT, trimming, and size optimization guide
  - System.Text.Json guidance for F# with common gotchas
  - F# 9 nullable reference types guidance for C# interop

### Changed
- **BREAKING**: Configured all logging to write to stderr instead of stdout to preserve stdout for command output. Scripts parsing stdout may need updates.
- Updated E2E test runner script to use Nuke build commands instead of deprecated `just` commands
- Updated README.md with Nuke build command examples
- Split monolithic Build.cs into 5 partial classes: Packaging, Publishing, Testing, CI (#214)
- Tool command name follows dotnet convention: `dotnet-morphir` (was `morphir`) (#214)
- Morphir.Tool delegates to public Morphir.Program.Main() eliminating code duplication (#214)
- Morphir project now packable (IsPackable=true, was false) (#214)
- PackAll now builds 4 packages: Core, Tooling, Morphir, Tool (#214)
- **AGENTS.md**: Enhanced with navigation sections for better discoverability across AI tools

### Fixed
- Fixed JSON output contamination issue where log messages were being written to stdout
- CLI tools now properly separate data (stdout) and diagnostics (stderr) following Unix conventions
- E2E tests for JSON output format now pass (12/13 tests passing, up from 10/13)
- Windows build file locking issues by removing problematic MSBuild target (#214)
- Circular build dependencies causing CS2012 errors on Windows (#214)
- Directory cleaning conflicts in PackAll target (#214)
- Resolved prepare-release.fsx issues with Spectre.Console markup and Int64 (#220)

### Removed
- Deprecated scripts: pack-tool-platform.cs, build-tool-dll.cs (#214)
- Problematic GenerateWolverineCode MSBuild target, moved to Nuke (#214)
- BuildInParallel=false workaround after fixing root cause (#214)

## [0.2.0-alpha-010] - 2025-12-16

### Changed
- Updated NuGet packages to use LICENSE.md instead of LICENSE file
- Added README.md to all NuGet packages (Morphir.Core, Morphir.Tooling, and Morphir CLI tool)
- Enhanced CLI tool package metadata with complete NuGet properties (license, authors, URLs, repository info)

### Fixed
- Fixed LICENSE file path issue in NuGet packages (now correctly placed at package root)
- Removed redundant LICENSE configuration from individual project files

## [0.2.0-alpha-009] - 2025-12-17

### Fixed
- NuGet packages now include LICENSE file and correct metadata (repository URL, project URL)

## [0.2.0-alpha-003] - 2025-12-16

### Added
- Platform-specific install scripts for Linux, macOS, and Windows that download from NuGet
- Support for installing Morphir without .NET SDK using platform-specific trimmed executables

### Changed
- Switched from AOT-compiled executables to trimmed (non-AOT) executables for publishing
- Updated deployment workflow to build trimmed single-file executables instead of AOT executables
- Updated pack-tool-platform script to handle trimmed executables with lowercase naming
- Updated installation documentation with platform-specific install script instructions

## [0.2.0-alpha-002] - 2025-12-16

### Added
- Single-file executable support for Morphir CLI (AOT-compiled, no .NET runtime required)
- Matrix build workflow for cross-platform executable generation (linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64)
- Platform-specific NuGet tool package that automatically selects the correct executable
- Build scripts for tool DLL and platform packaging (`scripts/build-tool-dll.sh`, `scripts/pack-tool-platform.sh`)
- `publish-executable` and `publish-executables` tasks for building platform-specific executables
- `pack-tool-platform` task for packaging platform-specific executables into NuGet tool package

### Changed
- Refactored complex build logic from justfile to dedicated scripts for better maintainability
- Updated deployment workflow to use matrix builds for platform-specific executables

## [0.2.0-alpha-001] - 2025-12-16

### Added
- Initial release of Morphir .NET
- Morphir.Core library with IR types
- Morphir.Tooling library with tooling infrastructure
- Morphir CLI as a dotnet tool
- Integration of just command runner for build orchestration
- Packaging and publishing tasks for NuGet packages and dotnet tools
- Local publishing workflow for testing packages
- KeepAChangelog integration for automatic versioning

[Unreleased]: https://github.com/finos/morphir-dotnet/compare/v0.4.0-alpha.4...HEAD
[0.4.0-alpha.4]: https://github.com/finos/morphir-dotnet/compare/v0.4.0-alpha.3...v0.4.0-alpha.4
[0.4.0-alpha.3]: https://github.com/finos/morphir-dotnet/compare/v0.4.0-alpha.2...v0.4.0-alpha.3
[0.4.0-alpha.2]: https://github.com/finos/morphir-dotnet/compare/v0.4.0-alpha.1...v0.4.0-alpha.2
[0.4.0-alpha.1]: https://github.com/finos/morphir-dotnet/compare/v0.3.0-rc.2...v0.4.0-alpha.1
[0.3.0-rc.2]: https://github.com/finos/morphir-dotnet/compare/v0.3.0-rc.1...v0.3.0-rc.2
[0.3.0-rc.1]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-010...v0.3.0-rc.1
[0.2.0-alpha-010]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-009...v0.2.0-alpha-010
[0.2.0-alpha-009]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-008...v0.2.0-alpha-009
[0.2.0-alpha-003]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-002...v0.2.0-alpha-003
[0.2.0-alpha-002]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-001...v0.2.0-alpha-002
[0.2.0-alpha-001]: https://github.com/finos/morphir-dotnet/releases/tag/v0.2.0-alpha-001

