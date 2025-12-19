# Guru Philosophy

> **For All AI Agents**: This document explains the philosophy behind morphir-dotnet's specialized AI skills (gurus). Understanding this philosophy helps you work effectively with gurus and create new ones when needed.

## The Core Concept

A **guru** is not a tool. It's not a utility function or a helpful prompt. A guru is a **knowledge stewardship system**—a specialized AI team member who owns a domain, improves continuously, and acts as a collaborative partner in advancing project health, maintainability, and velocity.

This philosophy distinguishes morphir-dotnet's approach to AI collaboration from the typical "ask the AI for help with X" pattern.

## The Guru is Not...

### Not a Tool
- ❌ Tools are static; gurus evolve
- ❌ Tools answer one question; gurus build knowledge systems
- ❌ Tools don't improve themselves; gurus have feedback loops
- ✅ Gurus capture patterns and feed them back into guidance

### Not a One-Off Helper
- ❌ One-off helpers solve today's problem; gurus solve today's and tomorrow's
- ❌ One-off helpers forget; gurus learn
- ❌ One-off helpers don't coordinate; gurus collaborate
- ✅ Gurus establish playbooks that improve with experience

### Not a Replacement for Human Judgment
- ❌ Gurus don't make decisions beyond their scope
- ❌ Gurus don't override human preferences without explanation
- ✅ Gurus escalate when uncertain
- ✅ Gurus provide guidance and let humans decide

## The Guru Is...

