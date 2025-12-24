# Language Design Patterns Knowledge Base

**Task**: Task 1.2 - Language Design Pattern Research (Issue #316)
**Created**: 2025-12-23
**Purpose**: Comprehensive guide to AST/CST design patterns, type system patterns, and tree structure modeling for the Morphir Application Architect skill

## Table of Contents

1. [AST/CST Design Patterns](#astcst-design-patterns)
2. [Type System Design Patterns](#type-system-design-patterns)
3. [Tree Structure Patterns](#tree-structure-patterns)
4. [Smart Constructor Patterns](#smart-constructor-patterns)
5. [Attribute and Metadata Patterns](#attribute-and-metadata-patterns)
6. [Immutability and Structural Sharing](#immutability-and-structural-sharing)
7. [Pattern Matching Strategies](#pattern-matching-strategies)
8. [Cross-Language Comparisons](#cross-language-comparisons)

---

## 1. AST/CST Design Patterns

### 1.1 Algebraic Data Types (ADTs) for ASTs

**Pattern**: Use discriminated unions (F#) or sealed record hierarchies (C#) to represent syntax trees with exhaustive pattern matching.

**Morphir-dotnet C# Implementation**:

```csharp
// Modern IR: src/Morphir.Core/IR/Type.cs
public abstract record Type
{
    public required Document Metadata { get; set; }

    public sealed record Variable(Name Name) : Type;
    public sealed record Reference(FqName TypeName, Seq<Type> TypeParameters) : Type;
    public sealed record Tuple(Seq<Type> ElementTypes) : Type;
    public sealed record Record(Seq<Field> FieldTypes) : Type;
    public sealed record ExtensibleRecord(Name VariableName, Seq<Field> FieldTypes) : Type;
    public sealed record Function(Type ParameterType, Type ReturnType) : Type;
    public sealed record Unit() : Type;
}
```

**Morphir-dotnet F# Implementation**:

```fsharp
// Classic IR: src/Morphir.Models/IR/Classic/Type.fs
type Type<'attributes> =
    | Variable of 'attributes * Name
    | Reference of 'attributes * FQName * Type<'attributes> list
    | Tuple of 'attributes * Type<'attributes> list
    | Record of 'attributes * Field<'attributes> list
    | ExtensibleRecord of 'attributes * Name * Field<'attributes> list
    | Function of 'attributes * Type<'attributes> * Type<'attributes>
    | Unit of 'attributes
```

**Key Characteristics**:
- **Closed hierarchy**: `sealed` records (C#) or discriminated union (F#) prevent extension
- **Value equality**: Records provide structural equality by default
- **Exhaustive matching**: Compiler enforces handling all cases
- **Immutability**: All fields are readonly (C#) or immutable (F#)

**Benefits**:
- Type safety: Illegal states are unrepresentable
- Pattern matching: Exhaustive case analysis guaranteed
- Value semantics: Structural equality for tree comparison
- AOT-friendly: No reflection required for pattern matching

### 1.2 Generic Attributes Pattern

**Pattern**: Parameterize AST nodes with a generic attribute type to enable extensibility without modifying core types.

**Morphir-elm Example**:

```elm
type Type attributes
    = Variable attributes Name
    | Reference attributes FQName (List (Type attributes))
    | Tuple attributes (List (Type attributes))
    | Record attributes (List (Field attributes))
    | ExtensibleRecord attributes Name (List (Field attributes))
    | Function attributes (Type attributes) (Type attributes)
    | Unit attributes
```

**Common Attribute Uses**:
- **Source locations**: For error reporting and code navigation
- **Type information**: For decorated ASTs after type checking
- **Documentation**: Inline comments and metadata
- **Optimization hints**: For backend code generation

**F# Classic IR Usage**:

```fsharp
// Untyped AST: attributes = ()
let untypedExpr: Value.Definition<(), Type<()>> = ...

// Typed AST: attributes = Type<()>
let typedExpr: Value.Definition<Type<()>, Type<()>> = ...
```

**Design Decision**: morphir-dotnet C# Modern IR uses `Document Metadata` property instead of generic parameters to simplify type signatures and improve IDE support.

### 1.3 Composite Pattern (Implicit)

**Pattern**: Tree structures naturally implement the Composite pattern through recursive type definitions.

**Example**: Type expressions contain nested type expressions

```csharp
public sealed record Function(Type ParameterType, Type ReturnType) : Type;

// Creates a composite structure:
var curriedFunc = new Type.Function(
    new Type.Variable(new Name("a")),
    new Type.Function(
        new Type.Variable(new Name("b")),
        new Type.Variable(new Name("c"))
    )
);
// Represents: a -> b -> c
```

**Morphir IR Hierarchy**:

```
Distribution
└── Package (Map<Path, PackageDefinition>)
    └── Module (Map<ModuleName, ModuleDefinition>)
        ├── Types (Map<Name, TypeDefinition>)
        └── Values (Map<Name, ValueDefinition>)
            └── Value.Body (recursive expressions)
```

**Key Operations**:
- **Traversal**: Recursive visitor functions
- **Transformation**: Map operations preserving structure
- **Queries**: Fold/reduce operations for aggregation

### 1.4 Wrapper Types for Contextual Information

**Pattern**: Wrap AST nodes with additional context (access control, documentation) without polluting core types.

**Morphir-elm AccessControlled**:

```elm
type AccessControlled a
    = Public a
    | Private a
```

**Morphir-dotnet C# Implementation**:

```csharp
public readonly record struct AccessControlled<T>(Access Access, T Value)
{
    public static implicit operator T(AccessControlled<T> controlled) => controlled.Value;
}

public enum Access { Public, Private }
```

**Morphir-elm Documented**:

```elm
type alias Documented a =
    { doc : String
    , value : a
    }
```

**Usage in Module Definition**:

```csharp
public sealed record ModuleDefinition(
    Map<Name, AccessControlled<Documented<TypeDefinition>>> Types,
    Map<Name, AccessControlled<ValueDefinition>> Values
);
```

**Benefits**:
- **Separation of concerns**: Core AST remains clean
- **Composability**: Wrappers can be nested
- **Type safety**: Wrapper type prevents accidental access bypassing

---

## 2. Type System Design Patterns

### 2.1 Explicit vs Erased Types

**Pattern**: Distinguish between types that appear in source code (explicit) and types inferred or erased during compilation.

**Morphir Type Categories**:

1. **Explicit Types**: User-written type annotations
   ```elm
   someValue : Int -> String
   someValue n = String.fromInt n
   ```

2. **Inferred Types**: Compiler-derived types for unannotated expressions
   ```elm
   -- Type inferred as List Int -> Int
   sum = List.foldl (+) 0
   ```

3. **Erased Types**: Type parameters eliminated during compilation (e.g., generics in JVM bytecode)

**F# Type Provider Erased Types**:

```fsharp
// Type provider generates types at compile time, erased at runtime
type JsonProvider = JsonProvider<"schema.json">
let data = JsonProvider.Load("data.json")
// At runtime, 'data' is just IJsonDocument - no generated types exist
```

**Design Tradeoff**:
- **Erased**: Faster compilation, smaller binaries, no runtime overhead
- **Generative**: Runtime reflection support, serialization, debugging

### 2.2 Phantom Types

**Pattern**: Use type parameters that don't appear in runtime representation to enforce compile-time constraints.

**Example**: Units of measure in F#

```fsharp
[<Measure>] type USD
[<Measure>] type EUR

let price = 100.0<USD>
let rate = 1.2<EUR/USD>
let converted = price * rate  // Type: float<EUR>

// Compile error: type mismatch
// let invalid = price + converted
```

**Morphir Application**: Could be used for tracking IR versions

```fsharp
type IR<'version> = { /* IR structure */ }

type V1
type V2
type V3

let migrateV1toV2 (ir: IR<V1>) : IR<V2> = ...
let migrateV2toV3 (ir: IR<V2>) : IR<V3> = ...

// Compile error if trying to skip a migration
// let invalid (ir: IR<V1>) : IR<V3> = migrateV2toV3 ir
```

### 2.3 Type-Level Computation

**Pattern**: Encode constraints and relationships at the type level to prevent invalid states.

**Example**: Non-empty lists

```fsharp
type NonEmptyList<'a> = {
    Head: 'a
    Tail: 'a list
}

// Cannot construct empty list
let singleton x = { Head = x; Tail = [] }
let cons x nel = { Head = x; Tail = nel.Head :: nel.Tail }

// Safe head operation (no Option needed)
let head nel = nel.Head
```

**Morphir Application**: FQName construction enforces non-empty paths

```csharp
public readonly record struct FqName(Path PackagePath, Path ModulePath, Name LocalName)
{
    // Cannot construct invalid FQName - all parts required
}

public readonly record struct Path(IReadOnlyList<Name> Segments)
{
    public Path(params Name[] segments) : this((IReadOnlyList<Name>)segments)
    {
        if (segments.Length == 0)
            throw new ArgumentException("Path cannot be empty", nameof(segments));
    }
}
```

### 2.4 Recursive Types with Fixed Points

**Pattern**: Model recursive structures using explicit fixed-point types for enhanced control over recursion.

**Basic Fixed Point Type**:

```fsharp
type Fix<'f> = Fix of 'f<Fix<'f>>

// Example: List without built-in recursion
type ListF<'a, 'r> =
    | Nil
    | Cons of 'a * 'r

type List<'a> = Fix<ListF<'a>>

// Construction
let emptyList<'a> : List<'a> = Fix Nil
let cons x xs = Fix (Cons (x, xs))
```

**Benefits**:
- **Explicit recursion control**: Can inject behavior at recursion points
- **Catamorphisms**: Fold operations naturally derived
- **Decorations**: Easier to add caching, memoization, or tracing

**Morphir Type System** uses direct recursion instead for simplicity, but backends could leverage fixed-point encoding for optimization.

---

## 3. Tree Structure Patterns

### 3.1 Immutable Trees with Structural Sharing

**Pattern**: Create modified trees by sharing unchanged subtrees, minimizing allocations.

**Morphir-dotnet Implementation**:

```csharp
// Records provide automatic structural equality
public abstract record Type { }

// Modifying a tree creates new nodes only on the path to the change
var originalType = new Type.Function(
    new Type.Variable(new Name("a")),
    new Type.Variable(new Name("b"))
);

var modifiedType = originalType with {
    ReturnType = new Type.Unit()
};
// ParameterType is shared between originalType and modifiedType
```

**F# Example with Map**:

```fsharp
// Map<K,V> in F# uses persistent tree structure
let module1 = Map.empty |> Map.add "foo" typeDef1 |> Map.add "bar" typeDef2
let module2 = module1 |> Map.add "baz" typeDef3
// module1 and module2 share the subtree containing "foo" and "bar"
```

**Benefits**:
- **Memory efficiency**: O(log n) space for modifications
- **Time efficiency**: O(log n) for operations instead of O(n) copying
- **Concurrency**: Safe sharing across threads (no mutations)

### 3.2 Zipper Pattern for Tree Navigation

**Pattern**: Represent a location in a tree with context, enabling efficient local updates.

**Conceptual Structure**:

```fsharp
type Zipper<'a> = {
    Focus: 'a
    Context: Context<'a> list
}

type Context<'a> =
    | Left of 'a  // Left sibling
    | Right of 'a // Right sibling
    | Parent of 'a * Context<'a> list
```

**Operations**:
- **moveUp**: Navigate to parent
- **moveDown**: Navigate to child
- **modify**: Update focused node
- **rebuild**: Reconstruct tree from zipper

**Morphir Application**: Could be used in editor for IR navigation and local transformations.

**Trade-offs**:
- Adds complexity to implementation
- Excellent for deep tree modifications
- morphir-dotnet currently uses simple recursive descent (sufficient for current needs)

### 3.3 Rose Tree (Multi-way Tree)

**Pattern**: Tree where each node can have arbitrary number of children.

**Generic Definition**:

```fsharp
type RoseTree<'a> = {
    Value: 'a
    Children: RoseTree<'a> list
}
```

**Morphir Module Hierarchy as Rose Tree**:

```
Distribution
├── Package "Morphir.SDK"
│   ├── Module "Basics"
│   │   ├── Type "Order"
│   │   └── Value "compare"
│   └── Module "List"
│       ├── Type "List"
│       └── Value "map"
└── Package "MyApp"
    └── Module "Domain"
        └── Type "Customer"
```

**Operations**:

```fsharp
// Map over all nodes
let rec mapTree f tree = {
    Value = f tree.Value
    Children = List.map (mapTree f) tree.Children
}

// Fold over tree (catamorphism)
let rec foldTree f acc tree =
    let acc' = f acc tree.Value
    List.fold (foldTree f) acc' tree.Children
```

---

## 4. Smart Constructor Patterns

### 4.1 Validated Construction

**Pattern**: Use smart constructors to enforce invariants at creation time.

**Example: Non-empty Path**:

```csharp
public readonly record struct Path
{
    private readonly IReadOnlyList<Name> _segments;

    public IReadOnlyList<Name> Segments => _segments;

    // Smart constructor enforces non-empty constraint
    public Path(params Name[] segments)
    {
        if (segments.Length == 0)
            throw new ArgumentException("Path must have at least one segment");
        _segments = segments;
    }

    // Alternative: return Result<Path, Error> for functional style
    public static Result<Path, string> Create(IReadOnlyList<Name> segments)
    {
        return segments.Count > 0
            ? Result.Ok(new Path(segments.ToArray()))
            : Result.Error("Path cannot be empty");
    }
}
```

**Benefits**:
- **Invariant enforcement**: Illegal states become impossible
- **Documentation**: Constructor signature documents constraints
- **Centralized validation**: Single point of control

### 4.2 Builder Pattern for Complex Trees

**Pattern**: Provide fluent API for constructing complex AST nodes.

**Example: Module Definition Builder**:

```csharp
public class ModuleBuilder
{
    private readonly List<(Name, AccessControlled<Documented<TypeDefinition>>)> _types = new();
    private readonly List<(Name, AccessControlled<ValueDefinition>)> _values = new();

    public ModuleBuilder AddPublicType(Name name, string doc, TypeDefinition def)
    {
        _types.Add((name, new AccessControlled<Documented<TypeDefinition>>(
            Access.Public,
            new Documented<TypeDefinition>(doc, def)
        )));
        return this;
    }

    public ModuleBuilder AddPrivateValue(Name name, ValueDefinition def)
    {
        _values.Add((name, new AccessControlled<ValueDefinition>(
            Access.Private,
            def
        )));
        return this;
    }

    public ModuleDefinition Build() => new ModuleDefinition(
        _types.ToMap(),
        _values.ToMap()
    );
}

// Usage
var module = new ModuleBuilder()
    .AddPublicType(new Name("Person"), "Represents a person", personTypeDef)
    .AddPrivateValue(new Name("defaultPerson"), defaultPersonDef)
    .Build();
```

### 4.3 Computation Expression Builders (F#)

**Pattern**: Use F# computation expressions to provide domain-specific syntax for tree construction.

**See**: [Computation Expressions for AST Modeling](#../computation-expressions-for-ast.md) for comprehensive coverage.

**Brief Example**:

```fsharp
type TypeBuilder() =
    member _.Yield(()) = Type.Unit(())

    [<CustomOperation("variable")>]
    member _.Variable(_, name) = Type.Variable((), name)

    [<CustomOperation("func")>]
    member _.Function(_, param, ret) = Type.Function((), param, ret)

let typeExpr = typeBuilder {
    func (variable "a") (variable "b")
}
// Produces: Type.Function((), Type.Variable((), "a"), Type.Variable((), "b"))
```

---

## 5. Attribute and Metadata Patterns

### 5.1 Annotation Layers

**Pattern**: Separate AST structure from annotations, allowing multiple annotation layers without changing core types.

**Layered Approach**:

```fsharp
// Core AST (structure only)
type Expr =
    | Literal of int
    | Variable of string
    | Add of Expr * Expr

// Annotation type
type Annotation<'a> = {
    Node: Expr
    Info: 'a
}

// Different annotation layers
type SourceInfo = { Line: int; Column: int }
type TypeInfo = { Type: Type }
type OptimizationHint = { Inline: bool }

// Annotated ASTs
type SourceAnnotatedExpr = Annotation<SourceInfo>
type TypedExpr = Annotation<TypeInfo>
type OptimizedExpr = Annotation<OptimizationHint>
```

**Morphir Approach**: Uses generic `'attributes` parameter instead

```fsharp
type Type<'attributes> =
    | Variable of 'attributes * Name
    | Reference of 'attributes * FQName * Type<'attributes> list
    // ...
```

**Trade-offs**:
- **Layered**: More complex, explicit separation
- **Generic**: Simpler, single annotation per node

### 5.2 Source Location Tracking

**Pattern**: Preserve source code positions for error reporting and IDE features.

**Span-based Tracking**:

```csharp
public readonly record struct SourceSpan(
    string FilePath,
    Position Start,
    Position End
);

public readonly record struct Position(int Line, int Column);

// Attach to AST nodes
public abstract record Type
{
    public SourceSpan? Span { get; init; }

    public sealed record Variable(Name Name) : Type;
    // ...
}
```

**Error Reporting Usage**:

```csharp
void ReportTypeError(Type type, string message)
{
    if (type.Span is { } span)
    {
        Console.Error.WriteLine($"{span.FilePath}:{span.Start.Line}:{span.Start.Column}: {message}");
    }
    else
    {
        Console.Error.WriteLine($"Type error: {message}");
    }
}
```

### 5.3 Documentation Attachment

**Pattern**: Attach structured documentation to AST nodes for tooling support.

**Morphir Documented Type**:

```elm
type alias Documented a =
    { doc : String
    , value : a
    }
```

**C# Implementation**:

```csharp
public readonly record struct Documented<T>(string Doc, T Value)
{
    public static implicit operator T(Documented<T> documented) => documented.Value;
}

// Usage in module definition
Map<Name, AccessControlled<Documented<TypeDefinition>>> Types
```

**Benefits**:
- **IDE tooltips**: Hover documentation from AST
- **Generated docs**: Export to HTML, Markdown
- **API contracts**: Structured documentation for validation

---

## 6. Immutability and Structural Sharing

### 6.1 Persistent Data Structures

**Pattern**: Use immutable collections that share structure across versions.

**F# Map (Red-Black Tree)**:

```fsharp
let empty = Map.empty<string, int>
let map1 = empty |> Map.add "a" 1 |> Map.add "b" 2
let map2 = map1 |> Map.add "c" 3

// map1 and map2 share the subtree containing "a" and "b"
// Only the path from root to "c" is new
```

**Time Complexity**:
- **Lookup**: O(log n)
- **Insert**: O(log n)
- **Remove**: O(log n)

**Space Complexity**:
- **Modification**: O(log n) new nodes
- **Original retained**: Full structure remains accessible

**Morphir-dotnet Usage**:

```csharp
// System.Collections.Immutable.ImmutableDictionary
Map<Name, TypeDefinition> types = Map.Empty<Name, TypeDefinition>()
    .Add(new Name("Int"), intTypeDef)
    .Add(new Name("String"), stringTypeDef);

var updatedTypes = types.Add(new Name("Bool"), boolTypeDef);
// 'types' unchanged, 'updatedTypes' shares structure
```

### 6.2 Copy-on-Write Semantics

**Pattern**: Defer copying until mutation is required.

**C# Records with `with` Expression**:

```csharp
var original = new Type.Function(
    new Type.Variable(new Name("a")),
    new Type.Variable(new Name("b"))
);

// Creates new record, sharing unchanged fields
var modified = original with {
    Metadata = new Document("Updated documentation")
};

// original.ParameterType and original.ReturnType are shared with modified
```

**F# Record Update**:

```fsharp
type Person = { Name: string; Age: int; Address: string }

let person1 = { Name = "Alice"; Age = 30; Address = "123 Main St" }
let person2 = { person1 with Age = 31 }
// person1 unchanged, person2 shares Name and Address strings
```

### 6.3 Interning for Memory Optimization

**Pattern**: Share identical subtrees by maintaining a global cache.

**Name Interning Example**:

```csharp
public class NameCache
{
    private readonly ConcurrentDictionary<string, Name> _cache = new();

    public Name Intern(string value)
    {
        return _cache.GetOrAdd(value, v => new Name(v));
    }
}

// Usage
var cache = new NameCache();
var name1 = cache.Intern("Map");
var name2 = cache.Intern("Map");
// ReferenceEquals(name1, name2) == true
```

**Benefits**:
- **Memory savings**: Common names stored once
- **Fast equality**: Reference equality for interned values
- **Trade-off**: Cache management overhead

**Morphir Application**: Could be used for frequently occurring names like "List", "Maybe", "String", "Int".

---

## 7. Pattern Matching Strategies

### 7.1 Exhaustive Pattern Matching

**Pattern**: Compiler-enforced coverage of all AST node types.

**C# Switch Expression**:

```csharp
public static string TypeToString(Type type) => type switch
{
    Type.Variable v => v.Name.ToString(),
    Type.Reference r => $"{r.TypeName}({string.Join(", ", r.TypeParameters.Select(TypeToString))})",
    Type.Tuple t => $"({string.Join(", ", t.ElementTypes.Select(TypeToString))})",
    Type.Record r => $"{{{string.Join(", ", r.FieldTypes.Select(f => $"{f.Name}: {TypeToString(f.Type)}"))}}}",
    Type.ExtensibleRecord er => $"{{ {er.VariableName} | {string.Join(", ", er.FieldTypes.Select(f => $"{f.Name}: {TypeToString(f.Type)}"))} }}",
    Type.Function f => $"{TypeToString(f.ParameterType)} -> {TypeToString(f.ReturnType)}",
    Type.Unit => "()",
    _ => throw new ArgumentException($"Unknown type: {type.GetType()}")
};
```

**F# Pattern Matching**:

```fsharp
let rec typeToString = function
    | Variable (_, name) -> name |> Name.toString
    | Reference (_, typeName, typeParams) ->
        sprintf "%s(%s)" (typeName |> FQName.toString) (typeParams |> List.map typeToString |> String.concat ", ")
    | Tuple (_, elementTypes) ->
        sprintf "(%s)" (elementTypes |> List.map typeToString |> String.concat ", ")
    | Record (_, fieldTypes) ->
        sprintf "{%s}" (fieldTypes |> List.map fieldToString |> String.concat ", ")
    | ExtensibleRecord (_, var, fieldTypes) ->
        sprintf "{ %s | %s }" (var |> Name.toString) (fieldTypes |> List.map fieldToString |> String.concat ", ")
    | Function (_, param, ret) ->
        sprintf "%s -> %s" (typeToString param) (typeToString ret)
    | Unit _ -> "()"
```

**Benefit**: Adding new AST node type causes compile errors at all match sites, forcing updates.

### 7.2 Active Patterns (F#)

**Pattern**: Define custom pattern matching logic for complex conditions.

**Example: Type Classification**:

```fsharp
// Active pattern for classifying types
let (|Primitive|Composite|Function|) (t: Type<'a>) =
    match t with
    | Variable _ | Unit _ -> Primitive
    | Tuple _ | Record _ | ExtensibleRecord _ | Reference _ -> Composite
    | Function _ -> Function

// Usage
let analyzeType = function
    | Primitive -> "Simple type"
    | Composite -> "Composite type"
    | Function -> "Function type"
```

**Parameterized Active Patterns**:

```fsharp
// Match functions with specific parameter type
let (|FunctionWith|_|) targetParam = function
    | Function (_, param, ret) when param = targetParam -> Some ret
    | _ -> None

// Usage
match someType with
| FunctionWith intType returnType ->
    printfn "Function taking Int, returning %A" returnType
| _ ->
    printfn "Not a function taking Int"
```

### 7.3 Visitor Pattern

**Pattern**: Separate algorithms from AST structure using visitor objects.

**See**: [Visitor Pattern Implementations](./visitor-pattern-implementations.md) for comprehensive coverage.

**Brief Example**:

```csharp
public interface ITypeVisitor<TResult>
{
    TResult VisitVariable(Type.Variable variable);
    TResult VisitReference(Type.Reference reference);
    TResult VisitTuple(Type.Tuple tuple);
    TResult VisitRecord(Type.Record record);
    TResult VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord);
    TResult VisitFunction(Type.Function function);
    TResult VisitUnit(Type.Unit unit);
}

// Visitor implementation
public class TypeSizeCalculator : ITypeVisitor<int>
{
    public int VisitVariable(Type.Variable variable) => 1;
    public int VisitReference(Type.Reference reference) =>
        1 + reference.TypeParameters.Sum(t => t.Accept(this));
    // ...
}
```

---

## 8. Cross-Language Comparisons

### 8.1 F# vs C# for AST Modeling

| Aspect | F# | C# |
|--------|----|----|
| **ADT Representation** | Discriminated unions (native) | Sealed record hierarchies (verbose) |
| **Pattern Matching** | Native, exhaustive by default | Switch expressions (C# 8+), requires `_` case |
| **Immutability** | Default for records and unions | Opt-in with `record` keyword |
| **Type Inference** | Excellent, minimal annotations | Limited, requires explicit types |
| **Generic Constraints** | Powerful (member constraints, SRTP) | Limited (interface/class constraints) |
| **Active Patterns** | Native feature | Not available |
| **Computation Expressions** | Native feature | Not available |
| **AOT Compatibility** | Excellent with careful design | Excellent with source generators |
| **IDE Support** | Good (Ionide, Rider) | Excellent (Visual Studio, Rider) |
| **Learning Curve** | Steeper for C# developers | Gentle for C# developers |

**Morphir-dotnet Strategy**:
- **F# for Classic IR**: Leverage native discriminated unions and functional patterns
- **C# for Modern IR**: Sealed records for better IDE support and C# interoperability
- **F# for transformations**: Computation expressions and active patterns
- **C# for CLI tools**: Better debugging and mainstream adoption

### 8.2 Elm vs F# vs C#

| Pattern | Elm | F# | C# |
|---------|-----|----|----|
| **ADTs** | Native (`type`) | Discriminated unions | Sealed record hierarchies |
| **Generics** | Type parameters | Generic types | Generic types |
| **Constraints** | None | Member constraints, SRTP | Interface/class constraints |
| **Null Safety** | No null | `option` type | Nullable reference types (opt-in) |
| **Module System** | File-based, explicit imports | File-based, auto-open | Namespace-based, using directives |
| **Interop** | JavaScript | .NET, C#, VB.NET | .NET, F#, VB.NET |
| **Tooling** | elm-format, elm-test | Fantomas, Expecto | dotnet format, xUnit |

**Key Insight**: Morphir IR design in Elm translates naturally to F#, but requires explicit encoding in C# (sealed records instead of discriminated unions).

### 8.3 Pattern Matching Across Languages

**F# (Exhaustive by Default)**:

```fsharp
let analyze = function
    | Variable (_, name) -> "Variable"
    | Reference _ -> "Reference"
    | Tuple _ -> "Tuple"
    | Record _ -> "Record"
    | ExtensibleRecord _ -> "Extensible record"
    | Function _ -> "Function"
    | Unit _ -> "Unit"
// Compiler error if any case is missing
```

**C# (Requires Default Case)**:

```csharp
string Analyze(Type type) => type switch
{
    Type.Variable => "Variable",
    Type.Reference => "Reference",
    Type.Tuple => "Tuple",
    Type.Record => "Record",
    Type.ExtensibleRecord => "Extensible record",
    Type.Function => "Function",
    Type.Unit => "Unit",
    _ => throw new ArgumentException() // Required
};
```

**Elm (Exhaustive by Default)**:

```elm
analyze : Type a -> String
analyze typeExpr =
    case typeExpr of
        Variable _ name ->
            "Variable"
        Reference _ _ _ ->
            "Reference"
        -- ... (compiler enforces exhaustiveness)
```

---

## Summary

This knowledge base documents 15+ core design patterns for AST/CST modeling:

**Structural Patterns**:
1. Algebraic Data Types (ADTs) for ASTs
2. Generic Attributes Pattern
3. Composite Pattern (implicit in recursive types)
4. Wrapper Types (AccessControlled, Documented)
5. Rose Tree (multi-way tree)

**Type System Patterns**:
6. Explicit vs Erased Types
7. Phantom Types
8. Type-Level Computation
9. Recursive Types with Fixed Points

**Construction Patterns**:
10. Smart Constructors
11. Builder Pattern
12. Computation Expression Builders (F#)

**Memory and Performance Patterns**:
13. Immutable Trees with Structural Sharing
14. Persistent Data Structures
15. Interning for Memory Optimization

**Traversal Patterns**:
16. Exhaustive Pattern Matching
17. Active Patterns (F#)
18. Visitor Pattern
19. Zipper Pattern for Tree Navigation

**Cross-Cutting Patterns**:
20. Annotation Layers
21. Source Location Tracking
22. Documentation Attachment

These patterns form the foundation for the Morphir Application Architect skill's understanding of language design and AST modeling in morphir-dotnet.

---

**Related Documents**:
- [Visitor Pattern Implementations](./visitor-pattern-implementations.md)
- [Computation Expressions for AST Modeling](./computation-expressions-for-ast.md)
- [Compiler Services and Metaprogramming](./compiler-services-metaprogramming.md)
- [Ecosystem Knowledge Base](./ecosystem-knowledge-base.md)
- [Architectural Decisions](../decisionlogs/architectural-decisions.md)
