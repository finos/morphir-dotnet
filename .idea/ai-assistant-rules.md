# JetBrains AI Assistant Rules for morphir-dotnet

This project uses **AGENTS.md** as the primary guidance file for all AI coding agents.

## 📚 Required Reading

**Primary Guidance**: [AGENTS.md](../AGENTS.md)

AGENTS.md is the authoritative source for this project, containing:
- Project overview and architecture (Section 1)
- Agent scope and responsibilities (Section 2)
- Repository structure (Section 3)
- Build, test, and deployment procedures (Section 4)
- Coding conventions and standards (Section 5)
- Morphir-specific modeling guidelines (Section 6)
- Testing strategy with TDD practices (Sections 9, 9.1)
- Decision policies and escalation rules (Section 10)
- Specialized guidance links (Section 18)
- Resources and references (Section 19)

## 🎯 Specialized Topics

For domain-specific guidance, see [.agents/](../.agents/) directory:

### QA Testing & Test Plans
**File**: [.agents/qa-testing.md](../.agents/qa-testing.md)

Use this when:
- Creating test plans
- Writing tests (BDD, unit, E2E)
- Performing QA review
- Running regression tests
- Reporting bugs

Contains:
- Pre-commit and PR verification checklists
- Test plan and bug report templates
- Regression, feature, build, and package testing playbooks
- BDD (Reqnroll) and unit testing (TUnit) guides
- Test coverage requirements (>= 80%)
- F# test automation scripts

### Future Topics
Additional .agents/ guides coming for:
- Deployment and release management
- Security testing and compliance
- Documentation and ADR writing
- Performance testing

## 🚀 Quick Reference

### Essential Build Commands

```bash
# Format code
./build.sh Format

# Run linter
./build.sh Lint

# Run all tests
./build.sh Test

# Full CI workflow (Restore → Lint → Compile → Test)
./build.sh DevWorkflow

# Package all projects
./build.sh PackAll
```

### Pre-Commit Checklist

```bash
# Required before every commit
./build.sh Format                           # Format code
./build.sh Lint                             # Check for issues
./build.sh Test                             # Run all tests
dotnet test --collect:"XPlat Code Coverage" # Verify coverage >= 80%
```

## 🧪 Test-Driven Development (Required)

**Critical**: Follow TDD RED → GREEN → REFACTOR cycle (AGENTS.md Section 9.1)

### 1. RED Phase - Write Failing Test
```csharp
// Example: BDD scenario (Reqnroll/Gherkin)
// File: tests/Morphir.Tooling.Tests/Features/VerifyIR/VerifyIR.feature
Feature: IR Schema Verification
  Scenario: Valid IR file passes validation
    Given a valid IR v3 file "valid-ir-v3.json"
    When I verify the IR file
    Then the validation should succeed

// Example: Unit test (TUnit)
[Test]
public async Task Handle_ShouldReturnValid_WhenIRIsValid()
{
    // Arrange
    var command = new VerifyIR("valid-ir.json");

    // Act
    var result = await handler.Handle(command, validator, ct);

    // Assert - Test FAILS (handler doesn't exist yet)
    result.IsValid.Should().BeTrue();
}
```

### 2. GREEN Phase - Minimal Implementation
```csharp
// Implement just enough to make test pass
public static class VerifyIRHandler
{
    public static Task<VerifyIRResult> Handle(VerifyIR command, ...)
    {
        // Minimal implementation
        return Task.FromResult(new VerifyIRResult(IsValid: true, ...));
    }
}
// Test now PASSES
```

### 3. REFACTOR Phase - Improve Design
```csharp
// Clean up while keeping tests green
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
// Tests still PASS
```

## 🏗️ Coding Standards

From AGENTS.md Section 5:

### 1. Immutability First
```csharp
// ✅ GOOD: Immutable record
public readonly record struct PackageName(string Value)
{
    public static PackageName Create(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Package name cannot be empty")
            : new PackageName(value);
}

// ❌ BAD: Mutable class
public class PackageName
{
    public string Value { get; set; }
}
```

### 2. No Nulls - Use Option<T>
```csharp
// ✅ GOOD: Option type for optional values
public Option<Document> FindDocument(string id);

// ❌ BAD: Nullable reference (allow only for external APIs)
public Document? FindDocument(string id);
```

