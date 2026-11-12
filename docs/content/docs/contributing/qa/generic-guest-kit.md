# Developing the generic guest kit runner

The adopted profile uses one process and one MEP `0.2.0-draft.1` session. The .NET implementation lives in `Morphir.Conformance.GuestKit`; the development command adapter lives in `Morphir.Mck`. The product CLI forwards `mck` arguments to a separate executable.

The frozen kit remains `guest-generic-json.json`, SHA-256 `f16f2a4c577b507527a77a19a383c2c0083b4db1db131cd06d170e65a88598ae`, with contractVersion `0.1.0-draft.1`. That data-contract version is distinct from the negotiated MEP version. Its original provenance and historical Python 0.4.2 expected IR remain unchanged. The four adoption inputs have separate provenance at approved parent commit `9e7d90e2fb5708580baef7cdd323cd994030c237`.

## Selection and the session

Describe offers only MEP `0.2.0-draft.1`. Selection requires a language supported by both frontend and backend, an IR version claimed by both, and a complete JSON `compileModel`/`generateModel` tuple pair for that version. Legacy `compile`/`generate` flags do not participate. IR 3 precedes IR 4; unusable versions are excluded from both version projections. A legacy-only guest or a claim without any usable pair fails describe.

The original successful describe operation issues the receipt used to bind the copied selected offer once. Binding sends no message and consumes no request id. Initialize is request 2 in the same session. No second describe process, reopened worker or new launcher is used.

The complete two-version plan is:

| Case | Method | Request id |
| --- | --- | --- |
| describe | morphir.extension.describe | 1 |
| model-initialize | morphir.initialize | 2 |
| model-initialized-silent | morphir.initialized | notification |
| unknown-notification-silent | morphir.mck.unknown | notification |
| compile-model-vthree | morphir.frontend.compileModel | 3 |
| generate-model-vthree | morphir.backend.generateModel | 4 |
| roundtrip-model-vthree | morphir.frontend.compileModel | 5 |
| generate-model-independent-vthree | morphir.backend.generateModel | 6 |
| roundtrip-model-independent-vthree | morphir.frontend.compileModel | 7 |
| compile-model-vfour | morphir.frontend.compileModel | 8 |
| generate-model-vfour | morphir.backend.generateModel | 9 |
| roundtrip-model-vfour | morphir.frontend.compileModel | 10 |
| generate-model-independent-vfour | morphir.backend.generateModel | 11 |
| roundtrip-model-independent-vfour | morphir.frontend.compileModel | 12 |
| bad-digest | morphir.backend.generateModel | 13 |
| malformed-source | morphir.frontend.compileModel | 14 |
| unknown-method | morphir.mck.unknown | 15 |
| describe-immutable | morphir.extension.describe | 16 |
| model-shutdown | morphir.shutdown | 17 |
| after-shutdown | morphir.ping | 18 |
| model-exit | morphir.exit | notification |

With one selected IR version, retain its five-case block and all other entries, giving 16 cases and request ids 1 through 13. Bad digest and malformed source use the first selected version. Independent generation starts from the fixed expected IR, followed by another compile round trip. No expected IR comes from a compiler or runner result.

## Observation boundaries

The internal MEP conformance controller returns typed results alongside immutable operation observations. The runner reads the winning original describe receipt and each operation's actual request/reply bodies, admitted operation identity, correlated request id, origin and `SubmissionCompleted`. It retains those observations internally. It does not choose replies by searching a snapshot, infer the last id, or reconstruct received claims from typed values. Typed admission must still succeed; a raw JSON result cannot invent a missing receipt.

`Submitted` with completed framing or adapter return proves submission at that boundary. `AnsweredLocally` is a local refusal. `Incomplete` retains the reason observation could not finish. Prepared bytes or a false `SubmissionCompleted` flag do not prove that no I/O occurred. Counts record known completed submissions.

Initialize owns a distinct `initialized` notification observation. The unknown request uses the fixed request API, separate from the fixed unknown notification. The post-shutdown probe bypasses only the explicit conformance lifecycle boundary and requires the actual correlated peer error `-32014`; a local refusal cannot pass. The unknown request requires exactly peer error `-32601` and bad digest requires `-32602`.

Parameterless fixture messages use explicit `params: {}` as their canonical sender representation. That fixture choice does not change product defaults or introduce a protocol requirement. Preserve actual body bytes rather than rewriting observations to match an expected packet.

Required notification silence cannot pass from a worker write, adapter return or policy withholding. Withheld, unknown or otherwise unestablished delivery to the actual guest remains `NotRun` and prevents qualification. Controlled peers and fake workers prove the outer runner path only.

WorkerSessions reuses the existing WindowsChild owner on Windows. The exit case requires the actual settled process status and the session reader's stdout EOF. An exit callback or successfully submitted exit notification is insufficient. Cancellation and timeouts settle the owned tree before returning. Stderr capture is bounded to 65,536 characters per line, 1,048,576 characters total and 4,096 retained lines; capture failure remains explicit.

## Fixed comparisons and negative controls

IR comparison permits exactly three equivalences:

1. JSON object member order is ignored.
2. In IR 3, the order of the name-keyed `modules`, `types` and `values` lists is ignored.
3. In IR 4, at `outputType` or an `inputTypes` member, a `Reference` with only `fqname` and absent or empty `args` equals the corresponding FQName string.

Other array order, changed values and extra reference members remain meaningful. Bool-to-Unit mutations must fail. Payload validation still checks version, codec, digest, length and negotiated encoded/decoded/depth/node limits. Successful compilation permits warning/info diagnostics but no error diagnostic. Malformed source must have `success: false`, an error diagnostic and no model member, including no null model. Generated files are checked by compiling them again against fixed expected IR.

Independent fixed transcript tables and payload literals live in GuestKitPlanFixture. Worker scripts compare each actual frame with those fixed packets before replying. Mutation controls cover lost raw JSON members, an undecodable describe, a changed second describe, wrong unknown-method or post-shutdown error codes, malformed diagnostics/model presence, dirty process exit and cancellation. Baseline and adoption provenance groups remain separate.

## Focused development checks

Use the pinned SDK and run bounded sequential checks with two processors and the shared process gate. After a successful current Release build:

```powershell
dotnet exec tests/Morphir.Conformance.Tests/bin/Release/net10.0/Morphir.Conformance.Tests.dll `
  --filter-test-list 'Generic guest kit' --sequenced --summary
dotnet exec tests/Morphir.Conformance.Tests/bin/Release/net10.0/Morphir.Conformance.Tests.dll `
  --filter-test-list 'GuestKit worker sessions' --sequenced --summary
```

Comparison, descendant settlement and crafted report/command controls have separate named lists. Keep setup/compiler failures distinct from behavioral RED and retain actual nonzero test counts. Bind results to current source, locked graph inputs and emitted implementation dependencies, including when unchanged test IL keeps an older timestamp. Fantomas 5.2.4 cannot parse some existing modern F# nullness and shorthand syntax; its failures are not successful formatting checks.

The generic report contract and checker are still pending parent authority. `ModelTierRun` and its diagnostic text are transitional, not a qualification report. No real published Python or MoonBit qualification follows from the controlled tests. Four E2E migrations, explicit offering-script runner input and generic CLI/report integration remain open. See the [development command reference](../../cli/morphir-mck.md) and [tracked change](../../../../../openspec/changes/adopt-generic-guest-kit/tasks.md).
