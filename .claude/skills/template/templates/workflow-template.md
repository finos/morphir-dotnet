# Workflow Template: {Workflow Name}

> **Template Instructions**: Replace all `{placeholder}` text with actual content. Remove this blockquote when done.

## Overview

**Purpose:** {What this workflow accomplishes}

**When to use:** {Scenarios where this workflow applies}

**Duration:** ~{X} minutes

**Difficulty:** {Easy / Medium / Hard}

## Prerequisites

Before starting this workflow, ensure:

- [ ] {Prerequisite 1}
- [ ] {Prerequisite 2}
- [ ] {Prerequisite 3}
- [ ] {Prerequisite 4}

**Required Tools:**
- {Tool 1} - {Why needed}
- {Tool 2} - {Why needed}
- {Tool 3} - {Why needed}

**Required Permissions:**
- {Permission 1}
- {Permission 2}

## Workflow Phases

### Phase 1: {Phase Name}

**Objective:** {What this phase accomplishes}

**Duration:** ~{X} minutes

#### Step 1.1: {Step Name}

**Action:**
```bash
{command if applicable}
```

**What it does:** {Explanation}

**Expected result:**
```
{What you should see}
```

**If it fails:**
- Check: {Troubleshooting tip 1}
- Check: {Troubleshooting tip 2}
- Solution: {How to resolve}

#### Step 1.2: {Step Name}

**Action:**
{Description of action to take}

**Why:** {Rationale for this step}

**Validation:**
- [ ] {Validation check 1}
- [ ] {Validation check 2}

**Note:** {Important information or caveat}

#### Step 1.3: {Step Name}

**Action:**
```{language}
{code if applicable}
```

**Details:**
- {Detail 1}
- {Detail 2}
- {Detail 3}

**Success criteria:** {What success looks like}

---

### Phase 2: {Phase Name}

**Objective:** {What this phase accomplishes}

**Duration:** ~{X} minutes

#### Step 2.1: {Step Name}

**Action:**
```bash
{command if applicable}
```

**What it does:** {Explanation}

**Expected output:**
```
{Example output}
```

**Common issues:**
- **Issue:** {Problem description}
  - **Cause:** {Why it happens}
  - **Fix:** {How to resolve}

#### Step 2.2: {Step Name}

**Action:**
{Description of action to take}

**Options:**
- **Option A:** {Description} - Use when {scenario}
- **Option B:** {Description} - Use when {scenario}
- **Option C:** {Description} - Use when {scenario}

**Recommended:** {Which option to prefer and why}

#### Step 2.3: {Step Name}

{Similar structure to previous steps}

---

### Phase 3: {Phase Name}

**Objective:** {What this phase accomplishes}

**Duration:** ~{X} minutes

#### Step 3.1: {Step Name}

{Similar structure to previous steps}

#### Step 3.2: {Step Name}

{Similar structure to previous steps}

#### Step 3.3: {Step Name}

**Action:**
{Final action description}

**Verification:**
1. {Verification step 1}
2. {Verification step 2}
3. {Verification step 3}

**Success indicators:**
- {Indicator 1}
- {Indicator 2}
- {Indicator 3}

## Post-Workflow

After completing the workflow:

### Cleanup

- [ ] {Cleanup task 1}
- [ ] {Cleanup task 2}
- [ ] {Cleanup task 3}

### Documentation

- [ ] {Documentation task 1}
- [ ] {Documentation task 2}
- [ ] {Documentation task 3}

### Feedback

- [ ] Capture patterns discovered
- [ ] Note any issues encountered
- [ ] Document improvements for next time
- [ ] Update this workflow if needed

## Troubleshooting

### Problem: {Common Problem 1}

**Symptoms:**
- {Symptom 1}
- {Symptom 2}

**Cause:** {Root cause}

**Solution:**
1. {Fix step 1}
2. {Fix step 2}
3. {Fix step 3}

**Prevention:** {How to avoid in future}

---

### Problem: {Common Problem 2}

{Similar structure to Problem 1}

---

### Problem: {Common Problem 3}

{Similar structure to Problem 1}

## Decision Points

### Decision Point 1: {Decision Name}

**When:** {At which step this decision is needed}

**Question:** {The decision to make}

**Options:**
- **A:** {Option} → {When to choose} → {Next step}
- **B:** {Option} → {When to choose} → {Next step}
- **C:** {Option} → {When to choose} → {Next step}

**Recommendation:** {Which option to prefer and why}

---

### Decision Point 2: {Decision Name}

{Similar structure to Decision Point 1}

## Examples

### Example 1: {Scenario Name}

**Context:** {Describe the situation}

**Workflow execution:**
- Phase 1: {Summary of what happened}
  - Used Option A in Step 1.2 because {reason}
- Phase 2: {Summary of what happened}
  - Encountered issue with Step 2.3, resolved by {solution}
- Phase 3: {Summary of what happened}

**Outcome:** {Final result}

**Duration:** {Actual time taken}

**Lessons learned:**
- {Lesson 1}
- {Lesson 2}

---

### Example 2: {Scenario Name}

{Similar structure to Example 1}

## Automation

### Fully Automated

Use this script for fully automated execution:

```bash
dotnet fsi .claude/skills/{guru-id}/scripts/{workflow-script}.fsx
```

**What it automates:**
- {Phase/Step 1}
- {Phase/Step 2}
- {Phase/Step 3}

**Still requires manual:**
- {Manual step 1}
- {Manual step 2}

### Partially Automated

For partial automation:

```bash
# Automate Phase 1
dotnet fsi .claude/skills/{guru-id}/scripts/{phase1-script}.fsx

# Manual Phase 2
{manual steps}

# Automate Phase 3
dotnet fsi .claude/skills/{guru-id}/scripts/{phase3-script}.fsx
```

## Variations

### Variation: {Variation Name}

**When to use:** {Scenario for this variation}

**Differences from standard workflow:**
- {Difference 1}
- {Difference 2}
- {Difference 3}

**Modified steps:**
- Step {N}: {How it changes}
- Step {N}: {How it changes}

**Duration:** ~{X} minutes

---

### Variation: {Variation Name}

{Similar structure to first variation}

## Success Criteria

The workflow is successful when:

- [ ] {Success criterion 1}
- [ ] {Success criterion 2}
- [ ] {Success criterion 3}
- [ ] {Success criterion 4}
- [ ] All verification steps passed
- [ ] Post-workflow tasks completed
- [ ] Documentation updated

## Related Workflows

- **{Related Workflow 1}** - See [workflow-{id}.md](./workflow-{id}.md)
  - Use this when {scenario}
- **{Related Workflow 2}** - See [workflow-{id}.md](./workflow-{id}.md)
  - Use this when {scenario}
- **{Related Workflow 3}** - See [workflow-{id}.md](./workflow-{id}.md)
  - Use this when {scenario}

## References

- {Reference 1} - {Description}
- {Reference 2} - {Description}
- {Reference 3} - {Description}

## Feedback

Help improve this workflow:
- Found an issue? Report it: {Issue URL template}
- Have a suggestion? Discuss it: {Discussion URL template}
- Discovered a pattern? Document it: {Pattern documentation process}

---

**Last Updated:** {YYYY-MM-DD}  
**Version:** {Semantic version}  
**Tested On:** {List of scenarios where tested}  
**Status:** {Draft / Active / Deprecated}
