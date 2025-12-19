# Guru Creation Validation Checklist

> Use this checklist to validate a new guru skill before launching. Each section corresponds to a phase in the guru creation process.

## Phase 0: Planning

### Domain Definition
- [ ] Domain clearly defined and documented
- [ ] Domain boundaries explicit (what's included, what's excluded)
- [ ] Domain doesn't overlap significantly with existing gurus
- [ ] Domain justifies dedicated guru (not just a section in AGENTS.md)

### Competencies
- [ ] 3-6 primary competencies identified
- [ ] 2-4 secondary competencies identified
- [ ] Each competency has clear description
- [ ] Competencies are distinct and non-overlapping
- [ ] Competencies cover the domain comprehensively

### Responsibilities
- [ ] Core responsibilities documented (5-7 items)
- [ ] Responsibilities are specific and measurable
- [ ] Scope boundaries explicit (what guru does NOT do)
- [ ] Escalation criteria defined

### Coordination
- [ ] Coordination points with other gurus mapped
- [ ] Hand-off protocols designed
- [ ] Integration workflows documented
- [ ] Dependencies on other gurus identified

### Feedback Mechanism
- [ ] Feedback capture method designed
- [ ] Review schedule established
- [ ] Improvement triggers defined
- [ ] Learning system designed (catalog, trees, playbooks)

### Review Capability
- [ ] Review scope clearly defined
- [ ] Review triggers established
- [ ] Review output format chosen
- [ ] Integration with retrospectives designed
- [ ] Automation strategy designed

## Phase 1: Implementation (Alpha)

### Directory Structure
- [ ] `.claude/skills/{guru-id}/` created
- [ ] `SKILL.md` created (1000+ lines target)
- [ ] `README.md` created (300-400 lines target)
- [ ] `MAINTENANCE.md` created
- [ ] `metadata.yaml` created with all required fields
- [ ] `scripts/` directory created
- [ ] `templates/` directory created
- [ ] `patterns/` directory created

### SKILL.md Content
- [ ] Frontmatter complete (name, description)
- [ ] Overview section clear and comprehensive
- [ ] Primary responsibilities listed (5-7)
- [ ] Core competencies documented (3-6 detailed sections)
- [ ] Project-specific context included
- [ ] 3-5 decision trees provided
- [ ] 3-5 playbooks documented
- [ ] Review capability section complete
- [ ] Pattern catalog started (5-10 seed patterns)
- [ ] Automation scripts documented
- [ ] Integration points with other gurus documented
- [ ] Feedback loop section included
- [ ] Cross-agent compatibility section included
- [ ] Templates listed and explained
- [ ] Related resources linked
- [ ] File is 1000+ lines

### README.md Content
- [ ] "What This Guru Does" section clear
- [ ] "When to Use" scenarios listed (5+)
- [ ] Core competencies summarized
- [ ] Scripts table complete
- [ ] Common tasks with quick how-tos (4+)
- [ ] Pattern catalog index provided
- [ ] Examples included (3+)
- [ ] Integration explained
- [ ] Usage by agent documented
- [ ] Quick decision trees included
- [ ] Getting help section complete
- [ ] Feedback and evolution explained
- [ ] Quick reference card at end
- [ ] File is 300-400 lines

### MAINTENANCE.md Content
- [ ] Quarterly review checklist complete
- [ ] Feedback collection section documented
- [ ] Improvement process defined
- [ ] Automation opportunity checklist included
- [ ] Review capability maintenance section included
- [ ] Version history template provided
- [ ] Success metrics defined
- [ ] Maintenance schedule documented
- [ ] Emergency update process defined
- [ ] Cross-project portability documented
- [ ] Contact and escalation info provided

### metadata.yaml Content
- [ ] Basic identity complete (id, name, version, status)
- [ ] Description clear and comprehensive
- [ ] Trigger keywords defined (5+)
- [ ] Domain and scope documented
- [ ] Competencies listed
- [ ] Review capability fully configured
- [ ] Automation scripts listed
- [ ] Coordination dependencies mapped
- [ ] Feedback and learning configured
- [ ] Cross-agent compatibility documented
- [ ] Maturity tracking configured
- [ ] Maintenance info provided
- [ ] References complete

### Automation Scripts
- [ ] 3-5 scripts implemented
- [ ] Each script has clear purpose statement
- [ ] Token savings estimated for each
- [ ] Scripts use standard F# (no agent-specific features)
- [ ] Scripts have clear usage documentation
- [ ] Scripts produce JSON output option
- [ ] Scripts have proper exit codes (0/1/2)
- [ ] Scripts document dependencies
- [ ] Scripts work on Windows, Mac, Linux (if applicable)
- [ ] Scripts tested with real data

### Pattern Catalog
- [ ] 5-10 seed patterns documented
- [ ] Each pattern has: Name, Category, Problem, Solution, When to Use, When to Avoid
- [ ] Patterns relevant to domain
- [ ] Patterns include code examples
- [ ] Patterns reference related patterns

### Templates
- [ ] 3-5 templates created
- [ ] Templates have clear purpose
- [ ] Templates have usage instructions
- [ ] Templates include placeholders with descriptions
- [ ] Templates tested for usability

## Phase 2: Validation (Beta)

