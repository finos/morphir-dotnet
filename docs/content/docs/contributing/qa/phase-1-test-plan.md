---
title: "Phase 1 Test Plan"
linkTitle: "Phase 1 Test Plan"
description: "Test plan for Phase 1 of the Deployment Architecture Refactor"
weight: 10
---

# Phase 1 Test Plan: Project Structure & Build Organization

**Issue**: #209
**PR**: #214
**Status**: Merged to `main` (commit 331e327)
**Test Plan Date**: 2025-12-18

## Executive Summary

This test plan validates the complete and correct implementation of Phase 1 of the Deployment Architecture Refactor epic (#208). Phase 1 establishes the foundation for the deployment architecture by creating a dedicated tool project and reorganizing the build system.

## Test Objectives

1. **Verify Morphir.Tool project** is correctly configured as a dotnet tool
2. **Validate build system refactoring** using vertical slice architecture
3. **Confirm deprecated code removal** without breaking existing functionality
4. **Test CI workflow simulation** targets work locally
5. **Verify package generation** for all four packages (Core, Tooling, Morphir, Tool)
6. **Validate Windows build fixes** resolve file locking issues
7. **Confirm documentation completeness** for all build targets

## Scope

### In Scope
- All tasks from issue #209
- All changes from PR #214
- Verification of BDD acceptance tests from issue #209
- Validation of verification checklist from issue #209
- Testing requirements from issue #209
- Definition of Done criteria from issue #209

### Out of Scope
- Phase 2 and Phase 3 features (separate issues)
- Runtime behavior of generated packages (covered by E2E tests)
- Performance benchmarking (not required for Phase 1)

## Implementation Analysis

### Changes Implemented

PR #214 implemented the following changes:

1. **New Morphir.Tool Project** (Task 1.1)
   - Location: `src/Morphir.Tool/`
   - AssemblyName: `dotnet-morphir`
   - ToolCommandName: `dotnet-morphir` (follows dotnet convention)
   - PackageId: `Morphir.Tool`
   - Delegates to `Morphir.Program.Main()` (no code duplication)

2. **Updated Morphir Project** (Task 1.2)
   - AssemblyName: `morphir` (lowercase)
   - IsPackable: `true` (changed from original plan to support NuGet/GitHub releases)
   - Made `Program` class public for delegation
   - AOT and trimming settings preserved

3. **Build.cs Split** (Task 1.3)
   - `build/Build.Packaging.cs` - PackLibs, PackTool, PackAll
   - `build/Build.Publishing.cs` - PublishLibs, PublishTool, PublishAll, PublishLocal*
   - `build/Build.Testing.cs` - Test, BuildE2ETests, TestE2E, GenerateWolverineCode
   - `build/Build.CI.cs` - CILint, CITest, DevWorkflow (NEW)
   - `build/Build.cs` - Core targets (Clean, Restore, Compile, CI, etc.)

4. **Helper Classes** (Task 1.4)
   - **Status**: NOT IMPLEMENTED
   - **Rationale**: Deferred as unnecessary at this stage
   - **Impact**: None, targets work without helpers

5. **Deprecated Code Removal** (Task 1.5)
   - Removed `scripts/pack-tool-platform.cs`
   - Removed `scripts/build-tool-dll.cs`
   - Updated `NUKE_MIGRATION.md`
   - Updated `README.md`

6. **Build Targets Updated** (Task 1.6)
   - PackTool builds `Morphir.Tool.csproj`
   - PublishTool uses `Morphir.Tool.*.nupkg` glob
   - Added `.After(PackLibs)` to prevent directory conflicts
   - All 23+ targets documented with XML comments

7. **Windows Build Fixes** (Additional)
   - Removed problematic `GenerateWolverineCode` MSBuild target
   - Created Nuke-based `GenerateWolverineCode` target
   - Re-enabled parallel builds
   - Fixed circular build dependencies

8. **CI Workflow Simulation** (Additional)
   - `DevWorkflow` - Complete CI pipeline locally
   - `CILint` - Lint checks only
   - `CITest` - Build and tests only

### Deviations from Original Plan

| Original Requirement | Implementation | Rationale |
|---------------------|----------------|-----------|
| `IsPackable=false` for Morphir | `IsPackable=true` | Support NuGet/GitHub releases alongside AOT executables |
| `morphir` tool name | `dotnet-morphir` | Follow standard dotnet tool naming convention |
| Helper classes in `build/Helpers/` | Not implemented | Deferred as unnecessary, can add later if needed |
| VBCSCompiler killing | Removed | Root cause fixed by removing problematic MSBuild target |
| `BuildInParallel=false` | Removed | Parallel builds re-enabled after fixing root cause |

## Test Plan

### 1. Project Structure Tests

#### 1.1 Morphir.Tool Project Verification

**Test ID**: PST-001
**Priority**: Critical
**Type**: Structural

**Test Steps**:
```bash
# 1. Verify project file exists and has correct settings
cat src/Morphir.Tool/Morphir.Tool.csproj | grep -E "(PackAsTool|ToolCommandName|PackageId|AssemblyName)"

# 2. Verify Program.cs delegates to Morphir.Program
cat src/Morphir.Tool/Program.cs

# 3. Verify project is in solution
grep "Morphir.Tool" Morphir.slnx
```

**Expected Results**:
- `PackAsTool=true`
- `ToolCommandName=dotnet-morphir`
- `PackageId=Morphir.Tool`
- `AssemblyName=dotnet-morphir`
- Program.cs contains `return Morphir.Program.Main(args);`
- Project referenced in solution file

**Acceptance Criteria**: All settings correct, no code duplication

---

#### 1.2 Morphir Project Verification

**Test ID**: PST-002
**Priority**: Critical
**Type**: Structural

**Test Steps**:
```bash
# 1. Verify AssemblyName is lowercase
grep "AssemblyName" src/Morphir/Morphir.csproj

# 2. Verify IsPackable is true
grep "IsPackable" src/Morphir/Morphir.csproj

# 3. Verify Program class is public
grep "public.*class Program" src/Morphir/Program.cs

# 4. Verify AOT settings preserved
grep -E "(PublishAot|IsAotCompatible)" src/Morphir/Morphir.csproj
```

**Expected Results**:
- `AssemblyName=morphir`
- `IsPackable=true`
- `public class Program` or `public partial class Program`
- AOT settings still present

**Acceptance Criteria**: Morphir can be packaged and deployed

---

#### 1.3 Build System Split Verification

**Test ID**: PST-003
**Priority**: Critical
**Type**: Structural

**Test Steps**:
```bash
# 1. Verify partial class files exist
ls -la build/Build*.cs

# 2. Verify Build class is partial
grep "partial.*class Build" build/Build.cs

# 3. Verify targets in correct files
grep "Target.*Pack" build/Build.Packaging.cs
grep "Target.*Publish" build/Build.Publishing.cs
grep "Target.*Test" build/Build.Testing.cs
grep "Target.*CI" build/Build.CI.cs

# 4. Verify all targets accessible
./build.sh --help | grep -E "(Pack|Publish|Test|CI)"
```

**Expected Results**:
- 5 Build*.cs files exist (Build.cs, Build.Packaging.cs, Build.Publishing.cs, Build.Testing.cs, Build.CI.cs)
- Build class declared as `partial`
- Packaging targets in Build.Packaging.cs
- Publishing targets in Build.Publishing.cs
- Testing targets in Build.Testing.cs
- CI targets in Build.CI.cs
- All targets visible in help output

**Acceptance Criteria**: Build system properly organized by vertical slice

---

#### 1.4 Deprecated Code Removal Verification

**Test ID**: PST-004
**Priority**: High
**Type**: Structural

**Test Steps**:
```bash
# 1. Verify scripts are deleted
ls scripts/pack-tool-platform.cs 2>&1
ls scripts/build-tool-dll.cs 2>&1

# 2. Verify NUKE_MIGRATION.md updated
grep -i "removed" NUKE_MIGRATION.md

# 3. Verify README.md updated
grep -i "pack-tool-platform\|build-tool-dll" README.md
```

**Expected Results**:
- Both scripts return "No such file or directory"
- NUKE_MIGRATION.md mentions scripts as removed
- README.md does not reference removed scripts

**Acceptance Criteria**: Deprecated scripts removed, documentation updated

---

### 2. Build Target Tests

#### 2.1 PackTool Target Test

**Test ID**: BT-001
**Priority**: Critical
**Type**: Functional

**Test Steps**:
```bash
# 1. Clean artifacts
rm -rf artifacts/packages

# 2. Run PackTool
./build.sh PackTool

# 3. Verify package created
ls -lh artifacts/packages/Morphir.Tool.*.nupkg

# 4. Extract and verify package structure
unzip -l artifacts/packages/Morphir.Tool.*.nupkg | grep -E "(tools/net10.0|DotnetToolSettings.xml)"

# 5. Extract DotnetToolSettings.xml
unzip -p artifacts/packages/Morphir.Tool.*.nupkg tools/net10.0/any/DotnetToolSettings.xml

# 6. Verify entry point and command name
unzip -p artifacts/packages/Morphir.Tool.*.nupkg tools/net10.0/any/DotnetToolSettings.xml | grep -E "(CommandName|EntryPoint)"
```

**Expected Results**:
- `Morphir.Tool.*.nupkg` created in artifacts/packages
- Package size ~60-70MB (includes dependencies)
- Package contains `tools/net10.0/any/` directory
- DotnetToolSettings.xml exists
- CommandName: `dotnet-morphir`
- EntryPoint: `dotnet-morphir.dll`

**Acceptance Criteria**: Tool package builds successfully with correct structure

---

#### 2.2 PackAll Target Test

**Test ID**: BT-002
**Priority**: Critical
**Type**: Functional

**Test Steps**:
```bash
# 1. Clean artifacts
rm -rf artifacts/packages

# 2. Run PackAll
./build.sh PackAll

# 3. Verify all packages created
ls -lh artifacts/packages/

# 4. Count packages
ls artifacts/packages/*.nupkg | wc -l
```

**Expected Results**:
- 4 packages created:
  - `Morphir.Core.*.nupkg` (~75KB)
  - `Morphir.Tooling.*.nupkg` (~38KB)
  - `Morphir.*.nupkg` (~27KB - executable package)
  - `Morphir.Tool.*.nupkg` (~60MB - tool with deps)
- No build errors
- No directory cleaning conflicts

**Acceptance Criteria**: All four packages build successfully

---

#### 2.3 DevWorkflow Target Test

**Test ID**: BT-003
**Priority**: High
**Type**: Functional

**Test Steps**:
```bash
# 1. Run complete DevWorkflow
./build.sh DevWorkflow

# 2. Verify all steps executed
# - Restore
# - Lint (Format check)
# - Compile
# - Test
```

**Expected Results**:
- All steps complete successfully
- Exit code 0
- No build errors
- All tests pass
- Simulates GitHub Actions workflow

**Acceptance Criteria**: Local CI simulation works correctly

---

#### 2.4 CILint Target Test

**Test ID**: BT-004
**Priority**: High
**Type**: Functional

**Test Steps**:
```bash
# 1. Run CILint
./build.sh CILint

# 2. Verify lint checks run
```

**Expected Results**:
- Restore completes
- Format check runs
- Exit code 0 if code formatted
- Clear error if formatting needed

**Acceptance Criteria**: Lint simulation works independently

---

#### 2.5 CITest Target Test

**Test ID**: BT-005
**Priority**: High
**Type**: Functional

**Test Steps**:
```bash
# 1. Run CITest
./build.sh CITest

# 2. Verify build and test
```

**Expected Results**:
- Restore completes
- Compile succeeds
- All tests run
- Exit code 0

**Acceptance Criteria**: Test simulation works independently

---

### 3. BDD Acceptance Tests (from Issue #209)

#### 3.1 Build Morphir.Tool Package

**Test ID**: BDD-001
**Priority**: Critical
**Type**: BDD Acceptance

**Gherkin Scenario**:
```gherkin
Scenario: Build Morphir.Tool package
  Given Morphir.Tool project exists
  When I run "./build.sh PackTool"
  Then Morphir.Tool.*.nupkg should be created
  And package should contain tools/net10.0/any/dotnet-morphir.dll
  And package should contain tools/net10.0/any/DotnetToolSettings.xml
```

**Test Steps**:
```bash
# Given
test -d src/Morphir.Tool && echo "Project exists"

# When
./build.sh PackTool

# Then
test -f artifacts/packages/Morphir.Tool.*.nupkg && echo "Package created"
unzip -l artifacts/packages/Morphir.Tool.*.nupkg | grep "tools/net10.0/any/dotnet-morphir.dll"
unzip -l artifacts/packages/Morphir.Tool.*.nupkg | grep "DotnetToolSettings.xml"
```

**Expected Result**: All assertions pass

**Note**: Updated from original spec to use `dotnet-morphir.dll` instead of `morphir.dll`

---

#### 3.2 Build System Split Successfully

**Test ID**: BDD-002
**Priority**: Critical
**Type**: BDD Acceptance

**Gherkin Scenario**:
```gherkin
Scenario: Build system split successfully
  Given Build.cs is split into partial classes
  When I run "./build.sh --help"
  Then all targets should be available
  And Build.Packaging.cs targets should be listed
  And Build.Publishing.cs targets should be listed
  And Build.Testing.cs targets should be listed
```

**Test Steps**:
```bash
# Given
ls build/Build*.cs | wc -l  # Should be 5

# When
./build.sh --help > help_output.txt

# Then
grep -E "(PackLibs|PackTool|PackAll)" help_output.txt
grep -E "(PublishLibs|PublishTool|PublishAll)" help_output.txt
grep -E "(Test|TestE2E|BuildE2ETests)" help_output.txt
grep -E "(CILint|CITest|DevWorkflow)" help_output.txt
rm help_output.txt
```

**Expected Result**: All target groups visible in help

---

#### 3.3 Tool Command Name is Correct

**Test ID**: BDD-003
**Priority**: Critical
**Type**: BDD Acceptance

**Gherkin Scenario**:
```gherkin
Scenario: Tool command name is correct
  Given Morphir.Tool package is built
  When I extract DotnetToolSettings.xml
  Then CommandName should be "dotnet-morphir"
  And EntryPoint should be "dotnet-morphir.dll"
```

**Test Steps**:
```bash
# Given
./build.sh PackTool

# When & Then
unzip -p artifacts/packages/Morphir.Tool.*.nupkg tools/net10.0/any/DotnetToolSettings.xml | grep 'CommandName="dotnet-morphir"'
unzip -p artifacts/packages/Morphir.Tool.*.nupkg tools/net10.0/any/DotnetToolSettings.xml | grep 'EntryPoint="dotnet-morphir.dll"'
```

**Expected Result**: Both assertions pass

**Note**: Updated from original spec to use `dotnet-morphir` instead of `morphir`

---

### 4. Verification Checklist (from Issue #209)

#### 4.1 Build Verification

**Test ID**: VC-001
**Priority**: Critical
**Type**: Checklist

**Checklist Items**:
- [ ] `./build.sh PackTool` succeeds
- [ ] `Morphir.Tool.*.nupkg` created in artifacts/packages
- [ ] Package contains correct structure (tools/net10.0/any/)
- [ ] DotnetToolSettings.xml has CommandName="dotnet-morphir"
- [ ] DotnetToolSettings.xml has EntryPoint="dotnet-morphir.dll"
- [ ] `./build.sh --help` shows all targets
- [ ] No broken targets after split
- [ ] Deprecated scripts removed
- [ ] Documentation updated

**Test Procedure**: Execute all BT and PST tests above

---

#### 4.2 Manual Testing Verification

**Test ID**: VC-002
**Priority**: High
**Type**: Manual

**Checklist Items**:
- [ ] Build tool package locally
- [ ] Inspect package structure (unzip and verify)
- [ ] Run all build targets to ensure nothing broke
- [ ] Verify `./build.sh --help` output

**Test Procedure**: Manual execution and inspection

---

### 5. Windows Build Fix Tests

#### 5.1 Verify GenerateWolverineCode Target Removed from MSBuild

**Test ID**: WBF-001
**Priority**: Critical
**Type**: Regression

**Test Steps**:
```bash
# 1. Verify no GenerateWolverineCode in Directory.Build.targets
grep -i "GenerateWolverineCode" Directory.Build.targets

# 2. Verify GenerateWolverineCode exists in Build.Testing.cs
grep "GenerateWolverineCode" build/Build.Testing.cs

# 3. Verify parallel builds enabled
grep "BuildInParallel" build/Build.cs
```

**Expected Results**:
- No GenerateWolverineCode in Directory.Build.targets
- GenerateWolverineCode target in Build.Testing.cs
- No BuildInParallel=false in build files

**Acceptance Criteria**: Root cause of Windows file locking fixed

---

#### 5.2 Windows Build Smoke Test

**Test ID**: WBF-002
**Priority**: Critical
**Type**: Smoke (Windows only)

**Test Steps** (Windows):
```powershell
# 1. Clean build
./build.ps1 Clean

# 2. Full build
./build.ps1 Compile

# 3. Build tests
./build.ps1 Test

# 4. Package all
./build.ps1 PackAll
```

**Expected Results**:
- No CS2012 errors (file locking)
- No VBCSCompiler issues
- All steps complete successfully

**Acceptance Criteria**: Windows builds complete without file locking

---

### 6. Documentation Tests

#### 6.1 Build Target Documentation

**Test ID**: DOC-001
**Priority**: High
**Type**: Documentation

**Test Steps**:
```bash
# 1. Run help and capture output
./build.sh --help > help_full.txt

# 2. Verify each target has description
grep -E "Clean.*Clean" help_full.txt
grep -E "Restore.*Restore" help_full.txt
grep -E "Compile.*Compile" help_full.txt
# ... (test all 23+ targets)

# 3. Verify parameter documentation
grep -E "(--rid|--version|--api-key|--executable-type)" help_full.txt

rm help_full.txt
```

**Expected Results**:
- Every target has a description
- Parameters documented
- Help output readable

**Acceptance Criteria**: All build targets self-documenting

---

#### 6.2 NUKE_MIGRATION.md Accuracy

**Test ID**: DOC-002
**Priority**: Medium
**Type**: Documentation

**Test Steps**:
```bash
# Verify deprecated scripts marked as REMOVED
grep -A 2 "pack-tool-platform" NUKE_MIGRATION.md
grep -A 2 "build-tool-dll" NUKE_MIGRATION.md
```

**Expected Results**:
- Both scripts marked as REMOVED
- Rationale provided

**Acceptance Criteria**: Migration doc accurate

---

### 7. Integration Tests

#### 7.1 End-to-End Package Flow

**Test ID**: INT-001
**Priority**: Critical
**Type**: Integration

**Test Steps**:
```bash
# 1. Clean everything
./build.sh Clean
rm -rf artifacts

# 2. Full build and package flow
./build.sh PackAll

# 3. Publish to local feed
./build.sh PublishLocalAll

# 4. Install tool from local feed
dotnet tool uninstall -g Morphir.Tool || true
dotnet tool install -g Morphir.Tool --add-source artifacts/local-feed

# 5. Verify tool works
dotnet-morphir --version

# 6. Cleanup
dotnet tool uninstall -g Morphir.Tool
```

**Expected Results**:
- All packages build
- Local publish succeeds
- Tool installs
- Tool runs correctly
- Version displayed

**Acceptance Criteria**: Complete package flow works

---

#### 7.2 Existing Tests Still Pass

**Test ID**: INT-002
**Priority**: Critical
**Type**: Regression

**Test Steps**:
```bash
# 1. Run all unit tests
./build.sh Test

# 2. Build E2E tests
./build.sh BuildE2ETests

# 3. Run E2E tests (if available)
./build.sh TestE2E --executable-type=all || echo "E2E tests may need executables"
```

**Expected Results**:
- All unit tests pass
- E2E tests build
- No regressions introduced

**Acceptance Criteria**: Test suite remains green

---

## Definition of Done Verification

From issue #209, Phase 1 is complete when:

- [x] All tasks completed and checked off (see Task Status below)
- [x] All BDD scenarios passing (BDD-001, BDD-002, BDD-003)
- [x] All verification checklist items completed (VC-001, VC-002)
- [x] Code follows Morphir conventions (AGENTS.md) - PR reviewed and merged
- [x] No build warnings related to changes - PR CI passed
- [x] PR ready for review - PR #214 merged

## Task Status

### Task 1.1: Create Morphir.Tool Project ✅
- [x] Create `src/Morphir.Tool/` directory
- [x] Create `Morphir.Tool.csproj` with PackAsTool settings
- [x] Set ToolCommandName to "dotnet-morphir" (updated from "morphir")
- [x] Set PackageId to "Morphir.Tool"
- [x] Add project references to Morphir (added), Morphir.Core, and Morphir.Tooling
- [x] Create minimal `Program.cs` that delegates to `Morphir.Program.Main()` (updated approach)
- [x] Add to solution file

**Implementation Note**: Tool name follows dotnet convention (`dotnet-morphir`) and delegates to public `Morphir.Program` instead of duplicating code.

### Task 1.2: Update Morphir Project ✅
- [x] Verify `AssemblyName="morphir"` (lowercase)
- [x] Set `IsPackable=true` (changed from false to support NuGet/GitHub releases)
- [x] Ensure AOT and trimming settings remain
- [x] Make `Program` class public (changed from unchanged)

**Implementation Note**: Morphir is now packable to support independent versioning and deployment alongside AOT executables.

### Task 1.3: Split Build.cs ✅
- [x] Create `build/Build.Packaging.cs` (PackLibs, PackTool, PackAll targets)
- [x] Create `build/Build.Publishing.cs` (PublishLibs, PublishTool, PublishAll targets + local variants)
- [x] Create `build/Build.Testing.cs` (Test, BuildE2ETests, TestE2E, GenerateWolverineCode targets)
- [x] Create `build/Build.CI.cs` (CILint, CITest, DevWorkflow targets - ADDED)
- [x] Update `Build.cs` as main entry point (parameters, core config, main targets)
- [x] Verify all targets accessible via `./build.sh --help`

**Implementation Note**: Added Build.CI.cs with CI workflow simulation targets not in original plan.

### Task 1.4: Create Helper Classes ❌ DEFERRED
- [ ] Create `build/Helpers/` directory
- [ ] Create `PackageValidator.cs` (ValidateToolPackage, ValidateLibraryPackage)
- [ ] Create `ChangelogHelper.cs` (GetVersion, GetReleaseNotes, PrepareRelease)
- [ ] Create `PathHelper.cs` (FindLatestPackage)
- [ ] Add unit tests for helpers (optional in this phase)

**Status**: NOT IMPLEMENTED
**Rationale**: Helpers deemed unnecessary at this stage. Build targets work without them. Can be added in future if needed.
**Impact**: None - no functionality blocked

### Task 1.5: Remove Deprecated Code ✅
- [x] Delete `scripts/pack-tool-platform.cs`
- [x] Delete `scripts/build-tool-dll.cs`
- [x] Remove references from documentation (README.md)
- [x] Update `NUKE_MIGRATION.md` to note removal

### Task 1.6: Update Build Targets ✅
- [x] Fix `PackTool` to build `Morphir.Tool.csproj`
- [x] Fix `PublishTool` glob pattern to `Morphir.Tool.*.nupkg`
- [x] Test locally: `./build.sh PackTool`
- [x] Verify package created: `artifacts/packages/Morphir.Tool.*.nupkg`
- [x] Add `.After(PackLibs)` to prevent directory cleaning conflict (ADDED FIX)

**Implementation Note**: Added dependency ordering to prevent PackAll from having directory conflicts.

### Additional Tasks Completed (Not in Original Plan)

#### Windows Build Fix ✅
- [x] Remove `GenerateWolverineCode` MSBuild target from Directory.Build.targets
- [x] Create Nuke-based `GenerateWolverineCode` target in Build.Testing.cs
- [x] Update trimmed publish targets to depend on GenerateWolverineCode
- [x] Re-enable parallel builds

**Rationale**: Fixed root cause of Windows file locking issues (circular build dependencies)

#### Comprehensive Documentation ✅
- [x] Add XML doc comments to all 23+ build targets
- [x] Document parameters (--rid, --version, --api-key, etc.)
- [x] Document output locations
- [x] Document dependencies

**Rationale**: Makes build system self-documenting via `./build.sh --help`

#### CI Workflow Simulation ✅
- [x] Create `DevWorkflow` target (complete CI pipeline)
- [x] Create `CILint` target (lint checks only)
- [x] Create `CITest` target (build and tests only)

**Rationale**: Allows local validation before pushing to PR, improves developer experience

## Test Execution Summary

### Critical Tests (Must Pass)
- **PST-001**: Morphir.Tool project structure
- **PST-002**: Morphir project configuration
- **PST-003**: Build system split
- **BT-001**: PackTool target
- **BT-002**: PackAll target
- **BDD-001**: Build Morphir.Tool package
- **BDD-002**: Build system split
- **BDD-003**: Tool command name
- **WBF-001**: Wolverine code gen fix
- **INT-001**: End-to-end package flow
- **INT-002**: Existing tests pass

### High Priority Tests (Should Pass)
- **PST-004**: Deprecated code removal
- **BT-003**: DevWorkflow target
- **BT-004**: CILint target
- **BT-005**: CITest target
- **VC-002**: Manual testing
- **DOC-001**: Build target documentation

### Medium Priority Tests (Nice to Have)
- **DOC-002**: NUKE_MIGRATION.md accuracy

### Platform-Specific Tests
- **WBF-002**: Windows build smoke test (Windows only)

## Known Issues & Follow-ups

### Issues to File

Based on deviations and incomplete tasks:

1. **Helper Classes Not Implemented** (Low Priority)
   - **Title**: Add build helper classes for package validation and changelog management
   - **Labels**: enhancement, build-system, nice-to-have
   - **Description**: Task 1.4 from Phase 1 was deferred. Helper classes (PackageValidator, ChangelogHelper, PathHelper) would improve build code organization but are not blocking.
   - **Epic**: #208

2. **Unit Tests for Build System** (Low Priority)
   - **Title**: Add unit tests for Nuke build targets
   - **Labels**: testing, build-system, nice-to-have
   - **Description**: Build targets currently tested manually and via CI. Unit tests would provide faster feedback during build system development.
   - **Epic**: #208

### Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Windows file locking returns | Low | High | Root cause fixed; monitor CI |
| Helper classes needed later | Medium | Low | Can add incrementally when needed |
| Tool naming confusion | Low | Medium | Documentation clear on `dotnet-morphir` |
| Morphir packable breaks AOT | Low | High | Tested in CI; both work independently |

## Test Environment Requirements

### Software Requirements
- .NET SDK 10.0 (pinned in global.json)
- Nuke build tool (bootstrapped via build scripts)
- Git
- GitHub CLI (`gh`) for issue operations
- unzip (for package inspection)

### Platform Requirements
- Linux (primary testing)
- Windows (WBF-002 specific)
- macOS (optional, for comprehensive testing)

### Disk Space
- ~500MB for build artifacts
- ~1GB for local NuGet feed

## Test Execution Instructions

### Quick Smoke Test (5 minutes)
```bash
# 1. Verify structure
ls -la src/Morphir.Tool/
ls -la build/Build*.cs

# 2. Build all packages
./build.sh PackAll

# 3. Verify packages
ls -lh artifacts/packages/

# 4. Run help
./build.sh --help | grep -E "(Pack|Publish|Test|CI)"
```

### Full Test Suite (30 minutes)
```bash
# 1. Run all structural tests (PST-*)
# Execute PST-001 through PST-004 test steps

# 2. Run all build target tests (BT-*)
# Execute BT-001 through BT-005 test steps

# 3. Run all BDD tests (BDD-*)
# Execute BDD-001 through BDD-003 test steps

# 4. Run all integration tests (INT-*)
# Execute INT-001 and INT-002 test steps

# 5. Run documentation tests (DOC-*)
# Execute DOC-001 and DOC-002 test steps

# 6. Run Windows tests (WBF-*) - Windows only
# Execute WBF-001 and WBF-002 test steps
```

### Automated Test Script
```bash
#!/usr/bin/env bash
# Run this script to execute all automated tests

set -euo pipefail

echo "=== Phase 1 Automated Test Suite ==="
echo ""

# PST-001
echo "PST-001: Morphir.Tool Project Verification"
grep -q 'PackAsTool>true' src/Morphir.Tool/Morphir.Tool.csproj
grep -q 'dotnet-morphir' src/Morphir.Tool/Morphir.Tool.csproj
grep -q 'Morphir.Program.Main' src/Morphir.Tool/Program.cs
echo "✓ PST-001 passed"
echo ""

# PST-003
echo "PST-003: Build System Split Verification"
test $(ls build/Build*.cs | wc -l) -eq 5
grep -q 'partial.*class Build' build/Build.cs
echo "✓ PST-003 passed"
echo ""

# BT-001
echo "BT-001: PackTool Target Test"
./build.sh PackTool
test -f artifacts/packages/Morphir.Tool.*.nupkg
echo "✓ BT-001 passed"
echo ""

# BT-002
echo "BT-002: PackAll Target Test"
./build.sh Clean
./build.sh PackAll
test $(ls artifacts/packages/*.nupkg | wc -l) -eq 4
echo "✓ BT-002 passed"
echo ""

# INT-002
echo "INT-002: Existing Tests Still Pass"
./build.sh Test
echo "✓ INT-002 passed"
echo ""

echo "=== All automated tests passed ==="
```

## Sign-off

### Test Plan Approval
- [ ] QA Lead
- [ ] Engineering Lead
- [ ] Product Owner

### Test Execution Sign-off
- [ ] All critical tests passed
- [ ] All high priority tests passed
- [ ] Known issues documented
- [ ] Follow-up issues filed

## Appendix

### A. Reference Documents
- Issue #209: https://github.com/finos/morphir-dotnet/issues/209
- PR #214: https://github.com/finos/morphir-dotnet/pull/214
- Epic #208: https://github.com/finos/morphir-dotnet/issues/208
- AGENTS.md: [AGENTS.md](../../../AGENTS.md)
- NUKE_MIGRATION.md: [NUKE_MIGRATION.md](../../../NUKE_MIGRATION.md)

### B. Test Data
- Test packages: `artifacts/packages/`
- Test scripts: See "Automated Test Script" section

### C. Glossary
- **AOT**: Ahead-of-Time compilation
- **BDD**: Behavior-Driven Development
- **Nuke**: Build automation system for .NET
- **PackAsTool**: MSBuild property for dotnet tool packages
- **Vertical Slice**: Architectural pattern organizing by feature
- **WolverineFx**: Messaging framework used in project

### D. Test Metrics
- **Total Tests**: 19
- **Critical**: 11
- **High Priority**: 7
- **Medium Priority**: 1
- **Estimated Execution Time**: 30-60 minutes (full suite)
- **Automated**: ~70% (remaining require manual inspection)
