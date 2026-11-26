# AGENTS.md

Primary entry point for all AI coding agents (including Claude Code, Codex, GitHub Copilot, Cursor, Windsurf, and others) contributing to `finos/morphir-dotnet`.

Produce correct, minimal, well-tested changes aligned with Morphir IR and tooling. Keep context lean by loading specialized guides only when needed.

## Progressive Disclosure

Read the relevant guide before editing or reviewing files in that area:

| Task / Area | Read |
|---|---|
| Any code change or code review | [CODING_STANDARDS.md](./CODING_STANDARDS.md) |
| F# (`*.fs`, `*.fsi`, `*.fsx`, `*.fsproj`) | [.agents/FSHARP_CODING_STANDARDS.md](./.agents/FSHARP_CODING_STANDARDS.md) |
| C# (`*.cs`, `*.csproj`) | [.agents/CSHARP_CODING_STANDARDS.md](./.agents/CSHARP_CODING_STANDARDS.md) |
| F# code called by C# (or C# calling F#) | [.agents/skills/fsharp-csharp-interop/SKILL.md](./.agents/skills/fsharp-csharp-interop/SKILL.md) + `fsharp-csharp-interop-reviewer` |
| Adding, moving, renaming, or deleting tests | [.agents/skills/test-manager/SKILL.md](./.agents/skills/test-manager/SKILL.md) + `test-manager-reviewer` |
| New or reshaped UI | [.agents/skills/frontend-design/SKILL.md](./.agents/skills/frontend-design/SKILL.md) |
| Native AOT, trimming, binary size | [.agents/aot-optimization.md](./.agents/aot-optimization.md) |
| Test plans, QA playbooks | [.agents/qa-testing.md](./.agents/qa-testing.md) |
| Full skills catalog & cross-agent matrix | [.agents/skills-reference.md](./.agents/skills-reference.md), [.agents/capabilities-matrix.md](./.agents/capabilities-matrix.md) |

## Repository Overview & Stack

