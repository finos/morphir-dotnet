# Tasks

Tracking: #494. Follow [the design](design.html) and the [discovery-containment](specs/discovery-containment/spec.md), [morphir-project-system](specs/morphir-project-system/spec.md) and [configuration-resolution](specs/configuration-resolution/spec.md) deltas.

Within every behavior task:
1. Write a failing test first and record that it fails.
2. Implement the minimum change.
3. Refactor with the tests green.

Add BDD scenarios before implementing new user-visible CLI behavior. Group numbers describe dependencies.

## 1. Shared test support

Follow TEST-1 to TEST-4 and TEST-6. Later groups' tests run inside this sandbox.

- [ ] 1.1 Add the non-packable `tests/Morphir.TestSupport` project and a `tests/Morphir.TestSupport.Tests` project to `Morphir.slnx`. Verify `./build.ps1 --target Compile` succeeds, and that `Morphir.TestSupport` is absent from `LibraryPackages` and from `artifacts/packages` after `PackAll`.
- [ ] 1.2 Add `TolerantDirectory.Delete` (retry with backoff, then log, never throw) and move the `LocalInstallationTests` helper onto it. Verify with a test that holds a file open in the directory, releases it during the retries, and confirms the delete succeeds without throwing; and a test where the lock never releases, which logs instead of throwing.
- [ ] 1.3 Add `ChildProcess`: concurrent stdout and stderr capture; a timeout that starts after the concurrency gate is acquired; and, on timeout or cancellation, `Kill(entireProcessTree: true)` followed by a bounded wait for exit and both readers before throwing with the captured output. Verify with tests:
  - A child that outlives the timeout has exited by the time the call returns, and its working directory can then be deleted.
  - A child writing more than 1 MB to both streams does not deadlock.
  - With `MORPHIR_TEST_MAX_PROCESSES=1`, two concurrent children run one after the other.
- [ ] 1.4 Add `TestSandbox`: it creates the TEST-4 directories, applies the environment to a `ProcessStartInfo`, and deletes itself tolerantly. Verify with a test that a `dotnet` child sees the sandbox `HOME`, `USERPROFILE`, `APPDATA`, `XDG_CONFIG_HOME`, `MORPHIR_HOME`, `MORPHIR_DISCOVERY_BOUNDARY` and `MORPHIR_ISOLATED`, sees no inherited `MORPHIR_*` variable, and keeps the real `NUGET_PACKAGES`. Verify the opt-out leaves `MORPHIR_ISOLATED` unset.
- [ ] 1.5 Add the machine-location tripwire (TEST-6) as an assembly hook. Verify with a test that writing a file under a fake snapshot root during the run fails the check and names that path, and that an unchanged root passes.
- [ ] 1.6 Document the sandbox, the concurrency gate, `MORPHIR_TEST_MAX_PROCESSES` and the tripwire in `docs/content/docs/contributing/` (testing guidance) and AGENTS.md's testing section. Verify that the documented usage snippet compiles in `Morphir.TestSupport.Tests`.

## 2. Containment controls for configuration commands

Depends on group 1. Follow CTL-1 to CTL-4.

