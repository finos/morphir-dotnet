# F# Frontend Design Documentation

This directory contains comprehensive design documentation for the **F# Frontend** (F# → Morphir IR parser), which completes the round-trip capability for morphir-dotnet.

## Overview

The F# Frontend parses F# source code and generates Morphir IR, enabling:
- F# developers to model business logic and generate Morphir IR
- Round-trip capability: **F# ↔ Morphir IR ↔ F#**
- Foundation for F#-based Morphir tooling

**Architecture**: F# Source → FSharp.Compiler.Service → F# AST (Typed) → IR Mapper → Morphir IR → JSON

---

## Document Index

### 1. [Product Requirements Document (PRD)](./PRD-fsharp-frontend.md)
**Purpose**: Complete product requirements and technical design

**Contents**:
- Executive summary and strategic positioning
- User personas and goals
- Architecture overview (FCS → AST → IR → JSON)
- F# language support strategy (MVP vs Extended vs Never)
- Technical design for all components
- Testing strategy (Unit, Property, Snapshot, Integration, E2E)
- 12-week implementation plan (5 phases)
- Success metrics, risks, and mitigations

**Key Sections**:
- §3: Architecture Overview - High-level data flow
- §4: F# Language Support - Feature categorization
- §5: Technical Design - Component specifications
- §7: Implementation Plan - 12-week timeline

**Read this first** for a complete understanding of the F# Frontend.

---

### 2. [Maturity Milestones](./fsharp-frontend-maturity-milestones.md)
**Purpose**: Incremental delivery roadmap with end-to-end validation at each milestone

**Contents**:
- M0: Foundation + CLI (2 weeks) - Minimal E2E pipeline
- M1: Type Parsing (3 weeks) - F# types → JSON
- M2: Value Parsing (3 weeks) - F# functions → Round-trip
- M3: Multi-File Projects (2 weeks) - .fsproj → Unified JSON
- M4: Production Ready (2 weeks) - Ionide analyzer + Advanced features

**Key Innovation**: **CLI integration from the start** - every milestone delivers a testable thin vertical slice.

**Testing Strategy**:
- M0: E2E (Parse empty module → JSON)
- M1: E2E (Parse types → Validate schema)
- M2: E2E (Parse functions → Round-trip test)
- M3: E2E (Parse .fsproj → Unified JSON)
- M4: E2E (Full production workflow + Ionide)

---

### 3. [GitHub Issues Breakdown](./github-issues-fsharp-frontend.md)
**Purpose**: Detailed task breakdown for implementation

**Contents**:
- Epic issue template
- 40 detailed issues across 5 milestones
- Acceptance criteria for each issue
- Estimated effort (85-94 developer-days total)
- Code examples and testing requirements

**Issue Organization**:
- **M0** (5 issues, 8-10 days): Project setup, Parser, IR Generator, CLI
- **M1** (8 issues, 16-17 days): TypeMapper (primitives, collections, records, DUs, aliases)
- **M2** (10 issues, 21-23 days): ValueMapper, PatternMapper, ModuleMapper, Round-trip
- **M3** (8 issues, 17-19 days): ProjectParser, DependencyResolver, Generics, HOFs
- **M4** (9 issues, 23-25 days): Ionide Analyzer, Advanced features, Documentation, Release

**Labels**: `m0-foundation`, `m1-types`, `m2-values`, `m3-multi-file`, `m4-production`, `e2e`, `cli`, `analyzer`

---

### 4. [Usage Scenarios](./fsharp-frontend-usage-scenarios.md)
**Purpose**: Demonstrate all supported usage patterns (in-memory, CLI, project files)

**Contents**:
- Scenario 1: In-Memory Parsing (try-morphir, REPL, testing)
- Scenario 2: Multiple Files Without .fsproj (ad-hoc parsing)
- Scenario 3: Project Files (.fsproj) (production builds)
- API examples for each scenario
- Comparison matrix
- Performance considerations

**Key Use Cases**:
- ✅ **try-morphir Integration**: `FrontendAPI.parseInMemory` for web playground
- ✅ **CLI - Multiple Files**: `morphir fsharp parse file1.fs file2.fs ...`
- ✅ **CLI - Project File**: `morphir fsharp parse MyProject.fsproj`
- ✅ **Programmatic API**: F# scripts, build tools, CI/CD

---

