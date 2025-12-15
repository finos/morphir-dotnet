# AGENTS.md

Guidance for AI coding agents contributing to finos/morphir-dotnet (a .NET
binding for the Morphir ecosystem). Produce correct, minimal, and well‑tested
changes aligned with Morphir IR and tooling.

Project links
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

Commands
- .NET (C# 14 / F#, .NET 10)
    - Build: `dotnet build`
    - Test (TUnit + Reqnroll): `dotnet test --nologo`
    - Format: `dotnet format`
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

## 14) Known Issues / TODOs

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
