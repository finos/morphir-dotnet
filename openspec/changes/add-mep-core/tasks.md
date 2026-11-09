# Tasks

Every behavior task follows red/green/refactor: write the failing test first. Group 2 and the later groups that read or write MEP types need the Myriad JSON generator from `add-myriad-json-codecs`; groups 1 and 3 can start before it lands. Each group lands its own tests and documentation. The pinned contract in `tests/Fixtures/Mep/` is the reference; transcripts are reviewed inputs, and a disagreement with the contract text goes into `conformance/deviations.md` with an upstream issue. No contract, setting or registry key holds a format version as an integer. F# code that C# calls follows the `fsharp-csharp-interop` skill, and each group that changes that boundary runs the `fsharp-csharp-interop-reviewer` agent on its diff.

## 0. Generated wire-rule prerequisite (approved 2026-11-08 UTC)

These tasks land separately before the remaining MEP types. Keep codecs generated; the pinned contract's type documentation is the acceptance reference.

- [x] 0.1 Add opt-in `JsonDefaultJson` JSON-literal defaults for missing record fields. Test false flags, empty collections and fieldless union defaults, malformed defaults, and explicit null remaining invalid for non-null fields. Unannotated fields remain required.
- [x] 0.2 Add opt-in `JsonOmitEmpty` for collection fields. Test omission of empty collections, unchanged nonempty values, and round trips together with a missing-field default. Refuse unsupported field shapes at generation time.
- [x] 0.3 Support `JsonExtensionData` on one string-keyed JsonElement map field. Test flattened unknown-member preservation, duplicate refusal, known-name collisions on write, empty default and invalid declarations.
- [x] 0.4 Add generated versioned-alias migration and critical-path validation. Test the pinned claims draft.1 to draft.2 member and path migration, current canonical writes, mismatched name/version refusal, both names refused, and unknown critical paths refused. Keep protocol constants in annotated input rather than in the generator.
- [x] 0.5 Document the annotations, run the compatibility and snapshot suites, package consumers, Native AOT/JIT equality and the full repository tests. Verify unchanged contexts retain their existing output.

- [x] 0.6 Replace JSON-text scalar defaults with typed `JsonDefaultValue` literals, add `JsonDefaultEmpty` and `JsonDefaultValueProvider` for accessible static no-argument methods, and keep explicit `JsonDefaultJson` as the JSON-literal escape hatch. Verify strings, numeric bounds, decimal providers, fresh mutable defaults, explicit null, invalid declarations, provider signature compiler failures, canonical versions, packed consumers and JIT/Native AOT equality. Preserve `JsonPropertyName`; constructor-based model support remains deferred.

## 1. Tracking and project setup

