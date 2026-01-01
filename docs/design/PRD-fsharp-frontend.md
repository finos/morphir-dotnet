# Product Requirements Document: F# Frontend

**Project**: morphir-dotnet F# Code Parsing Frontend
**Version**: 1.0.0
**Status**: Design Complete, Ready for Implementation
**Created**: 2025-12-31
**Epic**: TBD (to be created)

---

## Executive Summary

The F# Frontend enables morphir-dotnet to parse F# source code and generate Morphir IR (JSON format). This completes the **round-trip capability**: F# ↔ Morphir IR ↔ F#, allowing developers to:

- **Author domain models in F#** instead of Elm
- **Generate Morphir IR** from existing F# codebases
- **Enable F#-first workflows** for Morphir modeling
- **Validate behavioral equivalence** through round-trip testing

**Strategic Position**: While the F# Backend (#363) generates F# FROM Morphir IR, this frontend does the reverse: parses F# TO Morphir IR, creating a complete F#-native Morphir authoring experience.

---

## Table of Contents

1. [Background](#background)
2. [Goals & Non-Goals](#goals--non-goals)
3. [User Personas](#user-personas)
4. [Architecture Overview](#architecture-overview)
5. [F# Language Support Strategy](#f-language-support-strategy)
6. [Technical Design](#technical-design)
7. [Testing Strategy](#testing-strategy)
8. [Dependencies](#dependencies)
9. [Implementation Plan](#implementation-plan)
10. [Success Metrics](#success-metrics)
11. [Risks & Mitigations](#risks--mitigations)
12. [Open Questions](#open-questions)
13. [Appendices](#appendices)

---

## Background

### Current State

morphir-dotnet today can:
- ✅ Represent Morphir IR in F# (Classic IR) and C# (Modern IR)
- ✅ **Generate F# code** from Morphir IR (F# Backend #363-#372)
- ✅ Serialize/deserialize Morphir IR JSON
- ✅ Transform and validate IR through pipelines
- ❌ **Parse F# code** to generate Morphir IR (this PRD)

### The Gap

Users must author Morphir models in **Elm** using [morphir-elm](https://github.com/finos/morphir-elm), then consume the IR in .NET. This creates friction for F#-first teams who want to:
- Use F# as the modeling language
- Leverage F# tooling (IDE, LSP, analyzers)
- Integrate with existing F# codebases
- Avoid learning Elm for Morphir modeling

### The Opportunity

By implementing an F# frontend, we enable:
1. **F#-native authoring**: Write Morphir models directly in F#
2. **Round-trip verification**: F# → IR → F# → IR (validates backend correctness)
3. **Existing codebase migration**: Parse legacy F# domain models to Morphir
4. **Tooling parity**: Use Ionide, F# LSP, and analyzers for live feedback

---

## Goals & Non-Goals

### Goals

**Primary (P0):**
- ✅ Parse a **meaningful subset** of F# to valid Morphir IR JSON (v3)
- ✅ Support core functional constructs (records, DUs, functions, pattern matching)
- ✅ Integrate with **FSharp.Compiler.Service** for accurate parsing and type checking
- ✅ Provide **Ionide analyzer** for real-time compilation errors
- ✅ Enable **round-trip testing**: F# → IR → F# → IR (using F# Backend)
- ✅ Achieve ≥80% test coverage
- ✅ Deliver CLI command: `morphir parse fsharp --input src/ --output morphir-ir.json`

**Secondary (P1):**
- ✅ Support advanced F# features (generics, higher-order functions, recursive types)
- ✅ Import resolution (open statements, qualified names)
- ✅ Multi-file project parsing
- ✅ Performance: < 5s for 1000 LOC projects
- ✅ Comprehensive error messages (show F# source location + Morphir limitation)

**Tertiary (P2):**
- ⚠️ F# signature file (`.fsi`) support
- ⚠️ Incremental parsing (only reparse changed files)
- ⚠️ Watch mode for live IR regeneration
- ⚠️ LSP integration for "preview IR" in editor

### Non-Goals

**Will NOT Support:**
- ❌ **Mutable state**: `let mutable`, `ref`, `byref`, imperative loops
- ❌ **OOP constructs**: Classes, interfaces, inheritance, properties
- ❌ **Computation expressions**: `async {}`, `seq {}`, custom CEs
- ❌ **Active patterns**: Too F#-specific, no Morphir equivalent
- ❌ **Type providers**: Dynamic types incompatible with Morphir
- ❌ **Quotations/Reflection**: Runtime metaprogramming
- ❌ **Units of measure**: No Morphir equivalent
- ❌ **Full F# stdlib**: Only Morphir SDK-compatible functions

**Rationale**: Morphir IR is a **purely functional**, **statically typed**, **portable** representation. Features like mutation, OOP, and CEs don't translate to Morphir's semantics.

---

## User Personas

### Persona 1: F# Domain Modeler

**Name**: Alex (Senior F# Developer)
**Background**: 8 years F# experience, building financial domain models
**Goal**: Use F# for Morphir modeling instead of learning Elm
**Pain Point**: "I already know F#. Why do I need to learn Elm to use Morphir?"

**User Story**:
> As an F# developer, I want to write my domain model in F# so that I can leverage my existing expertise and tooling.

**Acceptance Criteria**:
- Can define Morphir types using F# discriminated unions and records
- Can write business logic using F# functions
- Can run `morphir parse fsharp` to generate Morphir IR
- Gets compilation errors in Ionide if using unsupported F# features

---

### Persona 2: Migration Engineer

**Name**: Jordan (DevOps + F# Developer)
**Background**: Migrating legacy F# codebase to Morphir-based architecture
**Goal**: Parse existing F# domain models to Morphir IR automatically
**Pain Point**: "Manually rewriting 10K lines of F# to Elm is not feasible."

**User Story**:
> As a migration engineer, I want to parse my existing F# codebase to Morphir IR so that I can incrementally adopt Morphir without a full rewrite.

**Acceptance Criteria**:
- Can point `morphir parse fsharp` at existing F# project
- Gets detailed report of what parsed successfully vs. unsupported features
- Can iteratively refactor to remove unsupported features
- Round-trip tests validate behavioral equivalence

---

### Persona 3: Morphir Contributor

**Name**: Casey (OSS Contributor)
**Background**: Contributing to morphir-dotnet
**Goal**: Validate F# Backend correctness through round-trip testing
**Pain Point**: "How do I know the F# Backend generates correct code?"

**User Story**:
> As a Morphir contributor, I want to test F# Backend output by round-tripping it through the F# Frontend so that I can verify code generation correctness.

**Acceptance Criteria**:
- Can run round-trip tests: IR → F# (backend) → IR (frontend)
- Compares original IR with regenerated IR (should be identical)
- Catches backend bugs (incorrect F# generation)
- Runs as part of CI/CD pipeline

---

## Architecture Overview

### High-Level Data Flow

```
F# Source Code (.fs files)
    ↓
┌───────────────────────────┐
│ FSharp.Compiler.Service   │ ← Microsoft's official F# compiler API
│ - Parsing                 │
│ - Type checking           │
│ - Symbol resolution       │
└───────────────────────────┘
    ↓
┌───────────────────────────┐
│ F# AST (Typed)            │ ← SynModuleDecl, SynType, SynExpr, etc.
│ - ParsedInput             │
│ - FSharpCheckFileResults  │
│ - FSharpSymbol info       │
└───────────────────────────┘
    ↓
┌───────────────────────────┐
│ IR Mapper                 │ ← Map F# AST → Morphir IR
│ - TypeMapper.fs           │
│ - ValueMapper.fs          │
│ - ModuleMapper.fs         │
└───────────────────────────┘
    ↓
┌───────────────────────────┐
│ Morphir IR (F# Model)     │ ← Morphir.Models/IR/Classic/
│ - Distribution            │
│ - Package → Modules       │
│ - Types, Values           │
└───────────────────────────┘
    ↓
┌───────────────────────────┐
│ JSON Serialization        │ ← Use existing codecs
│ - Morphir.Models.Json     │
└───────────────────────────┘
    ↓
Morphir IR JSON (v3)
```

### Core Components

| Component | Purpose | Implementation | Dependencies |
|-----------|---------|----------------|--------------|
| **Parser** | Parse F# to AST | FSharp.Compiler.Service | FCS 43.9.0+ |
| **Type Checker** | Resolve types, symbols | FSharp.Compiler.Service | - |
| **TypeMapper** | Map F# types → Morphir types | `Morphir.Frontends.FSharp/TypeMapper.fs` | Morphir.Models |
| **ValueMapper** | Map F# values → Morphir values | `Morphir.Frontends.FSharp/ValueMapper.fs` | Morphir.Models |
| **ModuleMapper** | Map F# modules → Morphir modules | `Morphir.Frontends.FSharp/ModuleMapper.fs` | - |
| **IR Generator** | Assemble full IR | `Morphir.Frontends.FSharp/Generator.fs` | - |
| **Analyzer** | Ionide diagnostics | `Morphir.Frontends.FSharp.Analyzer/` | Ionide.Analyzers.SDK |
| **CLI** | `morphir parse fsharp` | `Morphir.Tool/Commands/ParseFSharp.cs` | Spectre.Console |

---

## F# Language Support Strategy

### Incremental Support Approach

We will **NOT** support all F# features. Morphir IR is a strict subset focused on pure functional domain modeling.

#### Phase 0: MVP Feature Set (P0)

**Core Types:**
| F# Feature | Morphir Equivalent | Example |
|------------|-------------------|---------|
| Record types | Record types | `type Person = { Name: string; Age: int }` |
| Discriminated unions | Custom types | `type Maybe<'a> = None \| Some of 'a` |
| Type aliases | Type aliases | `type UserId = string` |
| Primitive types | Basic types | `int`, `string`, `bool`, `float` |
| Tuples | Tuple types | `int * string` |
| Function types | Function types | `int -> string` |
| List types | List types | `int list` |
| Option types | Maybe type (Morphir SDK) | `int option` → `Maybe Int` |
| Result types | Result type (Morphir SDK) | `Result<int, string>` → `Result String Int` |

**Core Values:**
| F# Feature | Morphir Equivalent | Example |
|------------|-------------------|---------|
| Let bindings (functions) | Value definitions | `let add x y = x + y` |
| Let bindings (constants) | Value definitions | `let pi = 3.14159` |
| Lambda expressions | Lambda | `fun x -> x + 1` |
| Pattern matching | Pattern matching | `match x with \| Some v -> v \| None -> 0` |
| If-then-else | If-then-else | `if x > 0 then "pos" else "neg"` |
| Function application | Apply | `add 1 2` |
| Operators | Binary operators | `x + y`, `x * y` |
| List literals | List constructor | `[1; 2; 3]` |
| Record literals | Record constructor | `{ Name = "Alice"; Age = 30 }` |

**Modules:**
| F# Feature | Morphir Equivalent | Example |
|------------|-------------------|---------|
| Module declarations | Modules | `module User` |
| Nested modules | Nested modules | `module User.Validation` |
| Open statements | Imports (qualified) | `open System` → `import Morphir.SDK.Basics` |

#### Phase 1: Extended Features (P1)

| F# Feature | Morphir Equivalent | Complexity | Priority |
|------------|-------------------|------------|----------|
| Generic types | Type parameters | Medium | P1 |
| Higher-order functions | First-class functions | Low | P1 |
| Recursive types | Recursive type aliases | Medium | P1 |
| Mutual recursion | Mutually recursive values | High | P1 |
| Custom operators | Binary/unary operators | Low | P1 |
| Pipeline operator `\|>` | Function application | Low | P1 |
| Composition `>>` | Function composition | Low | P1 |
| Partial application | Currying | Low | P0 (actually) |

#### Unsupported Features (Never)

| F# Feature | Why Unsupported | Error Message Strategy |
|------------|----------------|------------------------|
| Mutable bindings | Morphir is pure | "Mutable bindings not supported. Use immutable let bindings." |
| Classes/Interfaces | Morphir has no OOP | "Object-oriented features not supported. Use records and discriminated unions." |
| Computation expressions | No Morphir equivalent | "Computation expressions not supported. Use explicit monadic bindings." |
| Active patterns | F#-specific | "Active patterns not supported. Use standard pattern matching." |
| Type providers | Dynamic types | "Type providers not supported. Use static type definitions." |
| Units of measure | No Morphir type | "Units of measure not supported. Use plain numeric types." |
| `ref` cells | Mutable | "Ref cells not supported. Use immutable data structures." |
| Arrays | Mutable | "Arrays not supported. Use lists or tuples." |
| For/while loops | Imperative | "Loops not supported. Use recursive functions or List module functions." |

### Error Message Examples

**Good Error Messages** (user-friendly + actionable):

```
Error: Unsupported Feature - Mutable Binding
Location: User.fs:42:5
Code:
  42 | let mutable count = 0
     |     ^^^^^^^

Morphir only supports immutable functional programming.

Suggestion:
  - Remove 'mutable' keyword
  - Use immutable data structures
  - Pass updated values through function parameters

Learn more: https://morphir.finos.org/docs/fsharp-frontend/limitations
```

```
Error: Unsupported Feature - Computation Expression
Location: Service.fs:18:9
Code:
  18 | async {
     | ^^^^^

Computation expressions have no equivalent in Morphir IR.

Suggestion:
  - If using 'async', model effects explicitly in your types
  - If using 'option', use explicit Option.map/Option.bind
  - If using 'result', use explicit Result.map/Result.bind

Learn more: https://morphir.finos.org/docs/fsharp-frontend/limitations#computation-expressions
```

---

## Technical Design

### Component Details

#### 1. Parser (FSharp.Compiler.Service Integration)

**File**: `src/Morphir.Frontends.FSharp/Parser.fs`

**Responsibilities**:
- Parse F# source files using FSharp.Compiler.Service
- Perform type checking
- Extract symbol information
- Provide typed AST to mappers

**Key APIs**:
```fsharp
module Parser =
    open FSharp.Compiler.CodeAnalysis
    open FSharp.Compiler.Text

    type ParseResult = {
        ParsedInput: ParsedInput
        CheckResults: FSharpCheckFileResults
        Diagnostics: FSharpDiagnostic[]
    }

    type SourceInput =
        /// Parse from file on disk
        | FilePath of path: string
        /// Parse from in-memory source (for try-morphir, REPL scenarios)
        | InMemory of fileName: string * source: string
        /// Parse multiple files without .fsproj (CLI scenario)
        | MultipleFiles of files: (string * string) list  // (fileName, source) pairs

    /// Parse and type-check a single F# source (file or in-memory)
    let parseSource (input: SourceInput) : Result<ParseResult, string> =
        // 1. Create FSharpChecker
        let checker = FSharpChecker.Create()

        // 2. Get source and fileName
        let (fileName, sourceText) =
            match input with
            | FilePath path ->
                let source = System.IO.File.ReadAllText(path)
                (path, SourceText.ofString source)
            | InMemory (name, source) ->
                (name, SourceText.ofString source)
            | MultipleFiles _ -> failwith "Use parseMultipleFiles for multiple sources"

        // 3. Parse file
        let parseOptions = { ... }
        let parseResults = checker.ParseFile(fileName, sourceText, parseOptions) |> Async.RunSynchronously

        // 4. Type check
        let checkOptions = { ... }
        let checkResults = checker.CheckFileInProject(parseResults, fileName, ...) |> Async.RunSynchronously

        // 5. Return typed AST + diagnostics
        match checkResults with
        | FSharpCheckFileAnswer.Succeeded results ->
            Ok {
                ParsedInput = parseResults.ParseTree.Value
                CheckResults = results
                Diagnostics = results.Diagnostics
            }
        | FSharpCheckFileAnswer.Aborted ->
            Error "Type checking aborted"

    /// Parse multiple F# sources without .fsproj (preserves order for dependency resolution)
    let parseMultipleFiles (files: (string * string) list) : Result<ParseResult list, string> =
        // 1. Create single FSharpChecker for all files
        let checker = FSharpChecker.Create()

        // 2. Build project options for multi-file checking
        let projectOptions = createProjectOptions files

        // 3. Parse and check all files together (required for cross-file type resolution)
        let checkResults = checker.ParseAndCheckProject(projectOptions) |> Async.RunSynchronously

        // 4. Extract individual file results
        files
        |> List.mapi (fun i (fileName, _) ->
            checkResults.AssemblyContents.ImplementationFiles.[i])
        |> Ok

    /// Parse entire F# project from .fsproj file
    let parseProject (projectFile: string) : Result<ParseResult list, string> =
        // 1. Parse .fsproj XML
        // 2. Extract <Compile> items (source files) in order
        // 3. Read all source files
        // 4. Use parseMultipleFiles for type checking
        ...

    /// In-memory API for try-morphir and testing scenarios
    let parseInMemory (fileName: string) (source: string) : Result<ParseResult, string> =
        parseSource (InMemory (fileName, source))
```

**Trade-offs**:
| Approach | Pros | Cons |
|----------|------|------|
| **FSharp.Compiler.Service** | ✅ Accurate, mature, includes type checking | ❌ Not AOT-compatible, ~50MB dependency |
| Custom parser (FParsec) | ✅ AOT-compatible, lightweight | ❌ Huge effort, won't match F# compiler |
| Roslyn-style | ✅ Source generators | ❌ F# doesn't have Roslyn equivalent |

**Decision**: Use FSharp.Compiler.Service. AOT compatibility can be addressed later with a separate "runtime-only" distribution.

---

#### 2. TypeMapper

**File**: `src/Morphir.Frontends.FSharp/TypeMapper.fs`

**Responsibilities**:
- Map F# `SynType` AST nodes to Morphir `Type<unit>` IR
- Resolve type references using `FSharpCheckFileResults`
- Handle generics, tuples, functions, lists, options, results
- Map F# primitives to Morphir basic types

**Key Functions**:
```fsharp
module TypeMapper =
    open FSharp.Compiler.Syntax
    open Morphir.Models.IR.Classic

    /// Map F# SynType to Morphir Type
    let rec mapType (checkResults: FSharpCheckFileResults) (synType: SynType) : Type<unit> =
        match synType with
        // Primitives
        | SynType.LongIdent (LongIdentWithDots([Ident "int"], _)) ->
            Type.basicType (Path.fromString "Morphir.SDK.Basics") "Int"

        | SynType.LongIdent (LongIdentWithDots([Ident "string"], _)) ->
            Type.basicType (Path.fromString "Morphir.SDK.String") "String"

        | SynType.LongIdent (LongIdentWithDots([Ident "bool"], _)) ->
            Type.basicType (Path.fromString "Morphir.SDK.Basics") "Bool"

        | SynType.LongIdent (LongIdentWithDots([Ident "float"], _)) ->
            Type.basicType (Path.fromString "Morphir.SDK.Basics") "Float"

        // List
        | SynType.App (SynType.LongIdent (LongIdentWithDots([Ident "list"], _)), _, [elemType], _, _, _, _) ->
            let morphirElem = mapType checkResults elemType
            Type.Reference((), toFQName ["Morphir"; "SDK"; "List"] "List", [morphirElem])

        // Option
        | SynType.App (SynType.LongIdent (LongIdentWithDots([Ident "option"], _)), _, [elemType], _, _, _, _) ->
            let morphirElem = mapType checkResults elemType
            Type.Reference((), toFQName ["Morphir"; "SDK"; "Maybe"] "Maybe", [morphirElem])

        // Result
        | SynType.App (SynType.LongIdent (LongIdentWithDots([Ident "Result"], _)), _, [okType; errType], _, _, _, _) ->
            let morphirOk = mapType checkResults okType
            let morphirErr = mapType checkResults errType
            Type.Reference((), toFQName ["Morphir"; "SDK"; "Result"] "Result", [morphirErr; morphirOk])

        // Tuple
        | SynType.Tuple (_, types, _) ->
            let morphirTypes = types |> List.map (fun (_, t) -> mapType checkResults t)
            Type.Tuple((), morphirTypes)

        // Function
        | SynType.Fun (argType, returnType, _, _) ->
            let morphirArg = mapType checkResults argType
            let morphirRet = mapType checkResults returnType
            Type.Function((), morphirArg, morphirRet)

        // Generic type parameter
        | SynType.Var (SynTypar (Ident name, TyparStaticReq.None, _), _) ->
            Type.Variable((), Name.fromString name)

        // User-defined type (use symbol resolution)
        | SynType.LongIdent (LongIdentWithDots(idents, _)) ->
            let symbol = resolveTypeSymbol checkResults synType
            let fqName = extractFQName symbol
            Type.Reference((), fqName, [])

        | _ ->
            failwithf "Unsupported F# type: %A" synType
```

**Type Mapping Table**:

| F# Type | Morphir IR Type | Notes |
|---------|----------------|-------|
| `int` | `Morphir.SDK.Basics:Int` | 32-bit signed integer |
| `string` | `Morphir.SDK.String:String` | UTF-16 string |
| `bool` | `Morphir.SDK.Basics:Bool` | Boolean |
| `float` | `Morphir.SDK.Basics:Float` | 64-bit floating point |
| `decimal` | `Morphir.SDK.Decimal:Decimal` | High-precision decimal |
| `'a list` | `Morphir.SDK.List:List<'a>` | Immutable list |
| `'a option` | `Morphir.SDK.Maybe:Maybe<'a>` | Optional value |
| `Result<'ok, 'err>` | `Morphir.SDK.Result:Result<'err, 'ok>` | **Note**: Error first in Morphir |
| `'a * 'b` | `Tuple<'a, 'b>` | 2-tuple |
| `'a * 'b * 'c` | `Tuple<'a, Tuple<'b, 'c>>` | Nested tuples |
| `'a -> 'b` | `Function<'a, 'b>` | Function type |
| `'a -> 'b -> 'c` | `Function<'a, Function<'b, 'c>>` | Curried function |
| User-defined record | Custom record type | Mapped by structure |
| User-defined DU | Custom type | Mapped by structure |

---

#### 3. ValueMapper

**File**: `src/Morphir.Frontends.FSharp/ValueMapper.fs`

**Responsibilities**:
- Map F# `SynExpr` (expressions) to Morphir `Value` IR
- Map F# `SynPat` (patterns) to Morphir `Pattern` IR
- Map F# function definitions to Morphir value definitions
- Handle let bindings, lambdas, pattern matching, literals

**Key Functions**:
```fsharp
module ValueMapper =
    open FSharp.Compiler.Syntax
    open Morphir.Models.IR.Classic

    /// Map F# SynExpr to Morphir Value
    let rec mapExpr (checkResults: FSharpCheckFileResults) (expr: SynExpr) : Value<unit, unit> =
        match expr with
        // Literals
        | SynExpr.Const (SynConst.Int32 n, _) ->
            Value.Literal((), Literal.IntLiteral (int64 n))

        | SynExpr.Const (SynConst.String (s, _, _), _) ->
            Value.Literal((), Literal.StringLiteral s)

        | SynExpr.Const (SynConst.Bool b, _) ->
            Value.Literal((), Literal.BoolLiteral b)

        // Variable reference
        | SynExpr.Ident (Ident name) ->
            Value.Variable((), Name.fromString name)

        // Lambda
        | SynExpr.Lambda (_, _, SynSimplePats.SimplePats ([SynSimplePat.Id (Ident paramName, _, _, _, _, _)], _), body, _, _, _) ->
            let morphirParam = Pattern.AsPattern((), Pattern.WildcardPattern(()), Name.fromString paramName)
            let morphirBody = mapExpr checkResults body
            Value.Lambda((), morphirParam, morphirBody)

        // Function application
        | SynExpr.App (_, _, func, arg, _) ->
            let morphirFunc = mapExpr checkResults func
            let morphirArg = mapExpr checkResults arg
            Value.Apply((), morphirFunc, morphirArg)

        // If-then-else
        | SynExpr.IfThenElse (cond, thenExpr, Some elseExpr, _, _, _, _) ->
            let morphirCond = mapExpr checkResults cond
            let morphirThen = mapExpr checkResults thenExpr
            let morphirElse = mapExpr checkResults elseExpr
            Value.IfThenElse((), morphirCond, morphirThen, morphirElse)

        // Pattern matching
        | SynExpr.Match (_, matchExpr, clauses, _, _, _) ->
            let morphirExpr = mapExpr checkResults matchExpr
            let morphirClauses =
                clauses
                |> List.map (fun (SynMatchClause (pat, whenExpr, resultExpr, _, _, _)) ->
                    let morphirPat = mapPattern checkResults pat
                    let morphirResult = mapExpr checkResults resultExpr
                    (morphirPat, morphirResult)
                )
            Value.PatternMatch((), morphirExpr, morphirClauses)

        // Record literal
        | SynExpr.Record (_, _, fields, _) ->
            let morphirFields =
                fields
                |> List.map (fun (SynExprRecordField ((LongIdentWithDots ([Ident fieldName], _), _), _, Some expr, _)) ->
                    (Name.fromString fieldName, mapExpr checkResults expr)
                )
            Value.Record((), morphirFields)

        // List literal
        | SynExpr.ArrayOrList (_, exprs, _) ->
            let morphirExprs = exprs |> List.map (mapExpr checkResults)
            Value.List((), morphirExprs)

        // Tuple
        | SynExpr.Tuple (_, exprs, _, _) ->
            let morphirExprs = exprs |> List.map (mapExpr checkResults)
            Value.Tuple((), morphirExprs)

        // Binary operator
        | SynExpr.App (_, _, SynExpr.App (_, _, SynExpr.Ident (Ident op), left, _), right, _) when isOperator op ->
            let morphirLeft = mapExpr checkResults left
            let morphirRight = mapExpr checkResults right
            let morphirOp = mapOperator op
            Value.Apply((), Value.Apply((), morphirOp, morphirLeft), morphirRight)

        | _ ->
            failwithf "Unsupported F# expression: %A" expr

    /// Map F# SynPat to Morphir Pattern
    let rec mapPattern (checkResults: FSharpCheckFileResults) (pat: SynPat) : Pattern<unit> =
        match pat with
        | SynPat.Wild _ ->
            Pattern.WildcardPattern(())

        | SynPat.Named (SynIdent (Ident name, _), _, _, _) ->
            Pattern.AsPattern((), Pattern.WildcardPattern(()), Name.fromString name)

        | SynPat.Const (SynConst.Int32 n, _) ->
            Pattern.LiteralPattern((), Literal.IntLiteral (int64 n))

        | SynPat.Const (SynConst.String (s, _, _), _) ->
            Pattern.LiteralPattern((), Literal.StringLiteral s)

        | SynPat.LongIdent (LongIdentWithDots (idents, _), _, _, SynArgPats.Pats pats, _, _) ->
            let ctorName = idents |> List.map (fun (Ident id) -> id) |> String.concat "."
            let morphirPats = pats |> List.map (mapPattern checkResults)
            Pattern.ConstructorPattern((), toFQName [] ctorName, morphirPats)

        | SynPat.Tuple (_, pats, _) ->
            Pattern.TuplePattern((), pats |> List.map (mapPattern checkResults))

        | _ ->
            failwithf "Unsupported F# pattern: %A" pat
```

---

#### 4. ModuleMapper

**File**: `src/Morphir.Frontends.FSharp/ModuleMapper.fs`

**Responsibilities**:
- Map F# module declarations to Morphir modules
- Collect type definitions
- Collect value definitions
- Handle nested modules
- Generate Morphir Distribution structure

**Key Functions**:
```fsharp
module ModuleMapper =
    open FSharp.Compiler.Syntax
    open Morphir.Models.IR.Classic

    /// Map F# module to Morphir Module
    let mapModule (checkResults: FSharpCheckFileResults) (moduleDecl: SynModuleDecl list) : Module.Definition<unit> =
        let types =
            moduleDecl
            |> List.choose (function
                | SynModuleDecl.Types (typeDefns, _) -> Some typeDefns
                | _ -> None)
            |> List.concat
            |> List.map (TypeMapper.mapTypeDefn checkResults)
            |> Map.ofList

        let values =
            moduleDecl
            |> List.choose (function
                | SynModuleDecl.Let (_, bindings, _) -> Some bindings
                | _ -> None)
            |> List.concat
            |> List.map (ValueMapper.mapBinding checkResults)
            |> Map.ofList

        Module.Definition.create types values

    /// Generate full Morphir Distribution
    let generateIR (projectName: string) (modules: Map<Path, Module.Definition<unit>>) : Distribution =
        let packageDef = Package.Definition.create modules
        let packageName = Path.fromString projectName
        Distribution.Library (packageName, Map.empty, packageDef)
```

---

#### 5. High-Level API (For try-morphir and Library Consumers)

**File**: `src/Morphir.Frontends.FSharp/FrontendAPI.fs`

**Responsibilities**:
- Provide simple, high-level API for library consumers
- Support in-memory parsing (try-morphir, REPL, testing)
- Support file-based parsing (CLI, batch processing)
- Support multiple files without .fsproj
- Hide FCS complexity from consumers

**Key APIs**:
```fsharp
module FrontendAPI =
    open Morphir.Models.IR.Classic

    type ParseOptions = {
        /// Source files or in-memory sources
        Sources: SourceInput
        /// Target namespace (if not specified, infer from code)
        TargetNamespace: string option
        /// Include dependencies (Morphir.SDK, etc.)
        IncludeDependencies: bool
    }

    /// High-level API: Parse F# to Morphir IR Distribution
    let parseToIR (options: ParseOptions) : Result<Distribution, string list> =
        // 1. Parse source(s) using Parser
        let parseResults =
            match options.Sources with
            | FilePath path -> Parser.parseSource (FilePath path)
            | InMemory (name, src) -> Parser.parseInMemory name src
            | MultipleFiles files -> Parser.parseMultipleFiles files

        match parseResults with
        | Error err -> Error [err]
        | Ok results ->
            // 2. Map to IR using TypeMapper, ValueMapper, ModuleMapper
            let modules =
                results
                |> List.map (fun result ->
                    let moduleAST = extractModule result.ParsedInput
                    let moduleDef = ModuleMapper.mapModule result.CheckResults moduleAST
                    (Path.fromString moduleAST.Name, moduleDef))
                |> Map.ofList

            // 3. Generate Distribution
            let packageName =
                options.TargetNamespace
                |> Option.defaultValue "GeneratedPackage"
                |> Path.fromString

            let distribution = ModuleMapper.generateIR packageName modules
            Ok distribution

    /// Convenience: Parse single in-memory source (for try-morphir)
    let parseInMemory (fileName: string) (source: string) : Result<Distribution, string list> =
        parseToIR {
            Sources = InMemory (fileName, source)
            TargetNamespace = None
            IncludeDependencies = true
        }

    /// Convenience: Parse multiple files from disk without .fsproj
    let parseFiles (filePaths: string list) : Result<Distribution, string list> =
        let files =
            filePaths
            |> List.map (fun path -> (path, System.IO.File.ReadAllText path))

        parseToIR {
            Sources = MultipleFiles files
            TargetNamespace = None
            IncludeDependencies = true
        }

    /// Convenience: Parse .fsproj project
    let parseProject (projectPath: string) : Result<Distribution, string list> =
        // 1. Parse .fsproj to extract source files
        match Parser.parseProject projectPath with
        | Error err -> Error [err]
        | Ok parseResults ->
            // 2. Map to IR (same as above)
            ...
```

**Usage Examples**:

```fsharp
// try-morphir: In-memory parsing
let source = """
namespace MyDomain

type Customer = {
    CustomerId: int
    Name: string
}
"""

match FrontendAPI.parseInMemory "Customer.fs" source with
| Ok distribution ->
    // Serialize to JSON
    let json = Distribution.toJson distribution
    printfn "Generated IR: %s" json
| Error errors ->
    errors |> List.iter (printfn "Error: %s")
```

```fsharp
// CLI: Parse multiple files without .fsproj
let files = [
    "src/Types.fs"
    "src/Domain.fs"
    "src/Calculator.fs"
]

match FrontendAPI.parseFiles files with
| Ok distribution ->
    // Save to morphir-ir.json
    let json = Distribution.toJson distribution
    System.IO.File.WriteAllText("morphir-ir.json", json)
| Error errors ->
    errors |> List.iter (printfn "Error: %s")
```

```fsharp
// CLI: Parse .fsproj project
match FrontendAPI.parseProject "MyProject/MyProject.fsproj" with
| Ok distribution ->
    let json = Distribution.toJson distribution
    System.IO.File.WriteAllText("morphir-ir.json", json)
| Error errors ->
    errors |> List.iter (printfn "Error: %s")
```

---

#### 6. CLI Integration

**File**: `src/Morphir.Frontends.FSharp.Cli/Program.fs`

**Commands**:

```bash
# Parse single file
$ morphir fsharp parse Calculator.fs --output morphir-ir.json

# Parse multiple files (without .fsproj)
$ morphir fsharp parse Types.fs Domain.fs Calculator.fs --output morphir-ir.json

# Parse entire directory (finds all .fs files)
$ morphir fsharp parse src/ --output morphir-ir.json

# Parse .fsproj project
$ morphir fsharp parse MyProject.fsproj --output morphir-ir.json

# Output to stdout (for piping)
$ morphir fsharp parse Calculator.fs --json | jq .

# Pretty-print JSON
$ morphir fsharp parse Calculator.fs --json --pretty

# Validate only (no output)
$ morphir fsharp parse Calculator.fs --validate

# Round-trip test
$ morphir fsharp parse Calculator.fs --round-trip

# Watch mode (re-parse on file change)
$ morphir fsharp parse src/ --watch --output morphir-ir.json
```

**Implementation**:
```fsharp
module ParseCommand =
    open Spectre.Console
    open Morphir.Frontends.FSharp

    type ParseArgs = {
        Inputs: string list  // Files, directories, or .fsproj
        Output: string option
        Json: bool
        Pretty: bool
        Validate: bool
        RoundTrip: bool
        Watch: bool
    }

    let execute (args: ParseArgs) : int =
        // 1. Determine input type (file, files, directory, .fsproj)
        let sources =
            args.Inputs
            |> List.collect (fun input ->
                if input.EndsWith(".fsproj") then
                    // Parse project file
                    [ProjectFile input]
                elif System.IO.Directory.Exists(input) then
                    // Find all .fs files in directory
                    System.IO.Directory.GetFiles(input, "*.fs", SearchOption.AllDirectories)
                    |> Array.toList
                    |> List.map FilePath
                else
                    // Single file
                    [FilePath input])

        // 2. Parse to IR
        let result =
            match sources with
            | [ProjectFile proj] -> FrontendAPI.parseProject proj
            | files ->
                let filePaths = files |> List.choose (function FilePath p -> Some p | _ -> None)
                FrontendAPI.parseFiles filePaths

        // 3. Handle result
        match result with
        | Error errors ->
            errors |> List.iter (fun err -> AnsiConsole.MarkupLine($"[red]Error:[/] {err}"))
            1  // Exit code
        | Ok distribution ->
            // 4. Output
            if args.Validate then
                AnsiConsole.MarkupLine("[green]✓[/] Valid F# source")
                0
            elif args.RoundTrip then
                // Round-trip test: F# → IR → F# → IR
                performRoundTripTest distribution
            elif args.Json then
                // Output to stdout
                let json = if args.Pretty then
                              Distribution.toJsonPretty distribution
                           else
                              Distribution.toJson distribution
                Console.WriteLine(json)
                0
            else
                // Output to file
                let outputPath = args.Output |> Option.defaultValue "morphir-ir.json"
                let json = Distribution.toJsonPretty distribution
                System.IO.File.WriteAllText(outputPath, json)
                AnsiConsole.MarkupLine($"[green]✓[/] Generated {outputPath}")
                0
```

---

#### 7. Ionide Analyzer

**File**: `src/Morphir.Frontends.FSharp.Analyzer/MorphirAnalyzer.fs`

**Responsibilities**:
- Provide real-time diagnostics in Ionide
- Detect unsupported F# features
- Report errors at source location
- Suggest fixes

**Implementation**:
```fsharp
module MorphirAnalyzer

open Ionide.Analyzers.SDK
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

[<CliAnalyzer "MorphirUnsupportedFeatures">]
let morphirAnalyzer : Analyzer =
    fun (context: SDKContext) ->
        async {
            let! checkResults = context.TypedTree.Async()

            let diagnostics = ResizeArray<Diagnostic>()

            // Check for mutable bindings
            checkResults.GetAllUsesOfAllSymbolsInFile()
            |> Seq.choose (fun symbolUse ->
                match symbolUse.Symbol with
                | :? FSharpMemberOrFunctionOrValue as mfv when mfv.IsMutable ->
                    Some {
                        Type = Warning
                        Message = "Mutable bindings are not supported by Morphir. Use immutable let bindings."
                        Code = "MORPHIR001"
                        Severity = DiagnosticSeverity.Warning
                        Range = symbolUse.Range
                        Fixes = []
                    }
                | _ -> None
            )
            |> diagnostics.AddRange

            // Check for classes
            context.TypedTree.GetAllSymbols()
            |> Seq.choose (fun symbol ->
                match symbol with
                | :? FSharpEntity as entity when entity.IsClass ->
                    Some {
                        Type = Error
                        Message = "Classes are not supported by Morphir. Use records and discriminated unions."
                        Code = "MORPHIR002"
                        Severity = DiagnosticSeverity.Error
                        Range = entity.DeclarationLocation
                        Fixes = []
                    }
                | _ -> None
            )
            |> diagnostics.AddRange

            // Check for computation expressions
            // (detect builder.Bind, builder.Return patterns)
            // ...

            return diagnostics |> Seq.toList
        }
```

**Ionide Integration**:
- Analyzer runs on file save or keystroke pause
- Errors appear as squiggly underlines in editor
- Hover shows detailed error message
- Quick fixes suggest alternatives

---

## Testing Strategy

### Testing Pyramid

```
                  ┌─────────────────┐
                  │   E2E Tests     │  ← Full round-trip: F# → IR → F# → IR
                  │   (Slow, 5)     │
                  └─────────────────┘
                 ┌───────────────────┐
                 │ Integration Tests │  ← Multi-file projects, imports
                 │   (Medium, 20)    │
                 └───────────────────┘
               ┌─────────────────────────┐
               │   Snapshot Tests        │  ← Golden file tests (IR JSON)
               │   (Fast, 50)            │
               └─────────────────────────┘
            ┌──────────────────────────────┐
            │   Property-Based Tests       │  ← Invariants (roundtrip laws)
            │   (Fast, 30)                 │
            └──────────────────────────────┘
      ┌────────────────────────────────────────┐
      │        Unit Tests                      │  ← TypeMapper, ValueMapper, etc.
      │        (Fast, 200+)                    │
      └────────────────────────────────────────┘
```

### Test Types

#### 1. Unit Tests (Expecto)

Test individual mapper functions in isolation.

**File**: `tests/Morphir.Frontends.FSharp.Tests/TypeMapperTests.fs`

```fsharp
module TypeMapperTests

open Expecto
open Morphir.Frontends.FSharp
open Morphir.Models.IR.Classic

[<Tests>]
let typeMapperTests =
    testList "TypeMapper" [
        testCase "Maps F# int to Morphir Int" <| fun () ->
            // Arrange
            let fsharpType = parseFSharpType "int"

            // Act
            let morphirType = TypeMapper.mapType checkResults fsharpType

            // Assert
            let expected = Type.basicType (Path.fromString "Morphir.SDK.Basics") "Int"
            Expect.equal morphirType expected "Should map int to Morphir Int"

        testCase "Maps F# string to Morphir String" <| fun () ->
            let fsharpType = parseFSharpType "string"
            let morphirType = TypeMapper.mapType checkResults fsharpType
            let expected = Type.basicType (Path.fromString "Morphir.SDK.String") "String"
            Expect.equal morphirType expected "Should map string to Morphir String"

        testCase "Maps F# list to Morphir List" <| fun () ->
            let fsharpType = parseFSharpType "int list"
            let morphirType = TypeMapper.mapType checkResults fsharpType
            // Expect: Reference to Morphir.SDK.List.List with type param Int
            ...

        testCase "Maps F# option to Morphir Maybe" <| fun () ->
            let fsharpType = parseFSharpType "int option"
            let morphirType = TypeMapper.mapType checkResults fsharpType
            // Expect: Reference to Morphir.SDK.Maybe.Maybe with type param Int
            ...

        testCase "Maps F# Result to Morphir Result (with swapped params)" <| fun () ->
            let fsharpType = parseFSharpType "Result<int, string>"
            let morphirType = TypeMapper.mapType checkResults fsharpType
            // Expect: Reference to Morphir.SDK.Result.Result with params [String, Int]
            // NOTE: Error first in Morphir!
            ...
    ]
```

#### 2. Property-Based Tests (FsCheck)

Test invariants using generated inputs.

**File**: `tests/Morphir.Frontends.FSharp.Tests/PropertyTests.fs`

```fsharp
module PropertyTests

open Expecto
open FsCheck
open Morphir.Frontends.FSharp
open Morphir.Backends.FSharp  // For round-trip testing

type SimpleType =
    | IntType
    | StringType
    | BoolType
    | ListType of SimpleType
    | OptionType of SimpleType

let rec genSimpleType() =
    Gen.oneof [
        Gen.constant IntType
        Gen.constant StringType
        Gen.constant BoolType
        Gen.map ListType (genSimpleType())
        Gen.map OptionType (genSimpleType())
    ]

[<Tests>]
let propertyTests =
    testList "Property Tests" [
        testProperty "Parsing is deterministic" <| fun (source: string) ->
            // If parse succeeds, parsing twice should give same result
            match Parser.parseFile "test.fs" source with
            | Ok result1 ->
                match Parser.parseFile "test.fs" source with
                | Ok result2 ->
                    result1 = result2
                | Error _ -> false
            | Error _ -> true  // Errors are allowed, just be consistent

        testProperty "Type mapping preserves structure" <| fun (typ: SimpleType) ->
            // Map to Morphir and back should preserve structure
            let fsharpCode = generateFSharpType typ
            let fsharpType = parseFSharpType fsharpCode
            let morphirType = TypeMapper.mapType checkResults fsharpType
            let regeneratedFSharp = FSharpBackend.generateType morphirType
            let morphirType2 = TypeMapper.mapType checkResults (parseFSharpType regeneratedFSharp)

            morphirType = morphirType2

        testProperty "Valid F# parses successfully" <| fun (NonEmptyString name) (validExpr: SimpleExpr) ->
            let source = $"let {name} = {generateExpr validExpr}"
            match Parser.parseFile "test.fs" source with
            | Ok _ -> true
            | Error _ -> false  // Should succeed for valid inputs
    ]
```

#### 3. Snapshot Tests (Verify)

Compare generated IR JSON against golden files.

**File**: `tests/Morphir.Frontends.FSharp.Tests/SnapshotTests.fs`

```fsharp
module SnapshotTests

open Expecto
open VerifyExpecto
open Morphir.Frontends.FSharp

[<Tests>]
let snapshotTests =
    testList "Snapshot Tests" [
        testTask "Person record snapshot" {
            // Arrange
            let source = """
                type Person = {
                    Name: string
                    Age: int
                    Email: string option
                }
            """

            // Act
            let! result = Parser.parseFile "Person.fs" source
            let morphirIR = Generator.generateIR "MyApp" result
            let json = IR.toJson morphirIR

            // Assert (compare with snapshots/Person.verified.json)
            do! Verifier.Verify(json)
        }

        testTask "Maybe type snapshot" {
            let source = """
                type Maybe<'a> =
                    | Nothing
                    | Just of 'a
            """
            let! result = Parser.parseFile "Maybe.fs" source
            let morphirIR = Generator.generateIR "MyApp" result
            let json = IR.toJson morphirIR
            do! Verifier.Verify(json)
        }

        testTask "Function definition snapshot" {
            let source = """
                let add x y = x + y
            """
            let! result = Parser.parseFile "Math.fs" source
            let morphirIR = Generator.generateIR "MyApp" result
            let json = IR.toJson morphirIR
            do! Verifier.Verify(json)
        }
    ]
```

**Snapshot Directory Structure**:
```
tests/Morphir.Frontends.FSharp.Tests/
├── snapshots/
│   ├── Person.verified.json        ← Golden file
│   ├── Maybe.verified.json
│   ├── Math.verified.json
│   └── ...
└── SnapshotTests.fs
```

#### 4. Integration Tests

Test multi-file projects with imports and dependencies.

**File**: `tests/Morphir.Frontends.FSharp.Tests/IntegrationTests.fs`

```fsharp
module IntegrationTests

open Expecto
open Morphir.Frontends.FSharp

[<Tests>]
let integrationTests =
    testList "Integration Tests" [
        testCase "Parse multi-file project" <| fun () ->
            // Arrange
            let projectDir = "testdata/MultiFileProject/"
            // Contains: User.fs, Validation.fs, App.fs

            // Act
            let result = Parser.parseProject (projectDir + "Project.fsproj")

            // Assert
            match result with
            | Ok modules ->
                Expect.equal (Map.count modules) 3 "Should have 3 modules"
                Expect.isTrue (Map.containsKey (Path.fromString "User") modules) "Should have User module"
                Expect.isTrue (Map.containsKey (Path.fromString "Validation") modules) "Should have Validation module"
                Expect.isTrue (Map.containsKey (Path.fromString "App") modules) "Should have App module"
            | Error err ->
                failtest $"Parse failed: {err}"

        testCase "Resolve open statements" <| fun () ->
            // Arrange
            let source = """
                open System

                let toUpper (s: string) = s.ToUpper()
            """

            // Act
            let result = Parser.parseFile "Utils.fs" source

            // Assert
            match result with
            | Ok parsed ->
                // Should resolve System.String.ToUpper
                let morphirIR = Generator.generateIR "Utils" parsed
                // Verify ToUpper is mapped to Morphir.SDK.String function
                ...
            | Error err ->
                failtest $"Parse failed: {err}"
    ]
```

#### 5. Round-Trip E2E Tests

**File**: `tests/Morphir.Frontends.FSharp.Tests/RoundTripTests.fs`

```fsharp
module RoundTripTests

open Expecto
open Morphir.Frontends.FSharp
open Morphir.Backends.FSharp  // F# Backend from #363

[<Tests>]
let roundTripTests =
    testList "Round-Trip Tests" [
        testCase "F# -> IR -> F# -> IR (identical)" <| fun () ->
            // Arrange
            let originalFSharp = """
                type Person = {
                    Name: string
                    Age: int
                }

                let createPerson name age = {
                    Name = name
                    Age = age
                }
            """

            // Act
            // Step 1: Parse F# to IR
            let ir1 = Parser.parseFile "Person.fs" originalFSharp
                      |> Result.map (Generator.generateIR "MyApp")
                      |> Result.get

            // Step 2: Generate F# from IR (using backend)
            let generatedFSharp = FSharpBackend.generate ir1

            // Step 3: Parse generated F# to IR
            let ir2 = Parser.parseFile "Person.fs" generatedFSharp
                      |> Result.map (Generator.generateIR "MyApp")
                      |> Result.get

            // Step 4: Compare IR1 and IR2 (should be identical)
            let json1 = IR.toJson ir1
            let json2 = IR.toJson ir2

            // Assert
            Expect.equal json1 json2 "Round-trip should produce identical IR"

        testCase "morphir-elm compatibility" <| fun () ->
            // Arrange
            let elmGeneratedIR = loadFromFile "testdata/elm-generated-ir.json"

            // Act
            // Step 1: Parse elm IR
            let parsedIR = IR.fromJson elmGeneratedIR |> Result.get

            // Step 2: Generate F# from IR
            let fsharpCode = FSharpBackend.generate parsedIR

            // Step 3: Parse F# back to IR
            let regeneratedIR = Parser.parseFile "Generated.fs" fsharpCode
                                |> Result.map (Generator.generateIR "ElmApp")
                                |> Result.get

            // Step 4: Compare
            let json1 = elmGeneratedIR
            let json2 = IR.toJson regeneratedIR

            // Assert
            Expect.equal json1 json2 "Should match morphir-elm generated IR"
    ]
```

### Test Coverage Goals

| Component | Coverage Target | Rationale |
|-----------|----------------|-----------|
| **TypeMapper** | ≥90% | Critical path, must handle all supported types |
| **ValueMapper** | ≥90% | Critical path, must handle all expressions |
| **ModuleMapper** | ≥85% | Important but simpler than mappers |
| **Parser** | ≥80% | FCS handles most logic, we test integration |
| **Analyzer** | ≥75% | Nice-to-have, not critical path |
| **CLI** | ≥80% | User-facing, must work reliably |
| **Overall** | ≥80% | Acceptable for MVP, aim for 85%+ in Phase 2 |

---

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `FSharp.Compiler.Service` | 43.9.0+ | Parse and type-check F# code |
| `Ionide.Analyzers.SDK` | 0.9.0+ | Real-time diagnostics in Ionide |
| `Morphir.Models` | 0.4.0+ | Morphir IR types (F# Classic) |
| `Morphir.IR.Pipeline` | 0.4.0+ | IR transformations and validation |
| `Morphir.Backends.FSharp` | 0.4.0+ | For round-trip testing |
| `Spectre.Console` | 0.49.0+ | CLI UI (prompts, progress bars) |
| `Expecto` | 10.3.0+ | F# testing framework |
| `FsCheck` | 3.0.0-rc3+ | Property-based testing |
| `Verify.Expecto` | 28.5.1+ | Snapshot testing |

### Project Dependencies

```
Morphir.Frontends.FSharp
├── FSharp.Compiler.Service
├── Morphir.Models
├── Morphir.IR.Pipeline
└── Morphir.SDK (for type mappings)

Morphir.Frontends.FSharp.Analyzer
├── Ionide.Analyzers.SDK
├── FSharp.Compiler.Service
└── Morphir.Frontends.FSharp (for shared logic)

Morphir.Frontends.FSharp.Tests
├── Expecto
├── FsCheck
├── Verify.Expecto
├── Morphir.Frontends.FSharp
└── Morphir.Backends.FSharp (for round-trip tests)

Morphir.Tool (CLI)
├── Morphir.Frontends.FSharp
├── Spectre.Console
└── ... (existing dependencies)
```

### External Dependencies

- **morphir-elm**: For compatibility testing (optional, test fixtures only)
- **Ionide**: Editor integration (optional, for analyzer testing)
- **.NET 10 SDK**: Minimum version for native compilation

---

## Implementation Plan

### Timeline: 12 Weeks (5 Phases)

| Phase | Duration | Focus | Deliverable | Milestone |
|-------|----------|-------|-------------|-----------|
| **Phase 1** | 2 weeks | Foundation + Parser | Project setup, FCS integration | M0: Can parse basic F# |
| **Phase 2** | 3 weeks | Core Mapping | TypeMapper + ValueMapper for MVP features | M1: Can generate IR for simple types |
| **Phase 3** | 3 weeks | Advanced Features | Generics, HOFs, recursive types | M2: Can generate IR for complex models |
| **Phase 4** | 2 weeks | CLI + Integration | `morphir parse fsharp` command, Ionide analyzer | M3: Usable end-to-end |
| **Phase 5** | 2 weeks | Polish + Compatibility | Round-trip tests, morphir-elm compatibility | M4: Production-ready |

### Phase Breakdown

#### Phase 1: Foundation + Parser (2 weeks)

**Goal**: Set up project structure and integrate FSharp.Compiler.Service

**Tasks**:
1. **Project Setup** (2 days)
   - Create `src/Morphir.Frontends.FSharp/` (F# library)
   - Create `tests/Morphir.Frontends.FSharp.Tests/` (Expecto)
   - Add to `Morphir.slnx` solution
   - Configure NuGet dependencies
   - Set up CI/CD (GitHub Actions)

2. **FCS Integration** (3 days)
   - Implement `Parser.fs`
   - Parse single F# file
   - Extract `ParsedInput` and `FSharpCheckFileResults`
   - Handle parse errors
   - Write unit tests

3. **AST Exploration** (2 days)
   - Study `SynModuleDecl`, `SynType`, `SynExpr`, `SynPat` structures
   - Write test cases for different F# constructs
   - Document AST patterns

4. **Symbol Resolution** (3 days)
   - Use `FSharpCheckFileResults` to resolve types
   - Extract `FSharpSymbol` information
   - Map F# symbols to FQNames
   - Write unit tests

**Acceptance Criteria**:
- [ ] Can parse valid F# files using FCS
- [ ] Can extract typed AST
- [ ] Can resolve type symbols
- [ ] Unit tests cover parser logic
- [ ] CI/CD pipeline runs tests

---

#### Phase 2: Core Mapping (3 weeks)

**Goal**: Map MVP F# subset to Morphir IR

**Tasks**:
1. **TypeMapper - Primitives** (3 days)
   - Map `int`, `string`, `bool`, `float`
   - Map `list`, `option`, `Result`
   - Map tuples
   - Map function types
   - Write unit tests + snapshot tests

2. **TypeMapper - User Types** (4 days)
   - Map record types
   - Map discriminated unions
   - Map type aliases
   - Handle generic type parameters
   - Write unit tests + snapshot tests

3. **ValueMapper - Literals** (2 days)
   - Map integer, string, bool, float literals
   - Map list literals
   - Map record literals
   - Write unit tests

4. **ValueMapper - Expressions** (4 days)
   - Map variable references
   - Map lambda expressions
   - Map function application
   - Map if-then-else
   - Map binary operators
   - Write unit tests

5. **ValueMapper - Pattern Matching** (3 days)
   - Map patterns (wildcard, named, literal, constructor, tuple)
   - Map `match` expressions
   - Write unit tests

6. **ModuleMapper** (2 days)
   - Map module declarations
   - Collect type definitions
   - Collect value definitions
   - Generate `Module.Definition`
   - Write unit tests

7. **IR Generator** (2 days)
   - Assemble full `Distribution`
   - Generate FQNames
   - JSON serialization
   - Write snapshot tests

**Acceptance Criteria**:
- [ ] TypeMapper handles all MVP types
- [ ] ValueMapper handles all MVP expressions
- [ ] ModuleMapper generates valid modules
- [ ] Can generate Morphir IR JSON from simple F# files
- [ ] All mappers have ≥90% test coverage
- [ ] Snapshot tests validate IR structure

---

#### Phase 3: Advanced Features (3 weeks)

**Goal**: Support generics, higher-order functions, recursive types, imports

**Tasks**:
1. **Generic Types** (4 days)
   - Handle type parameters in types
   - Handle type parameters in functions
   - Proper substitution and inference
   - Write unit tests + property tests

2. **Higher-Order Functions** (3 days)
   - Functions as arguments
   - Functions as return values
   - Partial application (currying)
   - Write unit tests

3. **Recursive Types** (3 days)
   - Self-referential types (e.g., `type Tree = Leaf | Node of Tree * Tree`)
   - Mutually recursive types
   - Write unit tests

4. **Import Resolution** (4 days)
   - Parse `open` statements
   - Resolve qualified names
   - Map to Morphir imports
   - Write integration tests

5. **Multi-File Projects** (4 days)
   - Parse `.fsproj` files
   - Determine file order
   - Type-check across files
   - Generate multi-module IR
   - Write integration tests

6. **Error Handling** (2 days)
   - Detect unsupported features
   - Generate user-friendly error messages
   - Write error tests

**Acceptance Criteria**:
- [ ] Generics work correctly
- [ ] HOFs generate valid IR
- [ ] Recursive types parse without infinite loops
- [ ] Multi-file projects parse successfully
- [ ] Error messages are clear and actionable
- [ ] Integration tests pass

---

#### Phase 4: CLI + Integration (2 weeks)

**Goal**: Deliver usable CLI command and Ionide analyzer

**Tasks**:
1. **CLI Command** (4 days)
   - Implement `morphir parse fsharp` command
   - Options: `--input`, `--output`, `--project`, `--verbose`
   - Progress reporting (Spectre.Console)
   - Error reporting
   - Write CLI tests

2. **Ionide Analyzer** (5 days)
   - Create `Morphir.Frontends.FSharp.Analyzer` project
   - Detect unsupported features
   - Report diagnostics
   - Test in Ionide
   - Write analyzer tests

3. **Documentation** (3 days)
   - README for frontend usage
   - Supported features list
   - Unsupported features list
   - Error code reference
   - Examples

4. **CI/CD** (2 days)
   - Add frontend tests to CI
   - Publish NuGet packages
   - Validate on Windows/Linux/macOS

**Acceptance Criteria**:
- [ ] `morphir parse fsharp` command works end-to-end
- [ ] Ionide analyzer detects unsupported features
- [ ] Documentation is complete
- [ ] CI/CD publishes packages
- [ ] CLI tests pass

---

#### Phase 5: Polish + Compatibility (2 weeks)

**Goal**: Round-trip testing, morphir-elm compatibility, production readiness

**Tasks**:
1. **Round-Trip Tests** (4 days)
   - Write E2E round-trip tests (F# → IR → F# → IR)
   - Validate IR identity
   - Fix any discrepancies
   - Achieve 100% round-trip success for MVP features

2. **morphir-elm Compatibility** (4 days)
   - Load morphir-elm generated IR test fixtures
   - Parse with frontend, regenerate IR
   - Compare JSON outputs
   - Fix compatibility issues

3. **Performance Optimization** (2 days)
   - Profile parsing performance
   - Optimize hot paths
   - Measure: < 5s for 1000 LOC

4. **Final Polish** (2 days)
   - Code review
   - Documentation review
   - Fix remaining bugs
   - Finalize release notes

5. **Release Preparation** (2 days)
   - Prepare changelog
   - Tag version `0.4.0-alpha`
   - Publish NuGet packages
   - Announce on FINOS Slack

**Acceptance Criteria**:
- [ ] Round-trip tests pass for all MVP features
- [ ] Compatible with morphir-elm IR
- [ ] Performance < 5s for 1000 LOC
- [ ] Code coverage ≥80%
- [ ] v0.4.0-alpha released

---

## Success Metrics

### Must-Have (P0)

- ✅ Parse MVP F# subset to valid Morphir IR v3 JSON
- ✅ Type checking integration working (leverage FCS)
- ✅ Round-trip tests passing (F# → IR → F# → IR identical)
- ✅ ≥80% test coverage overall (≥90% for mappers)
- ✅ Functional CLI command (`morphir parse fsharp`)
- ✅ Ionide analyzer detects unsupported features
- ✅ User documentation complete

### Should-Have (P1)

- ✅ Generic types supported
- ✅ Higher-order functions supported
- ✅ Multi-file project parsing
- ✅ Performance < 5s for 1000 LOC
- ✅ morphir-elm IR compatibility
- ✅ Comprehensive error messages

### Nice-to-Have (P2)

- ⚠️ F# signature file (`.fsi`) support
- ⚠️ Incremental parsing
- ⚠️ Watch mode
- ⚠️ LSP integration ("preview IR" in editor)
- ⚠️ Performance < 1s for 1000 LOC

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **FCS not AOT-compatible** | High | High | Runtime-only distribution for now. Revisit with source generators in Phase 2. |
| **F# type inference complexity** | Medium | High | Leverage FCS fully. Don't reimplement type checking. |
| **Incomplete feature coverage** | High | Medium | Start with MVP subset. Be transparent about limitations. Good error messages. |
| **Round-trip IR not identical** | Medium | High | Extensive testing. May need to accept minor structural differences (comments, etc.). |
| **Performance issues** | Low | Medium | Profile early. FCS is fast. Likely bottleneck is IR generation, which is O(n). |
| **morphir-elm incompatibility** | Medium | High | Use morphir-elm test fixtures. Document known differences. |
| **Ionide analyzer adoption** | Medium | Low | Analyzer is nice-to-have. Core functionality works without it. |

---

## Open Questions

1. **How should we map F# namespaces vs. modules?**
   - **Proposal**: Map F# modules to Morphir modules. Ignore namespaces (they're just for .NET organization).
   - **Decision**: TBD (to be validated during implementation)

2. **Should we support F# signature files (`.fsi`)?**
   - **Proposal**: Phase 2 feature. Not critical for MVP.
   - **Decision**: Defer to Phase 2

3. **How to handle F# SDK type abbreviations (`type UserId = string`)?**
   - **Proposal**: Generate opaque types in Morphir (`type UserId = UserId String`).
   - **Decision**: TBD

4. **Should we validate F# code compiles before parsing?**
   - **Proposal**: No. FCS can parse invalid code. We report errors from FCS + our own validation.
   - **Decision**: TBD

5. **How to handle multi-file projects with circular dependencies?**
   - **Proposal**: Use FCS project-level type checking. It handles this.
   - **Decision**: TBD

6. **Should we support .NET BCL types (System.String, etc.)?**
   - **Proposal**: No. Only Morphir SDK types. Map `string` to `Morphir.SDK.String.String`, not `System.String`.
   - **Decision**: TBD

7. **How strict should morphir-elm compatibility be?**
   - **Proposal**: Best effort. Document known differences. Aim for 95%+ compatibility.
   - **Decision**: TBD

8. **Should we generate Morphir IR with attributes (source locations)?**
   - **Proposal**: Yes, for better error messages. Use `Type<SourceSpan>` variant.
   - **Decision**: TBD

9. **How to handle F#-specific features users might expect (active patterns, etc.)?**
   - **Proposal**: Clear error messages. Point to documentation. Suggest alternatives.
   - **Decision**: TBD

10. **Should we support incremental parsing?**
    - **Proposal**: Phase 2 feature. MVP parses entire project every time.
    - **Decision**: Defer to Phase 2

11. **How to package and distribute the frontend?**
    - **Proposal**: Separate NuGet package `Morphir.Frontends.FSharp`. Runtime-only distribution for AOT concerns.
    - **Decision**: TBD

---

## Appendices

### Appendix A: F# vs Elm Feature Comparison

| Feature | Elm | F# | Morphir Frontend Support |
|---------|-----|----|-----------------------|
| Records | ✅ | ✅ | ✅ MVP |
| Custom types (DUs) | ✅ | ✅ (Discriminated Unions) | ✅ MVP |
| Type aliases | ✅ | ✅ | ✅ MVP |
| Pattern matching | ✅ | ✅ | ✅ MVP |
| Maybe/Option | ✅ Maybe | ✅ option | ✅ MVP (map to Morphir Maybe) |
| Result | ✅ | ✅ | ✅ MVP (swap error/ok order) |
| List | ✅ | ✅ | ✅ MVP |
| Tuples | ✅ | ✅ | ✅ MVP |
| Functions | ✅ | ✅ | ✅ MVP |
| Generics | ✅ (limited) | ✅ (full) | ✅ Phase 3 |
| Extensible records | ✅ | ❌ (use interfaces/SRTP) | ❌ Never |
| Type classes | ❌ | ❌ (use interfaces) | ❌ Never |
| Mutation | ❌ | ✅ | ❌ Never |
| OOP | ❌ | ✅ | ❌ Never |
| Computation expressions | ❌ | ✅ | ❌ Never |
| Active patterns | ❌ | ✅ | ❌ Never |
| Type providers | ❌ | ✅ | ❌ Never |
| Units of measure | ❌ | ✅ | ❌ Never |

---

### Appendix B: Morphir IR JSON Schema (v3)

**Example IR JSON**:
```json
{
  "formatVersion": 3,
  "distribution": {
    "Library": {
      "packageName": [["my"], ["app"]],
      "dependencies": {},
      "packageDef": {
        "modules": {
          "User": {
            "types": {
              "Person": {
                "doc": "Represents a person",
                "value": {
                  "typeAliasDefinition": {
                    "typeParams": [],
                    "typeExpr": {
                      "record": [
                        {"name": ["name"], "type": {"reference": [["morphir"], ["s", "d", "k"], ["string"]], "String", []}},
                        {"name": ["age"], "type": {"reference": [["morphir"], ["s", "d", "k"], ["basics"]], "Int", []}}
                      ]
                    }
                  }
                }
              }
            },
            "values": {
              "createPerson": {
                "doc": "Creates a person",
                "value": {
                  "inputTypes": [
                    {"reference": [["morphir"], ["s", "d", "k"], ["string"]], "String", []},
                    {"reference": [["morphir"], ["s", "d", "k"], ["basics"]], "Int", []}
                  ],
                  "outputType": {"reference": [["my"], ["app"], ["user"]], "Person", []},
                  "body": {
                    "record": [
                      {"name": ["name"], "value": {"variable": ["name"]}},
                      {"name": ["age"], "value": {"variable": ["age"]}}
                    ]
                  }
                }
              }
            }
          }
        }
      }
    }
  }
}
```

---

### Appendix C: Error Code Reference

| Code | Message | Severity | Description |
|------|---------|----------|-------------|
| **MORPHIR001** | Mutable bindings not supported | Warning | User used `let mutable` |
| **MORPHIR002** | Classes not supported | Error | User defined a class |
| **MORPHIR003** | Computation expressions not supported | Error | User used `async {}`, `seq {}`, etc. |
| **MORPHIR004** | Active patterns not supported | Warning | User defined active pattern |
| **MORPHIR005** | Type providers not supported | Error | User used type provider |
| **MORPHIR006** | Units of measure not supported | Warning | User used `<kg>`, `<m>`, etc. |
| **MORPHIR007** | Ref cells not supported | Error | User used `ref` |
| **MORPHIR008** | Arrays not supported | Warning | User used `[|...|]` array literal |
| **MORPHIR009** | For/while loops not supported | Error | User used imperative loop |
| **MORPHIR010** | Unsupported type | Error | Generic error for unrecognized types |
| **MORPHIR011** | Unsupported expression | Error | Generic error for unrecognized expressions |

---

### Appendix D: Resource Links

**FSharp.Compiler.Service**:
- [Repository](https://github.com/fsharp/FSharp.Compiler.Service)
- [Documentation](https://fsharp.github.io/FSharp.Compiler.Service/)
- [Symbol API](https://fsharp.github.io/FSharp.Compiler.Service/symbols.html)
- [AST Documentation](https://fsharp.github.io/FSharp.Compiler.Service/untypedtree.html)

**Ionide**:
- [Ionide Repository](https://github.com/ionide/ionide-vscode-fsharp)
- [Ionide.Analyzers.SDK](https://github.com/ionide/ionide-analyzers-sdk)
- [Analyzer Documentation](https://ionide.io/Analyzers/)

**Morphir**:
- [morphir-elm Repository](https://github.com/finos/morphir-elm)
- [Morphir IR Specification](https://morphir.finos.org/docs/ir-spec)
- [Morphir Homepage](https://morphir.finos.org/)

**morphir-dotnet**:
- [Repository](https://github.com/finos/morphir-dotnet)
- [AGENTS.md](https://github.com/finos/morphir-dotnet/blob/main/AGENTS.md)
- [F# Backend Epic #363](https://github.com/finos/morphir-dotnet/issues/363)

**Testing Frameworks**:
- [Expecto](https://github.com/haf/expecto)
- [FsCheck](https://fscheck.github.io/FsCheck/)
- [Verify](https://github.com/VerifyTests/Verify)

---

**Status**: Design Complete, Ready for Implementation
**Last Updated**: 2025-12-31
**Author**: morphir-dotnet Architecture Team
**Reviewers**: TBD
