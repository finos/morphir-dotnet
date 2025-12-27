# Distributions DSL Design Review

**Status**: 🔄 In Review
**Date**: 2025-12-26
**Reviewers**: morphir-dotnet team

## Executive Summary

The Distributions DSL provides builder-style APIs for creating Morphir IR distribution values. Distributions are the top-level organizational unit in Morphir, representing a complete, self-contained package with all its dependencies. This is Phase 3 of the DSL modernization effort, following the successful completion of Modules and Packages DSL refactoring.

**Current State**:
- ✅ Has builder-based API (similar to pre-refactored Modules/Packages DSL)
- ✅ Supports Library distribution (the only distribution type currently)
- ✅ Has fluent methods: `.Library()`, `.Package()`, `.Dependency()`
- ❓ Uses inheritance pattern (`new()` constructor creates base builder with state)
- ❓ No CustomOperations - only direct method calls
- ❓ Different pattern from refactored Modules and Packages DSLs
- ❓ Builder pattern vs Computation Expression pattern

**Key Insight**: Distributions are the simplest of all DSLs - just three components: package name, dependencies, and package definition. The main complexity is in managing dependencies (map of package names to package specifications).

## Distribution IR Structure

### Distribution (Library Only)

```fsharp
type Distribution<'typeAttributes, 'valueAttributes> =
    | Library of PackageName * Map<PackageName, PackageSpecification<'typeAttributes>> * PackageDefinition<'typeAttributes, 'valueAttributes>
```

**Purpose**: Represents a complete, self-contained package of Morphir code with all dependencies

**Components**:
1. **PackageName** - Name of this distribution/package
2. **Dependencies** - Map of package names to their specifications (external packages)
3. **PackageDefinition** - The actual package implementation (modules, types, values)

**Characteristics**:
- Currently only Library distributions are supported
- Dependencies are just specifications (type signatures), not implementations
- The package definition contains the full implementation

### PackageName Structure

```fsharp
type PackageName = PackageName of Path

// Example usage:
let pkgName = PackageName.packageName (Path.fromString "com.example.myapp")
// Represents: com.example.myapp
```

**Purpose**: Identifies a package in the Morphir ecosystem (like Maven/NPM package names)

## Design Questions to Explore

### Q1: Builder Pattern vs CE Pattern?

**Current**: Builder pattern with stateful accumulation

```fsharp
type DistributionBuilder<'ta, 'va>(packageName, dependencies, packageDefinition) =
    new() = DistributionBuilder(None, Map.empty, None)

    member this.Library(pkgName) =
        DistributionBuilder(Some pkgName, dependencies, packageDefinition)
```

**Usage (Current)**:
```fsharp
let myDist = distribution {
    Library ["com"; "example"; "myapp"]
    Package myPackage
    Dependency (PackageName.fromString "com.acme.utils", utilsSpec)
}
```

**Options**:
1. **Keep builder pattern** - Fluent chaining without CE syntax
2. **Convert to CE pattern** - Add CustomOperations like refactored Modules/Packages DSLs
3. **Hybrid** - Support both builder chaining AND CE syntax

**Discussion Points**:
- Should we follow the same pattern as refactored Modules and Packages DSLs?
- Distributions are the simplest - just three fields
- Package name setting vs dependencies addition - different operations

### Q2: How Should Package Name Handling Work?

**Current**: Two overloads for setting package name

```fsharp
member this.Library(pkgName: PackageName)           // Full type
member this.Library(strs: string list)              // String list
```

**Alternative Options**:

**Option A: String-based package names**
```fsharp
// CE with string paths
distribution {
    library "com.example.myapp"
    package myPackage
}
```

**Option B: List-based package names**
```fsharp
// CE with list paths
distribution {
    library ["com"; "example"; "myapp"]
    package myPackage
}
```

**Option C: Dot-notation strings** (like Packages DSL)
```fsharp
// CE with dot-notation conversion
distribution {
    library "com.example.myapp"  // Auto-converts to PackageName
    package myPackage
}
```

**Discussion Points**:
- String dot-notation is simplest and most familiar
- PackageName conversion already exists
- Consistency with Packages DSL string path handling

