---
title: "GitHub Issues: Morphir Application Architect Skill"
linkTitle: "Architect GitHub Issues"
weight: 101
description: >
  Pre-formatted GitHub issues for implementing the Morphir Application Architect skill, ready to be created in the repository.
---

# GitHub Issues for Morphir Application Architect Skill

**Implementation Plan**: [Morphir Architect Implementation Plan](./morphir-architect-skill-plan.md)
**PRD**: [Morphir Application Architect Skill PRD](../prds/morphir-application-architect-skill.md)

## Overview

This document contains pre-formatted GitHub issues for all tasks in the Morphir Application Architect implementation plan. Create these issues in the morphir-dotnet repository to track implementation progress.

## Epic Issue

### Epic: Morphir Application Architect Skill

```markdown
## Epic: Morphir Application Architect Skill

**Type**: Epic
**Labels**: `epic`, `skill:architect`, `enhancement`

### Overview

Create a comprehensive Morphir Application Architect expert skill that combines deep knowledge of:
- Morphir specifications and ecosystem
- Programming language design (ASTs, CSTs, IRs, type systems)
- Functional programming patterns and design principles
- Cross-language integration patterns (bridging FP and OO worlds)
- WebAssembly Component Model and polyglot architecture
- Semantic web technologies (RDF, JSON-LD, linked data)
- Pluggable pipeline architectures (inspired by unified.js/unist)

### Documentation

- **PRD**: [Morphir Application Architect Skill PRD](../docs/content/contributing/design/prds/morphir-application-architect-skill.md)
- **Implementation Plan**: [Morphir Architect Implementation Plan](../docs/content/contributing/design/implementation-plans/morphir-architect-skill-plan.md)

### Phases

- [ ] Phase 1: Core Architectural Guidance (Weeks 1-4)
- [ ] Phase 2: Pluggable Pipeline Architecture (Weeks 5-8)
- [ ] Phase 3: WebAssembly Integration Strategy (Weeks 9-12)
- [ ] Phase 4: Semantic Web Integration (Weeks 13-16)
- [ ] Phase 5: Integration and Documentation (Weeks 17-20)

### Success Metrics

- 95%+ accuracy answering Morphir ecosystem questions
- 20+ architectural patterns documented
- Pluggable pipeline architecture implemented
- WebAssembly Component Model integration designed
- RDF/JSON-LD semantic attribution working
- 3+ architecture guides published
- 3+ tutorial series created

### Related Issues

Phase 1: #TBD, #TBD, #TBD, #TBD
Phase 2: #TBD, #TBD, #TBD, #TBD, #TBD
Phase 3: #TBD, #TBD, #TBD, #TBD, #TBD
Phase 4: #TBD, #TBD, #TBD, #TBD
Phase 5: #TBD, #TBD, #TBD, #TBD, #TBD, #TBD
```

---

## Phase 1: Core Architectural Guidance

### Issue 1.1: Morphir Ecosystem Documentation Review

```markdown
## Task 1.1: Morphir Ecosystem Documentation Review

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 1
**Labels**: `skill:architect`, `phase-1`, `priority-p0`, `documentation`

### Description

Comprehensive review and documentation of the entire Morphir ecosystem (morphir-elm, morphir-core, morphir-dotnet) to establish foundational knowledge for the Morphir Application Architect skill.

### Prerequisites

None

### Activities

1. Read all documentation from morphir-elm repository
   - IR format specifications (v1, v2, v3)
   - Type system documentation
   - SDK documentation
2. Read morphir-core documentation
   - Core concepts
   - Tooling architecture
   - CLI documentation
3. Document cross-repository patterns
4. Create ecosystem knowledge base

### Deliverables

- [ ] Ecosystem knowledge summary (markdown)
- [ ] Cross-repository pattern catalog
- [ ] Version compatibility matrix
- [ ] Architectural decision log

### Acceptance Criteria

- [ ] Can answer 95%+ of questions about Morphir ecosystem
- [ ] Can identify architectural patterns across all repositories
- [ ] Knowledge base contains 50+ entries

### Effort Estimate

2 days

### Related Issues

Part of Phase 1: Core Architectural Guidance
```

---

### Issue 1.2: Language Design Pattern Research

