# Packages DSL Design Review

**Status**: 🔄 In Review
**Date**: 2025-12-26
**Reviewers**: morphir-dotnet team

## Executive Summary

The Packages DSL provides builder-style APIs for creating Morphir IR package specifications and definitions. Packages are the top-level organizational unit in Morphir, containing modules that are versioned and distributed together. This is Phase 2 of the DSL modernization effort, following the successful completion of the Modules DSL refactoring.

**Current State**:
- ✅ Has builder-based API (similar to pre-refactored Modules DSL)
- ✅ Supports both PackageSpecification (interface) and PackageDefinition (implementation)
- ✅ Has fluent methods: `.Module()`, `.PrivateModule()`
- ❓ Uses inheritance pattern (`new()` constructor creates base builder with state)
- ❓ No CustomOperations - only direct method calls
- ❓ Different pattern from refactored Modules DSL
- ❓ Builder pattern vs Computation Expression pattern

**Key Insight**: Packages are simpler than Modules - they're essentially collections of modules with paths. The main complexity is in module path handling and access control for entire modules.

## Package IR Structure

### PackageSpecification (Public Interface)
```fsharp
type PackageSpecification<'attributes> = {
    Modules: Map<ModulePath, ModuleSpecification<'attributes>>
}
```

**Purpose**: Defines the public API of a package (only publicly exposed modules with type signatures)

**Characteristics**:
- Contains only ModuleSpecification values (no implementations)
- All modules in a specification are implicitly public
- Used for package interfaces and documentation

### PackageDefinition (Full Implementation)
```fsharp
type PackageDefinition<'typeAttributes, 'valueAttributes> = {
    Modules: Map<ModulePath, AccessControlled<ModuleDefinition<'typeAttributes, 'valueAttributes>>>
}
```

**Purpose**: Complete package with all modules (public + private) with full implementations

**Characteristics**:
- Contains ModuleDefinition values wrapped in AccessControlled
- Modules can be public or private
- Used for complete package implementations

### ModulePath Structure
```fsharp
type ModulePath = ModulePath of Path

// Example usage:
let modulePath = ModulePath.modulePathFromList [ Name.fromList ["com"]; Name.fromList ["example"]; Name.fromList ["Customer"] ]
// Represents: com.example.Customer
```

**Purpose**: Hierarchical path to a module within a package (like a file path or namespace)

## Design Questions to Explore

### Q1: Builder Pattern vs CE Pattern?

**Current**: Builder pattern with stateful accumulation
```fsharp
type PackageDefinitionBuilder<'ta, 'va>(modules: Map<ModulePath, AccessControlled<ModuleDefinition<'ta, 'va>>>) =
    new() = PackageDefinitionBuilder(Map.empty)

    member this.Module(modulePath, moduleDef) =
        PackageDefinitionBuilder(Map.add modulePath (public' moduleDef) modules)
```

**Usage (Current)**:
```fsharp
let myPackage = packageDef {
    Module(modulePath "com.example.Customer", customerModule)
    Module(modulePath "com.example.Order", orderModule)
    PrivateModule(modulePath "com.example.Internal", internalModule)
}
```

**Options**:
1. **Keep builder pattern** - Fluent chaining without CE syntax
2. **Convert to CE pattern** - Add CustomOperations like refactored Modules DSL
3. **Hybrid** - Support both builder chaining AND CE syntax

**Discussion Points**:
- Should we follow the same pattern as the refactored Modules DSL?
- Packages are simpler than Modules (just collections of modules with paths)
- Module paths add complexity - need clean syntax for hierarchical paths

### Q2: How Should Module Path Handling Work?

**Current**: Direct ModulePath parameter
```fsharp
member this.Module(modulePath: ModulePath, moduleDef: ModuleDefinition)
```

**Alternative Options**:

**Option A: String-based paths**
```fsharp
// CE with string paths
packageDef {
    moduledef "com.example.Customer" customerModule
    moduledef "com.example.Order" orderModule
}
```

**Option B: List-based paths**
```fsharp
// CE with list paths
packageDef {
    moduledef ["com"; "example"; "Customer"] customerModule
    moduledef ["com"; "example"; "Order"] orderModule
}
```

