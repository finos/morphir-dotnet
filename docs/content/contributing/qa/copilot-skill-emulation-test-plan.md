---
title: "GitHub Copilot Skill Emulation Test Plan"
description: "Test plan to validate documentation-based skill emulation in GitHub Copilot (Issue #266)."
date: 2025-12-19
draft: false
---

# GitHub Copilot Skill Emulation Test Plan

## Objective

Validate that morphir-dotnet skills (QA Tester, AOT Guru, Release Manager) are discoverable and usable in GitHub Copilot via documentation-based emulation, including running automation scripts and following playbooks.

## Scope

- Skills: QA Tester, AOT Guru, Release Manager
- Agent: GitHub Copilot (VS Code)
- Artifacts: Conversation transcripts, pass/fail report, documentation updates

## Test Matrix

- Discovery: List available skills and locations
- Invocation: Follow SKILL.md guidance on request
- Scripts: Provide and run script commands
- Playbooks: Walk through sequential steps
- Decision Trees: Apply logic from SKILL.md

## BDD Scenarios

Feature: Skill Discovery in GitHub Copilot

- Scenario: User requests list of available skills
  - Given Copilot is active in morphir-dotnet
  - When the user asks "What skills are available in this project?"
  - Then Copilot references `.agents/skills-reference.md`
  - And lists QA Tester, AOT Guru, Release Manager with short descriptions
  - And includes links to SKILL.md files

Feature: Skill Alias Understanding

- Scenario: User asks about skill aliases
  - When the user asks "Can I use @skill qa instead of @skill qa-tester?"
  - Then Copilot explains aliases are documentation-only
  - And clarifies `@skill` is Claude-specific
  - And suggests reading `.claude/skills/qa-tester/skill.md`

Feature: QA Tester Skill Emulation

- Scenario: Create test plan using QA Tester
  - Given PR acceptance criteria exists
  - When the user asks "Use the QA Tester skill to create a test plan for PR #123"
  - Then Copilot reads `.claude/skills/qa-tester/skill.md`
  - And produces a plan covering happy paths, edge cases, errors, priorities, and execution scripts

Feature: Skill Script Execution

- Scenario: Run smoke test script from QA Tester
  - When the user asks "How do I run the smoke test script from QA Tester?"
  - Then Copilot references `.claude/skills/qa-tester/scripts/`
  - And provides the command `dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx`
  - And explains expected behavior and outcomes

Feature: Playbook Navigation

- Scenario: Follow regression testing playbook
  - When the user asks "Walk me through the regression testing playbook"
  - Then Copilot outlines steps from SKILL.md in order
  - And includes commands and validation criteria for each step

## Acceptance Criteria

- Copilot lists all skills with correct locations
- Copilot follows SKILL.md guidance to produce outputs
- Copilot provides correct script commands and context
- Copilot can enumerate playbook steps with validation criteria
- Documentation includes Copilot usage and examples

## Execution Notes

- Recommended prompts:
  - "Use the QA Tester skill to create a test plan for PR #123"
  - "Apply AOT Guru guidance to optimize binary size"
  - "Follow Release Manager playbook to prepare v1.0.0"

- Script commands:

```bash
dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx
dotnet fsi .claude/skills/qa-tester/scripts/regression-test.fsx
dotnet fsi .claude/skills/aot-guru/scripts/aot-diagnostics.fsx
dotnet fsi .claude/skills/release-manager/scripts/prepare-release.fsx
```

## Reporting

- Record pass/fail per scenario and any limitations
- Capture Copilot transcripts where feasible
- Propose documentation improvements based on gaps

## References

- [.agents/skills-reference.md](../../../../.agents/skills-reference.md)
- [AGENTS.md](../../../../AGENTS.md)