```markdown
## Task 1.2: Language Design Pattern Research

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 1
**Labels**: `skill:architect`, `phase-1`, `priority-p0`, `research`, `patterns`

### Description

Research and document language design patterns including AST/CST manipulation, visitor patterns, and type system design to provide expert guidance for morphir-dotnet enhancements.

### Prerequisites

None

### Activities

1. Study AST/CST design patterns
   - Research compiler design literature
   - Analyze existing Morphir IR structure
   - Document transformation patterns
2. Research visitor pattern implementations
   - Gang of Four visitor pattern
   - Modern functional visitor alternatives
   - F#/C# idiomatic implementations
3. Document type system design patterns
4. Create pattern examples

### Deliverables

- [ ] AST/CST pattern guide
- [ ] Visitor pattern implementations (3+ variants)
- [ ] Type system design patterns document
- [ ] Code examples for each pattern

### Acceptance Criteria

- [ ] 10+ language design patterns documented
- [ ] 3+ working visitor pattern implementations
- [ ] Examples tested and verified

### Effort Estimate

3 days

### Related Issues

Part of Phase 1: Core Architectural Guidance
```

---

### Issue 1.3: Functional Programming Pattern Library

```markdown
## Task 1.3: Functional Programming Pattern Library

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 1
**Labels**: `skill:architect`, `phase-1`, `priority-p0`, `patterns`, `functional-programming`

### Description

Create comprehensive FP pattern library with F# and C# implementations, including patterns for bridging functional and object-oriented paradigms.

### Prerequisites

None

### Activities

1. Document core FP patterns
   - Monads, functors, applicatives
   - Lenses and optics
   - Free monads for DSLs
   - Railway-oriented programming
2. Create F# implementations
3. Create C# implementations (where applicable)
4. Document bridging patterns (FP ↔ OO)
   - Visitor pattern as bridge
   - Adapter pattern for FP in OO
   - Strategy pattern with FP

### Deliverables

- [ ] FP pattern catalog (15+ patterns)
- [ ] F# implementation examples
- [ ] C# implementation examples (where applicable)
- [ ] Bridging pattern guide

### Acceptance Criteria

- [ ] 15+ FP patterns documented with examples
- [ ] Each pattern has F# and (where applicable) C# example
- [ ] Bridging patterns tested with real code

### Effort Estimate

3 days

### Related Issues

Part of Phase 1: Core Architectural Guidance
```

---

### Issue 1.4: Create Initial Skill File

```markdown
## Task 1.4: Create Initial Skill File

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 1
**Labels**: `skill:architect`, `phase-1`, `priority-p0`, `documentation`

### Description

Create initial skill file structure for Morphir Application Architect using the skill template, populated with knowledge from previous tasks.

### Prerequisites

- [ ] Task 1.1: Morphir Ecosystem Documentation Review
- [ ] Task 1.2: Language Design Pattern Research
- [ ] Task 1.3: Functional Programming Pattern Library

### Activities

1. Use skill template
2. Populate core competencies section
3. Add initial decision trees
4. Create README and MAINTENANCE files
5. Set up directory structure

### Deliverables

- [ ] `.claude/skills/morphir-architect/skill.md`
- [ ] `.claude/skills/morphir-architect/README.md`
- [ ] `.claude/skills/morphir-architect/MAINTENANCE.md`
- [ ] `.claude/skills/morphir-architect/metadata.yaml`
- [ ] Directory structure created

### Acceptance Criteria

- [ ] Skill file follows template structure
- [ ] All required sections populated
- [ ] Passes skill validation checks
- [ ] README provides clear quick-start

### Effort Estimate

2 days

### Related Issues

Part of Phase 1: Core Architectural Guidance
Depends on: #TBD (1.1), #TBD (1.2), #TBD (1.3)
```

---

## Phase 2: Pluggable Pipeline Architecture

### Issue 2.1: Unified.js Architecture Study

```markdown
## Task 2.1: Unified.js Architecture Study

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 2
**Labels**: `skill:architect`, `phase-2`, `priority-p0`, `research`, `architecture`

### Description

Deep dive into unified.js architecture to understand pluggable transformation pipelines and adapt the pattern to .NET/F# for Morphir IR transformations.

### Prerequisites

- [ ] Phase 1 complete

### Activities

1. Deep dive into unified.js source code
   - Processor architecture
   - Plugin interface design
   - Middleware chain implementation
2. Study unist specification
   - Universal syntax tree structure
   - Position tracking
   - Metadata attachment
3. Study vfile architecture
   - Virtual file abstraction
   - Metadata management
   - Message reporting
4. Document key architectural insights

### Deliverables

- [ ] Unified.js architecture analysis document
- [ ] Unist specification summary
- [ ] VFile pattern analysis
- [ ] Adaptation strategy for .NET/F#

### Acceptance Criteria

- [ ] Complete understanding of unified.js architecture
- [ ] Documented differences between JS and .NET approaches
- [ ] Clear adaptation strategy for F#/C#

### Effort Estimate

2 days

### Resources

- https://github.com/unifiedjs/unified
- https://github.com/syntax-tree/unist
- https://github.com/vfile/vfile

### Related Issues

Part of Phase 2: Pluggable Pipeline Architecture
```

