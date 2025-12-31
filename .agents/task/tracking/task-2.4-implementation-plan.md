# Task 2.4 Implementation Plan: Create Example Plugins

**Task**: Create Example Plugins (Issue #322)
**Epic**: Morphir Application Architect Skill (Issue #314)
**Phase**: Phase 2: Pluggable Pipeline Architecture
**Status**: 🔨 Ready for Implementation
**Estimated Effort**: 4 days

## Overview

Task 2.4 creates example transformation plugins to demonstrate the pluggable pipeline architecture implemented in Task 2.3. These plugins serve as:
- **Reference implementations** for plugin developers
- **Test cases** for the pipeline infrastructure
- **Documentation** through working examples
- **Foundation** for the Plugin Development Guide

## Prerequisites

**Completed Tasks**:
- ✅ Task 2.1: Unified.js Architecture Study (#319)
- ✅ Task 2.2: Pipeline Architecture Design (ADR) (#320)
- ✅ Task 2.3: Implement Core Pipeline (#321, PR #356)

**Available Infrastructure**:
- ✅ MorphirFile for diagnostic accumulation
- ✅ MorphirProcessor for three-phase pipeline execution
- ✅ Plugin record type with Configure + Transform pattern
- ✅ PipelineBuilder computation expression
- ✅ 92 passing tests demonstrating core functionality

## Deliverables

### 1. Type Validator Plugin

**Purpose**: Validates Morphir IR type correctness and reports type errors with context.

**Files to Create**:
- `src/Morphir.IR.Pipeline.Plugins/TypeValidator.fs` - Type validation plugin
- `tests/Morphir.IR.Pipeline.Plugins.Tests/TypeValidatorTests.fs` - Test suite

**Functionality**:
- Validate type consistency across IR nodes
- Check for undefined type references
- Verify type parameter usage
- Report type errors with source positions
- Accumulate all type errors (not just first)

**Example Usage**:
```fsharp
let typeValidator = TypeValidator.create()

let processor = pipeline {
    parse irJsonParser
    uses typeValidator
    stringify irJsonSerializer
}

let result = processor |> MorphirProcessor.processFile inputFile

// result.Messages will contain all type errors found
```

**Test Coverage Target**: >90%

**Test Cases**:
- Valid IR with correct types
- IR with type mismatches
- IR with undefined type references
- IR with invalid type parameters
- Multiple type errors (accumulation test)
- Type error with source position tracking

### 2. Optimization Plugin

**Purpose**: Performs simple IR optimizations to demonstrate transformation plugins.

**Files to Create**:
- `src/Morphir.IR.Pipeline.Plugins/Optimizer.fs` - Optimization plugin
- `tests/Morphir.IR.Pipeline.Plugins.Tests/OptimizerTests.fs` - Test suite

**Functionality**:
- **Constant folding**: Evaluate constant expressions at compile time
- **Dead code elimination**: Remove unreachable code
- **Identity function elimination**: Simplify `x => x` applications
- **Report optimizations**: Add info messages for applied optimizations
- **Preserve semantics**: Ensure transformations are semantics-preserving

**Example Usage**:
```fsharp
let optimizer = Optimizer.create()

let processor = pipeline {
    parse irJsonParser
    uses optimizer
    stringify irJsonSerializer
}

// Input:  1 + 2
// Output: 3
// Message: "Applied constant folding: 1 + 2 → 3"
```

**Test Coverage Target**: >90%

**Test Cases**:
- Constant folding (arithmetic, boolean, string)
- Dead code elimination (if false, unreachable branches)
- Identity function elimination
- Multiple optimizations in sequence
- Optimization reporting (info messages)
- No-op case (already optimized IR)

### 3. Pretty Printer Plugin

**Purpose**: Generates human-readable IR representation for debugging and documentation.

**Files to Create**:
- `src/Morphir.IR.Pipeline.Plugins/PrettyPrinter.fs` - Pretty printing plugin
- `tests/Morphir.IR.Pipeline.Plugins.Tests/PrettyPrinterTests.fs` - Test suite

**Functionality**:
- Format IR as indented text
- Syntax highlighting (ANSI color codes for terminal)
- Configurable indentation width
- Show/hide type annotations
- Generate documentation comments
- Store output in MorphirFile.Data for retrieval

**Example Usage**:
```fsharp
let prettyPrinter = PrettyPrinter.create { IndentWidth = 2; ShowTypes = true }

let processor = pipeline {
    parse irJsonParser
    uses prettyPrinter
    stringify irJsonSerializer
}

let result = processor |> MorphirProcessor.processFile inputFile

// Retrieve pretty-printed output
let prettyOutput = result |> MorphirFile.getDataAs<string> "pretty-printed"
```

**Test Coverage Target**: >90%

**Test Cases**:
- Simple expression formatting
- Nested expression formatting
- Type annotation display
- Indentation correctness
- Color code generation (ANSI escapes)
- Configuration options (indent width, show types)

### 4. Plugin Development Guide

**Purpose**: Comprehensive guide for developers creating custom plugins.

**File to Create**:
- `docs/content/docs/pipeline/plugin-development-guide.md` - Plugin development guide

**Content Sections**:

1. **Introduction**
   - What are plugins?
   - When to create a plugin?
   - Plugin architecture overview

2. **Plugin Anatomy**
   - Plugin record structure
   - Configure vs Transform
   - Diagnostic accumulation
   - Data storage patterns

3. **Simple Plugin Pattern**
   - Read-only validation
   - Example: Type validator walkthrough
   - Error reporting best practices

4. **Transformation Plugin Pattern**
   - Modifying IR nodes
   - Example: Optimizer walkthrough
   - Preserving semantics

5. **Diagnostic Plugin Pattern**
   - Generating reports
   - Example: Pretty printer walkthrough
   - Using MorphirFile.Data

6. **Advanced Patterns**
   - Plugin configuration
   - Multi-phase plugins
   - Plugin composition
   - Sharing data between plugins

7. **Testing Plugins**
   - Unit testing strategies
   - Integration testing with pipelines
   - Property-based testing for transformations

8. **Best Practices**
   - Keep plugins focused (single responsibility)
   - Accumulate errors, don't fail fast
   - Use source positions for error reporting
   - Make transformations idempotent
   - Document configuration options

## Project Structure

```
src/Morphir.IR.Pipeline.Plugins/
├── TypeValidator.fs          # Type validation plugin
├── Optimizer.fs              # IR optimization plugin
├── PrettyPrinter.fs          # Pretty printing plugin
└── Morphir.IR.Pipeline.Plugins.fsproj  # Project file

tests/Morphir.IR.Pipeline.Plugins.Tests/
├── TypeValidatorTests.fs     # Type validator tests
├── OptimizerTests.fs         # Optimizer tests
├── PrettyPrinterTests.fs     # Pretty printer tests
├── Program.fs                # Test runner
└── Morphir.IR.Pipeline.Plugins.Tests.fsproj  # Test project

docs/content/docs/pipeline/
└── plugin-development-guide.md  # Plugin guide
```

## Implementation Order

### Day 1: Project Setup and Type Validator

**Morning (3 hours)**:
- [ ] Create `Morphir.IR.Pipeline.Plugins` F# project
- [ ] Create `Morphir.IR.Pipeline.Plugins.Tests` test project
- [ ] Add project references and dependencies
- [ ] Create `TypeValidator.fs` skeleton
- [ ] Create `TypeValidatorTests.fs` with initial test structure

**Afternoon (3 hours)**:
- [ ] Implement type validator core logic
  - Type consistency checking
  - Undefined type reference detection
  - Type parameter validation
- [ ] Write type validator tests (10+ tests)
- [ ] Achieve >90% coverage for type validator
- [ ] Commit: "feat: implement type validator plugin"

### Day 2: Optimization Plugin

**Morning (3 hours)**:
- [ ] Create `Optimizer.fs` skeleton
- [ ] Create `OptimizerTests.fs` with initial test structure
- [ ] Implement constant folding
  - Arithmetic expressions (1 + 2 → 3)
  - Boolean expressions (true && x → x)
  - String concatenation ("a" + "b" → "ab")

**Afternoon (3 hours)**:
- [ ] Implement dead code elimination
  - If-false branch removal
  - Unreachable code detection
- [ ] Implement identity function elimination (x => x)
- [ ] Write optimizer tests (15+ tests)
- [ ] Achieve >90% coverage for optimizer
- [ ] Commit: "feat: implement IR optimization plugin"

### Day 3: Pretty Printer Plugin

**Morning (3 hours)**:
- [ ] Create `PrettyPrinter.fs` skeleton
- [ ] Create `PrettyPrinterTests.fs` with initial test structure
- [ ] Implement basic formatting
  - Expression formatting
  - Indentation logic
  - Type annotation display

**Afternoon (3 hours)**:
- [ ] Implement ANSI color code generation
- [ ] Implement configuration options
- [ ] Store formatted output in MorphirFile.Data
- [ ] Write pretty printer tests (12+ tests)
- [ ] Achieve >90% coverage for pretty printer
- [ ] Commit: "feat: implement pretty printer plugin"

### Day 4: Plugin Development Guide and Integration

**Morning (3 hours)**:
- [ ] Create plugin development guide
  - Introduction and overview
  - Plugin anatomy walkthrough
  - Simple plugin pattern (type validator example)
  - Transformation plugin pattern (optimizer example)
  - Diagnostic plugin pattern (pretty printer example)

**Afternoon (3 hours)**:
- [ ] Add advanced patterns section
  - Plugin configuration
  - Multi-phase plugins
  - Plugin composition
- [ ] Add testing section
- [ ] Add best practices section
- [ ] Create integration tests (pipeline with all 3 plugins)
- [ ] Review all tests (40+ total tests expected)
- [ ] Create Task 2.4 completion summary
- [ ] Commit: "docs: add plugin development guide"

## Testing Strategy

### Unit Tests

**Type Validator Tests (10+ tests)**:
- `valid IR should pass validation`
- `type mismatch should report error`
- `undefined type reference should report error`
- `invalid type parameters should report error`
- `multiple type errors should accumulate`
- `type errors should include source positions`
- `nested type validation`
- `generic type validation`
- `recursive type validation`
- `type validator should not modify IR`

**Optimizer Tests (15+ tests)**:
- `constant arithmetic folding`
- `constant boolean folding`
- `constant string concatenation`
- `dead if-false branch elimination`
- `dead if-true branch elimination`
- `unreachable code elimination`
- `identity function elimination`
- `nested optimization`
- `multiple optimizations in sequence`
- `optimization info messages`
- `already optimized IR (no-op)`
- `optimizer should preserve semantics`
- `optimizer should handle complex expressions`
- `optimizer should not introduce errors`
- `optimization configuration options`

**Pretty Printer Tests (12+ tests)**:
- `simple expression formatting`
- `nested expression formatting`
- `type annotation display`
- `indentation correctness`
- `ANSI color code generation`
- `configurable indent width`
- `configurable type display`
- `output stored in MorphirFile.Data`
- `complex IR formatting`
- `multi-line formatting`
- `pretty printer should not modify IR`
- `pretty printer configuration`

### Integration Tests

**Pipeline Composition Tests (5+ tests)**:
- `pipeline with type validator only`
- `pipeline with optimizer only`
- `pipeline with pretty printer only`
- `pipeline with type validator + optimizer`
- `pipeline with all three plugins (validator → optimizer → pretty printer)`
- `frozen pipeline variant with additional plugin`
- `plugin data sharing between plugins`

**End-to-End Tests (3+ tests)**:
- `parse → validate → optimize → stringify`
- `parse → validate → pretty print`
- `error accumulation across plugins`

### Property-Based Tests (Optional)

**Optimizer Properties**:
- Semantics preservation: `optimize(IR) ≡ IR` (behavioral equivalence)
- Idempotence: `optimize(optimize(IR)) == optimize(IR)`
- Size reduction: `size(optimize(IR)) <= size(IR)`

## Quality Criteria

### Code Quality
- ✅ F# coding standards (see F# Coding Guide)
- ✅ XML documentation for all public APIs
- ✅ Exhaustive pattern matching
- ✅ No compiler warnings
- ✅ AOT-friendly (no reflection)
- ✅ Immutable data structures

### Test Quality
- ✅ >90% code coverage per plugin
- ✅ All public APIs tested
- ✅ Edge cases covered
- ✅ Error paths tested
- ✅ Integration tests for plugin composition

### Documentation
- ✅ XML doc comments for IntelliSense
- ✅ Code examples in doc comments
- ✅ Plugin development guide comprehensive
- ✅ Examples tested and verified

## Dependencies

### NuGet Packages
- `FSharp.Core` (already present)
- `System.Collections.Immutable` (already present)
- `Expecto` (for tests, already present)

### Internal Dependencies
- `Morphir.IR.Pipeline` (Task 2.3, completed)
- `Morphir.IR` (for IR type definitions)

## Placeholder IR Types

Since we don't have full Morphir IR types yet, we'll use simple placeholder types for demonstration:

```fsharp
// Placeholder IR node types for examples
type IRNode =
    | IntLiteral of int
    | BoolLiteral of bool
    | StringLiteral of string
    | BinaryOp of op: string * left: IRNode * right: IRNode
    | IfExpr of condition: IRNode * thenBranch: IRNode * elseBranch: IRNode
    | Lambda of param: string * body: IRNode
    | Apply of func: IRNode * arg: IRNode
    | Variable of name: string
```

These placeholders will be replaced with actual Morphir IR types in future tasks.

## Risk Mitigation

### Risk 1: IR Type Definitions Missing
**Risk**: Full Morphir IR types not yet available
**Mitigation**: Use simple placeholder types, document migration path

### Risk 2: Plugin Complexity
**Risk**: Plugins might be too complex for examples
**Mitigation**: Keep implementations simple, focus on demonstrating patterns

### Risk 3: Testing Placeholder IR
**Risk**: Tests might not translate well to real IR
**Mitigation**: Design tests to be IR-agnostic where possible

## Success Criteria

✅ Type validator plugin implemented and tested
✅ Optimization plugin implemented and tested
✅ Pretty printer plugin implemented and tested
✅ Plugin development guide written
✅ >90% test coverage achieved (40+ tests)
✅ All tests passing
✅ No compiler warnings
✅ AOT-friendly implementation
✅ Documentation complete with examples

## Next Steps After Task 2.4

### Task 2.5: Pipeline Automation Scripts
- Create `validate-pipeline.fsx` script
- Create `analyze-pipeline-performance.fsx` script
- Automate common pipeline tasks

### Future Enhancements
- Additional example plugins (linting, metrics, etc.)
- C# plugin examples
- Plugin marketplace/registry
- Plugin dependency resolution

## Notes

**Implementation Approach**: Follow TDD (Test-Driven Development)
1. Write failing test
2. Implement minimal code to pass
3. Refactor while keeping tests green

**Commit Strategy**: Commit after each plugin completion
- Day 1: Type validator complete
- Day 2: Optimizer complete
- Day 3: Pretty printer complete
- Day 4: Plugin guide and integration complete

**Review Points**: Request review after:
- All plugins complete (Day 3)
- Plugin development guide complete (Day 4)

---

**Status**: Ready for Implementation
**Estimated Start**: 2025-12-31
**Estimated Completion**: 4 days
**Created**: 2025-12-31
