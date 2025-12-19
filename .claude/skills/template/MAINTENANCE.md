# {Guru Name} Maintenance Guide

> **Template Instructions**: Replace all `{placeholder}` text with actual content. Remove this blockquote when done.

## Overview

This guide documents how to maintain, evolve, and improve the {Guru Name} skill over time. Following the guru philosophy, this skill is a **learning system** that improves through structured feedback and quarterly reviews.

## Quarterly Review Checklist

Run this checklist every quarter (Q1: Jan-Mar, Q2: Apr-Jun, Q3: Jul-Sep, Q4: Oct-Dec):

### 1. Collect Feedback

- [ ] Review feedback from past quarter
  - Location: {Where feedback is stored - e.g., "GitHub issues tagged `guru-{guru-id}`"}
  - Count: {N} items collected
- [ ] Review session logs/notes (if captured)
- [ ] Review retrospectives (if applicable)
- [ ] Solicit feedback from team:
  - What worked well?
  - What was confusing?
  - What could be improved?

### 2. Analyze Patterns

- [ ] Review pattern catalog
  - Current count: {N} patterns
  - Patterns added this quarter: {N}
  - Patterns updated this quarter: {N}
- [ ] Identify frequently used patterns (3+ times)
  - {Pattern 1}: Used {N} times
  - {Pattern 2}: Used {N} times
  - {Pattern 3}: Used {N} times
- [ ] Identify automation opportunities
  - Patterns suitable for Myriad plugin: {List}
  - Patterns suitable for scripts: {List}
  - Manual processes to automate: {List}

### 3. Identify Improvements

- [ ] 2-3 key improvement areas identified:
  1. {Improvement area 1}
  2. {Improvement area 2}
  3. {Improvement area 3}
- [ ] Prioritize improvements:
  - **High Priority** (must do): {Improvement}
  - **Medium Priority** (should do): {Improvement}
  - **Low Priority** (nice to have): {Improvement}

### 4. Update Guidance

- [ ] Update SKILL.md:
  - New patterns documented: {N}
  - Decision trees updated: {List}
  - Playbooks updated: {List}
  - Review scope updated: {Yes/No}
- [ ] Update README.md:
  - Quick reference updated
  - Examples refreshed
  - Script documentation current
- [ ] Update metadata.yaml:
  - Version bumped: {Old} → {New}
  - Status updated (if changed)
  - Review triggers adjusted (if changed)

### 5. Update Automation

- [ ] Existing scripts updated:
  - {Script 1}: {Changes made}
  - {Script 2}: {Changes made}
  - {Script 3}: {Changes made}
- [ ] New scripts created:
  - {New script 1}: {Purpose}
  - {New script 2}: {Purpose}
- [ ] Scripts tested:
  - All scripts run successfully: {Yes/No}
  - Token savings validated: {Yes/No}

### 6. Document Learnings

- [ ] Update IMPLEMENTATION.md (if exists) with quarter's learnings
- [ ] Document in this file under "Version History" section
- [ ] Update success metrics:
  - Pattern count: {N}
  - Script count: {N}
  - Token savings: ~{N} per {unit}
  - Usage frequency: {Metric}

### 7. Bump Version

- [ ] Semantic versioning rules followed:
  - **Major** (X.0.0): Breaking changes to skill interface
  - **Minor** (x.Y.0): New features, patterns, scripts added
  - **Patch** (x.y.Z): Bug fixes, clarifications, minor updates
- [ ] Version updated in:
  - [ ] metadata.yaml
  - [ ] SKILL.md footer
  - [ ] README.md footer
  - [ ] This file (MAINTENANCE.md)
- [ ] Git tag created: `{guru-id}-v{version}`

### 8. Communicate Updates

- [ ] Team notification prepared:
  - What changed
  - Why it changed
  - How to use new features
  - Migration notes (if applicable)
- [ ] Update announced in:
  - [ ] Team chat/Slack
  - [ ] GitHub discussion
  - [ ] Project README (if significant)

## Feedback Collection

### Where Feedback is Captured

**Primary Location:** {Where - e.g., "GitHub issues with label `guru-{guru-id}`"}

**Secondary Locations:**
- {Location 2 - e.g., "Session notes in `.claude/skills/{guru-id}/.sessions/`"}
- {Location 3 - e.g., "Retrospective reports in `.claude/skills/{guru-id}/retrospectives/`"}
- {Location 4 - e.g., "Team feedback in discussions"}

### Review Schedule