---

### Issue 2.2: Pipeline Architecture Design (ADR)

```markdown
## Task 2.2: Pipeline Architecture Design (ADR)

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 2
**Labels**: `skill:architect`, `phase-2`, `priority-p0`, `adr`, `architecture`

### Description

Design pluggable pipeline architecture for Morphir IR transformations, documenting decisions in an ADR and creating comprehensive API specifications.

### Prerequisites

- [ ] Task 2.1: Unified.js Architecture Study

### Activities

1. Design processor architecture
   - F# computation expression for pipeline
   - Plugin registration mechanism
   - Middleware chain execution
2. Design plugin interface
   - Transformation plugin API
   - Parser plugin API
   - Generator plugin API
3. Design metadata system
   - IR node annotation
   - Provenance tracking
   - Error reporting
4. Write ADR documenting decisions

### Deliverables

- [ ] ADR for pluggable pipeline architecture
- [ ] API design document
- [ ] Interface definitions (F# and C#)
- [ ] Architecture diagrams (Mermaid/PlantUML)

### Acceptance Criteria

- [ ] ADR approved by maintainers
- [ ] API design reviewed and validated
- [ ] Diagrams clearly illustrate architecture
- [ ] Interface definitions are type-safe and extensible

### Effort Estimate

3 days

### Related Issues

Part of Phase 2: Pluggable Pipeline Architecture
Depends on: #TBD (2.1)
```

---

### Issue 2.3: Implement Core Pipeline

```markdown
## Task 2.3: Implement Core Pipeline

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 2
**Labels**: `skill:architect`, `phase-2`, `priority-p0`, `implementation`

### Description

Implement the core pluggable pipeline architecture including processor, plugin interfaces, and metadata system.

### Prerequisites

- [ ] Task 2.2: Pipeline Architecture Design (ADR)

### Activities

1. Implement processor core
   - Pipeline builder
   - Plugin registration
   - Execution engine
2. Implement base plugin interfaces
   - ITransformationPlugin
   - IParserPlugin
   - IGeneratorPlugin
3. Implement metadata system
   - NodeMetadata type
   - Attachment/retrieval API
4. Create unit tests

### Deliverables

- [ ] `src/Morphir.Pipeline.Core/Processor.fs`
- [ ] `src/Morphir.Pipeline.Core/Plugin.fs`
- [ ] `src/Morphir.Pipeline.Core/Metadata.fs`
- [ ] Unit tests (>90% coverage)

### Acceptance Criteria

- [ ] All interfaces implemented
- [ ] Unit tests passing
- [ ] AOT-compatible (no reflection)
- [ ] Documented with XML comments

### Effort Estimate

5 days

### Related Issues

Part of Phase 2: Pluggable Pipeline Architecture
Depends on: #TBD (2.2)
```

---

### Issue 2.4: Create Example Plugins

```markdown
## Task 2.4: Create Example Plugins

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 2
**Labels**: `skill:architect`, `phase-2`, `priority-p1`, `examples`

### Description

Create example transformation plugins to demonstrate pipeline architecture and provide templates for plugin developers.

### Prerequisites

- [ ] Task 2.3: Implement Core Pipeline

### Activities

1. Type validator plugin
   - Validates Morphir IR type correctness
   - Reports type errors with context
2. Optimization plugin
   - Performs simple IR optimizations
   - Constant folding, dead code elimination
3. Pretty printer plugin
   - Generates human-readable IR representation

### Deliverables

- [ ] Type validator plugin with tests
- [ ] Optimization plugin with tests
- [ ] Pretty printer plugin with tests
- [ ] Plugin development guide

### Acceptance Criteria

- [ ] All plugins working end-to-end
- [ ] Tests demonstrate plugin composition
- [ ] Development guide helps users create plugins
- [ ] Examples referenced in skill documentation

### Effort Estimate

4 days

### Related Issues

Part of Phase 2: Pluggable Pipeline Architecture
Depends on: #TBD (2.3)
```

---

### Issue 2.5: Pipeline Automation Scripts