### Q3: How Should Dependency Management Work?

**Current**: Method call for each dependency

```fsharp
member this.Dependency(pkgName: PackageName, spec: PackageSpecification<'ta>)
```

**Alternative Options**:

**Option A: Single dependency CustomOperation**
```fsharp
distribution {
    library "com.example.myapp"
    package myPackage
    dependency "com.acme.utils" utilsSpec
    dependency "com.acme.http" httpSpec
}
```

**Option B: Dependencies block**
```fsharp
distribution {
    library "com.example.myapp"
    package myPackage

    dependencies {
        add "com.acme.utils" utilsSpec
        add "com.acme.http" httpSpec
    }
}
```

**Option C: Programmatic dependencies list**
```fsharp
let deps = [
    ("com.acme.utils", utilsSpec)
    ("com.acme.http", httpSpec)
]

distribution {
    library "com.example.myapp"
    package myPackage

    for (name, spec) in deps do
        dependency name spec
}
```

**Discussion Points**:
- Option A is simplest and most declarative
- Option B groups dependencies but adds complexity
- Option C requires `For` support (has CE limitations)
- Most distributions have 0-10 dependencies, not hundreds

### Q4: Should We Support CustomOperations?

**Current**: No CustomOperations, only method calls

**Potential CustomOperations**:
- `library` - Set package name (required)
- `package` - Set package definition (required)
- `dependency` / `dep` - Add dependency (optional, repeatable)

**Pros**:
- Consistency with refactored Modules and Packages DSLs
- Query-style syntax feels declarative
- Lowercase naming follows Fun.Blazor convention

**Cons**:
- Distributions are very simple - might not need full CE machinery
- Only three fields to set
- Builder pattern already works well

### Q5: Distribution Validation?

**Current**: `Run` uses `Option.defaultValue` for missing fields

```fsharp
let pkgName =
    builder.PackageName
    |> Option.defaultValue PackageName.emptyPackageName
let pkgDef =
    builder.PackageDefinition
    |> Option.defaultValue (packageDefinition Map.empty)
```

**Questions**:
1. **Should we require package name?**
   - Current: Uses empty package name if not provided
   - Alternative: Error if package name not set

2. **Should we require package definition?**
   - Current: Uses empty package definition if not provided
   - Alternative: Error if package not set

3. **Should we validate dependencies?**
   - Check for circular dependencies?
   - Check for missing dependencies?
   - Validate package name format?

**Discussion Points**:
- Empty defaults hide errors
- Explicit validation gives better error messages
- Morphir philosophy: Make illegal states unrepresentable

## Use Cases to Explore

### Use Case 1: Simple Distribution (No Dependencies)

```fsharp
// Define a simple standalone package
let myDist = distribution {
    library "com.example.myapp"
    package myPackage
}
```

**Expectations**:
- Concise syntax for simple case
- Clear package name specification
- No dependencies needed

### Use Case 2: Distribution with Dependencies

```fsharp
// Define a distribution with external dependencies
let myDist = distribution {
    library "com.example.myapp"
    package myPackage
    dependency "com.acme.utils" utilsSpec
    dependency "com.acme.http" httpSpec
    dependency "com.acme.json" jsonSpec
}
```

**Expectations**:
- Clear dependency declarations
- Each dependency listed separately
- Package name and spec clearly associated

### Use Case 3: Programmatic Construction

```fsharp
// Build distribution from data
let deps = [
    ("com.acme.utils", utilsSpec)
    ("com.acme.http", httpSpec)
]

let myDist = distribution {
    library "com.example.myapp"
    package myPackage

    // Add dependencies programmatically
    // Note: CustomOperations don't work in for loops (F# limitation)
}
```

**Expectations**:
- Support for programmatic construction
- Flexibility for generating distributions

### Use Case 4: Complex Package with Many Modules

```fsharp
// Distribution with a large package
let ecommercePkg = pkg {
    moduledef "com.example.ecommerce.Customer" customerModule
    moduledef "com.example.ecommerce.Order" orderModule
    moduledef "com.example.ecommerce.Product" productModule
    // ... many more modules
}

let myDist = distribution {
    library "com.example.ecommerce"
    package ecommercePkg
    dependency "com.acme.database" databaseSpec
    dependency "com.acme.auth" authSpec
}
```

