# Morphir Ecosystem Knowledge Base

**Created**: 2025-12-23
**Purpose**: Comprehensive knowledge base for the Morphir Application Architect skill
**Status**: v1.0 (Initial compilation from Task 1.1, Issue #315)

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Morphir Philosophy and Core Principles](#morphir-philosophy-and-core-principles)
3. [IR Structure and Type System](#ir-structure-and-type-system)
4. [Repository Relationships](#repository-relationships)
5. [Cross-Repository Patterns](#cross-repository-patterns)
6. [Version Compatibility Matrix](#version-compatibility-matrix)
7. [Backend Generation Strategies](#backend-generation-strategies)
8. [Testing Approaches](#testing-approaches)
9. [Domain Modeling Patterns](#domain-modeling-patterns)
10. [Integration Points](#integration-points)
11. [Quick Reference](#quick-reference)

---

## Executive Summary

Morphir is a multi-language system built on a technology-agnostic intermediate representation (IR) that captures business logic and domain models. The ecosystem consists of three primary repositories:

| Repository | Role | Primary Language | Key Capabilities |
|------------|------|------------------|------------------|
| **morphir-elm** | Frontend & Tooling | Elm (61.4%) | IR authoring, CLI (make/gen/develop), visualization, backend generators |
| **morphir** (core) | Specification & Docs | Elm (60.9%) | Core concepts, documentation, examples, community resources |
| **morphir-dotnet** | .NET Implementation | C# (42%), F# (27%) | .NET SDK, CLI tools, AOT-optimized tooling, Blazor playground |

**Core Value Proposition**: "Data and rules reign supreme" — business logic as portable data that translates across languages/platforms without rewrites.

---

## Morphir Philosophy and Core Principles

### 1. Foundational Philosophy

**"Data and rules reign supreme. The rest is implementation detail."**

Business logic becomes tightly coupled with technology stacks, forcing expensive rewrites when platforms evolve. Morphir decouples logic from implementation by capturing it as **data** in a functional IR.

### 2. Four Core Challenges Addressed

1. **Eliminating Rewrites** — Business knowledge survives technology transitions
2. **Consistency Across Systems** — Unified calculations prevent conflicting results
3. **Transparency & Correctness** — Users understand system behavior through tools
4. **Velocity** — Automation reduces development cycles from days to seconds

### 3. Why Functional Programming?

Morphir requires understanding application **intent**, which functional programming excels at expressing:

- **Clarity of Intent**: Specifies *what* without *how*, enabling tool recognition
- **Language Translation**: Convert specifications into Java, SQL, Scala, etc. while preserving meaning
- **Verification**: Pure functions enable compiler-enforced correctness
- **Legacy Integration**: Parse domain-specific languages into Morphir IR

### 4. Key Capabilities

| Capability | Description | Benefit |
|------------|-------------|---------|
| **Translate** | Move logic between languages/platforms | Technology resilience |
| **Visualize** | Turn black-box logic into explanations | Business user engagement |
| **Share** | Consistent interpretation across orgs | Eliminate calculation conflicts |
| **Store** | Version and retrieve historical logic | Audit trails, regulatory compliance |

---

## IR Structure and Type System

### 1. Hierarchical Structure

```
Distribution (output of morphir-elm make)
  └─ Package (modules versioned together)
      └─ Module (container grouping types and values)
          ├─ Types (domain model)
          └─ Values (business logic)
```

**Key Relationships**:
- **Distribution** = Complete package + all dependencies
- **Package** = PackageName + Specification + Definition
- **Module** = QualifiedModuleName + Types + Values
- **Access Control**: Public (Specification) vs Private (Definition)

### 2. Naming System

Four hierarchical levels:

| Level | Structure | Example |
|-------|-----------|---------|
| **Name** | `Seq<string>` | `["add", "function"]` |
| **Path** | `Seq<Name>` | `[["morphir"], ["sdk"], ["int"]]` |
| **QName** | `(ModulePath, Name)` | `([["issues"], ["issue410"]], ["add"])` |
| **FQName** | `(PackagePath, ModulePath, Name)` | `([["morphir"], ["reference"]], [["issues"]], ["add"])` |

**Rationale**: Globally unique identifiers for cross-package references.

### 3. Type System (7 Constructors)

```fsharp
type Type<'a> =
    | Variable of 'a * Name                        // Type variable
    | Reference of 'a * FQName * Type<'a> list     // Fully-qualified reference + generics
    | Tuple of 'a * Type<'a> list                  // Composition
    | Record of 'a * Field<'a> list                // Named fields
    | ExtensibleRecord of 'a * Name * Field<'a> list // "At least these fields"
    | Function of 'a * Type<'a> * Type<'a>         // Curried functions
    | Unit of 'a                                    // Empty type
```

**C# Representation** (morphir-dotnet):
```csharp
public abstract record Type {
    public sealed record Variable(Name Name) : Type;
    public sealed record Reference(FqName TypeName, Seq<Type> TypeParameters) : Type;
    public sealed record Tuple(Seq<Type> ElementTypes) : Type;
    public sealed record Record(Seq<Field> FieldTypes) : Type;
    public sealed record ExtensibleRecord(Name VariableName, Seq<Field> FieldTypes) : Type;
    public sealed record Function(Type ParameterType, Type ReturnType) : Type;
    public sealed record Unit() : Type;
}
```

### 4. Value System (16 Constructors)

**Data Construction**:
- Literal, Constructor, Tuple, List, Record, Unit

**References**:
- Variable, Reference

**Field Access**:
- Field (`record.field`), FieldFunction (`.field`)

**Function Application**:
- Apply, Lambda

**Control Flow**:
- IfThenElse, PatternMatch

**Bindings**:
- LetDefinition, LetRecursion, Destructure

**Updates**:
- UpdateRecord

### 5. SDK Types

**Primitive**: Bool, Int, Float, String, Char, Decimal
**Date/Time**: LocalDate, LocalTime, Month, Instant
**Collections**: List, Set, Dict, Tuple
**Utility**: Maybe (replaces null), Result (error handling), UUID

**Philosophy**: Superset of database schema capabilities, "make illegal states unrepresentable"

---

## Repository Relationships

### 1. morphir-elm → morphir-core

**Relationship**: Implementation → Specification

- **morphir-core** defines core abstractions, philosophy, documentation
- **morphir-elm** implements those concepts as runnable tooling
- morphir-elm provides the Elm package (`finos/morphir-elm`)

### 2. morphir-elm → morphir-dotnet

**Relationship**: JSON Producer → JSON Consumer

**Workflow**:
```
Elm Source Code
     ↓ (morphir-elm make)
morphir-ir.json (format version 3)
     ↓ (morphir-dotnet CLI)
.NET Code / Validation / Tooling
```

**Integration Points**:
- JSON IR format (v1, v2, v3 compatibility)
- Schema validation (embedded JSON schemas)
- Type mapping (Elm types → C#/F# types)

### 3. morphir-core → morphir-dotnet

**Relationship**: Conceptual Guidance → .NET Implementation

- Core principles influence morphir-dotnet architecture
- Documentation clarifies design decisions
- Examples provide reference patterns

### 4. All Repositories → FINOS Ecosystem

**Relationship**: Ecosystem Participation

- Community governance via FINOS
- CLA requirements
- Shared Slack (`#morphir`)
- Common license (Apache 2.0)

---

## Cross-Repository Patterns

### 1. IR Format Versioning

**Pattern**: Manual version management via `formatVersion` field

| Repository | Implementation |
|------------|----------------|
| **morphir-elm** | `src/Morphir/IR/FormatVersion.elm` — Current = 3 |
| **morphir-core** | Documentation of version rationale |
| **morphir-dotnet** | Schema files for v1/v2/v3 + auto-detection |

**Lesson**: No automatic migration; version checking required at load time.

### 2. Backend Code Generation

**Common Architecture** (All backends):

1. **Phase 1**: Load IR JSON → Decode to in-memory `Distribution`
2. **Phase 2**: Transform IR → Target AST → FileMap (path → content dictionary)
3. **Phase 3**: Write FileMap to disk, overwrite existing files

**Pattern Comparison**:

| Backend | Repository | Mapping Strategy |
|---------|------------|------------------|
| Scala | morphir-elm | `mapDistribution` → `mapPackageDefinition` → `mapType/mapValue/mapPattern` |
| TypeScript | morphir-elm | Types only (no values), tagged unions, `encode/decode` functions |
| SpringBoot | morphir-elm | Scala backend + REST API scaffold + Maven pom.xml |
| Cypher | morphir-elm | Query DSL generation for Neo4j graph databases |
| .NET (planned) | morphir-dotnet | F#/C# code generation using Myriad/source generators |

**Shared Concerns**:
- Keyword collision prevention (`scalaKeywords`, `javaObjectMethods`)
- FQName → namespace/package resolution
- Type reference resolution
- Pattern matching translation

### 3. CLI Command Structure

**Pattern**: Consistent command naming across repositories

| Command | morphir-elm | morphir-dotnet |
|---------|-------------|----------------|
| **Make** | `morphir-elm make` (Elm → IR) | *(Not applicable — consumes IR)* |
| **Gen** | `morphir-elm gen --target=Scala` | *(Planned backend generation)* |
| **Develop** | `morphir-elm develop` (web UI) | `Morphir.Live` (Blazor WASM) |
| **Test** | `morphir-elm test` (morphir-tests.json) | `./build.sh --target Test` (TUnit/BDD) |
| **Verify** | *(Not present)* | `morphir ir verify <file>` (JSON schema validation) |

**Lesson**: morphir-elm focuses on authoring/generation; morphir-dotnet focuses on consumption/validation.

### 4. Testing Framework

**morphir-elm Approach**:
- `morphir-tests.json` structure
- FQName-based test organization
- TestCase format: `{ inputs, expectedOutput, description }`
- Visual feedback in Develop UI

**morphir-dotnet Approach**:
- TUnit for unit tests
- Reqnroll (BDD) for acceptance tests
- Verify for snapshot testing
- E2E CLI tests for all executable types (AOT, trimmed, untrimmed)

**Pattern**: Both repositories emphasize comprehensive testing but use platform-appropriate tools.

### 5. Configuration Files

**morphir.json** (morphir-elm):
```json
{
  "name": "My.Package",
  "sourceDirectory": "src",
  "exposedModules": ["Foo", "Bar"]
}
```

**morphir-dotnet Layered Config**:
```
Global (~/.morphir/config.toml)
  ← Workspace (.morphir/config.toml)
    ← User (.morphir/config.user.toml)
      ← CI Profile (auto-applied)
```

**Pattern**: Elm uses single project config; .NET uses multi-layer merge strategy.

### 6. Documentation Approaches

| Repository | Approach | Tooling |
|------------|----------|---------|
| **morphir-core** | Community hub | GitHub Pages, Markdown |
| **morphir-elm** | Elm package docs | elm-doc-preview, package.elm-lang.org |
| **morphir-dotnet** | Professional docs site | Hugo/Docsy, Mermaid diagrams |

**Pattern**: All use documentation-as-code; morphir-dotnet has most sophisticated setup.

### 7. Functional Programming Patterns

**Shared Across All Repositories**:

- **Immutability-first**: Records over classes
- **ADTs**: Make illegal states unrepresentable
- **Exhaustive pattern matching**: Compiler-enforced
- **No nulls**: Use Maybe/Option
- **Railway-oriented programming**: Result types for error handling
- **Pure functions**: Business logic without side effects

**Implementation Differences**:

| Concept | Elm (morphir-elm) | F# (morphir-dotnet) | C# (morphir-dotnet) |
|---------|-------------------|---------------------|---------------------|
| Option | `Maybe a` | `Option<'a>` | `Option<T>` (LanguageExt) |
| Result | `Result e a` | `Result<'a, 'e>` | `Result<T, E>` (LanguageExt) |
| Lists | `List a` | `List<'a>` | `Seq<T>` (LanguageExt) |
| Records | `type alias` | `type ... = { }` | `record` (C# 14) |
| DUs | `type` with `|` | `type = | A | B` | Sealed record inheritance |

---

## Version Compatibility Matrix

### IR Format Versions

| Version | Status | morphir-elm | morphir-dotnet | Breaking Changes |
|---------|--------|-------------|----------------|------------------|
| **v1** | Legacy | Readable | Schema validation | Original format |
| **v2** | Legacy | Readable | Schema validation | Structural changes from v1 |
| **v3** | **Current** | ✅ Generate | ✅ Primary | Refined structure from v2 |

**Auto-Detection**: morphir-dotnet `VersionDetector` analyzes JSON structure to identify version.

**Migration**: No automatic migration functions; manual conversion required.

### Language & Framework Versions

#### morphir-elm

| Dependency | Version | Notes |
|------------|---------|-------|
| Elm | Based on elm/core 1.0.5 | Excludes Debug/Platform/Process/Task |
| Node.js | >= 12.x | For CLI tooling |
| NPM | >= 6.x | Package manager |

#### morphir-dotnet

| Dependency | Version | Notes |
|------------|---------|-------|
| .NET | 10.0 | Current LTS |
| C# | 14 | Records, pattern matching, init-only |
| F# | 9.0 | Computation expressions, pipelines |
| LanguageExt | 5.0.0-beta-38 | Functional programming library |

#### Backend Target Versions

| Backend | Default Version | Configurable | Notes |
|---------|----------------|--------------|-------|
| Scala | 2.11 | Yes (`--target-version`) | Scala 2.12/2.13/3.x supported |
| Java (SpringBoot) | 8+ | Via pom.xml | Maven 3.6.2+ |
| TypeScript | Latest | N/A | ES2015+ |

### Cross-Platform Compatibility

| Platform | morphir-elm | morphir-dotnet |
|----------|-------------|----------------|
| Windows (x64) | ✅ NPM package | ✅ Native AOT, trimmed |
| Linux (x64) | ✅ NPM package | ✅ Native AOT, trimmed |
| Linux (arm64) | ✅ NPM package | ✅ Native AOT, trimmed |
| macOS (Intel) | ✅ NPM package | ✅ Native AOT, trimmed |
| macOS (Apple Silicon) | ✅ NPM package | ✅ Native AOT, trimmed |

---

## Backend Generation Strategies

### 1. Scala Backend (morphir-elm)

**Strengths**:
- Type-safe pattern matching preservation
- Full IR support (types + values)
- JSON codec generation (`--include-codecs`)
- Mature, production-ready

**Limitations**:
- Requires understanding of Scala syntax
- Circe dependency for JSON

**Use Cases**:
- JVM-based microservices
- Spark data processing
- Financial modeling (LCR example)

### 2. SpringBoot Backend (morphir-elm)

**Strengths**:
- Complete REST API scaffold
- Swagger UI integration
- Dropwizard metrics
- Maven project setup

**Limitations**:
- Custom types require at least one argument
- Java 8+ required
- Spring Boot opinionated structure

**Use Cases**:
- Enterprise Java environments
- REST API services
- Legacy Java system integration

### 3. TypeScript Backend (morphir-elm)

**Strengths**:
- Types only (frontend integration)
- Tagged union pattern
- `encode/decode` for JSON
- No runtime dependencies

**Limitations**:
- No function/value generation (yet)
- Type definitions only

**Use Cases**:
- Frontend type safety
- TypeScript/JavaScript interop
- React/Angular/Vue integration

### 4. Cypher Backend (morphir-elm)

**Strengths**:
- Neo4j graph query generation
- Business logic → graph traversals

**Limitations**:
- Graph database specific

**Use Cases**:
- Knowledge graphs
- Relationship modeling
- Graph analytics

### 5. .NET Backend (morphir-dotnet, planned)

**Planned Strengths**:
- F# code generation (idiomatic functional)
- C# code generation (enterprise-friendly)
- Source generators (AOT-compatible)
- Minimal runtime dependencies

**Use Cases**:
- .NET microservices
- Azure Functions
- Blazor frontends

---

## Testing Approaches

### morphir-elm Testing

**Structure**: `morphir-tests.json` co-located with `morphir-ir.json`

```json
{
  "[PackagePath, ModulePath, LocalName]": [
    {
      "inputs": [4, 5],
      "expectedOutput": 9,
      "description": "Add function test"
    }
  ]
}
```

**Execution**:
- CLI: `morphir-elm test` (color-coded pass/fail)
- UI: `morphir-elm develop` (visual editor, live evaluation)

**Backend Test Generation**:
- Scala: `morphir scala-gen --generate-test --test-strategy=ScalaTest`

### morphir-dotnet Testing

**Unit Tests** (TUnit):
```csharp
[Test]
public async Task VerifyIR_ShouldSucceed_WhenIRIsValid() {
    var command = new VerifyIR("valid-ir-v3.json");
    var result = await handler.Handle(command, ct);
    result.IsValid.Should().BeTrue();
}
```

**BDD Tests** (Reqnroll):
```gherkin
Scenario: Verify valid IR v3 file
    Given a valid IR v3 JSON file
    When I validate the IR against schema version "3"
    Then the validation should succeed
```

**Snapshot Tests** (Verify):
```csharp
[Test]
public async Task Distribution_ShouldMatchSnapshot() {
    var dist = await loader.LoadDistribution("morphir-ir.json");
    await Verify(dist).UseDirectory("__snapshots__");
}
```

**E2E Tests**:
```csharp
[Test]
public async Task CLI_VerifyIR_ShouldOutputJSON() {
    var output = await RunCli("ir verify morphir-ir.json --json");
    var result = JsonSerializer.Deserialize<VerifyIRResult>(output);
    result.IsValid.Should().BeTrue();
}
```

### Testing Patterns (Cross-Repository)

1. **Test-Driven Development (TDD)**: Red-Green-Refactor cycle
2. **Property-Based Testing**: FsCheck (morphir-dotnet), QuickCheck-style (possible in Elm)
3. **Integration Tests**: Full IR → Backend pipeline tests
4. **Regression Tests**: Snapshot tests for generated code stability

---

## Domain Modeling Patterns

### 1. Type Precision

**Principle**: Use precise domain types, not generic primitives.

**Bad** (Generic):
```elm
type alias Order = { cusip : String, quantity : Int, price : Float }
```

**Good** (Precise):
```elm
type alias Cusip = String  -- Could be refined further
type alias Quantity = Int
type alias Price = Decimal
type alias Order = { cusip : Cusip, quantity : Quantity, price : Price }
```

**Best** (Units of Measure):
```elm
type Quantity a = Quantity Int
type Price a = Price Decimal
type alias Order = { cusip : Cusip, quantity : Quantity Order, price : Price Order }
```

### 2. Explicit Nullability

**Principle**: Use Maybe/Option instead of null.

```elm
type alias Comment = Maybe String
type alias Order = { cusip : Cusip, comment : Comment }
```

**.NET Equivalent**:
```csharp
public record Order(Cusip Cusip, Option<string> Comment);
```

### 3. Business Alternatives (Custom Types)

**Principle**: Model mutually exclusive states as ADTs.

```elm
type TradeStatus = Open | Closed
type OrderPrice = Market | Limit Decimal
```

**.NET Equivalent**:
```csharp
public abstract record TradeStatus {
    public sealed record Open() : TradeStatus;
    public sealed record Closed() : TradeStatus;
}

public abstract record OrderPrice {
    public sealed record Market() : OrderPrice;
    public sealed record Limit(decimal Price) : OrderPrice;
}
```

### 4. Query-Like Transformations

**Principle**: Elm as extended SQL.

```elm
largeOrders =
    orders
        |> List.filter (\order -> order.quantity > 100)
        |> List.map (\order -> { cusip = order.cusip, value = order.quantity * order.price })
        |> List.sortBy .value
```

**.NET Equivalent** (LINQ):
```csharp
var largeOrders = orders
    .Where(order => order.Quantity > 100)
    .Select(order => new { order.Cusip, Value = order.Quantity * order.Price })
    .OrderBy(x => x.Value);
```

### 5. Railway-Oriented Programming

**Principle**: Use Result for explicit error handling.

```elm
validateOrder : Order -> Result String Order
processOrder : Order -> Result String ProcessedOrder

processValidOrder = validateOrder >> Result.andThen processOrder
```

**.NET Equivalent**:
```csharp
Result<Order, string> ValidateOrder(Order order);
Result<ProcessedOrder, string> ProcessOrder(Order order);

var processValidOrder = order =>
    ValidateOrder(order).Bind(ProcessOrder);
```

### 6. Aggregation Patterns

```elm
totalValue = orders |> List.map (\o -> o.quantity * o.price) |> List.sum
```

**.NET Equivalent**:
```csharp
var totalValue = orders.Sum(o => o.Quantity * o.Price);
```

---

## Integration Points

### 1. Elm → .NET Workflow

```
1. Author business logic in Elm
     ↓
2. morphir-elm make → morphir-ir.json
     ↓
3. Transfer JSON to .NET project
     ↓
4. morphir-dotnet CLI validates IR
     ↓
5. (Future) morphir-dotnet generates C#/F# code
     ↓
6. Integrate generated code into .NET solution
```

### 2. JSON IR Structure

**morphir-ir.json** (format version 3):
```json
{
  "formatVersion": 3,
  "distribution": {
    "packagePath": [["my"], ["package"]],
    "modules": { ... },
    "dependencies": { ... }
  }
}
```

**morphir-dotnet Consumption**:
```csharp
// 1. Load JSON
var json = await File.ReadAllTextAsync("morphir-ir.json");

// 2. Detect version
var version = VersionDetector.DetectVersion(json);

// 3. Validate against schema
var result = await validator.ValidateAsync(json, version.ToString());

// 4. Deserialize to C# types
var options = new JsonSerializerOptions { /* custom converters */ };
var distribution = JsonSerializer.Deserialize<Distribution>(json, options);
```

### 3. Type Mapping Reference

| Morphir IR | Elm | F# | C# (LanguageExt) | TypeScript | Scala |
|------------|-----|----|----|------------|-------|
| Bool | Bool | bool | bool | boolean | Boolean |
| Int | Int | int | long | int64 | Int |
| Float | Float | float | double | float64 | Double |
| String | String | string | string | string | String |
| Decimal | Decimal | decimal | decimal | string | BigDecimal |
| Maybe T | Maybe a | Option<'a> | Option<T> | T \| null | Option[T] |
| Result E V | Result e a | Result<'v, 'e> | Result<V, E> | ["Err", E] \| ["Ok", V] | Either[E, V] |
| List T | List a | List<'a> | Seq<T> | Array<T> | List[T] |
| Dict K V | Dict k v | Map<'k, 'v> | Map<K, V> | Map<K, V> | Map[K, V] |

### 4. Cross-Platform Deployment

**morphir-elm**:
```bash
npm install -g morphir-elm
morphir-elm make
morphir-elm gen --target=Scala
```

**morphir-dotnet**:
```bash
# Via dotnet tool
dotnet tool install -g Morphir.Tool
morphir ir verify morphir-ir.json

# Via native executable
./morphir ir verify morphir-ir.json  # Linux/macOS
morphir.exe ir verify morphir-ir.json  # Windows
```

---

## Quick Reference

### Ecosystem URLs

| Resource | URL |
|----------|-----|
| Official Docs | https://morphir.finos.org |
| morphir-elm GitHub | https://github.com/finos/morphir-elm |
| morphir-core GitHub | https://github.com/finos/morphir |
| morphir-dotnet GitHub | https://github.com/finos/morphir-dotnet |
| Examples | https://github.com/finos/morphir-examples |
| LCR Interactive Demo | https://lcr-interactive.finos.org |
| NPM Package | https://www.npmjs.com/package/morphir-elm |
| Elm Package Docs | https://package.elm-lang.org/packages/finos/morphir-elm/latest/ |
| FINOS Slack | https://finos-lf.slack.com/messages/morphir/ |

### Command Cheat Sheet

```bash
# morphir-elm
morphir-elm make                         # Elm → IR JSON
morphir-elm gen --target=Scala           # IR → Scala
morphir-elm develop                      # Launch web UI
morphir-elm test                         # Run test suite

# morphir-dotnet
morphir ir verify <file>                 # Validate IR JSON
morphir ir verify <file> --json          # JSON output
morphir --version                        # Version info
```

### File Structure Quick Reference

```
# morphir-elm project
myproject/
├── morphir.json          # Project config
├── morphir-ir.json       # Generated IR
├── morphir-tests.json    # Test suite
└── src/                  # Elm sources

# morphir-dotnet project
myproject/
├── .morphir/
│   ├── config.toml       # Workspace config
│   └── cache/            # Workspace cache
└── morphir-ir.json       # IR from morphir-elm
```

### Key Concepts Glossary

| Term | Definition |
|------|------------|
| **Distribution** | Complete package with all dependencies (output of `morphir-elm make`) |
| **IR** | Intermediate Representation — technology-agnostic AST for business logic |
| **FQName** | Fully-Qualified Name — globally unique identifier (PackagePath, ModulePath, LocalName) |
| **SDK** | Standard Development Kit — baseline types and functions (based on elm/core 1.0.5) |
| **Specification** | Public API surface (type signatures only) |
| **Definition** | Complete implementation (including private members) |
| **Backend** | Code generator targeting a specific language (Scala, TypeScript, SpringBoot, etc.) |
| **Decoration** | Metadata annotation system for IR elements |

---

## Architectural Decision Records (Quick List)

See [architectural-decisions.md](../decisionlogs/architectural-decisions.md) for detailed ADRs.

**Key Decisions**:

1. **IR Format = JSON** — Human-readable, widely supported, schema-validatable
2. **Functional-First** — Pure business logic, no side effects in IR
3. **Hierarchical Structure** — Distribution → Package → Module → Types/Values
4. **Manual Versioning** — Explicit formatVersion field, no automatic migration
5. **Multi-Backend Strategy** — One IR, many target languages
6. **Elm as Primary Frontend** — Strong type system, no runtime exceptions
7. **morphir-dotnet AOT Focus** — Trimmed, native executables for performance

---

## Next Steps for Morphir Application Architect Skill

**Phase 1 Completion** (This Document):
- ✅ Comprehensive ecosystem knowledge
- ✅ Cross-repository patterns identified
- ✅ Version compatibility matrix
- ✅ Integration workflows documented

**Phase 2 Tasks**:
1. Language-specific modeling guidance (Elm, F#, C#)
2. Backend generation deep dive
3. Real-world pattern catalog (RegTech, financial modeling)
4. Migration strategies (legacy → Morphir)

**Phase 3 Tasks**:
1. Architecture decision playbooks
2. Anti-patterns and troubleshooting
3. Performance optimization patterns
4. Testing strategy templates

---

**Document Version**: 1.0
**Last Updated**: 2025-12-23
**Maintained By**: Morphir Application Architect Skill (Issue #315)
