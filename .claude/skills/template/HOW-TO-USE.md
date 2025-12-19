# Guru Skill Template

This directory contains a complete template for creating new guru skills in the morphir-dotnet project. Use this template as a starting point when implementing a new guru.

## What's Included

### Core Files

1. **metadata.yaml** - Skill configuration and metadata
   - 82 placeholders to fill in
   - Defines identity, triggers, competencies, review capability, coordination
   - Used by Claude Code for skill invocation

2. **SKILL.md** - Main skill documentation
   - 285 placeholders to fill in
   - ~1000-1200 lines target
   - Comprehensive guidance for using the skill
   - Includes review capability section

3. **README.md** - Quick reference guide
   - 300-400 lines target
   - User-friendly overview
   - Common tasks and examples
   - Cross-agent usage instructions

4. **MAINTENANCE.md** - Evolution and maintenance guide
   - Quarterly review checklist
   - Feedback collection process
   - Success metrics
   - Version history

5. **validation-checklist.md** - Validation checklist
   - 204 items to validate guru implementation
   - Covers all phases from planning to stable
   - Graduation criteria
   - Red flags to watch for

### Supporting Directories

6. **scripts/** - Automation script templates
   - `script-template.fsx` - Generic script template
   - `review-template.fsx` - Review capability script template
   - F# scripts with Spectre.Console for rich output

7. **templates/** - Reusable templates
   - `decision-template.md` - Decision tree template
   - `workflow-template.md` - Playbook/workflow template

8. **patterns/** - Pattern catalog template
   - `pattern-template.md` - Pattern documentation template

## How to Use This Template

### Step 1: Copy the Template

```bash
cp -r .claude/skills/template .claude/skills/{your-guru-id}
cd .claude/skills/{your-guru-id}
```

### Step 2: Fill in Placeholders

All placeholders follow the pattern `{description}`. Search for `{` to find them:

```bash
# Find all placeholders in a file
grep -n "{" SKILL.md

# Count placeholders
grep -c "{" SKILL.md
```

**Recommended order:**
1. Start with `metadata.yaml` (defines structure)
2. Then `SKILL.md` (main content)
3. Then `README.md` (user-friendly summary)
4. Then `MAINTENANCE.md` (evolution process)
5. Implement scripts in `scripts/`
6. Create templates in `templates/`
7. Document seed patterns in `patterns/`

### Step 3: Follow the Creation Guide

Refer to [.agents/guru-creation-guide.md](../../../.agents/guru-creation-guide.md) for comprehensive step-by-step instructions.

### Step 4: Validate

Use `validation-checklist.md` to ensure completeness:

```bash
# Work through checklist phase by phase
# Phase 0: Planning
# Phase 1: Implementation (Alpha)
# Phase 2: Validation (Beta)
# Phase 3: Launch (Stable)
```

### Step 5: Test

Before launching:
- [ ] All scripts run successfully
- [ ] Decision trees validated with real scenarios
- [ ] Playbooks complete (no missing steps)
- [ ] Templates usable (clear examples)
- [ ] Cross-agent compatibility verified
- [ ] Coordination with other gurus tested

### Step 6: Launch

1. Update `.agents/skills-reference.md`
2. Update `.agents/skill-matrix.md`
3. Update `AGENTS.md` (if needed)
4. Announce to team
5. Collect initial feedback

## Template Philosophy

This template embodies the [guru philosophy](../../../.agents/guru-philosophy.md):

1. **Stewardship** - Clear domain ownership and accountability
2. **Learning** - Built-in feedback mechanisms and quarterly reviews
3. **Review** - Proactive monitoring and quality assurance
4. **Automation** - Token-efficient scripts for high-cost tasks
5. **Collaboration** - Clear integration with other gurus
6. **Teaching** - Decision trees, patterns, playbooks

## Key Features

### Review Capability Built-In

Every guru has review capability from the start:
- Review scope defined
- Review triggers specified
- Review scripts templated
- Integration with retrospectives designed

This ensures gurus are proactive, not just reactive.

### Cross-Agent Compatible

Templates work for:
- Claude Code (interactive @skill invocation)
- GitHub Copilot (documentation + scripts)
- Cursor, Windsurf, Aider (documentation + scripts)

### Automation-First

Templates encourage automation:
- Script templates with token savings estimates
- Review script template for proactive monitoring
- Pattern detection for automation opportunities

### Scalable and Consistent

All gurus follow the same structure:
- Consistent file organization
- Standard sections in documentation
- Common validation process
- Predictable evolution cycle

## Template Statistics

- **Total placeholders:** ~400 across all files
- **Expected implementation time:** 8-16 hours for alpha
- **Target documentation:** 2000-2500 lines total
- **Scripts:** 3-5 minimum
- **Patterns:** 5-10 seed patterns (growing to 20+)

## Examples

See existing gurus for reference:
- **[QA Tester](./../qa-tester/)** - Stable guru example
- **[AOT Guru](./../aot-guru/)** - Stable guru example
- **[Release Manager](./../release-manager/)** - Stable guru example

## Questions?

- **Creation process:** See [.agents/guru-creation-guide.md](../../../.agents/guru-creation-guide.md)
- **Philosophy:** See [.agents/guru-philosophy.md](../../../.agents/guru-philosophy.md)
- **All gurus:** See [.agents/skills-reference.md](../../../.agents/skills-reference.md)
- **Maturity tracking:** See [.agents/skill-matrix.md](../../../.agents/skill-matrix.md)

## Contributing

Found ways to improve this template?

1. Test improvements with a new guru
2. Validate changes don't break existing usage
3. Update template and this README
4. Submit PR with `guru-template` label

---

**Version:** 1.0  
**Last Updated:** 2025-12-19  
**Maintained By:** Project maintainers