- **Purpose**: .NET libraries, IR codecs, frontends/backends, analyzers, and CLI tooling for the [Morphir](https://morphir.finos.org/) ecosystem ([finos/morphir](https://github.com/finos/morphir), [finos/morphir-elm](https://github.com/finos/morphir-elm)).
- **Layout**:
  - `src/`: F# domain/IR/compiler/diagram libraries (`Morphir.Models`, `Morphir.Frontend.FSharp`, `Morphir.Lang.CSharp`, `Morphir.Conformance`, `Morphir.Toolchain`, etc.) and C# CLI/tooling/Roslyn projects (`Morphir`, `Morphir.Tooling`, `Morphir.Core`, `Morphir.Lang.CSharp.*`).
  - `tests/`: Unit/property/snapshot test projects alongside `tests/Morphir.BlackBox.Tests` (Gherkin black-box tests), `tests/Morphir.TestSupport` (drivers/sandbox/fixtures), and `tests/Morphir.Build.Tests`.
  - `build/`: Fallout build system (`./build.sh` / `./build.cmd`).
  - `conformance/`: Compatibility kit suites and expectations.
  - `docs/`: Specifications, schemas, ADRs, and user/contributor docs.
  - `scripts/`: Repo maintenance and CI helper scripts.
- **Stack**: .NET 11 SDK (pinned in `global.json`), C# 15, F#, `Foundatio.Mediator`, `System.CommandLine`, Serilog, TUnit / Expecto / Verify / Reqnroll.
- **Target Frameworks**: Use the MSBuild properties in `Directory.Build.props` (`$(MorphirCurrentTargetFramework)` = `net11.0`, `$(MorphirLibraryTargetFrameworks)` = `net11.0;net10.0`, `$(MorphirHostedTargetFramework)` = `net10.0`) — never literal TFMs. Packable libraries compile for `net10.0` too; guard any `.NET 11`-only APIs.
- **Packages**: Centrally managed in `Directory.Packages.props`. Minimize and justify new dependencies.

## Essential Commands

```bash
# First-time setup
dotnet tool restore && dotnet restore && dotnet husky install

# Build, format, and test
./build.sh                                          # Default Fallout build
dotnet build                                        # Solution build
./build.sh --target Test                            # Run all test suites (MTP / TUnit / Expecto / Reqnroll)
dotnet run --project tests/<Project>/<Project>.csproj # Run a single MTP test project
dotnet format                                       # Format C#/F#
./build.sh --target Precommit                       # Pre-commit format/lint check
```

## Non-Negotiable Rules

### Scope, Safety, and User Confirmation
Ask for explicit user or maintainer confirmation before any of the following:
- Breaking public API changes.
- IR/JSON wire or schema compatibility breaks (require an ADR and version bump).
- Security, authentication, or cryptographic changes.
- Destructive or external actions: **force-pushing**, **publishing releases**, or modifying production systems.

Never commit secrets or violate FINOS license/CLA policies.

### Testing and TDD
- Follow **TDD (Red–Green–Refactor)**: write a failing test first, implement minimally to pass, then refactor while keeping tests green.
- Use the **`test-manager`** skill ([.agents/skills/test-manager/SKILL.md](./.agents/skills/test-manager/SKILL.md)) before adding, moving, renaming, or deleting any test, feature file, step, driver, or test project, and run `test-manager-reviewer` on the diff.
- Keep tests parallel-safe and hermetic with `Morphir.TestSupport` (`ChildProcess.RunAsync`, `TestSandbox.Apply`, `TolerantDirectory`, `FixtureCatalog`, object mothers). Never call `Environment.SetEnvironmentVariable` or `Directory.SetCurrentDirectory` in tests, and never touch the real `~/.morphir` or XDG folders.

### CLI Logging
- CLI tools **must never write logs to `stdout`**. `stdout` is strictly for command output (JSON, formatted results); all logging and diagnostics go to `stderr`. See `GEN-7` in [CODING_STANDARDS.md](./CODING_STANDARDS.md).

### Commit Messages
- Use **Conventional Commits** (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`).
- **CRITICAL (CLA compliance)**: **Do NOT include `Co-Authored-By: Claude` or any AI assistant as a co-author on commits.** AI assistants cannot sign the FINOS CLA and will fail CI/block PR merges. (Attribution in the commit body is allowed; GitHub Copilot may be listed only when it is an actual co-author.)
- If an AI co-author trailer was added by mistake, fix it with:
  ```bash
  dotnet fsi scripts/remove-claude-coauthor.fsx --commits 5 --yes
  ```

## Project Skills and Reviewer Agents

- **Canonical skills** live in `.agents/skills/<name>/SKILL.md` (read directly by Codex, GitHub Copilot, Cursor, Antigravity, and Muse Code).
- **Skill mirrors**: Claude Code reads `.claude/skills/` and Grok Build reads `.grok/skills/`. Never edit mirrored copies directly — edit `.agents/skills/<name>/`, then run:
  ```bash
  dotnet fsi scripts/sync-agent-skills.fsx
  ```
  `--check` runs in CI and the Husky pre-commit hook. (OpenSpec manages its own `openspec-*` skills; the sync script ignores them.)
- **Reviewer agents** live in `.claude/agents/*.md` (Claude Code & Cursor), `.codex/agents/*.toml`, `.github/agents/*.agent.md`, and `.grok/agents/*.md`. For tools without agent files, run a subagent using the skill's "Review mode" prompt.
- **External .NET skills** ([dotnet/skills](https://github.com/dotnet/skills)): enabled in `.claude/settings.json`; install locally with `dotnet fsi scripts/setup-dotnet-skills.fsx`.

## OpenSpec

New changes use the shared `html-design` schema (`design.html` with inline CSS and SVG) and the generated `openspec-*` skills for exploration, proposals, implementation, updates, spec sync, and archiving. Repository tracking, contribution, and approval rules still apply.

Keep project rules in `openspec/config.yaml`. Do not edit the managed schema, templates, or notices. Run `mise run openspec:verify` before submitting changes. See [docs/openspec.md](./docs/openspec.md).
