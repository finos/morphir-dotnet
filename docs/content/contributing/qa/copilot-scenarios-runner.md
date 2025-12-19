---
title: "Copilot Skill Emulation Scenarios Runner"
description: "Step-by-step prompts and expected outputs for executing Copilot skill emulation BDD scenarios."
date: 2025-12-19
draft: false
---

# Copilot Skill Emulation Scenarios Runner

Use this guide to execute each scenario in VS Code with GitHub Copilot and capture transcripts.

## Setup

1. Open morphir-dotnet repository in VS Code
2. Open GitHub Copilot chat (`Ctrl+Shift+I`)
3. For each scenario:
   - Copy the **Prompt** below
   - Paste into Copilot and send
   - Record the response (screenshot or copy-paste)
   - Assess **Pass/Fail Criteria**
   - Document in [Execution Report](./copilot-skill-emulation-execution-report.md)

---

## Scenario 1: Skill Discovery

### Prompt
```
What skills are available in this project?
```

### Expected Output (Pass Criteria)
Copilot should:
- ✅ Reference `.agents/skills-reference.md`
- ✅ List: QA Tester, AOT Guru, Release Manager
- ✅ Provide brief descriptions for each
- ✅ Include file paths (e.g., `.claude/skills/qa-tester/SKILL.md`)

### Example Response
```
The morphir-dotnet project provides three specialized skills:

1. **QA Tester** - Comprehensive QA testing and verification
   - Location: .claude/skills/qa-tester/skill.md
   - Use for: Test plan design, regression testing, coverage monitoring

2. **AOT Guru** - Native AOT and trimming optimization
   - Location: .claude/skills/aot-guru/skill.md
   - Use for: Single-file executables, binary size optimization

3. **Release Manager** - Release lifecycle management
   - Location: .claude/skills/release-manager/skill.md
   - Use for: Version management, changelog, release workflows

See .agents/skills-reference.md for detailed usage patterns per agent.
```

### Status: [ ] Pass [ ] Fail
- Notes: ________________________________________

---

## Scenario 2: Skill Alias Understanding

### Prompt
```
Can I use @skill qa instead of @skill qa-tester?
```

### Expected Output (Pass Criteria)
Copilot should:
- ✅ Explain aliases are documentation-only (not functional in Copilot)
- ✅ Clarify `@skill` is Claude Code-specific
- ✅ Suggest reading `.claude/skills/qa-tester/skill.md` directly
- ✅ Provide alternative: use natural language like "Use QA Tester skill to..."

### Example Response
```
No, in GitHub Copilot you cannot use `@skill qa` or aliases.

Here's why:
- `@skill` is a Claude Code-only feature
- Aliases are documented for reference but are not functional in Copilot
- Skills Reference may mention short forms (qa, tester) but these don't work with @

**For Copilot instead, use natural language:**
- "Use the QA Tester skill to create a test plan for PR #123"
- "Apply QA Tester guidance to validate this PR"
- Read directly: .claude/skills/qa-tester/skill.md

See AGENTS.md section "GitHub Copilot Usage Guide (Skill Emulation)" for more.
```

### Status: [ ] Pass [ ] Fail
- Notes: ________________________________________

---

## Scenario 3: QA Tester Skill - Create Test Plan

### Prompt
```
Use the QA Tester skill to create a test plan for this PR:
- Title: Add JSON schema verification for IR v3
- Changes: New VerifyIR handler for JSON validation
- Files: src/Morphir.Tooling/Features/VerifyIR/VerifyIR.cs

What should we test?
```

### Expected Output (Pass Criteria)
Copilot should:
- ✅ Reference `.claude/skills/qa-tester/skill.md` ("Test Plan Development")
- ✅ Cover: happy paths, edge cases, errors, priorities, execution approach
- ✅ Include test cases with steps and assertions
- ✅ Suggest automation scripts to run

