# Modules DSL Design Review

**Status**: 🔄 In Review
**Date**: 2025-12-26
**Reviewers**: morphir-dotnet team

## Executive Summary

The Modules DSL provides builder-style APIs for creating Morphir IR module specifications and definitions. Unlike Types/Values/Patterns which create single values, Modules aggregate multiple type and value definitions with access control and documentation.

**Current State**:
- ✅ Has builder-based API (not traditional CE with Yield/Zero)
- ✅ Supports both ModuleSpecification (interface) and ModuleDefinition (implementation)
- ✅ Has fluent methods: `.Type()`, `.Value()`, `.PrivateType()`, `.PrivateValue()`, `.Doc()`
- ❓ Uses inheritance pattern (`new()` constructor creates base builder with state)
- ❓ No CustomOperations - only direct method calls
- ❓ Different pattern from Types/Values/Patterns DSLs
- ❓ Builder pattern vs Computation Expression pattern

## Module IR Structure

### ModuleSpecification (Public Interface)
```fsharp
type ModuleSpecification<'attributes> = {
    Types: Map<Name, Documented<TypeSpecification<'attributes>>>
    Values: Map<Name, Documented<ValueSpecification<'attributes>>>
    Doc: string option
}
```

**Purpose**: Defines the public API of a module (type signatures only, no implementations)

### ModuleDefinition (Full Implementation)
```fsharp
type ModuleDefinition<'typeAttributes, 'valueAttributes> = {
    Types: Map<Name, AccessControlled<Documented<TypeDefinition<'typeAttributes>>>>
    Values: Map<Name, AccessControlled<Documented<ValueDefinition<'typeAttributes, 'valueAttributes>>>>
    Doc: string option
}
```

**Purpose**: Complete module with all types and values (public + private) with full implementations

## Design Questions to Explore

### Q1: Builder Pattern vs Computation Expression Pattern?

**Current**: Builder pattern with stateful accumulation
```fsharp
type ModuleDefinitionBuilder<'ta, 'va>(types, values, doc) =
    new() = ModuleDefinitionBuilder(Map.empty, Map.empty, None)
    member this.Type(name, def) =
        ModuleDefinitionBuilder(Map.add name (public' (withoutDocumentation def)) types, values, doc)
```

**Usage**:
```fsharp
// Current style (unclear if this works in CE?)
let myModule = moduleDef {
    Type("Person", personTypeDef)
    Value("greet", greetValueDef)
    Doc("A module for greeting people")
}
```

**Options**:
1. **Keep builder pattern** - Fluent chaining without CE syntax
2. **Convert to CE pattern** - Add Yield/Zero/Delay/Run like Types DSL
3. **Hybrid** - Support both builder chaining AND CE syntax

**Discussion Points**:
- Is the builder pattern appropriate for incremental construction?
- Do we want consistency with Types/Values/Patterns DSLs?
- What's the typical use case - programmatic or declarative?

### Q2: How Should Access Control Work?

**Current**: Separate methods for public/private
```fsharp
member this.Type(name, def)         // Public
member this.PrivateType(name, def)  // Private
```

**Alternative Options**:
1. **Access parameter**: `member this.Type(name, def, access: Access)`
2. **CustomOperations**: `publicType`, `privateType`, `publicValue`, `privateValue`
3. **Separate builders**: `public' { }` and `private' { }` sections

**Example with CustomOperations**:
```fsharp
let myModule = moduleDef {
    publicType "Person" personDef
    privateType "Internal" internalDef
    publicValue "greet" greetDef
    doc "My module"
}
```

### Q3: How Should Documentation Work?

**Current**: Optional parameter or separate `.Doc()` method
```fsharp
member this.Type(name, def)                    // No doc
member this.Type(name, def, documentation)     // With doc
member this.Doc(documentation)                 // Module-level doc
```

**Alternative Options**:
1. **Inline with item**: Documentation always part of item definition
2. **Separate section**: All docs in one place
3. **Attribute-style**: `[<Doc("...")>]` metadata

**Example with Inline Docs**:
```fsharp
let myModule = moduleDef {
    Type("Person", personDef, doc = "Represents a person")
    Value("greet", greetDef, doc = "Greets a person")
    Doc("A module for greeting people")  // Module doc
}
```

### Q4: Should We Support CustomOperations?

**Current**: No CustomOperations, only method calls

**Potential CustomOperations**:
- `type'` / `type_` (avoid keyword)
- `value'` / `value_` (avoid keyword)
- `publicType` / `privateType`
- `publicValue` / `privateValue`
- `doc`

