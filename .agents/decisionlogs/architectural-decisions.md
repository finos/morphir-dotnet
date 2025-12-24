# Morphir Ecosystem Architectural Decisions

**Created**: 2025-12-23
**Purpose**: Document key architectural decisions across the Morphir ecosystem
**Format**: Lightweight Architecture Decision Records (ADRs)
**Status**: v1.0 (Initial compilation from Task 1.1, Issue #315)

---

## Table of Contents

1. [Cross-Repository Decisions](#cross-repository-decisions)
2. [morphir-elm Decisions](#morphir-elm-decisions)
3. [morphir-core Decisions](#morphir-core-decisions)
4. [morphir-dotnet Decisions](#morphir-dotnet-decisions)
5. [Backend Generation Decisions](#backend-generation-decisions)
6. [Decision Patterns and Themes](#decision-patterns-and-themes)

---

## Cross-Repository Decisions

### ADR-001: JSON as IR Storage Format

**Status**: Accepted ✅
**Date**: Original Morphir design (pre-2020)
**Decision Makers**: Morgan Stanley Morphir team → FINOS community

**Context**:
Morphir IR needs to be stored, transmitted, and processed across different languages and platforms. Need a format that is:
- Human-readable (debugging, code review)
- Widely supported (all major languages have JSON libraries)
- Schema-validatable (ensure correctness)
- Tooling-friendly (linters, formatters, diffing)

**Decision**:
Use **JSON** as the serialization format for Morphir IR, with embedded JSON schemas for each format version.

**Consequences**:
- ✅ **Positive**: Universal tooling support, easy to debug, schema validation via JSON Schema
- ✅ **Positive**: GitHub diffs work well, version control friendly
- ✅ **Positive**: No binary serialization complexity
- ⚠️ **Neutral**: File size larger than binary formats (acceptable for IR)
- ⚠️ **Neutral**: Parsing overhead (mitigated by infrequent IR reads)

**Alternatives Considered**:
- **Protocol Buffers**: Rejected due to tooling complexity, binary format makes debugging harder
- **MessagePack**: Rejected for same reasons as Protobuf
- **XML**: Rejected due to verbosity, declining ecosystem support

**Referenced By**: All repositories
**Related**: ADR-002 (Manual Versioning)

---

### ADR-002: Manual IR Format Versioning

**Status**: Accepted ✅
**Date**: IR v2 → v3 transition (2021-2023)
**Decision Makers**: morphir-elm maintainers

**Context**:
IR schema evolves over time. Breaking changes require version management. Options:
1. Automatic migration functions (v1 → v2 → v3)
2. Manual version checking, no automatic migration
3. Single version, no backward compatibility

**Decision**:
Use **manual version checking** with explicit `formatVersion` field. No automatic migration functions.

**Consequences**:
- ✅ **Positive**: Explicit breaking changes, no hidden migration bugs
- ✅ **Positive**: Consumers control when to upgrade
- ⚠️ **Negative**: Users must manually migrate IR when upgrading
- ⚠️ **Negative**: Requires maintaining schema files for all versions

**Implementation**:
- morphir-elm: `src/Morphir/IR/FormatVersion.elm` — Current = 3
- morphir-dotnet: Embedded schemas (`morphir-ir-v1.json`, `morphir-ir-v2.json`, `morphir-ir-v3.json`)

**Related**: ADR-001 (JSON Format)

---

### ADR-003: Functional Programming as Core Paradigm

**Status**: Accepted ✅
**Date**: Original Morphir design
**Decision Makers**: Morgan Stanley Morphir team

**Context**:
Business logic needs to be:
- Understandable by tools (for translation, visualization)
- Verifiable for correctness
- Free of runtime errors
- Independent of side effects (databases, files, network)

**Decision**:
Use **pure functional programming** for all IR-representable code. No side effects allowed in IR.

**Rationale**:
- Functional code expresses *what* without *how* (enables tooling to recognize patterns)
- Pure functions enable compiler-enforced correctness
- Referential transparency required for cross-language translation
- Pattern matching + ADTs make illegal states unrepresentable

**Consequences**:
- ✅ **Positive**: Type systems catch bugs at compile-time
- ✅ **Positive**: Code generation preserves semantics across languages
- ✅ **Positive**: Compatible with formal verification
- ⚠️ **Negative**: Side effects (I/O, databases) must be at application edges
- ⚠️ **Negative**: Learning curve for imperative programmers

**Referenced By**: All repositories
**Related**: ADR-004 (Elm as Primary Frontend)

---

### ADR-004: Elm as Primary Frontend Language

**Status**: Accepted ✅
**Date**: Original Morphir design
**Decision Makers**: Morgan Stanley Morphir team

**Context**:
Need a language for authoring Morphir models. Requirements:
- Strong type system (catch errors at compile-time)
- Pure functional (no runtime exceptions)
- Familiar to business analysts and developers
- Good tooling ecosystem

**Decision**:
Use **Elm** as the primary language for authoring Morphir models.

**Rationale**:
- No runtime exceptions ("if it compiles, it works")
- SQL-like syntax for queries (familiar to analysts)
- Excellent type inference (minimal annotations)
- Mature ecosystem (elm/core, package registry, formatter, test framework)
- JSON interop built-in

**Consequences**:
- ✅ **Positive**: Business logic guaranteed free of runtime errors
- ✅ **Positive**: Elm's simplicity reduces learning curve vs Haskell/Scala
- ✅ **Positive**: Elm compiler error messages are beginner-friendly
- ⚠️ **Neutral**: Community smaller than JavaScript/TypeScript (acceptable for business logic domain)
- ⚠️ **Negative**: Elm lacks some advanced FP features (type classes, higher-kinded types)

**Alternatives Considered**:
- **Haskell**: Rejected due to complexity, steep learning curve
- **F#**: Rejected to avoid .NET dependency in frontend
- **Scala**: Rejected due to JVM dependency, complexity
- **Bosque**: Experimental alternative (morphir-bosque incubator project)

**Referenced By**: morphir-elm, morphir-core
**Related**: ADR-003 (Functional Programming)

---

### ADR-005: Hierarchical IR Structure (Distribution → Package → Module)

**Status**: Accepted ✅
**Date**: Original Morphir design
**Decision Makers**: Morgan Stanley Morphir team

**Context**:
Need to organize business logic at multiple granularity levels:
- Individual types and functions
- Modules grouping related functionality
- Packages versioned together
- Complete applications with dependencies

**Decision**:
Use **hierarchical structure**: Distribution → Package → Module → Types/Values

**Structure**:
```
Distribution (complete package + dependencies)
  └─ Package (modules versioned together)
      └─ Module (types and values grouped by domain)
          ├─ Types (domain model)
          └─ Values (business logic)
```

**Consequences**:
- ✅ **Positive**: Clear organizational boundaries
- ✅ **Positive**: Dependency management at package level
- ✅ **Positive**: Supports both monorepos and multi-package projects
- ✅ **Positive**: Access control at module/type/value level
- ⚠️ **Neutral**: Requires FQName (Fully-Qualified Name) for global references

**Implementation**:
- morphir-elm: `src/Morphir/IR/Distribution.elm`, `Package.elm`, `Module.elm`
- morphir-dotnet: `Morphir.Core/IR/Distribution.cs` (C#), `Morphir.Models/IR/Classic/Distribution.fs` (F#)

**Related**: ADR-006 (Naming System)

---

### ADR-006: Four-Level Naming System (Name → Path → QName → FQName)

**Status**: Accepted ✅
**Date**: Original Morphir design
**Decision Makers**: Morgan Stanley Morphir team

**Context**:
Need globally unique identifiers for:
- Types, values, modules, packages
- Cross-package references
- Code generation (namespace mapping)

**Decision**:
Use **four-level naming hierarchy**:
1. **Name**: Convention-agnostic representation (segments)
2. **Path**: List of names (hierarchical organization)
3. **QName**: Qualified Name (module path + local name)
4. **FQName**: Fully-Qualified Name (package path + qualified name)

**Example**:
```
Name: ["add", "function"]
Path: [["morphir"], ["sdk"], ["int"]]
QName: ([["sdk"], ["int"]], ["add"])
FQName: ([["morphir"]], [["sdk"], ["int"]], ["add"])
```

**Consequences**:
- ✅ **Positive**: Globally unique identifiers (no collisions)
- ✅ **Positive**: Clear resolution for cross-package references
- ✅ **Positive**: Maps cleanly to namespace/package systems (Java, C#, Scala)
- ⚠️ **Neutral**: Verbosity in JSON representation (acceptable for clarity)

**Implementation**:
- morphir-elm: `src/Morphir/IR/Name.elm`, `Path.elm`, `QName.elm`, `FQName.elm`
- morphir-dotnet: `Morphir.Core/IR/Name.cs`, `Path.cs`, `QName.cs`, `FqName.cs`

**Related**: ADR-005 (Hierarchical Structure)

---

### ADR-007: Public/Private Access Control via Specification/Definition

**Status**: Accepted ✅
**Date**: Original Morphir design
**Decision Makers**: Morgan Stanley Morphir team

**Context**:
Need to distinguish between:
- Public API surface (what consumers can use)
- Private implementation details (internal to package)

**Decision**:
Use **dual representation**:
- **Specification**: Public API only (type signatures, no implementations)
- **Definition**: Complete implementation (public + private)

**Consequences**:
- ✅ **Positive**: Clear API boundaries
- ✅ **Positive**: Supports library development patterns
- ✅ **Positive**: Code generators can respect visibility
- ⚠️ **Neutral**: Requires maintaining both Specification and Definition

**Implementation**:
```elm
type alias Package.Specification ta = { modules : Dict ModuleName (Module.Specification ta) }
type alias Package.Definition ta va = { modules : Dict ModuleName (AccessControlled (Module.Definition ta va)) }
```

**Referenced By**: All repositories

---

## morphir-elm Decisions

### ADR-101: Elm-Based CLI Tooling

**Status**: Accepted ✅
**Date**: morphir-elm v1.0
**Decision Makers**: morphir-elm maintainers

**Context**:
Need CLI tooling for:
- Compiling Elm to IR (`morphir-elm make`)
- Generating code from IR (`morphir-elm gen`)
- Interactive development (`morphir-elm develop`)

**Decision**:
Build CLI using **Node.js + Elm ports** for I/O operations.

**Rationale**:
- Elm handles pure logic (IR processing, code generation)
- Node.js handles side effects (file I/O, HTTP, process spawning)
- Single language for both IR definition and tooling

**Consequences**:
- ✅ **Positive**: Dogfooding (Elm tools written in Elm)
- ✅ **Positive**: Type safety for IR processing
- ⚠️ **Negative**: Node.js dependency (acceptable for CLI)

**Alternatives Considered**:
- **Rust**: Rejected to avoid additional language
- **Haskell**: Rejected due to deployment complexity

**Referenced By**: morphir-elm
**Related**: ADR-004 (Elm as Frontend)

---

### ADR-102: Three-Phase Backend Generation (Load → Transform → Output)

**Status**: Accepted ✅
**Date**: Scala backend implementation
**Decision Makers**: morphir-elm backend developers

**Context**:
Backend code generators need consistent architecture. Process:
1. Load IR JSON
2. Transform IR to target language AST
3. Write files to disk

**Decision**:
Standardize **three-phase architecture** for all backends:
- **Phase 1**: Load `morphir-ir.json` → Decode to `Distribution`
- **Phase 2**: Map `Distribution` → Target AST → FileMap (path → content dictionary)
- **Phase 3**: Write FileMap to disk, overwrite existing files

**Consequences**:
- ✅ **Positive**: Consistent architecture across backends
- ✅ **Positive**: FileMap enables preview before writing
- ✅ **Positive**: Easy to test (compare FileMap output)
- ⚠️ **Neutral**: Requires in-memory representation of all generated files

**Implementation**:
- Scala: `Morphir.Scala.Backend.mapDistribution`
- TypeScript: Similar pattern
- SpringBoot: Scala backend + additional scaffolding

**Referenced By**: morphir-elm
**Related**: ADR-103 (FileMap Pattern)

---

### ADR-103: FileMap Pattern for Generated Code

**Status**: Accepted ✅
**Date**: Scala backend implementation
**Decision Makers**: morphir-elm backend developers

**Context**:
Need in-memory representation of generated files before writing to disk.

**Decision**:
Use **FileMap**: Dictionary of file path → file content.

**Type**:
```elm
type alias FileMap = Dict Path String
```

**Consequences**:
- ✅ **Positive**: Preview generated code before writing
- ✅ **Positive**: Easy to test (no file I/O in tests)
- ✅ **Positive**: Can compute diffs before overwriting
- ⚠️ **Neutral**: Large projects may consume significant memory (acceptable)

**Referenced By**: All backend generators in morphir-elm
**Related**: ADR-102 (Three-Phase Generation)

---

### ADR-104: Keyword Collision Prevention

**Status**: Accepted ✅
**Date**: Scala backend implementation
**Decision Makers**: morphir-elm backend developers

**Context**:
Generated code may produce identifiers that conflict with target language keywords.

**Decision**:
Maintain **keyword collision sets** for each backend and escape/transform conflicting identifiers.

**Implementation**:
```elm
scalaKeywords : Set String
scalaKeywords = Set.fromList ["abstract", "case", "catch", "class", ...]

javaObjectMethods : Set String
javaObjectMethods = Set.fromList ["clone", "equals", "finalize", "getClass", ...]
```

**Consequences**:
- ✅ **Positive**: Generated code compiles without manual fixes
- ✅ **Positive**: Prevents subtle bugs from keyword shadowing
- ⚠️ **Neutral**: Requires maintaining keyword lists per backend

**Referenced By**: Scala, SpringBoot backends
**Related**: ADR-102 (Backend Generation)

---

### ADR-105: SDK Based on elm/core 1.0.5 (Excluding Side Effects)

**Status**: Accepted ✅
**Date**: morphir-elm SDK design
**Decision Makers**: morphir-elm maintainers

**Context**:
Need standard library for business logic. Requirements:
- Familiar to Elm developers
- No side effects (I/O, processes, tasks)
- Extensible for domain-specific types (LocalDate, Decimal, etc.)

**Decision**:
Base **Morphir.SDK** on `elm/core 1.0.5`, excluding:
- `Debug` (side effects)
- `Platform` (runtime, not business logic)
- `Process` (concurrency, side effects)
- `Task` (async I/O)

Add domain-specific extensions:
- `Morphir.SDK.LocalDate`, `LocalTime`, `Instant`
- `Morphir.SDK.Decimal`
- `Morphir.SDK.Aggregate`, `Rule`, `StatefulApp`

**Consequences**:
- ✅ **Positive**: Elm developers can use familiar functions
- ✅ **Positive**: Clear baseline for backend implementers
- ✅ **Positive**: Extensions support financial/business modeling
- ⚠️ **Negative**: Users cannot use Debug (acceptable for production code)

**Referenced By**: morphir-elm
**Related**: ADR-004 (Elm as Frontend)

---

## morphir-core Decisions

### ADR-201: Core Repository as Specification Hub

**Status**: Accepted ✅
**Date**: FINOS contribution (2019)
**Decision Makers**: FINOS Morphir working group

**Context**:
Need a central repository for:
- Core Morphir concepts and philosophy
- Documentation for all implementations
- Community resources
- Examples and reference implementations

**Decision**:
Use **morphir (core)** repository as specification and documentation hub, distinct from implementation repositories.

**Consequences**:
- ✅ **Positive**: Single source of truth for concepts
- ✅ **Positive**: Language-agnostic documentation
- ✅ **Positive**: Clear separation of spec vs implementation
- ⚠️ **Neutral**: Requires synchronization across repositories

**Referenced By**: All Morphir repositories
**Related**: ADR-202 (Documentation Strategy)

---

### ADR-202: Hugo/Docsy for Documentation Site

**Status**: Accepted ✅ (morphir-dotnet); Evolving 🔄 (morphir-core)
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need professional documentation site with:
- Easy authoring (Markdown)
- Versioning support
- Search functionality
- Responsive design
- Community contribution workflow

**Decision** (morphir-dotnet):
Use **Hugo static site generator** with **Docsy theme**.

**Consequences**:
- ✅ **Positive**: Professional appearance, excellent mobile support
- ✅ **Positive**: Fast builds, no server-side dependencies
- ✅ **Positive**: GitHub Pages deployment
- ✅ **Positive**: Mermaid diagram support
- ⚠️ **Negative**: Hugo installation required for local preview

**Note**: morphir-core uses simpler GitHub Pages setup (may adopt Hugo/Docsy in future).

**Referenced By**: morphir-dotnet
**Related**: ADR-201 (Core as Hub)

---

## morphir-dotnet Decisions

### ADR-301: Dual IR Implementation (C# Modern + F# Classic)

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.1
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need IR representation in .NET. Options:
1. C# only
2. F# only
3. Both (dual implementation)

**Decision**:
Maintain **both** implementations:
- **C# IR**: Modern, primary (Morphir.Core) — records, LanguageExt, source generators
- **F# IR**: Classic, legacy (Morphir.Models) — traditional F# DUs, maintains compatibility

**Rationale**:
- C# IR: Better AOT support, mainstream .NET developers, source generator ecosystem
- F# IR: Idiomatic functional code, interop with Elm patterns, existing codebase

**Consequences**:
- ✅ **Positive**: C# and F# developers can use idiomatic representations
- ✅ **Positive**: Gradual migration path (F# → C#)
- ⚠️ **Negative**: Maintenance overhead for two implementations
- ⚠️ **Negative**: Need interop layer (minimal, mostly serialization)

**Migration Path**: F# Classic is stable; new features added to C# Modern first.

**Referenced By**: morphir-dotnet
**Related**: ADR-302 (LanguageExt for C#)

---

### ADR-302: LanguageExt for Functional Programming in C#

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.1
**Decision Makers**: morphir-dotnet maintainers

**Context**:
C# lacks native functional programming primitives (Option, Result, immutable collections). Options:
1. Build custom FP library
2. Use LanguageExt
3. Use Functional.Maybe / other alternatives

**Decision**:
Use **LanguageExt** v5.0 for functional programming in C#.

**Rationale**:
- Comprehensive FP library (Option, Result, Either, Seq, Map, etc.)
- Active maintenance, large community
- LINQ integration
- Source generator support (v5.0+)

**Consequences**:
- ✅ **Positive**: Rich FP ecosystem without reinventing the wheel
- ✅ **Positive**: Familiar to C# FP community
- ⚠️ **Negative**: Dependency on third-party library (acceptable, well-maintained)
- ⚠️ **Neutral**: v5.0 currently in beta (waiting for stable release before v1.0)

**Alternatives Considered**:
- **OneOf**: Rejected (limited scope, no collections)
- **CSharpFunctionalExtensions**: Rejected (smaller ecosystem)
- **Custom library**: Rejected (unnecessary duplication)

**Referenced By**: morphir-dotnet
**Related**: ADR-301 (Dual IR)

---

### ADR-303: AOT and Trimming as First-Class Concerns

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.2
**Decision Makers**: morphir-dotnet maintainers

**Context**:
.NET Native AOT and trimming enable:
- Small, fast executables (5-15 MB vs 60+ MB)
- No runtime dependencies
- Faster startup times

**Decision**:
Design all morphir-dotnet components to be **AOT-compatible** and **trimming-friendly** from the start.

**Implementation**:
- Use source generators instead of reflection (JSON serialization)
- Myriad code generators for F# (no reflection)
- ILLink descriptors for unavoidable dynamic code
- E2E tests for AOT, trimmed, untrimmed executables

**Consequences**:
- ✅ **Positive**: Small, self-contained executables (ideal for CLI tools)
- ✅ **Positive**: No .NET runtime installation required
- ✅ **Positive**: Competitive with Go/Rust for deployment size
- ⚠️ **Negative**: Restricts use of reflection (mitigated by source generators)
- ⚠️ **Negative**: Trimming analysis warnings (addressed with ILLink descriptors)

**Target Sizes**:
- 5-8 MB (minimal CLI)
- 8-12 MB (feature-rich CLI)
- 10-15 MB (with UI/web features)

**Referenced By**: morphir-dotnet
**Related**: ADR-307 (Myriad Code Generation)

---

### ADR-304: TUnit as Test Framework (Replacing xUnit)

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.3
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need modern .NET test framework. Requirements:
- Async-first (all tests async by default)
- Fast execution
- Good IDE support
- Source generator extensibility

**Decision**:
Adopt **TUnit** as primary test framework, replacing xUnit.

**Rationale**:
- Modern async-first design
- Source generator-based (fast compilation)
- Better parameterized test syntax
- Dependency injection support
- Active development (community momentum)

**Consequences**:
- ✅ **Positive**: Cleaner test syntax for async tests
- ✅ **Positive**: Better performance than xUnit/NUnit
- ⚠️ **Neutral**: TUnit is newer (less mature than xUnit)
- ⚠️ **Neutral**: Requires migration from existing xUnit tests (acceptable, one-time cost)

**Migration**: Completed in v0.3-rc.2

**Referenced By**: morphir-dotnet
**Related**: ADR-305 (Reqnroll for BDD)

---

### ADR-305: Reqnroll for BDD Testing

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.2
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need BDD framework for acceptance testing. SpecFlow is now commercial.

**Decision**:
Use **Reqnroll** (open-source SpecFlow successor) for BDD testing.

**Rationale**:
- Community fork of SpecFlow (same syntax, open-source)
- Gherkin feature files
- Strong .NET integration
- Active development

**Consequences**:
- ✅ **Positive**: Familiar syntax for SpecFlow users
- ✅ **Positive**: Open-source, Apache 2.0 license
- ✅ **Positive**: Supports TUnit, NUnit, xUnit, MSTest
- ⚠️ **Neutral**: Smaller community than SpecFlow (acceptable, growing)

**Usage**: CLI integration tests, feature acceptance tests

**Referenced By**: morphir-dotnet
**Related**: ADR-304 (TUnit), ADR-306 (Verify)

---

### ADR-306: Verify for Snapshot Testing

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.2
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need snapshot testing for:
- JSON serialization output
- Generated code output
- Complex object comparisons

**Decision**:
Use **Verify** for snapshot testing.

**Rationale**:
- Excellent .NET integration (Verify.TUnit, Verify.Expecto)
- Automatic snapshot approval workflow
- Diff visualization (via DiffEngine)
- Supports all major test frameworks

**Consequences**:
- ✅ **Positive**: Easy to write tests for complex outputs
- ✅ **Positive**: Catch regressions in generated code
- ✅ **Positive**: Human-readable snapshot files (committed to git)
- ⚠️ **Neutral**: Requires manual approval of snapshot changes (acceptable, part of workflow)

**Usage**: JSON codec tests, code generation tests, IR transformation tests

**Referenced By**: morphir-dotnet
**Related**: ADR-304 (TUnit), ADR-305 (Reqnroll)

---

### ADR-307: Myriad for F# Code Generation

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.2
**Decision Makers**: morphir-dotnet maintainers

**Context**:
F# lacks source generators (C# feature). Need compile-time code generation for:
- JSON codecs (reflection-free)
- Visitor patterns
- Lenses (functional updates)

**Decision**:
Use **Myriad** as F# code generator framework.

**Rationale**:
- MSBuild integration (runs during compilation)
- Plugin-based architecture
- F# quotations for metaprogramming
- AOT-compatible output (no reflection)

**Consequences**:
- ✅ **Positive**: Compile-time code generation (no runtime cost)
- ✅ **Positive**: AOT-compatible, trimming-friendly
- ✅ **Positive**: Reduces boilerplate significantly
- ⚠️ **Negative**: Myriad less mature than C# source generators (acceptable, actively maintained)

**Plugins Created**:
- `JsonCodecGenerator` — Reflection-free JSON serialization
- `VisitorGenerator` — Type-safe visitor patterns
- `LensGenerator` — Functional lenses for nested updates
- `ActivePatternGenerator` — Active patterns from DUs
- `BuilderGenerator` — Fluent builder APIs

**Referenced By**: morphir-dotnet
**Related**: ADR-303 (AOT), ADR-301 (Dual IR)

---

### ADR-308: Vertical Slice Architecture for CLI Features

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.3
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need to organize CLI features. Traditional layered architecture (Controllers, Services, Repositories) scatters feature code across layers.

**Decision**:
Use **Vertical Slice Architecture** for CLI features.

**Structure**:
```
Features/
└── VerifyIR/
    ├── VerifyIR.cs           # Command, Result, Handler, Validator (all in one file)
    ├── VersionDetector.cs    # Feature-specific logic
    └── (tests co-located in test project Features/VerifyIR/)
```

**Rationale**:
- Feature cohesion (all related code in one place)
- Easy to add/remove features
- Clear boundaries, minimal coupling
- Matches command/query pattern (CQRS-lite)

**Consequences**:
- ✅ **Positive**: Easy to find all code for a feature
- ✅ **Positive**: Adding features doesn't impact others
- ✅ **Positive**: Testing is straightforward (test one slice at a time)
- ⚠️ **Neutral**: Requires discipline to avoid cross-slice dependencies

**Referenced By**: morphir-dotnet
**Related**: ADR-309 (WolverineFx)

---

### ADR-309: WolverineFx for Command/Query Handling

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.3
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need command/query handling for CLI. Requirements:
- Request-response pattern
- Dependency injection
- FluentValidation integration
- Simple, low-ceremony

**Decision**:
Use **WolverineFx (Foundatio.Mediator)** for command/query handling.

**Rationale**:
- Lightweight (no heavyweight messaging infrastructure)
- Dependency injection built-in
- FluentValidation support
- Request-response pattern (ideal for CLI)

**Consequences**:
- ✅ **Positive**: Simple programming model
- ✅ **Positive**: Dependency injection for handlers
- ✅ **Positive**: Testable (mock dependencies easily)
- ⚠️ **Neutral**: Requires handler registration (minimal boilerplate)

**Usage**:
```csharp
var result = await mediator.InvokeAsync<VerifyIR, VerifyIRResult>(command, ct);
```

**Referenced By**: morphir-dotnet
**Related**: ADR-308 (Vertical Slice)

---

### ADR-310: CLI Logging to Stderr Only (Stdout = Data)

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.3
**Decision Makers**: morphir-dotnet maintainers

**Context**:
CLI tools should follow Unix philosophy:
- Stdout = command output (data)
- Stderr = diagnostics, logs, progress

**Decision**:
Configure **Serilog to write all logs to stderr**. Stdout reserved for command output (JSON, results).

**Implementation**:
```csharp
LoggerConfiguration()
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
```

**Consequences**:
- ✅ **Positive**: Enables piping (`morphir ir verify file.json --json | jq`)
- ✅ **Positive**: Scriptable (scripts can parse stdout without filtering logs)
- ✅ **Positive**: Follows Unix conventions
- ⚠️ **Neutral**: Requires discipline (no `Console.WriteLine` for logs)

**Test**:
```bash
morphir ir verify morphir-ir.json --json | jq .isValid
# Should output: true
# (No log noise in stdout)
```

**Referenced By**: morphir-dotnet
**Related**: ADR-308 (Vertical Slice)

---

### ADR-311: Layered Configuration System with CI Auto-Detection

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.3
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need configuration system that supports:
- Global defaults
- Workspace overrides
- User-specific settings
- CI-specific behavior (no interactive prompts, caching strategy)

**Decision**:
Implement **layered configuration system** with four layers (lowest to highest precedence):
1. Global profile (`~/.morphir/config.toml`)
2. Workspace profile (`<workspace>/.morphir/config.toml`)
3. User overrides (`<workspace>/.morphir/config.user.toml`)
4. CI profile (auto-applied when CI environment detected)

**CI Detection**:
- Environment variables: `CI`, `GITHUB_ACTIONS`, `GITLAB_CI`, etc.
- Mode: `On` (always), `Off` (never), `Auto` (default, environment-based)

**Consequences**:
- ✅ **Positive**: Flexible configuration for different contexts
- ✅ **Positive**: CI behavior auto-applied (no manual flags)
- ✅ **Positive**: User overrides git-ignored (no accidental commits)
- ⚠️ **Neutral**: Requires understanding layer precedence

**Referenced By**: morphir-dotnet (Morphir.Configuration project)
**Related**: ADR-303 (AOT)

---

### ADR-312: Morphir.Live (Blazor WASM Playground)

**Status**: Accepted ✅
**Date**: morphir-dotnet v0.3
**Decision Makers**: morphir-dotnet maintainers

**Context**:
Need interactive playground for:
- Learning Morphir IR
- Experimenting with models
- Real-time validation
- Browser-based (no installation)

**Decision**:
Build **Morphir.Live** using Blazor WebAssembly + Fun.Blazor + MudBlazor.

**Rationale**:
- Blazor WASM runs in browser (no server required)
- Fun.Blazor provides F# DSL for components
- MudBlazor provides Material Design UI components
- Can reuse Morphir.Core/Morphir.Models libraries

**Consequences**:
- ✅ **Positive**: No server infrastructure cost (GitHub Pages)
- ✅ **Positive**: F# developers use familiar syntax
- ✅ **Positive**: Material Design UI (professional appearance)
- ⚠️ **Negative**: Blazor WASM download size (acceptable, ~2MB compressed)

**Status**: Early development (UI mockup complete, IR integration pending)

**Referenced By**: morphir-dotnet
**Related**: ADR-301 (Dual IR)

---

## Backend Generation Decisions

### ADR-401: Scala as Default Backend

**Status**: Accepted ✅
**Date**: morphir-elm v1.0
**Decision Makers**: Morgan Stanley Morphir team → morphir-elm maintainers

**Context**:
Need default backend for code generation. Requirements:
- JVM compatibility (enterprise environments)
- Functional programming support
- Strong type system
- Pattern matching

**Decision**:
Use **Scala** as default backend (`morphir-elm gen` without `--target` flag).

**Consequences**:
- ✅ **Positive**: Enterprise-friendly (JVM ecosystem)
- ✅ **Positive**: Functional + OOP hybrid
- ✅ **Positive**: Pattern matching translates cleanly
- ⚠️ **Neutral**: Scala learning curve (mitigated by generated code)

**Referenced By**: morphir-elm
**Related**: ADR-402 (SpringBoot Backend)

---

### ADR-402: SpringBoot Backend for REST APIs

**Status**: Accepted ✅
**Date**: morphir-elm v2.0
**Decision Makers**: morphir-elm maintainers

**Context**:
Need REST API generation from business logic. SpringBoot is enterprise standard.

**Decision**:
Generate **SpringBoot** projects from Morphir IR (`morphir-elm gen --target=SpringBoot`).

**Implementation**:
- Scala backend + additional scaffolding
- Maven pom.xml generation
- Jackson serialization
- Swagger UI integration
- Dropwizard metrics

**Consequences**:
- ✅ **Positive**: Enterprise Java ecosystem integration
- ✅ **Positive**: Complete runnable project (no manual setup)
- ⚠️ **Negative**: Custom types require at least one argument (current limitation)

**Use Case**: Business logic → REST API microservice

**Referenced By**: morphir-elm
**Related**: ADR-401 (Scala Backend)

---

### ADR-403: TypeScript Backend (Types Only)

**Status**: Accepted ✅ (Types); Future 🔄 (Values)
**Date**: morphir-elm v2.5
**Decision Makers**: morphir-elm maintainers

**Context**:
Frontend applications need TypeScript types for Morphir models.

**Decision**:
Generate **TypeScript types** from Morphir IR (`morphir-elm gen --target=TypeScript`).

**Current Scope**: Types only (no function/value generation yet)

**Consequences**:
- ✅ **Positive**: Type safety in TypeScript frontends
- ✅ **Positive**: encode/decode functions for JSON interop
- ⚠️ **Limitation**: No function generation (planned future feature)

**Use Case**: React/Angular/Vue applications consuming Morphir IR

**Referenced By**: morphir-elm
**Related**: ADR-401 (Scala Backend)

---

## Decision Patterns and Themes

### Theme 1: Technology Resilience

**Pattern**: Decisions prioritize long-term portability over short-term convenience.

**Examples**:
- JSON IR (ADR-001) — Universal format, not binary
- Functional programming (ADR-003) — Semantics-preserving translation
- Multi-backend strategy (ADR-401, 402, 403) — One model, many targets

**Principle**: Business logic should survive technology changes.

---

### Theme 2: Type Safety and Correctness

**Pattern**: Leverage strong type systems to catch errors at compile-time.

**Examples**:
- Elm as frontend (ADR-004) — No runtime exceptions
- LanguageExt for C# (ADR-302) — Option/Result types
- Exhaustive pattern matching (ADR-003) — Compiler-enforced completeness

**Principle**: "If it compiles, it works" (or at least, it's type-safe).

---

### Theme 3: Explicitness Over Convenience

**Pattern**: Prefer explicit, clear code over implicit magic.

**Examples**:
- Manual versioning (ADR-002) — Explicit breaking changes
- FQName system (ADR-006) — Globally unique, verbose but unambiguous
- Specification/Definition duality (ADR-007) — Clear API boundaries

**Principle**: Clarity aids tooling, maintenance, and cross-team collaboration.

---

### Theme 4: Functional-First Architecture

**Pattern**: Pure functions for business logic, effects at edges.

**Examples**:
- Functional programming paradigm (ADR-003)
- No side effects in IR (ADR-105)
- Railway-oriented programming (domain modeling patterns)

**Principle**: Referential transparency enables verification, translation, and testing.

---

### Theme 5: Tooling and Developer Experience

**Pattern**: Invest in tooling to reduce friction and enable automation.

**Examples**:
- CLI tooling (ADR-101, morphir-dotnet CLI)
- Interactive playground (ADR-312: Morphir.Live)
- BDD testing (ADR-305: Reqnroll)
- Snapshot testing (ADR-306: Verify)

**Principle**: Good tools multiply developer productivity.

---

### Theme 6: AOT and Performance Optimization (morphir-dotnet)

**Pattern**: Performance and deployment size are first-class concerns.

**Examples**:
- AOT/trimming (ADR-303)
- Source generators (ADR-302, 307)
- CLI logging to stderr (ADR-310)

**Principle**: Compete with Go/Rust for deployment characteristics while maintaining C#/F# productivity.

---

### Theme 7: Community and Ecosystem

**Pattern**: Decisions favor open-source, community-driven tools.

**Examples**:
- FINOS contribution (ADR-201)
- Reqnroll over SpecFlow (ADR-305)
- Apache 2.0 license (all repositories)

**Principle**: Open collaboration accelerates innovation and adoption.

---

## Decision Workflow

### How to Propose an ADR

1. **Identify decision**: What needs to be decided? What are the alternatives?
2. **Research**: Gather context, pros/cons, community feedback
3. **Draft ADR**: Use template (Status, Date, Decision Makers, Context, Decision, Consequences, Alternatives)
4. **Discuss**: GitHub issue, community meeting, Slack
5. **Decide**: Consensus or maintainer decision
6. **Document**: Add to this file, reference in code/docs
7. **Review**: Periodically revisit decisions (Accepted → Superseded if changed)

### ADR Statuses

- **Proposed** 🔄 — Under discussion
- **Accepted** ✅ — Active, implemented
- **Superseded** 🔁 — Replaced by newer ADR
- **Deprecated** ⚠️ — No longer recommended, but not yet removed
- **Rejected** ❌ — Decided against, documented for historical context

---

**Document Version**: 1.0
**Last Updated**: 2025-12-23
**Maintained By**: Morphir Application Architect Skill (Issue #315)
**Next Review**: After Phase 2 completion (Issue #314)
