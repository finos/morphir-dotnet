# Morphir Application Architect Skill

Expert Morphir application architect providing guidance on AST design, functional programming patterns, IR transformations, and code generation for morphir-dotnet.

## Overview

This skill provides comprehensive architectural guidance including:
- Language design patterns (AST/CST, type systems, visitors)
- Functional programming patterns (monads, lenses, railway-oriented programming)
- Morphir IR modeling and transformation
- Code generation and metaprogramming
- F#/C# interop strategies
- Pattern recognition and application

## Quick Start

### For Claude Code Users

```
@skill morphir-architect
Design an AST for representing Morphir function definitions
```

or use shorter aliases:

```
@skill architect
How should I implement a transformation pipeline with error handling?
```

### For All Users

**Read comprehensive guidance:**
```bash
cat .claude/skills/morphir-architect/skill.md
```

**Access knowledge bases:**
```bash
# Language design patterns (22+ patterns)
cat .agents/kbs/language-design-patterns.md

# Functional programming patterns (18+ patterns)
cat .agents/kbs/functional-programming-patterns.md

# Visitor implementations (8 variants)
cat .agents/kbs/visitor-pattern-implementations.md

# Computation expressions for AST
cat .agents/kbs/computation-expressions-for-ast.md

# Compiler services & metaprogramming
cat .agents/kbs/compiler-services-metaprogramming.md
```

**Run architectural review:**
```bash
dotnet fsi .claude/skills/morphir-architect/scripts/architecture-review.fsx
```

## Files and Structure

### Core Files
- **skill.md** - Main skill prompt with comprehensive architectural guidance
- **README.md** - This file - quick reference
- **MAINTENANCE.md** - Skill maintenance and evolution guidelines
- **metadata.yaml** - Skill metadata and configuration

### Knowledge Bases
Located in `.agents/kbs/`:
- **ecosystem-knowledge-base.md** - 50+ Morphir ecosystem entries, cross-repository patterns
- **language-design-patterns.md** - 22+ AST/CST and type system patterns
- **visitor-pattern-implementations.md** - 8 visitor pattern variants with examples
- **computation-expressions-for-ast.md** - F# computation expression patterns for tree construction
- **functional-programming-patterns.md** - 18 FP patterns (monads, functors, lenses, etc.)
- **compiler-services-metaprogramming.md** - Code generation and analysis patterns

### Decision Logs
Located in `.agents/decisionlogs/`:
- **architectural-decisions.md** - 25 documented architectural decision records (ADRs)

### Scripts (Future)
Located in `.claude/skills/morphir-architect/scripts/`:
- **architecture-review.fsx** - Scan for architectural anti-patterns (planned)
- **ir-consistency-check.fsx** - Verify Classic/Modern IR sync (planned)
- **pattern-matcher.fsx** - Identify applicable patterns in code (planned)

### Templates (Future)
Located in `.claude/skills/morphir-architect/templates/`:
- **new-ast-type.md** - Template for designing new AST/IR types (planned)
- **transformation-pipeline.md** - Template for ROP pipelines (planned)
- **visitor-implementation.md** - Template for visitor patterns (planned)

## Core Competencies

### 1. Language Design Patterns

**What**: AST/CST design, type system patterns, visitor implementations

**Knowledge Base**: [language-design-patterns.md](../../../.agents/kbs/language-design-patterns.md)

