# AGENTS.md

Guidance for AI coding agents contributing to finos/morphir-dotnet (a .NET
binding for the Morphir ecosystem). Produce correct, minimal, and well‑tested
changes aligned with Morphir IR and tooling.

## Quick Navigation

- **This file (AGENTS.md)**: Primary guidance for all AI agents
- **Specialized Topics**: See [.agents/](./.agents/) directory for domain-specific guides
  - [Skills Reference](./.agents/skills-reference.md) - QA Tester, AOT Guru, Release Manager
  - [Capabilities Matrix](./.agents/capabilities-matrix.md) - Cross-agent feature availability
  - [QA Testing](./.agents/qa-testing.md) - Test plans, playbooks, scripts
  - [AOT Optimization](./.agents/aot-optimization.md) - Trimming, AOT guidance
- **Agent-Specific Instructions**:
  - **Claude Code**: [CLAUDE.md](./CLAUDE.md) + [.claude/skills/](./.claude/skills/)
  - **GitHub Copilot**: [.github/copilot-instructions.md](./.github/copilot-instructions.md)
  - **Cursor**: [.cursorrules](./.cursorrules)
  - **Windsurf**: [.windsurf/rules.md](./.windsurf/rules.md) (if present)
  - **Aider**: [.aider.conf.yml](./.aider.conf.yml) (if present)
- **Documentation**: See [docs/](./docs/) for user-facing docs

## Project Links

- Morphir: https://morphir.finos.org/
- morphir (core/tooling): https://github.com/finos/morphir
- morphir-elm: https://github.com/finos/morphir-elm
- morphir-dotnet (this repo): https://github.com/finos/morphir-dotnet

## 1) Project Overview

Purpose
- Provide .NET bindings, libraries, codecs, and tooling interoperable with the
  Morphir IR (intermediate representation) and developer workflows.

Architecture (high-level)
- domain/: Pure Morphir domain/IR models, types, values, invariants.
- adapters/: Edges (serialization, CLI integration, file I/O, codegen).
- app/: Composition root, configuration, hosting/CLI entry points.
- docs/spec/: IR specification and schemas (JSON/OpenAPI schemas and samples).
- tests/: Unit (TUnit), property-based (via generators), and BDD/acceptance (Reqnroll).

Stacks and baselines
- C# 14, .NET 10 (SDK pinned in global.json).
- F# for ADT-heavy components where appropriate.
- TypeScript optional for dev tooling/schema checks.
- Critter Stack (WolverineFx & Marten) used in development for messaging and persistence.

Principles
- Immutability-first; push effects to edges.
- ADTs: make illegal states unrepresentable; avoid nulls.
- Strong testing: TUnit for units, Reqnroll for behavior, contract/roundtrip tests.

## 2) Agent Scope

Do
- Implement small features end-to-end (domain → adapter → tests).
- Fix bugs with minimal diffs and add regression tests (TUnit and/or Reqnroll).
- Improve domain types to encode Morphir invariants.
- Keep docs and scripts consistent with code.

Avoid or escalate
- Breaking public API changes (coordinate with maintainers).
- IR/JSON compatibility changes without ADR and version bump.
- Security/auth/crypto changes.
- Destructive migrations without explicit approval.

## 3) Repository Map

Adjust to actual repo layout.

