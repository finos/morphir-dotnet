---
title: "IR Classic Migration and Namespace Strategy"
linkTitle: "IR Classic Migration"
weight: 20
description: "Design guide for Morphir.IR.Classic namespace strategy and migration from morphir-elm"
---

# IR Classic Migration and Namespace Strategy

## Overview

This document describes the namespace strategy for Morphir IR in the F# implementation, specifically the separation between `Morphir.IR.Classic` (existing morphir-elm IR) and `Morphir.IR` (future evolution). This guide serves as a reference for AI agents and human contributors working on the Morphir IR model.

## Purpose

The Morphir maintainers recognize that the generic attribute approach in the current IR complicates things, but we need to support existing morphir-elm tools and enable migration of existing code. The namespace strategy allows us to:

1. **Support existing tools**: Maintain compatibility with morphir-elm ecosystem
2. **Enable migration**: Allow existing morphir-elm code to migrate to F# (and eventually other languages)
3. **Reserve evolution space**: Keep `Morphir.IR` namespace free for future improvements
4. **Document decisions**: Provide clear guidance for contributors and AI agents

## Namespace Strategy

### Morphir.IR.Classic

**Purpose**: Represents the existing IR available in morphir-elm with generic attributes.

**Characteristics**:
- Uses generic attributes (`Type<'attributes>`, `AccessControlled<'T>`, etc.)
- Maintains compatibility with current Morphir ecosystem
- Supports existing tools and JSON serialization formats
- Enables migration from morphir-elm codebase

**Namespace format**: `Morphir.IR.Classic` (not `Morphir.Classic.IR`)

**Directory structure**: `IR/Classic/` (not `Classic/IR/`)

**Modules**:
- `AccessControlled<'T>` - Access control wrapper
- `Type<'attributes>` - Type expressions, specifications, and definitions
- Future: `Value<'typeAttributes, 'valueAttributes>`, `Module`, `Package`, `Distribution`

### Morphir.IR

**Purpose**: Reserved for future evolution of Morphir IR.

**Characteristics**:
- Will support a different, simpler approach to attributes
- Allows for breaking changes and improvements
- Future-proof design space
- Better developer experience

**Modules** (foundational, no attributes):
- `Name` - Human-readable identifiers
- `Path` - Hierarchical paths
- `PackageName` - Package identifiers
- `ModulePath` - Module paths
- `FQName` - Fully-qualified names

These foundational modules are building blocks used by both Classic and future IR.

## Attribute Approach

### Current Classic IR Approach

**Generic Attributes**: The Classic IR uses generic type parameters for attributes (`'attributes`, `'T`, etc.)

**Rationale**:
- Required for compatibility with morphir-elm
- Enables extensibility (can attach any attribute type)
- Matches existing morphir-elm implementation

**Complications**:
- Adds complexity to type signatures
- Makes code more verbose
- Requires generic parameters throughout the type system
- Can complicate serialization and code generation

**Example**:
```fsharp
type Type<'attributes> =
    | Variable of 'attributes * Name
    | Reference of 'attributes * FQName * Type<'attributes> list
    // ...
```

### Future IR Approach

**Simpler Attributes**: The future `Morphir.IR` namespace will use a simpler attribute approach.

**Goals**:
- Less generic, more straightforward
- Better developer experience
- Reduced complexity
- Breaking changes acceptable in Morphir.IR namespace

**Note**: The exact design of the future attribute approach is still being determined by Morphir maintainers.

## Module Organization

### Dependency Relationships

```
Morphir.IR (foundational)
├── Name
├── Path
├── PackageName
├── ModulePath
└── FQName

Morphir.IR.Classic (uses IR modules)
├── AccessControlled<'T>
└── Type<'attributes>
    └── Uses: Name, FQName, AccessControlled
```

**Key Points**:
- Classic modules depend on IR modules (one-way dependency)
- IR modules are independent and don't depend on Classic
- This allows Classic to be optional while IR remains core

### Module Placement Guidelines

**Morphir.IR** (foundational, no attributes):
- ✅ Name, Path, PackageName, ModulePath, FQName
- ✅ Any module that doesn't need attributes
- ✅ Building blocks used by both Classic and future IR

