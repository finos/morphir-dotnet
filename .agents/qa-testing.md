# QA Testing Guidance for AI Coding Agents

**Audience**: All AI coding agents (Claude Code, GitHub Copilot, Cursor, Windsurf, Aider, etc.)
**Purpose**: Standardized QA testing practices for morphir-dotnet

## Quick Reference

| Need | Action |
|------|--------|
| Test Plan | See [Test Plan Template](#test-plan-template) |
| Regression Test | Run `dotnet fsi .claude/skills/qa-tester/regression-test.fsx` |
| Package Validation | Run `dotnet fsi .claude/skills/qa-tester/validate-packages.fsx` |
| Report Bug | Use [Bug Report Template](#bug-report-template) |
| PR Review | Follow [PR Verification Checklist](#pr-verification-checklist) |

## Testing Philosophy

morphir-dotnet follows Test-Driven Development (TDD):
1. **RED**: Write failing test first
2. **GREEN**: Implement minimal code to pass
3. **REFACTOR**: Improve while keeping tests green

**See**: [AGENTS.md Section 9.1](../AGENTS.md#91-test-driven-development-tdd---red-green-refactor)

## Testing Stack

| Type | Framework | Location | Command |
|------|-----------|----------|---------|
| Unit Tests | TUnit | `tests/*.Tests/` | `./build.sh Test` |
| BDD Tests | Reqnroll | `tests/*/Features/*.feature` | `./build.sh Test` |
| E2E Tests | CLI Execution | `tests/Morphir.E2E.Tests/` | `./build.sh TestE2E` |
| Build Tests | Nuke | `build/Build*.cs` | `./build.sh DevWorkflow` |

## Pre-Commit Checklist

Before committing code changes:

```bash
# 1. Format code
./build.sh Format

# 2. Run linter
./build.sh Lint

# 3. Run all tests
./build.sh Test

# 4. Verify no coverage decrease
dotnet test --collect:"XPlat Code Coverage"
```

**Requirement**: All checks must pass, coverage >= 80%

## PR Verification Checklist

When reviewing a PR for QA:

- [ ] Read PR description and linked issue
- [ ] Review all PR comments for implementation decisions
- [ ] Check out PR branch locally
- [ ] Run `./build.sh DevWorkflow` (full CI simulation)
- [ ] Execute relevant test suites
- [ ] Perform manual testing of changed features
- [ ] Check for regressions in related areas
- [ ] Verify documentation updated
- [ ] Confirm test coverage hasn't decreased
- [ ] Validate package structure if packaging changed
- [ ] Sign off or file issues

## Test Plan Template

When creating a test plan for an issue or PR:

```markdown
# Test Plan: [Feature Name]

**Issue/PR**: #XXX
**Status**: Draft/Active/Complete
**Date**: YYYY-MM-DD

## Objective

[What are we testing?]

## Scope

### In Scope
- [Feature/component to test]

### Out of Scope
- [What we're not testing]

## Test Cases

### TC-001: [Test Case Name]
**Priority**: Critical/High/Medium/Low
**Type**: Unit/BDD/E2E/Integration

**Steps**:
1. [Step 1]
2. [Step 2]

**Expected Result**: [What should happen]

**Actual Result**: [What actually happened - fill during execution]

**Status**: Pass/Fail/Blocked

---

[Repeat for each test case]

## Test Execution Summary

- Total: X
- Passed: X
- Failed: X
- Blocked: X

## Issues Found

- #XXX - [Description]
```

**Example**: See [phase-1-test-plan.md](../docs/content/contributing/qa/phase-1-test-plan.md)

## Bug Report Template

When filing a bug:

```markdown
## Description
[Clear, concise description]

## Steps to Reproduce
1.
2.
3.

## Expected Behavior
[What should happen]

## Actual Behavior
[What actually happens]

## Environment
- OS: [Windows/Linux/macOS]
- .NET SDK: [version from `dotnet --version`]
- morphir-dotnet: [version or commit]
- Branch: [branch name]

## Logs/Screenshots
[Error messages, stack traces, or screenshots]

## Related Issues/PRs
- Relates to #XXX
- Introduced in PR #XXX

## Suggested Priority
[Critical/High/Medium/Low]

## Possible Root Cause
[Your analysis if available]
```

## Regression Testing Playbook

**When**: After significant changes or before releases

**Steps**:
1. Identify changed areas from git diff
2. Map changes to affected functionality
3. Run full test suite: `./build.sh Test`
4. Run E2E tests: `./build.sh TestE2E --executable-type=all`
5. Test core workflows manually:
   - Build packages: `./build.sh PackAll`
   - Publish locally: `./build.sh PublishLocalAll`
   - Install tool: `dotnet tool install -g Morphir.Tool --add-source artifacts/local-feed`
   - Run commands: `dotnet-morphir --version`, `dotnet-morphir ir verify [file]`
6. Verify backwards compatibility
7. Check for performance regressions
8. Document any issues

**Automated Script**: `dotnet fsi .claude/skills/qa-tester/regression-test.fsx`

## Package Validation Playbook

**When**: Package structure or metadata changes

**Steps**:
1. Build packages: `./build.sh PackAll`
2. Run validation: `dotnet fsi .claude/skills/qa-tester/validate-packages.fsx`
3. Manually inspect if needed:
   ```bash
   unzip -l artifacts/packages/Morphir.Tool.*.nupkg
   unzip -p artifacts/packages/Morphir.Tool.*.nupkg tools/net10.0/any/DotnetToolSettings.xml
   ```
4. Test installation:
   ```bash
   dotnet tool uninstall -g Morphir.Tool || true
   dotnet tool install -g Morphir.Tool --add-source artifacts/local-feed
   dotnet-morphir --version
   ```

## Feature Testing Playbook

**When**: New feature implemented

**Steps**:
1. Read feature requirements (issue/PRD)
2. Review BDD scenarios if available
3. Design additional test cases
4. Test happy path
5. Test edge cases
6. Test error conditions
7. Test integration with existing features
8. Verify documentation
9. Check test coverage for new code
10. Perform exploratory testing

## Build System Testing Playbook

**When**: Build system changes made

**Steps**:
1. List all targets: `./build.sh --help`
2. Test each modified target
3. Verify target dependencies
4. Check artifacts in correct locations
5. Test package generation: `./build.sh PackAll`
6. Test publishing: `./build.sh PublishLocalAll`
7. Verify tool installation
8. Test CI simulation: `./build.sh DevWorkflow`
9. Test on multiple platforms if possible
10. Verify documentation accurate

## Test Scripts

### Quick Smoke Test (2 min)
```bash
dotnet fsi .claude/skills/qa-tester/smoke-test.fsx
```

**Runs**:
- Build
- Unit tests
- Packaging
- Package count verification

---

### Full Regression Test (10-15 min)
```bash
dotnet fsi .claude/skills/qa-tester/regression-test.fsx
```

**Runs**:
- Clean
- Full CI workflow
- E2E tests
- Packaging
- Local publishing
- Tool installation

---

### Package Validation (< 30 sec)
```bash
./build.sh PackAll
dotnet fsi .claude/skills/qa-tester/validate-packages.fsx
```

**Validates**:
- All 4 packages exist
- Package sizes reasonable
- Tool package structure correct
- DotnetToolSettings.xml correct

## Test Coverage Requirements

- **Minimum**: 80% code coverage
- **Coverage must not decrease** with new changes
- Use: `dotnet test --collect:"XPlat Code Coverage"`
- View: Coverage report in `TestResults/*/coverage.cobertura.xml`

## BDD Testing with Reqnroll

### Writing Feature Files

```gherkin
Feature: [Feature name]
  As a [role]
  I want [capability]
  So that [benefit]

  Scenario: [Happy path]
    Given [precondition]
    When [action]
    Then [expected result]
    And [additional validation]

  Scenario: [Edge case]
    Given [edge case setup]
    When [action]
    Then [expected behavior]
```

### Step Definitions

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

## Unit Testing with TUnit

### Test Structure (AAA Pattern)

```csharp
[Test]
public async Task Handle_ShouldReturnValid_WhenIRIsValid()
{
    // Arrange
    var command = new VerifyIR("valid-ir.json");
    var validator = new SchemaValidator(new SchemaLoader());

    // Act
    var result = await VerifyIRHandler.Handle(command, validator, CancellationToken.None);

    // Assert
    result.IsValid.Should().BeTrue();
    result.Errors.Should().BeEmpty();
}
```

### Naming Convention

`Method_Should{Expected}_When{Condition}`

Examples:
- `Handle_ShouldReturnValid_WhenIRIsValid`
- `Validate_ShouldReturnErrors_WhenSchemaInvalid`
- `Parse_ShouldThrowException_WhenFileNotFound`

## E2E Testing

### Test Structure

```csharp
[Test]
public async Task CLI_ShouldOutputJSON_WhenJsonFlagProvided()
{
    // Arrange
    var executable = GetExecutablePath();
    var testFile = "TestData/valid-ir-v3.json";

    // Act
    var result = await RunCLI(executable, $"ir verify {testFile} --json");

    // Assert
    result.ExitCode.Should().Be(0);
    var json = JsonSerializer.Deserialize<VerifyIRResult>(result.Output);
    json.Should().NotBeNull();
    json.IsValid.Should().BeTrue();
}
```

## CI/CD Integration

### GitHub Actions Workflow

Tests run automatically on:
- Every push to PR
- Merge to main
- Release tags

**Local simulation**: `./build.sh DevWorkflow`

### Pre-Release Checklist

- [ ] All unit tests passing
- [ ] All BDD tests passing
- [ ] All E2E tests passing
- [ ] Regression tests complete
- [ ] Package validation passed
- [ ] Documentation updated
- [ ] Changelog updated
- [ ] Version bumped

## Common Issues and Solutions

### Issue: Tests fail on Windows with CS2012 (file locked)
**Solution**: Build system now handles this. If recurring, check for VBCSCompiler processes.

### Issue: E2E tests fail with "executable not found"
**Solution**: Run `./build.sh BuildE2ETests` first to build executables.

### Issue: Package validation fails
**Solution**: Run `./build.sh PackAll` to rebuild packages.

### Issue: Tool installation fails from local feed
**Solution**: Run `./build.sh PublishLocalAll` to publish to local feed first.

### Issue: Coverage report missing
**Solution**: Ensure `dotnet test --collect:"XPlat Code Coverage"` completes successfully.

## Agent-Specific Notes

### For Claude Code
- Use `@skill qa-tester` to invoke QA skill
- Test scripts are F# for cross-platform compatibility
- All scripts use Spectre.Console for rich output

### For GitHub Copilot
- Use `/test` command for test generation
- Follow TDD: test first, then implementation
- Use chat for test plan discussions

### For Cursor
- Use Composer for multi-file test creation
- Ctrl+K for inline test generation
- Use chat for test strategy

### For Windsurf / Aider / Others
- Run F# scripts directly with `dotnet fsi`
- Follow templates in this document
- Refer to AGENTS.md Section 9 for detailed testing strategy

## Resources

- **Main Testing Docs**: [AGENTS.md Section 9](../AGENTS.md#9-testing-strategy)
- **TDD Guide**: [AGENTS.md Section 9.1](../AGENTS.md#91-test-driven-development-tdd---red-green-refactor)
- **Claude QA Skill**: [.claude/skills/qa-tester/](../.claude/skills/qa-tester/)
- **Example Test Plan**: [docs/content/contributing/qa/phase-1-test-plan.md](../docs/content/contributing/qa/phase-1-test-plan.md)
- **Reqnroll Docs**: https://docs.reqnroll.net/
- **TUnit Docs**: https://thomhurst.github.io/TUnit/

## Updates and Maintenance

This document should be updated when:
- New test types added
- Testing tools changed
- New playbooks needed
- Common issues discovered
- Test scripts updated

**Last Updated**: 2025-12-18
**Maintained By**: QA contributors and AI coding agents