**Option C: Nested path builder**
```fsharp
// CE with nested path construction
packageDef {
    moduledef (modulePath { "com.example.Customer" }) customerModule
    moduledef (path ["com"; "example"; "Order"]) orderModule
}
```

**Option D: Hierarchical sections**
```fsharp
// CE with hierarchical sections (inspired by namespaces)
packageDef {
    namespace' "com.example" {
        moduledef "Customer" customerModule
        moduledef "Order" orderModule
    }

    namespace' "com.internal" {
        privateModule "Utils" utilsModule
    }
}
```

**Discussion Points**:
- String paths are simplest but lose type safety
- List paths are more explicit but verbose
- Nested sections match common package organization patterns
- ModulePath CE builder already exists - should we leverage it?

### Q3: How Should Module Access Control Work?

**Current**: Separate methods for public/private
```fsharp
member this.Module(modulePath, moduleDef)         // Public
member this.PrivateModule(modulePath, moduleDef)  // Private
```

**Alternative Options**:

**Option A: Explicit CustomOperations**
```fsharp
packageDef {
    publicModule "com.example.Customer" customerModule
    privateModule "com.internal.Utils" utilsModule
}
```

**Option B: Access parameter**
```fsharp
packageDef {
    moduledef "com.example.Customer" customerModule Public
    moduledef "com.internal.Utils" utilsModule Private
}
```

**Option C: Nested access sections**
```fsharp
packageDef {
    pub {
        moduledef "com.example.Customer" customerModule
        moduledef "com.example.Order" orderModule
    }

    private' {
        moduledef "com.internal.Utils" utilsModule
    }
}
```

**Discussion Points**:
- Should access control be at module level or namespace level?
- Consistency with Modules DSL (which uses `pub { }` sections)
- Default should be public (align with API-first philosophy)

### Q4: Should We Support CustomOperations?

**Current**: No CustomOperations, only method calls

**Potential CustomOperations**:
- `moduledef` / `module'` - Add module (avoid keyword)
- `publicModule` / `privateModule` - Explicit access control
- `namespace'` - Group modules by path prefix
- `pub` / `private'` - Access control sections
- `extend` - Include modules from another package

**Pros**:
- Consistency with refactored Modules DSL
- Query-style syntax feels declarative
- Lowercase naming follows Fun.Blazor convention

**Cons**:
- Packages are simple - might not need full CE machinery
- "module" is a keyword, requires workarounds (`module'`, `moduledef`)
- Builder pattern already works well

### Q5: Package Composition/Merging?

**Current**: `Combine` merges two builders, later entries override earlier ones
```fsharp
member _.Combine(builder1, builder2) =
    PackageDefinitionBuilder(
        Map.fold (fun acc k v -> Map.add k v acc) builder1.Modules builder2.Modules
    )
```

**Questions**:
1. **Should we support extending existing packages?**
   ```fsharp
   packageDef {
       extend basePackage  // Include all modules from basePackage
       moduledef "com.example.New" newModule
   }
   ```

2. **What should happen on module path collision?**
   - Last wins (current)
   - Error on collision
   - Merge with warning

3. **Should we support package unions/intersections?**
   ```fsharp
   let combinedPackage = packageDef {
       merge package1
       merge package2
       // How to handle overlapping modules?
   }
   ```

## Use Cases to Explore

### Use Case 1: Simple Package with Few Modules

```fsharp
// Define a simple utility package
let stringUtilsPackage = packageDef {
    moduledef "com.example.StringUtils" stringUtilsModule
    moduledef "com.example.TextUtils" textUtilsModule
}
```

**Expectations**:
- Concise syntax for small packages
- Clear module paths
- All modules public by default

### Use Case 2: Large Package with Many Modules

```fsharp
// Define a domain package with hierarchical organization
let ecommercePackage = packageDef {
    // Public API modules
    moduledef "com.example.ecommerce.Customer" customerModule
    moduledef "com.example.ecommerce.Order" orderModule
    moduledef "com.example.ecommerce.Product" productModule
    moduledef "com.example.ecommerce.Payment" paymentModule

    // Internal modules
    privateModule "com.example.ecommerce.internal.Validation" validationModule
    privateModule "com.example.ecommerce.internal.Utils" utilsModule
}
```