```markdown
## Task 2.5: Pipeline Automation Scripts

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 2
**Labels**: `skill:architect`, `phase-2`, `priority-p1`, `automation`

### Description

Create automation scripts for pipeline validation and performance analysis to save agent tokens and accelerate development.

### Prerequisites

- [ ] Task 2.4: Create Example Plugins

### Activities

1. Create `validate-pipeline.fsx`
   - Validate pipeline configuration
   - Check plugin dependencies
   - Verify plugin compatibility
2. Create `analyze-pipeline-performance.fsx`
   - Measure transformation performance
   - Identify bottlenecks

### Deliverables

- [ ] `scripts/validate-pipeline.fsx`
- [ ] `scripts/analyze-pipeline-performance.fsx`
- [ ] Script documentation

### Acceptance Criteria

- [ ] Scripts save 500+ tokens vs manual validation
- [ ] Clear error messages for common issues
- [ ] Performance analysis actionable

### Effort Estimate

2 days

### Related Issues

Part of Phase 2: Pluggable Pipeline Architecture
Depends on: #TBD (2.4)
```

---

## Phase 3: WebAssembly Integration Strategy

### Issue 3.1: Component Model Deep Dive

```markdown
## Task 3.1: Component Model Deep Dive

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 3
**Labels**: `skill:architect`, `phase-3`, `priority-p0`, `research`, `webassembly`

### Description

Comprehensive study of WebAssembly Component Model, WIT, WAC, and Canonical ABI to design Morphir's WebAssembly integration strategy.

### Prerequisites

- [ ] Phase 2 complete

### Activities

1. Study WebAssembly Component Model specification
   - Component model concepts
   - Interface types
   - Canonical ABI
2. Study WIT (WebAssembly Interface Types)
   - Syntax and semantics
   - Type mappings
   - Import/export declarations
3. Study WAC (WebAssembly Compositions)
   - Component linking
   - Composition patterns
4. Document findings

### Deliverables

- [ ] Component Model study notes
- [ ] WIT language reference summary
- [ ] Canonical ABI mapping guide
- [ ] Component composition patterns

### Acceptance Criteria

- [ ] Deep understanding of Component Model
- [ ] Can design WIT interfaces for Morphir types
- [ ] Can map Morphir IR to Component Model types

### Effort Estimate

3 days

### Resources

- https://github.com/WebAssembly/component-model
- https://github.com/WebAssembly/component-model/blob/main/design/mvp/WIT.md
- https://github.com/WebAssembly/component-model/blob/main/design/mvp/CanonicalABI.md

### Related Issues

Part of Phase 3: WebAssembly Integration Strategy
```

---

### Issue 3.2: Design WIT Interfaces for Morphir IR

```markdown
## Task 3.2: Design WIT Interfaces for Morphir IR

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 3
**Labels**: `skill:architect`, `phase-3`, `priority-p0`, `webassembly`, `interface-design`

### Description

Design WebAssembly Interface Types (WIT) for Morphir IR, enabling cross-language interoperability via the Component Model.

### Prerequisites

- [ ] Task 3.1: Component Model Deep Dive

### Activities

1. Design core IR types in WIT
   - Package, Module, Type, Value
   - Expression and pattern types
2. Design transformation interfaces
   - Validation, optimization, generation
3. Create example WIT files
4. Write ADR for interface design

### Deliverables

- [ ] `morphir-ir.wit` - Core IR types
- [ ] `morphir-transform.wit` - Transformation interfaces
- [ ] ADR for WIT interface design
- [ ] Type mapping documentation

### Acceptance Criteria

- [ ] WIT files are valid and compilable
- [ ] Complete mapping from Morphir IR to WIT
- [ ] ADR approved by maintainers
- [ ] Examples demonstrate cross-language use

### Effort Estimate

4 days

### Related Issues

Part of Phase 3: WebAssembly Integration Strategy
Depends on: #TBD (3.1)
```

---

### Issue 3.3: Extism Integration Design