- src/
    - Morphir: C# CLI/host (.csproj, C# 14)
    - Morphir.Core: Core domain model and IR definition  (.csproj, C# 14)
- docs/spec/: IR specification and JSON/OpenAPI schemas, IR samples/fixtures
- tests/**.csproj: TUnit, Reqnroll, contract tests
- scripts/: format/test/contract/codegen utilities
- docs/: ADRs, architecture, contribution notes
- .github/: CI workflows, CODEOWNERS, PR templates

External touchpoints
- Morphir CLI and JSON IR formats
- morphir-elm and morphir repos for canonical IR samples

## 4) Build, Run, Test

Environment
- Required env vars documented in .env.example. Do not commit secrets.

First-time Setup
- Restore tools: `dotnet tool restore`
- Restore dependencies: `dotnet restore` or `./build.sh --target Restore`
- Install git hooks: `dotnet husky install`

Commands
- .NET (C# 14 / F#, .NET 10)
    - Build: `dotnet build` or `./build.sh`
    - Test (TUnit + Reqnroll): `dotnet test --nologo` or `./build.sh --target Test`
    - Format: `dotnet format` or `./build.sh --target Format`
- TypeScript (if present)
    - Install: `npm ci`
    - Build: `npm run build`
    - Test: `npm test`
    - Lint/Format: `npm run lint && npm run format`

CI
- All formatters/linters must pass.
- Tests must be green; coverage must not decrease (>= 80% unless stated).
- Snapshot/golden updates require justification in PRs.

## 5) Coding Conventions

General
- Domain is pure; side effects in adapters.
- Prefer Option/Result/Either-like models; avoid nulls/throws for expected flows.
- Exhaustive pattern matching for ADTs; avoid default fall-throughs.
- Use value types for IDs/quantities over raw primitives.

Error handling
- Typed domain errors (sum types). Map boundary errors at edges and log once.

Naming
- Use Morphir terms consistently (Package, Module, Type, Value, IR nodes).

Formatting
- `.editorconfig` + `dotnet format`. Prettier/ESLint for TS (if present).

C# 14 / .NET 10 specifics
- Prefer readonly `record struct`/`record` where appropriate.
- Use file‑scoped namespaces, primary constructors, and newer pattern features.
- Favor spans and efficient collections only with benchmarks backing changes.

F# specifics
- See [F# Coding Guide](./docs/contributing/fsharp-coding-guide.md) for comprehensive F# standards
- **Prefer active patterns** over complex if-then chains for value extraction
- Use discriminated unions to make illegal states unrepresentable
- Follow CLI logging standards (stdout for data, stderr for diagnostics)
- Use Result types for railway-oriented programming
- Maintain immutability with records and immutable collections

Native AOT, Trimming, and Size Optimization
- See [AOT/Trimming Guide](./docs/contributing/aot-trimming-guide.md) for comprehensive AOT/trimming guidance
- Use source generators for JSON serialization (not reflection)
- Design for trimming from the start (avoid reflection and dynamic code)
- Test with `PublishAot=true` and `PublishTrimmed=true`
- Target sizes: 5-8 MB (minimal), 8-12 MB (feature-rich), 10-15 MB (with UI)
- See [Issue #221](https://github.com/finos/morphir-dotnet/issues/221) for implementation tracking

CLI Tool Logging Requirements
- **CRITICAL**: CLI tools MUST NOT write log messages to stdout
- All logging output MUST be directed to stderr only
- Stdout is reserved exclusively for command output (JSON, formatted results, etc.)
- Use Serilog configured with `standardErrorFromLevel: LogEventLevel.Verbose`
- Clear default logging providers before configuring Serilog to avoid double logging
- Test all commands with `--json` flag to ensure stdout contains only valid JSON

Rationale: CLI tools that write to stdout break scriptability and JSON parsing.
Users expect to pipe command output (e.g., `morphir ir verify file.json --json | jq`)
without log noise contaminating the structured output. Following Unix philosophy:
stdout = data, stderr = diagnostics.

## 6) Morphir-Specific Modeling

Model Morphir IR precisely:
- Names: validated segments, non-empty paths, canonical casing rules.
- Types: aliases, custom (union) types, records, tuples, functions.
- Values/Expr: literals, lambdas, application, pattern matching.
- Keep IR fidelity; avoid lossy representations.

F# example
```fsharp
type NameSegment = private NameSegment of string
module NameSegment =
  let tryCreate s =
    if System.Text.RegularExpressions.Regex.IsMatch(s, "^[a-z][a-z0-9_]*$")
    then Ok (NameSegment s) else Error "InvalidNameSegment"

type QualifiedName = { Package: string list; Module: string list; Local: string }

type TypeExpr =
  | TInt
  | TString
  | TBool
  | TTuple of TypeExpr list
  | TRecord of Map<string, TypeExpr>
  | TFunc of input: TypeExpr * output: TypeExpr
```

C# example
```csharp
public abstract record TypeExpr {
  public sealed record TInt() : TypeExpr;
  public sealed record TString() : TypeExpr;
  public sealed record TBool() : TypeExpr;
  public sealed record TTuple(IReadOnlyList<TypeExpr> Items) : TypeExpr;
  public sealed record TRecord(IReadOnlyDictionary<string, TypeExpr> Fields) : TypeExpr;
  public sealed record TFunc(TypeExpr Input, TypeExpr Output) : TypeExpr;
}
```

TypeScript (tooling)
```ts
export type TypeExpr =
  | { _tag: "TInt" }
  | { _tag: "TString" }
  | { _tag: "TBool" }
  | { _tag: "TTuple"; items: ReadonlyArray<TypeExpr> }
  | { _tag: "TRecord"; fields: Readonly<Record<string, TypeExpr>> }
  | { _tag: "TFunc"; input: TypeExpr; output: TypeExpr };
```

## 7) Interfaces and Contracts

- IR JSON compatibility with Morphir toolchains is mandatory.
- Roundtrip codec tests: serialize → deserialize → equals.
- Backward compatibility
    - Additive OK.
    - Breaking changes require ADR, migration notes, and version bump.
- If OpenAPI/JSON Schemas exist, keep in `docs/spec/schemas/` and validate in CI.

## 8) Tooling and Scripts for Agents

Scripts to provide/use:
- scripts/format-all — format .NET (and TS if present)
- scripts/test-all — run TUnit and Reqnroll suites (and TS if present)
- scripts/check-contracts — run IR/JSON roundtrip and contract tests
- scripts/gen — codegen or schema sync steps (if any)

Run before committing:
```bash
scripts/format-all
scripts/test-all
scripts/check-contracts
```

Suggested implementations
- scripts/format-all
```bash
#!/usr/bin/env bash
set -euo pipefail
dotnet format
if [ -f "package.json" ]; then npm run format; fi
```

- scripts/test-all
```bash
#!/usr/bin/env bash
set -euo pipefail
dotnet test --nologo
if [ -f "package.json" ]; then npm test; fi
```

- scripts/check-contracts
```bash
#!/usr/bin/env bash
set -euo pipefail
# Implement IR/JSON codec roundtrip tests and any schema checks.
# Example (placeholder):
# dotnet test --filter "Category=Contract"
echo "Run IR JSON roundtrip + contract tests against Morphir samples."
```

Make scripts executable:
```bash
chmod +x scripts/format-all scripts/test-all scripts/check-contracts scripts/gen || true
```

## 9) Testing Strategy

Unit testing (TUnit)
- Place unit tests in tests/csharp.unit or equivalent.
- Cover smart constructors, ADT exhaustiveness, and edge cases.

Behavior/acceptance (Reqnroll)
- Feature files under tests/csharp.bdd/Features/*.feature
- Step definitions in tests/csharp.bdd/Steps/
- Use Reqnroll hooks for setup/teardown and environment orchestration.

Property-based tests
- Use FsCheck or generators within TUnit where appropriate:
    - Name normalization/validation
    - Codec roundtrips for random IR fragments
    - Structural invariants (non-empty lists where required, arity constraints)

Contract tests
- Roundtrip JSON using canonical samples from morphir-elm/morphir.
- Cross-validate against Morphir CLI when feasible.

Coverage targets
- Maintain or improve coverage (>= 80% overall unless specified).

## 9.1) Test-Driven Development (TDD) - Red, Green, Refactor

**CRITICAL**: This project follows strict Test-Driven Development practices. When implementing new features or fixing bugs, you MUST follow the Red-Green-Refactor cycle:

### Red-Green-Refactor Cycle

1. **RED**: Write a failing test first
   - Write the test that describes the desired behavior
   - Run the test to confirm it fails (red)
   - This ensures the test is actually testing something

2. **GREEN**: Write minimal code to make the test pass
   - Implement only what's needed to make the test green
   - Don't worry about perfection yet
   - Focus on making it work

3. **REFACTOR**: Improve the code while keeping tests green
   - Clean up the implementation
   - Remove duplication
   - Improve naming and structure
   - Run tests after each refactoring step

### TDD Workflow for Agents

When implementing features:

```bash
# 1. RED: Write failing unit tests
# Example: tests/Morphir.Tooling.Tests/Features/VerifyIR/VerifyIRHandlerTests.cs
[Test]
public async Task Handle_ShouldReturnValid_WhenIRIsValid()
{
    // Arrange
    var command = new VerifyIR("valid-ir.json");

    // Act
    var result = await VerifyIRHandler.Handle(command, validator, ct);

    // Assert
    result.IsValid.Should().BeTrue();
}

# Run: dotnet test
# Expect: Test fails because VerifyIRHandler doesn't exist

# 2. GREEN: Implement minimal code
public static class VerifyIRHandler
{
    public static Task<VerifyIRResult> Handle(VerifyIR command, ...)
    {
        // Minimal implementation to pass test
        return Task.FromResult(new VerifyIRResult(IsValid: true, ...));
    }
}

# Run: dotnet test
# Expect: Test passes (green)

# 3. REFACTOR: Improve implementation
public static class VerifyIRHandler
{
    public static async Task<VerifyIRResult> Handle(
        VerifyIR command,
        SchemaValidator validator,
        CancellationToken ct)
    {
        var jsonContent = await File.ReadAllTextAsync(command.FilePath, ct);
        var validationResult = await validator.ValidateAsync(jsonContent, "3", ct);

        return new VerifyIRResult(
            IsValid: validationResult.IsValid,
            SchemaVersion: "3",
            DetectionMethod: "auto",
            FilePath: command.FilePath,
            Errors: validationResult.Errors,
            Timestamp: DateTime.UtcNow
        );
    }
}

# Run: dotnet test
# Expect: Tests still pass (still green)
```

### BDD-First for Features

For new features, write BDD scenarios BEFORE implementation:

```gherkin
# tests/Morphir.Tooling.Tests/Features/VerifyIR/VerifyIR.feature
Feature: IR Schema Verification
  As a Morphir developer
  I want to validate IR JSON files against schemas
  So that I can ensure IR correctness

  Scenario: Valid IR file passes validation
    Given a valid IR v3 file "valid-ir-v3.json"
    When I verify the IR file
    Then the validation should succeed
    And no errors should be reported
```

Then implement step definitions:

```csharp
[Given(@"a valid IR v3 file ""(.*)""")]
public void GivenAValidIRV3File(string fileName)
{
    _context.FilePath = Path.Combine("TestData", fileName);
}

[When(@"I verify the IR file")]
public async Task WhenIVerifyTheIRFile()
{
    var command = new VerifyIR(_context.FilePath);
    _context.Result = await _handler.Handle(command, _validator, CancellationToken.None);
}

[Then(@"the validation should succeed")]
public void ThenTheValidationShouldSucceed()
{
    _context.Result.IsValid.Should().BeTrue();
}
```

### TDD Rules for Agents

1. **Never write production code without a failing test first**
   - Exception: Simple refactorings that don't change behavior

2. **Write the simplest test that could possibly fail**
   - Start with happy path
   - Add edge cases incrementally

3. **Write only enough production code to make the failing test pass**
   - Don't anticipate future requirements
   - Keep it simple

4. **Refactor continuously**
   - After each green test, look for improvements
   - Keep tests green throughout refactoring

5. **Test behaviors, not implementation details**
   - Focus on public APIs and observable outcomes
   - Avoid testing private methods directly

6. **One test, one assertion (when practical)**
   - Makes failures easier to diagnose
   - Tests are more focused

### Test Organization

```
tests/Morphir.Tooling.Tests/
├── Features/
│   └── VerifyIR/
│       ├── VerifyIR.feature          # BDD scenarios
│       ├── VerifyIRSteps.cs          # Step definitions
│       └── VerifyIRHandlerTests.cs   # Unit tests
├── Infrastructure/
│   └── JsonSchema/
│       ├── SchemaLoaderTests.cs      # Unit tests
│       └── SchemaValidatorTests.cs   # Unit tests
└── TestData/
    ├── valid-ir-v3.json
    └── invalid-*.json
```

### TDD Anti-Patterns to Avoid

❌ **Don't write tests after the code**
- Defeats the purpose of TDD
- Tests become implementation-focused instead of behavior-focused

❌ **Don't skip the refactor step**
- Code quality degrades over time
- Technical debt accumulates

❌ **Don't write too many tests before implementation**
- Write one test, implement, refactor, repeat
- Maintains focus and momentum

❌ **Don't test implementation details**
- Focus on behavior and contracts
- Private methods are tested through public APIs

### Verification Before Commit

Always run the full test suite before committing:

```bash
# Run all tests
dotnet test --nologo

# Verify coverage hasn't decreased
dotnet test --collect:"XPlat Code Coverage"

# Format code
dotnet format
```

## 10) Decision Policies

- Favor IR fidelity and correctness.
- Minimize dependencies; justify new packages.
- Performance changes require benchmarks and tests.
- Keep effects at the edges; domain remains pure.
- Prefer explicit ADTs over booleans/flags; update exhaustive matches on change.

### User Interaction Policies

**CRITICAL - Always Require User Confirmation for Destructive Actions:**

- **Auto-merging PRs**: NEVER auto-merge PRs without explicit user confirmation. Always prompt: "Do you want to auto-merge this PR when all checks pass?"
- **Deleting branches**: Always confirm before deleting remote or local branches
- **Force pushing**: Always confirm before force-pushing to any branch
- **Publishing releases**: Always confirm before triggering deployment workflows
- **Modifying production**: Always confirm before making changes that affect production systems

**Guiding Principle**: When in doubt, ask. It's better to prompt for confirmation than to perform an unwanted action.

## 11) Review and Contribution Rules

- Small, focused PRs with tests (TUnit and/or Reqnroll).
- Conventional Commits: feat:, fix:, refactor:, test:, docs:
- **Do not list Claude (or any AI assistant) as a co-author on commits.** Our CLA does not support AI assistants as co-authors. Note: GitHub Copilot is supported and may be listed as a co-author when it is an actual co-author.
- PR checklist:
    - [ ] Tests added/updated and passing
    - [ ] IR/JSON compatibility preserved or versioned with ADR
    - [ ] Formatters/lints run
    - [ ] Docs/ADR updated if behavior changed

## 12) Security and Compliance

- No secrets in code or tests.
- Respect FINOS policies and repository license.
- Auth/crypto/legal changes require maintainer review.

## 13) PRD Management and Implementation Tracking

Product Requirements Documents (PRDs)
- Location: `docs/content/contributing/design/prds/`
- Each feature starts with a comprehensive PRD before implementation
- PRDs are living documents updated during implementation

PRD Structure
- **Status tracking**: PRDs include a "Feature Status Tracking" table with all features and their implementation status
- **Implementation notes**: Add "Implementation Notes" sections to capture:
    - Design decisions made during implementation
    - Deviations from original design with rationale
    - Architectural insights discovered during development
    - Dependencies or blockers encountered
- **Open questions**: Document decisions as they're made in the "Open Questions" section

PRD Status Workflow
1. **Draft**: Initial PRD being refined
2. **Approved**: PRD reviewed and ready for implementation
3. **In Progress**: Active implementation underway
4. **Completed**: All features implemented, PRD archived
5. **Deferred**: PRD postponed, marked with reason

Cross-Agent Collaboration
- When starting work, check the PRD Feature Status Tracking table for current task
- Update feature status in real-time: ⏳ Planned → 🚧 In Progress → ✅ Implemented
- Add implementation notes directly in the PRD under relevant sections
- PRD serves as source of truth for "what's next" across multiple AI agent sessions

Example Implementation Notes Section
```markdown
## Implementation Notes

### Phase 1: Core Verification (Current)

#### VerifyIR Command Handler (2025-12-15)
- **Decision**: Used WolverineFx's `IMessageBus.InvokeAsync<T>()` instead of `IMessageContext.Send()`
- **Rationale**: InvokeAsync provides request-response pattern needed for CLI
- **Impact**: Simpler than setting up reply queues
- **Files**: `src/Morphir/Program.cs:397-401`, `src/Morphir.Tooling/Features/VerifyIR/VerifyIR.cs`

#### Schema Loading (2025-12-15)
- **Change**: Used `Assembly.GetManifestResourceStream()` instead of `EmbeddedFileProvider`
- **Rationale**: Simpler, no additional dependencies
- **Impact**: None, works as designed
- **Files**: `src/Morphir.Tooling/Infrastructure/JsonSchema/SchemaLoader.cs:15-25`
```

PRD Index (Markdown)
- Maintain `docs/content/contributing/design/prds/_index.md` with all PRDs and their status
- Format:
```markdown
| PRD | Status | Phase | Current Task |
|-----|--------|-------|--------------|
| [IR Schema Verification](./ir-json-schema-verification.md) | In Progress | Phase 1 | WolverineFx setup |
| [Migration Tooling](./ir-migration.md) | Draft | - | Design review |
```

## 14) Phase 1 Implementation Patterns (IR Schema Verification)

This section documents architectural patterns and conventions established during Phase 1 implementation that should be followed in future phases.

### Vertical Slice Architecture with WolverineFx

**Structure**: Features organized by use case in `src/Morphir.Tooling/Features/{FeatureName}/`

Example: `Features/VerifyIR/`
```
Features/VerifyIR/
├── VerifyIR.cs              # Command, Result, Handler, Validator (all in one file)
├── VersionDetector.cs       # Feature-specific logic
└── VerifyIR.feature         # BDD scenarios (in test project)
```

**Command Pattern**:
```csharp
// Command (immutable record)
public record VerifyIR(
    string FilePath,
    int? SchemaVersion = null,
    bool JsonOutput = false,
    bool Quiet = false
);

// Result (immutable record)
public record VerifyIRResult(
    bool IsValid,
    string SchemaVersion,
    string DetectionMethod,
    string FilePath,
    List<ValidationError> Errors,
    DateTime Timestamp
);

// Handler (static class with pure function)
public static class VerifyIRHandler
{
    public static async Task<VerifyIRResult> Handle(
        VerifyIR command,
        SchemaValidator validator,  // Injected dependency
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
        RuleFor(x => x.FilePath)
            .NotEmpty()
            .Must(File.Exists).WithMessage("File does not exist: {PropertyValue}");
    }
}
```

### CLI Integration with System.CommandLine

**Pattern**: CLI in `src/Morphir/Program.cs` invokes WolverineFx message bus

```csharp
// Create command with options
var verifyCommand = new Command("verify", "Verify Morphir IR JSON");
var filePathArgument = new Argument<FileInfo>("file-path");
var schemaVersionOption = new Option<int?>("--schema-version");
verifyCommand.Arguments.Add(filePathArgument);
verifyCommand.Options.Add(schemaVersionOption);

// Set action handler
verifyCommand.SetAction(async parseResult =>
{
    // Create WolverineFx host
    using var host = Tooling.Program.CreateToolingHost();
    await host.StartAsync();

    var messageBus = host.Services.GetRequiredService<IMessageBus>();

    // Create command
    var command = new Tooling.Features.VerifyIR.VerifyIR(
        FilePath: filePath.FullName,
        SchemaVersion: schemaVersion,
        JsonOutput: jsonOutput,
        Quiet: quiet
    );

    // Execute via message bus
    var result = await messageBus.InvokeAsync<VerifyIRResult>(command);

    // Format output
    FormatOutput(result, jsonOutput, quiet);

    return result.IsValid ? 0 : 1;
});
```

### Infrastructure Services

**Location**: `src/Morphir.Tooling/Infrastructure/{ServiceType}/`

**Pattern**: Services registered in WolverineFx host, injected into handlers

```csharp
// In Morphir.Tooling/Program.cs
builder.Services.AddWolverine(opts =>
{
    // Register infrastructure services
    opts.Services.AddSingleton<SchemaLoader>();
    opts.Services.AddSingleton<SchemaValidator>();

    // Auto-discover handlers
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
});
```

### Testing Layers

**1. Unit Tests** (`tests/Morphir.Tooling.Tests/{Component}/`)
- Test individual classes/functions in isolation
- Use TUnit framework
- Use FluentAssertions for readable assertions
- Test file naming: `{ClassName}Tests.cs`

**2. BDD Feature Tests** (`tests/Morphir.Tooling.Tests/Features/{Feature}/`)
- Gherkin feature files alongside step definitions
- Test business logic through handler
- File naming: `{Feature}.feature` and `{Feature}Steps.cs`

**3. Integration Tests** (`tests/Morphir.Tooling.Tests/Integration/`)
- Test end-to-end CLI execution
- Actually spawn CLI as subprocess
- Test all output formats and error scenarios
- File naming: `{Feature}Integration.feature` and `{Feature}IntegrationSteps.cs`

### Test Data Organization

```
tests/Morphir.Tooling.Tests/
├── TestData/                    # Shared test data
│   ├── valid-ir-v1.json
│   ├── valid-ir-v2.json
│   ├── valid-ir-v3.json
│   ├── invalid-*.json
│   └── malformed.json
└── Integration/
    └── CLI/
        ├── CliTestHelper.cs     # Reusable CLI execution helper
        ├── {Feature}.feature
        └── {Feature}Steps.cs
```

### Error Handling Pattern

**Domain Errors**: Use Result types in handlers, return structured errors
```csharp
try
{
    // Business logic
    return new VerifyIRResult(IsValid: true, ...);
}
catch (JsonException ex)
{
    // Handle expected errors, return structured result
    return new VerifyIRResult(
        IsValid: false,
        Errors: [new ValidationError(
            Path: "$",
            Message: $"Malformed JSON: {ex.Message}",
            Expected: "Valid JSON",
            Found: "Invalid JSON syntax"
        )],
        ...
    );
}
```

**CLI Errors**: Map to exit codes
- 0: Success
- 1: Validation failure (expected/business error)
- 2: Operational error (file not found, etc.)

### JSON Serialization for AOT

**Pattern**: Use source-generated serialization context

```csharp
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VerifyIRResult))]
internal partial class MorphirJsonContext : JsonSerializerContext
{
}

// Usage
var json = JsonSerializer.Serialize(result, MorphirJsonContext.Default.VerifyIRResult);
```

### Documentation Requirements

**Each feature must include**:
1. CLI command reference in `docs/content/docs/cli/{command}.md`
2. Getting started guide in `docs/content/docs/getting-started/`
3. Troubleshooting section in `docs/content/docs/cli/troubleshooting.md`
4. Examples with all output formats
5. CI/CD integration examples

**Documentation structure**:
```markdown
# Command Reference
- Synopsis
- Description
- Arguments
- Options (with examples)
- Exit codes
- Output formats
- Examples (basic → advanced)
- Common errors
- Troubleshooting
- Related commands
```

### Test Coverage Requirements

- **Unit tests**: >90% code coverage
- **BDD tests**: All user stories covered
- **Integration tests**: End-to-end CLI scenarios
- **All tests must pass before PR**

### Commit Messages

Follow Conventional Commits with co-author attribution:

```
feat: add comprehensive BDD integration tests for CLI

- Created CLI integration test infrastructure
- Added 13 BDD scenarios covering all features
- Fixed JSON exception handling
- All 62 tests passing

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>
```

**Note**: Do not list Claude or AI assistants as co-authors in the commit author field.

## 15) Known Issues / TODOs

- Maintain a prioritized list or link GitHub issues:
    - TODO: <short> [link]
    - BUG: <short> [link]
    - COMPAT: <short> [link]

## 15) ADRs and Rationale

- docs/adr/*.md — key decisions (IR mapping, codec strategy, versioning).
- Include ADRs for breaking changes or cross-tool compatibility shifts.

## 16) Maintainers and Ownership

- CODEOWNERS defines required reviewers.
- Primary contacts: <handles/emails>
- Escalation: label `maintainer-attention` or use project channels.

## 17) Agent Execution Rules (Important)

- Keep diffs minimal; follow existing patterns and style.
- Update all exhaustive matches and affected tests when changing ADTs.
- Always run formatters, TUnit, and Reqnroll suites; run contract/roundtrip checks.
- If uncertain about Morphir compatibility, add a TODO with a question and take
  the conservative path.

## 18) Specialized Guidance

This repository provides specialized, domain-specific guidance in the [.agents/](./.agents/) directory:

### Skills Reference

**NEW**: [Skills Reference](./.agents/skills-reference.md) - Comprehensive documentation of all expert skills (gurus):
- **QA Tester** - Test plan design, regression testing, coverage monitoring, issue reporting
- **AOT Guru** - Single-file trimmed executables, AOT readiness, trimming diagnostics, size optimization
- **Release Manager** - Release lifecycle, changelog management, version selection, workflow monitoring
- Each skill includes: scope, competencies, review capabilities, automation scripts, manual workflows
- Decision trees and pattern catalogs for common scenarios
- Cross-agent accessibility information

**NEW**: [Capabilities Matrix](./.agents/capabilities-matrix.md) - Cross-agent feature availability:
- Which skills work with which agents (Claude, Copilot, Cursor, Windsurf, Aider)
- How to invoke reviews in each agent
- Script portability notes and token usage comparisons
- Agent-specific workflows and troubleshooting

### Domain-Specific Guides

- **[QA Testing](./.agents/qa-testing.md)** - Comprehensive QA guidance
  - Test plan templates
  - Pre-commit and PR verification checklists
  - Regression, feature, build, and package testing playbooks
  - Bug report templates
  - Test scripts (F#): smoke tests, regression tests, package validation
  - BDD and unit testing guides
  - Coverage requirements and best practices

- **[AOT Optimization](./.agents/aot-optimization.md)** - Native AOT, trimming, and size optimization
  - Decision trees for AOT compatibility issues
  - Diagnostic procedures and automated testing
  - Common patterns and workarounds
  - Size optimization strategies
  - Known issues database and continuous improvement
  - Integration with CI/CD pipelines

### How to Use Skills with Different AI Agents

morphir-dotnet provides specialized expert skills that work across all AI coding agents, though with different invocation methods.

**Quick Reference:**

| Agent | Invocation Method | Example |
|-------|------------------|---------|
| **Claude Code** | `@skill {skill-name}` | `@skill qa-tester` |
| **GitHub Copilot** | Natural language + skill name | "Use QA Tester skill to create test plan" |
| **Cursor** | `.cursorrules` auto-trigger or `@file` mention | `@.claude/skills/qa-tester/SKILL.md` |
| **Windsurf** | Natural language (auto-discovery) | "Use QA Tester to validate this PR" |
| **JetBrains AI** | Custom prompts or natural language | "Use QA Tester skill..." |

**Key Points:**
- **Claude Code**: Native `@skill` command with interactive assistance
- **Other Agents**: Documentation-based emulation via natural language or file references
- **All Agents**: Can run automation scripts directly: `dotnet fsi .claude/skills/{skill}/scripts/{script}.fsx`
- **Skill Aliases**: Some skills document short forms (e.g., "qa", "tester") but these are **NOT functional** - use official names only

**Detailed Platform-Specific Guidance:**
- See [.agents/skills-reference.md](./.agents/skills-reference.md#cross-platform-skill-invocation) for comprehensive invocation patterns for each platform
- See [capabilities-matrix.md](./.agents/capabilities-matrix.md) for feature comparison table

**Cross-Platform Testing:**
- Issue #266: GitHub Copilot skill emulation tests
- Issue #267: Cursor skill emulation tests
- Issue #268: Windsurf skill emulation tests
- Issue #269: JetBrains AI skill emulation tests

### Future Topics

The `.agents/` directory will expand to include:
- Documentation and ADR writing
- Security testing and compliance
- Performance testing and benchmarking

See [.agents/README.md](./.agents/README.md) for navigation and contribution guidelines.

## 19) Resources and References

### Primary Documentation
- This file (AGENTS.md) - Start here for all agents
- [.agents/](./.agents/) - Specialized topic guides
- [CLAUDE.md](./CLAUDE.md) - Claude Code-specific features
- [README.md](./README.md) - Project README for humans

### Testing Resources
- [Phase 1 Test Plan](./docs/content/contributing/qa/phase-1-test-plan.md) - Example comprehensive test plan
- [QA Testing Guide](./.agents/qa-testing.md) - Cross-agent QA practices
- [QA Skill](./.claude/skills/qa-tester/) - Claude Code QA automation

### AOT and Optimization Resources
- [AOT/Trimming Guide](./docs/contributing/aot-trimming-guide.md) - User-facing AOT documentation
- [AOT Optimization Guide](./.agents/aot-optimization.md) - Agent-specific AOT guidance
- [AOT Guru Skill](./.claude/skills/aot-guru/) - Claude Code AOT diagnostics and optimization
- [F# Coding Guide](./docs/contributing/fsharp-coding-guide.md) - Includes F# AOT patterns

### Morphir Resources
- Morphir Homepage: https://morphir.finos.org/
- morphir-elm: https://github.com/finos/morphir-elm
- morphir (core): https://github.com/finos/morphir
- IR Specification: [docs/spec/](./docs/spec/)

### Standards and Tools
- AGENTS.md Standard: https://agents.md
- Reqnroll (BDD): https://docs.reqnroll.net/
- TUnit (Testing): https://thomhurst.github.io/TUnit/
- Nuke (Build): https://nuke.build/
- WolverineFx: https://wolverine.netlify.app/
