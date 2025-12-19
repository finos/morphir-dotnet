# Skill Matrix

> **For All AI Agents**: This document tracks the maturity and capabilities of all gurus (specialized AI skills) in morphir-dotnet. Use this to understand which gurus are available, their current status, and how they coordinate.

## Overview

The skill matrix provides a comprehensive view of:
- All gurus (existing and planned)
- Maturity phases for each guru
- Review capability status
- Coordination dependencies
- Graduation criteria

## Maturity Phases

Gurus progress through four maturity phases:

### Phase 0: Candidate
**Status:** Concept accepted, implementation not started  
**Duration:** Until implementation begins  
**Criteria:**
- Domain clearly defined
- Competencies identified (3+)
- Coordination points mapped
- Approved by maintainers

### Phase 1: Alpha
**Status:** Implemented, internal testing  
**Duration:** First quarter of use  
**Criteria:**
- [ ] Directory structure created
- [ ] skill.md complete (1000+ lines)
- [ ] README.md created
- [ ] MAINTENANCE.md documented
- [ ] 3-5 automation scripts working
- [ ] 5-10 seed patterns documented
- [ ] Initial templates created
- [ ] Team feedback collected

### Phase 2: Beta
**Status:** Review capability working, coordinating with peers  
**Duration:** Second quarter of use  
**Criteria:**
- [ ] Review capability implemented
- [ ] Review scripts tested on real data
- [ ] Feedback mechanism working
- [ ] Quarterly review completed (first)
- [ ] 15+ patterns in catalog
- [ ] 3+ improvements from feedback
- [ ] Coordination with other gurus tested
- [ ] Cross-agent compatibility verified

### Phase 3: Stable
**Status:** All features mature, predictable quarterly improvement  
**Duration:** Ongoing  
**Criteria:**
- [ ] 20+ patterns in catalog
- [ ] Review capability proven reliable
- [ ] Automated feedback generating insights
- [ ] 2+ quarters of successful evolution
- [ ] Token efficiency documented
- [ ] Cross-project reuse strategy documented
- [ ] Continuous improvement cycle established
- [ ] Integration with other gurus proven

## Current Gurus

### 1. QA Tester Guru

**Maturity Phase:** Stable (Phase 3)  
**Location:** `.claude/skills/qa-tester/`  
**Domain:** Quality assurance, testing, and verification

**Capabilities:**
- ✅ Test plan development
- ✅ Regression testing
- ✅ End-to-end testing
- ✅ Coverage monitoring
- ✅ Issue reporting
- ✅ Test automation

**Review Capability:**
- **Status:** Implemented and working
- **Scope:** Test coverage, ignored tests, BDD compliance, edge case gaps
- **Frequency:** Continuous (PR verification) + Manual (smoke/regression tests)
- **Triggers:** PR workflow, pre-release validation, manual playbook execution
- **Output:** Coverage trends, gap identification, testing debt reports
- **Integration:** Feeds findings to retrospectives and playbooks

**Automation Scripts:**
- `smoke-test.fsx` - Quick validation (~5-10 min, saves ~500 tokens)
- `regression-test.fsx` - Comprehensive testing (~30-45 min, saves ~1000 tokens)
- `validate-packages.fsx` - Package validation (saves ~300 tokens)

**Coordination Dependencies:**
- **→ Release Manager:** Post-release verification (QA → Release Manager)
- **← All gurus:** Testing support for guru-specific domains
- **→ AOT Guru:** Validate trimmed/AOT builds

**Maturity Evidence:**
- 20+ testing patterns documented
- 3+ quarters of continuous use
- Proven cross-agent compatibility
- Token savings documented
- Review capability integrated with CI/CD

---

### 2. AOT Guru

**Maturity Phase:** Stable (Phase 3)  
**Location:** `.claude/skills/aot-guru/`  
**Domain:** Native AOT, trimming, and binary size optimization