```markdown
## Task 3.3: Extism Integration Design

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 3
**Labels**: `skill:architect`, `phase-3`, `priority-p1`, `webassembly`, `extism`

### Description

Design integration with Extism plugin framework to enable polyglot Morphir transformation plugins written in any language that compiles to WebAssembly.

### Prerequisites

- [ ] Task 3.2: Design WIT Interfaces for Morphir IR

### Activities

1. Study Extism architecture
   - Plugin interface
   - Host SDK (.NET)
   - Plugin SDK (multiple languages)
2. Design Morphir plugin interface for Extism
3. Create proof-of-concept
   - Simple transformation in Rust
   - Host in morphir-dotnet
4. Document integration strategy

### Deliverables

- [ ] Extism integration design document
- [ ] Proof-of-concept Rust plugin
- [ ] .NET host integration code
- [ ] Plugin development guide

### Acceptance Criteria

- [ ] Proof-of-concept works end-to-end
- [ ] Clear path for multi-language plugins
- [ ] Performance acceptable for production
- [ ] Documentation enables plugin authors

### Effort Estimate

3 days

### Resources

- https://extism.org/

### Related Issues

Part of Phase 3: WebAssembly Integration Strategy
Depends on: #TBD (3.2)
```

---

### Issue 3.4: Cross-Runtime Communication Strategy

```markdown
## Task 3.4: Cross-Runtime Communication Strategy

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 3
**Labels**: `skill:architect`, `phase-3`, `priority-p1`, `webassembly`, `performance`

### Description

Design efficient cross-runtime communication strategy for .NET ↔ WebAssembly, including serialization, type marshalling, and async operations.

### Prerequisites

- [ ] Task 3.3: Extism Integration Design

### Activities

1. Design IR serialization for WASM boundary
   - Efficient binary format
   - Backwards compatibility
2. Design type marshalling
   - .NET ↔ WASM type mapping
   - Handle complex types (discriminated unions)
3. Design async operation support
   - Async transformations
   - Cancellation support
4. Create benchmarks

### Deliverables

- [ ] Serialization format specification
- [ ] Type marshalling guide
- [ ] Async operation design
- [ ] Performance benchmarks

### Acceptance Criteria

- [ ] Serialization format documented
- [ ] Benchmarks show acceptable overhead (<10%)
- [ ] Async operations work correctly
- [ ] Documentation complete

### Effort Estimate

3 days

### Related Issues

Part of Phase 3: WebAssembly Integration Strategy
Depends on: #TBD (3.3)
```

---

### Issue 3.5: WebAssembly Automation Scripts

```markdown
## Task 3.5: WebAssembly Automation Scripts

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 3
**Labels**: `skill:architect`, `phase-3`, `priority-p2`, `automation`, `webassembly`

### Description

Create automation scripts for WebAssembly-related tasks including WIT generation and plugin building.

### Prerequisites

- [ ] Task 3.2: Design WIT Interfaces for Morphir IR
- [ ] Task 3.3: Extism Integration Design

### Activities

1. Create `generate-wit-interface.fsx`
   - Generate WIT from Morphir IR types
   - Validate generated WIT
2. Create `build-wasm-plugin.fsx`
   - Build WASM plugins from source
   - Run Extism plugin tests

### Deliverables

- [ ] `scripts/generate-wit-interface.fsx`
- [ ] `scripts/build-wasm-plugin.fsx`
- [ ] Script documentation

### Acceptance Criteria

- [ ] Scripts automate common WASM tasks
- [ ] Save 700+ tokens vs manual approach
- [ ] Error messages helpful

### Effort Estimate

2 days

### Related Issues

Part of Phase 3: WebAssembly Integration Strategy
Depends on: #TBD (3.2), #TBD (3.3)
```

---

## Phase 4: Semantic Web Integration

### Issue 4.1: RDF Schema Design

```markdown
## Task 4.1: RDF Schema Design

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 4
**Labels**: `skill:architect`, `phase-4`, `priority-p2`, `rdf`, `semantic-web`

### Description

Design comprehensive RDF ontology for Morphir IR to enable semantic queries, provenance tracking, and linked data integration.

### Prerequisites

- [ ] Phase 3 complete

### Activities

1. Study existing ontologies
   - Schema.org
   - DOAP (Description of a Project)
   - SKOS (Simple Knowledge Organization System)
2. Design Morphir IR ontology
   - Core concepts (Package, Module, Type, Value)
   - Relationships (contains, references, extends)
   - Attributes (visibility, documentation)
3. Create RDF schema (Turtle format)
4. Create example RDF instances

### Deliverables

- [ ] Morphir IR ontology (morphir.ttl)
- [ ] Ontology documentation
- [ ] Example RDF instances (10+ examples)
- [ ] SPARQL query examples

### Acceptance Criteria

- [ ] Ontology validates with RDF tools
- [ ] Covers all Morphir IR concepts
- [ ] Examples demonstrate usefulness
- [ ] SPARQL queries work correctly

### Effort Estimate

4 days

### Resources

- https://www.w3.org/TR/rdf11-concepts/
- https://www.w3.org/TR/sparql11-query/

### Related Issues

Part of Phase 4: Semantic Web Integration
```

