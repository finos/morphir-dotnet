# Local Windows morphirup steel-thread evidence

This milestone implements one usable local developer journey, not the complete
acquisition/platform acceptance checklist. It composes the actual morphirup
manager, existing acquisition IO and installer ownership/validation primitives,
and the official self-contained managed Morphir folder. It adds no second
installer, checker, trust root or release authority.

## Demonstrated journey

One frozen F# source package is installed, selected and run from an empty Home.
The actual native manager compiles it to the fixed semantic IR and generates the
frozen 112-byte F# file. The same installed selection repeats after its source
archive/sidecar copies are withdrawn, with hostile PATH/runtime controls and
app-local runtime traces. All 320 installed files, receipt and selection bytes
remain unchanged. This was executed with AMD64 artifacts on Windows ARM64
emulation. Physical x64 and other platforms are not inferred.

The fixed authority is `tests/Fixtures/Toolchain/workflows/fsharp-compile-generate`.
Generated F# SHA-256 is
`64e08313f3e57be2c18995a2e8b275db9d921341958da2c8cd960c601c066de0`.
Both IR observations match the committed `ir-4.0.0.json` through standard JSON
structural equality. Expectations are not derived from the implementation.

## Retained candidate and provenance

| Item | Identity |
| --- | --- |
| Host archive | `bff0dbad0d70b15843237ab74f163c014e858ccc6e8aad8a7ec654176a7c1a6e`, 58,042,046 bytes |
| Raw candidate JSON | `89ee87439452be1a0afd294fc40af0cb2aea9d3f80b0bb15d922a964134ca037` |
| Installed folder | 320 files, 136,932,607 bytes, each path/length/hash checked |
| Host producer source | `eb7d164bccbb24e069087a61c0dd1ffffff326e5`, dirty producer state disclosed |
| Host full version | `0.4.0-alpha.4+eb7d164bccbb24e069087a61c0dd1ffffff326e5` |
| Pre-checkpoint corrected manager | `393b710f37066dff35443a6817034830f5d5cd190ac789310dd18fbd1f963aae`, 6,822,400 bytes |
| Pre-checkpoint manager source snapshot | `241c1441819c38ccdc1b07b047e0ae10db8e1d2d1e5f379c1dffc2d1da44b571` |

The table preserves the initial demonstrated bytes. A subsequent clean commit
and native build have their own producer head, exact native digest, build inputs
and execution records in the milestone PR and candidate bundle. They are not
silently substituted for this table. Local checksums identify explicit developer
bytes; they do not authenticate a publisher or qualify an acquired release.
No new product release version, tag or production publication is made.

## Validation and review

The pre-checkpoint source passed 87 manager/legacy controls with three platform
ignores, five supplied-candidate integrations, and 58 acquisition IO controls.
The nine launch controls are included in the 87 and also passed in Debug.
The current fixture DLL, JSON and satellites are copied from each configuration's
actual project output; all 33 files per configuration were compared. Existing
ordinary package versions remain unchanged; additional graph dependencies use
already centrally pinned versions.

Independent ownership/security/interop review found the original direct-child
lifetime defect. Creation-time Windows job assignment, non-inheritable
kill-on-close ownership, no breakaway, exact stdhandle inheritance, and awaited
zero job membership before lease release resolved it. Final disposal then gained
an explicit once-only owner which retains the primary exception and separately
records cleanup failure. The final independent recheck found no new actionable
ownership, containment, security or F#/C# interop defect within this slice.
The C# host facade uses the same job primitives through a BCL-shaped F# member.

Failure history is preserved rather than relabeled as passing. The first native
journey exposed a help-stream harness assumption. The next exposed refusal of
host-owned Home task locks after compile. The Home ownership split retains strict
setup admission and validates only owned partitions for later commands. The first
launch controls observed surviving descendants. The disposal RED exposed a
missing separate cleanup diagnostic; primary-exception replacement was not
reproduced. The first fixture-copy target failed with literal-wildcard MSB3030
before its corrected successful build. The actual PowerShell walkthrough caught
an unquoted `--` consumed by its wrapper, then passed with the literal quoted.
Raw logs, exact command vectors, typed UTC build/closure records and fixed outputs
remain in the supplied candidate evidence, not committed wholesale into git.

## Reproduction and remaining acceptance

The [user walkthrough](../../content/docs/getting-started/local-morphirup-candidate.md)
shows consumer commands, official producer targets and the opt-in actual-native
E2E test. Producer builds require the repository's pinned SDK and native compiler;
consumer installation and file-input compilation do not require system .NET.
The test refuses to claim runtime hiding on a machine it cannot isolate and
counts missing explicit inputs as ignored, not a milestone pass.

Full solution/coverage/all-format/changelog/Precommit qualification is not inferred
from focused commands. Authenticated acquisition, independently installed process
providers, exact portable-lock CI restore, recovery/rollback/update/remove/
self-update, project selection/proxy breadth, installer orphan recovery and
remaining offering/platform/AOT consumer qualification remain open. TUF is not
activated as production authority. WASM and the separate broker/POSIX work remain
outside this local managed milestone's critical path.