- **Frequency:** {How often - e.g., "Quarterly (Q1, Q2, Q3, Q4)"}
- **Duration:** {How long - e.g., "1-2 hours per review"}
- **Owner:** {Who leads - e.g., "Skill maintainer with team input"}

### Stakeholders to Consult

1. **{Stakeholder Role 1}** ({Name/Handle}) - {Why they're consulted}
2. **{Stakeholder Role 2}** ({Name/Handle}) - {Why they're consulted}
3. **{Stakeholder Role 3}** ({Name/Handle}) - {Why they're consulted}

### Feedback Template

When capturing feedback, use this structure:

```markdown
## Feedback: {Topic}
**Date:** {YYYY-MM-DD}
**Source:** {Person/Session/Retrospective}
**Category:** {Pattern/Decision Tree/Playbook/Automation/Review}

### What Happened
{Description of situation or issue}

### What Worked Well
{Positive aspects}

### What Could Be Improved
{Improvement suggestions}

### Action Items
- [ ] {Action 1}
- [ ] {Action 2}
- [ ] {Action 3}

### Priority
{High/Medium/Low}
```

## Improvement Process

### 1. Collect Feedback
- Gather all feedback from quarter
- Organize by category (patterns, playbooks, automation, review)
- Prioritize by frequency and impact

### 2. Identify Patterns
- What patterns repeated 3+ times? → Candidates for automation
- What questions came up frequently? → Need better documentation
- What workflows were painful? → Need playbook improvement

### 3. Update Playbooks/Templates
- Refine existing playbooks based on learnings
- Create new playbooks for common workflows
- Update templates with better examples
- Improve decision trees with new branches

### 4. Test Changes
- Validate updated playbooks with real scenarios
- Test automation scripts thoroughly
- Verify documentation clarity
- Get feedback from at least one other team member

### 5. Document in Changelog
- Add entry to "Version History" section below
- Explain what changed and why
- Provide migration notes if needed

### 6. Publish Update
- Commit changes with clear message
- Create git tag for version
- Announce to team
- Update skill matrix

## Automation Opportunity Checklist

Use this checklist to evaluate if a pattern should become automated:

- [ ] **Frequency:** Pattern appears 3+ times?
- [ ] **Token Cost:** Manual process takes 200+ tokens?
- [ ] **Repeatability:** Process is consistent and rule-based?
- [ ] **Clarity:** Requirements are clear and unambiguous?
- [ ] **Value:** Automation would save significant time/effort?

**Decision:**
- **Yes to all 5:** Create automation (script or Myriad plugin)
- **Yes to 3-4:** Consider automation, prioritize by impact
- **Yes to 0-2:** Document pattern, don't automate yet

## Review Capability Maintenance

### Review Script Updates

Update review scripts when:
- New anti-patterns discovered
- Detection logic improves
- Output format changes
- New categories added

**Checklist:**
- [ ] Review script logic still valid
- [ ] Detection patterns up to date
- [ ] Output format clear and actionable
- [ ] Integration with retrospectives working
- [ ] Performance acceptable

### Review Criteria Updates

Update review criteria when:
- New issues identified in retrospectives
- Thresholds need adjustment
- Scope expands or contracts
- Integration points change

**Process:**
1. Document why criteria changed
2. Update metadata.yaml review section
3. Update SKILL.md review capability section
4. Test updated criteria on historical data
5. Validate findings are still relevant

## Version History

### Version {X.Y.Z} (YYYY-MM-DD)

**Changes:**
- {Change category 1}:
  - {Specific change 1}
  - {Specific change 2}
- {Change category 2}:
  - {Specific change 1}
  - {Specific change 2}

**Why:**
{Explanation of why these changes were made}

**Migration Notes:**
{Instructions for users if changes affect usage}

**Metrics:**
- Patterns: {N} (was {N})
- Scripts: {N} (was {N})
- Token savings: ~{N} per {unit} (was ~{N})

---

### Version {X.Y.Z} (YYYY-MM-DD)

{Similar structure to above}

---

### Version 0.1.0 (YYYY-MM-DD)

**Initial Release**
- SKILL.md complete (1000+ lines)
- {N} automation scripts
- {N} seed patterns
- {N} playbooks
- Review capability implemented

---

## Success Metrics

Track these metrics quarterly to measure guru health:

### Pattern Catalog
- **Target (Alpha):** 5-10 patterns
- **Target (Beta):** 15+ patterns
- **Target (Stable):** 20+ patterns
- **Current:** {N} patterns

### Automation Scripts
- **Target (Alpha):** 3-5 scripts
- **Target (Beta):** 4-7 scripts
- **Target (Stable):** 5-10 scripts
- **Current:** {N} scripts

### Token Efficiency
- **Target:** {N}+ tokens saved per {unit}
- **Current:** ~{N} tokens saved per {unit}

### Usage Frequency
- **Measurement:** {How measured - e.g., "Script invocations per month"}
- **Target:** {N}+ uses per month
- **Current:** {N} uses per month

### Feedback Quality
- **Target:** 3+ improvement suggestions per quarter
- **Current:** {N} suggestions this quarter

### Review Effectiveness
- **Target:** 80%+ of findings are actionable
- **Current:** {N}% actionable findings

## Maintenance Schedule

| Activity | Frequency | Duration | Owner |
|----------|-----------|----------|-------|
| Feedback Collection | Continuous | Ongoing | All users |
| Pattern Documentation | As discovered | 15-30 min | Maintainer |
| Script Updates | As needed | 30-60 min | Maintainer |
| Quarterly Review | Quarterly | 1-2 hours | Maintainer + Team |
| Version Release | Quarterly | 30 min | Maintainer |
| Announcement | After release | 15 min | Maintainer |

## Emergency Updates

Sometimes updates can't wait for quarterly review:

### When to Issue Emergency Update

- Critical bug in automation script
- Incorrect guidance causing issues
- Security concern in patterns
- Breaking change in dependencies
- Urgent coordination need with other gurus

### Emergency Update Process

1. **Identify Issue:** Document what's wrong and impact
2. **Create Fix:** Minimal changes to resolve issue
3. **Test Fix:** Validate fix works
4. **Bump Patch Version:** Increment patch number (x.y.Z)
5. **Update Documentation:** Minimal updates to reflect fix
6. **Announce Urgently:** Notify team immediately
7. **Document in Changelog:** Add to version history

## Cross-Project Portability

### Portable Components

These components can be reused in other projects:
- {Component 1 - e.g., "Decision trees (concepts apply broadly)"}
- {Component 2 - e.g., "Pattern catalog (adapt examples)"}
- {Component 3 - e.g., "Script utilities (change paths)"}

### Non-Portable Components

These components are morphir-dotnet specific:
- {Component 1 - e.g., "Project-specific playbooks"}
- {Component 2 - e.g., "NUKE build integration"}
- {Component 3 - e.g., "morphir-dotnet conventions"}

### Adaptation Guide

To use this guru in another project:

1. **Update Paths:**
   - Change all file paths in scripts
   - Update directory references
   - Adjust build system paths

2. **Adapt Playbooks:**
   - Replace morphir-dotnet workflows
   - Update tool references
   - Adjust conventions

3. **Customize Patterns:**
   - Adapt examples to new project
   - Update code snippets
   - Revise naming conventions

4. **Test Thoroughly:**
   - Validate all scripts work
   - Test playbooks end-to-end
   - Verify patterns are relevant

**Estimated Effort:** {N} hours

## Contact and Escalation

### Primary Maintainer
- **Name:** {Maintainer name}
- **GitHub:** @{handle}
- **Slack:** @{handle} (if applicable)
- **Responsibility:** Quarterly reviews, script updates, pattern curation

### Backup Maintainer
- **Name:** {Backup name}
- **GitHub:** @{handle}
- **Slack:** @{handle} (if applicable)
- **Responsibility:** Cover when primary unavailable

### Escalation Path

**For Technical Issues:**
1. Open GitHub issue with label `guru-{guru-id}` + `bug`
2. Tag: @{maintainer}
3. Expected response: {N} business days

**For Guidance Questions:**
1. Open GitHub discussion
2. Tag: @{maintainer}
3. Expected response: {N} business days

**For Urgent Issues:**
1. Slack/Discord direct message to maintainer
2. If no response in {N} hours, escalate to: @{backup}
3. If still no response, escalate to: @{project-lead}

## References

- **[SKILL.md](./SKILL.md)** - Main skill documentation
- **[README.md](./README.md)** - Quick reference guide
- **[metadata.yaml](./metadata.yaml)** - Skill configuration
- **[Guru Philosophy](../../../.agents/guru-philosophy.md)** - Core principles
- **[Guru Creation Guide](../../../.agents/guru-creation-guide.md)** - Creation process
- **[Skill Matrix](../../../.agents/skill-matrix.md)** - Maturity tracking

---

**Version:** {Semantic version}  
**Last Review:** {YYYY-MM-DD}  
**Next Review:** {YYYY-MM-DD}  
**Maintainer:** {Maintainer name/GitHub handle}
