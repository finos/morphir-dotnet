---
title: "QA & Testing"
linkTitle: "QA & Testing"
weight: 20
description: "Test plans, quality assurance practices, and testing documentation"
---

This section contains quality assurance documentation, test plans, and testing practices for Morphir .NET.

## Test Plans

| Document | Description |
|----------|-------------|
| [Phase 1 Test Plan](phase-1-test-plan) | Initial test plan for Phase 1 features |
| [Copilot Skill Emulation Test Plan](copilot-skill-emulation-test-plan) | BDD scenarios for GitHub Copilot skill emulation |

## Test Reports

| Document | Description |
|----------|-------------|
| [Copilot Skill Emulation Execution Report](copilot-skill-emulation-execution-report) | Results from skill emulation testing |
| [Copilot Scenarios Runner](copilot-scenarios-runner) | Automated scenario execution documentation |

## Testing Practices

### Test-Driven Development (TDD)

All development in morphir-dotnet follows TDD:

1. **RED**: Write a failing test first
2. **GREEN**: Write minimal code to pass the test
3. **REFACTOR**: Improve code while keeping tests green

### Test Types

| Type | Framework | Purpose |
|------|-----------|---------|
| Unit Tests | TUnit | Individual component testing |
| BDD Tests | Reqnroll | Behavior specification and acceptance |
| Property Tests | FsCheck | Property-based verification |
| Integration Tests | TUnit | Cross-component integration |

### Coverage Requirements

- **Minimum Coverage**: 80% for all new code
- **Critical Paths**: 100% coverage for IR handling, validation, and CLI commands
- **Regression Prevention**: All bug fixes require accompanying tests

## Running Tests

```bash
# Run all tests
dotnet test --nologo

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/Morphir.Core.Tests
```

## Related Resources

- [QA Tester Skill]({{< ref "/docs/contributing/design/guru-creation-guide" >}}) - AI skill for test planning
- [AGENTS.md](https://github.com/finos/morphir-dotnet/blob/main/AGENTS.md) - TDD requirements
