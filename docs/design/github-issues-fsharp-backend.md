# GitHub Issues for F# Backend Implementation

**Epic**: F# Code Generation Backend
**Total Issues**: 9 (1 Epic + 8 Implementation)
**Estimated Timeline**: 12 weeks

---

## Issue #1: [EPIC] F# Code Generation Backend

**Type**: Epic
**Labels**: `epic`, `feature`, `backend`, `code-generation`
**Priority**: P0
**Milestone**: v1.0.0

### Description

Implement a complete F# code generation backend for morphir-dotnet that transforms Morphir IR (JSON) into idiomatic, type-safe F# code using Fabulous.AST and Fantomas.

### Vision

Enable developers to generate production-ready F# code from Morphir IR, eliminating manual translation and maintaining type safety throughout the business logic transformation.

### Goals

- ✅ Generate idiomatic F# code from Morphir IR v3
- ✅ Support all Morphir type and value constructs
- ✅ Map Morphir SDK types to F# built-in types
- ✅ Integrate with `morphir gen fsharp` CLI command
- ✅ Auto-format output using Fantomas
- ✅ Support JSON codecs and lens generation (optional)
- ✅ Achieve ≥80% test coverage
- ✅ AOT-compatible generated code

### Success Metrics

- All Morphir IR v3 constructs generate valid F# code
- Generated code compiles without errors
- Performance: < 5s for 1000 types
- Test coverage ≥ 80%
- At least 3 example projects using the backend

### Related Documents

