# Fixed report controls

The four inputs are copied without edits from finos/morphir commit
`d5890d00f4d7839a5edfbcca100ffaaf45281508`, merged by PR #1159.
`manifest.json` records their SHA-256 digests. The corpus bytes also equal
the existing runtime corpus pin; adopting the report rule does not move that pin.

`ReportContractTests.fs` constructs synthetic metadata inputs from this data.
The 22 expected results come from the independent parent controls, not host runs.
The shared contract-check decision is used by the runner's `report-check` path.
Separate report tests preserve observation recomputation and exact byte rebuilding.
These fixtures contain no evidence of a qualifying executable, guest invocation,
platform, release artifact or complete generic-kit run.
