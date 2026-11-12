# Reviewed MEP operation-observation donor

This tranche follows `5ef955b08f59ea2c6476a850bfe2b5286263da97` without a main
merge or history rewrite. Independent review `fbe46d84` cleared the frozen
security, correctness and F#/C# scope with no material finding. The review
verified nine live paths, eight source copies, five closure artifacts, 252
protected inputs and 117 unique passing cases. Historical report evidence is
unchanged.

Internal conformance calls now return `ConformanceOperationResult<T, Error>`
with `Result`, `Observation` and a read-only `Notifications` collection. The
observation exposes the admitted `Operation`, actual `RequestId`, defensive
`RequestBody` and `ResponseBody` copies, `Origin`, and `SubmissionCompleted`.
Origins distinguish submission, local answers and incomplete observations with
typed causes. Completion establishes framing or adapter return only; it never
proves an inner guest invocation. False does not prove absence of I/O.

The control supplies observed describe, initialize, request, shutdown,
after-shutdown probe, unknown notification and exit calls. Its fixed
`RequestUnknownMethodAsync` is initialized-only and sends only
`morphir.mck.unknown`. `OriginalDescribeObservation` reads the original receipt's
winning exchange. Duplicate same-ID replies and later describes cannot replace
that view. Initialize and initialized share one admission but have separate
observations. Public product methods use the same private cores and retain their
constructors, defaults and guards. No arbitrary raw send, caller recorder,
production reflection, public API or report contract is added or relaxed.

The initial build reached missing API declarations. Honest unavailable bridges
then reached an actual adapter assertion RED, with the expected request bytes
versus an unavailable body. After implementation, the final fresh Release build
and 14 exact selections passed 117 cases: 11 new projections, all 58 existing
conformance cases and all 48 ordinary cases, with zero skips, failures or errors.
The original test-file prefixes remain byte-identical. The admitted existing
67,108,865-byte adapter control ran; negative C# compile subprocesses, default
whole-assembly runs, other projects, Native AOT and real guests were excluded.

Pinned Fantomas 5.2.4 formatted and checked `ConformanceControl.fs`. Its checks
of four transport files are **not verified** because its parser cannot read
existing modern F# syntax/nullness. No formatter install, nullness downgrade or
unrelated formatting was performed. The post-format build and all 117 controls
passed. The build retained four known dependency TFM warnings without suppression.

Publication stages Git's prescribed CRLF-to-LF conversion on four closing-list
line terminators in the appended tests. Exact staged-byte and logical-line
comparison proves that tokens, indentation and literal contents are unchanged;
no parser, compiler or runtime rerun is claimed for this normalization. The
reviewed working files and original evidence remain intact.

Evidence is retained under `.dev/out/wasm-resume/operation-observations/` and is
not committed wholesale:

- Review manifest: `2A1F76BBC160F02CFEBC229E7D6DB528A8BFFA3643134A30E4D29BA1E0197B1D`.
- Source manifest: `70FB5E29CE793B25DD098722B71F8750B7D8278F1BA6EA787839F5AE252C4A8C`.
- Closure manifest: `4DBF850E700EC4B57F4AD7D12EBFAE065A2B3CE4930830E3D90EB053CFED914A`.
- Implementation and test copy: `7C031E67749181991A339C6EA1F1BEB9CF6C33780821B9E168B5D00EB3BA011B`.
- Test DLL: `EC1DEDC4139A74B10E687B02B1762B1DFEEB930A470251F5637AE2DF4582DFFF`.
- Release: `56117A522DD55EBF9D041AD8DB0906DD5B7CBC4552D3279D7E8B808318806251`.

B was released at `2026-11-12T04:36:35.5790523Z` after 122.88 seconds of cumulative
captured commands, with owned and informational global inventories empty. Normal
hooks were inspected; no bypass was used. Publication follows the coordinator's
explicit bounded-campaign sequencing exception. Full suite, coverage, full
formatting and current standalone changelog validation are not claimed passed.
Exact-head CI remains a separate gate.

This is a tested, reviewed source donor. Consumer integration requires its own
normal merge and validation allocation. It establishes no full generic 21-case
run, Python guest result, inner-WASM invocation, notification delivery, process
exit/EOF beyond the channel, platform, Native AOT or artifact qualification.
