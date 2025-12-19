# Claude Code Instructions for morphir-dotnet

This file provides Claude Code-specific guidance for the morphir-dotnet repository.

## Primary Guidance

**CRITICAL**: Read [AGENTS.md](./AGENTS.md) first - contains complete project guidance (architecture, Morphir modeling, TDD practices, conventions).

**Specialized Skills**: Use `@skill {skill-name}` to invoke expert skills:
- `@skill qa-tester` - Test plans, regression testing, coverage monitoring
- `@skill aot-guru` - AOT/trimming diagnostics, size optimization
- `@skill release-manager` - Release lifecycle, changelog, version management

See [.agents/skills-reference.md](./.agents/skills-reference.md) for complete skill documentation.

## Claude Code-Specific Features

### 1. Interactive Skills

**QA Tester** ([.claude/skills/qa-tester/](./. claude/skills/qa-tester/))
- Creates comprehensive test plans from issues/PRs
- Runs automated smoke and regression tests
- Monitors coverage trends and detects ignored tests
- **Review capability**: Continuous coverage scanning, BDD compliance checks
- **Scripts**: `smoke-test.fsx`, `regression-test.fsx`, `validate-packages.fsx`

**AOT Guru** ([.claude/skills/aot-guru/](./.claude/skills/aot-guru/))
- Diagnoses trimming and AOT compilation issues
- Analyzes binary size and optimizations
- Recommends source generators (C#) or Myriad (F#)
- **Review capability**: Quarterly project scan for reflection, size trends
- **Scripts**: `aot-diagnostics.fsx`, `aot-analyzer.fsx`, `aot-test-runner.fsx`

**Release Manager** ([.claude/skills/release-manager/](./.claude/skills/release-manager/))
- Orchestrates complete release lifecycle
- Manages changelog and version selection
- Monitors GitHub Actions workflows
- **Review capability**: Process consistency, changelog quality, version verification
- **Scripts**: `prepare-release.fsx`, `monitor-release.fsx`, `monitor-pr.fsx`, `validate-release.fsx`

**When to use skills**:
```
User: "Create a test plan for PR #123"
You: @skill qa-tester
     Please create a test plan for PR #123

User: "Help me fix these trimming warnings"
You: @skill aot-guru
     Diagnose trimming warnings in src/Morphir

User: "Prepare release 1.0.0"
You: @skill release-manager
     Prepare release for version 1.0.0
```

### 2. TDD Workflow (Required)

**CRITICAL**: Follow Test-Driven Development (See [AGENTS.md Section 9.1](./AGENTS.md#91-test-driven-development-tdd---red-green-refactor)):

1. **RED**: Write failing test first (unit or BDD)
2. **GREEN**: Minimal code to pass test  
3. **REFACTOR**: Improve while keeping tests green

Before ANY implementation:
- Write BDD scenarios for new features
- Write unit tests for new code
- Confirm tests fail → implement → refactor

### 3. Commit Standards

**Conventional Commits** (See [AGENTS.md Section 11](./AGENTS.md#11-review-and-contribution-rules)):
```
feat: add IR schema validation for v3 format
fix: resolve circular reference in schema loading
test: add unit tests for SchemaValidator
```

**IMPORTANT**: Do NOT list Claude or AI assistants as co-authors (CLA limitation). Attribution in commit body is OK: "🤖 Generated with Claude Code"

### 4. CLI Logging (CRITICAL)

**CLI tools MUST NOT write log messages to stdout** (See [AGENTS.md CLI Logging](./AGENTS.md#5-coding-conventions)):
- All logging → **stderr only**
- Stdout → command output (JSON, results) only
- Use Serilog: `standardErrorFromLevel: LogEventLevel.Verbose`
- Test: `morphir verify file.json --json | jq` must work

Rationale: Scriptability and Unix philosophy (stdout = data, stderr = diagnostics)

### 5. Key Commands

```bash
# Build and test
./build.sh                # Default build
./build.sh --target Test  # Run tests
dotnet format             # Format (required pre-commit)

# Use skills
@skill qa-tester     # Test plans, coverage
@skill aot-guru      # AOT diagnostics, optimization
@skill release-manager  # Release lifecycle
```
## Code Quality Standards

**From [AGENTS.md Section 5](./AGENTS.md#5-coding-conventions)**:
- Immutability first, effects at edges
- ADTs: Make illegal states unrepresentable
- Exhaustive pattern matching, no nulls
- Value types for IDs/quantities

**F# Standards** (See [F# Coding Guide](./docs/contributing/fsharp-coding-guide.md)):
- Use active patterns over complex if-then chains
- Railway-oriented programming with Result types
- Immutable records and collections
- CLI script standards (stdout/stderr separation)

**Morphir IR Modeling** (See [AGENTS.md Section 6](./AGENTS.md#6-morphir-specific-modeling)):
```csharp
public abstract record TypeExpr
{
    public sealed record TInt() : TypeExpr;
    public sealed record TTuple(IReadOnlyList<TypeExpr> Items) : TypeExpr;
    public sealed record TFunc(TypeExpr Input, TypeExpr Output) : TypeExpr;
}
```

## Resources

**Essential Documentation**:
- [AGENTS.md](./AGENTS.md) - Primary guidance (READ FIRST)
- [.agents/skills-reference.md](./.agents/skills-reference.md) - All skills documented
- [.agents/capabilities-matrix.md](./.agents/capabilities-matrix.md) - Cross-agent features
- [.agents/qa-testing.md](./.agents/qa-testing.md) - QA practices
- [.agents/aot-optimization.md](./.agents/aot-optimization.md) - AOT guidance
- [docs/contributing/fsharp-coding-guide.md](./docs/contributing/fsharp-coding-guide.md) - F# best practices
- [docs/contributing/aot-trimming-guide.md](./docs/contributing/aot-trimming-guide.md) - User-facing AOT docs

**Skill Documentation**:
- [.claude/skills/qa-tester/skill.md](./.claude/skills/qa-tester/skill.md)
- [.claude/skills/aot-guru/skill.md](./.claude/skills/aot-guru/skill.md)
- [.claude/skills/release-manager/skill.md](./.claude/skills/release-manager/skill.md)

**Morphir Resources**:
- [Morphir Homepage](https://morphir.finos.org/)
- [morphir-elm](https://github.com/finos/morphir-elm)
- [morphir (core)](https://github.com/finos/morphir)

## Summary

1. **Read AGENTS.md first** - Primary source of truth
2. **Use skills** - `@skill {skill-name}` for specialized tasks
3. **Follow TDD strictly** - Red, Green, Refactor
4. **CLI logging** - stderr only, never stdout
5. **Test before commit** - `dotnet format && dotnet test`
6. **Coverage >= 80%** - Maintain or improve
7. **No AI co-authors** - Attribution in commit body OK

**Workflow**: AGENTS.md → Use appropriate skill → Follow TDD → Run tests → Commit with conventional format

---

**Remember**: Skills exist to help you deliver better results faster. Use them liberally. They embody project expertise and best practices.
