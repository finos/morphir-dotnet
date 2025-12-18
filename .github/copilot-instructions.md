# GitHub Copilot Instructions for morphir-dotnet

## Project Overview

Morphir .NET provides .NET bindings, libraries, codecs, and tooling for the Morphir ecosystem. Morphir is a library of tools that captures business logic as data, making it portable, shareable, and translatable across languages and platforms.

**Purpose**: Provide .NET tooling and libraries interoperable with Morphir IR (Intermediate Representation) and developer workflows.

**Key Links**:
- [Morphir Documentation](https://morphir.finos.org/)
- [Core Morphir Tooling](https://github.com/finos/morphir)
- [Morphir Elm](https://github.com/finos/morphir-elm)
- [This Repository](https://github.com/finos/morphir-dotnet)

## Tech Stack

- **Languages**: C# 14, F# (for ADT-heavy components), TypeScript (optional dev tooling)
- **.NET**: .NET 10 SDK (see `build/global.json`)
- **Testing**: TUnit (unit tests), Reqnroll (BDD/acceptance), FsCheck (property-based)
- **Infrastructure**: WolverineFx (messaging), Marten (persistence), System.CommandLine (CLI)
- **Build Tools**: NUKE build system, Husky.Net (pre-commit hooks)

## Repository Structure

```
src/
├── Morphir/              # CLI/host application (.csproj)
├── Morphir.Core/         # Core domain model and IR definition
└── Morphir.Tooling/      # Tooling features (vertical slices)
    ├── Features/         # Feature folders (commands, handlers, validators)
    └── Infrastructure/   # Shared services (schema, validation, etc.)

tests/
├── Morphir.Tooling.Tests/  # Unit and BDD tests
├── TestData/                # Shared test fixtures
└── Integration/             # End-to-end tests

docs/
├── content/              # Documentation content
└── spec/                 # IR specification, JSON schemas, samples

.github/
├── workflows/            # CI/CD workflows
├── ISSUE_TEMPLATE/       # Issue templates
└── copilot-instructions.md  # This file
```

## Core Commands

### Build and Test
```bash
# Build
dotnet build

# Run tests
dotnet test --nologo

# Format code (required before commit)
dotnet format

# Setup git hooks
dotnet tool restore
dotnet husky install
```

### Documentation
```bash
cd docs
npm ci
npm run dev        # Local preview
npm run build      # Build static site
```

## Coding Conventions

### General Principles
- **Immutability first**: Pure domain logic, push side effects to edges (adapters)
- **Make illegal states unrepresentable**: Use ADTs (Algebraic Data Types) and avoid nulls
- **Exhaustive pattern matching**: Always handle all cases; avoid default fall-throughs
- **Value types over primitives**: Use strongly-typed IDs and quantities

### C# 14 / .NET 10 Style
- Use **file-scoped namespaces**
- Use **primary constructors** where appropriate
- Prefer `record` and `record struct` for immutable data
- Use **newer pattern matching** features
- Avoid spans/efficient collections without benchmarks

### Morphir-Specific Modeling
Model Morphir IR precisely:
- **Names**: Validated segments, non-empty paths, canonical casing
- **Types**: Aliases, custom (union) types, records, tuples, functions
- **Values/Expressions**: Literals, lambdas, application, pattern matching
- **IR Fidelity**: Avoid lossy representations

Example C# ADT:
```csharp
public abstract record TypeExpr
{
    public sealed record TInt() : TypeExpr;
    public sealed record TString() : TypeExpr;
    public sealed record TTuple(IReadOnlyList<TypeExpr> Items) : TypeExpr;
    public sealed record TFunc(TypeExpr Input, TypeExpr Output) : TypeExpr;
}
```

### CLI Tool Logging (CRITICAL)
- **CLI tools MUST NOT write log messages to stdout**
- All logging output MUST be directed to **stderr only**
- Stdout is reserved exclusively for command output (JSON, formatted results)
- Use Serilog configured with `standardErrorFromLevel: LogEventLevel.Verbose`
- Test all commands with `--json` flag to ensure stdout contains only valid JSON

Rationale: Preserve Unix philosophy (stdout = data, stderr = diagnostics) and scriptability (e.g., `morphir ir verify file.json --json | jq`)

### Error Handling
- Use typed domain errors (sum types)
- Map boundary errors at edges and log once
- Return Result types; avoid exceptions for expected flows
- CLI exit codes: 0 = success, 1 = validation failure, 2 = operational error

### Formatting
- Follow `.editorconfig` settings (4 spaces, LF line endings, UTF-8)
- Run `dotnet format` before committing (enforced by Husky pre-commit hook)
- F# files: Follow Fantomas configuration in `.editorconfig`

## Architecture Patterns

### Vertical Slice Architecture (WolverineFx)
Features organized by use case in `src/Morphir.Tooling/Features/{FeatureName}/`:

```
Features/VerifyIR/
├── VerifyIR.cs              # Command, Result, Handler, Validator
├── VersionDetector.cs       # Feature-specific logic
└── (tests in test project)
```

Example command pattern:
```csharp
// Immutable command
public record VerifyIR(string FilePath, int? SchemaVersion = null, bool JsonOutput = false);

// Immutable result
public record VerifyIRResult(bool IsValid, string SchemaVersion, List<ValidationError> Errors);

// Static handler with pure function
public static class VerifyIRHandler
{
    public static async Task<VerifyIRResult> Handle(
        VerifyIR command,
        SchemaValidator validator,  // Dependency injection
        CancellationToken ct)
    {
        // Pure logic, returns result
    }
}

// FluentValidation rules
public class VerifyIRValidator : AbstractValidator<VerifyIR>
{
    public VerifyIRValidator()
    {
        RuleFor(x => x.FilePath).NotEmpty().Must(File.Exists);
    }
}
```

### CLI Integration
CLI in `src/Morphir/Program.cs` invokes WolverineFx message bus:

```csharp
var command = new Tooling.Features.VerifyIR.VerifyIR(FilePath: path, ...);
var result = await messageBus.InvokeAsync<VerifyIRResult>(command);
```

### Infrastructure Services
- Location: `src/Morphir.Tooling/Infrastructure/{ServiceType}/`
- Registered in WolverineFx host, injected into handlers
- Examples: SchemaLoader, SchemaValidator

## Testing Strategy

### Test-Driven Development (TDD) - CRITICAL
**ALWAYS follow Red-Green-Refactor cycle**:

1. **RED**: Write a failing test first
2. **GREEN**: Write minimal code to make it pass
3. **REFACTOR**: Improve code while keeping tests green

### Testing Layers
1. **Unit Tests** (`tests/Morphir.Tooling.Tests/{Component}/`):
   - Test individual classes/functions in isolation
   - Use TUnit framework, FluentAssertions
   - File naming: `{ClassName}Tests.cs`

2. **BDD Feature Tests** (`tests/Morphir.Tooling.Tests/Features/{Feature}/`):
   - Gherkin `.feature` files alongside step definitions
   - Test business logic through handlers
   - File naming: `{Feature}.feature` and `{Feature}Steps.cs`

3. **Integration Tests** (`tests/Morphir.Tooling.Tests/Integration/`):
   - End-to-end CLI execution (spawn subprocess)
   - Test all output formats and error scenarios

### Test Coverage Requirements
- Unit tests: **>90% code coverage**
- BDD tests: All user stories covered
- Integration tests: End-to-end CLI scenarios
- **All tests must pass before PR**

### BDD-First for Features
Write BDD scenarios BEFORE implementation:
```gherkin
Feature: IR Schema Verification
  Scenario: Valid IR file passes validation
    Given a valid IR v3 file "valid-ir-v3.json"
    When I verify the IR file
    Then the validation should succeed
```

## Contribution Workflow

### Before Making Changes
1. Read existing code to understand patterns
2. Run existing tests to understand any pre-existing issues
3. Create focused tests for your changes (TDD approach)
4. Make minimal, surgical changes

### Before Committing
```bash
# Run formatters (automatic via Husky)
dotnet format

# Run all tests
dotnet test --nologo

# Verify coverage hasn't decreased
dotnet test --collect:"XPlat Code Coverage"
```

### Pull Request Guidelines
- **Small, focused PRs** with tests (TUnit and/or Reqnroll)
- **Conventional Commits**: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`
- **DO NOT list Claude or AI assistants as co-authors** (CLA limitation)
- Note: GitHub Copilot may be listed as co-author when it is an actual co-author

#### PR Checklist
- [ ] Tests added/updated and passing
- [ ] IR/JSON compatibility preserved or versioned with ADR
- [ ] Formatters/lints run (`dotnet format`)
- [ ] Documentation updated if behavior changed
- [ ] Coverage maintained or improved (>= 80%)

### DCO Requirement
All contributors must sign a Developer Certificate of Origin (DCO):
1. Create DCO file in `dco/<your-name>`
2. Include `Covered by <dco>` in commit messages
3. Update `NOTICE.txt` with copyright details

See [CONTRIBUTING.md](../CONTRIBUTING.md) for complete DCO instructions.

## Important Constraints

### What to Avoid
- Breaking public API changes without coordination
- IR/JSON compatibility changes without ADR and version bump
- Security/auth/crypto changes without maintainer review
- Destructive migrations without explicit approval
- Removing or editing unrelated tests
- Force pushes (`git reset`, `git rebase` not allowed in this workflow)

### Scope of Changes
- Implement small features end-to-end (domain → adapter → tests)
- Fix bugs with minimal diffs and add regression tests
- Improve domain types to encode Morphir invariants
- Keep docs and scripts consistent with code
- Ignore unrelated bugs or broken tests (not your responsibility)

## IR Compatibility and Contracts

- **IR JSON compatibility with Morphir toolchains is mandatory**
- Roundtrip codec tests: serialize → deserialize → equals
- Backward compatibility:
  - Additive changes: OK
  - Breaking changes: Require ADR, migration notes, version bump
- JSON Schemas in `docs/spec/schemas/` validated in CI

### JSON Serialization for AOT
Use source-generated serialization contexts:
```csharp
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VerifyIRResult))]
internal partial class MorphirJsonContext : JsonSerializerContext { }

// Usage
var json = JsonSerializer.Serialize(result, MorphirJsonContext.Default.VerifyIRResult);
```

## Documentation Requirements

Each feature must include:
1. CLI command reference in `docs/content/docs/cli/{command}.md`
2. Getting started guide in `docs/content/docs/getting-started/`
3. Troubleshooting section in `docs/content/docs/cli/troubleshooting.md`
4. Examples with all output formats
5. CI/CD integration examples

## PRD Management

Product Requirements Documents (PRDs) in `docs/content/contributing/design/prds/`:
- Each feature starts with comprehensive PRD
- PRDs are living documents updated during implementation
- Include "Feature Status Tracking" table and "Implementation Notes"
- Update status: ⏳ Planned → 🚧 In Progress → ✅ Implemented

## Additional Resources

For comprehensive agent guidance, see [AGENTS.md](../AGENTS.md), which includes:
- Detailed Morphir IR modeling examples
- Tooling and script implementations
- Property-based testing strategies
- Phase 1 implementation patterns
- ADR (Architecture Decision Records) guidance
- Complete TDD workflow examples

## Security and Compliance

- No secrets in code or tests
- Respect FINOS policies and repository license (Apache 2.0)
- Auth/crypto/legal changes require maintainer review

## Maintainers

- See `.github/CODEOWNERS` for required reviewers
- Label `maintainer-attention` for escalation
- Join [#morphir on FINOS Slack](https://finos-lf.slack.com/messages/morphir)

---

**Remember**: Keep diffs minimal, follow existing patterns, run formatters and tests, and when uncertain about Morphir compatibility, take the conservative path and add a TODO with a question.