- [ ] 2.1 Add reserved names: write a failing `Morphir.Configuration.Tests` case in which `MORPHIR_DISCOVERY_BOUNDARY` and `MORPHIR_ISOLATED` produce `discovery_boundary` and `isolated` keys, then add both to `EnvironmentMapping.reserved`. Verify that the "Containment controls are not configuration" scenario passes.
- [ ] 2.2 Add the containment resolver: write failing unit tests for every precedence, empty-value, relative-path, Windows native path and invalid-value case in the boundary and isolation requirements, then implement `Containment.Resolve`. Verify that each spec scenario has a passing test, including source attribution.
- [ ] 2.3 Add BDD scenarios to `tests/Morphir.Tooling.Tests/Features/Configuration` for the "Environment boundary applies", "Flag overrides the environment", "Environment boundary excludes the current directory", "Environment isolates", "Flag turns isolation off", "Bare flag isolates", "Unrecognized isolation value" and "Help ignores invalid variables" scenarios. Run them in the sandbox and record that they fail.
- [ ] 2.4 Give `--isolated` an optional value (CTL-2). Route the `config get/list/show/path/explain` commands and the inspection host through the resolver, with validation in the action (CTL-3). Make `config path --json` report the effective controls and their sources. Verify that the 2.3 scenarios pass and that existing configuration scenarios are unchanged.
- [ ] 2.5 Route `config set/unset` and the editing host through the resolver. Add and pass the BDD scenarios "Edits stay inside an environment boundary" and "Edits honor environment isolation".
- [ ] 2.6 Document `MORPHIR_DISCOVERY_BOUNDARY`, `MORPHIR_ISOLATED`, the precedence rules and `--isolated [true|false]` in the CLI configuration docs under `docs/content/docs/`, and add CHANGELOG `Added` entries. Verify that the documented commands produce the documented output in a sandbox.

## 3. Containment for migration commands

Depends on group 2.

- [ ] 3.1 Add BDD scenarios for "Workspace directory outside the environment boundary", "Project migration stays inside the environment boundary" and "Migration ignores isolation off" under `tests/Morphir.Tooling.Tests/Features/ProjectMigration` and `WorkspaceMigration`. Run them and record that they fail.
- [ ] 3.2 Route `project migrate` and its host through the resolver's boundary. Add `--discovery-boundary` to `workspace migrate`, and require its directory to lie inside the effective boundary before planning. Verify that the 3.1 scenarios pass, that dry runs write nothing, and that existing migration scenarios are unchanged.
- [ ] 3.3 Update the migration command docs and help text for the boundary control. Verify that `workspace migrate --help` lists `--discovery-boundary` and that `--help` performs no configuration I/O.

## 4. Discovery fixes for the Morphir home

Depends on group 1, and can run in parallel with groups 2 and 3. Follow DISC-1 and DISC-2.

- [ ] 4.1 Write failing `Morphir.Configuration.IO.Tests` cases for "Windows native Morphir home" and "Relative Morphir home". Normalize `MORPHIR_HOME` before the absolute-path test in `MachineLocations`. Verify that both pass and that the existing XDG and split-global scenarios still pass.
- [ ] 4.2 Write failing in-memory discovery tests for "Project below the home directory", "Relocated Morphir home" and "Real project .morphir layout still works". Extend the discovery request with the optional Morphir home and skip primaries located directly in it. Verify that all three pass and that callers passing no home behave as before.
- [ ] 4.3 Pass the Morphir home, computed from the environment snapshot even when isolated, from every native host. Verify with a sandbox CLI scenario that reproduces #494 problem 1: `config path --json` from `<sandbox>/home/work/proj` lists `home/.morphir/morphir.toml` only as `Global` and never as `RootPrimary`. Verify with another that a backslash `MORPHIR_HOME` exits 0 on Windows.
- [ ] 4.4 Add CHANGELOG `Fixed` entries for both discovery bugs. Document how the Morphir home relates to project discovery in the configuration docs. Verify that the docs example matches the 4.3 scenario's output.

## 5. Remove the legacy discovery and resolver

Depends on groups 2 to 4, so the containment behavior is covered by the new paths first. Follow LEG-1.

- [ ] 5.1 Delete `src/Morphir.Tooling/Configuration/WorkspaceDiscovery.cs`, `ConfigResolver.cs` and `TomlParser.cs`, their tests (`WorkspaceDiscoveryTests`, `ConfigResolverTests`, `TomlParserTests`), and the `WorkspaceDiscovery` registration in `ManagementScenario`. Verify that the solution builds, `git grep` finds no remaining reference, and `OsPaths`/`ArtifactPathResolver` and their tests still pass.
- [ ] 5.2 Add a CHANGELOG `Removed` entry marked **BREAKING** for the `Morphir.Tooling` API, naming the replacement entry points from the spec's Migration note. Update any docs that mention the legacy resolver. Verify with `git grep -i "ConfigResolver\|WorkspaceDiscovery" docs src` that nothing stale remains.