**Expectations**:
- Composes well with Packages DSL
- Clear separation of package definition and distribution
- Dependencies are external packages

## Comparison with Elm

### Elm Distribution Structure

Elm distributions are defined in `elm.json`:

```json
{
  "type": "package",
  "name": "author/package",
  "version": "1.0.0",
  "exposed-modules": ["Module1"],
  "dependencies": {
    "elm/core": "1.0.0",
    "elm/http": "2.0.0"
  }
}
```

**Key Differences**:
- Elm uses JSON configuration
- Morphir uses F# DSL
- Elm has version constraints
- Morphir dependencies are specifications (types only)

## BDD Scenarios

### Feature: Distribution Creation

```gherkin
Scenario: Create simple distribution without dependencies
  Given I have a package definition
  When I create a distribution with library name "com.example.myapp"
  And I set the package to my package definition
  Then the distribution should have package name "com.example.myapp"
  And the distribution should have the correct package definition
  And the distribution should have 0 dependencies

Scenario: Create distribution with dependencies
  Given I have a package definition
  And I have two dependency specifications
  When I create a distribution with library name "com.example.myapp"
  And I set the package to my package definition
  And I add dependency "com.acme.utils" with its specification
  And I add dependency "com.acme.http" with its specification
  Then the distribution should have 2 dependencies
  And the dependencies should include "com.acme.utils"
  And the dependencies should include "com.acme.http"

Scenario: Set library name using string
  When I create a distribution with library "com.example.myapp"
  Then the package name should be "com.example.myapp"

Scenario: Set library name using list
  When I create a distribution with library ["com"; "example"; "myapp"]
  Then the package name should be "com.example.myapp"
```

## Design Decisions (To Be Finalized)

### Decision 1: CE Pattern with CustomOperations

**Question**: Should we convert to CE pattern like Modules/Packages DSLs?

**Options**:
- ✅ **CE Pattern with CustomOperations** - Consistency with other DSLs
- ❌ Keep builder pattern - Simpler for distributions
- ❌ Hybrid approach - Too complex

**Proposed Decision**: ✅ **CE Pattern with CustomOperations**

**Rationale**:
- Consistency across all DSLs (Types, Values, Patterns, Modules, Packages, Distributions)
- Declarative syntax matches domain modeling
- Lowercase operations feel natural (`library`, `package`, `dependency`)
- Even simple DSLs benefit from consistent patterns

### Decision 2: Package Name Handling

**Question**: How should package names be specified?

**Options**:
1. **String dot-notation**: `library "com.example.myapp"`
2. **List of strings**: `library ["com"; "example"; "myapp"]`
3. **PackageName type**: `library (PackageName.fromString "com.example.myapp")`

**Proposed Decision**: ✅ **String dot-notation with auto-conversion**

**Rationale**:
- Simplest syntax
- Consistent with Packages DSL string path handling
- Automatic conversion via `PackageName.fromString`
- Familiar format (reverse domain notation)

**Example**:
```fsharp
distribution {
    library "com.example.myapp"
    package myPackage
}
```

### Decision 3: Dependency Syntax

**Question**: How should dependencies be specified?

**Options**:
1. Single operation per dependency: `dependency "name" spec`
2. Dependencies block: `dependencies { add "name" spec }`
3. Both (flexible)

**Proposed Decision**: ✅ **Single operation per dependency**

**Rationale**:
- Simplest and most declarative
- Each dependency is independent
- No additional nesting needed
- Follows same pattern as `moduledef` in Packages DSL

**Example**:
```fsharp
distribution {
    library "com.example.myapp"
    package myPackage
    dependency "com.acme.utils" utilsSpec
    dependency "com.acme.http" httpSpec
}
```

### Decision 4: Required vs Optional Fields

**Question**: Should we validate required fields?

**Options**:
1. Use defaults (current) - empty package name, empty package
2. Require fields - error if not provided
3. Hybrid - require library name, default empty package

**Proposed Decision**: ✅ **Require library name, use empty package as default**