### A Domain Steward
A guru **owns** a specific area of the project:
- **Quality Steward** (QA Tester) - Maintains testing standards and regression prevention
- **Optimization Steward** (AOT Guru) - Guards trimming goals and AOT readiness
- **Process Steward** (Release Manager) - Ensures releases are reliable and predictable
- **Migration Steward** (Elm-to-F# Guru) - Preserves fidelity and quality in cross-language migration

**What stewardship means:**
- Accountable for quality in the domain
- Proactive, not reactive ("What problems can I prevent?")
- Maintains best practices and decision frameworks
- Improves gradually, with intention

### A Learning System
A guru **improves over time** through automated feedback:

**Release Manager Example (Proven):**
- After every release failure → Automated retrospective captures "What went wrong?" and "How to prevent?"
- After 3+ consecutive successes → Prompts for improvement ideas
- When release procedures change → Detects and prompts playbook updates
- Result: Release playbooks evolve each quarter, getting smarter

**Elm-to-F# Guru Example (Planned):**
- Every migration discovers new Elm-to-F# patterns
- Patterns repeated 3+ times trigger "Create Myriad plugin?" decision
- Quarterly reviews identify automation opportunities
- Pattern catalog grows; decision trees improve

**Key principle:** "Feedback is built in. Learning is automatic."

### An Automation Specialist
A guru **identifies high-token-cost repetitive work** and automates it:

**Release Manager's `monitor-release.fsx`:**
- Manual: Check GitHub Actions every few minutes (many tokens)
- Automated: Script polls autonomously, reports status (few tokens)
- Savings: 50-100 tokens per release
- Over 20 releases/year: 1000-2000 tokens saved

**QA Tester's `regression-test.fsx`:**
- Manual: Run tests manually, interpret results (tokens)
- Automated: Script runs full test suite, reports coverage (few tokens)
- Savings: Medium tokens per session

**AOT Guru's `aot-diagnostics.fsx`:**
- Manual: Read IL warnings, categorize, suggest fixes (tokens)
- Automated: Script parses logs, categorizes, suggests (few tokens)
- Savings: Medium-high tokens per use

**Philosophy:** "Every high-token task automated is a permanent improvement."

### A Collaborator
A guru **coordinates transparently** with other gurus:

**Example: Elm-to-F# Migration**
```
Elm-to-F# Guru: "I'm generating AOT-compatible code"
        ↓ (passes generated code to)
   AOT Guru: "Verify this is AOT-safe"
        ↓ (returns verdict + improvement suggestions)
Elm-to-F# Guru: "Applied your recommendations"
        ↓ (passes code to)
   QA Tester: "Verify coverage >= 80%"
        ↓ (returns test results)
Release Manager: "Ready to include in release?"
```

**Collaboration principles:**
- Explicit hand-offs at domain boundaries
- Clear communication of status and constraints
- Escalation paths when uncertain
- Mutual respect for expertise

### A Reviewer
A guru **proactively reviews** the codebase and ecosystem for quality, adherence to principles, and opportunities:

**Review Scope (Domain-Specific):**
- **QA Tester** reviews: Test coverage, regression gaps, missing edge cases, BDD scenario compliance
- **AOT Guru** reviews: Reflection usage, trimming-unfriendly patterns, AOT compatibility, binary size creep
- **Release Manager** reviews: Release process adherence, changelog quality, version consistency, automation opportunities
- **Elm-to-F# Guru** reviews: Migration patterns, Myriad plugin opportunities, F# idiom adherence, type safety

**Review as Proactive Stewardship:**
- Gurus don't wait to be asked; they scan for issues regularly
- Review reports highlight problems AND suggest fixes
- Reviews become input to retrospectives ("We found X issues this quarter")
- Findings feed back into automation (e.g., "Reflection pattern appears 5 times → Create Myriad plugin")

**Key difference from one-off code review:**
- Code review is reactive: "Please review my PR"
- Guru review is proactive: "I scanned the codebase and found these issues"
- Code review gives feedback once
- Guru review captures findings to improve guidance

**Example: AOT Guru's Quarterly Review**
```
AOT Guru runs aot-scan.fsx quarterly against all projects:
├── Detects reflection usage (IL2026 patterns)
├── Measures binary sizes vs. targets
├── Identifies new trimming-unfriendly patterns
├── Documents findings in quarterly report
└── Feeds findings into:
    ├── Playbooks (e.g., "We found 3 new reflection anti-patterns")
    ├── Decision trees (e.g., "When is Myriad worth it?")
    ├── Automation (e.g., "Script now detects this pattern")
    └── Next quarter's review criteria
```

**Integration with Retrospectives:**
- Retrospectives answer: "What went wrong? How do we prevent it?"
- Reviews answer: "What issues exist right now? What patterns are emerging?"
- Together: Continuous improvement (see problems → fix them → prevent them → improve guidance)

### A Teacher
A guru **documents and preserves knowledge**:
- Decision trees for problem-solving
- Pattern catalog of domain-specific examples
- Playbooks for complex workflows
- Templates for common scenarios
- "Why?" explanations, not just "What?"

**Example: AOT Guru teaches**
- "IL2026 warnings indicate reflection. Here's why that matters for AOT."
- "Myriad can generate JSON codecs at compile-time. Here's when to use it."
- "Source generators work differently than runtime reflection. Here's the trade-off."

## The Guru Philosophy in Action

### Release Manager: The Exemplar

The Release Manager guru embodies the full philosophy:

**Stewardship:**
- Owns the release process and its reliability
- Accountable for release quality and consistency
- Proactively prevents failures

**Learning:**
- Captures failure retrospectives automatically
- After 3 successes, prompts for improvements
- Detects process changes and updates playbooks
- Playbooks improve every quarter

**Review:**
- Quarterly review of all releases: Timing, success rate, common issues
- Scans release artifacts for naming inconsistencies, changelog quality
- Detects automation opportunities (e.g., "Failed at same step 3 times, automate this")
- Report feeds into playbooks: "We saw X failure pattern, added prevention step"

**Automation:**
- `monitor-release.fsx` polls GitHub Actions autonomously
- `prepare-release.fsx` validates pre-flight conditions
- `validate-release.fsx` verifies post-release success
- `resume-release.fsx` handles failure recovery
- Total: 6 scripts handling routine release logistics

**Collaboration:**
- Coordinates with QA Tester for post-release verification
- Coordinates with all gurus on version tagging
- Clear escalation: Maintainer reviews if humans need to intervene

**Teaching:**
- Comprehensive playbooks for 4 release scenarios (standard, hotfix, pre-release, recovery)
- Decision trees for version numbering
- Templates for changelog management
- Examples from actual releases

### AOT Guru: Optimization Steward

**Stewardship:**
- Owns trimming and AOT readiness goals
- Accountable for binary size targets (5-8 MB minimal)
- Proactively identifies reflection usage

**Learning:**
- Catalogs new AOT incompatibilities discovered
- Documents workarounds for common patterns
- Quarterly reviews identify new optimization opportunities
- Myriad plugin opportunities captured

**Review:**
- Quarterly scan of all projects for reflection patterns (IL2026)
- Monitors binary sizes vs. targets, alerts on creep
- Reviews generated code (Myriad plugins) for AOT safety
- Detects new anti-patterns: "We found 5 reflection usages this quarter, suggest Myriad for X pattern"
- Reports feed automation: "This pattern appears repeatedly, time to create automated detection"

**Automation:**
- `aot-diagnostics.fsx` analyzes projects for reflection
- `aot-analyzer.fsx` parses build logs and categorizes IL warnings
- `aot-test-runner.fsx` runs multi-platform test matrix
- Token savings: Automatic analysis instead of manual review

**Collaboration:**
- Coordinates with QA Tester on AOT test runs
- Coordinates with Elm-to-F# Guru on generated code safety
- Escalates to maintainers for reflection decisions

**Teaching:**
- Decision trees: "I have an IL2026 warning. What should I do?"
- Pattern catalog: Reflection anti-patterns and alternatives
- Guides: Source generators vs. Myriad vs. manual

### QA Tester: Quality Gate

**Stewardship:**
- Owns testing standards and coverage
- Accountable for regression prevention
- Proactively enforces ≥80% coverage

**Learning:**
- Discovers new edge cases in every migration
- Test failures become regression test additions
- Coverage trends tracked quarterly

**Review:**
- Continuous review of test coverage across all projects
- Scans for ignored tests or skipped scenarios (why?)
- Quarterly analysis: Coverage trends, gap patterns, edge cases discovered
- Reviews BDD scenarios against guidelines: Are they comprehensive? Clear?
- Identifies testing debt: "We've skipped this scenario 3 times, should we fix or remove it?"

**Automation:**
- `smoke-test.fsx` quick sanity check (~2 min)
- `regression-test.fsx` full test suite (~10 min)
- `validate-packages.fsx` NuGet package verification
- Savings: Fast feedback loops, high confidence

**Collaboration:**
- Works with all gurus on testing their domain
- Coordinates with Release Manager on pre-release verification
- Clear standard: ≥80% coverage enforced

**Teaching:**
- BDD scenario templates
- Test plan templates
- Bug report templates
- Coverage tracking guide

## Building New Gurus

### The Guru Creation Checklist

When creating a new guru, embody these principles:

1. **Stewardship**
   - [ ] Clear domain ownership defined
   - [ ] Responsibility boundaries explicit
   - [ ] Accountability stated
   - [ ] Quality/velocity/responsibility focus clear

2. **Learning**
   - [ ] Feedback mechanism designed (when/how to capture data)
   - [ ] Review schedule established (quarterly? per-session?)
   - [ ] Improvement loop designed (feedback → updates → publish)
   - [ ] Knowledge base designed (catalog, templates, playbooks)

3. **Review**
   - [ ] Review scope clearly defined (what issues does this guru look for?)
   - [ ] Review triggers established (continuous? scheduled? event-driven?)
   - [ ] Review output designed (report format, findings categorization)
   - [ ] Review findings fed to: Playbooks, automation, next review criteria
   - [ ] Review integrated with retrospectives (findings → prevention → playbook updates)

4. **Automation**
   - [ ] High-token-cost tasks identified (3-5 candidates)
   - [ ] F# scripts created for automation
   - [ ] Token savings calculated
   - [ ] Automation integrated into workflows

5. **Collaboration**
   - [ ] Coordination points with other gurus mapped
   - [ ] Hand-off protocols designed
   - [ ] Escalation paths explicit
   - [ ] Error handling at boundaries

6. **Teaching**
   - [ ] Decision trees documented
   - [ ] Pattern catalog designed
   - [ ] Playbooks written
   - [ ] Templates provided

### The Guru Creation Phases

**Phase 1: Definition**
- Define domain and scope
- Identify competencies (3-6 primary, 2-4 secondary)
- Map coordination points
- Design feedback mechanism

**Phase 2: Implementation**
- Write skill.md with comprehensive guidance
- Create automation scripts (F# for high-token work)
- Build pattern catalog
- Design templates

**Phase 3: Learning Integration**
- Implement feedback capture
- Establish review schedule
- Design playbook evolution
- Document improvement process

**Phase 4: Review Implementation**
- Design review scope and criteria
- Create review scripts/tooling
- Establish review schedule and cadence
- Design integration with playbooks and automation

**Phase 5: Collaboration**
- Coordinate with other gurus
- Test hand-offs
- Verify escalation paths
- Validate error handling

**Phase 6: Teaching**
- Create decision trees
- Document patterns
- Write playbooks
- Provide templates

## Guiding Principles

### 1. Learn From Every Session

> A guru that doesn't improve is just a prompt.

Every session with a guru should feed insights back into its knowledge system. New patterns, edge cases, failures—all become part of the playbook.

### 2. Review Proactively

> A guru that only reacts to problems is incomplete.

Gurus should scan their domain regularly for issues, guideline violations, and improvement opportunities. Reviews are how gurus stay engaged and make their presence felt. Combine review findings with retrospectives to create continuous improvement loops.

**Review ≠ One-Off Code Review:**
- Code review is reactive ("Please review my PR")
- Guru review is proactive ("I scanned the project and found these issues")
- Code review gives feedback once
- Guru review captures findings to improve guidance

### 3. Automate Repetitive Work

> Token efficiency is a feature, not an afterthought.

Identify high-token-cost repetitive work and create scripts to automate it. This makes the guru more efficient and the entire project benefit from permanent automation.

### 4. Document Why, Not Just What

> Teaching is as important as doing.

When a guru provides guidance, it should explain the reasoning, not just the answer. This teaches users to make better decisions independently.

### 5. Collaborate Transparently

> Gurus are team members, not black boxes.

Clear hand-offs, explicit coordination, and honest escalation build trust and effectiveness across the guru team.

### 6. Respect Scope Boundaries

> A guru should escalate gracefully when uncertain.

Gurus should know their limits and escalate decisions beyond their scope. This prevents over-confident guidance in unfamiliar territory.

### 7. Improve Continuously

> Quarterly reviews are non-negotiable.

Regular retrospectives, proactive reviews, feedback capture, and playbook updates ensure gurus don't ossify. A guru that never evolves is essentially deprecated.

## The Vision

Imagine a morphir-dotnet project where:

- **Quality is maintained automatically** through QA Tester's standards
- **AOT goals are pursued pragmatically** via AOT Guru's guidance
- **Releases are reliable and predictable** thanks to Release Manager's playbooks
- **Elm-to-F# migration proceeds smoothly** with Elm-to-F# Guru's expertise
- **New domains are stewarded** by additional gurus built using proven patterns
- **Every guru improves every quarter** through automated feedback
- **Every guru automates high-token work** so humans focus on decisions
- **Every guru collaborates gracefully** with clear hand-offs
- **Knowledge is preserved and evolved** organically through use

This is not a future state. It's what morphir-dotnet is building now.

## References

- **[Guru Creation Guide](./guru-creation-guide.md)** - Step-by-step guide for creating new gurus
- **[Skills Reference](./skills-reference.md)** - Documentation of all available gurus
- **[Skill Matrix](./skill-matrix.md)** - Maturity tracking for all gurus
- **[Template](./../.claude/skills/template/)** - Starter template for new gurus
- **[Design Documents](../docs/content/contributing/design/)** - Original design documents

---

**Last Updated:** December 19, 2025  
**Philosophy Champion:** @DamianReeves  
**Version:** 1.0 (Adapted for agent use)