### Quality Checks
- [ ] SKILL.md reviewed for clarity
- [ ] SKILL.md reviewed for completeness
- [ ] SKILL.md reviewed for consistency
- [ ] All scripts tested and working
- [ ] All decision trees validated with real scenarios
- [ ] All playbooks verified (complete steps, no gaps)
- [ ] All templates usable (clear placeholders, good examples)
- [ ] Pattern catalog reviewed for relevance

### Review Capability Testing
- [ ] Review scripts implemented
- [ ] Review scripts tested on real data
- [ ] Review output format validated
- [ ] Review findings categorized properly
- [ ] Trends analysis working (if applicable)
- [ ] Recommendations generated correctly
- [ ] Automation opportunities identified correctly
- [ ] Integration with retrospectives tested

### Cross-Agent Compatibility
- [ ] Claude Code usage verified (@skill invocation)
- [ ] Copilot equivalent guidance provided
- [ ] Other agents can access documentation
- [ ] Scripts work for all agents
- [ ] Decision trees accessible to all
- [ ] Playbooks work for all

### Coordination Testing
- [ ] Integration with other gurus tested
- [ ] Hand-off protocols validated
- [ ] Escalation paths verified
- [ ] Error handling at boundaries working

### Feedback Collection
- [ ] Feedback mechanism tested
- [ ] Feedback captured successfully
- [ ] Quarterly review process validated
- [ ] Improvement loop working

## Phase 3: Launch (Stable)

### Documentation Updates
- [ ] AGENTS.md updated (reference new guru)
- [ ] .agents/skills-reference.md updated (new section)
- [ ] .agents/skill-matrix.md updated (new guru entry)
- [ ] .agents/capabilities-matrix.md updated (if needed)
- [ ] README.md (project root) updated (if significant)

### Team Communication
- [ ] Announcement prepared (what, why, how to use)
- [ ] Team notified in appropriate channels
- [ ] Initial feedback solicited
- [ ] Questions answered

### Version Control
- [ ] All files committed
- [ ] Git tag created: `{guru-id}-v{version}`
- [ ] Tag pushed to repository
- [ ] Version documented in all files

### Success Metrics Baseline
- [ ] Pattern count: {N}
- [ ] Script count: {N}
- [ ] Token savings: ~{N} per {unit}
- [ ] Usage frequency measurement started
- [ ] Feedback quality tracked

## Phase 4: Evolution (After 1 Quarter)

### Quarterly Review Completion
- [ ] Feedback collected and analyzed
- [ ] 2-3 improvements identified
- [ ] Improvements implemented
- [ ] Documentation updated
- [ ] Version bumped
- [ ] Team notified of improvements

### Pattern Growth
- [ ] Pattern catalog grown to 15+ patterns
- [ ] New patterns validated
- [ ] Pattern categories organized
- [ ] Pattern relationships documented

### Automation Evolution
- [ ] 1+ new script created (if opportunities found)
- [ ] Existing scripts improved
- [ ] Token savings validated
- [ ] Automation opportunities tracked

### Integration Maturity
- [ ] Coordination with other gurus proven
- [ ] Review capability working smoothly
- [ ] Feedback loop generating insights
- [ ] Continuous improvement cycle established

## Red Flags

Stop and reconsider if any of these apply:

### Domain Issues
- [ ] Domain overlaps >50% with existing guru
- [ ] Domain too narrow (could be AGENTS.md section)
- [ ] Domain too broad (could be 2-3 separate gurus)
- [ ] Domain boundaries unclear or contested

### Competency Issues
- [ ] Fewer than 3 competencies
- [ ] Competencies overlap significantly
- [ ] Competencies don't cover domain
- [ ] Competencies too abstract or vague

### Automation Issues
- [ ] No high-token-cost tasks identified
- [ ] Scripts don't save significant tokens (<200 per use)
- [ ] Automation opportunities rare (<1 per quarter)

### Coordination Issues
- [ ] No clear integration points with other gurus
- [ ] Hand-offs unclear or ambiguous
- [ ] Escalation paths undefined
- [ ] Error handling missing

### Review Issues
- [ ] Review scope unclear or too broad
- [ ] Review triggers not defined
- [ ] Review findings not actionable
- [ ] Integration with retrospectives missing

## Graduation Criteria

### Alpha → Beta
- [ ] All Phase 1 (Implementation) items complete
- [ ] Review capability implemented
- [ ] Feedback mechanism working
- [ ] 15+ patterns in catalog
- [ ] Coordination tested

### Beta → Stable
- [ ] 20+ patterns in catalog
- [ ] 2+ quarters of successful use
- [ ] Review capability proven reliable
- [ ] Automated feedback generating insights
- [ ] Token efficiency documented
- [ ] Continuous improvement cycle established

## Notes

Use this space to track validation progress, issues found, and resolutions:

```
Date: {YYYY-MM-DD}
Phase: {Phase name}
Status: {In Progress / Blocked / Complete}
Issues: {Any issues found}
Resolution: {How issues were resolved}
Next Steps: {What's next}
```

---

**Validator:** {Your name/handle}  
**Date Started:** {YYYY-MM-DD}  
**Date Completed:** {YYYY-MM-DD}  
**Final Status:** {Alpha / Beta / Stable}
