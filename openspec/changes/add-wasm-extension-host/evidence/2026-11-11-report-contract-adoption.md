# Current-main refresh and normative report adoption

This is local Windows ARM64 author evidence, not host qualification.

## Producer and authority

The reviewed MEP correlation regression was committed normally as
`885880695d7723c3ae0777575afc5d29a38d218f` after the coordinator accepted
independent recheck `5be5df2d` as terminal clear. Main was then merged normally
as `4f0d1ac7986696b5a4be8e7e6c6e73e78dbf5796`, with parents `88588069` and
`3da850d7b518d6cb9590d5cdb71f1a9c2f2b1a92`. No history was rewritten.
The four mechanical conflict resolutions retain both WASM and installation
registrations, Toolchain and Toolchain.IO in `LibraryPackages`, their pack flags,
and product forwarding of every `morphir mck` invocation without a Conformance
dependency. The retained runner sources remain development-entrypoint donors.

Parent finos/morphir#1159, commit
`d5890d00f4d7839a5edfbcca100ffaaf45281508`, defines task 1.4. Its report contract,
22 fixed controls, corpus and profile data are copied unchanged under
`tests/Fixtures/MepWasm/ReportContract/parent-d5890d00/`, with SHA-256 provenance.
The corpus equals the existing 59-case pin byte for byte. The runtime corpus pin,
modules, generated features and stable case ids are unchanged.

## Change and TDD evidence

Reports now start with the pinned `$schema`, contain the computed ordered verdict,
and use `guestKitCitations` with a profile per entry. The legacy `report`,
`reportVersion` and `qualificationNotice` members are removed. The shared checker
returns the eight named refusals in contract order. A foreign executable citation
contributes `guest_kit_host_mismatch` to the verdict rather than a checker problem.
The runner still recomputes outcomes from observations and requires its own exact
reconstructed bytes. The controls exercise the shared contract decision, not
synthetic observations being accepted as actual host execution.

Retained failures include:

- The schema-first assertion failed with expected `$schema`, actual `report`.
- The first control run had 3 passes and 21 failures. It found that the reader
  returned feature-file order instead of corpus order. A separate ordering
  assertion then failed before the validated reader was corrected.
- Command integration had 7 passes and 1 failure because the default Extism
  behavior wrote an empty declared-profile list. The command now records its
  Extism default explicitly and rejects explicit missing/duplicate profiles.
- Citation admission failed its new singleton assertion with expected operational
  exit 1, actual exit 0. The admission guard was implemented only after that RED.
- Fixture syntax, namespace/overload annotations and reserved-name compiler
  failures are preserved separately. They are setup failures, not behavioral RED.
- The first strict OpenSpec check reported two overlong requirement descriptions.
  Splitting them into requirements and scenarios made strict validation pass.
- Staged whitespace checking caught CRLF in the new local provenance manifest,
  whose fixture directory disables Git text conversion. Only that manifest's
  line endings were changed to LF, with parsed JSON equality checked. None of
  the four pinned upstream files changed. No runtime test was repeated for this
  formatting-only correction; its before copy and delta proof remain ignored.

No TypeScript checker, real guest, native engine experiment or runtime fallback
was used for these checks.

## Current focused results

One locked aggregate restore and one serial aggregate Release build covered
Build.Tests, Mep.Tests and Conformance.Tests after the main refresh. All lock files
and 240 tracked project/configuration inputs stayed unchanged across restore and
validation. The one owned test-project Compile addition was recorded as a source
change before that input baseline was frozen.

The refreshed build/MEP checkpoint passed:

| Selection | Passed | Skipped |
| --- | ---: | ---: |
| Four pure build-helper classes | 80 | 0 |
| Six MEP conformance lists | 58 | 0 |
| Seven ordinary MEP selections | 48 | 0 |

After the final report changes, one fresh Release Conformance.Tests build and
five exact lists passed:

| Selection | Passed | Skipped |
| --- | ---: | ---: |
| 22 fixed controls plus inventory and provenance | 24 | 0 |
| Runner report/reconstruction checks | 16 | 0 |
| Command boundary, including two unavailable-citation controls | 10 | 0 |
| Feature reader, including corpus order | 7 | 0 |
| Generated-file controls | 10 | 0 |

The independent inventory names all 22 controls and every rule element. Removing
a control fails that inventory; a test also removes one from a copy to check
the guard. No expectation was recorded from a host run. The ordinary admitted
over-limit adapter control used 67,108,865 bytes. No negative C# compile subprocess,
production routing project, full assembly or solution test was selected.

The final build had zero errors and four known dependency TFM warnings. No
suppression was added. Deterministic unchanged assemblies were allowed to retain
their timestamps only when bound to a successful current-source build and exact
dependency copies; no hash-inequality freshness rule was used.

## Frozen local evidence