**Expectations**:
- Scannable structure with clear hierarchy
- Public/private distinction visible
- Path prefixes show organization

### Use Case 3: Package with Namespace Grouping

```fsharp
// Define a package using namespace sections
let ecommercePackage = packageDef {
    namespace' "com.example.ecommerce" {
        pub {
            moduledef "Customer" customerModule
            moduledef "Order" orderModule
            moduledef "Product" productModule
        }

        private' {
            moduledef "Validation" validationModule
            moduledef "Utils" utilsModule
        }
    }

    namespace' "com.example.analytics" {
        pub {
            moduledef "Reporting" reportingModule
        }
    }
}
```

**Expectations**:
- Namespace sections reduce repetition
- Clear public/private grouping
- Hierarchical organization matches domain structure

### Use Case 4: Programmatic Package Construction

```fsharp
// Build package from lists of modules
let publicModules = [
    ("com.example.Customer", customerModule)
    ("com.example.Order", orderModule)
]

let privateModules = [
    ("com.internal.Utils", utilsModule)
]

let myPackage = packageDef {
    for (path, moduleDef) in publicModules do
        moduledef path moduleDef

    for (path, moduleDef) in privateModules do
        privateModule path moduleDef
}
```

**Expectations**:
- Programmatic construction from data
- Support for loops and conditional logic
- Mix programmatic and declarative styles

### Use Case 5: Package Composition

```fsharp
// Extend a base package with additional modules
let basePackage = packageDef {
    moduledef "com.example.Core" coreModule
}

let extendedPackage = packageDef {
    extend basePackage
    moduledef "com.example.Extensions" extensionsModule
}

// Merge multiple packages
let combinedPackage = packageDef {
    merge customerPackage
    merge orderPackage
    // Handle overlapping modules?
}
```

**Expectations**:
- Reuse existing packages
- Clear composition semantics
- Handle module conflicts gracefully

## Comparison with Elm

### Elm Package Structure
Elm doesn't have explicit "package" syntax - packages are defined in `elm.json`:
```json
{
  "type": "package",
  "name": "author/package",
  "version": "1.0.0",
  "exposed-modules": [
    "Module1",
    "Module2"
  ],
  "source-directories": ["src"]
}
```

Modules are files in the source directory:
```
src/
  Module1.elm
  Module2.elm
  Internal/
    Utils.elm  (not exposed)
```

### Morphir-Dotnet Packages DSL
```fsharp
let myPackage = packageDef {
    moduledef "Module1" module1
    moduledef "Module2" module2
    privateModule "Internal.Utils" utilsModule
}
```

**Observations**:
- Elm uses filesystem + elm.json for package structure
- Morphir uses programmatic DSL for package definition
- Both distinguish public (exposed) vs private modules
- Morphir's approach is more explicit and programmatic

## BDD Scenarios

### Scenario 1: Create Simple Package Specification

```gherkin
Feature: Package Specification Creation

Scenario: Create a package specification with multiple modules
  Given I have module specifications for "Customer" and "Order"
  When I create a package specification with:
    | Module Path           | Module Specification  |
    | com.example.Customer  | customerModuleSpec    |
    | com.example.Order     | orderModuleSpec       |
  Then the package specification should contain 2 modules
  And the package should have module at path "com.example.Customer"
  And the package should have module at path "com.example.Order"
```

### Scenario 2: Create Package Definition with Access Control

```gherkin
Feature: Package Definition with Access Control

Scenario: Create a package with public and private modules
  Given I have module definitions for "Customer", "Order", and "Utils"
  When I create a package definition with:
    | Access  | Module Path              | Module Definition  |
    | Public  | com.example.Customer     | customerModule     |
    | Public  | com.example.Order        | orderModule        |
    | Private | com.example.internal.Utils | utilsModule      |
  Then the package should have 3 modules (2 public, 1 private)
  And module "com.example.Customer" should be public
  And module "com.example.Order" should be public
  And module "com.example.internal.Utils" should be private
```

