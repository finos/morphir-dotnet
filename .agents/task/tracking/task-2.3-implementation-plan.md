# Task 2.3 Implementation Plan: Implement Core Pipeline

**Task**: Implement Core Pipeline (Issue #321)
**Epic**: Morphir Application Architect Skill (Issue #314)
**Phase**: Phase 2: Pluggable Pipeline Architecture
**Status**: 🔨 Ready for Implementation
**Estimated Effort**: 5 days

## Overview

Task 2.3 implements the core pluggable pipeline architecture designed in Task 2.2 (ADR-026). This includes the fundamental types, APIs, and computation expression builders for F# and C#.

## Prerequisites

**Design Documents** (Task 2.2):
- ✅ ADR-026: Pluggable Pipeline Architecture
- ✅ API Design Document
- ✅ Architecture Diagrams
- ✅ Task 2.2 Completion Summary

**Research** (Task 2.1):
- ✅ Unified.js Architecture Study
- ✅ Unist Specification
- ✅ VFile Pattern
- ✅ .NET Adaptation Strategies

## Deliverables

### 1. MorphirFile Implementation

**Files to Create**:
- `src/Morphir.IR.Pipeline/File.fs` (F# implementation)
- `src/Morphir.IR.Pipeline/File.cs` (C# interop)
- `tests/Morphir.IR.Pipeline.Tests/FileTests.fs` (F# tests)

**Types to Implement**:
```fsharp
// Message types
type MessageSeverity = Info | Warning | Error | Fatal
type SourcePosition = { Line: int; Column: int; Offset: int option }
type SourceRange = { Start: SourcePosition; End: SourcePosition }
type MorphirMessage = { Severity: MessageSeverity; Message: string; Position: SourceRange option; ... }

// File type
type MorphirFile = {
    Content: IRNode option
    Path: string option
    History: string list
    Messages: MorphirMessage list
    Data: ImmutableDictionary<string, obj>
}
```

**API Methods**:
- Constructors: `empty`, `fromPath`, `fromContent`, `create`
- Diagnostics: `info`, `warn`, `error`, `fail`, `message`
- Queries: `hasErrors`, `hasFatals`, `messagesOfSeverity`, `errors`, `warnings`
- Data: `setData`, `getData`, `getDataAs`, `removeData`

**Test Coverage Target**: >90%

**Test Cases**:
- Message accumulation
- Severity filtering
- Position tracking
- Data storage/retrieval
- Path history tracking

### 2. MorphirProcessor Implementation

**Files to Create**:
- `src/Morphir.IR.Pipeline/Processor.fs` (F# implementation)
- `src/Morphir.IR.Pipeline/Processor.cs` (C# interop)
- `tests/Morphir.IR.Pipeline.Tests/ProcessorTests.fs` (F# tests)

**Types to Implement**:
```fsharp
type Parser = MorphirFile -> Result<IRNode, string>
type Compiler = IRNode -> MorphirFile -> MorphirFile

type MorphirProcessor = {
    Parsers: Parser list
    Plugins: Plugin list
    Compilers: Compiler list
    Frozen: bool
    Data: ImmutableDictionary<string, obj>
}
```

**API Methods**:
- Constructors: `empty`
- Builders: `parse`, `plugin`, `stringify`, `freeze`
- Execution: `processFile`, `processPath`
- Queries: `isFrozen`

**Three-Phase Execution**:
1. **Parse Phase**: Run all parsers sequentially
2. **Transform Phase**: Run all plugins sequentially
3. **Stringify Phase**: Run all compilers sequentially

**Test Coverage Target**: >90%

**Test Cases**:
- Empty processor
- Single parser/plugin/compiler
- Multiple parsers/plugins/compilers
- Frozen/unfrozen behavior
- Error accumulation across phases
- Data propagation

### 3. Plugin Interface Implementation

**Files to Create**:
- `src/Morphir.IR.Pipeline/Plugin.fs` (F# implementation)
- `src/Morphir.IR.Pipeline/IPlugin.cs` (C# interface)
- `tests/Morphir.IR.Pipeline.Tests/PluginTests.fs` (F# tests)

**Types to Implement**:
```fsharp
type Plugin = {
    Name: string
    Configure: MorphirProcessor -> MorphirProcessor
    Transform: IRNode -> MorphirFile -> (IRNode option * MorphirFile)
}
```

**C# Interface**:
```csharp
public interface IPlugin {
    string Name { get; }
    MorphirProcessor Configure(MorphirProcessor processor);
    (IRNode? Node, MorphirFile File) Transform(IRNode node, MorphirFile file);
}
```

**Helper Methods**:
- `create`: Simple plugin (no configuration)
- `createConfigurable`: Plugin with configuration
- `createDiagnosticOnly`: Only adds diagnostics
- `createTransformOnly`: Only transforms node

**Test Coverage Target**: >90%

**Test Cases**:
- Simple plugin creation
- Plugin with configuration
- Plugin transformation
- Plugin diagnostic accumulation
- Plugin error handling

### 4. PipelineBuilder Computation Expression

**Files to Create**:
- `src/Morphir.IR.Pipeline/PipelineBuilder.fs`
- `tests/Morphir.IR.Pipeline.Tests/PipelineBuilderTests.fs`

**Implementation**:
```fsharp
type PipelineBuilder() =
    member _.Yield(_) = MorphirProcessor.empty

    [<CustomOperation("parse")>]
    member _.Parse(proc, parser) = MorphirProcessor.parse parser proc

    [<CustomOperation("plugin")>]
    member _.Plugin(proc, plugin) = MorphirProcessor.plugin plugin proc

    [<CustomOperation("stringify")>]
    member _.Stringify(proc, compiler) = MorphirProcessor.stringify compiler proc

    [<CustomOperation("freeze")>]
    member _.Freeze(proc) = MorphirProcessor.freeze proc

let pipeline = PipelineBuilder()
```

**Test Coverage Target**: >90%

**Test Cases**:
- Empty pipeline
- Pipeline with parser
- Pipeline with plugins
- Pipeline with compiler
- Pipeline freezing
- Pipeline composition

## Implementation Order

### Day 1: MorphirFile Foundation
- [ ] Create `Morphir.IR.Pipeline` project
- [ ] Implement message types (MessageSeverity, SourcePosition, SourceRange, MorphirMessage)
- [ ] Implement MorphirFile record
- [ ] Implement constructor methods
- [ ] Write unit tests for message types
- [ ] Write unit tests for MorphirFile creation

### Day 2: MorphirFile API
- [ ] Implement diagnostic methods (info, warn, error, fail)
- [ ] Implement query methods (hasErrors, hasFatals, etc.)
- [ ] Implement data methods (setData, getData, etc.)
- [ ] Write unit tests for diagnostic accumulation
- [ ] Write unit tests for data storage
- [ ] Achieve >90% coverage for MorphirFile

### Day 3: MorphirProcessor Foundation
- [ ] Implement MorphirProcessor record
- [ ] Implement Parser and Compiler types
- [ ] Implement constructor methods (empty)
- [ ] Implement builder methods (parse, plugin, stringify, freeze)
- [ ] Write unit tests for processor creation
- [ ] Write unit tests for frozen/unfrozen behavior

### Day 4: MorphirProcessor Execution
- [ ] Implement three-phase execution (Process method)
- [ ] Implement error accumulation across phases
- [ ] Implement data propagation
- [ ] Write unit tests for execution
- [ ] Write integration tests for full pipeline
- [ ] Achieve >90% coverage for MorphirProcessor

### Day 5: Plugin Interface and PipelineBuilder
- [ ] Implement Plugin record
- [ ] Implement C# IPlugin interface
- [ ] Implement plugin helper methods
- [ ] Implement PipelineBuilder computation expression
- [ ] Write unit tests for plugins
- [ ] Write unit tests for PipelineBuilder
- [ ] Write integration tests
- [ ] Achieve >90% overall coverage
- [ ] Create completion summary

## Project Structure

```
src/Morphir.IR.Pipeline/
├── File.fs                    # MorphirFile types and API
├── File.cs                    # C# interop extensions
├── Processor.fs               # MorphirProcessor types and API
├── Processor.cs               # C# interop extensions
├── Plugin.fs                  # Plugin types and helpers
├── IPlugin.cs                 # C# plugin interface
├── PipelineBuilder.fs         # Pipeline computation expression
└── Morphir.IR.Pipeline.fsproj # Project file

tests/Morphir.IR.Pipeline.Tests/
├── FileTests.fs               # MorphirFile tests
├── ProcessorTests.fs          # MorphirProcessor tests
├── PluginTests.fs             # Plugin tests
├── PipelineBuilderTests.fs    # PipelineBuilder tests
├── IntegrationTests.fs        # End-to-end tests
└── Morphir.IR.Pipeline.Tests.fsproj
```

## Testing Strategy

### Unit Tests

**MorphirFile Tests**:
- Message creation (all severity levels)
- Message accumulation (multiple messages)
- Position tracking (with and without positions)
- Data storage (typed and untyped)
- Query methods (hasErrors, etc.)

**MorphirProcessor Tests**:
- Empty processor creation
- Parser addition
- Plugin addition
- Compiler addition
- Frozen/unfrozen state transitions
- Variant creation from frozen processor

**Plugin Tests**:
- Simple plugin creation
- Configurable plugin creation
- Transformation with diagnostics
- Error handling

**PipelineBuilder Tests**:
- Empty pipeline
- Pipeline with all phases
- Pipeline composition
- Computation expression syntax

### Integration Tests

**Full Pipeline Tests**:
- Parse → Transform → Stringify
- Error accumulation across phases
- Data propagation through pipeline
- Multiple plugins in sequence
- Frozen processor reuse

**Example Integration Test**:
```fsharp
[<Fact>]
let ``Full pipeline with validation and optimization`` () =
    // Arrange
    let validatePlugin = Plugin.create "validate" (fun node file ->
        Some node, file.Info("Validation passed")
    )

    let optimizePlugin = Plugin.create "optimize" (fun node file ->
        Some (optimizeNode node), file.Info("Optimization complete")
    )

    let processor = pipeline {
        parse parseIR
        plugin validatePlugin
        plugin optimizePlugin
        stringify serializeIR
        freeze
    }

    let inputFile = MorphirFile.fromPath "test.json"

    // Act
    let result = processor |> MorphirProcessor.processFile inputFile

    // Assert
    result.Messages |> should haveLength 2
    result.Messages.[0].Message |> should equal "Validation passed"
    result.Messages.[1].Message |> should equal "Optimization complete"
    result |> MorphirFile.hasErrors |> should be False
```

## Quality Criteria

### Code Quality
- ✅ F# coding standards (see F# Coding Guide)
- ✅ XML documentation for all public APIs
- ✅ Exhaustive pattern matching
- ✅ No compiler warnings
- ✅ AOT-friendly (no reflection)

### Test Quality
- ✅ >90% code coverage
- ✅ All public APIs tested
- ✅ Edge cases covered
- ✅ Error paths tested
- ✅ Property-based tests where applicable

### Documentation
- ✅ XML doc comments for IntelliSense
- ✅ Code examples in doc comments
- ✅ README.md with usage examples
- ✅ Migration guide updates

## Dependencies

### NuGet Packages
- `FSharp.Core` (already present)
- `System.Collections.Immutable` (already present)
- `xUnit` (for tests, already present)
- `FsUnit.xUnit` (for tests, already present)

### Internal Dependencies
- `Morphir.IR` (for IRNode types)
- Existing IR type definitions

## Risk Mitigation

### Risk 1: IR Type Coupling
**Risk**: Tight coupling to specific IR type structure
**Mitigation**: Use IRNode interface, not concrete types

### Risk 2: Performance
**Risk**: Plugin chain might be slow for deep trees
**Mitigation**: Benchmark early, optimize if needed (trampolining, tail recursion)

### Risk 3: C# Interop
**Risk**: F# idioms might not translate well to C#
**Mitigation**: Provide C#-friendly extension methods, fluent API

### Risk 4: Breaking Changes
**Risk**: Might break existing code
**Mitigation**: New namespace, gradual adoption, deprecation period

## Success Criteria

✅ All deliverables implemented (File, Processor, Plugin, PipelineBuilder)
✅ >90% test coverage achieved
✅ All tests passing
✅ No compiler warnings
✅ AOT-friendly implementation
✅ C# interop working
✅ Documentation complete
✅ Examples provided

## Next Steps After Task 2.3

### Task 2.4: Transformer and Visitor Builders
- Implement TransformerBuilder computation expression
- Implement VisitorBuilder computation expression
- Additional helper utilities

### Task 2.5: Bridge Plugins
- Classic IR ↔ Modern IR bridge
- IR v2 → v3 migration bridge
- Format bridges (TypeScript, Scala)

## Notes

**Implementation Approach**: Follow TDD (Test-Driven Development)
1. Write failing test
2. Implement minimal code to pass
3. Refactor while keeping tests green

**Commit Strategy**: Commit after each day's work
- Day 1: MorphirFile foundation
- Day 2: MorphirFile API complete
- Day 3: MorphirProcessor foundation
- Day 4: MorphirProcessor execution complete
- Day 5: Plugin and PipelineBuilder complete

**Review Points**: Request review after:
- MorphirFile complete (Day 2)
- MorphirProcessor complete (Day 4)
- Full implementation complete (Day 5)

---

**Status**: Ready for Implementation
**Estimated Start**: After Task 2.2 approval
**Estimated Completion**: 5 days after start
**Created**: 2025-12-26
