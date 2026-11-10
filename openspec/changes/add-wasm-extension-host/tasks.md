# Tasks

This is the morphir-dotnet adoption of the finos/morphir change `add-dotnet-wasm-extension-host` (draft PR finos/morphir#1148, read at `8d678d2b`). Groups 1 to 6 keep the parent's task numbers and wording, so that completion can be reported against the parent. The indented notes under a checkbox say where the work lands in this repository; they are planning detail and are not tracked. Group 0 holds the two tasks only this repository has.

GitHub issue: [finos/morphir-dotnet#563](https://github.com/finos/morphir-dotnet/issues/563). This repository has no Beads database.

Gates and order:

- **Nothing below starts before this change is approved**, as the extension-model PRD requires. Task 2.1 is this change.
- **Task 2.2 is the first implementation task and a gate.** If any of its three claims fails, stop and return decision KRN-1 for review. Do not switch to another approach.
- Then groups 2, 3 and 4 in order. Task 3.2 no longer waits for the parent: the notification member is fixed at `48d10706f` (design NTFY-1).
- The pin under `tests/Fixtures/MepWasm/` moves to `8d678d2b` in the pull request that carries this revision (design PIN-1). Tasks 1.5, 2.5 and 3.2 test against that pin.
- Task 5.1 can start once group 2 exists. Tasks 5.2 to 5.4 wait for the publish profiles of `add-deployment-and-acquisition` to merge (design SEQ-1); check the state of that work and report it in the issue before starting.
- Group 6 runs only against a pin of a merged parent commit.

Rules for every task:

- Red, green, refactor: the failing test comes first. Each task lands its own tests and documentation.
- The parent specs win where they and the pinned contract JSON differ. Re-read the parent before depending on a detail, and ask when a detail is unsettled (design PIN-1).
- No trim or AOT warning and no suppression. F# code that C# calls follows the `fsharp-csharp-interop` skill, and the `fsharp-csharp-interop-reviewer` agent runs on each diff that crosses that boundary.
- Build one project with `dotnet build <project> -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false`, and run a TUnit assembly with `dotnet exec <dll> --treenode-filter "<filter>"`, as `build/Build.Testing.cs` does. Run one publish at a time and remove build output a task no longer needs.
- When a parent task is complete, report it in the tracking issue and the orchestrating thread with the evidence its verification clause asks for. Do not edit files in finos/morphir.

## 0. Adoption housekeeping, morphir-dotnet

- [x] 0.1 Update the extension-model PRD for the decision of 9 November 2026 that the host runs `extism-kernel-v1` only: the Runtimes section, step 2a, step 5, the reference guest, the status table and the "Memory limit report" open point, with a link to this change. Name the listing of configured WASM extensions in `extension list` ([#565](https://github.com/finos/morphir-dotnet/issues/565)) in steps 2a and 3 and in the status table. Add a dated note to the WASM runtime spike that its per-byte callback and module-serialization advice no longer applies. Verify the documentation build passes and that no page still says morphir-dotnet hosts `morphir-bytes-v1`.
  - Files: `docs/content/contributing/design/prds/morphir-extension-model.md`, `docs/content/contributing/design/prds/_index.md`, `docs/content/contributing/design/spikes/extension-model/wasm-runtime.md`.
  - Design: ORD-1.
- [x] 0.2 Pin the profile contract, the crafted corpus and its modules under `tests/Fixtures/MepWasm/`, copied unchanged from one finos/morphir commit, with `PROVENANCE.md`, the upstream licence and notice. Verify a test recomputes every recorded SHA-256, and that each module's digest equals the one the corpus records.
  - Files: `tests/Fixtures/MepWasm/` (new), a provenance test in the pattern of `tests/Morphir.Frontend.FSharp.Tests/FixtureProvenanceTests.fs`.
  - Design: PIN-1. The first pin is a commit of the draft PR's branch; it moves when the parent revises.

## 1. Shared contract and crafted corpus, finos/morphir (runner part of 1.5 only)

Parent tasks 1.1 to 1.4, 1.6 and 1.7 belong to finos/morphir and are dependencies only. At `8d678d2b` the parent has ticked 1.1 and added 1.7, which names `notificationDelivery` in the MEP handshake model.

- [x] 1.5 Add to the kit's runner only what is needed to run crafted cases through a stdio worker and to read its stderr reports, in the .NET runner in morphir-dotnet. Verify the complete corpus against the MoonBit native CLI's worker mode first, since that host already exists, and record each difference as a contract question or a MoonBit issue before any .NET result is compared.
  - The crafted cases are Gherkin (maintainer, 9 November 2026; design RUN-1). Generate the features from the pinned JSON; never edit one by hand.
  - Files: `src/Morphir.Conformance/` (a reader for the pinned JSON, the generator, the `wasm-host` suite in the Gherkin reader, a worker adapter, the matchers of the kit's `conventions`), `conformance/suites/wasm-host/` (the generated features and the id map), `conformance/steps/` (the new vocabulary version) and `conformance/README.md`, `src/Morphir.Tooling/Features/Mck/` (the experimental `morphir mck wasm-host run` and `report-check`), both entry points, and the `lint` job in `.github/workflows/development.yml` (the stale-features check).
  - Order inside the task: the vocabulary version and its steps; the JSON reader and the case model; the generator and the id map; the Gherkin reader for the suite; the equality test over every case; then the adapter and the comparer.
  - First failing tests: in `tests/Morphir.Conformance.Tests` (new), the JSON reader loads the pinned kit and lists every case (59 at `8d678d2b`: 49 for `extism-kernel-v1`, 10 for `morphir-bytes-v1`); the generated features and the pinned JSON give equal case models for every case; a feature edited by hand and a feature with an unknown step are refused; a case added to the corpus takes the next unused number and no existing number moves; a fake worker script gives a `refused` event and the comparer passes the case; a module whose bytes differ from the recorded digest is refused with both digests; a worker still alive at the end of a case has its process tree stopped. Use `ChildProcess.RunAsync` and `TestSandbox` from `tests/Morphir.TestSupport`.
  - A run with alternatives passes when one alternative whose condition matches is satisfied, and two alternatives may share a condition. A case's `worker.instructions` is passed as `--instructions`, as `requestBytes` and `responseBytes` are passed as `--request-bytes` and `--response-bytes`.
  - Specs: `wasm-host-qualification`. Design: RUN-1.
  - Needs a MoonBit native CLI build. If none is available on the machine, say so and run that verification where one is.
  - The report verdict follows parent task 1.4. Until it is defined, write per-case observations and no verdict.

## 2. Engine layer and the kernel proof, morphir-dotnet

- [x] 2.1 Open a morphir-dotnet OpenSpec change that adopts these requirements for roadmap steps 2a (runtime) and 5, updating its `extension-registry` delivery values and `mep-protocol` host dispatch, and recording that the host runs `extism-kernel-v1` only, so the roadmap's byte-profile-first order no longer applies. Verify it validates strictly and links this change; no implementation task below starts before it is approved, as that repository's PRD requires.
  - This change. Verify with `openspec validate add-wasm-extension-host --strict` and `mise run openspec:verify`. Tick it when the maintainer has approved.
- [x] 2.2 Prove the kernel approach before anything else is built on it: embed the `moonrockz/extism` kernel module at the digest MoonBit ships (Apache-2.0), link it in a store and run `morphir.initialize` on the Python `0.6.0` module under the Native AOT host. Verify zero IL2026 and IL3050 warnings with no suppressions, that the limiter applies to the kernel's memory and that the host reads the `handle` result directly. If any of these fails, stop and return decision KRN-1 for review.
  - Files: `src/Morphir.Extensions.Wasm/` (new C# project: the engine, the embedded kernel, the minimum linking for one call), `tests/Morphir.Extensions.Wasm.AotProbe/` (new, modeled on `tests/Morphir.Mep.AotProbe`), `Directory.Packages.props` (`Wasmtime` 50.0.1), `Morphir.slnx`, the kernel's licence and notice beside its bytes.
  - Record: the `moonrockz/extism` version (`0.3.0` in finos/morphir-moonbit today), the kernel module's SHA-256, how the bytes were obtained, the publish log's warning count, the probe's output and the runner it ran on.
  - The Python module is downloaded and checked against its published digest. It is not committed.
  - First run on the Python `0.4.2` module with a raw MEP `0.1` message, before decision MEP-1, then on the `0.6.0` module with a `0.2.0-draft.1` initialize, which is what the task now names. Both runs are in the evidence.
  - Design: KPF-1, LAY-1.
- [x] 2.3 Add failing tests for a message-based host channel in `Morphir.Mep`, then implement it and move the stream framing behind it. Verify every existing process-guest test and replay passes unchanged, and that a fake single-message guest completes initialize, a request and shutdown.
  - Files: `src/Morphir.Mep/Peers.fs`, `src/Morphir.Mep/Connection.fs`, a new channel file, `src/Morphir.Mep/README.md`.
  - First failing tests: in `tests/Morphir.Mep.Tests`, the scenarios of the `mep-protocol` delta, with a fake single-message guest built on `Dispatcher.DispatchAsync`. Keep the stream constructor of `HostSession`.
  - Republish `tests/Morphir.Mep.AotProbe` and confirm no new IL warning.
  - Design: CHAN-1.
- [x] 2.4 Add the engine layer: one engine with fuel and epoch interruption, one store per session, in-memory module reuse by digest, and classification from the validated module against the contract table. Verify the crafted classification cases in-process, including an unknown import named in the refusal, a feature refusal for shared memory and a byte-profile module refused with the profile named.
  - Files: `src/Morphir.Extensions.Wasm/`, `tests/Morphir.Extensions.Wasm.Tests/` (new, TUnit), `src/Morphir.Extensions/Registry.fs` (the `wasm` delivery and its availability rule) with `tests/Morphir.Extensions.Tests`.
  - The classifier reads its import table from the pinned contract file, not from a second copy in code. The `classification/` cases of the pinned corpus are the test data.
  - Specs: `dotnet-wasm-host`, `extension-registry`. Design: REG-1.
- [ ] 2.5 Implement the bounds and their mapping: fuel, epoch deadline, cancellation, the per-memory limit and the at-ceiling memory rule, with memory size on every failure. Verify the crafted bounds cases, that Ctrl+C stops a spinning guest within one second with exit status 130, and record the published guests' fuel use and compile time on the kit package in the change's evidence.
  - The instruction budget is the contract's one session value, 5,000,000,000. Deadline cases use the corpus margin; do not shorten a deadline in a way that hides a missing bound.
  - The memory rule is the one the parent corrected a second time on 10 November 2026 (`8d678d2b`): a call that ends in a trap while the guest's or the kernel's memory is at its ceiling is `extension.memory_limit`. Every other end keeps its own result, also at the ceiling: a failure the guest reported (an Extism error, a non-zero result, no output, a `proc_exit` with a code that is not 0), a failure the host detected (a response over its limit), and a stop for the budget, the deadline or a cancellation. Each failure diagnostic states both memory sizes and the ceiling. The code follows it since the pin moved to `8d678d2b` (design IMPL-1).
  - A module that does not export its memory runs as a copy with one added export, as the parent requirement "Reading a memory the module does not export" allows (design MEM-1).
  - The Ctrl+C check has its own parent requirement now, "Worker interruption": stop the guest within one second, exit status 130, no `fault` event. It is verified through the worker in task 4.1. This task checks cancellation in-process.
  - Evidence goes in `openspec/changes/add-wasm-extension-host/evidence/`.
- [x] 2.6 Load a module from `extensions.<name>.path` by content, with an optional declared ABI and digest, and separate guest output from command results. Verify a text file at the path, a replaced module and a guest that logs 10 MiB under `--json`, and document the configuration beside the existing extension settings.
  - Files: `src/Morphir.Configuration/Schema.fs` and its generator `scripts/configuration/generate-schema.py` (the `abi` and `sha256` amendments), `src/Morphir.Tooling/Features/Extensions/` (the loader), the configuration reference under `docs/content/docs/`.
  - No product command reaches the loader in this change. Verify at the service level, and through the worker for the output bound. Confirm and document how a relative `path` resolves today.
  - Design: CFG-1, and review questions 4 and 5.

## 3. Extism profile and sandbox, morphir-dotnet

- [x] 3.1 Implement the Extism adapter on the proven kernel: one instance per session, input and output through the kernel, Extism errors as session failures and `exit` ending the instance. Verify a compile session on the Python guest makes one `handle` call per request and that a guest which advertises nothing receives no notification.
  - Files: `src/Morphir.Extensions.Wasm/`. The adapter is the WASM implementation of the channel from 2.3.
  - "The Python guest" is release `0.6.0`, as the parent's tasks state. The session's `morphir.initialize` carries a model transport offer, as the parent requirement "Protocol line" asks.
  - The `extism/` and `limits/` cases of the pinned corpus are the test data.
- [x] 3.2 Implement negotiated notifications: read `capabilities.notificationDelivery` from the guest's `morphir.initialize` result, and for the value `call` deliver each notification as one `handle` call and fail the session when it returns output. Verify the crafted cases for a guest that advertises `call`, one that answers a notification, one that advertises a value the contract does not list, and a published guest that advertises nothing.
  - The member is a string. `call` asks for delivery; an absent member, `none` and any other value, the reserved `poll` among them, mean no delivery (design NTFY-1). The cases are in the pin from `48d10706f`, `extism/notification-delivery-unlisted` among them.
- [x] 3.3 Implement the host-function policy and the Morphir functions with their limits. Verify the crafted sandbox cases: `http_request` fails with no connection attempt and its URL recorded, the variable store and IR cache enforce their limits, and a second session starts empty.
  - The private workspace directory is created for the session and removed with it. Tests use `TestSandbox` and never touch the real home or temporary roots outside it.
- [x] 3.4 Configure the WASI sandbox with no preopens, arguments, environment or stdin, and bounded capture of stdout and stderr. Verify `path_open` returns an error with the session continuing, clocks and random bytes work, and guest stdout never reaches a MEP response.
  - Decide here, with the crafted cases as evidence, whether the engine's WASI or managed host functions supply the surface (design, open questions).
- [ ] 3.5 Run the generic guest kit's model tier in-process for the Python `0.6.0` guest and the SDK fixture. Verify IR equal to the kit's expectations for each claimed IR version, and document the Extism profile's resident memory bound of twice the ceiling in the host's extension guide.
  - In-process means through `HostSession`, which speaks MEP `0.2.0-draft.1` only. The kit's legacy tier is recorded as not run, and the result is not reported as a complete run of the kit (design MEP-1). The kit's documents and expected IR are fixed data; the comparison applies the kit's three stated rules and no other.
  - Design: RUN-2.

## 4. Worker mode and runtime report, morphir-dotnet

- [x] 4.1 Add `morphir extension worker` to both executables with MoonBit's argument shape, joining a stream channel to a WASM channel through the shared session code. Verify stdout carries only MEP frames, a refused module yields a `refused` line on stderr with no stdout bytes and a non-zero exit status, and a stale `--build-identity` exits before the module is read.
  - Files: `src/Morphir.Tooling/Features/Extensions/`, `src/Morphir/Program.cs`, `src/Morphir.Tool/Program.cs`, `tests/Morphir.E2E.Tests` for both executables.
  - The worker relays messages and is registered before the CLI host is built (design WRK-2). It takes every argument the contract lists, as the parent requirement "Worker mode" now names them: `--abi`, `--module`, `--build-identity`, `--request-bytes`, `--response-bytes`, `--call-ms`, `--instructions`, `--memory-pages`, `--json` and `--help`.
  - Interrupted while a guest is inside `handle`, the worker stops the guest within one second, writes no `fault` event and exits with 130 (parent requirement "Worker interruption"). `cancelled` is a host-session outcome only and has no fault kind.
  - A request frame over `--request-bytes` is fault `failure` without a call, exit 1. A module file that cannot be read or is not a well-formed module is fault `failure` before `started`, exit 1.
  - The events, their members, the 4 KiB message cut, the 64 KiB output tail and the exit statuses follow the MoonBit worker's README at the commit recorded in the pin.
- [ ] 4.2 Add the runtime availability report and the `extension.runtime_unavailable` failure with its reasons, probing the engine only on first need. Verify a host started without its engine library reports `engine_missing`, fails a WASM command by name and still runs commands that need no WASM.
  - Files: `src/Morphir.Tooling/Features/Extensions/` (`extension runtimes`), `docs/content/docs/cli/`.
  - Design: RPT-1. The `deployment` value needs the build identity of `add-deployment-and-acquisition`; until it merges, report the identities that exist and say so in the evidence.
- [ ] 4.3 Drive the worker from the kit's runner with the same worker arguments used for the MoonBit host, changing only the host path. Verify the generic guest kit's model tier and the crafted corpus both complete with no runner change, and document the command in the contributor guide as internal and experimental.
  - The crafted corpus runs through the runner of task 1.5. The generic guest kit runs through the Rust `morphir mck mep guest-run`, unchanged (design RUN-2, review question 1).
  - That runner cannot leave the kit's legacy tier out. Only the model tier is run against this host, and how that is done through `guest-run` is an open question (design, open questions). The legacy cases are recorded as not run, and the run is not reported as a complete run of the kit. Whether the kit drops the tier is an open maintainer question in the parent (design MEP-1, open questions).

## 5. Packaging across offerings, morphir-dotnet

- [ ] 5.1 Try the static link of the engine into the Native AOT executable on all six platforms before other packaging work, with the C API archive pinned by SHA-256 and its version checked against the managed package. Verify each platform's result on its own runner and record, per platform, static link or separate library and the shipped size.
  - A workflow of its own that publishes the AOT probe per runner. It does not touch the release profiles (design SEQ-1).
- [ ] 5.2 Package the engine for the `self-contained` and `framework-dependent` offerings on all six platforms. Verify each released archive on a clean machine of its platform, including first-run extraction with a read-only install directory.
- [ ] 5.3 Add release metadata for native assets and single-file claims per offering and platform, as `deployment/dotnet-profiles` requires. Verify a platform that ships a separate library does not claim single-file delivery and that the release check rejects a report cited for another offering.
  - In this repository the capability is `dotnet-deployment-profiles` of `add-deployment-and-acquisition`.
- [ ] 5.4 Convert `Morphir.Tool` to platform-specific packages with a fallback package that has no engine, first confirming the pinned SDK supports that arrangement. Verify install, update and uninstall from a local feed on Linux x64 and on Linux musl, where the tool installs and reports `wasm` as unavailable, and update the install guide and release notes in the same change.
  - The pinned SDK is 10.0.403 (`global.json`). If it cannot do it, keep a second, neutral package identity for those platforms, as the parent's PKG-2 says.

## 6. Qualification evidence, both repositories

- [ ] 6.1 Run host qualification from released artifacts on clean machines for every offering and platform that claims the runtime. Verify each report passes `report-check`, records the artifact digest and engine identity, and shows the memory ceiling and call deadline as enforced.
- [ ] 6.2 Run one bundle pinned in `.config/published-extension-bundles.toml` in both the Native AOT host and `dotnet-morphir`. Verify its digest matches the pin and its result matches the Rust CLI's for the same input, which is the roadmap's completion evidence for step 5.
  - Parent task 6.3 (the support table and the sibling changes in finos/morphir) belongs to finos/morphir. Report 6.1 and 6.2 so that it can be done there.

## Workflow follow-up

- These files are a proposal for review. Do not begin implementation from the planning turn.
- Keep these as separately scoped follow-up work: product commands that select a WASM analyzer, frontend or generator; listing configured modules in `extension list`, tracked in [#565](https://github.com/finos/morphir-dotnet/issues/565); user-granted host capabilities; extension installation; an on-disk compiled-module cache; a .NET port of the generic guest kit's runner; moving the rest of the compatibility kit from JSON to Gherkin, which the maintainer set as the direction on 9 November 2026 and which belongs with `establish-morphir-compatibility-kit`.
- When `add-deployment-and-acquisition` and this change are both open at archive time, the second to archive merges both edits to the `extension-registry` requirement "An entry states its delivery and hosts".
- Follow AGENTS.md for review, spec synchronization, archive, tracker closure and release approval.
