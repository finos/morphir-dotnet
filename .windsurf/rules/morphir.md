# Windsurf Rules for morphir-dotnet

This project uses **AGENTS.md** as the primary guidance file for all AI coding agents.

## 📚 Primary Guidance

**Start here**: [AGENTS.md](../../AGENTS.md)

AGENTS.md is the authoritative source containing:
- Project overview and architecture
- Agent scope and responsibilities
- Build, test, and deployment procedures
- Coding conventions and standards
- Morphir-specific modeling guidelines
- Testing strategy with TDD practices
- Decision policies and escalation rules
- Specialized guidance links

## 🎯 Specialized Topics

For domain-specific deep dives, see [.agents/](../../.agents/) directory:

### QA Testing
**File**: [.agents/qa-testing.md](../../.agents/qa-testing.md)

Comprehensive QA guidance including:
- Pre-commit and PR verification checklists
- Test plan templates and bug report templates
- Regression, feature, build, and package testing playbooks
- BDD (Reqnroll) and unit testing (TUnit) standards
- Test coverage requirements (>= 80%)
- F# test automation scripts

### Future Topics
The `.agents/` directory will expand to include:
- Deployment and release management
- Security testing and compliance
- Documentation and ADR writing
- Performance testing and benchmarking

## 🚀 Quick Reference

### Essential Commands

```bash
# Format code
./build.sh Format

# Run linter
./build.sh Lint

# Run all tests
./build.sh Test

# Full CI workflow simulation
./build.sh DevWorkflow

# Package everything
./build.sh PackAll
```

### Testing Workflow

```bash
# Quick smoke test (~2 minutes)
dotnet fsi .claude/skills/qa-tester/smoke-test.fsx

# Full regression test (~10-15 minutes)
dotnet fsi .claude/skills/qa-tester/regression-test.fsx

# Validate packages
./build.sh PackAll
dotnet fsi .claude/skills/qa-tester/validate-packages.fsx
```

## 🧪 Test-Driven Development (TDD)

**Required**: Follow RED → GREEN → REFACTOR cycle (AGENTS.md Section 9.1)

1. **RED**: Write failing test first
   - BDD scenario (Gherkin) for features
   - Unit test (TUnit) for components
   - Run tests, confirm they fail

2. **GREEN**: Implement minimal code to pass
   - Focus on making tests pass
   - Don't over-engineer

3. **REFACTOR**: Improve while keeping tests green
   - Clean up implementation
   - Remove duplication
   - Run tests after each change

### Testing Frameworks
- **Unit Tests**: TUnit (in `tests/*.Tests/`)
- **BDD Tests**: Reqnroll (Gherkin features in `tests/*/Features/*.feature`)
- **E2E Tests**: CLI execution tests (in `tests/Morphir.E2E.Tests/`)

## 🏗️ Coding Principles

From AGENTS.md Section 5:

### Immutability First
```csharp
// ✅ Good: Immutable record
public readonly record struct TypeId(string Value);

// ❌ Bad: Mutable class
public class TypeId { public string Value { get; set; } }
```

### No Nulls - Use Option<T>
```csharp
// ✅ Good: Option type
public Option<User> FindUser(string id);

// ❌ Bad: Nullable reference
public User? FindUser(string id);
```

### Make Illegal States Unrepresentable
```csharp
// ✅ Good: ADT with exhaustive cases
public abstract record ValidationResult
{
    public sealed record Valid(Document Document) : ValidationResult;
    public sealed record Invalid(List<ValidationError> Errors) : ValidationResult;
}

// ❌ Bad: Boolean flags
public class ValidationResult
{
    public bool IsValid { get; set; }
    public Document? Document { get; set; }
    public List<ValidationError>? Errors { get; set; }
}
```

### Pure Domain, Effects at Edges
```csharp
// ✅ Good: Pure function
public static ValidationResult Validate(Document doc, Schema schema);

// ❌ Bad: Side effects in domain
public async Task<ValidationResult> Validate(Document doc)
{
    await _logger.LogAsync("Validating..."); // Side effect!
    // ...
}
```

## 🔧 CLI Logging Standard

**CRITICAL**: CLI tools must never write logs to stdout

```csharp
// ✅ Good: Serilog configured to stderr
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateLogger();

// ❌ Bad: Console.WriteLine for logs
Console.WriteLine("Processing..."); // This goes to stdout!
```

**Why**:
- Stdout is for command output only (JSON, formatted results)
- Stderr is for diagnostics and logging
- Enables: `./morphir ir verify file.json --json | jq .`

See AGENTS.md Section 5 for complete details.

## ⚠️ Escalation Rules

**DO NOT** implement without human approval:
- Breaking public API changes
- IR/JSON compatibility changes without ADR
- Security/auth/crypto changes
- Destructive migrations

See AGENTS.md Section 2 for complete escalation rules.

## 📁 Project Structure

