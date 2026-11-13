# CI times after the change (task 10.5)

Date: 2026-11-13. The baseline is the pull request run 43215049975 (before the change). The runs after the change
are on the head `6a513b3c` of pull request 6 (#627), with pull requests 1 to 5 on `main`.

- Pull request run: https://github.com/finos/morphir-dotnet/actions/runs/43331723151 (passed, 17 checks).
- Manual full run (`workflow_dispatch`, `os=full`): https://github.com/finos/morphir-dotnet/actions/runs/43331745026.

## Job times, in minutes

| Job | Baseline 43215049975 | Pull request run 43331723151 | Manual full run 43331745026 |
| --- | ---: | ---: | ---: |
| `test (ubuntu-latest)`, job | 24.9 | 35.2 | 22.7 |
| `test (ubuntu-latest)`, step "Run tests" | 15.0 | 17.4 | 11.1 |
| `test (ubuntu-latest)`, Native AOT step | 1.9 | 5.2 | 3.3 |
| `test (windows-latest)`, job | 32.0 | 27.9 | 36.3 (attempt 2) |
| `test (windows-latest)`, step "Run tests" | 20.6 | 15.3 | 20.4 (attempt 2) |
| `test (macOS-latest)`, job | 13.1 | 30.5 | 19.7 (attempt 2) |
| `test (macOS-latest)`, step "Run tests" | 8.5 | 17.4 | 11.8 (attempt 2) |
| The complete run | 33.4 | 37.8 | no single value (see below) |

## What the numbers show

- The complete pull request run is 4.4 minutes longer. The longest job sets it: Linux, then macOS.
- The Native AOT step of Linux is longer: `TestAot` publishes 8 programs with Native AOT (the old step published 3
  probes). It replaces the old targets, which this pull request removes.
- The step "Run tests" now also runs the black-box project in its four groups. The integration suite runs alone
  (`.Alone()`), because some of its scenarios have time budgets.
- The times of one runner vary much from run to run: the Linux job of the manual full run on the same commit took
  22.7 minutes, less than the baseline. One run of each kind does not measure a trend.

## Failures of the manual full run

- The first attempt failed on macOS in `Morphir.Extensions.Installation.Tests`:
  `NamedLocks.native async cancellation settles before returning and releases its open owner` gave
  `FileLeaseBusyException: Lease is busy: .../home/.morphir/cancel.lock`. This change does not touch that project
  (its test came with #608), so it is a race outside the change, recorded in #618. The first attempt then
  cancelled the Windows job.
- The failed jobs ran again (attempt 2) and passed: macOS, Windows and `ci`. The Linux job and its times come from
  attempt 1. Attempt 2 took 37.8 minutes, from the start of the macOS and Windows jobs to the end of `ci`. The run
  has no single complete time, because its jobs ran in two attempts.
