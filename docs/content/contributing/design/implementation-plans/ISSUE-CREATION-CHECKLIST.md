---
title: "Issue Creation Checklist: Morphir Application Architect Skill"
linkTitle: "Issue Creation Checklist"
weight: 102
description: >
  Step-by-step checklist for creating GitHub issues for the Morphir Application Architect skill implementation.
---

# Issue Creation Checklist: Morphir Application Architect Skill

**Prerequisites**: PR #312 merged to main

## Step 1: Create Labels

Before creating issues, ensure these labels exist in the repository:

```bash
# Create labels using GitHub CLI
gh label create "skill:architect" --description "Morphir Application Architect skill" --color "0E8A16"
gh label create "phase-1" --description "Phase 1: Core Architectural Guidance" --color "D4C5F9"
gh label create "phase-2" --description "Phase 2: Pluggable Pipeline Architecture" --color "C5DEF5"
gh label create "phase-3" --description "Phase 3: WebAssembly Integration" --color "BFD4F2"
gh label create "phase-4" --description "Phase 4: Semantic Web Integration" --color "C2E0C6"
gh label create "phase-4.5" --description "Phase 4.5: Integration Technologies" --color "C2E0C6"
gh label create "phase-5" --description "Phase 5: Integration and Documentation" --color "FAD8C7"
gh label create "priority-p0" --description "Critical priority" --color "D73A4A"
gh label create "priority-p1" --description "High priority" --color "FB8C00"
gh label create "priority-p2" --description "Medium priority" --color "FEF2C0"
```

**Verification**:
- [ ] All 11 labels created
- [ ] Labels visible in repository settings

## Step 2: Create Milestones

Create milestones for each phase:

```bash
# Create milestones using GitHub CLI
gh milestone create "Phase 1: Core Architectural Guidance" --due-date 2025-02-03 --description "Weeks 1-4: Ecosystem mastery, language patterns, FP patterns"
gh milestone create "Phase 2: Pluggable Pipeline Architecture" --due-date 2025-03-03 --description "Weeks 5-8: Unified.js study, pipeline implementation, example plugins"
gh milestone create "Phase 3: WebAssembly Integration" --due-date 2025-03-31 --description "Weeks 9-12: Component Model, WIT interfaces, Extism integration"
gh milestone create "Phase 4: Semantic Web Integration" --due-date 2025-04-28 --description "Weeks 13-16: RDF schema, JSON-LD, linked data navigation"
gh milestone create "Phase 5: Integration and Documentation" --due-date 2025-05-26 --description "Weeks 17-20: Cross-skill integration, pattern catalog, guides"
```

**Verification**:
- [ ] All 5 milestones created
- [ ] Due dates set correctly
- [ ] Milestones visible in repository

## Step 3: Create Epic Issue

Create the epic issue first (it will serve as the parent for all task issues):

**File to use**: `morphir-architect-github-issues.md` - Epic section

**Command**:
```bash
# Extract epic issue body and create
gh issue create \
  --title "Epic: Morphir Application Architect Skill" \
  --label "epic,skill:architect,enhancement" \
  --assignee "@me"
```

Copy the epic issue markdown from the "Epic Issue" section in `morphir-architect-github-issues.md`.

**Verification**:
- [ ] Epic issue created
- [ ] Labels applied correctly
- [ ] Issue number noted for reference (will use to link task issues)

**Note Epic Issue Number**: #____ (fill in after creation)

## Step 4: Create Phase 1 Issues

Create all Phase 1 task issues:

### Issue 1.1: Morphir Ecosystem Documentation Review

```bash
gh issue create \
  --title "Task 1.1: Morphir Ecosystem Documentation Review" \
  --label "skill:architect,phase-1,priority-p0,documentation" \
  --milestone "Phase 1: Core Architectural Guidance"
```

Copy body from "Issue 1.1" section in `morphir-architect-github-issues.md`.
Update "Epic: #TBD" with actual epic issue number.

**Verification**:
- [ ] Issue 1.1 created
- [ ] Labels applied
- [ ] Milestone set
- [ ] Epic reference updated

### Issue 1.2: Language Design Pattern Research

