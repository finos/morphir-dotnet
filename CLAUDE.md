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

### 8. Morphir-Specific Guidelines

See AGENTS.md Section 6 for detailed Morphir IR modeling:

- Use Morphir terminology consistently (Package, Module, Type, Value)
- Maintain IR fidelity - no lossy representations
- Follow naming conventions (segments, paths, qualified names)
- Model ADTs explicitly (custom types, records, tuples, functions)

### 9. Testing Strategy Summary

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

### 10. Decision-Making Framework

From AGENTS.md Section 10:

1. **Favor IR fidelity and correctness** over convenience
2. **Minimize dependencies** - justify new packages
3. **Performance changes require benchmarks**
4. **Keep effects at edges** - domain remains pure
5. **Prefer explicit ADTs** over booleans/flags

### 11. When to Escalate

See AGENTS.md Section 2 for what to escalate:

- Breaking public API changes
- IR/JSON compatibility changes without ADR
- Security/auth/crypto changes
- Destructive migrations

### 12. Resources and References

- **Morphir Homepage**: https://morphir.finos.org/
- **morphir-elm**: https://github.com/finos/morphir-elm
- **morphir (core)**: https://github.com/finos/morphir
- **morphir-dotnet (this repo)**: https://github.com/finos/morphir-dotnet

### 13. Quick Command Reference

```bash
# Build
dotnet build

# Test (all)
dotnet test --nologo

# Test (specific project)
dotnet test tests/Morphir.Tooling.Tests --nologo

# Test (with coverage)
dotnet test --collect:"XPlat Code Coverage"

# Format
dotnet format

# Run CLI
dotnet run --project src/Morphir/Morphir.csproj -- [command]

# Example: Verify IR file
dotnet run --project src/Morphir/Morphir.csproj -- ir verify test.json
```

### 14. File Structure Reference

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
├── docs/
│   ├── content/contributing/design/prds/  # PRDs
│   └── spec/                 # IR specifications and schemas
├── AGENTS.md                 # Primary agent guidance (READ THIS!)
├── CLAUDE.md                 # This file
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