- [PRD: F# Backend](../design/PRD-fsharp-backend.md)
- [Ecosystem Knowledge Base](../../.agents/kbs/ecosystem-knowledge-base.md)
- [Computation Expressions for AST KB](../../.agents/kbs/computation-expressions-for-ast.md)

### Child Issues

- **#TBD: Phase 0: Morphir.SDK F# Library Implementation** (NEW - Critical dependency)
- #2 Foundation: Project Setup and Fabulous.AST Exploration
- #3 Type Mapping: Morphir IR Types → F# Types
- #4 Value Mapping: Morphir IR Values → F# Functions
- #5 CLI Integration: `morphir gen fsharp` Command
- #6 SDK Translation: Morphir SDK → Morphir.SDK Library
- #7 Advanced Features: JSON Codecs and Lenses
- #8 Testing and Documentation
- #9 Release Preparation

### Dependencies

- Fabulous.AST v1.9.0+ (already in Directory.Packages.props)
- Fantomas.Core v7.0.5+ (already in Directory.Packages.props)
- Morphir.IR.Pipeline (transformation infrastructure)

---

## Issue #TBD: Phase 0: Morphir.SDK F# Library Implementation

**Type**: Feature
**Labels**: `feature`, `sdk`, `library`, `phase-0`
**Priority**: P0 (Critical - Blocks Phase 1)
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 1-2 weeks (Can run in parallel with Phase 1 planning)
**Assignee**: TBD

### Description

Create the `Morphir.SDK` F# runtime library that provides Morphir SDK types and functions for generated F# code. This library ensures generated code has proper implementations of Morphir SDK semantics without requiring code generation for every SDK function.

**Strategic Decision**: Following morphir-scala and morphir-jvm patterns, we create a runtime library rather than pure type mapping. This provides:
- Consistent behavior across backends
- Versioning of SDK semantics
- Reusability from `main-archive` SDK code
- Foundation for complex SDK types (Key, Aggregate, Rule)

### Acceptance Criteria

**Project Setup**:
- [ ] Create `src/Morphir.SDK/Morphir.SDK.fsproj` (F# class library)
- [ ] Create `tests/Morphir.SDK.Tests/Morphir.SDK.Tests.fsproj` (TUnit)
- [ ] Add to `morphir-dotnet.sln`
- [ ] Configure NuGet package metadata
- [ ] Reference from F# backend (when available)

**Core Modules** (Phase 0.1 - Week 0.5):
- [ ] `Basics.fs`: Order type, comparison functions
- [ ] `Maybe.fs`: Type alias for Option + functions
- [ ] `Result.fs`: Type alias for Result + functions
- [ ] `List.fs`: Extensions to F# List module
- [ ] `String.fs`: Extensions to F# String
- [ ] `Int.fs`: Extensions to int
- [ ] `Bool.fs`: Minimal extensions (F# bool sufficient)
- [ ] `Char.fs`: Extensions to char

**Collections & Date/Time** (Phase 0.2 - Week 0.5):
- [ ] `Dict.fs`: Type alias for Map + extensions
- [ ] `Set.fs`: Extensions to F# Set
- [ ] `Tuple.fs`: Tuple helper functions
- [ ] `LocalDate.fs`: DateOnly alias (.NET 6+)
- [ ] `LocalTime.fs`: TimeOnly alias (.NET 6+)
- [ ] `Decimal.fs`: Decimal extensions

**Advanced Types** (Phase 0.3 - Optional for initial release):
- [ ] `Instant.fs`: DateTimeOffset alias
- [ ] `Month.fs`: Month enumeration
- [ ] `UUID.fs`: Guid alias
- [ ] `Regex.fs`: Regex wrapper
- [ ] `Key.fs`: Key<'a> type for lookups
- [ ] `Aggregate.fs`: Aggregation functions

**Testing**:
- [ ] Unit tests for all modules (≥80% coverage)
- [ ] Property-based tests (FsCheck) for laws
- [ ] Compatibility tests with morphir-elm semantics
- [ ] Examples for each module

**Documentation**:
- [ ] XML doc comments on all public APIs
- [ ] README.md with usage examples
- [ ] API reference documentation
- [ ] Migration guide from main-archive

**NuGet Package**:
- [ ] Publish `Morphir.SDK 0.4.0-alpha` to NuGet
- [ ] Validate package can be consumed
- [ ] Test in sample F# project

### Tasks

1. **Review Existing Code** (main-archive)
   - [ ] Extract `src/Morphir.SDK.Core/` code from main-archive branch
   - [ ] Review implementation quality and AOT compatibility
   - [ ] Identify reusable patterns (Order type, comparison functions)
   - [ ] Update to .NET 10 / F# 9 if needed

2. **Implement Core Modules** (Phase 0.1)
   - [ ] Create project structure
   - [ ] Implement Basics.fs (Order type - reuse from main-archive)
   - [ ] Implement Maybe.fs (type alias + delegate to Option)
   - [ ] Implement Result.fs (type alias + delegate to Result)
   - [ ] Implement List.fs (extensions - some from main-archive)
   - [ ] Implement String.fs, Int.fs, Bool.fs, Char.fs

3. **Implement Collections & Date/Time** (Phase 0.2)
   - [ ] Implement Dict.fs (Map alias + extensions)
   - [ ] Implement Set.fs (extensions)
   - [ ] Implement Tuple.fs (helper functions)
   - [ ] Implement LocalDate.fs (DateOnly alias)
   - [ ] Implement LocalTime.fs (TimeOnly alias)
   - [ ] Implement Decimal.fs (extensions)

4. **Write Tests**
   - [ ] Unit tests for all functions
   - [ ] Property tests (e.g., map preserves length)
   - [ ] Compatibility tests with morphir-elm behavior
   - [ ] Examples as executable documentation

5. **Documentation**
   - [ ] Add XML doc comments
   - [ ] Create README with usage guide
   - [ ] Document differences from morphir-elm (if any)
   - [ ] Create migration notes from main-archive

6. **Package & Publish**
   - [ ] Configure NuGet package metadata
   - [ ] Build and pack
   - [ ] Publish to NuGet (alpha)
   - [ ] Test installation in sample project

### Example Implementation

**Morphir.SDK/Maybe.fs**:
```fsharp
namespace Morphir.SDK

/// <summary>
/// Morphir Maybe type - alias for F# Option.
/// Maintains semantic alignment with Morphir IR.
/// ADAPTED FROM: morphir-elm src/Morphir/IR/SDK/Maybe.elm
/// </summary>
type Maybe<'a> = Option<'a>

module Maybe =
    /// <summary>Apply a function to a Maybe value</summary>
    let inline map f maybe = Option.map f maybe

    /// <summary>Chain Maybe operations</summary>
    let inline andThen f maybe = Option.bind f maybe

    /// <summary>Return the value or a default</summary>
    let inline withDefault defaultValue maybe =
        Option.defaultValue defaultValue maybe

    /// <summary>Convert Maybe to Result</summary>
    let inline toResult error maybe =
        match maybe with
        | Some value -> Ok value
        | None -> Error error
```

**Tests**:
```fsharp
module Morphir.SDK.Tests.MaybeTests

open TUnit.Core
open Morphir.SDK

[<Test>]
let ``Maybe.map should transform Some values`` () =
    let result = Maybe.map ((*) 2) (Some 5)
    result |> should equal (Some 10)

[<Test>]
let ``Maybe.withDefault should return default for None`` () =
    let result = Maybe.withDefault 0 None
    result |> should equal 0
```

### Success Criteria

- `Morphir.SDK` NuGet package published (v0.4.0-alpha)
- All core modules implemented with tests
- Coverage ≥ 80%
- Can be consumed by F# projects
- Ready for F# backend Phase 1 integration

### Dependencies

- None (this is Phase 0 - foundation for all other phases)

### Blocks

- Issue #2 (Foundation - needs SDK library to reference)
- All subsequent phases depend on SDK library

### Related Documents

- [Morphir SDK Library Plan](../design/morphir-sdk-library-plan.md)
- [Morphir-Elm Migration Assessment](../design/morphir-elm-migration-assessment.md) - Section 1
- [main-archive SDK Code](https://github.com/finos/morphir-dotnet/tree/main-archive/src/Morphir.SDK.Core)

---

## Issue #2: Foundation: Project Setup and Fabulous.AST Exploration

**Type**: Feature
**Labels**: `feature`, `backend`, `fsharp`, `phase-1`
**Priority**: P0
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 2 weeks
**Assignee**: TBD

### Description

Set up the `Morphir.Backends.FSharp` project and create foundational infrastructure including Morphir-specific helpers and complete SDK type/function mappings.

### Acceptance Criteria

**Project Structure**:
- [ ] Create `src/Morphir.Backends.FSharp/Morphir.Backends.FSharp.fsproj`
- [ ] Create `tests/Morphir.Backends.FSharp.Tests/Morphir.Backends.FSharp.Tests.fsproj`
- [ ] Add projects to `morphir-dotnet.sln`
- [ ] Configure dependencies: Fabulous.AST, Fantomas.Core, Morphir.Models, Morphir.IR.Pipeline

**Core Files**:
- [ ] `Helpers.fs`: Morphir-specific DSL helpers (curried functions, pipelines, ROP helpers)
- [ ] `SDK.fs`: Complete mapping of Morphir SDK types/functions to F# equivalents
- [ ] README.md with project overview and contribution guide

**Exploratory Tests**:
- [ ] Test creating simple F# types using Fabulous.AST (records, DUs, type aliases)
- [ ] Test creating simple F# functions using Fabulous.AST
- [ ] Test Oak rendering and Fantomas formatting
- [ ] Validate all Morphir SDK types are mapped (Int, Bool, Maybe, Result, List, Dict, Set, String, Decimal)

**Documentation**:
- [ ] Document Fabulous.AST API patterns used
- [ ] Document SDK mapping decisions
- [ ] Add code examples to README

### Migration Assessment Tasks (CRITICAL)

**Before Implementation**:
- [ ] Review [Morphir-Elm Migration Assessment](../design/morphir-elm-migration-assessment.md)
- [ ] Assess morphir-elm `Morphir.IR.SDK.*` modules for F# equivalents
- [ ] Document decision: Pure SDK mapping vs. runtime library
- [ ] Create SDK type mapping table (Morphir SDK → F# built-in types)
- [ ] Create SDK function mapping table (Morphir SDK → F# stdlib functions)
- [ ] Study morphir-elm keyword escaping approach
- [ ] List F# keywords for escaping (`fsharpKeywords` set)

**Traceability Requirements**:
- [ ] Add code comments linking to morphir-elm source patterns
- [ ] Document deviations from morphir-elm in decision log
- [ ] Test SDK mappings with morphir-elm examples

**Decision Log Entry**:
```markdown
### SDK Type Mapping Strategy (Phase 1)

**morphir-elm**: Provides `Morphir.IR.SDK.*` Elm modules

**Decision**: NATIVE - Use F# built-in types (no runtime library)

**Rationale**:
- F# has Option<'a>, Result<'v, 'e>, List<'a> natively
- .NET 6+ has DateOnly, TimeOnly (better than Elm)
- Generated code has zero Morphir dependencies

**Implementation**: Create mapping tables in `SDK.fs`
```

### Tasks

1. **Project Setup**
   ```bash
   dotnet new classlib -n Morphir.Backends.FSharp -lang F#
   dotnet new tunit -n Morphir.Backends.FSharp.Tests -lang F#
   dotnet sln add src/Morphir.Backends.FSharp
   dotnet sln add tests/Morphir.Backends.FSharp.Tests
   ```

2. **Migration Assessment: SDK Types**
   - [ ] Review morphir-elm `src/Morphir/IR/SDK/` modules
   - [ ] For each module, decide: NATIVE | ADAPT | MIGRATE | SKIP
   - [ ] Document decisions in migration assessment doc
   - [ ] Create SDK type mapping table (see assessment doc for structure)

3. **Create Helpers.fs**
   - Implement `curriedFunction` helper
   - Implement `pipelineExpr` helper (|>)
   - Implement `composeExpr` helper (>>)
   - Implement `resultBindChain` for ROP
   - Implement `optionMap`, `listMap` helpers
   - Implement `documentedValue`, `documentedFunction` for XML docs

4. **Create SDK.fs** (Based on Migration Assessment)
   - Create `sdkTypeMap` with all Morphir SDK → F# type mappings
   - Create `sdkFunctionMap` with all Morphir SDK → F# function mappings
   - Implement `tryGetFSharpType` lookup function
   - Implement `tryGetFSharpFunction` lookup function
   - Add traceability comments to morphir-elm SDK modules

5. **Write Exploratory Tests**
   - Test: Create simple record using `Record` widget
   - Test: Create discriminated union using `UnionType` widget
   - Test: Create function using `Function` widget
   - Test: Render Oak to string and validate F# syntax
   - Test: Format with Fantomas and validate style compliance
   - Test: SDK type mapping with morphir-elm examples

### Success Criteria

- All tests pass
- Can create simple F# code using Fabulous.AST
- All Morphir SDK types mapped (validate with unit tests)
- Helpers provide convenient Morphir-specific operations
- Code coverage ≥ 80%

### Dependencies

- Phase 0 SDK library (must be published before SDK.fs can reference it)

---

## Issue #3: Type Mapping: Morphir IR Types → F# Types

**Type**: Feature
**Labels**: `feature`, `backend`, `fsharp`, `phase-2`
**Priority**: P0
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 2 weeks
**Assignee**: TBD
**Depends On**: #2

### Description

Implement complete mapping from Morphir IR type definitions and type expressions to Fabulous.AST F# type constructs.

### Acceptance Criteria

**Mapper.fs Implementation**:
- [ ] `mapTypeDefinition`: Handle all Type.Definition variants
  - [ ] TypeAliasDefinition → `TypeAbbrev`
  - [ ] CustomTypeDefinition → `UnionType` with union cases
- [ ] `mapTypeExpr`: Handle all Type constructors
  - [ ] Variable → `GenericType` (`'a`)
  - [ ] Reference → `LongIdentType` or `AppType` (generics)
  - [ ] Tuple → `TupleType`
  - [ ] Record → `AnonRecordType`
  - [ ] Function → `FunctionType`
  - [ ] Unit → `LongIdentType("unit")`
  - [ ] ExtensibleRecord → `AnonRecordType` (F# limitation)

**SDK Type Integration**:
- [ ] Use `SDK.tryGetFSharpType` for Morphir SDK type references
- [ ] Map `Morphir.SDK.Basics.Int` → `int`
- [ ] Map `Morphir.SDK.Maybe.Maybe` → `Option`
- [ ] Map `Morphir.SDK.Result.Result` → `Result`
- [ ] Map `Morphir.SDK.List.List` → `List`
- [ ] Map all other SDK types per specification

**FQName Resolution**:
- [ ] Implement `fqNameToFSharpType` function
- [ ] Handle package path → namespace conversion
- [ ] Handle module path → module nesting
- [ ] Handle local name → type name
- [ ] Capitalize type names correctly

**Testing**:
- [ ] Unit tests for each Type.Definition variant
- [ ] Unit tests for each Type constructor
- [ ] Snapshot tests comparing generated F# to hand-written examples
- [ ] Integration tests with morphir-elm example types
- [ ] Test compilation of generated types (`dotnet build`)

**Code Quality**:
- [ ] All generated types compile without errors
- [ ] All generated types compile without warnings
- [ ] Fantomas formatting applied successfully
- [ ] Code coverage ≥ 80%

### Example Test Case

```fsharp
[<Test>]
let ``Map Morphir Result type to F# Result`` () =
    // Given: Morphir IR for type Result e v = Err e | Ok v
    let morphirType = CustomTypeDefinition((), Map.ofList [
        (Name.fromString "Err", ((), [Type.Variable((), Name.fromString "e")]))
        (Name.fromString "Ok", ((), [Type.Variable((), Name.fromString "v")]))
    ])

    // When: Map to Fabulous.AST
    let fsharpDecl = Mapper.mapTypeDefinition (Name.fromString "Result") morphirType

    // Then: Render and validate
    let code = Gen.mkOak fsharpDecl |> Gen.run

    code |> should contain "type Result"
    code |> should contain "| Err of 'e"
    code |> should contain "| Ok of 'v"
```

### Migration Assessment Tasks (CRITICAL)

**Before Implementation**:
- [ ] Review morphir-elm `src/Morphir/Scala/Feature/Core.elm` → `mapType` function
- [ ] Study how morphir-elm Scala backend handles all Type constructors
- [ ] Review morphir-elm `mapFQNameToTypeRef` for FQName resolution patterns
- [ ] Compare Scala sealed traits to F# discriminated unions
- [ ] Identify F# improvements over Elm/Scala approach

**Traceability**:
```fsharp
// ADAPTED FROM: morphir-elm src/Morphir/Scala/Feature/Core.elm mapType
// KEY DIFFERENCE: Uses Fabulous.AST TypeRef widgets instead of Scala AST
// IMPROVEMENT: F# type inference reduces need for explicit type annotations
```

**Decision Log Entry**:
```markdown
### Type Mapping Strategy (Phase 2)

**morphir-elm**: Maps Morphir types to Scala sealed traits and case classes

**Decision**: ADAPT - Use Fabulous.AST type widgets for F# DUs and records

**Rationale**:
- F# discriminated unions more concise than Scala sealed traits
- Fabulous.AST handles type syntax automatically
- F# type inference reduces annotation burden

**Differences from morphir-elm**:
- No custom AST types (use Fabulous.AST)
- Simpler syntax for discriminated unions
- No need for case class companions
```

### Tasks

1. **Migration Assessment: Type System**
   - [ ] Study morphir-elm type mapping for all Type constructors
   - [ ] Document differences between Scala and F# type systems
   - [ ] Identify F#-specific type features to leverage (units of measure, etc.)

2. Create `Mapper.fs` with type mapping functions
3. Implement `mapTypeDefinition` for all Type.Definition variants
4. Implement `mapTypeExpr` for all Type constructors
5. Implement `fqNameToFSharpType` with SDK integration
6. Write comprehensive unit tests (TDD: Red-Green-Refactor)
7. Create snapshot tests with morphir-elm examples
8. Validate all generated types compile

### Success Criteria

- All Type.Definition variants mapped correctly
- All Type constructors mapped correctly
- SDK types map to F# built-ins
- All tests pass
- Generated types compile successfully
- Code coverage ≥ 80%

### Dependencies

- Issue #2 (Foundation) must be complete

---

## Issue #4: Value Mapping: Morphir IR Values → F# Functions

**Type**: Feature
**Labels**: `feature`, `backend`, `fsharp`, `phase-3`
**Priority**: P0
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 2 weeks
**Assignee**: TBD
**Depends On**: #3

### Description

Implement complete mapping from Morphir IR value definitions and value expressions to Fabulous.AST F# function and expression constructs.

### Acceptance Criteria

**Value Definition Mapping**:
- [ ] `mapValueDefinition`: Convert Value.Definition to F# functions
- [ ] Extract parameters from nested lambda expressions
- [ ] Generate curried functions (multi-parameter)
- [ ] Add type annotations to function signatures

**Value Expression Mapping** (`mapValueExpr`):
- [ ] Literal → `ConstantExpr`
- [ ] Variable → `IdentExpr`
- [ ] Reference → `IdentExpr` with FQName resolution
- [ ] Lambda → `LambdaExpr`
- [ ] Apply → `AppExpr` (function application)
- [ ] IfThenElse → `IfThenElseExpr`
- [ ] PatternMatch → `MatchExpr` with match clauses
- [ ] LetDefinition → `LetExpr`
- [ ] LetRecursion → `LetRecExpr` (recursive functions)
- [ ] Destructure → `LetExpr` with pattern binding
- [ ] Record → `RecordExpr`
- [ ] Field → `DotGetExpr`
- [ ] FieldFunction → Lambda extracting field
- [ ] UpdateRecord → `RecordUpdateExpr`
- [ ] List → `ListExpr`
- [ ] Tuple → `TupleExpr`
- [ ] Constructor → `IdentExpr`
- [ ] Unit → `UnitExpr`

**Pattern Mapping** (`mapPattern`):
- [ ] WildcardPattern → `WildPattern`
- [ ] AsPattern → `AsPattern`
- [ ] TuplePattern → `TuplePattern`
- [ ] ConstructorPattern → `NamedPattern` with args
- [ ] EmptyListPattern → `ListPattern`
- [ ] HeadTailPattern → `ConsPattern`
- [ ] LiteralPattern → `ConstantPattern`
- [ ] UnitPattern → `UnitPattern`

**Literal Mapping** (`mapLiteral`):
- [ ] BoolLiteral → `Constant.Bool`
- [ ] CharLiteral → `Constant.Char`
- [ ] StringLiteral → `Constant.String`
- [ ] IntLiteral → `Constant.Int32`
- [ ] FloatLiteral → `Constant.Double`
- [ ] DecimalLiteral → `Constant.Decimal`

**Testing**:
- [ ] Unit tests for each Value expression variant
- [ ] Unit tests for each Pattern variant
- [ ] Snapshot tests for generated functions
- [ ] Test curried function generation
- [ ] Test pattern matching exhaustiveness
- [ ] Test nested let bindings
- [ ] Test recursive functions
- [ ] Integration tests with morphir-elm examples
- [ ] Test compilation (`dotnet build`)

### Example Test Case

```fsharp
[<Test>]
let ``Map curried function with pattern matching`` () =
    // Given: Morphir IR for:
    // isPositive : Int -> Bool
    // isPositive n =
    //     case n of
    //         0 -> False
    //         _ -> n > 0

    let morphirValue = Value.Lambda(
        (),
        Pattern.Variable("n"),
        Value.PatternMatch(
            (),
            Value.Variable((), Name.fromString "n"),
            [
                (Pattern.Literal(LiteralInt 0), Value.Literal(LiteralBool false))
                (Pattern.Wildcard, Value.Apply(...)) // n > 0
            ]
        )
    )

    // When: Map to F#
    let fsharpFunc = Mapper.mapValueDefinition (Name.fromString "isPositive") {
        ValueType = Type.Function(Type.Reference("Int"), Type.Reference("Bool"))
        Body = morphirValue
    }

    // Then: Validate generated code
    let code = Gen.mkOak fsharpFunc |> Gen.run

    code |> should contain "let isPositive"
    code |> should contain "match n with"
    code |> should contain "| 0 -> false"
```

### Migration Assessment Tasks (CRITICAL)

**Before Implementation**:
- [ ] Review morphir-elm `src/Morphir/Scala/Feature/Core.elm` → `mapValue` function
- [ ] Study all 16 Value expression variants in morphir-elm
- [ ] Review morphir-elm `mapPattern` for pattern matching translation
- [ ] Compare Scala and F# pattern matching syntax
- [ ] Study morphir-elm curried function generation strategy
- [ ] Identify lambda vs. explicit parameter differences

**Traceability**:
```fsharp
// ADAPTED FROM: morphir-elm src/Morphir/Scala/Feature/Core.elm mapValue
// KEY DIFFERENCE: F# lambda syntax simpler than Scala
// IMPROVEMENT: F# pattern matching more concise, exhaustiveness checking better
```

**Decision Log Entry**:
```markdown
### Value Mapping Strategy (Phase 3)

**morphir-elm**: Maps Morphir values to Scala methods and expressions

**Decision**: ADAPT - Use Fabulous.AST expression widgets for F# functions

**Rationale**:
- F# lambda syntax: `fun x -> ...` vs Scala: `(x: Type) => ...`
- F# pattern matching more concise than Scala match expressions
- F# curried functions native, no need for Scala currying transformation

**Differences from morphir-elm**:
- Simpler lambda generation (no type annotations needed)
- Native currying (no manual transformation)
- Exhaustiveness checking built into F# compiler
```

### Tasks

1. **Migration Assessment: Value Expressions**
   - [ ] Study morphir-elm value mapping for all 16 Value constructors
   - [ ] Study morphir-elm pattern mapping for all 8 Pattern variants
   - [ ] Document differences between Scala and F# expression syntax
   - [ ] Identify F# features that simplify value generation

2. Implement `mapValueDefinition` with parameter extraction
3. Implement `mapValueExpr` for all Value constructors (16 variants)
4. Implement `mapPattern` for all Pattern variants (8 variants)
5. Implement `mapLiteral` for all Literal variants (6 variants)
6. Write comprehensive unit tests (TDD)
7. Create snapshot tests with morphir-elm examples
8. Validate all generated functions compile and type-check

### Success Criteria

- All Value expression variants mapped correctly
- All Pattern variants mapped correctly
- Pattern matching is exhaustive
- Curried functions generated correctly
- All tests pass
- Generated code compiles successfully
- Code coverage ≥ 80%

### Dependencies

- Issue #3 (Type Mapping) must be complete

---

## Issue #5: CLI Integration: `morphir gen fsharp` Command

**Type**: Feature
**Labels**: `feature`, `backend`, `fsharp`, `cli`, `phase-4`
**Priority**: P0
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 1 week
**Assignee**: TBD
**Depends On**: #4

### Description

Integrate the F# backend into the morphir CLI as a `morphir gen fsharp` command with full option support and pipeline integration.

### Acceptance Criteria

**CLI Command Implementation**:
- [ ] Create `Commands.Gen.FSharp.fs` in `Morphir.CLI`
- [ ] Register `gen fsharp` subcommand
- [ ] Implement option parsing:
  - [ ] `--input <file>` (default: morphir-ir.json)
  - [ ] `--output <dir>` (default: ./generated)
  - [ ] `--namespace <ns>` (default: Generated)
  - [ ] `--codecs` flag
  - [ ] `--lenses` flag
  - [ ] `--limit-to-modules <list>`
  - [ ] `--help`

**Pipeline Plugin** (`Plugin.fs`):
- [ ] Implement `fsharpBackendPlugin` function
- [ ] Accept `FSharpBackendOptions` configuration
- [ ] Process `Distribution<unit, unit>` from IR
- [ ] Generate Fabulous.AST Oak nodes via `Mapper.mapDistribution`
- [ ] Render to F# code via `Generator.renderFileMap`
- [ ] Return output files in pipeline format

**Generator Module** (`Generator.fs`):
- [ ] Implement `renderToFSharp`: Oak → formatted F# string
- [ ] Implement `formatCode`: Apply Fantomas formatting
- [ ] Implement `renderFileMap`: Process all generated files
- [ ] Handle formatting errors gracefully

**File Writing**:
- [ ] Create output directory structure
- [ ] Write all `.fs` files to disk
- [ ] Preserve module hierarchy (Morphir/Reference/Model.fs)
- [ ] Generate README.md with generation metadata

**Logging**:
- [ ] Progress logging to stderr (not stdout!)
- [ ] Log file count and output directory
- [ ] Log errors with file/line context
- [ ] Verbose mode for debugging

**Testing**:
- [ ] E2E test: CLI command with default options
- [ ] E2E test: CLI command with custom namespace
- [ ] E2E test: CLI command with codecs flag
- [ ] E2E test: CLI command with lenses flag
- [ ] E2E test: CLI command with limit-to-modules
- [ ] Test file writing and directory creation
- [ ] Test error handling for invalid IR

### Example Usage

```bash
# Basic generation
morphir gen fsharp

# Custom namespace and output
morphir gen fsharp --namespace MyApp.Domain --output src/Generated

# With codecs and lenses
morphir gen fsharp --codecs --lenses

# Limit to specific modules
morphir gen fsharp --limit-to-modules "Morphir.Reference.Model,Morphir.Reference.Logic"

# Help
morphir gen fsharp --help
```

### Tasks

1. Create `Plugin.fs` with `fsharpBackendPlugin` implementation
2. Create `Generator.fs` with rendering functions
3. Create `Commands.Gen.FSharp.fs` in Morphir.CLI
4. Implement command-line option parsing
5. Implement file writing with directory creation
6. Add progress logging (stderr only)
7. Write E2E tests for all options
8. Update CLI documentation

### Success Criteria

- `morphir gen fsharp` command works end-to-end
- All command-line options functional
- Generated files written correctly to disk
- Logging goes to stderr (stdout clean for piping)
- All E2E tests pass
- Help text is clear and accurate

### Dependencies

- Issue #4 (Value Mapping) must be complete

---

## Issue #6: SDK Translation: Morphir SDK → F# Standard Library

**Type**: Feature
**Labels**: `feature`, `backend`, `fsharp`, `sdk`, `phase-5`
**Priority**: P0
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 1 week
**Assignee**: TBD
**Depends On**: #5

### Description

Translate Morphir SDK function calls and operators to their F# standard library equivalents for seamless integration with F# code.

### Acceptance Criteria

**Function Call Translation**:
- [ ] Extend `SDK.sdkFunctionMap` with all Morphir SDK functions
- [ ] Implement function call translation in `mapValueExpr`
- [ ] Translate List functions:
  - [ ] `List.map` → `List.map`
  - [ ] `List.filter` → `List.filter`
  - [ ] `List.foldl` → `List.fold`
  - [ ] `List.foldr` → `List.foldBack`
  - [ ] `List.head` → `List.head`
  - [ ] `List.tail` → `List.tail`
  - [ ] `List.length` → `List.length`
  - [ ] `List.isEmpty` → `List.isEmpty`
- [ ] Translate Maybe/Option functions:
  - [ ] `Maybe.map` → `Option.map`
  - [ ] `Maybe.withDefault` → `Option.defaultValue`
  - [ ] `Maybe.andThen` → `Option.bind`
- [ ] Translate Result functions:
  - [ ] `Result.map` → `Result.map`
  - [ ] `Result.mapError` → `Result.mapError`
  - [ ] `Result.andThen` → `Result.bind`
  - [ ] `Result.withDefault` → `Result.defaultValue`
- [ ] Translate String functions:
  - [ ] `String.isEmpty` → `String.IsNullOrEmpty`
  - [ ] `String.length` → `String.length`
  - [ ] `String.concat` → `String.concat`
  - [ ] `String.toUpper` → `(fun s -> s.ToUpper())`
  - [ ] `String.toLower` → `(fun s -> s.ToLower())`

**Operator Translation**:
- [ ] Arithmetic: `+`, `-`, `*`, `/`, `%` → F# equivalents
- [ ] Comparison: `==`, `/=`, `<`, `>`, `<=`, `>=` → F# equivalents
- [ ] Logical: `&&`, `||`, `not` → F# equivalents
- [ ] List cons: `::` → `::`
- [ ] String concat: `++` → `+`
- [ ] Composition: `>>`, `<<` → `>>`, `<<`
- [ ] Pipeline: `|>`, `<|` → `|>`, `<|`

**Testing**:
- [ ] Unit tests for each SDK function translation
- [ ] Unit tests for each operator translation
- [ ] Integration tests with morphir-elm SDK examples
- [ ] Test compilation of generated code using SDK functions
- [ ] Snapshot tests for complex SDK function compositions

### Example Test Case

```fsharp
[<Test>]
let ``Translate List.map to F# List.map`` () =
    // Given: Morphir IR for List.map double [1, 2, 3]
    let morphirExpr = Value.Apply(
        Value.Apply(
            Value.Reference(fqName "Morphir.SDK.List" "map"),
            Value.Variable("double")
        ),
        Value.List([Value.Literal(Int 1); Value.Literal(Int 2); Value.Literal(Int 3)])
    )

    // When: Map to F#
    let fsharpExpr = Mapper.mapValueExpr morphirExpr
    let code = Gen.mkOak fsharpExpr |> Gen.run

    // Then: Should use F# List.map
    code |> should contain "List.map double [1; 2; 3]"
```

### Tasks

1. Extend `SDK.sdkFunctionMap` with all SDK functions
2. Update `mapValueExpr` to use `SDK.tryGetFSharpFunction`
3. Implement operator translation in `mapValueExpr`
4. Write unit tests for all SDK function translations
5. Write unit tests for all operator translations
6. Test with morphir-elm SDK examples
7. Update SDK.fs documentation

### Success Criteria

- All SDK functions translate correctly
- All operators translate correctly
- Generated code uses F# standard library
- All tests pass
- Code compiles and runs correctly
- Code coverage ≥ 80%

### Dependencies

- Issue #5 (CLI Integration) must be complete

---

## Issue #7: Advanced Features: JSON Codecs and Lenses

**Type**: Feature
**Labels**: `feature`, `backend`, `fsharp`, `codecs`, `lenses`, `phase-6`
**Priority**: P1
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 2 weeks
**Assignee**: TBD
**Depends On**: #6

### Description

Implement advanced code generation features: JSON encoders/decoders (using Thoth.Json) and lens functions for nested record updates.

### Acceptance Criteria

**JSON Codec Generation** (`--codecs`):
- [ ] Generate Thoth.Json encoders for all types
- [ ] Generate Thoth.Json decoders for all types
- [ ] Handle recursive types correctly
- [ ] Handle polymorphic types (generics)
- [ ] Handle discriminated unions with tagged encoding
- [ ] Handle records with field encoding
- [ ] Handle Option types (nullable fields)
- [ ] Handle List/Array types
- [ ] Place codecs in separate `Codecs` module per file

**Lens Generation** (`--lenses`):
- [ ] Generate lens functions for all record types
- [ ] Generate lens type: `type Lens<'S, 'A> = { Get: 'S -> 'A; Set: 'A -> 'S -> 'S }`
- [ ] Generate field lenses for each record field
- [ ] Generate lens composition operator (`>>>`)
- [ ] Place lenses in separate `Lenses` module per file

**Testing**:
- [ ] Test codec round-trip: encode >> decode = identity
- [ ] Test codec handles recursive types (e.g., tree structures)
- [ ] Test codec handles polymorphic types
- [ ] Test codec handles discriminated unions
- [ ] Test lens get/set laws
- [ ] Test lens composition
- [ ] Test generated codecs with actual JSON data
- [ ] Performance benchmark for codec generation

**Documentation**:
- [ ] Document codec usage in generated README
- [ ] Document lens usage in generated README
- [ ] Add examples of codec/lens usage

### Example Generated Code

**Codecs**:
```fsharp
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

**Lenses**:
```fsharp
module Lenses =
    type Lens<'S, 'A> = {
        Get: 'S -> 'A
        Set: 'A -> 'S -> 'S
    }

    let (>>>) (outer: Lens<'A, 'B>) (inner: Lens<'B, 'C>) : Lens<'A, 'C> =
        {
            Get = fun a -> inner.Get (outer.Get a)
            Set = fun c a -> outer.Set (inner.Set c (outer.Get a)) a
        }

    let personNameLens = {
        Get = fun p -> p.name
        Set = fun n p -> { p with name = n }
    }
```

### Tasks

1. **Codecs**:
   - Implement `generateCodecs` function in Mapper.fs
   - Generate encoder for each type definition
   - Generate decoder for each type definition
   - Handle recursive types with `Decode.andThen`
   - Write codec round-trip tests

2. **Lenses**:
   - Implement `generateLenses` function in Mapper.fs
   - Generate `Lens<'S, 'A>` type definition
   - Generate lens composition operator
   - Generate field lenses for each record
   - Write lens law tests

3. **Integration**:
   - Wire `--codecs` flag into CLI command
   - Wire `--lenses` flag into CLI command
   - Add Thoth.Json dependency (optional)
   - Update documentation

### Success Criteria

- Codecs handle all type variants correctly
- Codec round-trip tests pass for all types
- Lenses satisfy get-put, put-get, put-put laws
- Lens composition works correctly
- All tests pass
- Code coverage ≥ 80%

### Dependencies

- Issue #6 (SDK Translation) must be complete

---

## Issue #8: Testing and Documentation

**Type**: Documentation + Testing
**Labels**: `testing`, `documentation`, `phase-7`
**Priority**: P0
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 1 week
**Assignee**: TBD
**Depends On**: #7

### Description

Create comprehensive test suite and user-facing documentation for the F# backend, including E2E tests with morphir-elm examples and complete user guides.

### Acceptance Criteria

**E2E Testing**:
- [ ] E2E test with morphir-elm LCR example
- [ ] E2E test with morphir-elm reference model
- [ ] E2E test with custom Morphir project
- [ ] Full pipeline test: morphir-ir.json → F# code → dotnet build → tests pass
- [ ] Performance benchmark: measure generation time for 1000 types
- [ ] Memory profiling: ensure < 500 MB for large IRs

**Integration Testing**:
- [ ] Load real morphir-elm example IRs
- [ ] Generate F# code
- [ ] Compile generated code
- [ ] Run generated code tests
- [ ] Verify semantic equivalence

**Snapshot Testing**:
- [ ] Snapshot tests for all example projects
- [ ] Verify stability across regeneration
- [ ] Compare against hand-written F# equivalents

**User Documentation**:
- [ ] **Getting Started Guide**: How to use `morphir gen fsharp`
- [ ] **Command Reference**: All CLI options documented
- [ ] **Type Mapping Reference**: Morphir types → F# types
- [ ] **SDK Mapping Reference**: Morphir SDK → F# stdlib
- [ ] **Advanced Features Guide**: Codecs and lenses
- [ ] **Troubleshooting Guide**: Common issues and solutions
- [ ] **Examples**: 3+ working example projects

**API Documentation**:
- [ ] XML doc comments on all public functions
- [ ] Architecture overview documentation
- [ ] Extension guide for contributors

**Example Projects**:
- [ ] Example 1: Simple domain model (Person, Order)
- [ ] Example 2: Business logic with validation
- [ ] Example 3: Complex nested types with codecs
- [ ] All examples include:
  - [ ] morphir-ir.json
  - [ ] Generated F# code
  - [ ] .fsproj file
  - [ ] Unit tests
  - [ ] README with instructions

### Tasks

1. **E2E Tests**:
   - Download morphir-elm examples
   - Create test fixtures
   - Write E2E test suite
   - Add performance benchmarks

2. **Documentation**:
   - Write getting started guide
   - Write command reference
   - Write type mapping guide
   - Write SDK mapping guide
   - Write advanced features guide
   - Write troubleshooting guide

3. **Examples**:
   - Create 3 example projects
   - Ensure all examples build and run
   - Add comprehensive READMEs

4. **Coverage Analysis**:
   - Run coverage report
   - Ensure ≥ 80% coverage
   - Add tests for uncovered code

### Success Criteria

- All E2E tests pass
- All morphir-elm examples generate valid F# code
- Performance < 5s for 1000 types
- Memory < 500 MB for large IRs
- Documentation is comprehensive and clear
- All examples build and run successfully
- Code coverage ≥ 80%

### Dependencies

- Issue #7 (Advanced Features) must be complete

---

## Issue #9: Release Preparation

**Type**: Release
**Labels**: `release`, `phase-8`
**Priority**: P0
**Milestone**: v1.0.0
**Epic**: #1
**Estimated Effort**: 1 week
**Assignee**: TBD
**Depends On**: #8

### Description

Final polish, code review, CI/CD integration, and preparation for v1.0.0 release of the F# backend.

### Acceptance Criteria

**Code Quality**:
- [ ] Code review completed by 2+ reviewers
- [ ] All review comments addressed
- [ ] No critical or high-severity issues
- [ ] Refactoring based on review feedback
- [ ] Code follows F# style guide (Fantomas validated)

**CI/CD Integration**:
- [ ] F# backend tests added to CI pipeline
- [ ] Build validation (untrimmed, trimmed, AOT)
- [ ] Test execution on all platforms (Windows, Linux, macOS)
- [ ] Coverage reporting integrated
- [ ] Performance regression tests

**AOT Compatibility**:
- [ ] Generated code tested with Native AOT
- [ ] No reflection warnings (IL2026, IL3050)
- [ ] Trimming compatibility validated
- [ ] AOT Guru skill review completed

**Documentation**:
- [ ] CHANGELOG updated with all changes
- [ ] Release notes drafted
- [ ] Migration guide (if applicable)
- [ ] Known limitations documented

**Release Assets**:
- [ ] NuGet package for Morphir.Backends.FSharp
- [ ] Updated morphir CLI tool (dotnet tool)
- [ ] Example projects packaged
- [ ] Documentation site updated

**Announcement**:
- [ ] Blog post drafted
- [ ] FINOS Slack announcement prepared
- [ ] GitHub release notes finalized
- [ ] Twitter/social media posts drafted

### Tasks

1. **Code Review**:
   - Request reviews from maintainers
   - Address all comments
   - Refactor as needed

2. **CI/CD**:
   - Add F# backend to build pipeline
   - Add test execution
   - Add coverage reporting
   - Test on all platforms

3. **AOT Testing**:
   - Build with PublishAot=true
   - Run AOT Guru skill review
   - Fix any compatibility issues

4. **Documentation**:
   - Update CHANGELOG
   - Write release notes
   - Finalize documentation

5. **Release**:
   - Create GitHub release
   - Publish NuGet packages
   - Announce release

### Success Criteria

- All code review comments addressed
- CI/CD pipeline passes on all platforms
- AOT compatibility validated
- Documentation complete
- v1.0.0 released successfully
- Announcement published

### Dependencies

- Issue #8 (Testing and Documentation) must be complete

---

## Summary

| Issue | Title | Priority | Effort | Phase | Dependencies |
|-------|-------|----------|--------|-------|--------------|
| #1 | [EPIC] F# Code Generation Backend | P0 | 13 weeks | - | - |
| #TBD | Phase 0: Morphir.SDK F# Library Implementation | P0 | 1-2 weeks | 0 | - |
| #2 | Foundation: Project Setup and Fabulous.AST Exploration | P0 | 2 weeks | 1 | Phase 0 |
| #3 | Type Mapping: Morphir IR Types → F# Types | P0 | 2 weeks | 2 | #2 |
| #4 | Value Mapping: Morphir IR Values → F# Functions | P0 | 2 weeks | 3 | #3 |
| #5 | CLI Integration: `morphir gen fsharp` Command | P0 | 1 week | 4 | #4 |
| #6 | SDK Translation: Morphir SDK → F# Standard Library | P0 | 1 week | 5 | #5 |
| #7 | Advanced Features: JSON Codecs and Lenses | P1 | 2 weeks | 6 | #6 |
| #8 | Testing and Documentation | P0 | 1 week | 7 | #7 |
| #9 | Release Preparation | P0 | 1 week | 8 | #8 |

**Total Timeline**: 13 weeks (1 week Phase 0 + 12 weeks implementation)
**Total Issues**: 10 (1 Epic + 9 Implementation - including Phase 0)

---

## Next Steps

1. Review and approve this issue breakdown
2. Create issues in GitHub (use this document as template)
3. Assign issues to team members
4. Begin Phase 1 implementation (Issue #2)
5. Track progress in GitHub Projects board

---

**Document Status**: Ready for Review
**Created**: 2025-12-31
**Updated**: 2025-12-31