```bash
gh issue create \
  --title "Task 1.2: Language Design Pattern Research" \
  --label "skill:architect,phase-1,priority-p0,research,patterns" \
  --milestone "Phase 1: Core Architectural Guidance"
```

Copy body from "Issue 1.2" section.

**Verification**:
- [ ] Issue 1.2 created
- [ ] Labels applied
- [ ] Milestone set

### Issue 1.3: Functional Programming Pattern Library

```bash
gh issue create \
  --title "Task 1.3: Functional Programming Pattern Library" \
  --label "skill:architect,phase-1,priority-p0,patterns,functional-programming" \
  --milestone "Phase 1: Core Architectural Guidance"
```

Copy body from "Issue 1.3" section.

**Verification**:
- [ ] Issue 1.3 created
- [ ] Labels applied
- [ ] Milestone set

### Issue 1.4: Create Initial Skill File

```bash
gh issue create \
  --title "Task 1.4: Create Initial Skill File" \
  --label "skill:architect,phase-1,priority-p0,documentation" \
  --milestone "Phase 1: Core Architectural Guidance"
```

Copy body from "Issue 1.4" section.
Update dependencies with actual issue numbers.

**Verification**:
- [ ] Issue 1.4 created
- [ ] Labels applied
- [ ] Milestone set
- [ ] Dependencies updated

## Step 5: Create Phase 2 Issues

Follow the same pattern for Phase 2 (5 issues: 2.1 - 2.5):

- [ ] Issue 2.1: Unified.js Architecture Study
- [ ] Issue 2.2: Pipeline Architecture Design (ADR)
- [ ] Issue 2.3: Implement Core Pipeline
- [ ] Issue 2.4: Create Example Plugins
- [ ] Issue 2.5: Pipeline Automation Scripts

**Milestone**: Phase 2: Pluggable Pipeline Architecture

## Step 6: Create Phase 3 Issues

Create Phase 3 issues (5 issues: 3.1 - 3.5):

- [ ] Issue 3.1: Component Model Deep Dive
- [ ] Issue 3.2: Design WIT Interfaces for Morphir IR
- [ ] Issue 3.3: Extism Integration Design
- [ ] Issue 3.4: Cross-Runtime Communication Strategy
- [ ] Issue 3.5: WebAssembly Automation Scripts

**Milestone**: Phase 3: WebAssembly Integration

## Step 7: Create Phase 4 and 4.5 Issues

### Phase 4: Semantic Web Integration (4 issues: 4.1 - 4.4)

- [ ] Issue 4.1: RDF Schema Design
- [ ] Issue 4.2: JSON-LD Context Design
- [ ] Issue 4.3: Linked Data Navigation Design
- [ ] Issue 4.4: RDF Conversion Script

**Milestone**: Phase 4: Semantic Web Integration

### Phase 4.5: Integration Technologies (3 issues)

**Note**: These are NEW issues not yet documented in the GitHub issues template. Create them manually:

#### Issue 4.5.1: gRPC Integration Design

```bash
gh issue create \
  --title "Task 4.5.1: gRPC Integration Design" \
  --label "skill:architect,phase-4.5,priority-p1,grpc,integration" \
  --milestone "Phase 4: Semantic Web Integration"
```

**Body**:
```markdown
## Task 4.5.1: gRPC Integration Design

**Epic**: #XXX (Morphir Application Architect Skill)
**Phase**: Phase 4.5
**Labels**: `skill:architect`, `phase-4.5`, `priority-p1`, `grpc`, `integration`

### Description
Design gRPC service definitions for Morphir operations with Protocol Buffers.

### Prerequisites
- [ ] Phase 3 complete

### Activities
1. Design gRPC service definitions for Morphir operations
2. Create Protocol Buffers (.proto) for IR types
3. Support bidirectional streaming for transformations
4. Enable service discovery and health checks

### Deliverables
- [ ] Proto definitions for core IR types
- [ ] Service definitions for validation, optimization, generation
- [ ] Streaming transformation support
- [ ] gRPC-Web support for browser clients

### Acceptance Criteria
- [ ] Proto definitions for core IR types created
- [ ] Service definitions working
- [ ] Streaming transformation examples
- [ ] gRPC-Web client tested

### Effort Estimate
2 days

### Related Issues
Part of Phase 4.5: Integration Technologies
See PRD Section: Phase 4.5
```

