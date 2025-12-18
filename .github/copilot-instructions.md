# GitHub Copilot Instructions for morphir-dotnet

This project uses **AGENTS.md** as the primary guidance file for all AI coding agents.

## Primary Guidance

**Start here**: [AGENTS.md](../AGENTS.md)

AGENTS.md contains comprehensive guidance covering:
- Project overview and architecture (Section 1)
- Agent scope and responsibilities (Section 2)
- Build, test, and deployment procedures (Section 4)
- Coding conventions and standards (Section 5)
- Morphir-specific modeling (Section 6)
- Testing strategy and TDD practices (Section 9)
- Decision policies and escalation rules (Section 10)
- Specialized guidance links (Section 18)

## Specialized Topics

For domain-specific guidance, see [.agents/](./.agents/) directory:

### QA Testing
**File**: [.agents/qa-testing.md](./.agents/qa-testing.md)

When working on testing tasks, refer to this guide for:
- Pre-commit and PR verification checklists
- Test plan templates
- Bug report templates
- Regression testing playbooks
- BDD and unit testing standards
- Test coverage requirements (>= 80%)

## Quick Reference

### Before Committing
```bash
./build.sh Format    # Format code
./build.sh Lint      # Run linter
./build.sh Test      # Run all tests
```

### Testing Requirements
- **Follow TDD**: RED → GREEN → REFACTOR (AGENTS.md Section 9.1)
- **Write tests first**: BDD scenarios, then unit tests, then implementation
- **Maintain coverage**: >= 80% code coverage required
- **Framework**: TUnit for unit tests, Reqnroll for BDD tests

### Build Commands
```bash
./build.sh                    # Build project
./build.sh DevWorkflow        # Full CI simulation (Restore → Lint → Compile → Test)
./build.sh PackAll            # Build all packages
./build.sh PublishLocalAll    # Publish to local NuGet feed
```

## Coding Principles

From AGENTS.md Section 5:
- **Domain is pure**: Side effects only in adapters
- **Immutability first**: Prefer `readonly record` and immutable collections
- **No nulls**: Use `Option<T>` or nullable reference types
- **ADTs**: Make illegal states unrepresentable
- **Exhaustive matching**: Update all pattern matches when changing ADTs

## CLI Logging Standard

**CRITICAL**: CLI tools must never write log messages to stdout.
- All logging → stderr (using Serilog)
- Stdout → command output only (JSON, formatted results)
- Test with: `./morphir ir verify file.json --json | jq .`

See AGENTS.md Section 5 for details and rationale.

## Escalation Rules

Do NOT implement without human approval (AGENTS.md Section 2):
- Breaking public API changes
- IR/JSON compatibility changes without ADR
- Security/auth/crypto changes
- Destructive migrations

## Project Tech Stack

- **Language**: C# 14, .NET 10 (F# for ADT-heavy components)
- **Testing**: TUnit (unit), Reqnroll (BDD)
- **Build**: Nuke build system
- **Messaging**: WolverineFx
- **CLI**: System.CommandLine
- **Logging**: Serilog (stderr only)

## File Structure

```
src/
├── Morphir/              # CLI/host application
├── Morphir.Core/         # Core domain model
├── Morphir.Tooling/      # Tooling services
└── Morphir.Tool/         # Dotnet tool package

tests/
├── Morphir.Core.Tests/
├── Morphir.Tooling.Tests/
│   └── Features/         # BDD feature files
└── Morphir.E2E.Tests/

build/
├── Build.cs              # Main build entry point
├── Build.Packaging.cs    # Package targets
├── Build.Publishing.cs   # Publish targets
├── Build.Testing.cs      # Test targets
└── Build.CI.cs           # CI simulation targets
```

## Resources

- **AGENTS.md**: [../AGENTS.md](../AGENTS.md) - Comprehensive guidance
- **QA Testing**: [../.agents/qa-testing.md](../.agents/qa-testing.md) - Testing practices
- **Test Plan Example**: [../docs/content/contributing/qa/phase-1-test-plan.md](../docs/content/contributing/qa/phase-1-test-plan.md)
- **Morphir Homepage**: https://morphir.finos.org/
- **AGENTS.md Standard**: https://agents.md

---

**Note**: This file provides Copilot-specific pointers. For complete guidance, always refer to AGENTS.md and specialized topic guides in `.agents/`.
