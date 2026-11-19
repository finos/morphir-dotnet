# The `PlatformSpecific` marks after the change (task 10.4)

Date: 2026-11-12. `main` at `9c9695ea` (pull requests 1 to 5 merged), with pull request 6. The count comes from
`evidence/count-marks.py`. It counts the marks of `build/Helpers/TestPlan.cs` (`MentionsPlatformMarker`) in the test
projects other than `Morphir.BlackBox.Tests`. The black-box project has no mark on a test: its assembly has the
category.

| | Marks | Files | Projects |
| --- | ---: | ---: | ---: |
| Base, `main` at `4d9e10a0` (before the change) | 695 | 286 | 30 |
| After pull request 1 | 721 | | |
| After pull requests 1 to 6 | 709 | 294 | 29 |

## By project

| Project | Base | After | Why it changed |
| --- | ---: | ---: | --- |
| `Morphir.Tooling.Tests` | 161 | 143 | The CLI features and CLI tests moved to the black-box project (pull requests 1 and 3). |
| `Morphir.TestSupport.Tests` | 13 | 36 | The tests of the new drivers, and the packaging helpers that moved from `Morphir.E2E.Tests` (pull requests 1 to 5). They start processes or use the file system of the machine. |
| `Morphir.Extensions.Installation.Tests` | 23 | 36 | Not this change: #608 (Windows setup) added tests to `main`. |
| `Morphir.Toolchain.Tests` | 100 | 104 | Mostly not this change: #608 and a morphirup fix added tests to `main`. Pull request 5 also changed the project. |
| `Morphir.Compilation.Mep.Tests` | 1 | 3 | Not this change: #608 added tests to `main`. |
| `Morphir.Managed.Tests` | 10 | 7 | The host tests moved to the black-box project (pull requests 2 and 3). |
| `Morphir.Tests` | 10 | 9 | The host tests moved to the black-box project (pull requests 2 and 3). |
| `Morphir.Build.Tests` | 57 | 56 | The package tests moved to the packages lane (pull request 4). |
| `Morphir.Myriad.Integration.Tests` | 1 | 0 | Project removed (pull request 4). |
| `Morphir.Myriad.SystemTextJson.Tests` | 4 | 3 | Pull request 4. |
| `Morphir.CodeGeneration.FSharp.Extension.Tests` | 13 | 12 | Pull request 4. |
| `Morphir.Frontend.FSharp.Extension.Tests` | 9 | 8 | Pull request 4. |
| `Morphir.SDK.Tests` | 3 | 2 | Pull request 4. |
| The other 17 projects | | | No change. |

The class of each mark is in the output of `python3 openspec/changes/consolidate-black-box-tests/evidence/count-marks.py`.
A mark stays on a unit test class that starts a process, uses the file system of the machine, or depends on the
operating system. The guard of `TestPlan` gives such a class the platform scope.