### 3. Make Illegal States Unrepresentable (ADTs)
```csharp
// ✅ GOOD: Explicit ADT (Algebraic Data Type)
public abstract record Result<T, TError>
{
    public sealed record Success(T Value) : Result<T, TError>;
    public sealed record Failure(TError Error) : Result<T, TError>;
}

// Use exhaustive pattern matching
return result switch
{
    Success(var value) => ProcessValue(value),
    Failure(var error) => HandleError(error),
    // Compiler ensures all cases covered
};

// ❌ BAD: Boolean flags and nullable fields
public class Result<T>
{
    public bool IsSuccess { get; set; }
    public T? Value { get; set; }
    public string? Error { get; set; }
}
```

### 4. Pure Domain, Effects at Edges
```csharp
// ✅ GOOD: Pure function in domain
namespace Morphir.Core.Domain;
public static ValidationResult Validate(Document doc, Schema schema)
{
    // Pure logic, no I/O, no logging
    return schema.ValidateDocument(doc);
}

// Adapter handles effects
namespace Morphir.Tooling.Adapters;
public async Task<ValidationResult> ValidateWithLogging(Document doc)
{
    _logger.Information("Starting validation"); // Effect
    var result = Validate(doc, _schema);        // Pure call
    _logger.Information("Validation complete"); // Effect
    return result;
}

// ❌ BAD: Side effects in domain
public async Task<ValidationResult> Validate(Document doc)
{
    await _logger.LogAsync("Validating"); // Side effect in domain!
    // ...
}
```

### 5. Exhaustive Pattern Matching
```csharp
// ✅ GOOD: Exhaustive matching (no default)
public string FormatError(ValidationError error) => error switch
{
    SchemaError se => $"Schema error: {se.Message}",
    TypeError te => $"Type error: {te.Expected} vs {te.Actual}",
    ParseError pe => $"Parse error at {pe.Location}",
    // Compiler error if new case added to ValidationError
};

// ❌ BAD: Default case (hides missing cases)
public string FormatError(ValidationError error) => error switch
{
    SchemaError se => $"Schema error: {se.Message}",
    _ => "Unknown error" // Hides bugs when new error types added
};
```

## 🔧 CLI Logging Standard (Critical)

**Rule**: CLI tools must NEVER write log messages to stdout

```csharp
// ✅ GOOD: Serilog configured to stderr only
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(
        standardErrorFromLevel: LogEventLevel.Verbose,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

// ✅ GOOD: Structured output to stdout
var result = new VerifyIRResult(...);
Console.WriteLine(JsonSerializer.Serialize(result));

// ❌ BAD: Log messages to stdout
Console.WriteLine("Processing file..."); // Breaks: morphir verify file.json | jq .
```

**Why**:
- Stdout = command output (JSON, formatted data)
- Stderr = diagnostics and logging
- Enables: `./morphir ir verify file.json --json | jq .`

See AGENTS.md Section 5 for complete rationale.

## ⚠️ Escalation Rules

**DO NOT** implement without explicit human approval:

1. Breaking public API changes
2. IR/JSON compatibility changes without ADR
3. Security/authentication/cryptography changes
4. Destructive data migrations
5. Changes to CI/CD workflows

See AGENTS.md Section 2 for complete escalation policy.

## 📁 Project Structure

```
morphir-dotnet/
├── AGENTS.md                 # ← Primary guidance (START HERE)
├── .agents/                  # ← Specialized topics
│   ├── qa-testing.md         # QA and testing
│   └── README.md             # Navigation
├── .idea/                    # ← JetBrains config
│   └── ai-assistant-rules.md # This file
├── src/
│   ├── Morphir/              # CLI/host (C# 14)
│   ├── Morphir.Core/         # Core domain (C# 14)
│   ├── Morphir.Tooling/      # Tooling (C# 14, WolverineFx)
│   └── Morphir.Tool/         # Dotnet tool package
├── tests/
│   ├── Morphir.Core.Tests/         # Unit tests
│   ├── Morphir.Tooling.Tests/      # Unit + BDD tests
│   │   └── Features/               # Gherkin feature files
│   └── Morphir.E2E.Tests/          # End-to-end CLI tests
├── build/
│   ├── Build.cs                    # Main build entry
│   ├── Build.Packaging.cs          # Package targets
│   ├── Build.Publishing.cs         # Publish targets
│   ├── Build.Testing.cs            # Test targets
│   └── Build.CI.cs                 # CI workflow simulation
└── docs/
    ├── content/contributing/qa/    # Test plans
    └── spec/                       # IR specifications
```

