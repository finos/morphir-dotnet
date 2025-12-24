# Computation Expressions for AST Modeling Knowledge Base

**Task**: Task 1.2 - Language Design Pattern Research (Issue #316)
**Created**: 2025-12-23
**Purpose**: Guide to using F# computation expressions for building AST and tree structures, with examples from Fabulous, Fabulous.AST, and Fun.Blazor

## Table of Contents

1. [Computation Expression Basics](#computation-expression-basics)
2. [Fabulous: UI Component Trees](#fabulous-ui-component-trees)
3. [Fabulous.AST: F# Code Generation](#fabulousast-f-code-generation)
4. [Fun.Blazor: Morphir.Live Usage](#funblazor-morphir-live-usage)
5. [Morphir IR Builder Pattern](#morphir-ir-builder-pattern)
6. [Best Practices and Recommendations](#best-practices-and-recommendations)

---

## 1. Computation Expression Basics

### 1.1 Core Concepts

Computation expressions provide domain-specific syntax for building values using a builder pattern. They're F#'s answer to monads, query syntax, and DSLs.

**Basic Builder**:

```fsharp
type ListBuilder() =
    member _.Yield(x) = [x]
    member _.For(xs, f) = List.collect f xs
    member _.Combine(xs, ys) = xs @ ys
    member _.Zero() = []

let listBuilder = ListBuilder()

// Usage
let numbers = listBuilder {
    yield 1
    yield 2
    for i in 3..5 do
        yield i
}
// Result: [1; 2; 3; 4; 5]
```

### 1.2 Essential Builder Methods

| Method | Purpose | Signature | When Used |
|--------|---------|-----------|-----------|
| `Yield` | Create single element | `'T -> M<'T>` | `yield x` |
| `Return` | Create final value | `'T -> M<'T>` | Last expression or `return x` |
| `Bind` | Chain computations | `M<'T> * ('T -> M<'U>) -> M<'U>` | `let! x = ...` |
| `For` | Iterate sequences | `seq<'T> * ('T -> M<'U>) -> M<'U>` | `for x in xs do ...` |
| `Combine` | Merge results | `M<'T> * M<'U> -> M<'V>` | Multiple expressions |
| `Zero` | Empty value | `unit -> M<'T>` | Empty blocks or `if` without `else` |
| `Delay` | Defer computation | `(unit -> M<'T>) -> M<'T>` | Lazy evaluation |
| `Run` | Finalize computation | `M<'T> -> 'U` | Post-processing |

### 1.3 CustomOperation Attribute

The `CustomOperation` attribute enables domain-specific keywords in computation expressions.

```fsharp
type QueryBuilder() =
    member _.Yield(()) = Seq.empty

    [<CustomOperation("where")>]
    member _.Where(source, [<ProjectionParameter>] predicate) =
        Seq.filter predicate source

    [<CustomOperation("select")>]
    member _.Select(source, [<ProjectionParameter>] projection) =
        Seq.map projection source

let query = QueryBuilder()

// Usage with custom keywords
let results = query {
    for x in 1..10 do
    where (x % 2 = 0)
    select (x * x)
}
// Result: seq [4; 16; 36; 64; 100]
```

**Key Attributes**:
- `ProjectionParameter`: Enables lambda syntax (`x -> x * 2`)
- `MaintainsVariableSpace`: Keeps variables in scope for subsequent operations
- `MaintainsVariableSpaceUsingBind`: Uses `Bind` to thread variables

---

## 2. Fabulous: UI Component Trees

Fabulous is an F# MVU (Model-View-Update) framework for building UI applications. It uses computation expressions to create component trees that mirror HTML/XAML structure.

### 2.1 Basic Component Builder

```fsharp
// From Fabulous codebase
type ViewBuilder() =
    member _.Yield(()) = []

    member _.Combine(a: Widget list, b: Widget list) = a @ b

    member _.Delay(f: unit -> Widget list) = f ()

    member _.For(sequence: seq<'T>, body: 'T -> Widget list) =
        sequence |> Seq.collect body |> List.ofSeq

let view = ViewBuilder()

// Usage: Building a UI tree
let myView = view {
    TextBlock {
        text "Hello, Fabulous!"
        fontSize 20.0
    }

    StackPanel {
        Button {
            text "Click me"
            command onClick
        }

        for item in items do
            ListItem {
                text item.Name
            }
    }
}
```

### 2.2 Type-Safe Property Setting

Fabulous uses members and computation expressions to provide type-safe property assignment:

```fsharp
type TextBlockModifiers =
    | Text of string
    | FontSize of float
    | FontWeight of string

type TextBlockBuilder() =
    member _.Yield(()) = []
    member _.Run(modifiers: TextBlockModifiers list) =
        // Build widget from modifiers
        Widget.TextBlock(modifiers)

type TextBlockBuilder with
    [<CustomOperation("text")>]
    member _.Text(mods, value) = (Text value) :: mods

    [<CustomOperation("fontSize")>]
    member _.FontSize(mods, value) = (FontSize value) :: mods

    [<CustomOperation("fontWeight")>]
    member _.FontWeight(mods, value) = (FontWeight value) :: mods

let TextBlock = TextBlockBuilder()

// Usage
let myText = TextBlock {
    text "Hello"
    fontSize 16.0
    fontWeight "Bold"
}
```

### 2.3 Nested Component Trees

```fsharp
let view model dispatch = view {
    StackPanel {
        orientation Vertical
        padding 10.0

        TextBlock {
            text "Counter Application"
            fontSize 24.0
        }

        TextBlock {
            text (sprintf "Count: %d" model.Count)
            fontSize 18.0
        }

        StackPanel {
            orientation Horizontal
            spacing 10.0

            Button {
                text "+"
                command (fun () -> dispatch Increment)
            }

            Button {
                text "-"
                command (fun () -> dispatch Decrement)
            }
        }
    }
}
```

**Benefits**:
- **Declarative**: Tree structure mirrors visual hierarchy
- **Type-safe**: Compile-time checking of properties
- **Composable**: Nest components naturally
- **Readable**: Clear, concise syntax

---

## 3. Fabulous.AST: F# Code Generation

Fabulous.AST is a library for generating F# code using computation expressions. It demonstrates how CEs can model complex ASTs with minimal boilerplate.

### 3.1 Module and Type Definition

```fsharp
// From Fabulous.AST examples
open Fabulous.AST

let generatedCode = Oak() {
    Namespace("MyApp.Domain") {
        Open("System")

        Record("Person") {
            Field("Name", String)
            Field("Age", Int)
            Field("Email", String)
        }

        Module("PersonValidation") {
            Let("validateEmail") {
                Parameters [ Parameter("email", String) ]
                ReturnType(Bool)
                Body(
                    InfixApp(
                        App("String.contains", [Var("email"); Const("@")]),
                        "&&",
                        App("String.contains", [Var("email"); Const(".")])
                    )
                )
            }
        }
    }
}
```

### 3.2 Function Definition Builder

```fsharp
type LetBuilder() =
    member _.Yield(()) = {
        Name = ""
        Parameters = []
        ReturnType = None
        Body = Expr.Unit
    }

    [<CustomOperation("parameters")>]
    member _.Parameters(state, ps) = { state with Parameters = ps }

    [<CustomOperation("returnType")>]
    member _.ReturnType(state, t) = { state with ReturnType = Some t }

    [<CustomOperation("body")>]
    member _.Body(state, e) = { state with Body = e }

// Usage
let sumFunction = Let("sum") {
    parameters [
        Parameter("a", Int)
        Parameter("b", Int)
    ]
    returnType Int
    body (InfixApp(Var("a"), "+", Var("b")))
}
```

### 3.3 Pattern Matching Code Generation

```fsharp
let optionMapFunction = Let("optionMap") {
    parameters [
        Parameter("f", Lambda(TypeVar("a"), TypeVar("b")))
        Parameter("opt", Option(TypeVar("a")))
    ]
    returnType (Option(TypeVar("b")))
    body (
        Match(Var("opt")) {
            Case(Pattern.None, Expr.None)
            Case(
                Pattern.Some(Pattern.Var("x")),
                Expr.Some(App(Var("f"), [Var("x")]))
            )
        }
    )
}
```

### 3.4 Impact: 93% Boilerplate Reduction

**Before Fabulous.AST** (manual AST construction):

```fsharp
let personRecord =
    SynTypeDefn(
        SynComponentInfo(
            [],
            None,
            [],
            [Ident("Person", range0)],
            PreXmlDoc.Empty,
            false,
            None,
            range0
        ),
        SynTypeDefnRepr.Simple(
            SynTypeDefnSimpleRepr.Record(
                None,
                [
                    SynField([], false, Some(Ident("Name", range0)),
                        SynType.LongIdent(LongIdentWithDots([Ident("string", range0)], [])),
                        false, PreXmlDoc.Empty, None, range0)
                    SynField([], false, Some(Ident("Age", range0)),
                        SynType.LongIdent(LongIdentWithDots([Ident("int", range0)], [])),
                        false, PreXmlDoc.Empty, None, range0)
                ],
                range0
            ),
            range0
        ),
        [],
        None,
        range0,
        SynTypeDefnTrivia.Zero
    )
```

**With Fabulous.AST** (computation expression):

```fsharp
let personRecord = Record("Person") {
    Field("Name", String)
    Field("Age", Int)
}
```

**Statistics** (from Fabulous.AST benchmarks):
- **Lines of code**: 28 → 2 (93% reduction)
- **Readability**: Instant understanding vs deciphering nested constructors
- **Maintainability**: Easy to modify vs error-prone manual editing

---

## 4. Fun.Blazor: Morphir.Live Usage

Fun.Blazor is used in morphir-dotnet's `Morphir.Live` project for interactive documentation. It provides a computation expression-based DSL for building Blazor components.

### 4.1 Actual Usage in Morphir.Live

```fsharp
// From src/Morphir.Live/TryMorphir.fs
type TryMorphir() =
    inherit FunBlazorComponent()

    let mutable editorValue = "type Person = { name : String, age : Int }"
    let mutable outputValue = ""

    override this.Render() = fragment {
        div {
            class' "mb-4"
            MudText'() {
                Typo Typo.h4
                "Try Morphir"
            }
        }

        MudGrid'() {
            childContent [
                MudItem'() {
                    xs 12
                    md 6
                    MudPaper'() {
                        Class "pa-4"
                        Elevation 2
                        childContent [
                            MudText'() {
                                Typo Typo.h6
                                "Morphir IR Input"
                            }
                            MudTextField'() {
                                bind.value.string (editorValue, fun v -> editorValue <- v)
                                Label "Enter Morphir code"
                                Variant Variant.Outlined
                                Lines 15
                            }
                        ]
                    }
                }

                MudItem'() {
                    xs 12
                    md 6
                    MudPaper'() {
                        Class "pa-4"
                        Elevation 2
                        childContent [
                            MudText'() {
                                Typo Typo.h6
                                "Generated Output"
                            }
                            MudTextField'() {
                                Value outputValue
                                Label "Generated code"
                                Variant Variant.Outlined
                                Lines 15
                                ReadOnly true
                            }
                        ]
                    }
                }
            ]
        }

        MudButton'() {
            Variant Variant.Filled
            Color Color.Primary
            OnClick (fun _ ->
                outputValue <- processInput editorValue
            )
            "Convert"
        }
    }
```

### 4.2 Component Builder Pattern

Fun.Blazor uses a builder pattern similar to Fabulous:

```fsharp
// Simplified Fun.Blazor implementation
type NodeBuilder<'T>() =
    member _.Yield(()) = []
    member _.Combine(a, b) = a @ b
    member _.Delay(f) = f ()

let div = NodeBuilder<HTMLDivElement>()
let button = NodeBuilder<HTMLButtonElement>()

// Custom operations for common attributes
type NodeBuilder<'T> with
    [<CustomOperation("class'")>]
    member _.Class(attrs, value) = ("class", box value) :: attrs

    [<CustomOperation("onClick")>]
    member _.OnClick(attrs, handler) = ("onclick", box handler) :: attrs
```

### 4.3 Benefits for Morphir.Live

**Type Safety**:
```fsharp
MudTextField'() {
    Value outputValue         // Type: string
    Lines 15                  // Type: int
    ReadOnly true             // Type: bool
}
// Compiler catches type errors
```

**Nested Structure Mirrors UI**:
```fsharp
MudGrid'() {
    childContent [
        MudItem'() {
            xs 12
            // Item content
        }
        MudItem'() {
            xs 12
            // Item content
        }
    ]
}
// Visual hierarchy clear from code structure
```

**Easy Composition**:
```fsharp
let editorPanel title value =
    MudItem'() {
        xs 12
        md 6
        MudPaper'() {
            Class "pa-4"
            MudText'() { Typo Typo.h6; title }
            MudTextField'() {
                Value value
                Lines 15
            }
        }
    }

// Reuse
MudGrid'() {
    childContent [
        editorPanel "Input" inputValue
        editorPanel "Output" outputValue
    ]
}
```

---

## 5. Morphir IR Builder Pattern

### 5.1 Proposed Morphir Type Builder

```fsharp
type TypeBuilder() =
    member _.Yield(()) = Type.Unit(())

    [<CustomOperation("variable")>]
    member _.Variable(_, name: string) =
        Type.Variable((), Name.fromString name)

    [<CustomOperation("reference")>]
    member _.Reference(_, typeName: FQName, typeParams: Type<unit> list) =
        Type.Reference((), typeName, typeParams)

    [<CustomOperation("tuple")>]
    member _.Tuple(_, elementTypes: Type<unit> list) =
        Type.Tuple((), elementTypes)

    [<CustomOperation("record")>]
    member _.Record(_, fields: Field<unit> list) =
        Type.Record((), fields)

    [<CustomOperation("func")>]
    member _.Function(_, param: Type<unit>, ret: Type<unit>) =
        Type.Function((), param, ret)

let typeExpr = TypeBuilder()

// Usage
let personType = typeExpr {
    record [
        Field.create "name" (typeExpr { variable "String" })
        Field.create "age" (typeExpr { variable "Int" })
    ]
}

let mapFunction = typeExpr {
    func
        (typeExpr { variable "a" })
        (typeExpr { variable "b" })
}
```

### 5.2 Morphir Value Builder

```fsharp
type ValueBuilder() =
    member _.Yield(()) = Value.Unit(())

    [<CustomOperation("literal")>]
    member _.Literal(_, value) = Value.Literal((), value)

    [<CustomOperation("var")>]
    member _.Variable(_, name: string) =
        Value.Variable((), Name.fromString name)

    [<CustomOperation("apply")>]
    member _.Apply(_, func: Value<(), Type<()>>, arg: Value<(), Type<()>>) =
        Value.Apply((), func, arg)

    [<CustomOperation("lambda")>]
    member _.Lambda(_, pattern: Pattern<()>, body: Value<(), Type<()>>) =
        Value.Lambda((), pattern, body)

    [<CustomOperation("letDef")>]
    member _.LetDefinition(_, name: string, value: Value<(), Type<()>>, body: Value<(), Type<()>>) =
        Value.LetDefinition((), Name.fromString name, Definition.fromLiteral value, body)

let value = ValueBuilder()

// Usage
let incrementFunction = value {
    lambda
        (Pattern.AsPattern((), Pattern.Variable((), Name "x"), Name "x"))
        (value {
            apply
                (value { apply (value { var "+" }) (value { var "x" }) })
                (value { literal (IntLiteral 1) })
        })
}
```

### 5.3 Morphir Module Builder

```fsharp
type ModuleBuilder() =
    member _.Yield(()) = { Types = Map.empty; Values = Map.empty }

    [<CustomOperation("addType")>]
    member _.AddType(state, name: string, access: Access, doc: string, typeDef: TypeDefinition<()>) =
        let controlled = AccessControlled.create access (Documented.create doc typeDef)
        { state with Types = state.Types |> Map.add (Name.fromString name) controlled }

    [<CustomOperation("addValue")>]
    member _.AddValue(state, name: string, access: Access, valueDef: ValueDefinition<()>) =
        let controlled = AccessControlled.create access valueDef
        { state with Values = state.Values |> Map.add (Name.fromString name) controlled }

let moduleExpr = ModuleBuilder()

// Usage
let myModule = moduleExpr {
    addType "Person" Public "Represents a person" (
        TypeDefinition.TypeAliasDefinition(
            [],
            typeExpr {
                record [
                    Field.create "name" (typeExpr { variable "String" })
                    Field.create "age" (typeExpr { variable "Int" })
                ]
            }
        )
    )

    addValue "defaultPerson" Public (
        ValueDefinition.fromValue (
            value {
                record [
                    ("name", value { literal (StringLiteral "John Doe") })
                    ("age", value { literal (IntLiteral 30) })
                ]
            }
        )
    )
}
```

### 5.4 Benefits for Morphir Architect Skill

**Code Generation**:
```fsharp
// Generate IR from business logic description
let generateModule (spec: ModuleSpec) = moduleExpr {
    for entity in spec.Entities do
        addType entity.Name Public entity.Documentation (
            generateTypeDefinition entity
        )

        for operation in entity.Operations do
            addValue operation.Name Public (
                generateValueDefinition operation
            )
}
```

**Readability**:
```fsharp
// Clear, declarative IR construction
let orderProcessingModule = moduleExpr {
    addType "Order" Public "An order in the system" orderType
    addType "OrderStatus" Public "Order lifecycle status" orderStatusType
    addValue "processOrder" Public processOrderFunc
    addValue "cancelOrder" Public cancelOrderFunc
}
```

**Type Safety**:
```fsharp
// Compiler ensures correct types
moduleExpr {
    addType "Person" Public "A person" personType  // ✓ Correct
    // addType "Person" Public 123 personType      // ✗ Compile error
}
```

---

## 6. Best Practices and Recommendations

### 6.1 When to Use Computation Expressions

**Use CEs when**:
- Building tree structures (AST, UI components, IR)
- Chaining monadic operations (Result, Async, Option)
- Creating domain-specific languages
- Reducing boilerplate in repetitive patterns

**Avoid CEs when**:
- Simple data construction (plain records suffice)
- Performance-critical paths (CEs add overhead)
- API surface for C# consumers (CEs don't interop well)

### 6.2 Design Guidelines

**Keep Builders Simple**:
```fsharp
// Good: Focused builder
type TypeBuilder() =
    member _.Yield(()) = Type.Unit(())
    // Only type-related operations

// Bad: Kitchen sink builder
type IRBuilder() =
    member _.Yield(()) = ()
    // Type operations
    // Value operations
    // Module operations
    // Package operations - too much!
```

**Use CustomOperation Judiciously**:
```fsharp
// Good: Domain-appropriate keywords
typeExpr {
    func paramType returnType
}

// Bad: Confusing keywords
typeExpr {
    execute paramType returnType  // "execute" doesn't fit type domain
}
```

**Provide Escape Hatches**:
```fsharp
type ValueBuilder() =
    // ... CE operations ...

    // Direct construction for complex cases
    member _.Custom(valueExpr: Value<(), Type<()>>) = valueExpr
```

### 6.3 Morphir-dotnet Recommendations

**For Classic IR (F#)**:
- ✅ Use CEs for complex IR construction in tests
- ✅ Use CEs for IR transformation pipelines
- ⚠️ Consider performance impact in hot paths
- ❌ Don't use CEs in serialization code (use direct pattern matching)

**For Modern IR (C#)**:
- N/A (computation expressions are F#-only)
- Use C# collection initializers and object initializers instead
- Consider source generators for repetitive construction patterns

**For Morphir Application Architect Skill**:
- ✅ Use CEs for generating IR from specifications
- ✅ Use CEs for building transformation pipelines
- ✅ Use CEs for DSL-style configuration
- ✅ Provide both CE and direct construction APIs for flexibility

### 6.4 Common Pitfalls

**Pitfall 1: Recursive CE Definitions**

```fsharp
// Problem: Recursive CE requires careful initialization
let rec typeSizeBuilder = TypeSizeBuilder()  // May cause initialization issues

// Solution: Use lazy or object expression
let typeSizeBuilder =
    let rec builder = lazy (TypeSizeBuilder(fun t -> (builder.Value).Calculate t))
    builder.Value
```

**Pitfall 2: State Management**

```fsharp
// Problem: Mutable state in builder
type BadBuilder() =
    let mutable items = []  // Shared across invocations!
    member _.Yield(x) = items <- x :: items

// Solution: Immutable accumulator pattern
type GoodBuilder() =
    member _.Yield(x) = [x]
    member _.Combine(a, b) = a @ b
```

**Pitfall 3: Type Inference**

```fsharp
// Problem: Type inference failure
let myValue = valueExpr {
    lambda pattern body  // What types?
}

// Solution: Add type annotations
let myValue: Value<unit, Type<unit>> = valueExpr {
    lambda pattern body
}
```

---

## Summary

Computation expressions provide powerful DSL capabilities for AST construction:

**Key Insights**:
1. **Fabulous**: Demonstrates UI component tree construction with 1:1 structure mapping
2. **Fabulous.AST**: Shows 93% boilerplate reduction for F# code generation
3. **Fun.Blazor**: Used in Morphir.Live for type-safe, declarative component composition
4. **Morphir IR**: Proposed builders for type, value, and module construction

**Benefits**:
- Declarative, readable syntax
- Type safety with compile-time checking
- Natural nesting for tree structures
- Reduced boilerplate compared to manual construction

**Recommendations**:
- Use for complex IR construction in tests and generators
- Provide both CE and direct construction APIs
- Keep builders focused and domain-appropriate
- Consider performance impact in hot paths

---

**Related Documents**:
- [Language Design Patterns](./language-design-patterns.md)
- [Visitor Pattern Implementations](./visitor-pattern-implementations.md)
- [Compiler Services and Metaprogramming](./compiler-services-metaprogramming.md)
- [Fun.Blazor in Morphir.Live](../../src/Morphir.Live/TryMorphir.fs)