### 5. [Unified File Architecture](./unified-file-architecture.md) 🆕 **RECOMMENDED**
**Purpose**: Unified file abstraction across all morphir-dotnet components (Pipeline, F# Frontend, F# Backend, future components)

**Key Discovery**: The `Morphir.IR.Pipeline` project **already has** `VFile` - a VFile-like abstraction! We should use it across all components.

**Contents**:
- **VFile** (✅ exists): Single file with diagnostics, metadata, history
- **VFileTree** (🆕 proposed): Hierarchical multi-file structure
- Integration with Pipeline, F# Frontend, F# Backend
- End-to-end examples (F# → IR → JSON)
- Migration path (Phase 1-4)

**Key Concepts**:
- **VFile**: VFile equivalent - already in `Morphir.IR.Pipeline/File.fs`
- **VMessage**: Rich diagnostics with source locations, severity, rule IDs
- **VFileTree**: Hierarchical tree for multi-file projects (new)
- **TreeProcessor**: Pipeline support for processing trees (new)

**Benefits**:
- ✅ **Consistency**: Same file abstraction across all components
- ✅ **Composability**: Files flow through pipelines seamlessly
- ✅ **Rich Diagnostics**: Already implemented in Pipeline
- ✅ **No Duplication**: Reuse existing `VFile` instead of creating new VFile

**Status**: Recommended approach - supersedes F# Frontend-only DocumentTree

---

### 6. [DocumentTree Architecture](./fsharp-frontend-document-tree.md) (Superseded)
**Purpose**: Original F# Frontend-specific proposal (now superseded by unified approach)

**Note**: This document proposed VFile and DocumentTree for F# Frontend only. The [Unified File Architecture](./unified-file-architecture.md) is now the recommended approach as it integrates with the existing `VFile` in the Pipeline project.

**Status**: Reference only - Use Unified File Architecture instead

---

### 6. [F# Language Coverage Strategy](./fsharp-language-coverage.md)
**Purpose**: Define which F# features are supported and why

**Contents**:
- Support tiers: P0 (MVP), P1 (Extended), P2 (Future), Never (Incompatible)
- Complete feature matrix for types, expressions, patterns, modules
- Migration patterns (mutation → immutable, OOP → ADTs, exceptions → Result)
- Ionide Analyzer guidance (errors, warnings, info)
- Testing strategy for language coverage

**Key Decisions**:
- ✅ **P0 (MVP)**: Pure functional subset - records, DUs, functions, pattern matching
- ✅ **P1 (Extended)**: Generics, higher-order functions, advanced patterns
- ⚠️ **P2 (Future)**: Active patterns, anonymous records, units of measure
- ❌ **Never**: Mutation, OOP, computation expressions, side effects

**Language Coverage**:
- M1: ~30% (types only)
- M2: ~60% (MVP complete)
- M3: ~75% (extended MVP)
- M4: ~85% (production ready)

---

## Quick Start

### For Product Managers
1. Read [PRD §1-2](./PRD-fsharp-frontend.md#1-executive-summary) - Strategic positioning
2. Review [Maturity Milestones](./fsharp-frontend-maturity-milestones.md) - Delivery roadmap
3. Check [Language Coverage §1](./fsharp-language-coverage.md#support-tiers) - Feature scope

### For Architects
1. Read [PRD §3](./PRD-fsharp-frontend.md#3-architecture-overview) - Architecture overview
2. Review [PRD §5](./PRD-fsharp-frontend.md#5-technical-design) - Component design
3. Check [Language Coverage](./fsharp-language-coverage.md) - Feature decisions

### For Developers
1. Review [Maturity Milestones](./fsharp-frontend-maturity-milestones.md) - Implementation phases
2. Read [GitHub Issues](./github-issues-fsharp-frontend.md) - Task breakdown
3. Check [Language Coverage §6](./fsharp-language-coverage.md#migration-patterns) - Migration patterns

### For QA/Testers
1. Review [PRD §6](./PRD-fsharp-frontend.md#6-testing-strategy) - Testing strategy
2. Check [Maturity Milestones](./fsharp-frontend-maturity-milestones.md#testing-strategy-per-milestone) - Testing per milestone
3. Read [Language Coverage §7](./fsharp-language-coverage.md#testing-strategy-for-language-coverage) - Feature testing

---

## Key Design Decisions

### 1. FSharp.Compiler.Service (FCS)
**Decision**: Use FCS for parsing and type checking, despite AOT incompatibility.

**Rationale**:
- Only mature, accurate option for F# parsing
- Official Microsoft compiler API
- Provides typed AST (critical for accurate IR generation)

**Trade-off**: Frontend CLI is **runtime-only** (not AOT-compatible). Backend remains AOT-compatible.

**Mitigation**: Distribute frontend as `dotnet tool`, backend as single-file AOT executable.

---

### 2. Incremental Language Support
**Decision**: Start with pure functional subset (MVP), expand based on demand.

**Rationale**:
- MVP covers 80%+ of Morphir use cases (business logic modeling)
- Allows early validation with real users
- Avoids over-engineering features that may not be needed

**Strategy**: P0 (MVP) → P1 (Extended) → P2 (Future, demand-driven)

**Never Support**: Mutation, OOP, computation expressions (fundamentally incompatible with Morphir)

---

### 3. CLI Integration from Start
**Decision**: Build CLI in M0, integrate into every milestone.

**Rationale**:
- Enables end-to-end testing of each thin slice
- Provides immediate user feedback
- Validates architecture early

**Approach**: Each milestone delivers a complete vertical slice testable via CLI.

---

### 4. Round-Trip Testing
**Decision**: Use F# Backend to validate frontend correctness (F# → IR → F# → IR).

**Rationale**:
- Guarantees semantic equivalence (not just syntactic correctness)
- Catches subtle bugs in IR generation
- Provides confidence for production use

**Target**: 95%+ round-trip success rate by M2.

---

### 5. Ionide Analyzer Integration
**Decision**: Build Ionide analyzer for real-time diagnostics (VS Code).

**Rationale**:
- Provides immediate feedback to developers
- Prevents unsupported features from being used
- Suggests Morphir-friendly alternatives

**Timing**: M4 (Production Ready) - after core functionality is stable.

---

## Success Metrics

| Metric | Target | Milestone |
|--------|--------|-----------|
| Parse success rate | 100% (valid F# files) | M0 |
| Type mapping accuracy | 100% (MVP types) | M1 |
| Value mapping accuracy | 100% (MVP expressions) | M2 |
| Round-trip success | 95%+ | M2 |
| Multi-file parsing | 100% (.fsproj projects) | M3 |
| Code coverage | 80%+ | M4 |
| Performance | 1000 LOC < 1s | M4 |
| Language coverage | 85% (pure functional subset) | M4 |

---

## Dependencies

### External Dependencies
- **FSharp.Compiler.Service** 43.9+ - F# parsing and type checking
- **Ionide.Analyzers.SDK** 0.9+ - Real-time diagnostics (M4)
- **Morphir.Models** - F# Classic IR types
- **Morphir.Backends.FSharp** M2+ - For round-trip testing

### Internal Dependencies
- **Morphir.SDK** M0.3+ - Type mappings (Basics, String, Maybe, Result, List)
- **.NET 10** - Platform target
- **F# 9/10** - Language version

---

## Timeline

**Total**: 12 weeks (3 months)

| Phase | Duration | Weeks | Key Deliverable |
|-------|----------|-------|----------------|
| M0: Foundation + CLI | 2 weeks | 1-2 | Parse empty modules → JSON |
| M1: Type Parsing | 3 weeks | 3-5 | Parse types → Validate schema |
| M2: Value Parsing | 3 weeks | 6-8 | Parse functions → Round-trip |
| M3: Multi-File | 2 weeks | 9-10 | Parse .fsproj → Unified JSON |
| M4: Production | 2 weeks | 11-12 | Ionide + Documentation + Release |

**Note**: Timeline assumes 1-2 developers working full-time. With 3 developers, can be compressed to 8-10 weeks.

---

## Deliverables

### NuGet Packages
- **Morphir.Frontends.FSharp** - Core library (parser + mapper)
- **Morphir.Frontends.FSharp.Cli** - CLI tool (`dotnet tool`)
- **Morphir.Frontends.FSharp.Analyzer** - Ionide analyzer

### Documentation
- User guide (usage, examples, troubleshooting)
- API documentation (XML docs)
- Language support matrix (supported vs unsupported)
- CLI reference
- Migration guide (Elm → F# patterns)
- Ionide analyzer setup guide

### Testing
- 100+ unit tests (80%+ coverage)
- 50+ property-based tests (FsCheck)
- 100+ snapshot tests (approved IR outputs)
- 20+ integration tests (complete modules)
- 10+ E2E CLI tests per milestone
- 50+ round-trip tests

---

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| FCS API changes | High | Low | Pin to specific FCS version, test across versions |
| Performance issues (large projects) | Medium | Medium | Profile early (M3), optimize hot paths, parallel parsing |
| Incomplete type inference | High | Medium | Leverage FCS type checking, validate with tests |
| Round-trip failures | High | Low | Extensive testing, snapshot tests, early detection |
| AOT incompatibility (FCS) | Medium | High | Accept runtime-only for frontend, document clearly |
| Language feature creep | Medium | High | Strict prioritization (P0 → P1 → P2), user validation |

---

## Related Documentation

### morphir-dotnet Project
- [F# Backend PRD](./PRD-fsharp-backend.md) - Generates F# FROM IR
- [F# Backend Maturity Milestones](./fsharp-backend-maturity-milestones.md)
- [Morphir.SDK Library Plan](./morphir-sdk-library-plan.md) - SDK type mappings
- [Morphir Elm Migration Assessment](./morphir-elm-migration-assessment.md) - Elm patterns

### External Resources
- [Morphir Homepage](https://morphir.finos.org/)
- [morphir-elm](https://github.com/finos/morphir-elm) - Reference implementation
- [FSharp.Compiler.Service Docs](https://fsharp.github.io/fsharp-compiler-docs/)
- [Ionide Analyzers](https://ionide.io/Analyzers/)

---

## Next Steps

1. **Review** this design documentation with the team
2. **Create Epic** issue using [GitHub Issues template](./github-issues-fsharp-frontend.md#epic-issue)
3. **Create M0 issues** (5 issues) in GitHub
4. **Assign** issues to developers
5. **Start M0** implementation (Foundation + CLI)
6. **Iterate** through milestones (M0 → M1 → M2 → M3 → M4)

---

## Questions?

For questions about this design:
- Open a discussion in GitHub Discussions
- Tag: `@morphir-architect` or `@elm-to-fsharp-guru` skills
- Reference: [PRD: F# Frontend](./PRD-fsharp-frontend.md)

---

**Document Status**: Draft
**Last Updated**: 2025-12-31
**Next Review**: Before M0 kickoff