**Capabilities:**
- ✅ Single-file trimmed executables
- ✅ AOT readiness assessment
- ✅ Trimming diagnostics
- ✅ Size optimization analysis
- ✅ Reflection workarounds
- ✅ F# and Myriad expertise

**Review Capability:**
- **Status:** Implemented and working
- **Scope:** Reflection usage (IL2026), binary size creep, trimming-unfriendly patterns, AOT compatibility
- **Frequency:** Quarterly comprehensive review + On-demand
- **Triggers:** Quarterly schedule, major dependency updates, size target exceeded
- **Output:** IL warning summary, size trends, reflection hot spots, AOT readiness score, plugin recommendations
- **Integration:** Quarterly reports feed playbook updates, automation improvements, Myriad plugin decisions

**Automation Scripts:**
- `aot-diagnostics.fsx` - Project analysis (saves ~800 tokens)
- `aot-analyzer.fsx` - Build log parsing (saves ~600 tokens)
- `aot-test-runner.fsx` - Multi-config test matrix (saves ~1200 tokens)

**Coordination Dependencies:**
- **← Elm-to-F# Guru:** Review generated code for AOT safety (Elm-to-F# → AOT)
- **← Release Manager:** AOT verification in releases (Release → AOT)
- **→ QA Tester:** E2E testing of trimmed builds (AOT → QA)

**Maturity Evidence:**
- 15+ optimization patterns documented
- Quarterly reviews completed successfully
- Myriad plugin recommendations proven
- Binary size targets maintained
- Review findings integrated into decision trees

---

### 3. Release Manager

**Maturity Phase:** Stable (Phase 3)  
**Location:** `.claude/skills/release-manager/`  
**Domain:** Release lifecycle, version management, deployment

**Capabilities:**
- ✅ Version management
- ✅ Changelog management
- ✅ Release preparation
- ✅ Release execution
- ✅ Release verification
- ✅ Release documentation

**Review Capability:**
- **Status:** Implemented and working
- **Scope:** Process adherence, changelog quality, version consistency, automation opportunities
- **Frequency:** Per-release (preparation + verification) + Quarterly retrospective
- **Triggers:** Pre-release (preparation), post-release (verification), quarterly review
- **Output:** Process compliance report, changelog quality score, version checks, documentation completeness
- **Integration:** Retrospectives capture failures/successes, feed playbook evolution

**Automation Scripts:**
- `prepare-release.fsx` - Pre-flight validation (saves ~700 tokens)
- `monitor-release.fsx` - Autonomous workflow polling (saves ~1000 tokens)
- `monitor-pr.fsx` - PR status monitoring (saves ~500 tokens)
- `validate-release.fsx` - Post-release verification (saves ~600 tokens)
- `resume-release.fsx` - Failure recovery (saves ~400 tokens)

**Coordination Dependencies:**
- **→ QA Tester:** Post-release smoke tests (Release → QA)
- **→ AOT Guru:** Version compatibility verification (Release → AOT)
- **→ All gurus:** Retrospective system as common feedback hub

**Maturity Evidence:**
- 20+ releases successfully managed
- 4 comprehensive playbooks (standard, hotfix, pre-release, recovery)
- Automated retrospective system working
- Continuous playbook evolution documented
- Review capability proven through multiple releases

---

### 4. Technical Writer

**Maturity Phase:** Alpha (Phase 1)
**Location:** `.claude/skills/technical-writer/`
**Domain:** Documentation, Hugo/Docsy, diagrams, and visual communication

**Capabilities:**
- ✅ Hugo static site generator mastery
- ✅ Docsy theme expertise
- ✅ Mermaid diagram creation
- ✅ PlantUML expertise
- ✅ Markdown mastery
- ✅ API documentation
- ✅ Style guide enforcement