---

### Issue 4.2: JSON-LD Context Design

```markdown
## Task 4.2: JSON-LD Context Design

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 4
**Labels**: `skill:architect`, `phase-4`, `priority-p2`, `json-ld`, `semantic-web`

### Description

Design JSON-LD context for Morphir IR to add semantic attribution and enable linked data traversal without changing existing JSON format.

### Prerequisites

- [ ] Task 4.1: RDF Schema Design

### Activities

1. Design JSON-LD context for Morphir IR
   - Map IR JSON to RDF vocabulary
   - Add attribution metadata
   - Support provenance tracking
2. Create JSON-LD context file
3. Create example JSON-LD documents
4. Validate with JSON-LD tools

### Deliverables

- [ ] morphir-context.jsonld
- [ ] JSON-LD examples (10+ examples)
- [ ] Conversion guide (JSON → JSON-LD)
- [ ] Validation results

### Acceptance Criteria

- [ ] Context validates with JSON-LD tools
- [ ] Examples demonstrate attribution
- [ ] Conversion from existing IR works
- [ ] Documentation clear

### Effort Estimate

3 days

### Resources

- https://www.w3.org/TR/json-ld11/

### Related Issues

Part of Phase 4: Semantic Web Integration
Depends on: #TBD (4.1)
```

---

### Issue 4.3: Linked Data Navigation Design

```markdown
## Task 4.3: Linked Data Navigation Design

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 4
**Labels**: `skill:architect`, `phase-4`, `priority-p2`, `linked-data`, `api-design`

### Description

Design linked data navigation system allowing IR nodes to be dereferenced as URIs with content negotiation for different formats.

### Prerequisites

- [ ] Task 4.2: JSON-LD Context Design

### Activities

1. Design URI scheme for IR nodes
   - Package URIs
   - Module URIs
   - Type/Value URIs
2. Design content negotiation
   - JSON representation
   - RDF/Turtle representation
   - HTML representation
3. Create navigation API design
4. Document navigation patterns

### Deliverables

- [ ] URI scheme specification
- [ ] Content negotiation design
- [ ] Navigation API design
- [ ] Usage examples

### Acceptance Criteria

- [ ] URI scheme is hierarchical and dereferenceable
- [ ] Content negotiation supports 3+ formats
- [ ] API design enables graph traversal
- [ ] Examples demonstrate linked data benefits

### Effort Estimate

3 days

### Resources

- https://www.w3.org/DesignIssues/LinkedData.html

### Related Issues

Part of Phase 4: Semantic Web Integration
Depends on: #TBD (4.2)
```

---

### Issue 4.4: RDF Conversion Script

```markdown
## Task 4.4: RDF Conversion Script

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 4
**Labels**: `skill:architect`, `phase-4`, `priority-p2`, `automation`, `rdf`

### Description

Create automation script to convert Morphir IR JSON to RDF and JSON-LD formats with SPARQL query library.

### Prerequisites

- [ ] Task 4.1: RDF Schema Design
- [ ] Task 4.2: JSON-LD Context Design

### Activities

1. Implement `rdf-converter.fsx`
   - Convert Morphir IR JSON to RDF
   - Generate JSON-LD from IR
   - Validate output
2. Add SPARQL query examples
3. Create usage guide

### Deliverables

- [ ] `scripts/rdf-converter.fsx`
- [ ] SPARQL query library (20+ queries)
- [ ] Conversion guide
- [ ] Example outputs

### Acceptance Criteria

- [ ] Script converts IR to RDF correctly
- [ ] SPARQL queries work on converted data
- [ ] Saves 600+ tokens vs manual conversion
- [ ] Documentation complete

### Effort Estimate

3 days

### Related Issues

Part of Phase 4: Semantic Web Integration
Depends on: #TBD (4.1), #TBD (4.2)
```

---

## Phase 5: Integration and Documentation

### Issue 5.1: Cross-Skill Integration

