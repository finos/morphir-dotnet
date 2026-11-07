# Ecosystem deviations

Baseline: `finos/morphir@cde7722eb35987042a49dc646a846d693b0bd7f6`.
These entries explain the planned compatibility choices. They are not claims
that the new frontend or guest has passed qualification.

| Area | Recorded difference and decision | Evidence |
| --- | --- | --- |
| Compile streaming | The pinned Rust CLI implementation writes one compact report for `--json-lines`. The .NET plan emits events followed by a completed report. Keep this visible until an upstream agreement resolves it. | [STRM-2 and follow-up](../openspec/changes/add-fsharp-compile/design.html#strm-2), [planned upstream follow-ups](../openspec/changes/add-fsharp-compile/tasks.md#workflow-follow-up) |
| MEP guest qualification | The pinned generic guest kit requires legacy 0.1 and both frontend and backend capabilities. The planned F# guest offers only the current model-tier frontend contract. It will not add legacy protocol or a backend to satisfy the older kit. | [MEP-3](../openspec/changes/add-fsharp-compile/design.html#mep-3), [pinned contract provenance](../tests/Fixtures/Mep/PROVENANCE.md) |
| IR v4 kit canonical spellings | Two canonical JSON statements in the pinned IR v4 kit disagree with other statements in the same kit, and the reader gives each pair one model. `definitions-0020` writes an annotation with no arguments as `{ "name": … }`, while `definitions-0021` and `definitions-0022` write the same kind of annotation as a compact string. `distributions-0004` spells the module path as `u-s/f-r-2052-a/data-tables`, while `names-0001` and `names-0008` keep the same kind of name as `US` and `FR-2052-a`. The .NET writer uses the compact annotation string and the `CanonicalNames` spelling. Its tests keep both cases listed, and they fail if the kit is corrected. | [`V4JsonWriterTests.fs`](../tests/Morphir.Models.Tests/Json/V4/V4JsonWriterTests.fs), [pinned kit](../tests/Fixtures/MorphirIon/kit/) |

Upstream issue links and qualification reports are added when those follow-ups
run. This seed does not claim an upstream guest-kit qualification or a released
kit supersession.
