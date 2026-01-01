# GitHub Copilot Instructions for morphir-dotnet

> **Primary Guidance**: This project uses [AGENTS.md](../AGENTS.md) as the primary guidance file. See [.agents/](./.agents/) for specialized topics.

## Quick Start

**Always read first**: [AGENTS.md](../AGENTS.md) - Complete project guidance
**Specialized skills**: [.agents/skills-reference.md](./.agents/skills-reference.md) - QA Tester, AOT Guru, Release Manager, Technical Writer
**Cross-agent compatibility**: [.agents/capabilities-matrix.md](./.agents/capabilities-matrix.md)

**Purpose**: Morphir .NET provides .NET tooling and libraries for Morphir IR (Intermediate Representation).

**Links**: [Morphir Docs](https://morphir.finos.org/) | [morphir](https://github.com/finos/morphir) | [morphir-elm](https://github.com/finos/morphir-elm)

## Tech Stack & Structure

**Stack**: C# 14, F# (ADTs), .NET 10, TUnit/Reqnroll/FsCheck, WolverineFx, System.CommandLine, NUKE build

**Key Directories**:
- `src/Morphir/` - CLI host
- `src/Morphir.Core/` - Domain model
- `src/Morphir.Tooling/` - Features (vertical slices)
- `tests/` - TUnit, Reqnroll, E2E
- `docs/` - Content, specs, schemas
- `.agents/` - Specialized guidance (QA, AOT, Release)
- `.claude/skills/` - Interactive skills (Claude Code)

**Commands**:
```bash
dotnet build              # Build
dotnet test --nologo      # Run tests
dotnet format             # Format (required before commit)
./build.sh --help         # NUKE build targets
```

See [AGENTS.md Section 4](../AGENTS.md#4-build-run-test) for complete build instructions.

## Coding Standards (Summary)

**Core Principles** (See [AGENTS.md Section 5](../AGENTS.md#5-coding-conventions)):
- Immutability first, effects at edges
- ADTs: Make illegal states unrepresentable
- Exhaustive pattern matching, no nulls
- Value types for IDs/quantities

**C# 14 Style**:
- File-scoped namespaces, primary constructors
- `record` and `record struct` for immutable data
- Modern pattern matching

**Morphir IR Modeling** (See [AGENTS.md Section 6](../AGENTS.md#6-morphir-specific-modeling)):
```csharp
public abstract record TypeExpr
{
    public sealed record TInt() : TypeExpr;
    public sealed record TString() : TypeExpr;
    public sealed record TTuple(IReadOnlyList<TypeExpr> Items) : TypeExpr;
    public sealed record TFunc(TypeExpr Input, TypeExpr Output) : TypeExpr;
}
```

**CLI Logging (CRITICAL)**:
- **NO log messages to stdout** - stderr only!
- Use Serilog: `standardErrorFromLevel: LogEventLevel.Verbose`
- Test with `--json` flag: stdout must be valid JSON only
- Rationale: Scriptability (`morphir verify file.json --json | jq`)

## Architecture & Testing

**Vertical Slice Architecture** (See [AGENTS.md Section 14](../AGENTS.md#14-phase-1-implementation-patterns-ir-schema-verification)):
- Features in `src/Morphir.Tooling/Features/{Name}/`
- Immutable command/result records
- Static handler with pure functions
- FluentValidation for input validation
- CLI invokes via WolverineFx `IMessageBus.InvokeAsync<T>()`

**TDD Required** (See [AGENTS.md Section 9.1](../AGENTS.md#91-test-driven-development-tdd---red-green-refactor)):
1. **RED**: Write failing test
2. **GREEN**: Minimal code to pass
3. **REFACTOR**: Improve while staying green

**Testing Layers**:
- **Unit**: TUnit, `tests/*/`, `{ClassName}Tests.cs`
- **BDD**: Reqnroll, `tests/*/Features/*.feature`
- **Integration**: E2E CLI execution

**Coverage**: >= 80% required

## Expert Skills & Review Capabilities

**NEW**: morphir-dotnet provides specialized expert skills accessible to all agents. See [.agents/skills-reference.md](./.agents/skills-reference.md).

### Available Skills

**QA Tester** - Test plan design, regression testing, coverage monitoring
- **Review Capability**: Continuous coverage scanning, ignored test detection, BDD compliance
- **Scripts**: `smoke-test.fsx`, `regression-test.fsx`, `validate-packages.fsx`
- **Access**: Documentation + run scripts directly

**AOT Guru** - Single-file trimmed executables, AOT readiness, size optimization
- **Review Capability**: Quarterly project review for reflection usage, size trends, IL warnings
- **Scripts**: `aot-diagnostics.fsx`, `aot-analyzer.fsx`, `aot-test-runner.fsx`
- **Access**: Documentation + run scripts directly

**Release Manager** - Release lifecycle, changelog, version management
- **Review Capability**: Process consistency checks, changelog quality, version verification
- **Scripts**: `prepare-release.fsx`, `monitor-release.fsx`, `monitor-pr.fsx`, `validate-release.fsx`
- **Access**: Documentation + run scripts directly

**Technical Writer** - Documentation, Hugo/Docsy, diagrams, visual communication
- **Review Capability**: Link validation, Hugo build health, diagram syntax, style compliance
- **Scripts**: `link-validator.fsx`, `hugo-doctor.fsx`, `diagram-validator.fsx`, `content-auditor.fsx`
- **Access**: Documentation + run scripts directly
- **Expertise**: Hugo troubleshooting, Docsy customization, Mermaid/PlantUML diagrams

**For Copilot users**:
- Read skill documentation: [.agents/skills-reference.md](./.agents/skills-reference.md)
- Run automation scripts: `dotnet fsi .claude/skills/{skill}/scripts/{script}.fsx`
- Follow decision trees and playbooks
- Cross-agent compatibility: [.agents/capabilities-matrix.md](./.agents/capabilities-matrix.md)

**Token savings**: Scripts save ~300-1000 tokens per invocation vs manual workflows.

## Contribution Workflow

**Before committing**: `dotnet format && dotnet test --nologo`

**PR Requirements** (See [AGENTS.md Section 11](../AGENTS.md#11-review-and-contribution-rules)):
- Small, focused PRs with tests
- Conventional Commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`
- **DO NOT list AI assistants as co-authors** (CLA limitation, except Copilot when actual co-author)
- Tests passing, coverage >= 80%, formatters run
- IR/JSON compatibility preserved or versioned with ADR

**DCO Required**: See [CONTRIBUTING.md](../CONTRIBUTING.md)

## Important Constraints

**Avoid** (See [AGENTS.md Section 2](../AGENTS.md#2-agent-scope)):
- Breaking API changes without coordination
- IR/JSON compatibility changes without ADR + version bump
- Security/auth/crypto changes without maintainer review
- Removing/editing unrelated tests

**IR Compatibility** (See [AGENTS.md Section 7](../AGENTS.md#7-interfaces-and-contracts)):
- JSON roundtrip tests mandatory
- Schemas in `docs/spec/schemas/` validated in CI
- Backward compatibility: additive OK, breaking needs ADR

## Copilot Coding Agent Firewall Configuration

The Copilot coding agent runs with a firewall that limits internet access by default. This project is configured to allow access to the GitHub API for monitoring CI/CD workflow status.

**Configuration File**: [`.github/workflows/copilot-setup-steps.yml`](./workflows/copilot-setup-steps.yml)

This file:
- Sets `COPILOT_AGENT_FIREWALL_ALLOW_LIST_ADDITIONS` to include `api.github.com`
- Configures .NET SDK for the development environment
- Runs before the firewall is enabled

**Troubleshooting Firewall Issues**:

If you see a warning like:
> Firewall rules blocked me from connecting to one or more addresses

Options to resolve:
1. **Add to custom allowlist** (admin-only): Settings → Copilot → coding agent → Custom allowlist
2. **Update copilot-setup-steps.yml**: Add the blocked domain to `COPILOT_AGENT_FIREWALL_ALLOW_LIST_ADDITIONS`
3. **Use setup steps**: Run network calls in the `copilot-setup-steps` job before the firewall activates

**Documentation**:
- [Customizing the firewall](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent/customize-the-agent-firewall)
- [Copilot allowlist reference](https://docs.github.com/en/copilot/reference/copilot-allowlist-reference)
- [Customizing the development environment](https://docs.github.com/en/copilot/how-tos/agents/copilot-coding-agent/customizing-the-development-environment-for-copilot-coding-agent)

## Additional Resources

**Essential Reading**:
- [AGENTS.md](../AGENTS.md) - Primary guidance (READ THIS FIRST)
- [.agents/skills-reference.md](./.agents/skills-reference.md) - Expert skills (QA, AOT, Release, Technical Writer)
- [.agents/capabilities-matrix.md](./.agents/capabilities-matrix.md) - Cross-agent features
- [.agents/qa-testing.md](./.agents/qa-testing.md) - QA practices
- [.agents/aot-optimization.md](./.agents/aot-optimization.md) - AOT guidance

**Morphir Resources**:
- [Morphir Homepage](https://morphir.finos.org/)
- [morphir-elm](https://github.com/finos/morphir-elm)
- [morphir (core)](https://github.com/finos/morphir)

**Maintainers**: See `.github/CODEOWNERS` | Escalate with label `maintainer-attention` | [#morphir on FINOS Slack](https://finos-lf.slack.com/messages/morphir)

---

**Remember**: Keep diffs minimal, follow TDD, use expert skills/scripts, run formatters and tests. When uncertain about Morphir compatibility, be conservative and add a TODO with a question.