**Key Patterns**:
- Algebraic Data Types (F# DU, C# sealed records)
- Generic Attributes Pattern (`Type<'attributes>`)
- Wrapper Types (AccessControlled, Documented)
- Immutable Trees with Structural Sharing
- Smart Constructors

**Example Use Cases**:
- Designing new Morphir IR node types
- Implementing AST transformations
- Creating type-safe visitor patterns

---

### 2. Functional Programming Patterns

**What**: Monads, functors, lenses, railway-oriented programming

**Knowledge Base**: [functional-programming-patterns.md](../../../.agents/kbs/functional-programming-patterns.md)

**Key Patterns**:
- **Monads**: Option, Result, List, State, Reader
- **Functors**: map operations preserving structure
- **Applicatives**: validation with error accumulation
- **Railway-Oriented Programming**: Result pipelines
- **Lenses**: composable getters/setters

**Example Use Cases**:
- IR validation pipelines
- Error handling in transformations
- Nested immutable structure updates

---

### 3. Visitor Pattern Implementations

**What**: 8 visitor variants for AST traversal and transformation

**Knowledge Base**: [visitor-pattern-implementations.md](../../../.agents/kbs/visitor-pattern-implementations.md)

**Variants**:
1. Classic OO Visitor (C# Modern IR)
2. Functional Pattern Matching (F# Classic IR)
3. Type-Safe Record Visitor (F# composable)
4. Visitor with Default Behavior
5. Transforming Visitor
6. Accumulating Visitor
7. Context-Passing Visitor
8. Async Visitor

**Example Use Cases**:
- Type size calculation
- Pretty-printing IR
- AST transformations
- Dependency collection

---

### 4. Computation Expressions for AST

**What**: F# computation expressions for tree construction

**Knowledge Base**: [computation-expressions-for-ast.md](../../../.agents/kbs/computation-expressions-for-ast.md)

**Examples Studied**:
- **Fabulous**: UI component trees
- **Fabulous.AST**: 93% boilerplate reduction for F# code generation
- **Fun.Blazor**: Used in Morphir.Live

**Example Use Cases**:
- Building IR programmatically
- Creating DSLs for transformations
- Simplifying tree construction

---

### 5. Compiler Services & Metaprogramming

**What**: Code generation and analysis with FCS, Roslyn, Source Generators, Myriad

**Knowledge Base**: [compiler-services-metaprogramming.md](../../../.agents/kbs/compiler-services-metaprogramming.md)

**Technologies**:
- **F# Compiler Service**: Parse/analyze F# code
- **Roslyn**: Parse/analyze C# code
- **C# Source Generators**: Generate C# visitors
- **Myriad**: Generate F# visitors/lenses
- **F# Type Providers**: External schema integration

**Example Use Cases**:
- Generate visitor interfaces for IR types
- Generate lenses for nested updates
- Analyze SDK code for documentation

## Common Scenarios

### Scenario 1: "I need to design a new AST type"

1. Read playbook in [skill.md](./skill.md) - "Design New AST/IR Type"
2. Reference [language-design-patterns.md](../../../.agents/kbs/language-design-patterns.md) - ADT patterns
3. Follow template in `templates/new-ast-type.md` (when available)
4. Implement with F# DU or C# sealed records

### Scenario 2: "I need to validate IR with good error messages"

1. Read playbook in [skill.md](./skill.md) - "Implement Railway-Oriented Programming Pipeline"
2. Reference [functional-programming-patterns.md](../../../.agents/kbs/functional-programming-patterns.md) - Result monad, Applicative
3. Choose:
   - **Applicative Validation** if need all errors at once
   - **Railway-Oriented** if short-circuit on first error
4. Implement using template in `templates/transformation-pipeline.md` (when available)

### Scenario 3: "I need to traverse and transform an AST"

1. Read playbook in [skill.md](./skill.md) - Decision Tree for visitor selection
2. Reference [visitor-pattern-implementations.md](../../../.agents/kbs/visitor-pattern-implementations.md)
3. Choose variant based on:
   - **F# Classic IR**: Functional pattern matching or record visitor
   - **C# Modern IR**: Classic OO visitor or transforming visitor
4. Implement using template in `templates/visitor-implementation.md` (when available)

### Scenario 4: "I need to update deeply nested IR structures"

1. Reference [functional-programming-patterns.md](../../../.agents/kbs/functional-programming-patterns.md) - Lenses section
2. Create lens composition: `addressLens >>> cityLens`
3. Use `Lens.set` for updates
4. Consider Myriad lens generator for automation (future)

### Scenario 5: "I need to generate code for IR visitors"

1. Reference [compiler-services-metaprogramming.md](../../../.agents/kbs/compiler-services-metaprogramming.md)
2. Choose technology:
   - **C# Modern IR**: Source Generators
   - **F# Classic IR**: Myriad
3. Implement incremental generator
4. Integrate with build process

## Decision Trees

### When to Use Which IR?

```
Working with Morphir IR?
  ├─ F# consumer → Classic IR (discriminated unions)
  ├─ C# consumer → Modern IR (sealed records)
  └─ Both → Use conversion functions
```

### When to Use Which Visitor?

```
Traversing AST?
  ├─ F# + DU → Functional pattern matching
  ├─ C# + Sealed records → Classic OO visitor
  ├─ Composable operations → Record visitor (F#)
  └─ Stack-safe deep traversal → Trampolined visitor
```

### When to Use Which Error Handling?

```
Handling errors?
  ├─ Collect all errors → Applicative Validation
  ├─ Stop on first error → Railway-Oriented Programming
  └─ Optional values → Option/Maybe
```

## Integration with Other Skills

**Works with:**
- **Technical Writer** - Architecture diagrams, pattern documentation
- **QA Tester** - Test strategies for FP code
- **AOT Guru** - Pattern selection for AOT compatibility
- **Elm-to-F# Guru** - Elm pattern translation

**Escalate to maintainer when:**
- Fundamental IR design changes proposed
- Conflicting pattern recommendations
- Breaking API changes needed

## Quick Reference Tables

### Pattern Selection Matrix

| Use Case | F# Pattern | C# Pattern |
|----------|-----------|-----------|
| IR Type Modeling | Discriminated Union | Sealed Record Hierarchy |
| AST Traversal | Pattern Matching | Classic OO Visitor |
| Error Handling | Result + bind | Result with extension methods |
| Optional Values | option type | Nullable reference types |
| Collections | Map, Set | ImmutableDictionary, ImmutableHashSet |

### Monad Quick Reference

| Monad | F# Type | Purpose | Key Operations |
|-------|---------|---------|----------------|
| Option | `'T option` | Nullable values | `Some`, `None`, `Option.bind`, `Option.map` |
| Result | `Result<'T,'E>` | Error handling | `Ok`, `Error`, `Result.bind`, `Result.map` |
| List | `'T list` | Non-determinism | `::`, `List.collect`, `List.map` |
| Async | `Async<'T>` | Async operations | `async { }`, `Async.bind` |

### Visitor Variant Selection

| Requirement | Recommended Variant |
|-------------|-------------------|
| Simple traversal (F#) | Functional Pattern Matching |
| Multiple operations (C#) | Classic OO Visitor |
| Composable operations | Record Visitor (F#) |
| AST transformation | Transforming Visitor |
| Collecting information | Accumulating Visitor |
| Scoped context | Context-Passing Visitor |

## External Resources

- [Morphir Homepage](https://morphir.finos.org/) - Official documentation
- [morphir-elm](https://github.com/finos/morphir-elm) - Reference implementation
- [F# for Fun and Profit](https://fsharpforfunandprofit.com/) - FP patterns, ROP
- [Category Theory for Programmers](https://bartoszmilewski.com/2014/10/28/category-theory-for-programmers-the-preface/) - Theoretical foundations

## Maintenance

See [MAINTENANCE.md](./MAINTENANCE.md) for:
- Quarterly review process
- Knowledge base updates
- Pattern catalog evolution
- Automation script development

## Version History

### 1.0.0-alpha (2025-12-24)
- Initial release with comprehensive knowledge bases
- 5 knowledge bases: ecosystem, language design, visitors, CEs, FP patterns, metaprogramming
- 25 architectural decision records
- Core competencies and playbooks documented
- Decision trees for pattern selection

---

**Status:** Alpha
**Maintainer:** Damian Reeves (@DamianReeves)
**Last Updated:** 2025-12-24
