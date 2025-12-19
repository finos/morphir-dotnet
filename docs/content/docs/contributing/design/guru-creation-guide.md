---
title: "Guru Creation Guide"
linkTitle: "Creating Gurus"
description: "Step-by-step guide for creating new AI gurus in morphir-dotnet"
weight: 30
---

# Guru Creation Guide

## Overview

This guide walks through the process of creating a new guru (AI skill) in morphir-dotnet. It establishes a repeatable pattern that ensures consistency, quality, and alignment with the guru philosophy.

A guru should be created when you have a domain of expertise that:
- Is distinct and has clear boundaries
- Crosses multiple project areas or is deep within one area
- Has 3+ core competencies (expertise areas)
- Contains repetitive work suitable for automation

## Part 1: Should This Be a Guru?

### Decision Framework

Ask yourself these questions in order:

#### 1. Is it a distinct domain?
- **Question:** Can I clearly define what this guru owns?
- **Examples:** Yes
  - Testing/QA (QA Tester)
  - AOT optimization (AOT Guru)
  - Release management (Release Manager)
  - Elm-to-F# migration (Elm-to-F# Guru)
- **Examples:** No
  - "Helping with random coding tasks" (too broad)
  - "One-off problem solving" (not a domain)

#### 2. Does it justify deep expertise?
- **Question:** Is there enough depth to warrant 20+ patterns and multiple playbooks?
- **Examples:** Yes
  - QA has 20+ testing patterns (unit, BDD, E2E, property-based, etc.)
  - AOT has 15+ IL error categories and workarounds
  - Release has 4 playbooks (standard, hotfix, pre-release, recovery)
- **Examples:** No
  - One-off task (create a simple script instead)
  - Straightforward process (document in AGENTS.md instead)

#### 3. Will it have 3+ core competencies?
- **Question:** Can I identify at least 3 areas of expertise?
- **Examples:** Yes
  - QA: Test planning, automation, coverage tracking, bug reporting, BDD design
  - AOT: Diagnostics, size optimization, source generators, Myriad, IL parsing
  - Release: Version management, changelog handling, deployment monitoring, recovery
- **Examples:** No
  - Only 1-2 areas (document as section of existing guru or create guide)

#### 4. Is there high-token-cost repetitive work?
- **Question:** Will automation save significant tokens or effort?
- **Examples:** Yes
  - Release: Monitoring workflow status manually (tokens) → autonomous polling (few tokens)
  - AOT: Reading IL warnings manually (tokens) → automated analysis (few tokens)
  - QA: Running tests manually (tokens) → automated test runner (few tokens)
- **Examples:** No
  - Guidance only (no scripts needed, create .agents/ guide instead)
  - One-off automation (create a utility script, not a guru)

#### 5. Will it coordinate with other gurus?
- **Question:** Does this domain have clear integration points?
- **Examples:** Yes
  - Elm-to-F# → AOT Guru (verify generated code is AOT-compatible)
  - Elm-to-F# → QA Tester (verify test coverage)
  - Release Manager ↔ QA Tester (post-release verification)
- **Examples:** No
  - Isolated domain (could be .agents/ guide or standalone skill)

### Decision Result

**If all 5 questions are YES → Create a guru skill**

**If any are NO → Consider alternatives:**
- Just 1-2 competencies? → Create `.agents/{topic}.md` guide instead
- No automation opportunity? → Document decision trees in AGENTS.md
- No coordination needed? → Create standalone utility or guide
- Too narrow/specific? → Create template or plugin, not full guru

## Part 2: Guru Definition

### Step 1: Define the Domain

Write a clear 2-3 sentence description:
```
Domain: Release Management
Description: Orchestrating the complete release lifecycle from version planning 
through deployment and verification. Ensures releases are reliable, predictable, 
and recoverable.
```

### Step 2: Define Competencies

List primary and secondary competencies:

**Primary Competencies (3-6 core areas):**
1. Version Management - Semantic versioning, version detection
2. Changelog Management - Keep a Changelog format, parsing, generation
3. Deployment Orchestration - Workflow automation, status tracking
4. Verification & Recovery - Post-release checks, failure recovery
5. Process Improvement - Retrospectives, playbook evolution
6. Documentation - Comprehensive playbooks, decision trees

