# Slice 2: the runs of `manual-e2e-test.yml` (task 6.5)

Date: 2026-11-12. Each run is `manual-e2e-test.yml` with `configuration=Release` and
`platforms=linux-x64,osx-arm64`. The branch runs are of commit `35ac8ba6` (the first push of this pull request). The
`main` runs are of `3d1c94ab` with the workflow and the project of `main` (the old project), which has only the types
`trimmed` and `offerings`; they are the "before" of these two types on the same runners. The counts are the sum of
the four group runs of `TestE2E` for a branch job, and the summary of the old run for a `main` job.

The old runs include 48 tests that do not test the target: the 41 tests of the helpers in `InfrastructureTests`
(39 pass, 2 skip), the 8 log filter tests and the sample check of the corpus (`evidence/slice-2-moves.md`). Those
run once in `Morphir.TestSupport.Tests` now. At commit `35ac8ba6` the sample check was still in the integration
group, so the branch counts below include it.

| Type | Run | linux-x64: passed / failed / skipped | osx-arm64: passed / failed / skipped |
| --- | --- | --- | --- |
| `trimmed`, before (`main`) | [43287779045](https://github.com/finos/morphir-dotnet/actions/runs/43287779045) | 118 / 4 / 4 | 118 / 4 / 4 |
| `trimmed` | [43287722115](https://github.com/finos/morphir-dotnet/actions/runs/43287722115) | 116 / 0 / 21 | 116 / 0 / 21 |
| `untrimmed` | [43287733423](https://github.com/finos/morphir-dotnet/actions/runs/43287733423) | 116 / 0 / 21 | 116 / 0 / 21 |
| `aot` | [43287709959](https://github.com/finos/morphir-dotnet/actions/runs/43287709959) | 111 / 1 / 25 | 112 / 0 / 25 |
| `aot`, again at `20459432` (linux-x64 only; the sample check is in `Morphir.TestSupport.Tests` there) | [43294820527](https://github.com/finos/morphir-dotnet/actions/runs/43294820527) | 110 / 1 / 25 | |
| `tool` (the development build with the tool host) | [43287744989](https://github.com/finos/morphir-dotnet/actions/runs/43287744989) | 121 / 0 / 16 | 121 / 0 / 16 |
| `offerings`, before (`main`) | [43287790176](https://github.com/finos/morphir-dotnet/actions/runs/43287790176) | 511 / 19 / 22 | 511 / 19 / 22 |
| `offerings` (the four offerings with no host setting) | [43287756028](https://github.com/finos/morphir-dotnet/actions/runs/43287756028) | 505 / 3 / 40 | 505 / 3 / 40 |
| `executable` (the trimmed executable at a path, native host) | [43287767723](https://github.com/finos/morphir-dotnet/actions/runs/43287767723) | 115 / 0 / 22 | 115 / 0 / 22 |

## The comparison with before

| Type | Before: product tests passed (passed less 47 helper tests for each run) | After: passed |
| --- | ---: | ---: |
| `trimmed` | 71 on each platform | 116 on each platform |
| `offerings` (four runs) | 323 on each platform | 505 on each platform |

For the types that `main` cannot run in this workflow (`aot`, `untrimmed`, `tool`, `executable`), the "before" is the
local run of the old project on macOS arm64 (`evidence/slice-2-moves.md`): 65, 71, 71 and 70 product tests passed.
The runs above pass 111 to 121 tests on each of them. So no type passes fewer tests than before.

The new tests that each target runs, and that the old project did not run on it: the scenarios that pull request 1
moved to the black-box project (generation, model verification, the management commands, the journey), the smoke
scenarios of each host, and the end-to-end tests of the published lane.

## Failures and skips

- The four failures of each old run are the `mck wasm-host` scenarios (exit code 127; removed, see
  `evidence/slice-2-moves.md`). The old `offerings` run has them four times, and the 3 failures of native-aot below.
- `offerings`, 3 failures on each platform: "The installed layout is what the offering claims", "No installed .NET
  runtime" and "The declared runtime" on native-aot. The old run fails the same three on both platforms: the helper
  `ExecutableStructure` finds the bundle signature in the Native AOT `morphir` of Linux and macOS (it is verified on PE
  files only). A finding about the helper, not about this change.
- `aot` on linux-x64, 1 failure: "A guest that writes 20 MiB for each call keeps standard output to frames and its
  report bounded". The peak working set of the flood session was 143,863,808 bytes against 58,228,736 for an echo
  session, more than the 64 MiB bound; the second run gave 147,927,040 against 58,171,392 bytes. So the failure is
  stable on this target. The packaged native-aot offering passed the same scenario on linux-x64, and the old project
  never ran on `aot` in a workflow (the old workflow has no type `aot`), so no earlier run shows this. On macOS the
  platform reports no working set, so the check does not apply there. It is an open point for the owner: a property of
  the product on this target (the bound of the guest output, design of add-wasm-extension-host), not a change of the
  check, which is the same as before.
- Skips: a smoke scenario with `@host-managed` or `@host-tool` on a target with one host (2); a test of the published
  lane for another target, or a scenario that compares two hosts on a target with one host (by capability); the three
  scenarios of a copy with no engine library on a Native AOT executable (6, the reason of the old test); the Windows
  Ctrl+Break test; and the opt-in local candidate journey.

## Not run

- Windows: `manual-e2e-test.yml` was run on linux-x64 and osx-arm64 only. The Windows Ctrl+Break test runs only on
  Windows; the pull request CI runs the integration group on Windows on the development build.
- `manual-aot-test.yml` and `deployment.yml` were not run: their steps call the same `TestE2E` with the targets
  above (`trimmed`, `untrimmed`, `aot`, `native-aot`).
