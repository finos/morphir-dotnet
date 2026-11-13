# Slice 1: runs and times (task 4.5)

Date: 2026-11-12. Machine: macOS arm64, 18 processors, .NET SDK 11.0.100, Release. `Test` runs 4 test processes at
the same time on this machine. Each run started after `./build.sh --target Compile`.

## The runs

| Command | Result |
| --- | --- |
| `./build.sh --target Test --skip Restore Compile` | Passes. 38 of 38 suites. 246 s to 247 s (two runs). |
| `./build.sh --target Test --skip Restore Compile --test-scope platform` | Passes. 32 of 38 suites; the three groups of the black-box project are selected, with their group filter. 243 s. |
| `./build.sh --target Test --skip Restore Compile --test-group integration` | Passes. 1 of 38 suites: only `Morphir.BlackBox.Tests (integration)`. No package-consumer check. 14 s for the command, 13 s for the suite. |
| `./build.sh --target Test --skip Restore Compile --test-group smoke`, and `journey` | Pass. 1 s for each suite. |

## The groups and `Morphir.Tooling.Tests`

In the complete `Test` run, with the default limit of this machine (9 for the run, 2 for each test process):

| Suite | Tests | Time |
| --- | ---: | ---: |
| `Morphir.BlackBox.Tests (integration)` | 60 | 13 s |
| `Morphir.BlackBox.Tests (smoke)` | 4 | 1 s |
| `Morphir.BlackBox.Tests (journey)` | 1 | 1 s |
| `Morphir.Tooling.Tests`, before the CLI driver (`dotnet run`), 1,287 tests | | 136 s |
| `Morphir.Tooling.Tests`, on the CLI driver and after the move, 1,243 tests | | 76 s |
| `Test` target, before the CLI driver | | 257 s |
| `Test` target, after | | 247 s |

`Morphir.Tooling.Tests` alone on the machine (limit 9): 40 s before, 33 s on the driver, 30 s after the move. The
summed time of its tests: 1,151 s before, 750 s on the driver, 642 s after the move
(`evidence/tooling-baseline.md`).

The longest suite of the `Test` run is `Morphir.Frontend.FSharp.Tests` (150 s), not `Morphir.Tooling.Tests`.

## The fixed share of the process limit

The complete `Test` run, after the change, with three limits for the run:

| `MORPHIR_TEST_MAX_PROCESSES` | Share of a test process | `Test` | `Morphir.Frontend.FSharp.Tests` | `Morphir.Tooling.Tests` | Black-box integration |
| ---: | ---: | ---: | ---: | ---: | ---: |
| not set (9) | 2 | 247 s | 150 s | 76 s | 13 s |
| 18 | 4 | 237 s | 115 s | 47 s | 9 s |
| 36 | 9: the limit that each test process had before this change | 224 s | 89 s | 34 s | 7 s |

So on this machine the fixed share with the default limit costs 23 s (10 %) of the `Test` run. A developer with
many processors can set a larger limit.

The two slowest suites alone on the machine, with a low limit:

| Suite | Limit 1 | Limit 2 |
| --- | ---: | ---: |
| `Morphir.Tooling.Tests` | 112 s | 58 s |
| `Morphir.Frontend.FSharp.Tests` | 200 s | 124 s |

A CI runner has 3 or 4 processors and runs 2 test processes at the same time. With no value, the limit of the run
is 2 and the share is 1; before this change each test process had 2. So `development.yml` sets
`MORPHIR_TEST_MAX_PROCESSES: 4` for the test jobs, which gives each test process 2, as before. The CI times of the
pull request show the result.

## A solution build after the tests

The check: `./build.sh --target Compile`, then tests, then `./build.sh --target Compile` again, with no commit
between them (the commit is in the version of each assembly, so a commit makes each project compile again). The
count is the assemblies below `obj/` that were written after the first compile.

| Tests between the two compiles | Written by the tests | Written by the second compile |
| --- | ---: | ---: |
| None | | 0 |
| The three groups of the black-box project (`--test-group`), `Morphir.TestSupport.Tests`, `Morphir.Tooling.Tests` | 0 | 0 |
| The Myriad package-consumer scripts (`TestMyriadPackages`) | 0 | 0 |
| `Morphir.Myriad.Tests`; `Morphir.Myriad.Integration.Tests` | 0 | 0 |
| `Morphir.Myriad.SystemTextJson.Tests` alone | 0 | **48** |
| `Morphir.Mep.Tests` alone | 0 | **48** |
| The complete `Test` target | 0 | **48** |

The black-box project, the drivers and `CliTestHelper` on the driver replace no assembly of the solution build: the
compile after them builds no project.

**A defect that this change does not make and does not repair:** after `Morphir.Myriad.SystemTextJson.Tests` or
`Morphir.Mep.Tests`, the next compile builds 48 projects again, from `Morphir.Myriad.SystemTextJson.Annotations` on.
This branch has no change in those two test projects or in the Myriad and MEP sources (`git diff 4d9e10a0` is empty
for them). Pull request 4 moves the fixture builds of those projects to `IPackageDriver`, and task 8.2 has the test
that a solution build after the driver compiles nothing.