**Secondary Competencies (2-4 supporting areas):**
1. Git/GitHub Coordination - Tag management, branch strategies
2. CI/CD Integration - GitHub Actions, workflow triggers
3. Communication - Status updates, failure alerts
4. Historical Analysis - Release metrics, trend tracking

### Step 3: Define Responsibilities

What is this guru accountable for?

```
Release Manager Responsibilities:
- Ensure releases happen on schedule without surprises
- Prevent release failures through pre-flight validation
- Enable fast recovery if failures occur
- Improve the release process continuously (quarterly reviews)
- Communicate clearly about status and blockers
- Coordinate with QA on verification and AOT Guru on version compatibility
```

### Step 4: Define Scope Boundaries

What is explicitly NOT this guru's responsibility?

```
Release Manager Does NOT:
- Make product decisions about what features to include
- Review code quality (that's QA Tester's job)
- Decide version numbering policies (that's maintainers' decision)
- Handle security issues (that's future Security Guru's job)
- Manage documentation (that's future Documentation Guru's job)
```

### Step 5: Map Coordination Points

Identify other gurus this will coordinate with:

```
Release Manager Coordination:
- WITH QA Tester: Hand-off after release for verification
  - Trigger: Release deployed
  - Signal: "Ready for post-release QA?"
  - Response: Test results, coverage, functional verification
  
- WITH AOT Guru: Verify version tags are AOT-compatible
  - Trigger: Before publishing release
  - Signal: "Can I publish this version?"
  - Response: AOT status, any breaking changes
  
- WITH Elm-to-F# Guru: Track feature parity milestones
  - Trigger: Migration progress updates
  - Signal: "What's our migration status for this release?"
  - Response: Completed modules, parity progress
```

## Part 3: Implementation Structure

### Directory Layout

Create the following structure:

```
.claude/skills/{guru-name}/
├── skill.md                    # Main skill prompt (1000-1200 lines)
├── README.md                   # Quick start guide (300-400 lines)
├── MAINTENANCE.md              # Quarterly review process
├── scripts/
│   ├── automation-1.fsx        # High-token-cost task automation
│   ├── automation-2.fsx        # High-token-cost task automation
│   ├── automation-3.fsx        # High-token-cost task automation
│   └── common.fsx              # Shared utilities
├── templates/
│   ├── {decision-type}-decision.md
│   ├── {workflow-type}-workflow.md
│   └── issue-template.md
└── patterns/
    ├── pattern-1.md
    ├── pattern-2.md
    └── [more patterns discovered over time]
```

### skill.md Structure

Your main skill file should contain:

```markdown
---
id: {guru-id}
name: {Guru Name}
triggers:
  - keyword1
  - keyword2
  - keyword3
---

# {Guru Name}

## Overview
[2-3 sentences about the guru]

## Responsibilities
[List of core responsibilities]

## Competencies
[Detailed list of competencies with examples]

## Decision Trees
[3-5 decision trees for common scenarios]

## Playbooks
[3-5 detailed workflows]

## Pattern Catalog
[Growing collection of patterns]

## Automation
[Available F# scripts]

## Integration Points
[How this guru coordinates with others]

## Feedback Loop
[How this guru improves over time]

## Related Resources
[Links to guides, documentation, templates]
```

**Size Target:** 1000-1200 lines (~50 KB)

### README.md Structure

Quick reference for users:

```markdown
# {Guru Name} - Quick Reference

## What This Guru Does
[One paragraph overview]

## When to Use This Guru
[List of scenarios]

## Core Competencies
[Quick bullet list]

## Available Scripts
[Table of scripts with descriptions]

## Common Tasks
[Quick how-tos]

## Pattern Catalog
[Index of patterns]

## Examples
[Real examples from the project]

## Integration
[How to use this guru with others]

## References
[Links to related documentation]
```

**Size Target:** 300-400 lines (~16 KB)

### MAINTENANCE.md Structure

Guidance for maintaining this guru:

```markdown
# Maintenance Guide

## Quarterly Review Checklist
- [ ] Read through collected feedback
- [ ] Identify 2-3 improvements for next quarter
- [ ] Update patterns that changed
- [ ] Create/update Myriad plugins if automation opportunities exist
- [ ] Document learnings in Implementation Notes
- [ ] Update success metrics

## Feedback Collection
- Where feedback is captured: [GitHub issue, tracking doc, etc.]
- Review schedule: [Quarterly, per-release, etc.]
- Stakeholders to consult: [maintainers, project leads]

## Improvement Process
1. Collect feedback
2. Identify patterns
3. Update playbooks/templates
4. Test changes
5. Document in changelog
6. Publish update

## Version History
[Track skill evolution]
```

### F# Scripts

Create 3-5 scripts targeting high-token-cost tasks:

**Script Template:**

```fsharp
#!/usr/bin/env -S dotnet fsi

/// Automation Script: {Purpose}
/// Saves {N} tokens per use by automating {high-token-cost task}
/// Usage: dotnet fsi {script-name}.fsx [args]

#r "nuget: Spectre.Console"
open Spectre.Console

let main argv =
    // Parse arguments
    // Analyze/test/validate something
    // Print results
    0

exit (main fsx.CommandLineArgs.[1..])
```

**Script checklist:**
- [ ] Clear purpose stated in comments
- [ ] Token savings estimated
- [ ] Usage documented
- [ ] Error handling included
- [ ] JSON output option (for automation)
- [ ] Progress indicators (for long-running scripts)

### Templates

Create domain-specific templates:

**Decision Template:**
```markdown
# Decision: {Decision Type}

## Scenario
[When would you make this decision?]

## Options
1. Option A
   - Pros: ...
   - Cons: ...
   - When to use: ...

2. Option B
   - Pros: ...
   - Cons: ...
   - When to use: ...

## Recommendation
[What does the guru recommend?]

## Examples
[Real examples from the project]
```

**Workflow Template:**
```markdown
# Workflow: {Workflow Name}

## Overview
[What does this workflow accomplish?]

## Prerequisites
[What must be true before starting?]

## Steps
1. Step 1 - [description]
2. Step 2 - [description]
...

## Validation
[How do you know it worked?]

## Rollback
[How do you undo if it fails?]

## Related Workflows
[Links to similar workflows]
```

### Pattern Catalog

Start with 5-10 seed patterns, add more as discovered:

**Pattern Entry Template:**
```markdown
# Pattern: {Pattern Name}

## Description
[What is this pattern?]

## Context
[When and why would you use it?]

## Examples
[Real code examples]

## Pros and Cons
[Trade-offs]

## Related Patterns
[Similar or complementary patterns]

## References
[Links to documentation or standards]
```

## Part 4: Automation Strategy

### Identify High-Token-Cost Tasks

For your guru domain, identify 5-10 repetitive tasks:

**Release Manager Example:**
1. Check GitHub Actions status (manual every 5 min = many tokens)
2. Prepare release checklist (manual validation = tokens)
3. Validate post-release status (manual testing = tokens)
4. Extract release history for notes (manual searching = tokens)
5. Detect process changes (manual review = tokens)

### Prioritize for Automation

Score tasks on:
- **Frequency:** How often does this happen? (1-5 scale)
- **Token Cost:** How many tokens does it cost? (1-5 scale)
- **Repetitiveness:** Is this the same every time? (1-5 scale)

```
Task                          Frequency  Token Cost  Repetitive  Priority
Monitor release status        5 (every few min)  3       5        Critical
Prepare checklist             3 (per release)    2       5        High
Post-release validation       3 (per release)    3       5        High
Extract release history       2 (per release)    2       3        Medium
Detect process changes        1 (quarterly)      2       4        Medium
```

**Select top 3-5 for automation**

### Design Automation Scripts

For each task, design an F# script:

**Script Design Pattern:**
1. **Input:** What data does this need?
2. **Processing:** What analysis/transformation?
3. **Output:** What does it return?
4. **Token Savings:** How much does this save?

**Example: Monitor Release Status**
```
Input: GitHub Action workflow ID
Processing: Poll GitHub Actions API, track status
Output: Current status, elapsed time, next check
Token Savings: 100+ tokens per hour (vs. manual polling)
```

## Part 5: Feedback Mechanisms

### Design Feedback Capture

Define when and how the guru learns:

**Trigger Points:**
- After workflow completion? (success/failure)
- After N sessions? (every 5 migrations)
- On quarterly schedule? (Q1, Q2, Q3, Q4)
- After escalations? (decisions beyond scope)