### Scenario 3: Programmatic Package Construction

```gherkin
Feature: Programmatic Package Construction

Scenario: Build a package from a list of module definitions
  Given I have a list of module definitions:
    | Module Path           | Module Definition  |
    | com.example.Customer  | customerModule     |
    | com.example.Order     | orderModule        |
    | com.example.Product   | productModule      |
  When I programmatically add each module to the package builder
  And I run the builder
  Then the resulting package should contain 3 modules
  And all modules should be public by default
```

### Scenario 4: Package with Hierarchical Module Organization

```gherkin
Feature: Hierarchical Module Organization

Scenario: Create a package with namespace-style organization
  Given I have modules organized by namespace
  When I create a package using namespace sections:
    | Namespace               | Access  | Module Name | Module Definition |
    | com.example.ecommerce   | Public  | Customer    | customerModule    |
    | com.example.ecommerce   | Public  | Order       | orderModule       |
    | com.example.ecommerce   | Private | Validation  | validationModule  |
    | com.example.analytics   | Public  | Reporting   | reportingModule   |
  Then the package should contain 4 modules
  And module "com.example.ecommerce.Customer" should be public
  And module "com.example.ecommerce.Validation" should be private
  And modules should be organized by namespace prefix
```

### Scenario 5: Package Composition

```gherkin
Feature: Package Composition

Scenario: Extend a base package with additional modules
  Given I have a base package with modules:
    | Module Path        | Access |
    | com.example.Core   | Public |
  When I create an extended package that:
    - Includes all modules from the base package
    - Adds new module "com.example.Extensions"
  Then the extended package should contain 2 modules
  And module "com.example.Core" should be included
  And module "com.example.Extensions" should be included

Scenario: Merge packages with conflicting modules
  Given I have package A with module "com.example.Shared"
  And I have package B with module "com.example.Shared"
  When I attempt to merge the packages
  Then the operation should [error/warn/use last] based on merge strategy
```

## Design Decisions (To Be Finalized)

### Decision 1: CE Pattern with CustomOperations
**Question**: Should we convert to CE pattern like Modules DSL?

**Options**:
- ✅ **CE Pattern with CustomOperations** - Consistency with Modules DSL
- ❌ Keep builder pattern - Simpler for packages
- ❌ Hybrid approach - Too complex

**Proposed Decision**: ✅ **CE Pattern with CustomOperations**
**Rationale**:
- Consistency across all DSLs (Types, Values, Patterns, Modules, Packages)
- Declarative syntax matches domain modeling
- Supports programmatic construction via `for` loops
- Fun.Blazor-style lowercase operations feel natural

### Decision 2: Module Path Handling
**Question**: How should module paths be specified?

**Options**:
1. **String paths**: `moduledef "com.example.Customer" customerModule`
2. **List paths**: `moduledef ["com"; "example"; "Customer"] customerModule`
3. **ModulePath CE**: `moduledef (modulePath { "com.example.Customer" }) customerModule`
4. **Namespace sections**: Hierarchical grouping (see Option D in Q2)

**Proposed Decision**: ✅ **Hybrid: String paths + Namespace sections**
**Rationale**:
- String paths are simplest for direct module addition
- Namespace sections reduce repetition for organized packages
- Both patterns serve different use cases
- String parsing to ModulePath handled automatically

**Example**:
```fsharp
packageDef {
    // Direct string paths
    moduledef "com.utils.Strings" stringUtilsModule

    // Namespace sections for organization
    namespace' "com.example.ecommerce" {
        pub {
            moduledef "Customer" customerModule
            moduledef "Order" orderModule
        }
    }
}
```

### Decision 3: Access Control Syntax
**Question**: How should module access control be specified?

**Options**:
1. Separate operations: `publicModule`, `privateModule`
2. Access parameter: `moduledef path module access`
3. Nested sections: `pub { }`, `private' { }`

**Proposed Decision**: ✅ **All three supported**
**Rationale**:
- Default `moduledef` creates public module (API-first)
- Explicit operations (`publicModule`, `privateModule`) for clarity
- Nested sections (`pub { }`, `private' { }`) for grouping
- Flexibility for different coding styles

