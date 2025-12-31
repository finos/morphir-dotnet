# Product Requirements Document: F# Backend for Morphir

**Document Version**: 1.0
**Created**: 2025-12-31
**Status**: Draft
**Owner**: Morphir Architecture Team
**Epic**: F# Code Generation Backend

---

## Executive Summary

This PRD defines the requirements for implementing an F# code generation backend for morphir-dotnet. The backend will consume Morphir IR (JSON format) and generate idiomatic, type-safe F# code using Fabulous.AST and Fantomas libraries.

### Vision

Enable developers to:
1. Generate production-ready F# code from Morphir IR
2. Leverage Morphir's platform-agnostic business logic in F# applications
3. Maintain type safety and functional programming principles throughout the transformation

### Success Metrics

- **Code Generation**: Successfully generate F# code for 100% of Morphir IR v3 type and value constructs
- **Quality**: Generated code compiles without errors and passes all tests
- **Performance**: Generate code for large IRs (<1000 types) in under 5 seconds
- **Adoption**: At least 3 example projects using the F# backend within 6 months
- **Boilerplate Reduction**: Achieve 93% reduction in AST construction code vs. manual FSharp.Compiler.Service

---

## Background

### Problem Statement

morphir-dotnet currently consumes and validates Morphir IR but lacks the ability to generate executable code from it. Users must manually translate Morphir models into F# or C# code, which is:
- **Time-consuming**: Manual translation takes days/weeks for large models
- **Error-prone**: Human errors during translation break type safety
- **Non-scalable**: Changes to Morphir models require manual re-translation

### Current State