**Review Capability:**
- **Status:** Defined, implementation pending
- **Scope:** Link validation, Hugo build health, diagram syntax, style compliance, content freshness
- **Frequency:** Pre-release + Quarterly audit + PR reviews
- **Triggers:** Pre-release validation, quarterly audit, docs PR reviews
- **Output:** Link report, build diagnostics, diagram validation, style score, content gaps
- **Integration:** Pre-release checklist, documentation quality gates

**Automation Scripts (Defined):**
- `link-validator.fsx` - Link validation (saves ~800 tokens)
- `hugo-doctor.fsx` - Hugo diagnostics (saves ~600 tokens)
- `diagram-validator.fsx` - Mermaid/PlantUML validation (saves ~400 tokens)
- `content-auditor.fsx` - Content coverage analysis (saves ~700 tokens)
- `style-checker.fsx` - Style guide enforcement (saves ~500 tokens)
- `release-notes-generator.fsx` - Release documentation (saves ~600 tokens)
- `screenshot-taker.fsx` - Visual documentation (saves ~900 tokens)

**Coordination Dependencies:**
- **→ Release Manager:** Create release notes and What's New documents (Technical Writer → Release)
- **→ QA Tester:** Document test procedures and results (Technical Writer → QA)
- **→ AOT Guru:** Document AOT patterns and troubleshooting (Technical Writer → AOT)
- **← All gurus:** Documentation support for domain-specific docs

**Alpha Status:**
- [x] Directory structure created
- [x] SKILL.md complete (~800 lines)
- [x] README.md created
- [x] MAINTENANCE.md documented
- [ ] 7 automation scripts defined (implementation pending)
- [x] 11 seed patterns documented
- [ ] Initial templates created
- [ ] Team feedback collected

---

### 5. Elm-to-F# Guru

**Maturity Phase:** Candidate (Phase 0)  
**Location:** Planned at `.claude/skills/elm-to-fsharp/`  
**Domain:** Elm-to-F# migration, pattern translation, code generation