```markdown
## Task 5.1: Cross-Skill Integration

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 5
**Labels**: `skill:architect`, `phase-5`, `priority-p0`, `integration`

### Description

Create integration playbooks and handoff protocols for coordinating Morphir Application Architect with other expert skills (aot-guru, elm-to-fsharp-guru, technical-writer, qa-tester).

### Prerequisites

- [ ] Phases 1-4 complete

### Activities

1. Coordinate with aot-guru
   - WASM compilation integration
   - AOT compatibility checks
2. Coordinate with elm-to-fsharp-guru
   - Transformation pipeline integration
   - Migration pattern sharing
3. Coordinate with technical-writer
   - Documentation review
   - Diagram creation
4. Coordinate with qa-tester
   - Architecture validation tests

### Deliverables

- [ ] Integration playbooks (4 playbooks)
- [ ] Cross-skill decision trees
- [ ] Handoff protocols documented
- [ ] Example multi-skill workflows

### Acceptance Criteria

- [ ] Clear integration points with each skill
- [ ] Playbooks tested with real scenarios
- [ ] Handoff protocols smooth
- [ ] Multi-skill examples work end-to-end

### Effort Estimate

4 days

### Related Issues

Part of Phase 5: Integration and Documentation
```

---

### Issue 5.2: Complete Pattern Catalog

```markdown
## Task 5.2: Complete Pattern Catalog

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 5
**Labels**: `skill:architect`, `phase-5`, `priority-p1`, `patterns`, `documentation`

### Description

Create comprehensive pattern catalog with 20+ architectural patterns, decision trees, code examples, and evolution tracking.

### Prerequisites

- [ ] Phases 1-4 complete

### Activities

1. Document 20+ architectural patterns
   - Visitor pattern variants
   - Transformation chain patterns
   - Plugin patterns
   - WASM integration patterns
   - Semantic attribution patterns
2. Create decision trees for pattern selection
3. Provide code examples for each pattern
4. Document pattern evolution

### Deliverables

- [ ] Pattern catalog (20+ patterns)
- [ ] Decision trees (5+ trees)
- [ ] Code examples (tested)
- [ ] Pattern evolution log

### Acceptance Criteria

- [ ] 20+ patterns documented
- [ ] Each pattern has working example
- [ ] Decision trees guide pattern selection
- [ ] Evolution tracked

### Effort Estimate

5 days

### Related Issues

Part of Phase 5: Integration and Documentation
```

---

### Issue 5.3: Architecture Guides

```markdown
## Task 5.3: Architecture Guides

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 5
**Labels**: `skill:architect`, `phase-5`, `priority-p1`, `documentation`

### Description

Create comprehensive architecture guides covering pluggable pipeline, WebAssembly integration, and semantic web in Morphir with diagrams and examples.

### Prerequisites

- [ ] Task 5.2: Complete Pattern Catalog

### Activities

1. Write "Pluggable Pipeline Architecture" guide
2. Write "WebAssembly Integration" guide
3. Write "Semantic Web in Morphir" guide
4. Create architecture diagrams (Mermaid, PlantUML)

### Deliverables

- [ ] Pluggable pipeline guide
- [ ] WASM integration guide
- [ ] Semantic web guide
- [ ] 10+ architecture diagrams

### Acceptance Criteria

- [ ] Guides comprehensive and clear
- [ ] Each guide includes examples
- [ ] Diagrams illustrate key concepts
- [ ] Reviewed by technical-writer skill

### Effort Estimate

4 days

### Related Issues

Part of Phase 5: Integration and Documentation
Depends on: #TBD (5.2)
Collaboration: @skill technical-writer
```

---

### Issue 5.4: Tutorial Series

```markdown
## Task 5.4: Tutorial Series

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 5
**Labels**: `skill:architect`, `phase-5`, `priority-p1`, `documentation`, `tutorials`

### Description

Create tutorial series teaching developers how to use the Morphir Application Architect patterns and tools.

### Prerequisites

- [ ] Task 5.3: Architecture Guides

### Activities

1. "Building Your First Transformation Plugin"
2. "Creating a WASM-Based Code Generator"
3. "Adding Semantic Attribution to IR"

### Deliverables

- [ ] Tutorial 1: Transformation plugin
- [ ] Tutorial 2: WASM generator
- [ ] Tutorial 3: Semantic attribution
- [ ] Tutorial code repositories

### Acceptance Criteria

- [ ] Tutorials step-by-step and clear
- [ ] Code in tutorials tested and working
- [ ] Beginners can follow tutorials
- [ ] Reviewed by technical-writer

### Effort Estimate

3 days

### Related Issues

Part of Phase 5: Integration and Documentation
Depends on: #TBD (5.3)
Collaboration: @skill technical-writer
```

---

### Issue 5.5: Architecture Health Monitoring