**Rationale**:
- Every distribution needs a name (identity)
- Empty package is valid (package with no modules)
- Balance between strictness and flexibility
- Better error messages for missing names

**Implementation**:
```fsharp
member _.Run(f: unit -> DistributionState) =
    let state = f()
    match state.PackageName with
    | None -> failwith "Distribution requires a library name (use 'library \"name\"')"
    | Some pkgName ->
        let pkgDef = state.Package |> Option.defaultValue (packageDefinition Map.empty)
        library pkgName state.Dependencies pkgDef
```

### Decision 5: Naming Conventions

**Question**: What names should we use for operations?

**Options**:
- `library` / `lib` - Set package name
- `package` / `pkg` - Set package definition
- `dependency` / `dep` - Add dependency

**Proposed Decision**: ✅ **Support both full and abbreviated names**

**Rationale**:
- `library` is primary (explicit, self-documenting)
- `package` is primary (consistent with Packages DSL)
- `dependency` is primary (explicit)
- Abbreviated versions for minimal syntax preference
- User choice based on style

## Proposed API Examples

### Example 1: Simple Distribution

**Current Style**:
```fsharp
let myDist =
    let builder = DistributionBuilder()
    builder.Library(["com"; "example"; "myapp"])
           .Package(myPackage)
```

**Proposed CE Style**:
```fsharp
let myDist = distribution {
    library "com.example.myapp"
    package myPackage
}
```

### Example 2: Distribution with Dependencies

**Proposed CE Style**:
```fsharp
let myDist = distribution {
    library "com.example.myapp"
    package myPackage
    dependency "com.acme.utils" utilsSpec
    dependency "com.acme.http" httpSpec
    dependency "com.acme.json" jsonSpec
}
```

### Example 3: Using Abbreviated Names

**Proposed CE Style**:
```fsharp
let myDist = dist {  // Using abbreviated builder name
    lib "com.example.myapp"
    pkg myPackage
    dep "com.acme.utils" utilsSpec
    dep "com.acme.http" httpSpec
}
```

### Example 4: Complex Distribution

**Proposed CE Style**:
```fsharp
// First define the package
let ecommercePkg = pkg {
    publicModule "com.example.ecommerce.Customer" customerModule
    publicModule "com.example.ecommerce.Order" orderModule
    publicModule "com.example.ecommerce.Product" productModule
    privateModule "com.example.ecommerce.internal.Utils" utilsModule
}

// Then create the distribution
let ecommerceDist = distribution {
    library "com.example.ecommerce"
    package ecommercePkg

    // External dependencies
    dependency "com.acme.database" databaseSpec
    dependency "com.acme.auth" authSpec
    dependency "com.acme.logging" loggingSpec
}
```

## Implementation Plan

### Phase 1: Core CE Pattern with CustomOperations ✅ Ready to Implement

**Goal**: Convert to CE pattern like Modules/Packages DSLs

**Tasks**:
1. ✅ Create DistributionState record type
   - PackageName: PackageName option
   - Dependencies: Map<PackageName, PackageSpecification<unit>>
   - Package: PackageDefinition<unit, unit> option

2. ✅ Create DistributionBuilder CE class
   - Add `Yield`, `Zero`, `Delay`, `Run`
   - Add `Combine` for merging states
   - Add CustomOperations:
     - `library` - Set package name (required)
     - `package` - Set package definition
     - `dependency` - Add dependency

3. ✅ String conversion helpers
   - Auto-convert strings to PackageName
   - Support dot-notation: "com.example.myapp"

4. ✅ Validation in Run
   - Require library name (error if not provided)
   - Default empty package if not provided
   - Return final Distribution

5. ✅ Update tests
   - Add CE pattern tests
   - Test string package name conversion
   - Test dependency management
   - Test validation (missing library name)

### Phase 2: Enhancements (Future)

**Tasks**:
6. ⏳ Add abbreviated builder (`dist`, `lib`, `pkg`, `dep`)
7. ⏳ Add dependency validation (optional)
8. ⏳ Support programmatic dependency construction

### Phase 3: Testing & Documentation (Future)

