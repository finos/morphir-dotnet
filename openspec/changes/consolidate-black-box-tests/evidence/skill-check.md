# Check of the `test-manager` skill (task 5.5)

Date: 2026-11-12. Each check ran in a new agent with no other context. The agent got a clean worktree of the
branch, read only the three files of the skill, and did not edit a file.

## Five classification cases

| Case | Expected | Result of check 1 | Result of check 2 |
| --- | --- | --- | --- |
| A. A new test starts the CLI for one command (`ir verify`) and checks the exit code and the text | integration | integration, step 8, `Integration/`, Gherkin, `@integration` | integration, step 8, `Integration/`, Gherkin, `@integration @needs-model-verification` |
| B. A new test of a pure function | unit | unit, step 4, the test project of the library, framework test, no marker | The same |
| C. A test that follows a tutorial through three commands | journey | journey, step 6, `Journeys/`, Gherkin, `@journey` | journey, step 6, `Journeys/`, Gherkin, `@journey @needs-generation` |
| D. A journey that must also run on the Native AOT executable | journey, with a target | journey, step 6, `@journey @lane @published`; the agent said that two lanes fit | journey, step 6, `@journey @lane @published`, and not `@lane @native-aot` |
| E. A corpus loop against an executable | A framework test in the black-box project | integration, step 8 with the form of step 3: a TUnit class in `Integration/` that derives from `BlackBoxTest`, `[Category("integration")]` | The same, with a category for each capability |

The five classes are correct in both checks.

## Places that were not clear

Check 1 found eight places in which the skill gave no answer or two answers. Each one was corrected, and check 2
ran on the corrected skill:

| Place | Correction |
| --- | --- |
| The capability tags were only in another document | The skill has the four tags with the meaning of each |
| No rule for the first scenario of a capability with no driver interface | Rule 10: the scenario has a step class that gives the arguments to `ICliDriver`; the interface comes with the second scenario |
| The Native AOT executable of a release fits two lanes | Rule 5 has a table: `@lane @native-aot` is only for a test that makes a Native AOT publish itself |
| The hosts had no definition | Rule 6 names the project and the executable of each host, and says that the form is the target |
| "Capability" had no definition | A definition under the classification steps |
| "One basic function" had no limit | A definition of smoke, and a hard case in `classification.md` |
| Two texts for the moment at which a group gets a suite | Both say "its first test with no lane" |
| "TUnit or Expecto" for a framework test | The black-box project uses TUnit; a unit test uses the framework of its project |

Check 2 found seven smaller places. Five were corrected after it, with no third check: the capability tags of a
capability that has no driver yet, the definition of a capability that gets its interface later, the form of a unit
test in a project with feature files, the wording of step 7, and a category on the class or on the method. One is a
gap that stays until pull request 3: compile and configuration have no driver and no capability tag yet. One is
answered by a new sentence: a test that needs something that only one target has states it as a capability.

## The reviewer agent

Diff: one new file in a unit test project, `tests/Morphir.Core.Tests/CliVersionProcessTests.cs`. Its test finds
`src/Morphir/bin/Release/net11.0/morphir.dll` from the repository root, starts it with `System.Diagnostics.Process`
and `--version`, and checks the exit code and the output. It uses no product type. The file was not committed.

The agent followed `.claude/agents/test-manager-reviewer.md` and reported:

```text
tests/Morphir.Core.Tests/CliVersionProcessTests.cs:9: rule 1: CONFIRMED. `TheCliPrintsItsVersion` starts the built
`morphir.dll` with `--version` and uses no product type, so its class is smoke (steps 4 and 7), but it is in a unit
test project. Delete the file: `tests/Morphir.BlackBox.Tests/Smoke/Version.feature` (default host and `@host-native`
scenarios) already runs `--version` on `src/Morphir` with a stricter check. If you want an added check, add a
`@smoke` scenario there with the steps that exist.

tests/Morphir.Core.Tests/CliVersionProcessTests.cs:13: rule 1: PLAUSIBLE. The test reads
`src/Morphir/bin/Release/net11.0/morphir.dll` from a fixed path, but `Morphir.Core.Tests.csproj` references only
`src/Morphir.Core`, so the artifact is absent or stale in a Debug build or when the project runs alone. Get the
artifact through the CLI driver of the black-box project, not through a path in the test.

tests/Morphir.Core.Tests/CliVersionProcessTests.cs:8: rule 11: CONFIRMED. A test in a unit test project uses a
process and the real file system and has no `[Category("PlatformSpecific")]`. The move to the black-box project
removes the need. If the test stays, add the marker and use `ChildProcess` of `tests/Morphir.TestSupport`.
```

The report has the misplaced test with its file, its line, its rule and its correction.

One change came from this check: the first reviewer instruction read only `git diff`, which does not show a file
that is not tracked. The instruction of the four agent files now also says to read each untracked file of
`git status --short`.