**Examples**:
```fsharp
packageDef {
    // Default is public
    moduledef "com.example.Customer" customerModule

    // Explicit operations
    publicModule "com.example.Order" orderModule
    privateModule "com.internal.Utils" utilsModule

    // Nested sections
    pub {
        moduledef "com.example.Product" productModule
    }

    private' {
        moduledef "com.internal.Validation" validationModule
    }
}
```

### Decision 4: Naming Options
**Question**: What names should we use for operations?

**Options**:
- `module'` - Tick avoids keyword (like Modules DSL `typedef`, `valuedef`)
- `moduledef` - More explicit, self-documenting
- `mod` - Short, but might be confusing
- `publicModule` / `privateModule` - Very explicit

**Proposed Decision**: ✅ **Support both `module'` and `moduledef`**
**Rationale**:
- `moduledef` - Primary, explicit, consistent with `typedef`/`valuedef`
- `module'` - Alternative for minimal syntax preference
- Both compile to same operation
- User choice based on style preference

### Decision 5: Package Composition
**Question**: Should we support package composition?

**Options**:
1. No composition - packages are standalone
2. Simple extension - `extend basePackage`
3. Full composition - `extend`, `merge`, `combine` with conflict handling

**Proposed Decision**: ✅ **Phased approach: Start with `extend`, add `merge` later**
**Rationale**:
- `extend` covers common use case (base + additions)
- Error on module path collision initially (safe default)
- `merge` strategies can be added in Phase 2 based on real needs
- Keep initial implementation simple

**Phase 1 Example**:
```fsharp
packageDef {
    extend basePackage  // Includes all modules, errors on collision
    moduledef "com.example.New" newModule
}
```

## Proposed API Examples

### Example 1: Simple Package (Direct Paths)

**Current Style**:
```fsharp
let utilsPackage =
    let builder = PackageDefinitionBuilder()
    builder.Module(modulePath "com.utils.Strings", stringUtilsModule)
           .Module(modulePath "com.utils.Collections", collectionsModule)
           .PrivateModule(modulePath "com.utils.internal.Helpers", helpersModule)
```

**Proposed CE Style**:
```fsharp
let utilsPackage = packageDef {
    moduledef "com.utils.Strings" stringUtilsModule
    moduledef "com.utils.Collections" collectionsModule
    privateModule "com.utils.internal.Helpers" helpersModule
}
```

### Example 2: Package with Namespace Sections

**Proposed CE Style**:
```fsharp
let ecommercePackage = packageDef {
    namespace' "com.example.ecommerce" {
        pub {
            moduledef "Customer" customerModule
            moduledef "Order" orderModule
            moduledef "Product" productModule
            moduledef "Payment" paymentModule
        }

        private' {
            moduledef "Validation" validationModule
            moduledef "Utils" utilsModule
        }
    }

    namespace' "com.example.analytics" {
        pub {
            moduledef "Reporting" reportingModule
            moduledef "Metrics" metricsModule
        }
    }
}
```

### Example 3: Programmatic Construction

**Proposed CE Style**:
```fsharp
let publicModuleDefs = [
    ("Customer", customerModule)
    ("Order", orderModule)
    ("Product", productModule)
]

let privateModuleDefs = [
    ("Validation", validationModule)
]

let myPackage = packageDef {
    namespace' "com.example.ecommerce" {
        pub {
            for (name, moduleDef) in publicModuleDefs do
                moduledef name moduleDef
        }

        private' {
            for (name, moduleDef) in privateModuleDefs do
                moduledef name moduleDef
        }
    }
}
```

### Example 4: Package Composition

**Proposed CE Style**:
```fsharp
let basePackage = packageDef {
    moduledef "com.example.Core" coreModule
    moduledef "com.example.Common" commonModule
}

let extendedPackage = packageDef {
    extend basePackage

    namespace' "com.example.extensions" {
        pub {
            moduledef "Advanced" advancedModule
            moduledef "Experimental" experimentalModule
        }
    }
}
```

### Example 5: Mixed Syntax (All Features)