**Tasks**:
9. ⏳ Implement all BDD scenarios
10. ⏳ Add unit tests
11. ⏳ Document patterns and examples
12. ⏳ Migration guide from builder pattern

## Technical Implementation Notes

### State Structure

```fsharp
type DistributionState = {
    PackageName: PackageName option
    Dependencies: Map<PackageName, PackageSpecification<unit>>
    Package: PackageDefinition<unit, unit> option
}
```

### CustomOperations

```fsharp
type DistributionBuilder() =
    member _.Yield(()) =
        { PackageName = None; Dependencies = Map.empty; Package = None }

    [<CustomOperation("library")>]
    member _.library(state, nameStr: string) =
        let pkgName = PackageName.fromString nameStr
        { state with PackageName = Some pkgName }

    [<CustomOperation("package")>]
    member _.package(state, pkgDef: PackageDefinition<unit, unit>) =
        { state with Package = Some pkgDef }

    [<CustomOperation("dependency")>]
    member _.dependency(state, nameStr: string, spec: PackageSpecification<unit>) =
        let pkgName = PackageName.fromString nameStr
        { state with Dependencies = Map.add pkgName spec state.Dependencies }

    member _.Run(f) =
        let state = f()
        match state.PackageName with
        | None -> failwith "Distribution requires a library name"
        | Some pkgName ->
            let pkgDef = state.Package |> Option.defaultValue (packageDefinition Map.empty)
            library pkgName state.Dependencies pkgDef
```

### String to PackageName Conversion

```fsharp
// Check if this exists or needs to be added
let fromString (str: string) : PackageName =
    Path.fromString str |> packageName
```

## Success Criteria

- [ ] CE pattern with CustomOperations works
- [ ] String package name conversion (`"com.example.myapp"`) works
- [ ] `library`, `package`, `dependency` CustomOperations work
- [ ] Validation requires library name (error if missing)
- [ ] Empty package default works
- [ ] Multiple dependencies can be added
- [ ] All BDD scenarios pass
- [ ] Documentation examples work
- [ ] Consistent with refactored Modules and Packages DSLs
- [ ] Migration path from current builder pattern documented

## Differences from Modules and Packages DSLs

| Aspect | Modules DSL | Packages DSL | Distributions DSL |
|--------|-------------|--------------|-------------------|
| **Aggregation** | Types and Values | Modules | Packages |
| **Complexity** | Medium | Medium | Simple |
| **Required Fields** | None | None | Library name |
| **Collections** | Types, Values | Modules | Dependencies |
| **Hierarchical** | Single level | Multi-level paths | Flat |
| **Composition** | Rare | Common | Rare |

**Key Insight**: Distributions are the simplest DSL - just three fields with clear semantics.

## Next Steps

1. ⏳ **Phase 1 Implementation**: Convert to CE pattern with CustomOperations
   - Start with DistributionBuilder
   - Add string package name handling
   - Implement core operations: `library`, `package`, `dependency`
   - Add validation for required fields

2. ⏳ **Testing**: BDD scenarios and unit tests
   - Simple distribution creation
   - Dependency management
   - Validation testing
   - Edge cases (empty package, many dependencies)

3. ⏳ **Documentation**: Examples and migration guide
   - Update user documentation
   - Create migration guide from builder pattern
   - Document best practices

## Related Documents

- [CE DSL Modules](./ce-dsl-modules.md) - Completed Modules DSL refactoring
- [CE DSL Packages](./ce-dsl-packages.md) - Completed Packages DSL refactoring
- [CE DSL Types](./ce-dsl-types.md) - Types DSL patterns
- [CE DSL Values](./ce-dsl-values.md) - Values DSL patterns
- [AGENTS.md](../../AGENTS.md) - Project guidance and conventions

## Open Questions for Review

1. **Validation strictness**: How strict should we be about required fields?
2. **Empty defaults**: Is empty package a sensible default, or should we require it?
3. **Dependency validation**: Should we validate dependencies at DSL level?
4. **Abbreviated names**: Should we support `dist`, `lib`, `pkg`, `dep` aliases?
5. **PackageName format**: Should we validate package name format (reverse domain)?
6. **Multiple packages**: Should we ever support multiple packages in one distribution? (Currently Library only)