```
morphir-dotnet/
├── AGENTS.md                 # ← Primary guidance (START HERE)
├── .agents/                  # ← Specialized topics
│   ├── qa-testing.md         # QA and testing guidance
│   └── README.md             # Navigation and index
├── .windsurf/                # ← You are here
│   └── rules/morphir.md      # This file
├── .cursorrules              # Cursor pointer
├── .github/
│   └── copilot-instructions.md  # Copilot pointer
├── CLAUDE.md                 # Claude Code pointer
├── src/
│   ├── Morphir/              # CLI/host application
│   ├── Morphir.Core/         # Core domain model
│   ├── Morphir.Tooling/      # Tooling services (WolverineFx)
│   └── Morphir.Tool/         # Dotnet tool package
├── tests/
│   ├── Morphir.Core.Tests/
│   ├── Morphir.Tooling.Tests/
│   │   └── Features/         # BDD feature files
│   └── Morphir.E2E.Tests/
├── build/
│   ├── Build.cs              # Main build entry
│   ├── Build.Packaging.cs    # Package targets
│   ├── Build.Publishing.cs   # Publish targets
│   ├── Build.Testing.cs      # Test targets
│   └── Build.CI.cs           # CI simulation
├── docs/
│   ├── content/contributing/qa/  # Test plans
│   └── spec/                 # IR specifications
└── .claude/
    └── skills/qa-tester/     # F# test automation scripts
```

## 🛠️ Tech Stack

- **Languages**: C# 14, .NET 10 (F# for ADT-heavy components)
- **Testing**: TUnit (unit), Reqnroll (BDD), CLI execution (E2E)
- **Build**: Nuke build system (strongly-typed C#)
- **Messaging**: WolverineFx (command handlers)
- **Persistence**: Marten (PostgreSQL document store)
- **CLI**: System.CommandLine
- **Logging**: Serilog (stderr only for CLI tools)
- **Serialization**: System.Text.Json with source generators

## 🔍 Windsurf-Specific Notes

### Cascade Mode
When using Windsurf's Cascade mode:
- **Works well with**: Multi-file changes, complex refactorings
- **Read first**: AGENTS.md Sections 1-5 for context
- **Follow**: TDD cycle (RED → GREEN → REFACTOR)
- **Run tests**: After each Cascade iteration

### Rules Activation
This rules file can be activated:
- **Manual**: Reference explicitly in your prompts
- **Always-on**: Configure in Windsurf settings
- **Model-driven**: Based on descriptive criteria
- **Glob pattern**: For specific subdirectories

### Workflow Integration
Windsurf workflows saved as markdown prompts should:
1. Reference AGENTS.md for project conventions
2. Follow TDD practices from Section 9.1
3. Use .agents/qa-testing.md for testing guidance
4. Run formatters and tests before completion

### Best Practices
Create reusable Windsurf workflows for:
- **Create Test Plan**: Use .agents/qa-testing.md template
- **Implement Feature**: Follow TDD cycle
- **Run Quality Checks**: Format → Lint → Test
- **Package and Validate**: PackAll → validate-packages.fsx

## 📖 Resources

### Documentation
- **Primary**: [AGENTS.md](../../AGENTS.md) - Comprehensive guidance
- **QA Testing**: [.agents/qa-testing.md](../../.agents/qa-testing.md) - Testing practices
- **Claude-Specific**: [CLAUDE.md](../../CLAUDE.md) - If interested in Claude patterns
- **Test Plan Example**: [docs/content/contributing/qa/phase-1-test-plan.md](../../docs/content/contributing/qa/phase-1-test-plan.md)

### External Resources
- **Morphir Homepage**: https://morphir.finos.org/
- **morphir-elm**: https://github.com/finos/morphir-elm
- **morphir (core)**: https://github.com/finos/morphir
- **AGENTS.md Standard**: https://agents.md
- **Reqnroll Docs**: https://docs.reqnroll.net/
- **TUnit Docs**: https://thomhurst.github.io/TUnit/
- **Nuke Build**: https://nuke.build/
- **WolverineFx**: https://wolverine.netlify.app/

## 💡 Quick Tips

### Before Starting Any Task
1. Read relevant section of AGENTS.md
2. For testing: Read .agents/qa-testing.md
3. Run `./build.sh --help` to see all targets
4. Check git status and current branch

### Before Committing
```bash
# Pre-commit checklist
./build.sh Format              # Format code
./build.sh Lint                # Check for issues
./build.sh Test                # Run all tests
dotnet test --collect:"XPlat Code Coverage"  # Verify coverage
```

### When Stuck
1. Check AGENTS.md Section 2 for escalation rules
2. Review .agents/qa-testing.md for testing questions
3. Look at existing tests for patterns
4. Check PRD documents in docs/content/contributing/design/prds/

### When Creating New Features
1. Write BDD scenario first (RED)
2. Write unit tests (RED)
3. Implement (GREEN)
4. Refactor (REFACTOR)
5. Update documentation
6. Run full test suite

## 🎯 Success Criteria

For every change:
- [ ] Tests written first (TDD)
- [ ] All tests passing
- [ ] Code formatted (`./build.sh Format`)
- [ ] Linter passing (`./build.sh Lint`)
- [ ] Coverage >= 80%
- [ ] Documentation updated
- [ ] AGENTS.md conventions followed

---

**Navigation**:
- Questions about project? → Read [AGENTS.md](../../AGENTS.md)
- Testing task? → Read [.agents/qa-testing.md](../../.agents/qa-testing.md)
- Build question? → Run `./build.sh --help`
- Need examples? → Check existing tests and PRDs

**Remember**: This file provides Windsurf-specific pointers. For complete guidance, always refer to AGENTS.md and specialized topic guides in .agents/.