**Proposed CE Style**:
```fsharp
let fullPackage = packageDef {
    // Extend base package
    extend corePackage

    // Direct module additions
    publicModule "com.example.Standalone" standaloneModule

    // Namespace organization
    namespace' "com.example.domain" {
        pub {
            moduledef "Customer" customerModule
            moduledef "Order" orderModule
        }

        private' {
            moduledef "Internal" internalModule
        }
    }

    // Programmatic additions
    namespace' "com.example.generated" {
        pub {
            for moduleDef in generatedModules do
                moduledef moduleDef.Name moduleDef
        }
    }
}
```

## Implementation Plan

### Phase 1: Core CE Pattern with CustomOperations ✅ COMPLETE
**Goal**: Convert to CE pattern like Modules DSL

**Tasks**:
1. ✅ PackageDefinitionBuilder CE pattern implementation
   - Add `Yield`, `Zero`, `Delay`, `Run` ✅
   - Add `Combine` for merging states ✅
   - Add `For` for iteration support ✅
   - Add CustomOperations:
     - `moduledef` - Add public module (default) ✅
     - `publicModule` - Explicit public module ✅
     - `privateModule` - Explicit private module ✅

2. ✅ String path handling
   - Auto-convert strings to ModulePath using `ModulePath.modulePathFromString` ✅
   - Support dot-notation: "com.example.Module" ✅
   - Support single-segment and deep hierarchical paths ✅

3. ✅ Comprehensive testing
   - 13 new tests in PackagesTests.fs ✅
   - Test empty package creation ✅
   - Test module addition with moduledef ✅
   - Test access control (public/private) ✅
   - Test string path handling (dot-notation, single, deep) ✅
   - Test package composition (combine, collision handling) ✅
   - Test multiple module addition ✅
   - All 308 tests passing (295 + 13 new) ✅

