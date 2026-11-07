# Ecosystem deviations

Baseline: `finos/morphir@cde7722eb35987042a49dc646a846d693b0bd7f6`.
These entries explain the planned compatibility choices. They are not claims
that the new frontend or guest has passed qualification.

| Area | Recorded difference and decision | Evidence |
| --- | --- | --- |
| Compile streaming | The pinned Rust CLI implementation writes one compact report for `--json-lines`. The .NET plan emits events followed by a completed report. Keep this visible until an upstream agreement resolves it. | [STRM-2 and follow-up](../openspec/changes/add-fsharp-compile/design.html#strm-2), [planned upstream follow-ups](../openspec/changes/add-fsharp-compile/tasks.md#workflow-follow-up) |
| MEP guest qualification | The pinned generic guest kit requires legacy 0.1 and both frontend and backend capabilities. The planned F# guest offers only the current model-tier frontend contract. It will not add legacy protocol or a backend to satisfy the older kit. | [MEP-3](../openspec/changes/add-fsharp-compile/design.html#mep-3), [pinned contract provenance](../tests/Fixtures/Mep/PROVENANCE.md) |

Upstream issue links and qualification reports are added when those follow-ups
run. This seed does not claim an upstream guest-kit qualification or a released
kit supersession.
