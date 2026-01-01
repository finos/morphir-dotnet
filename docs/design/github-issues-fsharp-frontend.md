# GitHub Issues: F# Frontend

This document provides a comprehensive breakdown of GitHub issues for implementing the F# Frontend (F# → Morphir IR parser). Issues are organized by milestone and follow the thin-slice approach with CLI integration from the start.

## Epic Issue

**Epic #XXX: F# Frontend - Parse F# to Morphir IR**

This epic tracks the implementation of the F# Frontend, which parses F# source code and generates Morphir IR. This completes the round-trip capability: F# ↔ Morphir IR ↔ F#.

**Strategic Value**:
- Enables F# developers to model business logic and generate Morphir IR
- Completes the round-trip: F# Backend generates F# FROM IR, F# Frontend parses F# TO IR
- Provides foundation for F#-based Morphir tooling

**Milestones**:
- M0: Foundation + CLI (2 weeks)
- M1: Type Parsing (3 weeks)
- M2: Value Parsing (3 weeks)
- M3: Multi-File Projects (2 weeks)
- M4: Production Ready (2 weeks)

**Timeline**: 12 weeks

**References**:
- [PRD: F# Frontend](../design/PRD-fsharp-frontend.md)
- [Maturity Milestones](../design/fsharp-frontend-maturity-milestones.md)
- [F# Backend Epic #363](https://github.com/finos/morphir-dotnet/issues/363)

---

## M0: Foundation + CLI (Weeks 1-2)

### Issue #XXX: M0.1 - Setup F# Frontend Project Structure

**Labels**: `enhancement`, `m0-foundation`, `frontend`

**Description**:
Create the project structure for the F# Frontend, including the core library and CLI tool.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/` project (.fsproj)
- [ ] Create `src/Morphir.Frontends.FSharp.Cli/` project (.fsproj)
- [ ] Create `tests/Morphir.Frontends.FSharp.Tests/` project (.fsproj)
- [ ] Create `tests/Morphir.Frontends.FSharp.Cli.Tests/` project (.fsproj)
- [ ] Add FSharp.Compiler.Service 43.9+ dependency
- [ ] Add Morphir.Models dependency (IR types)
- [ ] Add Expecto test framework
- [ ] Add TUnit test framework (for CLI tests)
- [ ] Configure AOT compatibility (library only, CLI is runtime)
- [ ] Add to solution file
- [ ] Add to CI/CD pipeline

**Definition of Done**:
- All projects build successfully
- Tests run in CI (even if empty)
- NuGet dependencies restored
- AOT warnings suppressed for FCS (runtime-only)

**Estimated Effort**: 1 day

---

### Issue #XXX: M0.2 - Implement FSharp.Compiler.Service Parser

**Labels**: `enhancement`, `m0-foundation`, `frontend`

**Description**:
Implement the `Parser` module that wraps FSharp.Compiler.Service for parsing and type-checking F# source files.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/Parser.fs`
- [ ] Implement `parseFile : string -> string -> Result<ParseResult, string>`
- [ ] Parse F# source using FCS (`FSharpChecker.ParseFileInProject`)
- [ ] Type-check F# source using FCS (`FSharpChecker.CheckFileInProject`)
- [ ] Extract typed AST (`ParsedInput`)
- [ ] Extract FCS diagnostics (errors, warnings)
- [ ] Handle syntax errors gracefully
- [ ] Handle missing files gracefully
- [ ] Unit tests for Parser module (100% coverage)
- [ ] Test multi-file projects (dependencies)

**Example Usage**:
```fsharp
let result = Parser.parseFile "Calculator.fs" sourceCode
match result with
| Ok parseResult ->
    printfn "Parsed: %A" parseResult.ParsedInput
    printfn "Diagnostics: %A" parseResult.Diagnostics
| Error msg ->
    printfn "Error: %s" msg
```

**Definition of Done**:
- Parser can parse any valid F# file
- Reports accurate FCS diagnostics
- 100% test coverage
- All tests pass

**Estimated Effort**: 2-3 days

---

### Issue #XXX: M0.3 - Implement Minimal IR Generator

**Labels**: `enhancement`, `m0-foundation`, `frontend`

**Description**:
Implement the `IRGenerator` module that generates minimal Morphir IR (empty modules) from parsed F# AST.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/IRGenerator.fs`
- [ ] Implement `generatePackage : ParsedInput -> Package.Definition<unit>`
- [ ] Extract F# namespace → Morphir package name
- [ ] Extract F# module name → Morphir module name
- [ ] Generate empty module definition (no types, no values)
- [ ] Preserve module documentation comments
- [ ] Generate valid Morphir IR v3 JSON schema
- [ ] Unit tests for IRGenerator (100% coverage)
- [ ] Test namespace mapping
- [ ] Test module metadata extraction

**Example Output**:
```json
{
  "formatVersion": 3,
  "distribution": {
    "Library": {
      "packageName": ["My", "Domain"],
      "packageDef": {
        "modules": {
          "Calculator": {
            "types": {},
            "values": {}
          }
        }
      }
    }
  }
}
```

**Definition of Done**:
- Generates valid IR v3 JSON
- 100% test coverage
- All tests pass

**Estimated Effort**: 2 days

---

### Issue #XXX: M0.4 - Implement CLI Tool (Minimal)

**Labels**: `enhancement`, `m0-foundation`, `frontend`, `cli`

**Description**:
Implement the CLI tool with minimal commands: `parse`, `--json`, `--output`, `--validate`.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp.Cli/Program.fs`
- [ ] Create `src/Morphir.Frontends.FSharp.Cli/ParseCommand.fs`
- [ ] Implement `morphir fsharp parse <file>` command
- [ ] Implement `--json` flag (output JSON to stdout)
- [ ] Implement `--output <file>` flag (write JSON to file)
- [ ] Implement `--validate` flag (validate only, no output)
- [ ] Error reporting to stderr (never stdout)
- [ ] Exit codes: 0 = success, 1 = error
- [ ] Logging to stderr only (use Serilog)
- [ ] CLI integration tests (invoke actual binary)
- [ ] Test stdout vs stderr separation
- [ ] Test exit codes

**Example Usage**:
```bash
$ morphir fsharp parse EmptyModule.fs --json
{ "formatVersion": 3, ... }

$ morphir fsharp parse EmptyModule.fs --output ir/module.json
✓ Parsed EmptyModule.fs → ir/module.json

$ morphir fsharp parse EmptyModule.fs --validate
✓ EmptyModule.fs is valid (0 errors, 0 warnings)
```

**Definition of Done**:
- CLI is scriptable: `morphir fsharp parse file.fs --json | jq`
- Stdout = JSON data only, stderr = diagnostics
- E2E CLI tests pass
- NuGet package: `Morphir.Frontends.FSharp.Cli`

**Estimated Effort**: 2-3 days

---

### Issue #XXX: M0.5 - E2E Testing (M0 Validation)

**Labels**: `testing`, `m0-foundation`, `frontend`, `e2e`

**Description**:
Create end-to-end tests that validate the complete M0 pipeline: F# source → CLI → JSON output.

**Acceptance Criteria**:
- [ ] Create `tests/Morphir.Frontends.FSharp.Cli.Tests/E2ETests.fs`
- [ ] Test: Parse empty module → JSON output
- [ ] Test: Parse empty module → File output
- [ ] Test: Validate-only mode (no JSON output)
- [ ] Test: Syntax error handling (exit code 1)
- [ ] Test: Missing file handling (exit code 1)
- [ ] Test: JSON schema validation (use `morphir verify`)
- [ ] Test: Stdout vs stderr separation
- [ ] Test: Scriptability (`... | jq` works)
- [ ] All E2E tests pass in CI

**Definition of Done**:
- Complete M0 thin slice validated end-to-end
- All E2E tests pass
- CI pipeline green

**Estimated Effort**: 1-2 days

---

## M1: Type Parsing (Weeks 3-5)

### Issue #XXX: M1.1 - Implement TypeMapper (Primitives)

**Labels**: `enhancement`, `m1-types`, `frontend`

**Description**:
Implement the `TypeMapper` module to map F# primitive types to Morphir IR types.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/TypeMapper.fs`
- [ ] Implement `mapType : FSharpCheckFileResults -> SynType -> Type<unit>`
- [ ] Map `int` → `Morphir.SDK.Basics.Int`
- [ ] Map `string` → `Morphir.SDK.String.String`
- [ ] Map `bool` → `Morphir.SDK.Basics.Bool`
- [ ] Map `float` → `Morphir.SDK.Basics.Float`
- [ ] Map `char` → `Morphir.SDK.Char.Char`
- [ ] Map `unit` → `Morphir.SDK.Basics.Unit`
- [ ] Unit tests for all primitive mappings (100% coverage)
- [ ] Property-based tests (FsCheck)

**Definition of Done**:
- All primitives map correctly
- 100% test coverage
- Property tests pass

**Estimated Effort**: 2 days

---

### Issue #XXX: M1.2 - Implement TypeMapper (Collections & Option/Result)

**Labels**: `enhancement`, `m1-types`, `frontend`

**Description**:
Extend `TypeMapper` to support collection types, Option, and Result.

**Acceptance Criteria**:
- [ ] Map `list<'T>` → `Morphir.SDK.List.List a`
- [ ] Map `'T array` → `Morphir.SDK.Array.Array a`
- [ ] Map `seq<'T>` → `Morphir.SDK.List.List a` (approximation)
- [ ] Map `Option<'T>` → `Morphir.SDK.Maybe.Maybe a`
- [ ] Map `Result<'T, 'E>` → `Morphir.SDK.Result.Result e a`
- [ ] Map tuples: `'T1 * 'T2` → `(a, b)`
- [ ] Map tuples: `'T1 * 'T2 * 'T3` → `(a, b, c)`
- [ ] Unit tests for all collection mappings
- [ ] Property-based tests

**Definition of Done**:
- All collection types map correctly
- 100% test coverage
- Property tests pass

**Estimated Effort**: 2 days

---

### Issue #XXX: M1.3 - Implement TypeMapper (Records)

**Labels**: `enhancement`, `m1-types`, `frontend`

**Description**:
Extend `TypeMapper` to support F# record types.

**Acceptance Criteria**:
- [ ] Map F# records → Morphir `Type.Record`
- [ ] Extract field names
- [ ] Map field types recursively
- [ ] Preserve documentation comments
- [ ] Handle recursive record references
- [ ] Handle generic records (`type Point<'T> = { X: 'T; Y: 'T }`)
- [ ] Generate `Type.Definition.TypeAliasDefinition`
- [ ] Unit tests (simple records, generic records, recursive)
- [ ] Snapshot tests (approved snapshots)

**Example**:
```fsharp
type Customer = {
    CustomerId: int
    Name: string
    Email: string option
}
```

**Definition of Done**:
- Records map to Morphir Record types
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 3 days

---

### Issue #XXX: M1.4 - Implement TypeMapper (Discriminated Unions)

**Labels**: `enhancement`, `m1-types`, `frontend`

**Description**:
Extend `TypeMapper` to support F# discriminated unions.

**Acceptance Criteria**:
- [ ] Map F# DUs → Morphir `Type.CustomType`
- [ ] Extract constructor names
- [ ] Map constructor fields (named and unnamed)
- [ ] Preserve documentation comments
- [ ] Handle recursive DU references
- [ ] Handle generic DUs (`type Tree<'T> = Leaf of 'T | Branch of Tree<'T> * Tree<'T>`)
- [ ] Generate `Type.Definition.CustomTypeDefinition`
- [ ] Unit tests (simple DUs, generic DUs, recursive)
- [ ] Snapshot tests

**Example**:
```fsharp
type OrderStatus =
    | Pending
    | Confirmed of confirmedAt: System.DateTime
    | Shipped of trackingNumber: string
    | Cancelled
```

**Definition of Done**:
- DUs map to Morphir Custom types
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 3 days

---

### Issue #XXX: M1.5 - Implement TypeMapper (Type Aliases & Function Types)

**Labels**: `enhancement`, `m1-types`, `frontend`

**Description**:
Extend `TypeMapper` to support type aliases and function types.

**Acceptance Criteria**:
- [ ] Map type aliases: `type UserId = int` → TypeAlias
- [ ] Map function types: `'T1 -> 'T2` → `Function a b`
- [ ] Map curried functions: `'T1 -> 'T2 -> 'T3` → `Function a (Function b c)`
- [ ] Map generic type parameters: `'T`, `'a`, `'b` → type variables
- [ ] Handle constraints (ignore for MVP, future: #XXX)
- [ ] Unit tests
- [ ] Snapshot tests

**Definition of Done**:
- Type aliases and function types map correctly
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 2 days

---

### Issue #XXX: M1.6 - Integrate TypeMapper into IRGenerator

**Labels**: `enhancement`, `m1-types`, `frontend`

**Description**:
Integrate `TypeMapper` into `IRGenerator` to generate type definitions in the IR.

**Acceptance Criteria**:
- [ ] Update `IRGenerator` to extract type definitions from AST
- [ ] Map type definitions using `TypeMapper`
- [ ] Generate `Module.Definition.types` (no longer empty)
- [ ] Preserve type documentation
- [ ] Handle multiple type definitions per module
- [ ] Unit tests (modules with types)
- [ ] Integration tests (complete modules)

**Definition of Done**:
- IR output includes type definitions
- JSON schema valid
- Integration tests pass

**Estimated Effort**: 2 days

---

### Issue #XXX: M1.7 - CLI Enhancement: --pretty Flag

**Labels**: `enhancement`, `m1-types`, `frontend`, `cli`

**Description**:
Add `--pretty` flag to CLI for human-readable JSON output.

**Acceptance Criteria**:
- [ ] Implement `--pretty` flag in `ParseCommand`
- [ ] Use `JsonSerializerOptions.WriteIndented = true`
- [ ] CLI integration tests for pretty-printing
- [ ] Update CLI documentation

**Definition of Done**:
- `--pretty` produces formatted JSON
- Tests pass
- Documentation updated

**Estimated Effort**: 0.5 days

---

### Issue #XXX: M1.8 - E2E Testing (M1 Validation)

**Labels**: `testing`, `m1-types`, `frontend`, `e2e`

**Description**:
Create end-to-end tests for M1: Parse F# types → JSON → Validate schema.

**Acceptance Criteria**:
- [ ] Test: Parse records → JSON → Validate with `morphir verify`
- [ ] Test: Parse DUs → JSON → Validate
- [ ] Test: Parse type aliases → JSON → Validate
- [ ] Test: Parse generic types → JSON → Validate
- [ ] Test: Parse recursive types → JSON → Validate
- [ ] Test: Round-trip (F# type → IR → JSON → IR, equality)
- [ ] All E2E tests pass in CI

**Definition of Done**:
- Complete M1 thin slice validated end-to-end
- Round-trip tests pass
- CI pipeline green

**Estimated Effort**: 2 days

---

## M2: Value Parsing (Weeks 6-8)

### Issue #XXX: M2.1 - Implement ValueMapper (Literals)

**Labels**: `enhancement`, `m2-values`, `frontend`

**Description**:
Implement the `ValueMapper` module to map F# literal expressions to Morphir IR values.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/ValueMapper.fs`
- [ ] Implement `mapExpr : FSharpCheckFileResults -> SynExpr -> Value<unit, unit>`
- [ ] Map int literals: `42` → `Value.Literal (IntLiteral 42)`
- [ ] Map string literals: `"hello"` → `Value.Literal (StringLiteral "hello")`
- [ ] Map bool literals: `true`, `false`
- [ ] Map float literals: `3.14`
- [ ] Map char literals: `'a'`
- [ ] Map unit literal: `()`
- [ ] Unit tests for all literal types (100% coverage)

**Definition of Done**:
- All literal types map correctly
- 100% test coverage
- Tests pass

**Estimated Effort**: 2 days

---

### Issue #XXX: M2.2 - Implement ValueMapper (Tuples, Lists, Records, DUs)

**Labels**: `enhancement`, `m2-values`, `frontend`

**Description**:
Extend `ValueMapper` to support structured data expressions.

**Acceptance Criteria**:
- [ ] Map tuple expressions: `(1, "hello")` → `Value.Tuple [...]`
- [ ] Map list literals: `[1; 2; 3]` → `Value.List [...]`
- [ ] Map array literals: `[| 1; 2; 3 |]` → `Value.List [...]` (approximation)
- [ ] Map record construction: `{ Field = value }` → `Value.Record [...]`
- [ ] Map DU construction: `Some 42`, `Ok "success"` → `Value.Constructor`
- [ ] Unit tests for all structured expressions
- [ ] Snapshot tests

**Definition of Done**:
- All structured expressions map correctly
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 3 days

---

### Issue #XXX: M2.3 - Implement ValueMapper (Variables, Application, Lambda)

**Labels**: `enhancement`, `m2-values`, `frontend`

**Description**:
Extend `ValueMapper` to support variables, function application, and lambda expressions.

**Acceptance Criteria**:
- [ ] Map variable references: `x` → `Value.Variable "x"`
- [ ] Map function application: `f x` → `Value.Apply (f, x)`
- [ ] Map curried application: `f x y` → `Value.Apply (Apply (f, x), y)`
- [ ] Map lambda expressions: `fun x -> x + 1` → `Value.Lambda`
- [ ] Map multi-argument lambdas: `fun x y -> x + y`
- [ ] Unit tests
- [ ] Snapshot tests

**Definition of Done**:
- Variables, application, lambdas map correctly
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 3 days

---

### Issue #XXX: M2.4 - Implement ValueMapper (Let Bindings)

**Labels**: `enhancement`, `m2-values`, `frontend`

**Description**:
Extend `ValueMapper` to support let bindings.

**Acceptance Criteria**:
- [ ] Map simple let bindings: `let x = 42 in x + 1` → `Value.LetDefinition`
- [ ] Map multiple let bindings: `let x = 1 in let y = 2 in x + y`
- [ ] Map recursive bindings: `let rec f n = ...`
- [ ] Map mutually recursive bindings: `let rec f ... and g ...`
- [ ] Pattern extraction (simple patterns only, see #XXX for complex)
- [ ] Unit tests
- [ ] Snapshot tests

**Definition of Done**:
- Let bindings map correctly
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 2 days

---

### Issue #XXX: M2.5 - Implement ValueMapper (If-Then-Else)

**Labels**: `enhancement`, `m2-values`, `frontend`

**Description**:
Extend `ValueMapper` to support if-then-else expressions.

**Acceptance Criteria**:
- [ ] Map if-then-else: `if cond then x else y` → `Value.IfThenElse`
- [ ] Handle nested if-then-else
- [ ] Handle unit-returning branches (e.g., imperative-style)
- [ ] Unit tests
- [ ] Snapshot tests

**Definition of Done**:
- If-then-else maps correctly
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 1 day

---

### Issue #XXX: M2.6 - Implement PatternMapper

**Labels**: `enhancement`, `m2-values`, `frontend`

**Description**:
Implement the `PatternMapper` module to map F# patterns to Morphir IR patterns.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/PatternMapper.fs`
- [ ] Implement `mapPattern : SynPat -> Pattern<unit>`
- [ ] Map wildcard pattern: `_` → `Pattern.Wildcard`
- [ ] Map variable pattern: `x` → `Pattern.AsPattern (Wildcard, "x")`
- [ ] Map literal patterns: `42`, `"hello"`
- [ ] Map tuple patterns: `(x, y)`
- [ ] Map record patterns: `{ Field = x }`
- [ ] Map DU patterns: `Some x`, `Ok value`
- [ ] Map list patterns: `[]`, `head :: tail`
- [ ] Map as-patterns: `x as value`
- [ ] Unit tests (100% coverage)

**Definition of Done**:
- All pattern types map correctly
- 100% test coverage
- Tests pass

**Estimated Effort**: 3 days

---

### Issue #XXX: M2.7 - Implement ValueMapper (Match Expressions)

**Labels**: `enhancement`, `m2-values`, `frontend`

**Description**:
Extend `ValueMapper` to support match expressions (pattern matching).

**Acceptance Criteria**:
- [ ] Map match expressions: `match x with | pat -> expr` → `Value.PatternMatch`
- [ ] Map multiple cases: `match x with | p1 -> e1 | p2 -> e2`
- [ ] Use `PatternMapper` for pattern extraction
- [ ] Handle exhaustiveness (FCS diagnostics)
- [ ] Handle when guards: `match x with | p when cond -> expr`
- [ ] Unit tests
- [ ] Snapshot tests

**Definition of Done**:
- Match expressions map correctly
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 3 days

---

### Issue #XXX: M2.8 - Implement ModuleMapper (Functions)

**Labels**: `enhancement`, `m2-values`, `frontend`

**Description**:
Implement the `ModuleMapper` module to extract function definitions from F# modules.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/ModuleMapper.fs`
- [ ] Extract top-level `let` bindings as functions
- [ ] Map function name, parameters, return type
- [ ] Use `ValueMapper` to map function body
- [ ] Handle curried functions
- [ ] Handle recursive functions (`let rec`)
- [ ] Handle mutually recursive functions
- [ ] Preserve function documentation
- [ ] Generate `Module.Definition.values`
- [ ] Unit tests
- [ ] Integration tests (complete modules)

**Definition of Done**:
- Functions appear in IR output (`values: {...}`)
- 100% test coverage
- Integration tests pass

**Estimated Effort**: 3 days

---

### Issue #XXX: M2.9 - CLI Enhancement: --round-trip Flag

**Labels**: `enhancement`, `m2-values`, `frontend`, `cli`

**Description**:
Add `--round-trip` flag to CLI to validate F# → IR → F# → IR round-trip.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp.Cli/RoundTripCommand.fs`
- [ ] Implement `--round-trip` flag
- [ ] Parse F# → IR₁ (frontend)
- [ ] Generate F# from IR₁ (backend, requires F# Backend M2+)
- [ ] Parse generated F# → IR₂ (frontend)
- [ ] Compare IR₁ == IR₂ (structural equality)
- [ ] Report round-trip success/failure
- [ ] CLI integration tests
- [ ] E2E round-trip tests

**Example**:
```bash
$ morphir fsharp parse Calculator.fs --round-trip
1. F# → IR (frontend) ✓
2. IR → F# (backend) ✓
3. F# → IR (frontend) ✓
4. Compare IR₁ == IR₂ ✓
✅ Round-trip successful!
```

**Definition of Done**:
- Round-trip validation works end-to-end
- Tests pass
- Documentation updated

**Estimated Effort**: 2 days

---

### Issue #XXX: M2.10 - E2E Testing (M2 Validation)

**Labels**: `testing`, `m2-values`, `frontend`, `e2e`

**Description**:
Create end-to-end tests for M2: Parse F# functions → JSON → Round-trip test.

**Acceptance Criteria**:
- [ ] Test: Parse simple functions → JSON
- [ ] Test: Parse recursive functions → JSON
- [ ] Test: Parse pattern matching → JSON
- [ ] Test: Parse lambdas → JSON
- [ ] Test: Round-trip (F# → IR → F# → IR, 95%+ success)
- [ ] Test: Validate JSON schema
- [ ] All E2E tests pass in CI
- [ ] Round-trip tests pass for 50+ example functions

**Definition of Done**:
- Complete M2 thin slice validated end-to-end
- Round-trip tests pass (95%+)
- CI pipeline green

**Estimated Effort**: 3 days

---

## M3: Multi-File Projects (Weeks 9-10)

### Issue #XXX: M3.1 - Implement ProjectParser (.fsproj)

**Labels**: `enhancement`, `m3-multi-file`, `frontend`

**Description**:
Implement the `ProjectParser` module to parse F# project files (.fsproj) and extract file dependencies.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/ProjectParser.fs`
- [ ] Implement `parseProject : string -> Result<ProjectInfo, string>`
- [ ] Parse .fsproj XML
- [ ] Extract `<Compile Include="..." />` items (source files)
- [ ] Extract compile order (F# requires ordered compilation)
- [ ] Extract NuGet package references
- [ ] Handle nested directories
- [ ] Handle glob patterns (`**/*.fs`)
- [ ] Unit tests (sample .fsproj files)

**Definition of Done**:
- Can parse .fsproj files
- Extracts source files in correct order
- 100% test coverage
- Tests pass

**Estimated Effort**: 2 days

---

### Issue #XXX: M3.2 - Implement DependencyResolver

**Labels**: `enhancement`, `m3-multi-file`, `frontend`

**Description**:
Implement the `DependencyResolver` module to resolve cross-file dependencies and type references.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/DependencyResolver.fs`
- [ ] Resolve `open` statements (F# imports)
- [ ] Build dependency graph (module → dependencies)
- [ ] Detect circular dependencies (error reporting)
- [ ] Resolve qualified names (`Module.function`)
- [ ] Resolve type references across files
- [ ] Unit tests (multi-file scenarios)

**Definition of Done**:
- Cross-file references resolve correctly
- Circular dependencies detected
- 100% test coverage
- Tests pass

**Estimated Effort**: 3 days

---

### Issue #XXX: M3.3 - Implement ImportMapper

**Labels**: `enhancement`, `m3-multi-file`, `frontend`

**Description**:
Implement the `ImportMapper` module to map F# `open` statements to Morphir imports.

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp/ImportMapper.fs`
- [ ] Map `open MyDomain.Types` → Morphir import
- [ ] Handle qualified imports
- [ ] Handle wildcard imports (`open type MyModule`)
- [ ] Resolve import paths
- [ ] Unit tests

**Definition of Done**:
- F# imports map to Morphir imports
- 100% test coverage
- Tests pass

**Estimated Effort**: 2 days

---

### Issue #XXX: M3.4 - Extend IRGenerator for Multi-File Projects

**Labels**: `enhancement`, `m3-multi-file`, `frontend`

**Description**:
Extend `IRGenerator` to generate unified IR packages from multi-file projects.

**Acceptance Criteria**:
- [ ] Parse multiple source files
- [ ] Resolve dependencies using `DependencyResolver`
- [ ] Generate unified `Package.Definition`
- [ ] Merge modules from all files
- [ ] Handle cross-file type/value references
- [ ] Integration tests (multi-file projects)

**Definition of Done**:
- Multi-file projects generate unified IR
- Cross-file references work
- Integration tests pass

**Estimated Effort**: 3 days

---

### Issue #XXX: M3.5 - Implement Generic Type Support

**Labels**: `enhancement`, `m3-multi-file`, `frontend`

**Description**:
Add support for generic types and functions.

**Acceptance Criteria**:
- [ ] Parse generic type parameters: `'T`, `'a`, `'b`
- [ ] Map generic types to polymorphic IR
- [ ] Handle generic constraints (basic: `'T when 'T : comparison`)
- [ ] Map generic functions to polymorphic IR
- [ ] Infer type parameters (use FCS type checking)
- [ ] Unit tests (generic types, functions)
- [ ] Snapshot tests

**Definition of Done**:
- Generic types and functions map correctly
- Type inference works
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 3 days

---

### Issue #XXX: M3.6 - Implement Higher-Order Function Support

**Labels**: `enhancement`, `m3-multi-file`, `frontend`

**Description**:
Add support for higher-order functions (map, filter, fold, etc.).

**Acceptance Criteria**:
- [ ] Parse higher-order functions: `List.map f xs`
- [ ] Parse function composition: `f >> g`, `f << g`
- [ ] Parse partial application: `add 1` (returns `int -> int`)
- [ ] Map to Morphir IR (lambda lifting if needed)
- [ ] Unit tests
- [ ] Snapshot tests

**Definition of Done**:
- Higher-order functions map correctly
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 2 days

---

### Issue #XXX: M3.7 - CLI Enhancement: Parse .fsproj

**Labels**: `enhancement`, `m3-multi-file`, `frontend`, `cli`

**Description**:
Extend CLI to support parsing entire F# projects (.fsproj).

**Acceptance Criteria**:
- [ ] Update `ParseCommand` to accept .fsproj files
- [ ] Parse all source files in project
- [ ] Generate unified IR JSON
- [ ] `--output <dir>` writes JSON to directory
- [ ] Progress indicators for large projects
- [ ] CLI integration tests

**Example**:
```bash
$ morphir fsharp parse MyProject/MyProject.fsproj --output ir/
✓ Parsed 10 files
✓ Resolved 45 cross-file references
✓ Generated ir/MyProject.json (unified IR)
```

**Definition of Done**:
- CLI can parse .fsproj projects
- Generates unified JSON
- Tests pass

**Estimated Effort**: 2 days

---

### Issue #XXX: M3.8 - E2E Testing (M3 Validation)

**Labels**: `testing`, `m3-multi-file`, `frontend`, `e2e`

**Description**:
Create end-to-end tests for M3: Parse .fsproj → Unified JSON → Round-trip.

**Acceptance Criteria**:
- [ ] Test: Parse multi-file project → JSON
- [ ] Test: Cross-file type references
- [ ] Test: Cross-file function calls
- [ ] Test: Generic types and functions
- [ ] Test: Higher-order functions
- [ ] Test: Round-trip (multi-file project)
- [ ] All E2E tests pass in CI

**Definition of Done**:
- Complete M3 thin slice validated end-to-end
- Round-trip tests pass (multi-file)
- CI pipeline green

**Estimated Effort**: 3 days

---

## M4: Production Ready (Weeks 11-12)

### Issue #XXX: M4.1 - Implement Ionide Analyzer (Unsupported Features)

**Labels**: `enhancement`, `m4-production`, `frontend`, `analyzer`

**Description**:
Implement an Ionide analyzer to detect unsupported F# features in real-time (VS Code integration).

**Acceptance Criteria**:
- [ ] Create `src/Morphir.Frontends.FSharp.Analyzer/` project
- [ ] Add Ionide.Analyzers.SDK dependency
- [ ] Implement `[<CliAnalyzer "MorphirUnsupportedFeatures">]`
- [ ] Error: Mutable bindings (`let mutable`)
- [ ] Error: Classes/OOP (`type MyClass() = ...`)
- [ ] Error: Computation expressions (`async { }`, `seq { }`)
- [ ] Warning: Active patterns (future support)
- [ ] Info: Suggest Morphir-friendly alternatives
- [ ] VS Code integration tests
- [ ] Documentation (setup guide)

**Definition of Done**:
- Analyzer works in VS Code (via Ionide)
- Detects all unsupported features
- Documentation complete
- NuGet package: `Morphir.Frontends.FSharp.Analyzer`

**Estimated Effort**: 3 days

---

### Issue #XXX: M4.2 - Implement Advanced Features (P1)

**Labels**: `enhancement`, `m4-production`, `frontend`

**Description**:
Implement advanced F# features from PRD P1 list.

**Acceptance Criteria**:
- [ ] Generic constraints (`'T when 'T : comparison`)
- [ ] Recursive types (mutual recursion)
- [ ] Nested modules
- [ ] Module type annotations
- [ ] Record update syntax (`{ record with Field = value }`)
- [ ] List comprehensions (`[ for x in xs -> f x ]`)
- [ ] Unit tests for all P1 features
- [ ] Snapshot tests

**Definition of Done**:
- All P1 features implemented
- 100% test coverage
- Snapshot tests approved

**Estimated Effort**: 4 days

---

### Issue #XXX: M4.3 - CLI Enhancement: --strict, --benchmark, --watch

**Labels**: `enhancement`, `m4-production`, `frontend`, `cli`

**Description**:
Add final CLI flags for production use: `--strict`, `--benchmark`, `--watch`.

**Acceptance Criteria**:
- [ ] Implement `--strict` flag (fail on warnings)
- [ ] Implement `--benchmark` flag (show performance metrics)
- [ ] Implement `--watch` flag (re-parse on file change)
- [ ] Progress indicators for large projects
- [ ] CLI integration tests for all flags
- [ ] Documentation updated

**Example**:
```bash
$ morphir fsharp parse src/ --strict
❌ Calculator.fs:2:9: Mutable bindings not supported
Exit code: 1

$ morphir fsharp parse LargeProject.fsproj --benchmark
✓ Parsed 10,000 lines in 847ms
✓ Generated 2.3MB IR JSON
✓ Memory usage: 145MB peak

$ morphir fsharp parse src/ --watch
👁️ Watching src/ for changes...
✓ Parsed Calculator.fs
[file changed: Calculator.fs]
✓ Re-parsed Calculator.fs
```

**Definition of Done**:
- All CLI flags implemented
- Tests pass
- Documentation updated

**Estimated Effort**: 3 days

---

### Issue #XXX: M4.4 - Performance Optimization & Benchmarking

**Labels**: `enhancement`, `m4-production`, `frontend`, `performance`

**Description**:
Optimize parser performance and create benchmarks.

**Acceptance Criteria**:
- [ ] Profile parser (identify bottlenecks)
- [ ] Optimize hot paths
- [ ] Cache FCS type checking results
- [ ] Parallel file parsing (multi-core)
- [ ] Memory profiling (no leaks)
- [ ] Benchmark suite (BenchmarkDotNet)
- [ ] Performance baseline: Parse 1000 LOC < 1s
- [ ] Stress tests (10K+ LOC projects)

**Definition of Done**:
- Performance target met (1000 LOC/sec)
- No memory leaks
- Benchmarks pass
- Stress tests pass

**Estimated Effort**: 3 days

---

### Issue #XXX: M4.5 - Compatibility Testing (F# 8, 9, 10)

**Labels**: `testing`, `m4-production`, `frontend`, `compatibility`

**Description**:
Test compatibility across multiple F# versions.

**Acceptance Criteria**:
- [ ] Test with F# 8.0
- [ ] Test with F# 9.0
- [ ] Test with F# 10.0 (preview)
- [ ] Test with FSharp.Compiler.Service versions (43.x, 44.x)
- [ ] Compatibility matrix documentation
- [ ] CI matrix builds (multiple F# versions)

**Definition of Done**:
- Compatible with F# 8, 9, 10
- CI tests pass for all versions
- Documentation updated

**Estimated Effort**: 2 days

---

### Issue #XXX: M4.6 - Documentation (User Guide, API Docs, Troubleshooting)

**Labels**: `documentation`, `m4-production`, `frontend`

**Description**:
Create comprehensive documentation for the F# Frontend.

**Acceptance Criteria**:
- [ ] `docs/frontend/user-guide.md` - Complete user guide
- [ ] `docs/frontend/language-support.md` - Feature matrix (supported vs unsupported)
- [ ] `docs/frontend/cli-reference.md` - CLI command reference
- [ ] `docs/frontend/troubleshooting.md` - Common issues and solutions
- [ ] `docs/frontend/ionide-setup.md` - Ionide analyzer setup
- [ ] API documentation (XML docs in code)
- [ ] Code examples (20+ examples)
- [ ] Tutorial: First F# → Morphir project

**Definition of Done**:
- All documentation complete
- Examples tested and work
- Documentation reviewed

**Estimated Effort**: 4 days

---

### Issue #XXX: M4.7 - Regression Test Suite

**Labels**: `testing`, `m4-production`, `frontend`

**Description**:
Create comprehensive regression test suite with all examples from documentation.

**Acceptance Criteria**:
- [ ] Extract all code examples from docs
- [ ] Create regression tests (parse + validate)
- [ ] 100+ regression test cases
- [ ] Snapshot tests for all examples
- [ ] Round-trip tests for all examples
- [ ] CI integration (run on every commit)

**Definition of Done**:
- Regression suite complete
- All tests pass
- CI integration working

**Estimated Effort**: 3 days

---

### Issue #XXX: M4.8 - NuGet Packaging & Release

**Labels**: `release`, `m4-production`, `frontend`

**Description**:
Package and release the F# Frontend to NuGet.

**Acceptance Criteria**:
- [ ] Configure NuGet package metadata
- [ ] Package: `Morphir.Frontends.FSharp` (library)
- [ ] Package: `Morphir.Frontends.FSharp.Cli` (dotnet tool)
- [ ] Package: `Morphir.Frontends.FSharp.Analyzer` (Ionide analyzer)
- [ ] Package icons, README, license
- [ ] Publish to NuGet.org
- [ ] GitHub release with binaries
- [ ] Release notes

**Definition of Done**:
- All packages published to NuGet
- GitHub release created
- Release notes complete

**Estimated Effort**: 2 days

---

### Issue #XXX: M4.9 - E2E Testing (M4 Validation)

**Labels**: `testing`, `m4-production`, `frontend`, `e2e`

**Description**:
Final end-to-end validation for M4: Full production workflow.

**Acceptance Criteria**:
- [ ] Test: Complete workflow (F# project → IR → Round-trip)
- [ ] Test: Ionide analyzer in VS Code
- [ ] Test: All CLI flags
- [ ] Test: Performance benchmarks
- [ ] Test: Compatibility (F# 8, 9, 10)
- [ ] Test: Large projects (10K+ LOC)
- [ ] Test: Stress testing
- [ ] All E2E tests pass in CI

**Definition of Done**:
- Complete M4 thin slice validated end-to-end
- All production features work
- Zero critical bugs
- CI pipeline green

**Estimated Effort**: 3 days

---

## Issue Summary

| Milestone | Issues | Estimated Effort |
|-----------|--------|------------------|
| M0: Foundation + CLI | 5 | 8-10 days |
| M1: Type Parsing | 8 | 16-17 days |
| M2: Value Parsing | 10 | 21-23 days |
| M3: Multi-File Projects | 8 | 17-19 days |
| M4: Production Ready | 9 | 23-25 days |
| **Total** | **40 issues** | **85-94 days (17-19 weeks)** |

**Note**: Timeline assumes 1 developer working full-time. With 2-3 developers working in parallel, the 12-week target is achievable.

---

## Issue Labels

Suggested GitHub labels for organizing issues:

- `epic` - Epic-level tracking issue
- `enhancement` - New feature/functionality
- `testing` - Testing-related issue
- `documentation` - Documentation-related issue
- `performance` - Performance optimization
- `release` - Release-related tasks
- `frontend` - F# Frontend (general)
- `cli` - CLI tool
- `analyzer` - Ionide analyzer
- `m0-foundation` - M0 milestone
- `m1-types` - M1 milestone
- `m2-values` - M2 milestone
- `m3-multi-file` - M3 milestone
- `m4-production` - M4 milestone
- `e2e` - End-to-end testing
- `aot` - AOT compatibility
- `compatibility` - Cross-version compatibility

---

## References

- [PRD: F# Frontend](./PRD-fsharp-frontend.md)
- [F# Frontend Maturity Milestones](./fsharp-frontend-maturity-milestones.md)
- [F# Backend GitHub Issues](./github-issues-fsharp-backend.md) - Similar pattern
- [Morphir.SDK Library Plan](./morphir-sdk-library-plan.md)
- [F# Coding Guide](../contributing/fsharp-coding-guide.md)

---

**Document Status**: Draft
**Last Updated**: 2025-12-31
**Next Review**: Before issue creation