**Implementation Details**:
- Created `PackageState` record type
- Created `PackageBuilder` class with CE methods
- Global `pkg` builder instance for use
- Note: CustomOperations cannot be used inside `for` loops (F# CE limitation)

**Status**: Completed 2025-12-26

### Phase 2: Package Composition ✅ COMPLETE
**Goal**: Support package extension and reuse

**Tasks**:
1. ✅ Implement `extend` CustomOperation
   - Include all modules from base package ✅
   - Error on module path collision ✅
   - Preserve access control from base package ✅
   - Test module preservation ✅

2. ✅ Comprehensive testing
   - 5 new tests in PackagesTests.fs ✅
   - Test basic package extension ✅
   - Test access control preservation ✅
   - Test collision detection (error on duplicate paths) ✅
   - Test empty package extension ✅
   - Test multiple sequential extensions ✅
   - All 313 tests passing (308 + 5 new) ✅

**Implementation Details**:
- Added `extend` CustomOperation to `PackageBuilder`
- Errors on module path collision (safe default)
- Preserves both module definitions and access control
- Supports multiple sequential extends

**Example Usage**:
```fsharp
let basePackage = pkg {
    moduledef "com.example.Core" coreModule
    moduledef "com.example.Common" commonModule
}

let extendedPackage = pkg {
    extend basePackage
    moduledef "com.example.Advanced" advancedModule
}
```

**Status**: Completed 2025-12-26

### Phase 3: Nested Sections (Future - Optional)
**Goal**: Support namespace-style organization with nested CE builders

**Note**: Deferred due to F# CE complexity. Current string path approach already supports hierarchical organization.

**Alternative Pattern** (Works Today):
```fsharp
// Hierarchical organization using string prefixes
let ecommercePackage = pkg {
    // Public API
    moduledef "com.example.ecommerce.Customer" customerModule
    moduledef "com.example.ecommerce.Order" orderModule
    moduledef "com.example.ecommerce.Product" productModule

    // Private internals
    privateModule "com.example.ecommerce.internal.Validation" validationModule
    privateModule "com.example.ecommerce.internal.Utils" utilsModule
}
```

**Future Tasks** (if strong demand):
5. ⏳ Implement `namespace'` CustomOperation
   - Nested CE builder for namespace context
   - Prefix handling for module paths

6. ⏳ Implement `pub { }` and `private' { }` sections
   - Public/private section builders
   - Auto-apply access control to modules within

### Phase 4: Advanced Composition (Future - Optional)
**Goal**: Merge strategies and advanced collision handling

**Tasks**:
7. ⏳ Design merge strategies
   - Research real-world needs
   - Define collision handling (error, warn, override)
   - Document merge semantics

8. ⏳ Implement `merge` operation (if needed)
    - Choose merge strategy
    - Implement conflict resolution
    - Test merge scenarios

### Phase 4: Testing & Documentation
**Goal**: Comprehensive testing and user documentation

**Tasks**:
11. ⏳ Implement all BDD scenarios
    - Simple package creation
    - Hierarchical organization
    - Programmatic construction
    - Package composition
    - Access control verification

12. ⏳ Add unit tests
    - CE operations
    - Path handling
    - Access control
    - Composition operations

13. ⏳ Document patterns and examples
    - Migration guide from builder pattern
    - Best practices for package organization
    - Namespace conventions
    - Composition patterns

14. ⏳ Performance testing
    - Large packages (100+ modules)
    - Deep namespace hierarchies
    - Composition overhead

## Technical Implementation Notes

### State Structure

```fsharp
// PackageSpecification state
type PackageSpecState = {
    Modules: Map<ModulePath, ModuleSpecification<unit>>
}

// PackageDefinition state
type PackageDefState = {
    Modules: Map<ModulePath, AccessControlled<ModuleDefinition<unit, unit>>>
}
```

### CustomOperations

```fsharp
type PackageDefinitionBuilder() =
    member _.Yield(()) = { Modules = Map.empty }

    [<CustomOperation("moduledef")>]
    member _.ModuleDef(state, pathStr: string, moduleDef: ModuleDefinition<unit, unit>) =
        let path = ModulePath.modulePathFromString pathStr
        { state with Modules = Map.add path (public' moduleDef) state.Modules }

    [<CustomOperation("module'")>]
    member _.Module'(state, pathStr: string, moduleDef: ModuleDefinition<unit, unit>) =
        // Alias for moduledef
        let path = ModulePath.modulePathFromString pathStr
        { state with Modules = Map.add path (public' moduleDef) state.Modules }

    [<CustomOperation("publicModule")>]
    member _.PublicModule(state, pathStr: string, moduleDef: ModuleDefinition<unit, unit>) =
        let path = ModulePath.modulePathFromString pathStr
        { state with Modules = Map.add path (public' moduleDef) state.Modules }

    [<CustomOperation("privateModule")>]
    member _.PrivateModule(state, pathStr: string, moduleDef: ModuleDefinition<unit, unit>) =
        let path = ModulePath.modulePathFromString pathStr
        { state with Modules = Map.add path (private' moduleDef) state.Modules }

    [<CustomOperation("extend")>]
    member _.Extend(state, package: PackageDefinition<unit, unit>) =
        // Merge modules, error on collision
        let mergedModules =
            Map.fold (fun acc path moduleDef ->
                if Map.containsKey path acc then
                    failwithf "Module path collision: %A already exists" path
                else
                    Map.add path moduleDef acc
            ) state.Modules package.Modules
        { state with Modules = mergedModules }

    member _.Run(state) = packageDefinition state.Modules
```

### Namespace Section Builder

```fsharp
type NamespaceSectionBuilder(prefix: string) =
    member _.Yield(()) = []

    [<CustomOperation("moduledef")>]
    member _.ModuleDef(items, name: string, moduleDef) =
        let fullPath = sprintf "%s.%s" prefix name
        (fullPath, moduleDef, Public) :: items

    member _.Run(items) = items

type PublicSectionBuilder(prefix: string option) =
    member _.Yield(()) = []

    [<CustomOperation("moduledef")>]
    member _.ModuleDef(items, name: string, moduleDef) =
        let path =
            match prefix with
            | Some p -> sprintf "%s.%s" p name
            | None -> name
        (path, moduleDef, Public) :: items

    member _.Run(items) = items

// In PackageDefinitionBuilder:
[<CustomOperation("namespace'")>]
member _.Namespace'(state, prefix: string, items: (string * ModuleDefinition<unit, unit> * Access) list) =
    let modules =
        items
        |> List.fold (fun acc (pathStr, moduleDef, access) ->
            let path = ModulePath.modulePathFromString pathStr
            let accessControlled =
                match access with
                | Public -> public' moduleDef
                | Private -> private' moduleDef
            Map.add path accessControlled acc
        ) state.Modules
    { state with Modules = modules }

[<CustomOperation("pub")>]
member _.Pub(state, items: (string * ModuleDefinition<unit, unit> * Access) list) =
    // Similar to namespace' but no prefix
    ...
```

### String to ModulePath Conversion

```fsharp
// In ModulePath module or Packages DSL
let modulePathFromString (pathStr: string) : ModulePath =
    // Split by dots: "com.example.Customer" -> ["com"; "example"; "Customer"]
    let parts = pathStr.Split('.') |> Array.toList
    let names = parts |> List.map Name.fromString
    ModulePath.modulePathFromList names
```

## Success Criteria

- [ ] CE pattern with CustomOperations works
- [ ] String path conversion (`"com.example.Module"`) works
- [ ] Both `moduledef` and `module'` supported
- [ ] `publicModule` and `privateModule` explicit operations work
- [ ] `namespace'` sections for hierarchical organization work
- [ ] `pub { }` and `private' { }` sections work
- [ ] `extend` package composition works
- [ ] Error on module path collision
- [ ] All BDD scenarios pass
- [ ] Documentation examples work
- [ ] Consistent with refactored Modules DSL
- [ ] Programmatic construction with `for` loops works
- [ ] Migration path from current builder pattern documented

## Differences from Modules DSL

| Aspect | Modules DSL | Packages DSL |
|--------|-------------|--------------|
| **Aggregation** | Types and Values | Modules |
| **Path Handling** | Names (simple) | ModulePath (hierarchical) |
| **Organization** | Single level | Multi-level (namespaces) |
| **Access Control** | Per type/value | Per module |
| **Composition** | Rare | Common (package extension) |
| **Namespace Support** | N/A | `namespace'` sections |

**Key Insight**: Packages need stronger support for hierarchical organization (namespaces) because module paths are naturally hierarchical.

## Next Steps

1. ⏳ **Phase 1 Implementation**: Convert to CE pattern with CustomOperations
   - Start with PackageDefinitionBuilder
   - Add string path handling
   - Implement core operations: `moduledef`, `publicModule`, `privateModule`

2. ⏳ **Phase 2 Implementation**: Add namespace sections
   - Implement `namespace'` builder
   - Implement `pub { }` and `private' { }` sections
   - Test hierarchical organization

3. ⏳ **Phase 3 Implementation**: Add package composition
   - Implement `extend` operation
   - Add collision detection
   - Consider merge strategies

4. ⏳ **Testing**: BDD scenarios and unit tests
   - All scenarios from this document
   - Edge cases (empty packages, deep hierarchies, collisions)
   - Performance tests

5. ⏳ **Documentation**: Examples and migration guide
   - Update user documentation
   - Create migration guide from builder pattern
   - Document best practices for package organization

## Related Documents

- [CE DSL Modules](./ce-dsl-modules.md) - Completed Modules DSL refactoring (our template)
- [CE DSL Types](./ce-dsl-types.md) - Types DSL patterns
- [CE DSL Values](./ce-dsl-values.md) - Values DSL patterns
- [CE DSL Patterns](./ce-dsl-patterns.md) - Patterns DSL
- [AGENTS.md](../../AGENTS.md) - Project guidance and conventions

## Open Questions for Review

1. **Namespace syntax**: Should `namespace'` be the primary method, or just convenience?
2. **Default access**: Should modules be public by default (API-first) or require explicit access?
3. **Path validation**: Should we validate module paths (e.g., must start with reverse domain)?
4. **Merge semantics**: What strategies do we need for package merging?
5. **Performance**: How do large packages (100+ modules) perform with this DSL?
6. **F# modules vs Morphir modules**: How to avoid confusion with F# `module` keyword?

---

**Status**: Ready for team review and feedback. Please add comments, questions, and suggestions.
