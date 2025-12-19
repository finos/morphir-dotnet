# Technical Writer Maintenance Guide

## Overview

This guide documents how to maintain, evolve, and improve the Technical Writer skill over time. Following the guru philosophy, this skill is a **learning system** that improves through structured feedback and quarterly reviews.

## Quarterly Review Checklist

Run this checklist every quarter (Q1: Jan-Mar, Q2: Apr-Jun, Q3: Jul-Sep, Q4: Oct-Dec):

### 1. Collect Feedback

- [ ] Review feedback from past quarter
  - Location: GitHub issues tagged `skill-technical-writer`
  - Count: N items collected
- [ ] Review session logs/notes (if captured)
- [ ] Review retrospectives (if applicable)
- [ ] Solicit feedback from team:
  - What documentation challenges were hard to solve?
  - What Hugo/Docsy issues came up?
  - What diagram patterns were needed?

### 2. Analyze Patterns

- [ ] Review pattern catalog
  - Current count: 11 patterns
  - Patterns added this quarter: N
  - Patterns updated this quarter: N
- [ ] Identify frequently used patterns (3+ times)
  - Hugo Frontmatter: Used N times
  - Mermaid Flowchart: Used N times
  - Tutorial Structure: Used N times
- [ ] Identify automation opportunities
  - Link validation issues: Automate detection
  - Hugo troubleshooting: Script diagnostics
  - Diagram validation: Syntax checking

### 3. Identify Improvements

- [ ] 2-3 key improvement areas identified:
  1. (Improvement area 1)
  2. (Improvement area 2)
  3. (Improvement area 3)
- [ ] Prioritize improvements:
  - **High Priority** (must do): (Improvement)
  - **Medium Priority** (should do): (Improvement)
  - **Low Priority** (nice to have): (Improvement)

### 4. Update Guidance

- [ ] Update SKILL.md:
  - New patterns documented: N
  - Decision trees updated: (List)
  - Playbooks updated: (List)
  - Hugo/Docsy guidance current
- [ ] Update README.md:
  - Quick reference updated
  - Examples refreshed
  - Script documentation current
- [ ] Verify Hugo/Docsy compatibility:
  - Hugo version tested
  - Docsy version tested
  - Any breaking changes documented

### 5. Update Automation

- [ ] Existing scripts updated:
  - link-validator.fsx: (Changes made)
  - hugo-doctor.fsx: (Changes made)
  - diagram-validator.fsx: (Changes made)
- [ ] New scripts created:
  - (New script): (Purpose)
- [ ] Scripts tested:
  - All scripts run successfully: (Yes/No)
  - Token savings validated: (Yes/No)

### 6. Document Learnings

- [ ] Update this file under "Version History" section
- [ ] Update success metrics:
  - Pattern count: N
  - Script count: N
  - Token savings: ~N per audit cycle

### 7. Bump Version

- [ ] Semantic versioning:
  - **Major** (X.0.0): Breaking changes to skill interface
  - **Minor** (x.Y.0): New features, patterns, scripts added
  - **Patch** (x.y.Z): Bug fixes, clarifications, minor updates
- [ ] Version updated in SKILL.md and README.md
- [ ] Git tag created: `technical-writer-vX.Y.Z`

### 8. Communicate Updates

- [ ] Team notification prepared:
  - What changed
  - Why it changed
  - How to use new features
- [ ] Update announced in relevant channels

## Feedback Collection

### Where Feedback is Captured

**Primary Location:** GitHub issues with label `skill-technical-writer`

**Secondary Locations:**
- Session notes (if captured)
- Retrospective reports
- Team discussions

### Review Schedule

- **Frequency:** Quarterly (Q1, Q2, Q3, Q4)
- **Duration:** 1-2 hours per review
- **Owner:** Skill maintainer with team input

### Feedback Template

```markdown
## Feedback: {Topic}
**Date:** YYYY-MM-DD
**Source:** Person/Session/Retrospective
**Category:** Pattern/Decision Tree/Playbook/Automation/Hugo/Docsy/Diagrams

### What Happened
{Description of situation or issue}

### What Worked Well
{Positive aspects}

### What Could Be Improved
{Improvement suggestions}

### Action Items
- [ ] Action 1
- [ ] Action 2

### Priority
{High/Medium/Low}
```

## Improvement Process

### 1. Collect Feedback
- Gather all feedback from quarter
- Organize by category (patterns, Hugo/Docsy, diagrams, automation)
- Prioritize by frequency and impact

### 2. Identify Patterns
- What documentation challenges repeated 3+ times? → Add to patterns
- What Hugo issues came up frequently? → Improve troubleshooting
- What diagram types were needed? → Add templates

