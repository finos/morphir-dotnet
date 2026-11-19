# The filter forms on the pinned TUnit (task 2.3)

Date: 2026-11-12. TUnit 1.81.10 on Microsoft.Testing.Platform, Reqnroll.TUnit 3.3.4, macOS arm64, Release.
The assembly has `[assembly: TUnit.Core.Category("PlatformSpecific")]`.

Tests in the assembly for this check: the three scenarios of `Smoke/Version.feature` (feature tag `@smoke`), and the
temporary fixtures of `evidence/hooks.md`: four scenarios in a feature with `@smoke` (one with `@lane @packages`,
one with two capability tags, two others) and five framework tests in a class with `[Category("smoke")]` (one with
the categories `lane` and `packages`). Total: 12 tests, of which 2 have a lane.

Command: `dotnet exec tests/Morphir.BlackBox.Tests/bin/Release/net11.0/Morphir.BlackBox.Tests.dll --treenode-filter "<filter>"`

| Filter | Selected | Result |
| --- | ---: | --- |
| `/*/*/*/*[Category=smoke]` | 12 | Each smoke test, also with the assembly-level `PlatformSpecific` category. The scenario with two capability tags is selected once. |
| `/*/*/*/*[(Category=smoke)&(Category!=lane)]` | 10 | The two tests with `@lane @packages` are not selected: the scenario and the framework test. |
| `/*/*/*/*[Category=packages]` | 2 | Only the two tests of the packages lane. In a run with no lane both are skipped: `lane: test packages, run none`. |
| `/*/*/*/*[Category=PlatformSpecific]` | 12 | Each test, from the assembly-level category. |
| `/*/*/*/*[Category=journey]` (no test has it) | 0 | `Zero tests ran`, exit code 8. |

## Decisions

- The engine accepts the exclusion form. A group suite uses `/*/*/*/*[(Category=<group>)&(Category!=lane)]`, so the
  tests of a lane are not in the report of a group run. The lane check of `TestEligibility.Decide` stays as the
  second barrier: a lane test that a run selects is skipped before its first step.
- A group suite uses its one filter in both scopes. The `PlatformSpecific` filter of the scope `platform` would
  select the same tests, because of the assembly-level category, and a process takes one filter.
- A group with no test fails its run (exit code 8). A run in which each selected test is skipped exits with 0.