## 6. Make the test suites hermetic

Depends on group 1, and on group 5 for the Tooling configuration tests. Follow TEST-2 to TEST-6. Each task must still pass its suite.

- [ ] 6.1 Move the `Morphir.Tooling.Tests` child-process steps onto `TestSandbox` and `ChildProcess`: `ConfigurationSteps`, `ConfigurationEditingSteps`, `ProjectMigrationSteps`, `WorkspaceMigrationSteps` (including its child test host with `--results-directory` in the sandbox), `CliTestHelper`/`CliVerifyIRIntegrationSteps` (timeouts added) and `GenerateCommandTests`. Verify that `dotnet run --project tests/Morphir.Tooling.Tests -c Release` passes three times in a row on Windows with no `being used by another process` error and no 30-second timeouts.
- [ ] 6.2 Fix the in-process isolation in `Morphir.Tooling.Tests`: give `ArtifactPathResolverTests` an isolated global root; clean up `CSharpGenerationSteps`, `CSharpCliGenerationTests`, `CSharpGenerationPublicationTests` and the `CSharpGenerationBackendTests` loaders; make the unconditional deletes in the generation tests tolerant. Verify that the tripwire passes for the assembly and that no `morphir-*` temp directory from the run remains afterwards.
- [ ] 6.3 Fix `Morphir.E2E.Tests`:
  - `SharedSteps` stores its result in `ScenarioContext`.
  - `ExecutableRunner` kills the process tree and awaits it.
  - Repo-root lookup uses `AppContext.BaseDirectory`.
  - The AOT publish steps publish into a sandbox, with a timeout, and run `[NotInParallel]`.

  Verify with `./build.ps1 --target TestE2E --executable-type trimmed`: 24 passed and 1 skipped as before, and no new files under `artifacts/test-builds`.
- [ ] 6.4 Fix `Morphir.Build.Tests`: `LocalInstallationTests` and `VersionSourceTests` use `ChildProcess`, with timeouts and concurrent stream reads. Verify that all build tests pass and that a hung child fails within its timeout, not indefinitely, using a test-only command that sleeps.
- [ ] 6.5 Fix the F# and Myriad process users, with `ChildProcess` or an equivalent kill-tree plus wait, tolerant cleanup and `-nodeReuse:false`/`MSBUILDDISABLENODEREUSE=1`: `CarRentalSteps` (and remove its `0.1.0-test.*` package from the global cache in `[AfterTestRun]`), `TranspilerTests.fs`, `Morphir.Myriad.Tests/Program.fs` and `src/Morphir.Testing/GenerationCompiler.fs`'s delete. Verify that each suite passes and leaves no leftover temp directory or test package.
- [ ] 6.6 Fix the `Morphir.Configuration.IO.Tests` child-process cases (`ConfigurationTransactionTests` when `SpinUntil` fails, `ConfigurationRecoveryProcessTests`) and `PackageMigrationTests.fs`'s evidence root. On every path the child must be killed and awaited, and the evidence root removed or deliberately kept. Verify that the suites pass and that a forced `SpinUntil` failure leaves no running child.

## 7. Integration verification

- [ ] 7.1 Run `./build.ps1 --target TestAll` locally on Windows with `~/.morphir` present, and in CI on all three platforms. Verify that it is green, that the tripwire passes in every process-heavy assembly, and that `WorkspaceDiscoveryTests`/`ConfigResolverTests` no longer exist.
- [ ] 7.2 Reproduce both #494 bugs in the original sandbox commands and confirm that they are fixed: no `RootPrimary` for the global home, and backslash `MORPHIR_HOME` exits 0. Run `openspec validate add-discovery-containment --strict` and verify that it passes.

## Workflow follow-up

- Open the PR linking #494. It needs explicit approval before merge, per `openspec/config.yaml`.
- Merge after #489 and before tagging `v0.4.0-alpha.1`.
- Archive the change after review, sync the main specs, and close #494.
