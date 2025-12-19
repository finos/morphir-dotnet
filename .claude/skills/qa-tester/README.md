# QA Tester Skill

Specialized QA testing skill for the morphir-dotnet project.

## Overview

This skill provides comprehensive QA testing capabilities including:
- Test plan design and execution
- Regression testing
- End-to-end testing
- Package validation
- Issue reporting
- Test automation

## Files

- **SKILL.md** - Main skill prompt with testing guidelines and playbooks
- **smoke-test.fsx** - Quick smoke test (F# script)
- **regression-test.fsx** - Full regression test suite (F# script)
- **validate-packages.fsx** - NuGet package structure validation (F# script)

## Usage

### Running Test Scripts

All test scripts are F# scripts that can be executed with `dotnet fsi`:

```bash
# Quick smoke test (~2 minutes)
dotnet fsi .claude/skills/qa-tester/smoke-test.fsx

# Full regression test (~10-15 minutes)
dotnet fsi .claude/skills/qa-tester/regression-test.fsx

# Validate NuGet packages
./build.sh PackAll  # First build packages
dotnet fsi .claude/skills/qa-tester/validate-packages.fsx
```

### Using the QA Skill

To invoke the QA skill in Claude Code:

```
@skill qa-tester
Please create a test plan for PR #XXX
```

or

```
@skill qa-tester
Run regression tests for the build system changes
```

## Test Scripts Details

### smoke-test.fsx

Quick sanity check that runs:
1. Build (Compile target)
2. Unit tests (Test target)
3. Packaging (PackAll target)
4. Package count verification (expects 4 packages)

**Exit codes:**
- 0: All tests passed
- 1: One or more tests failed

**Duration:** ~2 minutes

---

### regression-test.fsx

Comprehensive regression test suite that runs:
1. Clean build artifacts
2. Full CI workflow (DevWorkflow)
3. Build E2E tests
4. Run E2E tests
5. Package all projects
6. Publish to local NuGet feed
7. Test tool installation from local feed
8. Verify tool executes correctly
9. Cleanup

**Exit codes:**
- 0: All tests passed
- 1: One or more tests failed

**Duration:** ~10-15 minutes

**Features:**
- Progress indicators with spinners
- Detailed step-by-step output
- Summary table at the end
- Timing information

---

### validate-packages.fsx

Validates NuGet package structure and metadata:
1. Checks all 4 packages exist (Core, Tooling, Morphir, Tool)
2. Validates package sizes
3. For Morphir.Tool:
   - Verifies DotnetToolSettings.xml exists
   - Checks CommandName = "dotnet-morphir"
   - Checks EntryPoint = "dotnet-morphir.dll"
   - Validates required assemblies present
4. Generates validation report

**Exit codes:**
- 0: All validations passed
- 1: One or more validations failed

**Duration:** < 30 seconds

**Output:** Formatted table with validation results

---

## Dependencies

All scripts use:
- **Spectre.Console** - For rich terminal output (progress bars, tables, colors)
- **.NET 10 SDK** - Required for F# script execution

Dependencies are automatically downloaded via NuGet when scripts run.

### Troubleshooting Script Dependencies

If you encounter errors about missing packages when running scripts:

```bash
# Option 1: Let F# Interactive download packages (usually automatic)
dotnet fsi .claude/skills/qa-tester/smoke-test.fsx

# Option 2: Pre-restore NuGet packages
dotnet tool restore
dotnet restore

# Option 3: Manually restore F# script packages
dotnet fsi --langversion:preview --define:SCRIPTING \
  .claude/skills/qa-tester/smoke-test.fsx
```

**Note for Claude Code Users**: Claude skills may have limitations installing packages during execution. If scripts fail with package errors:
1. Run `dotnet tool restore` in the project root first
2. Run `dotnet restore` to cache NuGet packages locally
3. Then retry the script

**Note for Other AI Agents**: These scripts use standard F# scripting with NuGet package references (`#r "nuget: ..."`), which should work in most AI coding environments that support F# script execution.

## Integration with Build System

The QA scripts complement the Nuke build system:

| QA Script | Build Targets Used |
|-----------|-------------------|
| smoke-test.fsx | Compile, Test, PackAll |
| regression-test.fsx | Clean, DevWorkflow, BuildE2ETests, TestE2E, PackAll, PublishLocalAll |
| validate-packages.fsx | (none - inspects artifacts) |

## Best Practices

1. **Run smoke test** before committing changes
2. **Run regression test** before creating PRs
3. **Run package validation** after packaging changes
4. **Use the skill** for test plan design and issue reporting
5. **Keep scripts updated** as the project evolves

## Extending the Skill

To add new test scenarios:

1. Add playbook to **SKILL.md**
2. Create new F# script if needed
3. Update this README
4. Document in test plan template

## Examples

### Create Test Plan for PR
```
@skill qa-tester
Create a comprehensive test plan for PR #214 which refactored the build system into vertical slices.
```

### Report Bug
```
@skill qa-tester
The PackAll target fails with a directory not found error. Help me create a detailed bug report.
```

### Run Specific Test
```
@skill qa-tester
Run the package validation tests and report any issues.
```

## See Also

- [Phase 1 Test Plan](../../../docs/content/contributing/qa/phase-1-test-plan.md) - Example comprehensive test plan
- [AGENTS.md](../../../AGENTS.md) - Section 9: Testing Strategy
- [CLAUDE.md](../../../CLAUDE.md) - Section 2: TDD Red-Green-Refactor