**Morphir.IR.Classic** (existing morphir-elm IR with attributes):
- ✅ Type, AccessControlled
- ✅ Future: Value, Module, Package, Distribution
- ✅ Any module that uses generic attributes for morphir-elm compatibility

## Migration Path

### From morphir-elm to F#

**Strategy**: Use `Morphir.IR.Classic` namespace for all modules that match morphir-elm structure.

**Steps**:
1. Implement modules in `Morphir.IR.Classic` namespace
2. Use generic attributes to match morphir-elm types
3. Maintain JSON serialization compatibility
4. Support existing tooling

**Example Migration**:
```elm
-- morphir-elm
type Type attributes
    = Variable attributes Name
    | Reference attributes FQName (List (Type attributes))
```

```fsharp
// morphir-dotnet (Classic)
namespace Morphir.IR.Classic

type Type<'attributes> =
    | Variable of 'attributes * Name
    | Reference of 'attributes * FQName * Type<'attributes> list
```

### From Classic to Future IR

**Strategy**: When future IR is designed, new modules will go in `Morphir.IR` namespace.

**Considerations**:
- Breaking changes are acceptable in `Morphir.IR`
- Classic modules remain for backward compatibility
- Migration tools may be provided to convert Classic → IR
- Both namespaces can coexist

## Design Decisions

### Why Generic Attributes in Classic?

1. **Compatibility**: Required to match morphir-elm implementation
2. **Tool Support**: Existing tools expect generic attributes
3. **Migration**: Enables direct translation from morphir-elm
4. **Extensibility**: Allows attaching various attribute types

### Why Separate Namespaces?

1. **Evolution Space**: Reserves `Morphir.IR` for future improvements
2. **Clear Separation**: Makes it obvious which modules are "classic" vs "future"
3. **Backward Compatibility**: Classic modules remain available
4. **Gradual Migration**: Allows incremental adoption of future IR

### Trade-offs and Considerations

**Pros**:
- Supports existing ecosystem
- Enables migration from morphir-elm
- Clear separation of concerns
- Future-proof design

**Cons**:
- Generic attributes add complexity
- Two namespaces to understand
- Potential confusion about which to use
- Maintenance of both namespaces

## Future Evolution

### Plans for Morphir.IR Namespace

The Morphir maintainers plan to evolve the IR with a simpler attribute approach. The `Morphir.IR` namespace is reserved for this evolution.

**Timeline**: TBD by Morphir maintainers

**Breaking Changes**: Acceptable in `Morphir.IR` namespace

**Backward Compatibility**: Classic modules will remain available

### Migration Strategy

When future IR is ready:
1. New modules go in `Morphir.IR`
2. Classic modules remain for compatibility
3. Migration tools may be provided
4. Documentation will guide users

## Directory Structure

The directory structure matches the namespace:

```
src/Morphir.Models/
  IR/
    Name.fs                    → namespace Morphir.IR
    Path.fs                    → namespace Morphir.IR
    PackageName.fs             → namespace Morphir.IR
    ModulePath.fs              → namespace Morphir.IR
    FQName.fs                  → namespace Morphir.IR
    Classic/
      AccessControlled.fs      → namespace Morphir.IR.Classic
      Type.fs                  → namespace Morphir.IR.Classic
```

**Key Point**: `IR/Classic/` directory structure matches `Morphir.IR.Classic` namespace.

## Reference for AI Agents

When implementing IR modules:

1. **Check namespace**: Is this a foundational module (no attributes) or Classic module (with attributes)?
2. **Use appropriate namespace**: 
   - Foundational → `Morphir.IR`
   - Classic → `Morphir.IR.Classic`
3. **Follow directory structure**: Match namespace with directory structure
4. **Maintain dependencies**: Classic can depend on IR, but not vice versa
5. **Document decisions**: Add notes about why modules are in their chosen namespace

## Summary

- **Morphir.IR.Classic**: Existing morphir-elm IR with generic attributes
- **Morphir.IR**: Future evolution with simpler attributes
- **Directory**: `IR/Classic/` matches `Morphir.IR.Classic` namespace
- **Strategy**: Support existing tools while reserving space for future improvements
- **Migration**: Enable morphir-elm → F# migration while planning for future evolution