## 🛠️ Tech Stack

- **Languages**: C# 14, .NET 10 SDK (F# for ADT-heavy components)
- **Testing**: TUnit (unit tests), Reqnroll (BDD), CLI execution (E2E)
- **Build**: Nuke build system (strongly-typed C# build orchestration)
- **Messaging**: WolverineFx (command/message handlers)
- **Persistence**: Marten (PostgreSQL document store)
- **CLI**: System.CommandLine
- **Logging**: Serilog (stderr-only for CLI tools)
- **Serialization**: System.Text.Json with source generators (AOT-compatible)

## 🎯 JetBrains-Specific Tips

### Using Rules in AI Assistant

This rules file can be applied:
- **Always**: Applied to all chat sessions automatically
- **Manually**: Reference with @rule or #rule in chat
- **By model decision**: AI decides when relevant
- **By file patterns**: Applied based on file being edited

### Recommended Rule Type

Configure this file as **"Always"** in Settings → AI Assistant → Rules, so AI Assistant always considers morphir-dotnet conventions.

### Creating Custom Prompts

When creating custom prompts in JetBrains AI Assistant:

**Test Plan Creation**:
```
Create a test plan following .agents/qa-testing.md template
for the feature described in [issue/PR description]
```

**TDD Feature Implementation**:
```
Implement [feature] using TDD:
1. Write BDD scenario (Gherkin)
2. Write failing unit test (RED)
3. Implement (GREEN)
4. Refactor
Follow AGENTS.md Section 9.1
```

**Quality Check**:
```
Run pre-commit checks:
./build.sh Format
./build.sh Lint
./build.sh Test
```

### Code Analysis Integration

JetBrains AI Assistant integrates with:
- ReSharper code analysis
- IntelliJ inspections
- .editorconfig settings

Ensure code follows these before requesting AI assistance.

## 📖 Resources

### Documentation
- **Primary**: [AGENTS.md](../AGENTS.md) - Complete guidance
- **QA Testing**: [.agents/qa-testing.md](../.agents/qa-testing.md) - Testing practices
- **Test Plan Example**: [docs/content/contributing/qa/phase-1-test-plan.md](../docs/content/contributing/qa/phase-1-test-plan.md)

### External Resources
- **Morphir Homepage**: https://morphir.finos.org/
- **morphir-elm**: https://github.com/finos/morphir-elm
- **morphir (core)**: https://github.com/finos/morphir
- **AGENTS.md Standard**: https://agents.md
- **Reqnroll (BDD)**: https://docs.reqnroll.net/
- **TUnit**: https://thomhurst.github.io/TUnit/
- **Nuke Build**: https://nuke.build/
- **WolverineFx**: https://wolverine.netlify.app/

## 💡 Quick Tips

### Before Starting
1. Read relevant AGENTS.md sections
2. For testing: Read .agents/qa-testing.md
3. Check `./build.sh --help` for available targets
4. Review git status and current branch

### Before Committing
```bash
./build.sh Format                           # Format
./build.sh Lint                             # Lint
./build.sh Test                             # Test
dotnet test --collect:"XPlat Code Coverage" # Coverage
```

### When Stuck
1. Check AGENTS.md Section 2 for escalation
2. Review .agents/qa-testing.md for testing
3. Look at existing tests for patterns
4. Check PRDs in docs/content/contributing/design/prds/

## ✅ Success Criteria

Every change must:
- [ ] Follow TDD (tests first)
- [ ] All tests passing
- [ ] Code formatted
- [ ] Linter passing
- [ ] Coverage >= 80%
- [ ] Documentation updated
- [ ] AGENTS.md conventions followed

---

**Rule Configuration**: In JetBrains IDE, go to Settings → AI Assistant → Rules and configure this file as "Always" for automatic application.

**Navigation**:
- Project questions? → Read [AGENTS.md](../AGENTS.md)
- Testing task? → Read [.agents/qa-testing.md](../.agents/qa-testing.md)
- Build question? → Run `./build.sh --help`

**Remember**: This file provides JetBrains-specific pointers. For complete guidance, always refer to AGENTS.md and specialized guides in .agents/.