```markdown
## Task 5.5: Architecture Health Monitoring

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 5
**Labels**: `skill:architect`, `phase-5`, `priority-p1`, `automation`, `ci-cd`

### Description

Create architecture health monitoring script to continuously validate architectural decisions, plugin compatibility, and semantic metadata presence.

### Prerequisites

- [ ] All other Phase 5 tasks

### Activities

1. Create `architecture-report.fsx`
   - Scan codebase for architecture violations
   - Check plugin compatibility
   - Verify semantic metadata presence
2. Integrate with CI/CD
3. Create health score dashboard

### Deliverables

- [ ] `scripts/architecture-report.fsx`
- [ ] CI/CD integration
- [ ] Health score metrics
- [ ] Report examples

### Acceptance Criteria

- [ ] Script generates comprehensive reports
- [ ] Saves 800+ tokens vs manual review
- [ ] Integrates with GitHub Actions
- [ ] Actionable recommendations

### Effort Estimate

2 days

### Related Issues

Part of Phase 5: Integration and Documentation
Collaboration: @skill qa-tester
```

---

### Issue 5.6: Final Documentation Review

```markdown
## Task 5.6: Final Documentation Review

**Epic**: #TBD (Morphir Application Architect Skill)
**Phase**: Phase 5
**Labels**: `skill:architect`, `phase-5`, `priority-p0`, `documentation`, `review`

### Description

Comprehensive documentation review to ensure consistency, accuracy, and completeness across all Morphir Application Architect skill documentation.

### Prerequisites

- [ ] All Phase 5 tasks

### Activities

1. Review all skill documentation
2. Ensure consistency across all files
3. Update cross-references
4. Get final approval from technical-writer

### Deliverables

- [ ] Documentation review checklist
- [ ] Cross-reference validation
- [ ] Final approval sign-off
- [ ] Published documentation

### Acceptance Criteria

- [ ] No broken links
- [ ] Consistent terminology
- [ ] All examples tested
- [ ] Technical-writer approval

### Effort Estimate

2 days

### Related Issues

Part of Phase 5: Integration and Documentation
Depends on: All Phase 5 tasks
Collaboration: @skill technical-writer
Final milestone for Morphir Application Architect skill
```

---

## Project Tracking

### Labels to Create

- `epic`
- `skill:architect`
- `phase-1`
- `phase-2`
- `phase-3`
- `phase-4`
- `phase-5`
- `priority-p0` (Critical)
- `priority-p1` (High)
- `priority-p2` (Medium)
- `research`
- `documentation`
- `patterns`
- `implementation`
- `automation`
- `webassembly`
- `rdf`
- `semantic-web`
- `integration`
- `tutorials`

### Project Board

Create a GitHub Project board with columns:
- **Backlog**: All issues not yet started
- **In Progress**: Currently being worked on
- **Review**: Awaiting review/approval
- **Done**: Completed and merged

### Milestones

- **Phase 1: Core Architectural Guidance** - Week 4
- **Phase 2: Pluggable Pipeline Architecture** - Week 8
- **Phase 3: WebAssembly Integration Strategy** - Week 12
- **Phase 4: Semantic Web Integration** - Week 16
- **Phase 5: Integration and Documentation** - Week 20

---

## Instructions for Creating Issues

1. **Create Epic Issue First**: Start with the Epic issue to establish the parent tracking issue
2. **Create Phase Issues in Order**: Create issues for Phase 1, then Phase 2, etc.
3. **Link Dependencies**: Use "Depends on #issue-number" in issue descriptions
4. **Apply Labels**: Use appropriate labels for tracking and filtering
5. **Assign Milestones**: Link each issue to its phase milestone
6. **Add to Project Board**: Add all issues to the project board in Backlog column
7. **Update Epic**: As phase issues are created, update the Epic with issue numbers

## CLI Command to Create Issues (Example)

```bash
# Using GitHub CLI
gh issue create \
  --title "Epic: Morphir Application Architect Skill" \
  --body-file epic-issue.md \
  --label "epic,skill:architect,enhancement"

# Create subsequent issues and link to epic
gh issue create \
  --title "Task 1.1: Morphir Ecosystem Documentation Review" \
  --body-file task-1.1.md \
  --label "skill:architect,phase-1,priority-p0,documentation" \
  --milestone "Phase 1: Core Architectural Guidance"
```

---

**Document Purpose**: Pre-formatted GitHub issues for task tracking
**Usage**: Copy issue markdown into GitHub issue creation forms or use GitHub CLI
**Maintenance**: Update as implementation plan evolves