**Pros**:
- Consistency with Types/Values/Patterns DSLs
- Query-style syntax feels declarative
- Lowercase naming follows Fun.Blazor convention

**Cons**:
- Builder pattern might not need CustomOperations
- Method calls are already clean and discoverable
- Keywords "type" and "value" require workarounds

### Q5: Combine/Merge Semantics?

**Current**: `Combine` merges two builders, later entries override earlier ones
```fsharp
member _.Combine(builder1, builder2) =
    ModuleDefinitionBuilder(
        Map.fold (fun acc k v -> Map.add k v acc) builder1.Types builder2.Types,
        // ...
    )
```

**Question**: What should happen on name collision?
1. **Last wins** (current)
2. **Error on collision**
3. **Merge with warning**

## Use Cases to Explore

### Use Case 1: Simple Module with Few Definitions
```fsharp
// Define a small utility module
let stringUtils = moduleDef {
    Type("NonEmptyString", nonEmptyStringDef)
    Value("isEmpty", isEmptyDef)
    Value("length", lengthDef)
    Doc("String utility functions")
}
```

**Expectations**:
- Concise syntax for small modules
- Clear which items are public
- Easy to add documentation

### Use Case 2: Large Module with Many Definitions
```fsharp
// Define a domain module with many types and values
let customerModule = moduleDef {
    // Types
    Type("Customer", customerDef, "A customer record")
    Type("Address", addressDef, "Mailing address")
    Type("Order", orderDef, "Customer order")
    PrivateType("InternalState", stateDef)

    // Values
    Value("createCustomer", createCustomerDef)
    Value("updateAddress", updateAddressDef)
    Value("placeOrder", placeOrderDef)
    PrivateValue("validateOrder", validateOrderDef)

    Doc("Customer domain module")
}
```

**Expectations**:
- Scannable structure (types, then values)
- Clear public/private distinction
- Inline documentation

### Use Case 3: Programmatic Module Construction
```fsharp
// Build module from lists of definitions
let typeDefs = [("Person", personDef); ("Address", addressDef)]
let valueDefs = [("greet", greetDef); ("format", formatDef)]

let myModule =
    let builder = moduleDef.new()
    let withTypes = typeDefs |> List.fold (fun b (n, d) -> b.Type(n, d)) builder
    let withValues = valueDefs |> List.fold (fun b (n, d) -> b.Value(n, d)) withTypes
    withValues.Doc("Generated module")
```

**Expectations**:
- Programmatic construction from data
- Builder chaining works smoothly
- Can mix programmatic and declarative styles

### Use Case 4: Module Composition/Extension
```fsharp
// Extend an existing module with new definitions
let extendedModule = moduleDef {
    // Include base module contents?
    // Add new definitions
    Type("NewType", newTypeDef)
    Value("newFunc", newFuncDef)
}
```

**Question**: Do we support module extension/composition?

## Comparison with Elm

### Elm Module Syntax
```elm
module Customer exposing (Customer, Address, createCustomer)

-- Types
type alias Customer = { ... }
type alias Address = { ... }
type InternalState = ... -- private

-- Values
createCustomer : ... -> Customer
createCustomer = ...

updateAddress : ... -> Customer -> Customer
updateAddress = ...
```

### Morphir-Dotnet Modules DSL
```fsharp
let customerModule = moduleDef {
    Type("Customer", customerAliasDef)
    Type("Address", addressAliasDef)
    PrivateType("InternalState", internalStateDef)

    Value("createCustomer", createCustomerDef)
    Value("updateAddress", updateAddressDef)
}
```

**Observation**:
- Elm has explicit `exposing` clause (public interface)
- Morphir separates Specification (public) from Definition (full)
- DSL uses methods to distinguish public/private

## BDD Scenarios

### Scenario 1: Create Simple Module Specification
```gherkin
Feature: Module Specification Creation

Scenario: Create a module specification with public types and values
  Given I have a type specification for "Person"
  And I have a value specification for "greet"
  When I create a module specification with:
    | Item Type | Name     | Specification      | Documentation        |
    | Type      | Person   | personTypeSpec     | Represents a person  |
    | Value     | greet    | greetValueSpec     | Greets a person      |
  And I set the module documentation to "People module"
  Then the module specification should contain 1 type
  And the module specification should contain 1 value
  And the module specification documentation should be "People module"
  And the type "Person" should have documentation "Represents a person"
```