Raw logs, ANSI-normalized derived counts, arguments, per-command creation
identities, source/input manifests and artifact closures remain under
`.dev/out/wasm-resume/report-contract-adoption/`. That ignored directory is not
committed wholesale.

- `step1-checkpoint.json` binds the pushed current-main checkpoint and 186 controls.
- `final-source.json`: SHA-256
  `C056F032ECBB4CDCEEB7AB73B4C07BF09C0DE0AC0CAB7C20589EC07808B92CD9`.
- `final-build.result.json`: SHA-256
  `6973E8C9A880CD050F3386AA75CA8D39D7665B3C2D2C5828DDEC8E5BA3C2831B`.
- Conformance implementation and test copy:
  `9AC3E1D7DCE8003D2D93640019FD97DAC334A1466E9B79D05BEB498910B79395`.
- Conformance test DLL:
  `865872BCC8C4B2510BF48D95BB863E5919BAAB082EF62F106251989AF54D9E7F`.
- `final-conformance-closure.json` binds those files, deps/runtimeconfig and typed
  timestamps to the successful final build. `final-conformance-0..4` preserve the
  final 67 names and raw results.
- `release.json`: SHA-256
  `E8C43DBB4C8790E3CE20CE96FFF9F4587D9E27CD1FC5DC4A7709521E9442EFC5`.

The bounded commands used pinned SDK 10.0.403, two CPUs, serial MSBuild, no shared
compiler/server/node reuse, hidden captured output and a 240-second tree bound.
Cleanup matched original process creation identities before killing or awaiting
owned processes. The coordinator-authorized other batch remained independent.
Cumulative captured command time was 220.14 seconds, below the 900-second limit.
At release both owned and global .NET/compiler/test inventories were empty.

## Remaining gates

Positive citation admission is deliberately unavailable until the single actual
generic-kit report checker is integrated. Complete citation-bearing `RunAsync`
calls return operational `mck.guest_kit_check_unavailable` before a worker or output
report. Both matching and foreign caller-supplied identities are covered. File
existence/hash and pass-shaped JSON are not positive guest proof. This is neither
an invented invalid-guest verdict nor a new normative reason. The citation model
and foreign-host policy remain for later integration; the feature is not deleted.

The maintained generic-kit owner still owns its actual 21-case runner, own report
check and development CLI migration. Its retained C# donor help text needs to
follow this report contract when that entrypoint is integrated. Product MCK
forwarding is unchanged. Required notification delivery and inner-WASM invocation
remain separate, unqualified evidence.

This normal checkpoint follows the coordinator's explicit bounded campaign
exception to full-suite/coverage/full-format-before-commit sequencing. Those
checks, standalone changelog validation and full formatting were not run. No hook
was disabled. Whitespace and OpenSpec strict validation pass. Root's independent
final review and exact-head CI remain landing gates. No AOT, acquired/released
artifact, platform/minimum-OS, packaging, CET/Spectre, OpenSSL, version, tag or
publication decision is changed, and task 6.1 remains open.

## Independent review correction, 12 November 2026

The first independent review found that public `report-check` could accept an
unchecked citation, although `RunAsync` already refused citation admission. It
also found that the deadline margin was written and read as a string instead of
the positive JSON number required by the pinned schema.

Three new regressions reproduced those gaps as three assertion failures, with
16 existing report tests passing and no test errors. They cover missing citation
files, existing pass-shaped files with matching supplied digests, and the numeric
deadline wire shape. Public report validation now refuses citation-bearing
reports until the one actual generic-kit checker is integrated. The shared
`checkContract` policy still evaluates synthetic positive and foreign-host
controls; those fixtures grant no runtime evidence authority. The deadline writer
uses a JSON number, and its reader refuses textual or non-positive values.

The final fresh Release build passed with zero errors and four existing upstream
support warnings. Final focused results were 24 contract controls, 19 report
checks, 10 command controls, seven reader controls and 10 generation controls,
all 70 passing with no ignored, failed or errored cases. The earlier 186 merged
build/MEP controls remain historical evidence; they were not rerun in this repair.

Pinned Fantomas 5.2.4 formatted only the two changed F# files and its final check
passed. The first formatter capture incorrectly applied an immutable-source
guard to the expected formatting edits. That instrumentation result is retained
separately; it was not a product or runtime failure. No fixture, protocol,
parent-schema or dependency input changed. All 121 frozen code/project entries
and 240 tracked project/lock/config inputs stayed fixed through the final runs.

The repair evidence is retained under `.dev/root-report-review-fix/`, including
separate raw streams, the three assertion REDs, final build and five test results,
five-artifact closure, formatter classification and release. CPU ownership was
released at 02:56:31Z with no remaining owned processes. Fresh independent
recheck and corrected-head CI are still required. Positive guest citation
integration, full 21-case and inner-guest qualification remain open.
