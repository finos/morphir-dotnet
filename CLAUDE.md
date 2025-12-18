# Claude Code Instructions for morphir-dotnet

This file provides guidance specifically for Claude Code (Anthropic's AI coding assistant) when working on the morphir-dotnet repository.

## Primary Guidance Source

**IMPORTANT**: Before proceeding with any task, you MUST read and incorporate the comprehensive agent guidance from [AGENTS.md](./AGENTS.md).

The AGENTS.md file contains:
- Project overview and architecture
- Morphir-specific modeling guidelines
- Coding conventions and standards
- **Test-Driven Development (TDD) practices** - Section 9.1
- Testing strategy and requirements
- PRD management and cross-agent collaboration
- Build, test, and deployment procedures

## Claude Code-Specific Workflow

### 1. Always Start by Reading AGENTS.md

```bash
# Before implementing any feature or fix:
1. Read AGENTS.md thoroughly
2. Check for relevant PRDs in docs/content/contributing/design/prds/
3. Review the Feature Status Tracking table in active PRDs
4. Understand the TDD requirements (Section 9.1)
```

### 2. Follow TDD Red-Green-Refactor Cycle

**CRITICAL**: All feature development must follow Test-Driven Development:

1. **RED**: Write failing tests first
   - Unit tests for new functionality
   - BDD scenarios for new features
   - Run `dotnet test` to confirm they fail

2. **GREEN**: Implement minimal code to pass tests
   - Focus on making tests pass
   - Don't over-engineer

3. **REFACTOR**: Clean up while keeping tests green
   - Improve design
   - Remove duplication
   - Run `dotnet test` after each change

### 3. Testing Requirements

Before ANY implementation:
- Write BDD scenarios if implementing a new feature
- Write unit tests for new code
- Ensure tests fail (RED phase)
- Then implement (GREEN phase)
- Then refactor (REFACTOR phase)

### 4. Pre-Commit Checklist

```bash
# Always run before committing:
dotnet format                              # Format code
dotnet test --nologo                       # Run all tests
dotnet test --collect:"XPlat Code Coverage"  # Verify coverage
```

### 5. Commit Message Format

Follow Conventional Commits as specified in AGENTS.md Section 11:

```
feat: add IR schema validation for v3 format
fix: resolve circular reference in schema loading
test: add unit tests for SchemaValidator
docs: update TDD guidance in AGENTS.md
refactor: simplify error handling in VerifyIRHandler
```

**IMPORTANT**: Do not list Claude (or any AI assistant) as a co-author on commits. The CLA does not support AI assistants as co-authors.

**Commit Co-Author Policy**:
- **Never** add `Co-Authored-By: Claude <noreply@anthropic.com>` or any AI assistant as a co-author
- Only human contributors who have signed the CLA should be listed as co-authors
- You may include attribution in the commit message body (e.g., "🤖 Generated with Claude Code")
- This applies to all commits, including those created by AI coding assistants

### 6. Working with PRDs

When implementing features from PRDs:

1. Check the Feature Status Tracking table for current tasks
2. Update status as you progress: ⏳ Planned → 🚧 In Progress → ✅ Implemented
3. Add Implementation Notes to the PRD as you make decisions
4. Document any deviations from the original design

Example:
```markdown
## Implementation Notes

### SchemaLoader (2025-12-15)
- **Decision**: Used embedded resources instead of file system
- **Rationale**: Simpler deployment, no external dependencies
- **Impact**: Schemas bundled with assembly
- **Files**: SchemaLoader.cs:15-30
```

### 7. Code Quality Standards

From AGENTS.md Section 5:

- **Immutability first**: Prefer `readonly record` and immutable collections
- **No nulls**: Use `Option<T>` or nullable reference types
- **ADTs**: Make illegal states unrepresentable
- **Pure functions**: Push side effects to edges
- **Exhaustive pattern matching**: Update all matches when changing ADTs

**F# Coding Standards**: See [F# Coding Guide](docs/contributing/fsharp-coding-guide.md) for comprehensive F# best practices:
- Use active patterns instead of complex if-then chains
- Railway-oriented programming with Result types
- Immutable records and collections
- CLI script standards with proper stdout/stderr separation

### 8. Morphir-Specific Guidelines

See AGENTS.md Section 6 for detailed Morphir IR modeling:

- Use Morphir terminology consistently (Package, Module, Type, Value)
- Maintain IR fidelity - no lossy representations
- Follow naming conventions (segments, paths, qualified names)
- Model ADTs explicitly (custom types, records, tuples, functions)

### 9. CLI Logging Standards

**CRITICAL Requirement**: CLI tools must never write log messages to stdout.

- **All logging goes to stderr**: Use Serilog configured with `standardErrorFromLevel: LogEventLevel.Verbose`
- **Stdout is for output only**: JSON results, formatted output, command results
- **Configuration order matters**:
  1. Clear default providers with `builder.Logging.ClearProviders()`
  2. Add Serilog with stderr configuration
  3. Then configure Wolverine (it will use the Serilog configuration)

**Testing**: Always test commands with `--json` flag and verify stdout contains only valid JSON:

```bash
./morphir ir verify test.json --json | jq .
```

If this fails, logging is leaking to stdout.

**Why**: CLI tools that write to stdout break:
- Scriptability (cannot pipe output to jq, grep, etc.)
- JSON parsing (log lines contaminate structured output)
- Unix conventions (stdout = data, stderr = diagnostics)

**Example configuration** (see `Morphir.Tooling/Program.cs:13-46`):

```csharp
// CRITICAL: Clear default providers FIRST
builder.Logging.ClearProviders();

// Configure Serilog to write ALL logs to stderr
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(
        standardErrorFromLevel: LogEventLevel.Verbose,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    );

Log.Logger = loggerConfig.CreateLogger();
builder.Services.AddSerilog();
```

### 10. Testing Strategy Summary

From AGENTS.md Section 9:

**Unit Testing (TUnit)**
- Cover smart constructors, ADT exhaustiveness, edge cases
- Place in `tests/Morphir.*.Tests/`
- Use FluentAssertions for readable assertions

**BDD/Acceptance (Reqnroll)**
- Feature files in `tests/*/Features/*.feature`
- Step definitions in `tests/*/Steps/`
- Use Gherkin syntax: Given-When-Then

**Contract Tests**
- JSON roundtrip validation
- Cross-validate with morphir-elm samples
- Ensure compatibility with Morphir CLI

**Coverage Targets**
- Maintain >= 80% code coverage
- Coverage must not decrease with new changes

### 11. Decision-Making Framework

From AGENTS.md Section 10:

1. **Favor IR fidelity and correctness** over convenience
2. **Minimize dependencies** - justify new packages
3. **Performance changes require benchmarks**
4. **Keep effects at edges** - domain remains pure
5. **Prefer explicit ADTs** over booleans/flags

### 12. When to Escalate

See AGENTS.md Section 2 for what to escalate:

- Breaking public API changes
- IR/JSON compatibility changes without ADR
- Security/auth/crypto changes
- Destructive migrations

### 13. Resources and References

- **Morphir Homepage**: https://morphir.finos.org/
- **morphir-elm**: https://github.com/finos/morphir-elm
- **morphir (core)**: https://github.com/finos/morphir
- **morphir-dotnet (this repo)**: https://github.com/finos/morphir-dotnet

### 14. Quick Command Reference

**Build System (Nuke):**
```bash
# Build (default target)
./build.sh

# Restore dependencies
./build.sh --target Restore

# Run tests
./build.sh --target Test

# Format code
./build.sh --target Format

# Lint code
./build.sh --target Lint

# Full CI pipeline
./build.sh --target CI

# Show all available targets and parameters
./build.sh --help
```

**Windows:** Use `build.cmd` or `build.ps1` instead of `./build.sh`

**Direct .NET Commands:**
```bash
# Test with coverage
dotnet test --collect:"XPlat Code Coverage"

# Format
dotnet format

# Run CLI
dotnet run --project src/Morphir/Morphir.csproj -- [command]

# Example: Verify IR file
dotnet run --project src/Morphir/Morphir.csproj -- ir verify test.json
```

**Migration Note:** This project migrated from `just` to Nuke build. See [NUKE_MIGRATION.md](NUKE_MIGRATION.md) for complete command mappings.

### 15. File Structure Reference

```
morphir-dotnet/
├── src/
│   ├── Morphir/              # CLI/host application
│   ├── Morphir.Core/         # Core domain model
│   └── Morphir.Tooling/      # Tooling services (WolverineFx)
├── tests/
│   ├── Morphir.Core.Tests/
│   └── Morphir.Tooling.Tests/
│       ├── Features/         # BDD feature files + step definitions
│       ├── Infrastructure/   # Unit tests for infrastructure
│       └── TestData/         # Test fixtures
├── build/
│   ├── _build.csproj         # Nuke build project
│   └── Build.cs              # Build orchestration (strongly-typed C#)
├── scripts/                  # Build and utility C# scripts
├── docs/
│   ├── content/contributing/design/prds/  # PRDs
│   └── spec/                 # IR specifications and schemas
├── build.sh/cmd/ps1          # Nuke bootstrap scripts
├── AGENTS.md                 # Primary agent guidance (READ THIS!)
├── CLAUDE.md                 # This file
├── NUKE_MIGRATION.md         # Nuke migration guide
├── justfile                  # Legacy build commands (preserved for reference)
└── README.md                 # Project README
```

## Summary

1. **Always read AGENTS.md first** - It's your primary source of truth
2. **Follow TDD strictly** - Red, Green, Refactor (Section 9.1)
3. **Test before code** - BDD scenarios and unit tests come first
4. **Maintain quality** - >= 80% coverage, formatters, linters
5. **Update PRDs** - Track status and add implementation notes
6. **Commit properly** - Conventional commits, no AI co-authors
7. **Run full test suite** - Before every commit

**Remember**: AGENTS.md contains the complete, authoritative guidance. This file is just a quick reference to remind you to consult AGENTS.md and highlights the critical TDD workflow.
