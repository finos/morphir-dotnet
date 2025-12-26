# Maintenance Guide: Morphir Application Architect Skill

This document describes the maintenance and evolution process for the Morphir Application Architect skill.

## Overview

The Morphir Application Architect skill is a living artifact that evolves with:
- Morphir-dotnet architecture changes
- New patterns and best practices discovered
- User feedback and common questions
- Ecosystem updates (morphir-elm, morphir-scala, etc.)

**Maintainer**: Damian Reeves (@DamianReeves)
**Review Frequency**: Quarterly (Jan, Apr, Jul, Oct)
**Version**: 1.0.0-alpha
**Status**: Active development

## Maintenance Schedule

### Quarterly Reviews (Required)

**When**: 1st week of Jan, Apr, Jul, Oct

**Review checklist**:

1. **Knowledge Base Freshness**
   - [ ] Review all 6 knowledge bases for accuracy
   - [ ] Check for deprecated patterns or libraries
   - [ ] Add new patterns discovered in last quarter
   - [ ] Update code examples for API changes
   - [ ] Verify external links still work

2. **Pattern Catalog Validation**
   - [ ] Run architecture-review.fsx script (when available)
   - [ ] Review codebase for new pattern usage
   - [ ] Document new patterns in appropriate KB
   - [ ] Update pattern selection decision trees
   - [ ] Check cross-language pattern parity (F#/C#)

3. **Decision Logs Sync**
   - [ ] Review `.agents/decisionlogs/architectural-decisions.md`
   - [ ] Add new ADRs to skill context
   - [ ] Mark superseded decisions
   - [ ] Update decision trees based on new ADRs

4. **Playbook Effectiveness**
   - [ ] Collect user feedback on playbooks
   - [ ] Identify missing playbook scenarios
   - [ ] Update playbooks with new insights
   - [ ] Add common troubleshooting steps

5. **Integration Testing**
   - [ ] Test skill invocation with real scenarios
   - [ ] Verify cross-skill handoffs work
   - [ ] Check automation script functionality
   - [ ] Validate template usability

6. **Version Management**
   - [ ] Update version number if changes made
   - [ ] Document changes in README.md version history
   - [ ] Tag release if significant updates
   - [ ] Announce changes in project channels

### Event-Driven Updates (As Needed)

**Trigger events requiring immediate updates**:

1. **Breaking API Changes**
   - When: morphir-dotnet introduces breaking changes to IR types
   - Action: Update all affected code examples within 1 week
   - Priority: Critical

2. **New Morphir IR Version**
   - When: New IR schema version released
   - Action: Document migration patterns, update decision trees
   - Priority: High

3. **Ecosystem Updates**
   - When: Major morphir-elm or morphir-scala changes
   - Action: Review for applicable patterns, update ecosystem KB
   - Priority: Medium

4. **User-Reported Issues**
   - When: Skill gives incorrect or outdated advice
   - Action: Immediate fix, add to pattern catalog if recurring
   - Priority: High to Critical (depending on impact)

5. **New AOT/Trimming Requirements**
   - When: AOT Guru skill identifies new constraints
   - Action: Update pattern recommendations for AOT compatibility
   - Priority: High

## Knowledge Base Maintenance

### 1. ecosystem-knowledge-base.md

**Update frequency**: Quarterly + event-driven

**Maintenance tasks**:
- Sync with morphir-elm releases (check monthly)
- Document cross-repository patterns discovered
- Update repository file structure if changed
- Add new ecosystem tools/libraries
- Verify hyperlinks to external repos

**Quality checks**:
- All repos listed have correct GitHub URLs
- File paths verified against latest repo state
- Cross-references between repos are accurate
- No orphaned or deprecated entries

### 2. language-design-patterns.md

**Update frequency**: Quarterly

**Maintenance tasks**:
- Add patterns discovered in code reviews
- Update F#/C# parity examples
- Document anti-patterns to avoid
- Add performance characteristics if measured
- Cross-reference with actual codebase usage

**Quality checks**:
- All code examples compile
- Pattern selection guidance is clear
- Cross-language comparisons are fair
- References to morphir-dotnet code are current

### 3. visitor-pattern-implementations.md

**Update frequency**: Semi-annual (Jan, Jul)

**Maintenance tasks**:
- Add new visitor variants if discovered
- Update performance benchmarks if available
- Document stack-safety considerations
- Add real-world usage examples from codebase
- Update visitor selection decision tree

**Quality checks**:
- All 8 visitor variants have working examples
- Decision matrix reflects actual project needs
- Code examples follow current conventions
- References to IR types are up-to-date

### 4. computation-expressions-for-ast.md

**Update frequency**: Quarterly

**Maintenance tasks**:
- Monitor Fabulous/Fabulous.AST/Fun.Blazor updates
- Document new CE patterns discovered
- Add morphir-dotnet CE usage examples if implemented
- Update performance/ergonomics trade-offs
- Track F# language CE enhancements

**Quality checks**:
- External library versions documented
- Code examples use current API
- Morphir.Live integration examples current
- CE builder implementations are complete

### 5. functional-programming-patterns.md

**Update frequency**: Quarterly

**Maintenance tasks**:
- Add new monad/functor patterns if needed
- Update Railway-Oriented Programming examples
- Document lens usage in actual transformations
- Add error handling case studies
- Cross-reference with morphir-dotnet validation code

**Quality checks**:
- All 18 patterns have clear examples
- Mathematical laws documented where applicable
- Practical usage guidance is actionable
- Performance implications noted

### 6. compiler-services-metaprogramming.md

**Update frequency**: Quarterly + FCS/Roslyn updates

**Maintenance tasks**:
- Track FCS version compatibility
- Monitor Roslyn API changes
- Update Source Generator examples for latest SDK
- Document Myriad plugin compatibility
- Add new code generation use cases

**Quality checks**:
- FCS version tested and documented
- Roslyn examples use current NuGet packages
- Source Generator incremental pipeline correct
- Myriad configuration up-to-date

## Decision Log Maintenance

### Architectural Decisions (ADRs)

**Location**: `.agents/decisionlogs/architectural-decisions.md`

**Update frequency**: As decisions are made

**Maintenance tasks**:
- Add new ADRs when architectural decisions occur
- Link ADRs to skill playbooks where relevant
- Mark superseded ADRs with [SUPERSEDED] tag
- Update decision trees based on ADR outcomes
- Cross-reference ADRs in knowledge bases

**ADR Template**:
```markdown
### ADR-XXX: [Decision Title]

**Date**: YYYY-MM-DD
**Status**: Accepted | Proposed | Superseded
**Context**: Why this decision is needed
**Decision**: What we decided
**Consequences**: Implications for architecture
**Related Patterns**: Links to KB patterns
```

**Quality checks**:
- All ADRs numbered sequentially
- Status field accurate
- Superseded ADRs link to replacement
- ADRs referenced in skill.md where applicable

## Automation Script Development

### Planned Scripts

**Priority 1 (Next Quarter)**:
1. **architecture-review.fsx**
   - Scan codebase for anti-patterns
   - Detect pattern usage trends
   - Generate architectural health report
   - Suggest playbooks for issues found

2. **ir-consistency-check.fsx**
   - Verify Classic IR / Modern IR sync
   - Check for missing conversion functions
   - Validate type parity between F#/C# IR
   - Report inconsistencies

**Priority 2 (Future)**:
3. **pattern-matcher.fsx**
   - Identify applicable patterns in code
   - Suggest refactoring opportunities
   - Match code to knowledge base patterns
   - Generate pattern usage statistics

4. **knowledge-base-validator.fsx**
   - Verify code examples compile
   - Check external links
   - Validate cross-references
   - Report stale content

**Script Development Guidelines**:
- Use F# Interactive (.fsx) for portability
- Follow `.claude/skills/qa-tester/scripts/` conventions
- Document in scripts/README.md (when created)
- Add to quarterly review checklist
- Test on CI before relying on output

## Template Development

### Planned Templates

**Location**: `.claude/skills/morphir-architect/templates/`

**Priority 1**:
1. **new-ast-type.md**
   - Checklist for designing new AST/IR types
   - F# DU vs C# sealed record guidance
   - Generic attributes decision tree
   - Testing strategy template

2. **transformation-pipeline.md**
   - Railway-Oriented Programming setup
   - Error type design
   - Validation step template
   - Testing approach

3. **visitor-implementation.md**
   - Visitor variant selection
   - Boilerplate code generation
   - Testing strategy
   - Performance considerations

**Template Structure**:
```markdown
# Template: [Name]

## When to Use This Template
[Triggers and scenarios]

## Prerequisites
- [ ] Checklist of prerequisites

## Step-by-Step Guide
1. [Detailed steps]

## Code Scaffolding
[Reusable code snippets]

## Testing Strategy
[How to test the result]

## Related Patterns
[Links to knowledge bases]
```

**Quality Criteria**:
- Templates tested on real scenarios
- Code scaffolding compiles
- Clear decision points documented
- Examples from actual codebase included

## Pattern Catalog Evolution

### Adding New Patterns

**Process**:
1. **Discovery**
   - Pattern used in PR review
   - Found in external research
   - User request for guidance
   - Identified in quarterly review

2. **Validation**
   - Verify pattern solves real problem
   - Check for existing similar pattern
   - Confirm morphir-dotnet applicability
   - Test in prototype or PR

3. **Documentation**
   - Add to appropriate knowledge base
   - Include code example (F# and C# if applicable)
   - Document when to use / when not to use
   - Add to pattern selection decision tree
   - Cross-reference related patterns

4. **Integration**
   - Update skill.md if core pattern
   - Add to playbook if workflow pattern
   - Update README.md quick reference
   - Mention in version history

5. **Review**
   - Get maintainer approval
   - Test with skill invocation
   - Gather initial feedback
   - Schedule quarterly re-evaluation

### Deprecating Patterns

**Triggers**:
- Better pattern discovered
- Pattern causes issues in practice
- Ecosystem deprecation
- AOT/trimming incompatibility

**Process**:
1. Mark pattern as [DEPRECATED] in KB
2. Document replacement pattern
3. Add migration guide
4. Update decision trees
5. Remove from quick reference tables
6. Keep historical documentation for 2 quarters
7. Archive pattern if no usage in codebase

## Integration with Other Skills

### Cross-Skill Maintenance

**Dependencies**:
- **Technical Writer**: Documentation pattern updates
- **AOT Guru**: AOT compatibility pattern validation
- **QA Tester**: Testing strategy patterns
- **Elm-to-F# Guru**: Elm pattern translation

**Coordination**:
- Notify other skills when patterns change
- Align decision trees across skills
- Share automation scripts where applicable
- Coordinate quarterly reviews

**Escalation Paths**:
- Conflicting pattern recommendations → Maintainer review
- Breaking IR changes → Coordinate with all skills
- New AOT constraints → Update pattern compatibility
- Elm ecosystem changes → Sync Elm-to-F# mappings

## User Feedback Collection

### Feedback Channels

1. **Direct Skill Invocation**
   - Monitor skill usage logs (if available)
   - Track common question patterns
   - Identify missing playbooks
   - Note confusion points

2. **PR Reviews**
   - Patterns suggested but not followed → Why?
   - New patterns discovered → Add to catalog
   - Anti-patterns detected → Add to KB
   - Decision tree failures → Refine logic

3. **Issue Reports**
   - Skill gave wrong advice → Immediate fix
   - Missing pattern → Add to backlog
   - Unclear playbook → Clarify documentation
   - Automation request → Script backlog

4. **Quarterly Survey**
   - Ask project contributors for feedback
   - Identify most/least useful patterns
   - Gather playbook effectiveness data
   - Collect wish list items

### Feedback Action Process

**High Priority** (respond within 1 week):
- Incorrect pattern recommendation
- Broken code example
- Missing critical guidance
- Security/AOT issues

**Medium Priority** (respond within quarter):
- New pattern requests
- Playbook improvements
- Decision tree refinements
- Template additions

**Low Priority** (backlog):
- Nice-to-have patterns
- Optimization suggestions
- Additional examples
- External resource links

## Quality Metrics

### Skill Health Indicators

**Track quarterly**:
1. **Knowledge Base Freshness**
   - Last update date for each KB
   - Number of stale examples
   - Broken external links
   - Deprecated pattern count

2. **Pattern Catalog Coverage**
   - Patterns documented vs patterns used in codebase
   - Missing critical patterns identified
   - Redundant patterns merged
   - F#/C# parity percentage

3. **User Satisfaction**
   - Skill invocation success rate
   - Pattern recommendation acceptance rate
   - Playbook completion rate
   - Escalation frequency

4. **Code Quality Impact**
   - PRs using skill patterns
   - Anti-pattern reduction over time
   - Test coverage correlation
   - AOT compatibility improvements

**Success Criteria**:
- All KBs updated within last quarter
- Zero stale code examples
- 90%+ pattern recommendation acceptance
- < 5% escalation rate

### Continuous Improvement

**Review process**:
1. Collect metrics quarterly
2. Identify improvement areas
3. Prioritize updates
4. Implement changes
5. Measure impact next quarter

**Improvement targets**:
- Increase pattern catalog coverage by 10% annually
- Reduce average playbook completion time
- Improve skill invocation success rate
- Maintain 95%+ code example accuracy

## Version Management

### Versioning Scheme

**Format**: `MAJOR.MINOR.PATCH-STATUS`

**Examples**:
- `1.0.0-alpha` - Initial release, active development
- `1.0.0-beta` - Feature complete, testing
- `1.0.0` - Stable release
- `1.1.0` - Minor updates (new patterns, playbooks)
- `2.0.0` - Major updates (restructure, breaking changes)

### Version Update Triggers

**Patch** (1.0.0 → 1.0.1):
- Bug fixes in code examples
- Typo corrections
- Broken link fixes
- Minor clarifications

**Minor** (1.0.0 → 1.1.0):
- New patterns added
- New playbooks added
- New automation scripts
- Decision tree improvements
- Template additions

**Major** (1.0.0 → 2.0.0):
- Skill restructure
- Breaking changes to playbooks
- Removal of deprecated patterns
- Fundamental approach changes
- Major IR redesign

### Release Process

1. **Prepare Release**
   - Update version in metadata.yaml
   - Update version in README.md
   - Document changes in version history
   - Run all quality checks

2. **Create Release Notes**
   - Summarize changes
   - Highlight breaking changes
   - List new patterns/playbooks
   - Note deprecations

3. **Tag and Publish**
   - Git tag: `morphir-architect-v1.0.0`
   - Update skill status if graduating (alpha → beta → stable)
   - Announce in project channels

4. **Post-Release**
   - Monitor for issues
   - Collect feedback
   - Plan next iteration

## Troubleshooting

### Common Maintenance Issues

**Issue**: Code examples don't compile

**Solution**:
1. Check for API changes in morphir-dotnet
2. Update NuGet package versions
3. Test examples in isolation
4. Update knowledge base
5. Add compiler version notes

---

**Issue**: Pattern recommendations conflict

**Solution**:
1. Review context where conflict occurs
2. Refine decision tree logic
3. Add disambiguating questions
4. Document edge cases
5. Escalate if fundamental disagreement

---

**Issue**: Knowledge base out of sync with codebase

**Solution**:
1. Run quarterly review checklist
2. Compare KB patterns to actual usage
3. Update outdated examples
4. Add new patterns found in code
5. Mark deprecated patterns

---

**Issue**: Playbook doesn't work for user scenario

**Solution**:
1. Understand user's specific context
2. Identify missing steps
3. Add scenario variant to playbook
4. Consider new playbook if significantly different
5. Update decision tree to route correctly

---

**Issue**: Automation scripts fail

**Solution**:
1. Check F# / .NET SDK version compatibility
2. Verify file paths still correct
3. Test script in isolation
4. Update for API changes
5. Add error handling and diagnostics

## Contact and Support

**Maintainer**: Damian Reeves (@DamianReeves)

**For Issues**:
- Skill errors: Create issue with label `skill:morphir-architect`
- Pattern questions: Use skill invocation or ask in PR
- Maintenance concerns: Contact maintainer directly
- Urgent fixes: Label issue `priority:high`

**For Contributions**:
- New patterns: Submit PR with KB update
- Playbook improvements: Submit PR with changes
- Automation scripts: Follow scripts README guidelines
- Templates: Follow template structure above

---

**Last Updated**: 2025-12-24
**Next Scheduled Review**: 2026-01-07 (Q1 2026)