- ✅ morphir-dotnet can load and validate Morphir IR (v1, v2, v3)
- ✅ Classic IR (F#) and Modern IR (C#) representations exist
- ✅ `Morphir.IR.Pipeline` provides pluggable transformation infrastructure
- ✅ Dependencies ready: Fabulous.AST v1.9.0, Fantomas.Core v7.0.5
- ❌ No code generation backends exist (all generator stubs return empty output)

### Morphir Ecosystem Context

**morphir-elm** provides backends for:
- Scala (with JSON codecs)
- TypeScript (types only)
- SpringBoot (REST API scaffold)
- Cypher (Neo4j queries)

**morphir-dotnet** should provide:
- **F# backend** (this PRD) - Functional-first, idiomatic F# code
- **C# backend** (future) - Enterprise-friendly, object-oriented code

---

## Goals and Non-Goals

### Goals

1. **Generate idiomatic F# code** from Morphir IR using Fabulous.AST
2. **Support all Morphir type constructs**: Records, discriminated unions, tuples, functions, type aliases
3. **Support all Morphir value constructs**: Literals, functions, pattern matching, let bindings, records, lists, tuples
4. **Map Morphir SDK types to F# built-ins**: `Int → int`, `Maybe → Option`, `Result → Result`, etc.
5. **Integrate with `morphir` CLI**: `morphir gen fsharp --input morphir-ir.json --output ./generated`
6. **Auto-format output**: Use Fantomas for consistent F# style
7. **Support advanced features**: JSON codecs (via Thoth.Json), lens generation (optional)
8. **Follow TDD practices**: Red-Green-Refactor for all components
9. **Achieve ≥80% test coverage**: Unit, integration, snapshot, E2E tests
10. **AOT-compatible**: Generated code must work with Native AOT compilation

### Non-Goals

1. ❌ **Frontend (Elm → Morphir IR)**: Use morphir-elm for authoring
2. ❌ **IR optimization**: Assume IR is already optimized
3. ❌ **C# backend**: Separate PRD (future work)
4. ❌ **Custom Morphir runtime**: Use F# standard library + Morphir.SDK
5. ❌ **IDE integration**: CLI-first, editor support is out of scope
6. ❌ **Backwards compatibility with IR v1/v2**: Focus on v3 (current standard)

---

## User Stories

### US-1: Developer Generates F# Code from IR

**As a** developer using Morphir
**I want to** generate F# code from my Morphir IR
**So that** I can integrate business logic into my F# application without manual translation

**Acceptance Criteria**:
- Given a valid morphir-ir.json (v3 format)
- When I run `morphir gen fsharp --input morphir-ir.json --output ./generated`
- Then F# files are created in `./generated` directory
- And all generated files compile successfully with `dotnet build`
- And generated code matches Morphir IR semantics

### US-2: Developer Maps SDK Types to F# Types

**As a** developer generating F# code
**I want** Morphir SDK types to map to F# built-in types
**So that** generated code integrates seamlessly with F# standard library

**Acceptance Criteria**:
- `Morphir.SDK.Basics.Int` → `int`
- `Morphir.SDK.Maybe.Maybe` → `Option<'a>`
- `Morphir.SDK.Result.Result` → `Result<'v, 'e>`
- `Morphir.SDK.List.List` → `List<'a>`
- All SDK functions map to F# equivalents (e.g., `List.map` → `List.map`)

### US-3: Developer Generates JSON Codecs

**As a** developer integrating with web APIs
**I want to** generate JSON encoders/decoders for my Morphir types
**So that** I can serialize/deserialize data without manual codec writing

**Acceptance Criteria**:
- Given `--codecs` flag
- When I run `morphir gen fsharp --codecs`
- Then Thoth.Json encoders and decoders are generated
- And codecs handle recursive types correctly
- And codecs handle polymorphic types correctly

### US-4: Developer Generates Lenses for Records

**As a** developer working with nested records
**I want to** generate lens functions for my record types
**So that** I can update nested fields without verbose `with` expressions

**Acceptance Criteria**:
- Given `--lenses` flag
- When I run `morphir gen fsharp --lenses`
- Then lens functions are generated in a `Lenses` module
- And lenses compose correctly (`addressLens >>> cityLens`)

### US-5: Developer Customizes Generated Namespace

**As a** developer integrating generated code
**I want to** specify the root namespace for generated F# code
**So that** it fits into my project structure

**Acceptance Criteria**:
- Given `--namespace MyApp.Generated`
- When I run `morphir gen fsharp --namespace MyApp.Generated`
- Then all generated files use `namespace MyApp.Generated.Morphir.Reference.Model`

---

## Technical Requirements

### TR-1: Architecture

**Component Structure**:
```
src/Morphir.Backends.FSharp/
├── Mapper.fs          # Morphir IR → Fabulous.AST
├── Generator.fs       # Fabulous.AST → F# code string
├── Helpers.fs         # Morphir-specific DSL helpers
├── SDK.fs             # SDK type/function mappings
└── Plugin.fs          # Pipeline integration
```

**Dependencies**:
- Fabulous.AST >= 1.9.0
- Fantomas.Core >= 7.0.5
- Morphir.Models (Classic IR)
- Morphir.IR.Pipeline

**Pipeline Integration**:
```fsharp
let fsharpPipeline = pipeline {
    parse irJsonParser
    uses (fsharpBackendPlugin options)
    stringify fsharpCodeGenerator
}
```

### TR-2: Type Mapping

**Morphir Types → F# Constructs**:

| Morphir IR Type | F# Output | Fabulous.AST Widget |
|-----------------|-----------|---------------------|
| `TypeAliasDefinition` | `type Alias = ...` | `TypeAbbrev` |
| `CustomTypeDefinition` | `type DU = \| A \| B` | `UnionType` |
| `Record` | `{| field: type |}` (anon) | `AnonRecordType` |
| `Tuple` | `int * string` | `TupleType` |
| `Function` | `int -> string` | `FunctionType` |
| `Reference` | `MyType<'a>` | `AppType` / `LongIdentType` |
| `Variable` | `'a` | `GenericType` |
| `Unit` | `unit` | `LongIdentType("unit")` |

### TR-3: Value Mapping

**Morphir Values → F# Constructs**:

| Morphir IR Value | F# Output | Fabulous.AST Widget |
|------------------|-----------|---------------------|
| `Literal` | `42`, `"hello"`, `true` | `ConstantExpr` |
| `Variable` | `x` | `IdentExpr` |
| `Lambda` | `fun x -> ...` | `LambdaExpr` |
| `Apply` | `f x` | `AppExpr` |
| `IfThenElse` | `if c then t else e` | `IfThenElseExpr` |
| `PatternMatch` | `match x with \| ...` | `MatchExpr` |
| `LetDefinition` | `let x = v in body` | `LetExpr` |
| `Record` | `{ field = value }` | `RecordExpr` |
| `Field` | `record.field` | `DotGetExpr` |
| `List` | `[1; 2; 3]` | `ListExpr` |
| `Tuple` | `(1, "two", 3.0)` | `TupleExpr` |

### TR-4: CLI Interface

**Command Structure**:
```bash
morphir gen fsharp [options]

Options:
  --input <file>           Input IR file (default: morphir-ir.json)
  --output <dir>           Output directory (default: ./generated)
  --namespace <ns>         Root namespace (default: Generated)
  --codecs                 Generate Thoth.Json encoders/decoders
  --lenses                 Generate lens functions for records
  --limit-to-modules <m>   Comma-separated module paths to generate
  --help                   Show help
```

**Examples**:
```bash
# Basic generation
morphir gen fsharp

# With codecs and lenses
morphir gen fsharp --codecs --lenses

# Custom namespace and output
morphir gen fsharp --namespace MyApp.Domain --output src/Generated

# Limit to specific modules
morphir gen fsharp --limit-to-modules "Morphir.Reference.Model,Morphir.Reference.Logic"
```

### TR-5: Output Format

**File Organization**:
```
generated/
├── Morphir/
│   ├── Reference/
│   │   ├── Model.fs          # Types from Morphir.Reference.Model
│   │   ├── Logic.fs          # Functions from Morphir.Reference.Logic
│   │   └── Codecs.fs         # JSON codecs (if --codecs)
│   └── SDK/
│       ├── List.fs
│       ├── Maybe.fs
│       └── Result.fs
└── README.md                  # Generated documentation
```

**Generated File Structure**:
```fsharp
namespace Generated.Morphir.Reference.Model

open System
open Morphir.SDK

// Types
type Person = {
    name: string
    age: int
    email: Option<string>
}

type Result<'e, 'v> =
    | Err of 'e
    | Ok of 'v

// Functions
let validateEmail (email: string) : bool =
    email.Contains("@")

// Codecs (if --codecs)
module Codecs =
    open Thoth.Json

    let personEncoder (p: Person) : JsonValue =
        Encode.object [
            "name", Encode.string p.name
            "age", Encode.int p.age
            "email", Encode.option Encode.string p.email
        ]

    let personDecoder : Decoder<Person> =
        Decode.object (fun get -> {
            name = get.Required.Field "name" Decode.string
            age = get.Required.Field "age" Decode.int
            email = get.Optional.Field "email" Decode.string
        })
```

### TR-6: Quality Requirements

**Code Quality**:
- Generated code must compile without warnings
- Follow F# style guide (enforced by Fantomas)
- No reflection (AOT-compatible)
- No nulls (use Option/Result)
- Exhaustive pattern matching

**Performance**:
- Generate code for 1000 types in < 5 seconds
- Memory usage < 500 MB for large IRs
- Incremental generation (only changed modules)

**Testing**:
- Unit tests for all mapper functions (≥80% coverage)
- Snapshot tests for generated code stability
- Integration tests with morphir-elm examples
- E2E tests: IR → F# code → dotnet build → tests pass

---

## Implementation Plan

### Phase 1: Foundation (Weeks 1-2)

**Goal**: Project setup and Fabulous.AST exploration

**Deliverables**:
- `Morphir.Backends.FSharp` project created
- `Helpers.fs` with Morphir-specific DSL helpers
- `SDK.fs` with complete SDK type/function mappings
- Exploratory tests demonstrating Fabulous.AST capabilities

**Success Criteria**:
- Can create simple F# code using Fabulous.AST
- All Morphir SDK types mapped
- Tests validate basic code generation

### Phase 2: Type Mapping (Weeks 3-4)

**Goal**: Implement Morphir IR Type → Fabulous.AST

**Deliverables**:
- `mapTypeDefinition` for all Type constructors
- `mapTypeExpr` for all Type expressions
- FQName resolution using SDK mappings
- Snapshot tests for generated types

**Success Criteria**:
- All Morphir type constructs generate valid F# types
- Generated types compile successfully
- Snapshot tests pass

### Phase 3: Value Mapping (Weeks 5-6)

**Goal**: Implement Morphir IR Value → Fabulous.AST

**Deliverables**:
- `mapValueDefinition` for functions
- `mapValueExpr` for all Value expressions
- Pattern matching translation
- Snapshot tests for generated functions

**Success Criteria**:
- All Morphir value constructs generate valid F# functions
- Pattern matching is exhaustive
- Currying handled correctly

### Phase 4: CLI Integration (Week 7)

**Goal**: Wire F# backend into `morphir gen` command

**Deliverables**:
- CLI command handler with all options
- Pipeline plugin integration
- File writing with directory creation
- Progress logging (stderr)

**Success Criteria**:
- `morphir gen fsharp` works end-to-end
- All command-line options functional
- Generated files written correctly

### Phase 5: SDK Function Translation (Week 8)

**Goal**: Translate Morphir SDK function calls to F#

**Deliverables**:
- Extended `SDK.fs` with function mappings
- Function call translation in `mapValueExpr`
- Operator translation (`+`, `::`, `++`)
- Tests with morphir-elm SDK examples

**Success Criteria**:
- All SDK functions translate correctly
- Operators work as expected
- Generated code uses F# standard library

### Phase 6: Advanced Features (Weeks 9-10)

**Goal**: JSON codecs, lenses, optimization

**Deliverables**:
- `--codecs` implementation (Thoth.Json)
- `--lenses` implementation
- Performance benchmarks
- Optimization if needed

**Success Criteria**:
- Codecs handle recursive/polymorphic types
- Lenses compose correctly
- Performance < 5s for large IRs

### Phase 7: E2E Testing & Documentation (Week 11)

**Goal**: Comprehensive testing and user docs

**Deliverables**:
- E2E tests with morphir-elm examples
- User guide and tutorial
- API reference documentation
- Example projects

**Success Criteria**:
- All morphir-elm examples generate valid F#
- Documentation complete
- Examples runnable

### Phase 8: Release Preparation (Week 12)

**Goal**: Polish and release

**Deliverables**:
- Code review and refactoring
- Coverage report (≥80%)
- CI/CD integration
- Release notes and blog post

**Success Criteria**:
- Production-ready F# backend
- v1.0.0 release published

---

## Dependencies

### Technical Dependencies

- **Fabulous.AST v1.9.0+**: Code generation DSL
- **Fantomas.Core v7.0.5+**: F# code formatting
- **Morphir.Models**: Classic IR (F# discriminated unions)
- **Morphir.IR.Pipeline**: Pluggable transformation pipeline
- **Thoth.Json** (optional): JSON codec generation

### Process Dependencies

- **morphir-elm**: Provides reference Morphir IR (v3) for testing AND source code patterns for migration assessment
- **QA Tester Skill**: Test plan creation and regression testing
- **Technical Writer Skill**: Documentation and examples
- **AOT Guru Skill**: Ensure generated code is AOT-compatible

### Migration Assessment Dependency

**CRITICAL**: Throughout implementation, systematically assess morphir-elm functionality for migration, adaptation, or F# replacement.

**See**: [Morphir-Elm Migration Assessment](./morphir-elm-migration-assessment.md) for:
- Decision framework (NATIVE vs. ADAPT vs. MIGRATE vs. SKIP vs. NEW)
- Component-by-component assessment
- Traceability requirements
- F# idiom guidelines

**Key Principle**: Maintain traceable connections to morphir-elm while leveraging F# capabilities that exceed Elm's constraints.

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **Fabulous.AST API changes** | Low | High | Pin to specific version, monitor releases |
| **Incomplete Morphir IR coverage** | Medium | High | Comprehensive test suite with morphir-elm examples |
| **Performance issues for large IRs** | Medium | Medium | Profile early, optimize incrementally |
| **F# type system limitations** | Low | Medium | Document unsupported patterns, provide workarounds |
| **Generated code doesn't compile** | High | Critical | Extensive snapshot and compilation tests |
| **AOT compatibility issues** | Medium | High | Involve AOT Guru skill, test trimmed/AOT builds |

---

## Success Criteria

### Must-Have (P0)

- ✅ Generate compilable F# code for all Morphir IR v3 constructs
- ✅ CLI command `morphir gen fsharp` works end-to-end
- ✅ All SDK types map to F# built-ins
- ✅ Generated code passes all tests
- ✅ Test coverage ≥ 80%

### Should-Have (P1)

- ✅ JSON codec generation (`--codecs`)
- ✅ Lens generation (`--lenses`)
- ✅ Performance < 5s for large IRs
- ✅ Comprehensive documentation

### Nice-to-Have (P2)

- Auto-generate .fsproj files
- Incremental generation (only changed modules)
- Watch mode for live regeneration
- IDE integration (F# language service)

---

## Open Questions

1. **Namespace collision handling**: How do we handle Morphir module names that conflict with F# keywords?
   - **Resolution**: Use backtick escaping (`` `module` ``)

2. **Extensible records**: F# doesn't have extensible records - how do we handle them?
   - **Resolution**: Map to regular records (ignore extensibility)

3. **Circular dependencies**: How do we handle mutually recursive modules?
   - **Resolution**: Use `rec module` or consolidate into single file

4. **Generated file organization**: One file per module or consolidate?
   - **Resolution**: One file per Morphir module (matches morphir-elm pattern)

---

## Appendix

### A. Glossary

- **Morphir IR**: Intermediate Representation - Technology-agnostic AST for business logic
- **Fabulous.AST**: F# DSL for generating F# code via computation expressions
- **Fantomas**: F# code formatter (enforces F# style guide)
- **Oak**: Fantomas's internal AST representation
- **Classic IR**: F# representation of Morphir IR (discriminated unions)
- **Modern IR**: C# representation of Morphir IR (sealed records)
- **FQName**: Fully-Qualified Name (PackagePath, ModulePath, LocalName)
- **TDD**: Test-Driven Development (Red-Green-Refactor)
- **AOT**: Ahead-of-Time compilation (Native AOT)

### B. References

- [Morphir Homepage](https://morphir.finos.org)
- [morphir-elm GitHub](https://github.com/finos/morphir-elm)
- [Fabulous.AST Documentation](https://edgarfgp.github.io/Fabulous.AST/)
- [Fantomas Documentation](https://fsprojects.github.io/fantomas/)
- [F# Style Guide](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/)

### C. Related Documents

- [AGENTS.md](../../AGENTS.md) - Primary project guidance
- [Ecosystem Knowledge Base](../../.agents/kbs/ecosystem-knowledge-base.md)
- [Language Design Patterns KB](../../.agents/kbs/language-design-patterns.md)
- [Computation Expressions for AST KB](../../.agents/kbs/computation-expressions-for-ast.md)

---

**Document Status**: Ready for Review
**Next Steps**: Create GitHub issues, assign to team, begin Phase 1 implementation