### Example Response (Truncated)
```
Based on the QA Tester skill, here's a comprehensive test plan for PR "Add JSON schema verification":

## Test Plan: IR JSON Schema Verification

### Happy Path (Priority: Critical)
1. Valid IR v3 JSON file
   - Steps: Run `VerifyIR valid-ir-v3.json`
   - Expected: Success, JSON output shows IsValid=true
2. Valid IR v2 JSON file
   - Expected: Detection should identify v2, validate against v2 schema

### Edge Cases (Priority: High)
1. Empty JSON file
   - Expected: Returns validation error, friendly message
2. Malformed JSON (missing closing brace)
   - Expected: JSON exception caught, validation error returned
3. Large IR file (>100MB)
   - Expected: Handles gracefully, no memory issues

### Error Conditions (Priority: High)
1. File not found
   - Expected: Exit code 2, error message
2. Permission denied
   - Expected: Handled gracefully

### Automation
- Run: `dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx`
- Full E2E: `dotnet fsi .claude/skills/qa-tester/scripts/regression-test.fsx`
```

### Status: [ ] Pass [ ] Fail
- Notes: ________________________________________

---

## Scenario 4: Skill Script Execution

### Prompt
```
How do I run the smoke test script from the QA Tester skill?
```

### Expected Output (Pass Criteria)
Copilot should:
- ✅ Reference `.claude/skills/qa-tester/scripts/` directory
- ✅ Provide exact command: `dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx`
- ✅ Explain what the script does (quick validation of core functionality)
- ✅ Note expected duration and output

### Example Response
```
To run the QA Tester smoke test script:

**Command:**
```bash
dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx
```

**What it does:**
- Validates core functionality
- Runs: Build, Unit Tests, Package Generation
- Duration: ~5-10 minutes
- Checks: All builds succeed, tests pass, packages are created

**Expected output:**
```
✅ Build succeeded
✅ All tests passed (62 tests)
✅ Packages generated (4 files)
✅ Smoke test PASSED
```

See .claude/skills/qa-tester/scripts/ for other test scripts (regression-test.fsx, etc).
```

### Status: [ ] Pass [ ] Fail
- Notes: ________________________________________

---

## Scenario 5: Playbook Navigation - Regression Testing

### Prompt
```
Walk me through the QA Tester regression testing playbook step by step.
```

### Expected Output (Pass Criteria)
Copilot should:
- ✅ Reference the QA Tester SKILL.md playbook section
- ✅ List steps in logical order with commands/validation
- ✅ Include criteria for "pass" at each step
- ✅ Note any dependencies on previous steps

### Example Response (Truncated)
```
Here's the QA Tester Regression Testing Playbook:

**Step 1: Identify Changed Areas**
- Review git diff against main
- Command: `git diff main --name-only | grep -E '(src|tests)/' | head -20`
- Validation: List affected files displayed

**Step 2: Run Full Unit Test Suite**
- Command: `dotnet test --nologo`
- Pass Criteria: All tests pass, no skipped tests
- Duration: ~3-5 minutes

**Step 3: Run End-to-End Tests**
- Command: `dotnet fsi .claude/skills/qa-tester/scripts/regression-test.fsx`
- Pass Criteria: No E2E failures, CLI works end-to-end
- Duration: ~30-45 minutes

**Step 4: Validate Backwards Compatibility**
- Check: IR JSON roundtrip tests (existing IR files validate)
- Command: Look for tests with "Contract" or "Roundtrip" in name
- Pass Criteria: All backward compat tests pass

**Step 5: Review Code Coverage**
- Check: Coverage >= 80% overall
- Command: `dotnet test --collect:"XPlat Code Coverage"`
- Validation: Report shows no coverage decrease
```

### Status: [ ] Pass [ ] Fail
- Notes: ________________________________________

---

## How to Record and Upload Results

1. **Capture Response**: Screenshot or copy-paste Copilot response
2. **Save to File**: Create a `.txt` file with the transcript
3. **Assess Pass/Fail**: Mark in checklist above
4. **Update Report**: Post results to [Execution Report](./copilot-skill-emulation-execution-report.md)
5. **Commit**: `git add . && git commit -m "test: add Copilot scenario results"`