**Capabilities (Planned):**
- 🔄 Elm syntax to F# translation
- 🔄 Type system mapping (Elm → F#)
- 🔄 Pattern catalog for common migrations
- 🔄 Myriad plugin opportunities identification
- 🔄 F# idiom enforcement
- 🔄 Migration quality assurance

**Review Capability (Planned):**
- **Scope:** Migration patterns, Myriad plugin opportunities, F# idiom adherence, type safety preservation
- **Frequency:** Per-migration session + Quarterly pattern review
- **Triggers:** After each module migration, quarterly pattern inventory
- **Output:** Pattern catalog updates, Myriad plugin recommendations, F# idiom violations
- **Integration:** Patterns appearing 3+ times trigger automation decisions

**Automation Scripts (Planned):**
- `elm-to-fsharp.fsx` - Automated translation for simple patterns
- `migration-validator.fsx` - Type safety and idiom checks
- `pattern-detector.fsx` - Repetitive pattern identification

**Coordination Dependencies:**
- **→ AOT Guru:** Generated code AOT safety review (Elm-to-F# → AOT)
- **→ QA Tester:** Test coverage verification (Elm-to-F# → QA)
- **← Release Manager:** Version milestones tracking (Release → Elm-to-F#)

**Candidate Status:**
- Domain defined in issue #240
- Competencies identified
- Coordination points mapped
- Awaiting implementation

---

## Coordination Matrix

This matrix shows how gurus interact:

| From ↓ / To → | QA Tester | AOT Guru | Release Manager | Technical Writer | Elm-to-F# |
|---------------|-----------|----------|-----------------|------------------|-----------|
| **QA Tester** | - | Test AOT builds | Post-release validation | Document test results | Verify test coverage |
| **AOT Guru** | - | - | Version compatibility | Document AOT patterns | Review generated code |
| **Release Manager** | Request verification | Request AOT check | - | Release notes/What's New | Track milestones |
| **Technical Writer** | Document procedures | Document troubleshooting | Create release docs | - | - |
| **Elm-to-F#** | Request testing | Request review | Report progress | - | - |

## Review Capability Status

| Guru | Review Status | Review Frequency | Review Scope | Integration |
|------|---------------|------------------|--------------|-------------|
| **QA Tester** | ✅ Implemented | Continuous + Manual | Coverage, ignored tests, BDD | CI/CD integrated |
| **AOT Guru** | ✅ Implemented | Quarterly + On-demand | Reflection, size, patterns | Quarterly reports |
| **Release Manager** | ✅ Implemented | Per-release + Quarterly | Process, changelog, version | Retrospectives |
| **Technical Writer** | 🔄 Defined | Pre-release + Quarterly | Links, Hugo, diagrams, style | Quality gates |
| **Elm-to-F#** | 🔄 Planned | Per-session + Quarterly | Patterns, idioms, safety | Pattern automation |

**Legend:**
- ✅ Implemented and working
- 🔄 Planned but not yet implemented
- ❌ Not implemented

## Success Metrics

### Token Efficiency
Total estimated token savings across all gurus:
- **QA Tester:** ~1800 tokens per release cycle
- **AOT Guru:** ~2600 tokens per quarter
- **Release Manager:** ~3200 tokens per release
- **Technical Writer:** ~5200 tokens per audit cycle
- **Total (4 gurus):** ~12800 tokens per release cycle

### Maturity Distribution
- **Phase 3 (Stable):** 3 gurus (QA, AOT, Release)
- **Phase 2 (Beta):** 0 gurus
- **Phase 1 (Alpha):** 1 guru (Technical Writer)
- **Phase 0 (Candidate):** 1 guru (Elm-to-F#)

### Coordination Health
- All stable gurus have documented coordination points
- Cross-guru workflows tested and proven
- Escalation paths defined
- Review findings shared across gurus

## Quarterly Review Schedule

| Quarter | QA Tester | AOT Guru | Release Manager | Technical Writer | Elm-to-F# |
|---------|-----------|----------|-----------------|------------------|-----------|
| **Q1 2025** | ✅ Complete | ✅ Complete | ✅ Complete | - | - |
| **Q2 2025** | ✅ Complete | ✅ Complete | ✅ Complete | - | - |
| **Q3 2025** | ✅ Complete | ✅ Complete | ✅ Complete | - | - |
| **Q4 2025** | ✅ Complete | ✅ Complete | ✅ Complete | 🆕 Alpha | 🔄 Candidate |

## Adding New Gurus

When proposing a new guru:

1. **Validate candidacy** using [guru-creation-guide.md Part 1](./guru-creation-guide.md#part-1-should-this-be-a-guru)
2. **Add to this matrix** with Phase 0 (Candidate) status
3. **Define competencies** (3-6 primary, 2-4 secondary)
4. **Map coordination** points with existing gurus
5. **Design review capability** from the start (see [guru-creation-guide.md Part 5B](./guru-creation-guide.md#part-5b-review-capability))
6. **Get maintainer approval** before implementation
7. **Follow implementation guide** in [guru-creation-guide.md](./guru-creation-guide.md)

## Future Gurus (Proposed)

Ideas for future gurus (not yet candidates):

- **Security Guru** - Vulnerability scanning, dependency audits, security best practices
- **Performance Guru** - Profiling, benchmarking, optimization recommendations
- **Deployment Guru** - Container packaging, cloud deployment, infrastructure as code

## References

- **[Guru Philosophy](./guru-philosophy.md)** - Core philosophy behind gurus
- **[Guru Creation Guide](./guru-creation-guide.md)** - Step-by-step creation guide
- **[Skills Reference](./skills-reference.md)** - Detailed guru documentation
- **[Capabilities Matrix](./capabilities-matrix.md)** - Cross-agent compatibility

---

**Last Updated:** December 19, 2025  
**Maintained By:** Project maintainers and AI agents  
**Version:** 1.0 (Initial)  
**Next Review:** Q1 2026