- [x] 1.1 Open the step 1 issue for the extension model (link the PRD, #503, #499 and this change) and link it from `proposal.md`. Verify that the issue and the proposal link each other.
- [x] 1.2 Create `src/Morphir.Mep` and `tests/Morphir.Mep.Tests` (F#, Expecto with FsCheck, as `tests/Morphir.Extensions.Tests` does), with the interop rule 1 settings (nullness, warnings as errors, trimmable, AOT-compatible, documentation file). Add both to `Morphir.slnx` and to the build's test and AOT closure targets. Verify that `./build.sh --target Test` runs the empty test project and that the solution builds with no new warning.

## 2. MEP types and codecs (mep-protocol)

- [x] 2.1 Run the F# generator on the pinned `mep.ir.json` and record in the PRD (step 1, "Generate the MEP types") what it produces and what it lacks for the MEP wire rules. Verify that the PRD names the generator version, the command and the gap.
- [x] 2.2 Declare the `MepJson` context for `Morphir.Mep` with the Myriad JSON generator (`add-myriad-json-codecs`): camel-case members, skippable option fields, string tags for fieldless unions, and the contract's unknown-member rule. Verify that the project builds the context from the plugin's build output and that `MepJson.Verify` passes in a unit test.
- [x] 2.3 Write the annotated session types: `ExtensionInfo`, `ExtensionType`, `PeerInfo`, `CapabilityClaimSet` (claims version `0.1.0-draft.2`), `DescribeParams`, `InitializeParams`, `InitializeResult`, `ExtensionCapabilities`, `CancelParams`, `ProgressParams`, `RpcError`, `ErrorCode`, `Method`, `Diagnostic` with `DiagnosticSeverity`, `SourceLocation`, `SourceRange`, `SourcePosition` and `RelatedInformation`, the untyped `json` value, and the model-transport types (`ModelTransportTuple`, `ModelTransportLimits`, `ModelTransportSupport`, `ModelOperation`, `ModelCodec`, `ModelLayout`, `MorphirModel`, `ModelPayload`). Versions, ids and digests are parsed values (`JsonParsedValue`), not strings. Verify a round-trip test for each message of the three pinned transcripts, that an absent optional member is not written, and that a `null` for a required member fails.
- [x] 2.4 Add the contract test (design MEP-3): for each implemented type, read its constructor from `mep.ir.json` and check the wire member names and which members are required. Verify that the test passes, and that it fails when a member is renamed in a copy of the pin.

Foundation delivery covers `ProtocolVersion`, `DescribeParams`, the generated context, bounded framing, a describe constructor/pin-mutation contract test, and the C# runtime/Native AOT probe. Tasks 2.3 and 2.4 remain open for the complete wire-type inventory and its contract coverage; task 4.6 remains open for the full session AOT probe.

## 3. JSON-RPC layer (mep-protocol)

- [x] 3.1 Write the hand-written message classification (request, notification, response, error response by their members), with raw params, and `RequestId` (`u64` or string; generated with `SelectByTokenKind` once the generator lands). Verify the spike cases as unit tests: ids `"req-abc"`, `""`, `42`, `9223372036854775808` and `18446744073709551615` echo exactly; `-1`, `1.5` and a number over `u64` give `-32600`; bad JSON gives `-32700` with `"id": null`.
- [x] 3.2 Write the dispatcher, `DispatchAsync(bytes, ct) -> bytes option`, with a method table of typed handlers. Verify: a one-member params object (`DescribeParams` with only `protocolVersions`) binds as the whole object; an unknown method gives `-32601`; a params type mismatch gives `-32602`; a notification gives no bytes, also when its handler fails; a handler exception gives `-32603` and the next request succeeds.
- [x] 3.3 Write the `Content-Length` framer with the limits 8192 header bytes and 64 MiB body. Verify: frames split across many reads, a body with non-ASCII text, an empty-string id, a missing or malformed length, a header block over the limit, a body over the limit refused before it is read, a stream that ends inside a frame, and log text outside a frame each give the specified result.
- [x] 3.4 Write the connection: read frames, dispatch inbound requests, match inbound responses to the pending-request table, send and receive `$/cancelRequest`. Verify over in-memory pipes: a cancelled request with each id kind gets `-32800` and its handler token cancels; a cancel for an unknown id is ignored; a response with an unknown id is a protocol diagnostic; the stream path and the single-message path give equal bytes for the same request.

## 4. Session lifecycle and model transport (mep-protocol)

- [x] 4.1 Write the pure session state machine (design SES-1) with the lifecycle methods from the pinned `method` type. Verify by replay of `lifecycle-violations.md` and `protocol-errors.md`: `-32014` before initialization and after shutdown, `-32013` for a capability that was not advertised, a second `initialize` that fails and leaves the session initialized, and `exit` that ends the session.
- [x] 4.2 Write protocol version negotiation: offer and accept only `0.2.0-draft.1`, by exact match. Verify the legacy-only-host scenario (`-32011`, then a successful `initialize`) and that `0.1` is never offered.
- [x] 4.3 Write tuple negotiation: host order kept, each limit at the bilateral minimum, a host refusal of a tuple or limit it did not offer, and an empty agreement that makes the operation unavailable (`extension.tuple_unavailable`). Verify with the session part of `session-model-transport.md`, an Ion-only offer and a guest that selects a tuple it was not offered.
- [x] 4.4 Write envelope creation and admission (design MT-1): canonical padded base64, exact decoded length, lowercase SHA-256, deterministic `revision`, and admission checks in the spec order with depth and node limits and critical paths. Verify every case of `model-transport.cases.json`, each bound on its own, and that no IR reader runs for a refused envelope.
- [x] 4.5 Write the host session and the guest loop on a transport (design SES-2), and a test guest built only from `Morphir.Mep`. Verify a full in-memory session: describe, initialize, initialized, ping, info, capabilities, shutdown and exit, with the bytes checked against the transcripts.
- [x] 4.6 Add an AOT probe app under `tests/` that runs the full in-memory session, and add its publish to the AOT checks. Verify zero IL warnings with no suppression, the same messages as under JIT, and record the size added to the `morphir` binary in the PRD.
- [x] 4.7 Document `Morphir.Mep` for contributors: a README in the project (layers, how to add a method, the pin and how to update it) and a link from the PRD. Verify that the README's example compiles as a test.

## 5. Analyzer contract and the built-in type validator (model-analyzers, extension-registry)

- [x] 5.1 Add the analyzer contract to `Morphir.Extensions` (design ANA-1): `AnalyzerDiagnostic` over `Morphir.Mep.Diagnostic`, `IModelAnalyzer`, and an F# runner that selects by claim and configuration, runs in id order and turns an exception into a "failed" outcome. Verify with fake analyzers: additive merge, stable order, a turned-off analyzer, a failing analyzer beside a working one, and no analyzer for the model's family.
- [x] 5.2 Add `ValidatorClaim` (a non-empty set of `FormatVersion`) and the `Validator` capability case; the entry type `validator` comes from the claim. Verify the registry unit tests, including that a validator entry with no IR version cannot be built.
- [x] 5.3 Add `ExtensionEntry.ToMep()` to `Morphir.Mep.ExtensionInfo`, with only contract claim kinds. Verify that each built-in entry writes contract-valid `ExtensionInfo` JSON (checked with the contract test of 2.4), that an entry with no description has no `description` member, and that a validator entry has `validator` in `types` and no validator claim.
- [x] 5.4 Create `src/Morphir.Analyzers` (design ANA-2) with the `morphir.type-validator` analyzer over `TypeValidator`, its entry (classic versions `1`, `2`, `3`) and the list of built-in analyzers for C#. Verify that the existing `TypeValidator` test cases give the same diagnostics through the analyzer, with severity, code `type-mismatch`, message and location mapped as the design says.
- [x] 5.5 Register the built-in analyzers in both hosts' registries. Verify the `extension-registry` scenarios: `morphir.type-validator` in `extension list --json` and in the table in both hosts, the validator claim, and both hosts equal apart from `host`.
- [ ] 5.6 Run the interop reviewer on the F# surface of groups 2 to 5 that C# calls, and fix each finding. Verify that the reviewer reports no finding.

Task 5.6 status (feat/mep-analyzers): the reviewer reports no finding for the group 5 and 6 surface (`Morphir.Extensions`, `Morphir.Analyzers` and their C# callers). Open: the first-pass findings inside `src/Morphir.Mep/Wire.fs` and `Framing.fs` (option fields without `OrNull` members, `Method.TryFind` returning an option, `MethodKind.ToString`, the public constructors of `ModelTransportLimits` and `ProgressParams`, `PeerKind.Parse`, `RequestId.Match`, `WireValue` on `ProgressKind`, `ModelCodec` and `ModelLayout`, and doc comments on union cases), `DistributionModel.Match` in `Morphir.Models`, and the rule 1 settings of `Morphir.Models.fsproj` and `Morphir.Configuration.fsproj`. C# reads the MEP `Diagnostic` and `ExtensionInfo` through `MepRecords` in `Morphir.Extensions` until `Morphir.Mep` adds the members.

## 6. morphir validate (model-analyzers)

- [x] 6.1 Add `Features/Validate` to `Morphir.Tooling` and the `validate` command in both hosts, with `--input`, `--input-format` and `--json`, reusing the generate model loader. Verify with tests: a classic model with a type mismatch gives exit 1 and one `type-mismatch` error; a clean model gives exit 0; a missing input gives exit 2 and runs no analyzer; a V4 model gives one `info` diagnostic and exit 0.
- [x] 6.2 Read `extensions."<id>".enabled` from the layered configuration of the invocation directory. Verify that `enabled = false` in a project configuration turns off `morphir.type-validator`, and that the report lists it as not run.
- [x] 6.3 Write the human output as a TUI table with a summary line, and the `--json` report from a source-generated context. Verify: `validate --json | jq .exitCode` works, stdout has only the report, logs are on stderr, the table is plain when stdout is redirected, and the model file and working directory are unchanged after a run.
- [x] 6.4 Add E2E scenarios (`tests/Morphir.E2E.Tests`) that run `validate` in the published AOT `morphir` and in `dotnet-morphir`. Verify that both reports are equal apart from the host, and that the AOT closure check still passes.
- [x] 6.5 Write `docs/content/docs/cli/validate.md` (synopsis, options, output, exit codes, configuration, the difference from `morphir ir verify`, external analyzers coming in step 2a) and add it to the CLI index and `extension list` documentation. Verify that every documented command runs against the test fixtures.

## 7. Upstream validate payload and plan updates

- [x] 7.1 Write the validate payload proposal (design UP-1) as a document in `docs/content/contributing/design/` with the Smithy shapes, a transcript and model-transport cases for `validateModel`. Verify that the transcript messages round-trip through the codecs of group 2 extended in a test-only copy, and that the proposal adds no member outside the validator operation.
- [x] 7.2 After maintainer approval, open the draft pull request in `finos/morphir` and record its URL and head commit in the PRD. Verify that the PRD's "Upstream validate payload" row links the pull request and names the commit.
- [ ] 7.3 Update the PRD status table: MEP core, in-process validator contract and upstream payload rows, with links to this change and its evidence. Verify that each row names its completion evidence.

The proposal document of task 7.1 is written: [`docs/content/contributing/design/proposals/mep-validate-model.md`](../../../docs/content/contributing/design/proposals/mep-validate-model.md), with the Smithy shapes, two transcripts and the model-transport cases model-0036 to model-0040. Its JSON blocks parse, the transcript carries case model-0036 unchanged, and the case digests agree with the fixture bytes. `ProposalTests` round-trips every message of both transcripts through a test-only copy of the group 2 codecs extended with the proposed items, and checks that `validator` is the only member name outside the pinned contract.

## 8. Integration check

- [ ] 8.1 Run `./build.sh --target Test`, `dotnet format --verify-no-changes`, the AOT publish of `morphir` and the E2E suite. Verify that all pass, and that the `morphir` binary size change is recorded in the PRD.

## Workflow follow-up

- Follow AGENTS.md for review, approval and landing through a pull request.
- Archive the change after it lands, and sync the `mep-protocol`, `model-analyzers` and `extension-registry` specs.
- Steps 2a and 2b start from the pinned upstream draft of 7.2.
