# ADR-026: Pluggable Pipeline Architecture for IR Transformations

**Date**: 2025-12-26
**Status**: Proposed
**Context**: Task 2.2 - Pipeline Architecture Design (Issue #320)
**Related ADRs**: ADR-001 (Dual IR), ADR-003 (Immutable IR)

## Context and Problem Statement

Morphir-dotnet currently lacks a standardized, composable architecture for IR transformations. Transformations are implemented ad-hoc, making it difficult to:

1. **Compose Transformations**: No standard way to chain multiple transformations
2. **Accumulate Diagnostics**: Current `Result<T, Error>` model forces early exit on first error
3. **Share Context**: No mechanism for plugins to share metadata across pipeline
4. **Version Migration**: IR version upgrades require manual, error-prone code
5. **Format Bridges**: Converting between Classic IR (F#) and Modern IR (C#) is manual
6. **Extensibility**: Adding new transformations requires modifying core codebase

**Goal**: Design a pluggable pipeline architecture inspired by unified.js that enables composable, type-safe IR transformations with comprehensive diagnostics.

## Decision Drivers

### Technical Requirements
- **Type Safety**: Compile-time verification of plugin types
- **Immutability**: No mutation of processor or file state
- **Diagnostics**: Accumulate errors/warnings without early exit
- **Performance**: Minimal overhead for plugin composition
- **Ergonomics**: F#-idiomatic API with computation expressions

### Architectural Principles
- **Separation of Concerns**: Parse → Transform → Stringify phases
- **Open/Closed**: Open for extension (plugins), closed for modification (core)
- **Single Responsibility**: Each plugin does one transformation
- **Dependency Inversion**: Core depends on plugin abstractions, not concrete implementations

### Compatibility Constraints
- **Dual IR Support**: Must work with both Classic IR (F#) and Modern IR (C#)
- **Backward Compatible**: Existing code should continue to work
- **AOT Friendly**: No reflection, source generation where needed
- **Cross-Platform**: F# and C# consumers

## Considered Options

### Option 1: Middleware Pattern (ASP.NET Core Style)

**Pros**:
- Familiar to .NET developers
- Mature pattern with known implementations
- Good tooling support

**Cons**:
- Mutable RequestDelegate chain
- Not functional-first
- Designed for HTTP, not AST transformations
- Difficult to freeze/share pipelines

### Option 2: Free Monad Pattern

**Pros**:
- Pure functional approach
- Composable by design
- Separates description from interpretation

**Cons**:
- Complex for most developers
- Performance overhead (interpreter pattern)
- Requires advanced FP knowledge
- Overkill for this use case

### Option 3: Unified.js-Inspired Processor Pattern ✅ **SELECTED**

**Pros**:
- Proven pattern in similar domain (syntax tree transformations)
- Immutable processor with frozen/unfrozen states
- Clean separation: Parse → Transform → Stringify
- Plugin composition with attacher/transformer pattern
- VFile-style diagnostic accumulation
- Computation expression support for ergonomics

**Cons**:
- Requires porting from JavaScript idioms
- Need to adapt for .NET type system
- New pattern for morphir-dotnet

**Why Selected**: Best balance of functional principles, practical ergonomics, and proven success in similar domain (unified.js ecosystem has 100M+ downloads/month).

### Option 4: Visitor Pattern with Handlers

**Pros**:
- Classic OOP pattern
- Familiar to most developers

**Cons**:
- Tight coupling to IR structure
- Difficult to compose handlers
- No standard diagnostic accumulation
- Mutation or complex immutable updates

## Decision

**Adopt the Unified.js-inspired Processor Pattern** with the following adaptations for .NET/F#:

### 1. MorphirProcessor

**Immutable processor with three phases**:

```fsharp
type MorphirProcessor = {
    Parsers: Parser list
    Plugins: Plugin list
    Compilers: Compiler list
    Frozen: bool
    Data: ImmutableDictionary<string, obj>
}

type Parser = MorphirFile -> Result<IRNode, string>
type Compiler = IRNode -> MorphirFile -> MorphirFile
```

**Key Properties**:
- **Frozen State**: Immutable template for creating variant processors
- **Plugin List**: Sequential transformation chain
- **Data Dictionary**: Shared metadata across plugins

### 2. Plugin Interface

**Dual-function plugin (Configure + Transform)**:

```fsharp
type Plugin = {
    Name: string
    Configure: MorphirProcessor -> MorphirProcessor
    Transform: IRNode -> MorphirFile -> (IRNode option * MorphirFile)
}
```

**F# Module Style**:
```fsharp
module ValidateIRPlugin =
    let name = "validate-ir"

    let configure (proc: MorphirProcessor) =
        // Add metadata, configure processor
        proc

    let transform (node: IRNode) (file: MorphirFile) =
        // Validate node, accumulate diagnostics
        Some node, file.Info("Validation complete")

    let plugin = {
        Name = name
        Configure = configure
        Transform = transform
    }
```

**C# Interface**:
```csharp
public interface IPlugin
{
    string Name { get; }
    MorphirProcessor Configure(MorphirProcessor processor);
    (IRNode? Node, MorphirFile File) Transform(IRNode node, MorphirFile file);
}
```

### 3. MorphirFile (VFile Pattern)

**Diagnostic accumulation container**:

```fsharp
type MessageSeverity = Info | Warning | Error | Fatal

type MorphirMessage = {
    Severity: MessageSeverity
    Message: string
    Position: SourceRange option
    Source: string option
    RuleId: string option
}

type MorphirFile = {
    Content: IRNode option
    Path: string option
    History: string list
    Messages: MorphirMessage list
    Data: ImmutableDictionary<string, obj>
}
```

**API Methods**:
```fsharp
module MorphirFile =
    let info (msg: string) (file: MorphirFile) : MorphirFile
    let warn (msg: string) (pos: SourceRange option) (file: MorphirFile) : MorphirFile
    let error (msg: string) (pos: SourceRange option) (file: MorphirFile) : MorphirFile
    let fail (msg: string) (pos: SourceRange option) (file: MorphirFile) : MorphirFile

    let hasErrors (file: MorphirFile) : bool
    let hasFatals (file: MorphirFile) : bool
```

### 4. Pipeline Execution

**Three-phase execution**:

```fsharp
member processor.Process(file: MorphirFile) : MorphirFile =
    // Phase 1: Parse
    let parsed =
        processor.Parsers
        |> List.fold (fun f parser ->
            match parser f with
            | Ok node -> { f with Content = Some node }
            | Error err -> f.Error(err, None)
        ) file

    // Phase 2: Transform
    let transformed =
        match parsed.Content with
        | Some node ->
            processor.Plugins
            |> List.fold (fun (n, f) plugin ->
                plugin.Transform n f
            ) (node, parsed)
            |> fun (node, file) -> { file with Content = node }
        | None -> parsed

    // Phase 3: Stringify
    let compiled =
        processor.Compilers
        |> List.fold (fun f compiler ->
            match f.Content with
            | Some node -> compiler node f
            | None -> f
        ) transformed

    compiled
```

### 5. Computation Expression Builders

**Pipeline Builder**:
```fsharp
let irProcessor = pipeline {
    parse irJsonParser
    plugin validateIRPlugin
    plugin normalizeTypesPlugin
    plugin optimizePlugin
    stringify irJsonSerializer
    freeze
}
```

**Transformer Builder**:
```fsharp
let validate node file =
    transformer {
        info "Validating IR"
        let! valid = checkValidity node
        if not valid then
            error "Invalid IR structure"
        return node
    }
```

## Consequences

### Positive

✅ **Composability**: Plugins can be freely composed and reordered
✅ **Diagnostic Accumulation**: All errors/warnings collected, not just first
✅ **Type Safety**: Compile-time verification of plugin types
✅ **Immutability**: No mutation, safe to share frozen processors
✅ **Extensibility**: New plugins without modifying core
✅ **Bridge Pattern**: IR version/format migration as first-class plugins
✅ **F# Ergonomics**: Computation expressions reduce boilerplate
✅ **Testing**: Easy to test individual plugins in isolation

### Negative

⚠️ **Learning Curve**: New pattern for morphir-dotnet developers
⚠️ **Verbosity**: Plugin structure more verbose than simple functions
⚠️ **Performance**: List fold overhead (mitigated: minimal for typical pipelines)
⚠️ **Dual Maintenance**: F# and C# implementations need sync

### Neutral

➖ **Breaking Change**: New API, but existing code continues to work
➖ **Migration Effort**: Existing transformations can be gradually ported

## Implementation Plan

### Phase 1: Core Infrastructure (Week 1)
- [ ] `MorphirFile` record with `Info`/`Warn`/`Error`/`Fail` API
- [ ] `MessageSeverity` and `MorphirMessage` types
- [ ] `SourceRange` for position tracking
- [ ] Unit tests for diagnostic accumulation

**Deliverables**:
- `Morphir.IR.File.fs` (F#)
- `Morphir.IR.File.cs` (C#)
- Tests: `Morphir.IR.File.Tests.fs`

### Phase 2: Processor Pattern (Week 2)
- [ ] `MorphirProcessor` record with `Parsers`/`Plugins`/`Compilers`
- [ ] `Plugin` record/interface
- [ ] `Frozen`/`Unfrozen` state management
- [ ] `Process` method with three-phase execution
- [ ] Pipeline Builder computation expression
- [ ] Unit tests for processor composition

**Deliverables**:
- `Morphir.IR.Processor.fs` (F#)
- `Morphir.IR.IPlugin.cs` (C#)
- `Morphir.IR.PipelineBuilder.fs`
- Tests: `Morphir.IR.Processor.Tests.fs`

### Phase 3: Computation Expression Builders (Week 3)
- [ ] Transformer Builder (diagnostic threading)
- [ ] Visitor Builder (declarative traversal)
- [ ] Documentation and examples
- [ ] Integration tests

**Deliverables**:
- `Morphir.IR.TransformerBuilder.fs`
- `Morphir.IR.VisitorBuilder.fs`
- Examples: `examples/pipeline-examples.fsx`

### Phase 4: Bridge Plugins (Week 4)
- [ ] Classic IR ↔ Modern IR bridge
- [ ] IR v2 → v3 migration bridge
- [ ] Format bridges (IR → TypeScript, IR → Scala)
- [ ] Bridge testing infrastructure

**Deliverables**:
- `Morphir.IR.Bridges.ClassicModern.fs`
- `Morphir.IR.Bridges.VersionMigration.fs`
- Tests: `Morphir.IR.Bridges.Tests.fs`

### Phase 5: Migration and Documentation (Week 5)
- [ ] Migrate existing transformations to plugins
- [ ] API documentation
- [ ] Tutorial: "Building Your First Plugin"
- [ ] Tutorial: "Migrating Existing Transformations"
- [ ] Performance benchmarks

**Deliverables**:
- Migration guide: `docs/migration-to-pipeline.md`
- Tutorial: `docs/tutorials/first-plugin.md`
- Benchmark results: `benchmarks/pipeline-performance.md`

## Alternatives Considered

### Alternative 1: Keep Status Quo
- **Rejected**: Doesn't address diagnostic accumulation, composability, or extensibility

### Alternative 2: Simple Function Composition
- **Rejected**: No standard diagnostic pattern, difficult to share context

### Alternative 3: Effect System (F# Computation Expressions only)
- **Rejected**: C# interop difficult, too abstract for common use cases

## References

- **Unified.js**: https://unifiedjs.com/
- **Unist Specification**: https://github.com/syntax-tree/unist
- **VFile**: https://github.com/vfile/vfile
- **Task 2.1 Research**: `.agents/kbs/unified-js-architecture.md`
- **Adaptation Strategy**: `.agents/kbs/unified-to-dotnet-adaptation.md`
- **ADR-001**: Dual IR Architecture
- **ADR-003**: Immutable IR Design

## Decision Outcome

**Chosen option**: "Unified.js-Inspired Processor Pattern" because:

1. **Proven Success**: Unified.js has 100M+ downloads/month with similar use case
2. **Functional-First**: Aligns with F# principles (immutability, composition)
3. **Type-Safe**: Adapts well to F#/C# static typing
4. **Extensible**: Plugin architecture enables community contributions
5. **Diagnostic-Rich**: VFile pattern solves error accumulation problem
6. **Ergonomic**: Computation expressions make F# API feel native

This architecture provides a solid foundation for morphir-dotnet's IR transformation pipeline while maintaining compatibility with existing code and enabling future extensibility.

---

**Approved By**: TBD
**Implementation Start**: TBD
**Review Date**: TBD
