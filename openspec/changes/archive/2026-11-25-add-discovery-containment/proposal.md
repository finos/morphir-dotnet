# Proposal

## Why

Morphir discovery can't be reliably contained. The global home is discovered as a project, a native Windows `MORPHIR_HOME` breaks every `config` command, and the boundary and isolation controls exist only on some commands and only as flags. So tests, CI jobs and child processes can't keep Morphir away from the real machine. Several tests already depend on whatever is in the developer's home directory, or write to it. This needs to be fixed before the 0.4.0-alpha.1 release, because that release is the first whose packages can actually be installed.

## What Changes

- Add the `MORPHIR_DISCOVERY_BOUNDARY` and `MORPHIR_ISOLATED` environment variables, which mirror `--discovery-boundary` and `--isolated`. A command-line value wins over the environment variable, and the environment variable wins over the default: the start path's filesystem root, with no isolation. `--isolated` accepts an optional `true`/`false` value, so a command can turn off an isolation set in the environment.
- Reserve both names, so `MORPHIR_*` environment mapping never turns them into configuration values.
- Give every discovering command the controls that apply to it:
  - `config get/list/show/path/explain/set/unset` get both.
  - `project migrate` gets the boundary.
  - `workspace migrate` checks that its directory lies inside the boundary.
  - The migration commands already exclude machine and personal sources, so isolation does not change them.
- Fix discovery so the global Morphir home never declares a project or workspace. Today `~/.morphir/morphir.toml` loads as both a `Global` source and a `RootPrimary` for any directory under the home folder.
- Treat native absolute `MORPHIR_HOME` paths (Windows drive and UNC paths, with either separator) as absolute. Today `C:\Users\me\.morphir` is joined onto the current directory.
- **BREAKING (Morphir.Tooling API):** Delete the legacy `Morphir.Tooling.Configuration.WorkspaceDiscovery` and `ConfigResolver`, and retire the "Legacy configuration entry points remain compatible" requirement.
  - The legacy discovery walks to the drive root and treats any `.git` or `.morphir` directory as a workspace, which contradicts `morphir-project-system`.
  - No release that could be restored ever exposed these types.
  - The document-aware resolver covers CI overlays through profiles.
  - `OsPaths` stays, because `ArtifactPathResolver` uses it.
- Make the test suites hermetic. Add a shared sandbox for child-process tests: a fake home and machine directories, a boundary, isolation, processes killed and awaited on timeout, and tolerant cleanup. Fix the isolation and cleanup problems listed in the design. This has no effect on product behavior.

## Capabilities

### New Capabilities
- `discovery-containment`: host-level containment controls for every discovering command. This covers the boundary and isolation sources (flag, environment, default), their precedence and validation, and which commands honor which control.

### Modified Capabilities
- `morphir-project-system`: the global Morphir home must not declare a project or workspace, and native absolute `MORPHIR_HOME` paths count as absolute.
- `configuration-resolution`: the reserved operational variables include `MORPHIR_DISCOVERY_BOUNDARY` and `MORPHIR_ISOLATED`, and the legacy cache-only entry-point compatibility requirement is removed.

## Tracking

- GitHub issue: #494 (Discovery containment controls and test isolation)
- Related:
  - #490, #491 and #492 (wiring the `dist`, `tool` and `extension` commands): they must use the boundary-aware discovery rather than the deleted legacy types.
  - #489 (0.4.0-alpha.1 release fixes).

## Impact

- **Code:**
  - `src/Morphir.Configuration`: environment mapping reserved names, discovery skipping the global home.
  - `src/Morphir.Configuration.IO`: `MachineLocations` path handling.
  - `src/Morphir.Tooling`: the configuration, project-migration and workspace-migration commands and hosts; removal of the legacy `Configuration/WorkspaceDiscovery.cs` and `Configuration/ConfigResolver.cs`, and of the `WorkspaceDiscovery` registration in `ManagementScenario`.
- **Public API:** `Morphir.Tooling.Configuration.WorkspaceDiscovery` and `ConfigResolver` are removed. The CLI gains two environment variables and an optional value for `--isolated`. `workspace migrate` gains `--discovery-boundary`.
- **Tests:**
  - `tests/Morphir.Tooling.Tests`: the legacy discovery and resolver tests are removed; the configuration, editing, migration, CLI and management step classes use the shared sandbox.
  - `tests/Morphir.E2E.Tests`: per-scenario state.
  - `tests/Morphir.Build.Tests`: process timeouts.
  - `tests/Morphir.Myriad.Integration.Tests`, `tests/Morphir.CodeGeneration.FSharp.Tests` and `tests/Morphir.Myriad.Tests`: processes awaited after kill, tolerant cleanup.
- **Docs:** the CLI configuration documentation and the CHANGELOG.
