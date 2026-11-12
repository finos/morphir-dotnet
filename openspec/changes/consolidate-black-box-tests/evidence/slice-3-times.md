# Slice 3: runs and times (task 7.5)

Date: 2026-11-12. Machine: macOS arm64, 18 processors, .NET SDK 11.0.100, Release. Two other agents built and tested
on the same machine during these runs, so a time can be up to two times longer than on a machine with no other
load. The baseline is task 3.1 (`evidence/tooling-baseline.md`) and the run of task 4.5 (`evidence/slice-1-times.md`).

## The complete `Test` run

`./build.sh --target Test --skip Restore Compile` after `./build.sh --target Compile`: passes, 38 of 38 suites, 4 test
processes at the same time, 5:15 for the command. Each group of the black-box project reports `Skipped tests by
reason: lane 0, host 0, capability 0`.

| Suite | Tests | Pull request 1 (task 4.5) | This pull request |
| --- | ---: | ---: | ---: |
| `Morphir.Tooling.Tests` | 1,243 before the merge of `main`, 1,023 now | 76 s | 27 s |
| `Morphir.Tests` | 62, now 42 | | 3 s |
| `Morphir.Managed.Tests` | 14, now 9 | | 1 s |
| `Morphir.BlackBox.Tests (integration)` | 60, now 330 | 13 s | 117 s |
| `Morphir.BlackBox.Tests (journey)` | 1, now 3 | 1 s | 13 s |
| `Morphir.BlackBox.Tests (smoke)` | 4 | 1 s | 1 s |
| `Morphir.Frontend.FSharp.Tests` (the longest suite) | | 150 s | 226 s |

The integration group is now the second longest suite. It has the moved CLI tests, so the time moves with them: the
scenarios start the same commands as before, each in its own workspace, with 2 product processes at the same time
(the fixed share of design DRV-4).

## Each assembly alone

The complete assembly with a TRX report, `TMPDIR=/private/tmp/claude-501/p3 dotnet exec tests/<P>/bin/Release/net11.0/<P>.dll`
(the default process limit of the gate, 9), summarized with `evidence/trx-summary.py`:

| Assembly | Tests (passed / failed / skipped) | Time of the run | Summed time of the tests |
| --- | --- | ---: | ---: |
| `Morphir.Tooling.Tests`, task 3.1 (`dotnet run`) | 1,287 (1,283 / 0 / 4) | 40 s | 1,151 s |
| `Morphir.Tooling.Tests`, after pull request 1 | 1,243 (1,239 / 0 / 4) | 30 s | 642 s |
| `Morphir.Tooling.Tests`, before this pull request (base `c041f454`, with `main` of #608) | 1,255 (1,250 / 0 / 5) | 30 s | 681 s |
| `Morphir.Tooling.Tests`, after this pull request | 1,023 (1,018 / 0 / 5) | 33 s | 409 s |
| `Morphir.BlackBox.Tests`, after this pull request (all groups in one process) | 337 (337 / 0 / 0) | 34 s | 920 s |

The time of a run of `Morphir.Tooling.Tests` alone does not go down: its longest tests stay (the corpus runs of
`GenerationWireConformanceTests` and `CSharpCliGenerationTests`, `MsBuildProjectFileProviderTests`). The summed time
of its tests goes from 681 s to 409 s.