- [ ] Issue 4.5.1 created

#### Issue 4.5.2: JSON-RPC Integration Design

Similar to above, with JSON-RPC specifics.

- [ ] Issue 4.5.2 created

#### Issue 4.5.3: JSON Lines Integration

Similar to above, with JSONL specifics.

- [ ] Issue 4.5.3 created

## Step 8: Create Phase 5 Issues

Create Phase 5 issues (6 issues: 5.1 - 5.6):

- [ ] Issue 5.1: Cross-Skill Integration
- [ ] Issue 5.2: Complete Pattern Catalog
- [ ] Issue 5.3: Architecture Guides
- [ ] Issue 5.4: Tutorial Series
- [ ] Issue 5.5: Architecture Health Monitoring
- [ ] Issue 5.6: Final Documentation Review

**Milestone**: Phase 5: Integration and Documentation

## Step 9: Create Project Board

Set up GitHub Project board for tracking:

```bash
# Via GitHub UI (no CLI command for new Projects)
1. Go to: https://github.com/finos/morphir-dotnet/projects
2. Click "New project"
3. Choose "Board" template
4. Name: "Morphir Application Architect Skill Implementation"
5. Add columns: Backlog, In Progress, Review, Done
```

**Verification**:
- [ ] Project board created
- [ ] Columns configured
- [ ] All issues added to board (in Backlog)

## Step 10: Update Epic Issue

After all issues are created, update the Epic issue with actual issue numbers:

**Update Epic issue body**:
```markdown
### Related Issues

Phase 1: #XXX, #XXX, #XXX, #XXX
Phase 2: #XXX, #XXX, #XXX, #XXX, #XXX
Phase 3: #XXX, #XXX, #XXX, #XXX, #XXX
Phase 4: #XXX, #XXX, #XXX, #XXX
Phase 4.5: #XXX, #XXX, #XXX
Phase 5: #XXX, #XXX, #XXX, #XXX, #XXX, #XXX
```

**Verification**:
- [ ] Epic issue updated with all task issue numbers
- [ ] All links work correctly

## Step 11: Final Verification

**Complete Checklist**:
- [ ] All labels created (11 labels)
- [ ] All milestones created (5 milestones)
- [ ] Epic issue created and updated
- [ ] All Phase 1 issues created (4 issues)
- [ ] All Phase 2 issues created (5 issues)
- [ ] All Phase 3 issues created (5 issues)
- [ ] All Phase 4 issues created (4 issues)
- [ ] All Phase 4.5 issues created (3 issues)
- [ ] All Phase 5 issues created (6 issues)
- [ ] **Total: 1 epic + 27 task issues = 28 issues**
- [ ] Project board created and populated
- [ ] All dependencies linked correctly
- [ ] All issues have proper labels and milestones

## Quick Commands Summary

For rapid issue creation after labels/milestones are set up:

```bash
# Epic
gh issue create --title "Epic: Morphir Application Architect Skill" \
  --label "epic,skill:architect,enhancement"

# Phase 1 (repeat for each issue with appropriate title/labels)
gh issue create --title "Task 1.1: Morphir Ecosystem Documentation Review" \
  --label "skill:architect,phase-1,priority-p0,documentation" \
  --milestone "Phase 1: Core Architectural Guidance"

# Use the morphir-architect-github-issues.md file for complete issue bodies
```

## Automation Script (Optional)

For fully automated issue creation, create an F# script:

**File**: `scripts/create-architect-issues.fsx`

```fsharp
#!/usr/bin/env dotnet fsi

// Script to automate creation of all Morphir Architect issues
// Usage: dotnet fsi scripts/create-architect-issues.fsx

// TODO: Implement automated issue creation from template
```

---

**Estimated Time**: 1-2 hours for manual creation
**Recommended**: Create Epic and Phase 1 issues first, then iterate on subsequent phases as work progresses

**Questions?**: Open an issue with label `maintainer-attention`