### 3. Update Playbooks/Templates
- Refine existing playbooks based on learnings
- Create new playbooks for common workflows
- Update templates with better examples
- Improve decision trees with new branches

### 4. Test Changes
- Validate updated playbooks with real scenarios
- Test automation scripts on actual docs
- Verify Hugo/Docsy guidance works
- Get feedback from at least one other team member

### 5. Document in Changelog
- Add entry to "Version History" section below
- Explain what changed and why

### 6. Publish Update
- Commit changes with clear message
- Create git tag for version
- Announce to team

## Hugo/Docsy Maintenance

### When Hugo Updates

Check these when Hugo releases new versions:

- [ ] Test Hugo build with new version
- [ ] Verify Docsy compatibility
- [ ] Update Decision Tree 2 if new error types
- [ ] Update troubleshooting patterns
- [ ] Test all documentation examples

### When Docsy Updates

Check these when Docsy releases new versions:

- [ ] Review Docsy changelog for breaking changes
- [ ] Test customizations still work
- [ ] Update navigation patterns if needed
- [ ] Verify SCSS overrides work
- [ ] Update documentation as needed

### Keeping References Current

Quarterly:
- [ ] Verify Hugo documentation links work
- [ ] Verify Docsy documentation links work
- [ ] Check Mermaid documentation for updates
- [ ] Update example documentation sites list

## Version History

### Version 0.1.0 (2025-12-19)

**Initial Release**
- SKILL.md complete (~800 lines)
- 7 automation scripts defined
- 11 seed patterns documented
- 5 playbooks created
- 3 decision trees
- Hugo/Docsy mastery documented
- Mermaid/PlantUML patterns included
- Playwright MCP integration documented

**Key Features:**
- Complete Hugo troubleshooting decision tree
- Diagram type selection guide
- Documentation type decision tree
- Release documentation playbook
- Visual communication patterns

---

## Success Metrics

Track these metrics quarterly to measure skill health:

### Pattern Catalog
- **Target (Alpha):** 11+ patterns
- **Target (Beta):** 18+ patterns
- **Target (Stable):** 25+ patterns
- **Current:** 11 patterns

### Automation Scripts
- **Target (Alpha):** 7 scripts
- **Target (Beta):** 8+ scripts
- **Target (Stable):** 10+ scripts
- **Current:** 7 scripts (defined, implementation pending)

### Token Efficiency
- **Target:** 5000+ tokens saved per audit cycle
- **Current:** ~5200 tokens estimated

### Hugo Issues Resolved
- **Measurement:** Track Hugo troubleshooting uses
- **Target:** 90%+ resolved using decision tree

### Documentation Quality
- **Measurement:** Link check failures
- **Target:** <5 broken links at any time

## Maintenance Schedule

| Activity | Frequency | Duration | Owner |
|----------|-----------|----------|-------|
| Feedback Collection | Continuous | Ongoing | All users |
| Pattern Documentation | As discovered | 15-30 min | Maintainer |
| Script Updates | As needed | 30-60 min | Maintainer |
| Hugo/Docsy Compatibility | On releases | 1-2 hours | Maintainer |
| Quarterly Review | Quarterly | 1-2 hours | Maintainer + Team |
| Version Release | Quarterly | 30 min | Maintainer |

## Emergency Updates

### When to Issue Emergency Update

- Hugo breaking change affects docs
- Docsy update breaks site
- Critical bug in automation script
- Incorrect guidance causing issues

### Emergency Update Process

1. **Identify Issue:** Document what's wrong and impact
2. **Create Fix:** Minimal changes to resolve issue
3. **Test Fix:** Validate fix works
4. **Bump Patch Version:** Increment patch number (x.y.Z)
5. **Announce Urgently:** Notify team immediately
6. **Document:** Add to version history

## Cross-Skill Coordination

### With Release Manager
- Coordinate on release notes and What's New
- Ensure changelog documentation aligns
- Update version references together

### With QA Tester
- Coordinate on test documentation
- Ensure documentation matches tested behavior
- Align on BDD scenario documentation

### With AOT Guru
- Coordinate on AOT/trimming documentation
- Ensure patterns are consistently documented

## References

- **[SKILL.md](./SKILL.md)** - Main skill documentation
- **[README.md](./README.md)** - Quick reference guide
- **[Requirements](../../../docs/content/contributing/design/technical-writer-skill-requirements.md)** - Full requirements
- **[Guru Philosophy](../../../.agents/guru-philosophy.md)** - Core principles
- **[Guru Creation Guide](../../../.agents/guru-creation-guide.md)** - Creation process
- **[Skill Matrix](../../../.agents/skill-matrix.md)** - Maturity tracking

---

**Version:** 0.1.0
**Last Review:** 2025-12-19
**Next Review:** Q1 2026
**Maintainer:** @DamianReeves