**Capture Methods:**
- GitHub tracking issue (Release Manager model)
- IMPLEMENTATION.md notes (AOT Guru model)
- Automated prompts in skill (your choice)
- Quarterly review meetings (maintainer involvement)

**Example: Elm-to-F# Guru**
```
Capture Trigger: After each module migration
Capture Method: Migration template includes "Patterns Discovered" section
Review Schedule: Quarterly pattern inventory review
Improvement Action: If pattern appears 3+ times, create Myriad plugin

Q1: Discovered 15 new patterns
Q2: Created 2 Myriad plugins for repetitive patterns
Q3: Updated decision trees based on learnings
Q4: Plan next quarter's automation
```

### Design Review Process

Define quarterly reviews:

1. **Collect:** Gather all feedback from past quarter
2. **Analyze:** Identify 2-3 key improvements
3. **Decide:** What will change? What won't?
4. **Update:** Modify playbooks, templates, patterns
5. **Document:** Record what changed and why
6. **Communicate:** Let users know about improvements

**Review Checklist:**
- [ ] Feedback reviewed (N items)
- [ ] Improvement areas identified (3-5 topics)
- [ ] Playbooks updated (X changes)
- [ ] Patterns added/modified (Y patterns)
- [ ] Automation opportunities identified (Z scripts to create)
- [ ] Version bumped if user-facing changes

## Part 5B: Review Capability

### Design the Review Scope

A guru should proactively **review** its domain for issues, guideline violations, and improvement opportunities.

**Define Review Scope Questions:**
1. **What issues should this guru look for?**
   - AOT Guru: Reflection usage, binary size creep, trimming-unfriendly patterns
   - QA Tester: Coverage gaps, ignored tests, missing edge cases, guideline violations
   - Release Manager: Process deviations, changelog quality, version inconsistencies
   - Elm-to-F# Guru: Migration anti-patterns, Myriad plugin opportunities, F# idiom violations

2. **How often should reviews run?**
   - Continuous (real-time detection)
   - Per-session (after each major workflow)
   - Weekly (scheduled scan)
   - Quarterly (comprehensive review)
   - On-demand (user-triggered)

3. **What triggers a review?**
   - Code push? (CI/CD trigger)
   - Release? (post-release verification)
   - Schedule? (weekly, quarterly)
   - Escalation? (manual request)

4. **What's the output format?**
   - Report document (Markdown table of findings)
   - GitHub issues (one issue per finding)
   - Notification (Slack, PR comment)
   - Integrated summary (skill guidance update)

5. **How do review findings feed back?**
   - To playbooks: "We found 3 reflection patterns, add to decision tree"
   - To automation: "This pattern appears repeatedly, create detection script"
   - To retrospectives: "Q1 findings suggest process changes"
   - To next review criteria: "Focus on this area going forward"

### Create Review Scripts

Design and implement F# scripts that perform reviews:

**Example: AOT Guru's Quarterly Review**
```fsharp
// scripts/aot-scan.fsx - Quarterly review of all projects
// Scans for:
//   - Reflection usage (IL2026 patterns)
//   - Binary sizes vs. targets
//   - Trimming-unfriendly patterns (static fields, etc.)
//
// Output: Markdown report with findings, trends, recommendations

Findings:
- Reflection in 7 locations (5 in serialization, 2 in codegen)
- Binary sizes: 8.2 MB (target 8 MB) - creeping by ~200 KB/quarter
- New pattern: ValueTuple boxing in LINQ chains (appears 3x)
- Opportunities: 2 patterns ready for Myriad plugin automation

Recommendations:
- Create aot-serializer.fsx (Myriad plugin) for serialization reflection
- Add ValueTuple boxing detection to aot-diagnostics.fsx
- Set size limit at 8.5 MB (buffer) or refactor

Next Quarter Focus:
- Monitor ValueTuple pattern frequency
- Implement Myriad plugin if pattern appears >5 more times
- Evaluate serialization library alternatives
```

### Integrate Review with Retrospectives

Design how reviews and retrospectives work together:

```
Review (Proactive):
  "I scanned the code and found these issues"
  └─ Findings feed into retrospectives

Retrospective (Reactive):
  "That failure happened because of X"
  └─ Root cause feeds into reviews: "Start looking for X pattern"

Together: Continuous improvement cycle
  Findings → Prevention → Process update → Review criteria → Next quarter
```

**Example Integration:**
```
Q1 Review Findings:
- "We found 5 ignored tests. Why?"

Q1 Retrospective:
- "Test X was failing intermittently. We skipped it to unblock releases."

Q1 Outcomes:
- Fix root cause of flaky test
- Add test to monitoring criteria
- Playbook update: "Always investigate skipped tests in Q1 review"

Q2 Review:
- Monitors for skipped tests automatically
- Finds 0 skipped tests (improvement!)
- Pattern: "Skipped tests went from 5 → 0"
```

### Design Review Integration Points

Define where reviews fit in the workflow:

**Option A: Continuous Review**
- Trigger: Every code push to main
- Runs: During CI/CD
- Output: GitHub check or PR comment
- Effort: Medium (depends on scan speed)

**Option B: Scheduled Review**
- Trigger: Weekly or quarterly
- Runs: Off-hours or on-demand
- Output: Report + GitHub issues for findings
- Effort: Low (scheduled, low impact)

**Option C: Session-Based Review**
- Trigger: After each major workflow (migration, release)
- Runs: As part of workflow
- Output: Integrated into workflow results
- Effort: Varies (per-session analysis)

**Option D: Manual Review**
- Trigger: User request ("@guru review")
- Runs: On-demand
- Output: Full report generated immediately
- Effort: Medium (real-time analysis)

### Review Checklist

When implementing review capability:

- [ ] Review scope clearly defined (what issues to look for)
- [ ] Review trigger designed (when does review run)
- [ ] Review scripts created (F# implementation)
- [ ] Review output format chosen (report/issues/notification)
- [ ] Review findings documented (findings structure)
- [ ] Integration with retrospectives designed
- [ ] Integration with automation strategy designed
- [ ] Integration with playbooks designed
- [ ] Review schedule established (continuous/weekly/quarterly/on-demand)
- [ ] Tested on real project data (not just examples)

## Part 6: Cross-Agent Compatibility

### Ensure Scripts Work Everywhere

Your F# scripts should work for Claude Code, Copilot, and all other agents:

**Checklist:**
- [ ] Scripts use standard F# (no Claude-specific features)
- [ ] Scripts have clear usage documentation
- [ ] Scripts produce JSON output option (for parsing)
- [ ] Scripts have exit codes (0 = success, 1 = validation failure, 2 = error)
- [ ] Scripts document dependencies (required NuGet packages)
- [ ] Scripts work on Windows, Mac, Linux

### Document for All Agents

Your README and documentation should explain:

**For Claude Code users:**
- How to invoke via @skill syntax
- What YAML triggers work

**For Copilot users:**
- How to read .agents/ equivalent guides
- How to run scripts directly

**For other agents:**
- How to find and copy this skill's README
- How to run scripts directly

**Example section:**
```markdown
## Using This Guru

**Claude Code:** Mention keywords like "release", "deploy", "publish"
**Copilot:** Read .agents/release-manager.md for equivalent guidance
**Other agents:** Run scripts directly: `dotnet fsi scripts/monitor-release.fsx`
```

### Cross-Project Portability

Document how this guru could be used in other projects:

```markdown
## Using This Guru in Other Projects

### Portable Components
- Decision trees (universal for this domain)
- Pattern catalog (concepts apply broadly)
- Script utilities (adapt paths for new project)

### Non-Portable Components
- Project-specific playbooks (morphir-dotnet release process)
- Integration with NUKE build system
- Version numbering conventions

### To Adapt to New Project
1. Update script paths (if paths differ)
2. Update build system integration (if not NUKE)
3. Adapt playbooks to new project's process
4. Customize templates for new project conventions

Estimated effort: 4-8 hours
```

## Part 7: Workflow & Validation

### Red-Green-Refactor for Skill Development

Follow TDD principles even for skills:

**Red:** Write test scenarios for the skill
- Create BDD features showing how the guru should behave
- Create decision tree tests ("Given this scenario, recommend this")

**Green:** Implement skill.md
- Write guidance that makes tests pass
- Create playbooks covering test scenarios

**Refactor:** Improve skill based on feedback
- Test with real scenarios
- Get feedback from team
- Update guidance and playbooks

### BDD Scenarios for Skills

Create `.feature` files demonstrating skill behavior:

```gherkin
Feature: Release Manager Guru

  Scenario: Release fails and guru captures retrospective
    Given a release is in progress
    When the release fails
    Then the guru should prompt for "What went wrong?"
    And capture the response in the tracking issue
    And suggest prevention strategies

  Scenario: After 3 successful releases, guru prompts for improvements
    Given 3 consecutive successful releases
    When starting the 4th release
    Then the guru should ask "What could we improve?"
```

### Testing Checklist

Before releasing your guru:

- [ ] Read through skill.md (is it clear? comprehensive?)
- [ ] Test all automation scripts (do they work? return correct output?)
- [ ] Validate decision trees (do they handle real scenarios?)
- [ ] Check playbooks (are they complete? any steps missing?)
- [ ] Review templates (are they usable? any clarifications needed?)
- [ ] Test cross-agent compatibility (can Copilot users find equivalent info?)
- [ ] Verify coordination (do other gurus know about this one?)
- [ ] Get team feedback (does this feel useful? any blind spots?)

## Part 8: Success Criteria

### For Skill Delivery

- [ ] Directory structure created
- [ ] skill.md written (1000+ lines)
- [ ] README.md created (300-400 lines)
- [ ] MAINTENANCE.md documented
- [ ] 3-5 automation scripts implemented
- [ ] 5-10 seed patterns documented
- [ ] 3-5 templates created
- [ ] Coordination points identified
- [ ] Cross-agent compatibility verified
- [ ] Team feedback incorporated

### For Skill Maturity (After First Quarter)

- [ ] Feedback capture mechanism working
- [ ] Quarterly review completed
- [ ] 15+ patterns in catalog
- [ ] 3+ improvements made based on feedback
- [ ] 1+ new automation scripts created (if opportunities found)
- [ ] Playbooks updated with learnings
- [ ] Documentation updated
- [ ] Version bumped (if user-facing changes)
- [ ] Success metrics documented

### For Skill Excellence (After Two Quarters)

- [ ] 20+ patterns in catalog
- [ ] 2+ custom Myriad plugins (if applicable)
- [ ] Automated feedback mechanism working smoothly
- [ ] Token efficiency analysis complete
- [ ] Cross-project reuse strategy documented
- [ ] Integration with other gurus proven
- [ ] Continuous improvement cycle established
- [ ] Learning system generating insights

## Checklist: Creating a New Guru

Use this checklist when creating a new guru:

### Planning Phase
- [ ] Domain clearly defined
- [ ] 3+ competencies identified
- [ ] Responsibilities documented
- [ ] Scope boundaries explicit
- [ ] Coordination points mapped
- [ ] Feedback mechanism designed
- [ ] Review schedule established

### Implementation Phase
- [ ] Directory structure created
- [ ] skill.md written (1000+ lines)
- [ ] README.md written (300-400 lines)
- [ ] MAINTENANCE.md created
- [ ] 3-5 automation scripts (high-token-cost tasks)
- [ ] 5-10 seed patterns
- [ ] 3-5 templates
- [ ] Examples from real project

### Validation Phase
- [ ] Skill.md reviewed for clarity
- [ ] Scripts tested (all work?)
- [ ] Decision trees validated (real scenarios)
- [ ] Playbooks verified (complete steps)
- [ ] Templates usable (examples included)
- [ ] Team feedback collected
- [ ] Cross-agent compatibility checked
- [ ] Coordination with other gurus verified

### Launch Phase
- [ ] Referenced in AGENTS.md
- [ ] Added to .agents/skills-reference.md
- [ ] Announcement to team
- [ ] Integration guide created
- [ ] First feedback collected
- [ ] Initial learnings captured

### Evolution Phase (After 1 Quarter)
- [ ] Quarterly review completed
- [ ] Feedback analyzed
- [ ] 2-3 improvements made
- [ ] Documentation updated
- [ ] Version bumped
- [ ] Team notified of improvements
- [ ] Next quarter's improvements planned

---

**Last Updated:** December 19, 2025
**Created By:** @DamianReeves
**Version:** 1.0 (Initial Release)