### Scenario 2: Create Module Definition with Access Control
```gherkin
Feature: Module Definition with Access Control

Scenario: Create a module with public and private definitions
  Given I have type definitions for "Customer" and "InternalState"
  And I have value definitions for "create" and "validate"
  When I create a module definition with:
    | Access  | Type  | Name          | Definition        |
    | Public  | Type  | Customer      | customerDef       |
    | Private | Type  | InternalState | internalStateDef  |
    | Public  | Value | create        | createDef         |
    | Private | Value | validate      | validateDef       |
  Then the module should have 2 types (1 public, 1 private)
  And the module should have 2 values (1 public, 1 private)
  And "Customer" should be public
  And "InternalState" should be private
```

### Scenario 3: Programmatic Module Construction
```gherkin
Feature: Programmatic Module Construction

Scenario: Build a module from a list of definitions
  Given I have a list of type definitions:
    | Name    | Definition  |
    | Person  | personDef   |
    | Address | addressDef  |
  When I programmatically add each type to the module builder
  And I run the builder
  Then the resulting module should contain 2 types
  And all types should be public by default
```

### Scenario 4: Module Documentation
```gherkin
Feature: Module Documentation

Scenario: Add documentation to module and its members
  Given I create a module definition
  When I add a type "Result" with documentation "Result of an operation"
  And I add a value "success" with documentation "Creates a success result"
  And I set module documentation to "Result module for error handling"
  Then the type "Result" documentation should be "Result of an operation"
  And the value "success" documentation should be "Creates a success result"
  And the module documentation should be "Result module for error handling"
```

## Design Decisions (Finalized)

### ✅ Decision 1: Use CE Pattern with CustomOperations
**Chosen**: Convert to Computation Expression pattern like Types/Values DSLs
**Rationale**: Consistency across all DSLs, declarative syntax, Fun.Blazor style

### ✅ Decision 2: Hybrid Access Control Syntax
**Chosen**: Combination of nested sections + CustomOperations
```fsharp
mod {
    // Option A: Nested sections for grouping
    pub {
        typedef "Customer" customerDef
        valuedef "create" createDef
    }

    // Option B: Explicit CustomOperations
    publicType "Order" orderDef
    privateType "Internal" internalDef

    doc "My module"
}
```

### ✅ Decision 3: Multiple Naming Options
**Chosen**: Support both `type'`/`value'` AND `typedef`/`valuedef`
- `type'`, `value'` - Minimal syntax (tick avoids keyword)
- `typedef`, `valuedef` - More explicit, self-documenting

### ✅ Decision 4: Rust-style `pub` Modifier
**Chosen**: Add `pub` as alias for `public'`
```fsharp
pub {  // Rust-style
    typedef "Person" personDef
}

public' {  // F#-style
    typedef "Person" personDef
}
```

### ✅ Decision 5: Module Composition Support
**Chosen**: Add combinator operations for merging modules
- `combine` - Merge two modules (error on collision)
- `merge` - Merge with override (last wins)
- `extend` - Add to existing module

## Implementation Considerations

### Current Pattern Analysis
```fsharp
// This is a BUILDER pattern, not a traditional CE
type ModuleDefinitionBuilder<'ta, 'va>(types, values, doc) =
    new() = ModuleDefinitionBuilder(Map.empty, Map.empty, None)

    // Builder methods return NEW builder instances (immutable)
    member this.Type(name, def) =
        ModuleDefinitionBuilder(Map.add name ... types, values, doc)
```

**Key Insight**: Current implementation is **immutable builder pattern**, each method returns a new builder. This is good but doesn't quite follow CE pattern.

**For CE Pattern** we'd need:
- `Yield` to create initial state
- `Delay` to defer evaluation
- `Run` to produce final result
- Method calls via `let!` or `do!` bindings OR CustomOperations

### Potential CE Conversion
```fsharp
type ModuleDefinitionBuilder() =
    member _.Yield(()) = { Types = Map.empty; Values = Map.empty; Doc = None }

    [<CustomOperation("publicType")>]
    member _.PublicType(state, name, def) =
        { state with Types = Map.add name (public' (withoutDoc def)) state.Types }

    [<CustomOperation("doc")>]
    member _.Doc(state, documentation) =
        { state with Doc = Some documentation }

    member _.Run(state) = moduleDefinition state.Types state.Values state.Doc
```

**Usage**:
```fsharp
let myModule = moduleDef {
    publicType "Person" personDef
    privateType "Internal" internalDef
    doc "My module"
}
```

## Proposed API Examples

### Example 1: Simple Module (Nested Sections)
```fsharp
let stringUtils = mod {
    pub {
        typedef "NonEmptyString" nonEmptyStringDef
        valuedef "isEmpty" isEmptyDef "Checks if string is empty"
        valuedef "length" lengthDef
    }
    doc "String utility functions"
}
```

