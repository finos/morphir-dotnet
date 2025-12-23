---
title: "Create Project Board: Morphir Application Architect Skill"
linkTitle: "Create Project Board"
weight: 103
description: >
  Step-by-step guide to create and configure the GitHub Project board for tracking Morphir Application Architect skill implementation.
---

# Create Project Board for Morphir Application Architect Skill

This guide walks through creating a GitHub Project board to track all 28 issues for the Morphir Application Architect skill.

## Option 1: Automated Script (Recommended)

### Prerequisites

Your GitHub CLI token needs the `project` scope.

**Add the scope**:
```bash
gh auth refresh -s project
```

### Run the Script

```bash
chmod +x scripts/create-project-board.sh
./scripts/create-project-board.sh
```

The script will:
1. Create a new project board
2. Add all 28 issues (#314-341)
3. Output the project URL

---

## Option 2: Manual Creation via Web UI

If you prefer or if the script doesn't work, follow these steps:

### Step 1: Create the Project

1. Navigate to: https://github.com/orgs/finos/projects
2. Click **"New project"**
3. Choose **"Board"** template
4. Name: `Morphir Application Architect Skill Implementation`
5. Click **"Create project"**

### Step 2: Configure Views

#### Default Board View

The default board view should have these columns:
- **Backlog** - Issues not yet started
- **In Progress** - Currently being worked on
- **Review** - Awaiting review/approval
- **Done** - Completed issues

#### Add Status Field

1. Click **"+"** to add a field
2. Select **"Status"** (or create "Status" single-select field)
3. Add options:
   - ⏳ Backlog
   - 🚧 In Progress
   - 👀 Review
   - ✅ Done

#### Add Custom Fields (Optional but Recommended)

**Phase Field** (Single Select):
- Phase 1
- Phase 2
- Phase 3
- Phase 4
- Phase 4.5
- Phase 5

**Priority Field** (Single Select):
- P0 (Critical)
- P1 (High)
- P2 (Medium)

**Effort Field** (Number):
- Unit: days

### Step 3: Add Issues to Project

#### Add Epic Issue

1. In the project board, click **"+ Add item"**
2. Search for `#314` (Epic: Morphir Application Architect Skill)
3. Press Enter to add

#### Bulk Add All Task Issues

There are two ways to add the remaining 27 issues:

**Method A: Add by Label Filter**

1. Click **"+ Add item"**
2. Type `label:skill:architect` in the search
3. Select all issues that appear
4. Add them to the project

**Method B: Add Individually**

Add each issue number manually:
```
#315, #316, #317, #318  (Phase 1)
#319, #320, #321, #322, #323  (Phase 2)
#324, #325, #326, #327, #328  (Phase 3)
#329, #330, #331, #332  (Phase 4)
#333, #334, #335  (Phase 4.5)
#336, #337, #338, #339, #340, #341  (Phase 5)
```

### Step 4: Organize Issues

#### Set Status for All Issues

1. Select all issues (Ctrl+A or Cmd+A)
2. Set Status to "Backlog"

#### Group by Phase (Optional)

1. Click **"Group"** dropdown
2. Select **"Milestone"**
3. Issues will be grouped by phase

#### Sort by Issue Number

1. Click **"Sort"** dropdown
2. Select **"Issue number"** (ascending)
3. Issues will be in order: #314-341

### Step 5: Create Additional Views

#### Create Table View

1. Click **"+ New view"**
2. Choose **"Table"**
3. Name: `All Tasks`
4. This view shows all fields in a spreadsheet format
5. Useful for editing multiple fields at once

#### Create Roadmap View

1. Click **"+ New view"**
2. Choose **"Roadmap"**
3. Name: `Timeline`
4. Set date field to milestone due dates
5. Visualizes phases on a timeline

#### Create Kanban per Phase

For each phase, create a filtered view:

1. Click **"+ New view"**
2. Choose **"Board"**
3. Name: `Phase 1 Tasks`
4. Add filter: `milestone:"Phase 1: Core Architectural Guidance"`
5. Repeat for Phases 2-5

### Step 6: Configure Automation (Optional)

GitHub Projects (beta) supports workflow automation:

#### Auto-set Status on Issue Events

1. Click **"..."** menu → **"Workflows"**
2. Enable these automations:

**Item added to project**:
- Set Status to "Backlog"

**Item closed**:
- Set Status to "Done"

**Pull request merged**:
- Set Status to "Done"

#### Custom Automations

For more complex workflows, use GitHub Actions with the Projects API.

Example: Auto-move to "In Progress" when assigned
```yaml
# .github/workflows/project-automation.yml
name: Project Automation

on:
  issues:
    types: [assigned]

jobs:
  move-to-in-progress:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/add-to-project@v0.5.0
        with:
          project-url: https://github.com/orgs/finos/projects/YOUR_PROJECT_NUMBER
          github-token: ${{ secrets.PROJECT_TOKEN }}
          labeled: skill:architect

      # Additional steps to update status field
```

### Step 7: Set Project Description and README

1. Click **"..."** menu → **"Settings"**
2. Add description:
   ```
   Tracking implementation of the Morphir Application Architect expert skill.

   Epic: #314
   PRD: See docs/content/contributing/design/prds/morphir-application-architect-skill.md
   Total: 27 tasks across 5 phases (20 weeks)
   ```

3. Add project README (visible on project page):
   ```markdown
   # Morphir Application Architect Skill Implementation

   Comprehensive expert skill combining:
   - Morphir ecosystem mastery
   - Language design expertise
   - Functional programming patterns
   - WebAssembly integration
   - Semantic web technologies
   - Integration protocols (gRPC, JSON-RPC, JSONL)

   ## Quick Links
   - [Epic #314](https://github.com/finos/morphir-dotnet/issues/314)
   - [PRD](../docs/content/contributing/design/prds/morphir-application-architect-skill.md)
   - [Implementation Plan](../docs/content/contributing/design/implementation-plans/morphir-architect-skill-plan.md)

   ## Phases
   - Phase 1: Core Architectural Guidance (4 tasks)
   - Phase 2: Pluggable Pipeline Architecture (5 tasks)
   - Phase 3: WebAssembly Integration (5 tasks)
   - Phase 4: Semantic Web Integration (4 tasks)
   - Phase 4.5: Integration Technologies (3 tasks)
   - Phase 5: Integration and Documentation (6 tasks)
   ```

### Step 8: Share the Project

1. Click **"..."** menu → **"Settings"**
2. Set visibility to **"Public"** (or appropriate for your org)
3. Get shareable link
4. Add link to Epic issue (#314)

## Verification Checklist

After creating the project board, verify:

- [ ] Project created with correct name
- [ ] All 28 issues added (#314-341)
- [ ] Board view configured with status columns
- [ ] Custom fields added (Phase, Priority, Effort)
- [ ] Issues set to "Backlog" status
- [ ] Additional views created (Table, Roadmap)
- [ ] Automation rules enabled
- [ ] Project description and README set
- [ ] Project visibility configured
- [ ] Link added to Epic issue

## Project Board URL

After creation, the project URL will be:
```
https://github.com/orgs/finos/projects/YOUR_PROJECT_NUMBER
```

Add this URL to:
- Epic issue #314 description
- Repository README
- This documentation

## Maintenance

**Weekly**:
- Review and update issue statuses
- Move completed issues to "Done"
- Identify blocked issues

**Monthly**:
- Review milestone progress
- Update phase completion percentages
- Adjust timeline if needed

**Per Phase**:
- Close completed phase milestone
- Open next phase for work
- Conduct phase retrospective

---

**Questions?**: Tag `@maintainers` in Epic issue #314