### Example 2: Mixed Syntax
```fsharp
let customerModule = mod {
    // Use nested sections for grouping
    pub {
        typedef "Customer" customerDef "Customer record"
        typedef "Address" addressDef "Mailing address"
    }

    // Or use explicit operations
    publicValue "createCustomer" createCustomerDef
    privateType "InternalState" stateDef
    privateValue "validateOrder" validateOrderDef

    doc "Customer domain module"
}
```

### Example 3: Module Composition
```fsharp
let baseModule = mod {
    pub {
        typedef "Person" personDef
        valuedef "greet" greetDef
    }
}

let extendedModule = mod {
    extend baseModule  // Include base module
    pub {
        typedef "Employee" employeeDef
        valuedef "hire" hireDef
    }
}

// Or merge modules
let merged = mod {
    merge baseModule
    merge additionalModule
    doc "Combined module"
}
```

### Example 4: Programmatic Construction
```fsharp
let typeDefs = [("Person", personDef); ("Address", addressDef)]

let myModule = mod {
    pub {
        for (name, def) in typeDefs do
            typedef name def
    }
    doc "Generated module"
}
```

## Implementation Plan

### Phase 1: Core CE Pattern ✅ Design Complete
1. ✅ Convert to CE pattern with Yield/Zero/Delay/Run
2. ✅ Add lowercase CustomOperations
3. ✅ Support both `typedef`/`valuedef` and `type'`/`value'`
4. ✅ Add `publicType`, `privateType`, `publicValue`, `privateValue`
5. ✅ Add `doc` for module documentation

### Phase 2: Nested Sections
6. ⏳ Implement `pub { }` and `public' { }` sections
7. ⏳ Support nested type/value definitions within sections

### Phase 3: Module Composition
8. ⏳ Add `extend` operation
9. ⏳ Add `merge` operation
10. ⏳ Add `combine` operation with collision detection

### Phase 4: Testing & Documentation
11. ⏳ Implement BDD scenarios
12. ⏳ Add comprehensive test coverage
13. ⏳ Document patterns and examples

## Technical Implementation Notes

### State Structure
```fsharp
type ModuleState = {
    Types: Map<Name, AccessControlled<Documented<TypeDefinition<unit>>>>
    Values: Map<Name, AccessControlled<Documented<ValueDefinition<unit, unit>>>>
    Doc: string option
}
```

### CustomOperations
```fsharp
[<CustomOperation("typedef")>]
member _.typedef(state, name: string, def, ?doc) = ...

[<CustomOperation("valuedef")>]
member _.valuedef(state, name: string, def, ?doc) = ...

[<CustomOperation("publicType")>]
member _.publicType(state, name: string, def, ?doc) = ...

[<CustomOperation("privateType")>]
member _.privateType(state, name: string, def, ?doc) = ...

[<CustomOperation("doc")>]
member _.doc(state, documentation: string) = ...

[<CustomOperation("extend")>]
member _.extend(state, otherModule: ModuleDefinition) = ...
```

### Nested Sections (pub/public')
```fsharp
// pub and public' are themselves computation expressions
type PublicSectionBuilder() =
    member _.Yield(()) = []

    [<CustomOperation("typedef")>]
    member _.typedef(items, name, def, ?doc) = ...

    member _.Run(items) = items  // Returns list of items

// Main mod builder consumes the list
[<CustomOperation("pub")>]
member _.pub(state, items: list<...>) =
    // Add all items as public
```

## Success Criteria

- [ ] CE pattern with CustomOperations works
- [ ] Both `typedef`/`valuedef` and `type'`/`value'` supported
- [ ] `pub { }` nested sections work
- [ ] `publicType`/`privateType` explicit operations work
- [ ] Module composition (`extend`, `merge`, `combine`) works
- [ ] All BDD scenarios pass
- [ ] Documentation examples work
- [ ] Consistent with Types/Values/Patterns DSLs

## Next Steps

1. ✅ **Design Complete**: All decisions made
2. ⏳ **Phase 1 Implementation**: Convert to CE pattern
3. ⏳ **Phase 2 Implementation**: Add nested sections
4. ⏳ **Phase 3 Implementation**: Add module composition
5. ⏳ **Testing**: BDD scenarios and unit tests
6. ⏳ **Documentation**: Examples and patterns

## Related Documents

- [CE DSL Types](./ce-dsl-types.md)
- [CE DSL Values](./ce-dsl-values.md)
- [CE DSL Patterns](./ce-dsl-patterns.md)
- [CE DSL Literals](./ce-dsl-literals.md)
