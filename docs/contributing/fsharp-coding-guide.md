# F# Coding Guide for morphir-dotnet

This guide provides F#-specific coding standards and best practices for the morphir-dotnet project, including F# Interactive scripts in `.claude/skills/`.

## Table of Contents

1. [Pattern Matching and Value Extraction](#pattern-matching-and-value-extraction)
2. [Active Patterns](#active-patterns)
3. [Error Handling](#error-handling)
4. [Immutability and Data Structures](#immutability-and-data-structures)
5. [Async and Task Workflows](#async-and-task-workflows)
6. [Type Design](#type-design)
7. [Computation Expressions and DSLs](#computation-expressions-and-dsls)
8. [JSON Serialization with System.Text.Json](#json-serialization-with-systemtextjson)
9. [CLI Scripts (.fsx)](#cli-scripts-fsx)
10. [Testing](#testing)

---

## Pattern Matching and Value Extraction

### ✅ Prefer Active Patterns Over Complex If-Then Chains

Active patterns make value extraction more declarative and easier to understand.

**❌ Avoid: Complex if-then chains**
```fsharp
let processJson (element: JsonElement) =
    if element.ValueKind = JsonValueKind.Null then
        None
    elif element.ValueKind = JsonValueKind.String then
        Some (element.GetString())
    elif element.ValueKind = JsonValueKind.Number then
        Some (element.GetInt32().ToString())
    else
        None
```

**✅ Prefer: Active patterns**
```fsharp
let (|NullJson|StringJson|NumberJson|OtherJson|) (element: JsonElement) =
    match element.ValueKind with
    | JsonValueKind.Null -> NullJson
    | JsonValueKind.String -> StringJson (element.GetString())
    | JsonValueKind.Number -> NumberJson (element.GetInt32())
    | _ -> OtherJson

let processJson (element: JsonElement) =
    match element with
    | NullJson -> None
    | StringJson s -> Some s
    | NumberJson n -> Some (n.ToString())
    | OtherJson -> None
```

### Common Active Pattern Use Cases

#### 1. JSON Property Extraction

**✅ Good: Active pattern for optional properties**
```fsharp
let (|JsonProperty|_|) (propertyName: string) (element: JsonElement) =
    let mutable prop = Unchecked.defaultof<JsonElement>
    if element.TryGetProperty(propertyName, &prop) then
        Some prop
    else
        None

// Usage
match root with
| JsonProperty "version" version -> Some (version.GetString())
| _ -> None
```

#### 2. String Pattern Matching

**✅ Good: Active patterns for string parsing**
```fsharp
let (|StartsWith|_|) (prefix: string) (input: string) =
    if input.StartsWith(prefix) then
        Some (input.Substring(prefix.Length))
    else
        None

let (|Contains|_|) (substring: string) (input: string) =
    if input.Contains(substring) then Some ()
    else None

// Usage
match line with
| StartsWith "## " title -> processHeader title
| StartsWith "- " item -> processListItem item
| Contains "BREAKING" & Contains "CHANGE" -> markAsBreaking line
| _ -> processPlainText line
```

#### 3. Result/Option Chaining

**✅ Good: Active patterns for result unpacking**
```fsharp
let (|Success|Failure|) (result: Result<'a, 'b>) =
    match result with
    | Ok value -> Success value
    | Error err -> Failure err

// Usage
match checkRemoteCI() with
| Success ciState -> processCI ciState
| Failure error -> logError error
```

### Pattern Matching Best Practices

1. **Exhaustive Matching**: Always handle all cases
```fsharp
// ✅ Good: Exhaustive match
match optionalValue with
| Some value -> processValue value
| None -> useDefault()

// ❌ Bad: Incomplete match (compiler warning)
match optionalValue with
| Some value -> processValue value
```

2. **Guard Clauses**: Use `when` for additional conditions
```fsharp
match parseVersion changelog with
| Major, changes when changes > 0 -> "major"
| Minor, changes when changes > 0 -> "minor"
| Patch, _ -> "patch"
| _, _ -> "none"
```

3. **Pattern AND/OR Combinations**
```fsharp
// AND pattern (&)
match line with
| Contains "TODO" & StartsWith "- " -> extractTodo line
| _ -> None

// OR pattern (|)
match status with
| "success" | "completed" -> handleSuccess()
| _ -> handleOther()
```

---

## Active Patterns

### Partial Active Patterns

Use partial active patterns (`|Pattern|_|`) when the pattern might not match:

```fsharp
let (|Int|_|) (str: string) =
    match Int32.TryParse(str) with
    | true, value -> Some value
    | false, _ -> None

let (|ValidEmail|_|) (str: string) =
    if str.Contains("@") && str.Contains(".") then
        Some str
    else
        None

// Usage
match input with
| Int n -> printfn "Number: %d" n
| ValidEmail email -> printfn "Email: %s" email
| _ -> printfn "Unknown format"
```

### Multi-Case Active Patterns

Use for categorizing values into multiple cases:

```fsharp
let (|Even|Odd|) n =
    if n % 2 = 0 then Even else Odd

let (|Positive|Negative|Zero|) n =
    if n > 0 then Positive
    elif n < 0 then Negative
    else Zero

// Usage
match number with
| Even & Positive -> "even positive"
| Even & Negative -> "even negative"
| Odd & Positive -> "odd positive"
| Odd & Negative -> "odd negative"
| Zero -> "zero"
```

### Parameterized Active Patterns

```fsharp
let (|DivisibleBy|_|) divisor n =
    if n % divisor = 0 then Some() else None

// Usage
match number with
| DivisibleBy 15 -> "FizzBuzz"
| DivisibleBy 3 -> "Fizz"
| DivisibleBy 5 -> "Buzz"
| _ -> string number
```

---

## Error Handling

### Result Type

Prefer `Result<'T, 'Error>` for operations that can fail:

```fsharp
type ValidationError =
    | MissingField of string
    | InvalidFormat of string
    | OutOfRange of string * int * int

let validateVersion (version: string) : Result<string, ValidationError> =
    if String.IsNullOrWhiteSpace(version) then
        Error (MissingField "version")
    elif not (version.Contains(".")) then
        Error (InvalidFormat "Version must contain dots")
    else
        Ok version
```

### Result Active Patterns

```fsharp
let (|Ok|Error|) (result: Result<'a, 'b>) =
    match result with
    | Result.Ok value -> Ok value
    | Result.Error err -> Error err

// Usage
match validateVersion input with
| Ok version -> processVersion version
| Error (MissingField field) -> printfn "Missing: %s" field
| Error (InvalidFormat msg) -> printfn "Invalid: %s" msg
| Error (OutOfRange (field, min, max)) -> printfn "%s must be between %d and %d" field min max
```

### Railway-Oriented Programming

```fsharp
let (>>=) result func =
    match result with
    | Ok value -> func value
    | Error err -> Error err

let validateAndProcess input =
    validateVersion input
    >>= parseVersion
    >>= checkAvailability
    >>= createRelease
```

---

## Immutability and Data Structures

### Record Types

Always use immutable records:

```fsharp
// ✅ Good: Immutable record
type ReleaseInfo = {
    Version: string
    Date: DateTime
    Changes: string list
}

// ✅ Good: Record update expression
let updated = { original with Date = DateTime.Now }

// ❌ Avoid: Mutable fields
type ReleaseInfo = {
    mutable Version: string  // Don't do this
    mutable Date: DateTime
}
```

### Collections

Prefer immutable collections:

```fsharp
// ✅ Good: Immutable list
let changes = [ "Added feature X"; "Fixed bug Y" ]
let moreChanges = "Updated docs" :: changes

// ✅ Good: List comprehension
let numbers = [ for i in 1..10 -> i * 2 ]

// ❌ Avoid: ResizeArray (mutable)
let changes = ResizeArray<string>()
changes.Add("Added feature X")  // Mutable operation
```

### Options vs Nulls

Always use `Option<'T>` instead of null:

```fsharp
// ✅ Good: Option type
type Config = {
    Port: int option
    Host: string
}

let getPort config =
    config.Port |> Option.defaultValue 8080

// ❌ Avoid: Nullable
type Config = {
    Port: Nullable<int>  // Don't use in F#
}
```

### C# Interop: Nullable Reference Types (F# 9+)

When writing F# code that interoperates with C# (especially in mixed C#/F# projects), use F# 9's nullable reference types feature for better C# interop:

```fsharp
// Enable nullable reference types in .fsproj:
// <Nullable>enable</Nullable>

// ✅ Good: Explicit nullability for C# consumers
type IUserService =
    abstract member GetUserById: userId: string -> string | null
    abstract member GetUserName: userId: string -> string  // Non-nullable

// ✅ Good: Clear null handling in public API
let tryGetValue (key: string) : string | null =
    if cache.ContainsKey(key) then
        cache.[key]
    else
        null

// ✅ Good: Guard against nulls from C# code
let processName (name: string | null) : string =
    match name with
    | null -> "Unknown"
    | value -> value.Trim()

// ✅ Good: F# Option for internal code, nullable for C# boundary
type UserRepository() =
    // Internal: use Option
    let findUserInternal (id: string) : User option =
        // ... implementation
        None

    // Public API for C#: use nullable reference types
    member this.FindUser(id: string) : User | null =
        findUserInternal id |> Option.toObj
```

**When to use nullable reference types:**
- Public APIs consumed by C# code
- Implementing C# interfaces
- Interacting with C# libraries that use nullable annotations
- Converting between F# Option and C# nullable types

**Pattern: Converting between Option and nullable**
```fsharp
// Option to nullable (for C# API)
let toNullable (opt: 'T option) : 'T | null =
    opt |> Option.toObj

// Nullable to Option (from C# API)
let fromNullable (value: 'T | null) : 'T option =
    value |> Option.ofObj

// Example usage
type MorphirService() =
    // Internal F# code uses Option
    let loadPackage (name: string) : Package option =
        // ... implementation
        None

    // C# API uses nullable reference types
    interface IMorphirService with
        member this.LoadPackage(name: string) : Package | null =
            loadPackage name |> toNullable
```

**Important**: Even with nullable reference types enabled, prefer `Option<'T>` for F#-only code. Only use nullable reference types at C# interop boundaries.

---

## Async and Task Workflows

### Async Workflows

Use async workflows for asynchronous operations:

```fsharp
let fetchDataAsync (url: string) : Async<Result<string, string>> =
    async {
        try
            use client = new HttpClient()
            let! response = client.GetStringAsync(url) |> Async.AwaitTask
            return Ok response
        with ex ->
            return Error ex.Message
    }
```

### Cancellation Support

Always support cancellation in long-running async operations:

```fsharp
let processWithCancellation (ct: CancellationToken) : Async<Result<unit, string>> =
    async {
        try
            do! Async.Sleep(1000)  // Automatically checks cancellation

            if ct.IsCancellationRequested then
                return Error "Cancelled"
            else
                return Ok ()
        with
        | :? OperationCanceledException ->
            return Error "Cancelled"
    }
```

### Parallel Async Operations

```fsharp
let checkAllPackages packages ct =
    async {
        let! results =
            packages
            |> List.map (fun pkg -> checkPackageAsync pkg ct)
            |> Async.Parallel

        return results |> Array.toList
    }
```

---

## Type Design

### Discriminated Unions for State

Make illegal states unrepresentable:

```fsharp
// ✅ Good: Impossible to have invalid state
type WorkflowState =
    | NotStarted
    | InProgress of runId: int64 * startTime: DateTime
    | Completed of runId: int64 * result: string
    | Failed of runId: int64 * error: string

// ❌ Avoid: Boolean flags
type WorkflowState = {
    IsStarted: bool
    IsCompleted: bool
    IsFailed: bool
    RunId: int64 option
    Error: string option
}  // Can represent invalid states!
```

### Single-Case Discriminated Unions

Use for type safety and domain modeling:

```fsharp
type Version = Version of string
type PackageName = PackageName of string
type GitHash = GitHash of string

let createVersion (str: string) : Result<Version, string> =
    if str.Contains(".") then
        Ok (Version str)
    else
        Error "Invalid version format"

// Usage - type safety prevents mixing up string parameters
let publishPackage (name: PackageName) (version: Version) = ...
```

### Measure Types for Units

```fsharp
[<Measure>] type minutes
[<Measure>] type seconds

let timeout = 30<minutes>
let interval = 5<seconds>

let toSeconds (mins: float<minutes>) : float<seconds> =
    mins * 60.0<seconds/minutes>
```

---

## Computation Expressions and DSLs

Computation Expressions (CEs) are F#'s powerful mechanism for creating Domain-Specific Languages (DSLs). morphir-dotnet uses CEs extensively for building IR constructs with clean, declarative syntax.

### Understanding Computation Expressions

A computation expression is syntactic sugar that transforms code inside `{ }` blocks according to rules defined by a builder class:

```fsharp
// What you write:
literal { Bool true }

// What F# translates to:
let _builder = literal
let _state = _builder.Zero()
let _result = _builder.BoolOp(_state, true)
_result
```

### The Two Main CE Patterns

morphir-dotnet uses two complementary patterns:

#### Pattern 1: CustomOperation Pattern (Query-Style)

Use for query-like DSLs where operations flow sequentially:

```fsharp
type FQNameBuilder(packagePath, modulePath, localName) =
    new() = FQNameBuilder(None, None, None)

    member _.Zero() = FQNameBuilder(None, None, None)

    [<CustomOperation("package")>]
    member _.Package(builder: FQNameBuilder, strs: string list) =
        let pkg = Path.fromList (strs |> List.map Name.fromString) |> PackageName.packageName
        FQNameBuilder(Some pkg, builder.ModulePath, builder.LocalName)

    [<CustomOperation("module'")>]
    member _.Module(builder: FQNameBuilder, strs: string list) =
        let mod' = Path.fromList (strs |> List.map Name.fromString) |> ModulePath.modulePath
        FQNameBuilder(builder.PackagePath, Some mod', builder.LocalName)

    [<CustomOperation("local")>]
    member _.Local(builder: FQNameBuilder, str: string) =
        FQNameBuilder(builder.PackagePath, builder.ModulePath, Some (Name.fromString str))

let fqName = FQNameBuilder()

// Usage - clean query syntax:
fqName {
    package ["morphir"; "sdk"]
    module' ["basics"]
    local "int"
}
```

**Key characteristics:**
- State flows through operations (builder accumulates changes)
- CustomOperations modify and return new builder state
- No `Yield`, `Delay`, or `Run` needed for simple cases
- Clean keyword-based syntax

#### Pattern 2: Yield/Delay/Run Pattern (Compositional)

Use for hierarchical, nested structures:

```fsharp
type LiteralBuilder() =
    member _.Yield(lit: Literal) = lit
    member _.Combine(_, lit: Literal) = lit
    member _.For(items, f) = items |> Seq.map f |> Seq.last
    member _.Zero() = BoolLiteral false
    member _.Delay(f: unit -> Literal) = f
    member _.Run(f: unit -> Literal) = f()

    // CustomOperations that ignore state and return literals
    [<CustomOperation("Bool")>]
    member _.BoolOp(_: Literal, value: bool) = BoolLiteral value

    [<CustomOperation("String")>]
    member _.StringOp(_: Literal, value: string) = StringLiteral value

let literal = LiteralBuilder()

// Usage:
literal { Bool true }
literal { String "hello" }
```

**Key characteristics:**
- Supports deep nesting through `Yield`
- `Delay` and `Run` enable deferred execution
- `Combine` enables multiple expressions in one block
- CustomOperations can coexist with Yield pattern

### Hybrid Pattern (Best of Both Worlds)

morphir-dotnet's DSL builders use a hybrid approach - combining both patterns:

```fsharp
type ValueBuilder() =
    // Standard CE methods for composition
    member _.Yield(value: Value<unit, unit>) = value
    member _.Delay(f: unit -> Value<unit, unit>) = f
    member _.Run(f: unit -> Value<unit, unit>) = f()
    member _.Combine(_, value: Value<unit, unit>) = value
    member _.Zero() = Unit()

    // CustomOperations for clean syntax
    [<CustomOperation("literal")>]
    member _.LiteralOp(_: Value<unit, unit>, lit: Literal) =
        Literal((), lit)

    [<CustomOperation("tuple")>]
    member _.TupleOp(_: Value<unit, unit>, elements: Value<unit, unit> list) =
        Tuple((), elements)

let value = ValueBuilder()

// Both styles work:
value { literal (BoolLiteral true) }  // CustomOperation style
value { Literal((), BoolLiteral true) }  // Yield style
```

### CustomOperation Best Practices

#### 1. Naming Conventions

**✅ Good: Use domain-appropriate names**
```fsharp
[<CustomOperation("package")>]    // Clear, matches domain
[<CustomOperation("where")>]      // Query keyword
[<CustomOperation("select")>]     // Standard operation
```

**❌ Avoid: Generic or unclear names**
```fsharp
[<CustomOperation("set")>]        // Too generic
[<CustomOperation("do")>]         // F# keyword (confusing)
```

#### 2. State Parameter Pattern

The first parameter of a CustomOperation is always the builder state, often ignored:

```fsharp
// ✅ Good: Clear state ignore pattern
[<CustomOperation("Bool")>]
member _.BoolOp(_state: Literal, value: bool) = BoolLiteral value

// ❌ Avoid: Unnamed parameter (unclear intent)
[<CustomOperation("Bool")>]
member _.BoolOp(lit, value: bool) = BoolLiteral value
```

#### 3. Method vs CustomOperation

Choose based on usage context:

| Use CustomOperation When | Use Regular Method When |
|--------------------------|-------------------------|
| Query-style syntax desired (`where`, `select`) | Function-call style preferred |
| Inside CE blocks only | Need to call outside CE |
| Building up state | Direct value construction |
| Example: `fqName { package ["morphir"] }` | Example: `fqName.Package(["morphir"])` |

```fsharp
type Builder() =
    member _.Zero() = State.Empty

    // CustomOperation - for CE use
    [<CustomOperation("select")>]
    member _.SelectOp(state, value) = { state with Value = value }

    // Regular method - callable anywhere
    member _.Select(value) = { State.Empty with Value = value }

let b = Builder()

// CustomOperation usage (only inside CE):
b { select "name" }

// Regular method usage (anywhere):
b.Select("name")
b { Select("name") }  // Also works in CE
```

### Performance: InlineIfLambda (F# 6+)

For high-performance DSLs, use `[<InlineIfLambda>]` to eliminate closure allocations:

```fsharp
type ListBuilder() =
    [<InlineIfLambda>]
    member inline _.Delay([<InlineIfLambda>] f: unit -> 'T list) = f

    [<InlineIfLambda>]
    member inline _.Run([<InlineIfLambda>] f: unit -> 'T list) = f()

    [<InlineIfLambda>]
    member inline _.Combine([<InlineIfLambda>] a: 'T list, [<InlineIfLambda>] b: 'T list) =
        List.append a b
```

**Benefits:**
- Up to 5x faster than standard CE implementation
- Zero allocations when combined with struct builders
- Completely linear IL - nested lambdas flattened at compile time

**When to use:**
- High-frequency code paths (HTML generation, string building)
- Performance-critical DSLs
- List/array builders
- Any CE where allocations matter

**Requirements:**
- Must use `inline` functions
- Must mark lambda parameters with `[<InlineIfLambda>]`
- Only available in F# 6+

### Common CE Gotchas

#### Gotcha 1: Bare Identifiers Require Properties or CustomOperations

```fsharp
// ❌ This FAILS - F# can't find 'Bool' in scope
type LiteralBuilder() =
    member _.Zero() = BoolLiteral false
    member _.Bool(value: bool) = BoolLiteral value  // Method, not property

let literal = LiteralBuilder()
literal { Bool true }  // Error: 'Bool' not defined

// ✅ Fix Option 1: Use CustomOperation
type LiteralBuilder() =
    member _.Zero() = BoolLiteral false

    [<CustomOperation("Bool")>]
    member _.BoolOp(_: Literal, value: bool) = BoolLiteral value

// ✅ Fix Option 2: Make it a property returning a function
type LiteralBuilder() =
    member _.Zero() = BoolLiteral false
    member _.Bool = BoolLiteral  // Property

// ✅ Fix Option 3: Call with explicit parentheses
literal { Bool(true) }  // Works with regular method
```

#### Gotcha 2: F# Keywords Conflict with Method Names

```fsharp
// ❌ These conflict with F# built-in conversion functions
member _.string(value) = ...  // Conflicts with 'string' function
member _.int(value) = ...     // Conflicts with 'int' function
member _.float(value) = ...   // Conflicts with 'float' function

// ✅ Use Pascal case to avoid conflicts
member _.String(value) = ...  // No conflict
member _.Int(value) = ...
member _.Float(value) = ...
```

#### Gotcha 3: CustomOperations Return Type

CustomOperations must return the builder state type (or compatible type):

```fsharp
// ❌ Wrong return type causes "expected 'Type<unit>' but got 'unit'" error
[<CustomOperation("reference")>]
member _.ReferenceOp(_: Type<unit>, fqName: FQName) : unit =
    ()  // Returns unit, not Type<unit>!

// ✅ Correct - returns the state type
[<CustomOperation("reference")>]
member _.ReferenceOp(_: Type<unit>, fqName: FQName) : Type<unit> =
    Reference((), fqName, [])
```

### morphir-dotnet DSL Examples

#### Example 1: FQName Builder (CustomOperation Pattern)

```fsharp
// From src/Morphir.Models/IR/DSL/Names.fs
fqName {
    package ["morphir"; "sdk"]
    module' ["basics"]
    local "int"
}
// Result: FQName for morphir.sdk.basics.int
```

#### Example 2: Literal Builder (Hybrid Pattern)

```fsharp
// From src/Morphir.Models/IR/Classic/DSL/Literals.fs
literal { Bool true }
literal { String "hello" }
literal { Int 42L }
literal { Float 3.14 }
```

#### Example 3: Type Builder (Hybrid Pattern)

```fsharp
// From src/Morphir.Models/IR/Classic/DSL/Types.fs
type' {
    reference (fqName {
        package ["morphir"; "sdk"]
        module' ["list"]
        local "list"
    })
}

type' {
    tuple [
        intType
        stringType
    ]
}

type' {
    record [
        field "name" stringType
        field "age" intType
    ]
}
```

#### Example 4: Pattern Builder (Hybrid Pattern)

```fsharp
// From src/Morphir.Models/IR/Classic/DSL/Patterns.fs
pattern { wildcard }
pattern { Variable "x" }
pattern { Tuple [ pattern1; pattern2 ] }
pattern { Constructor fqName [ argPattern ] }
```

### CE Decision Tree

```
What kind of DSL are you building?
├── Query-style (operations flow sequentially)
│   └── Use: CustomOperations only
│       - Example: fqName { package []; module' []; local "" }
│       - Need: Zero, CustomOperations
│       - Skip: Yield, Delay, Run
│
├── Hierarchical (nested structures)
│   └── Use: Yield/Delay/Run pattern
│       - Example: div { span { "Hello" }; span { "World" } }
│       - Need: Yield, Delay, Run, Combine
│       - Optional: CustomOperations for keywords
│
└── Hybrid (both query and nesting)
    └── Use: Both patterns together
        - Example: morphir-dotnet IR builders
        - Need: All CE methods + CustomOperations
        - Flexible: Supports multiple usage styles
```

### Testing CE Builders

```fsharp
[<Test>]
let ``literal builder creates BoolLiteral`` () =
    let result = literal { Bool true }
    let expected = BoolLiteral true
    Assert.AreEqual(expected, result)

[<Test>]
let ``fqName builder creates correct FQName`` () =
    let result =
        fqName {
            package ["morphir"; "sdk"]
            module' ["basics"]
            local "int"
        }

    Assert.AreEqual("morphir.sdk", result.PackagePath)
    Assert.AreEqual("basics", result.ModulePath)
    Assert.AreEqual("int", result.LocalName)
```

### CE Best Practices Summary

1. ✅ **Choose the right pattern** - CustomOperations for queries, Yield for nesting
2. ✅ **Use hybrid for flexibility** - Combine both patterns in complex DSLs
3. ✅ **Name CustomOperations clearly** - Use domain-appropriate keywords
4. ✅ **Ignore state parameter** - Use `_state` or `_` when not needed
5. ✅ **Avoid F# keyword conflicts** - Use Pascal case for method names
6. ✅ **Return correct types** - CustomOperations must return builder state
7. ✅ **Consider InlineIfLambda** - For performance-critical DSLs (F# 6+)
8. ✅ **Test both styles** - If hybrid, test CustomOperation and Yield usage
9. ✅ **Document usage patterns** - Show examples of both styles
10. ✅ **Provide helper functions** - Complement CEs with standalone functions

### References

- [F# Computation Expressions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions)
- [F# RFC FS-1056: Custom Operation Overloads](https://github.com/fsharp/fslang-design/blob/main/FSharp-6.0/FS-1056-allow-custom-operation-overloads.md)
- [F# RFC FS-1098: InlineIfLambda](https://github.com/fsharp/fslang-design/blob/main/FSharp-6.0/FS-1098-inline-if-lambda.md)
- [F# for Fun and Profit - Computation Expressions Series](https://fsharpforfunandprofit.com/series/computation-expressions/)
- [Bolero HTML Builders](https://github.com/fsbolero/Bolero/blob/master/src/Bolero.Html/Builders.fs) - Excellent InlineIfLambda example
- [Fun.Blazor](https://github.com/slaveOftime/Fun.Blazor) - Component-based DSL
- [morphir-dotnet IR DSL](../src/Morphir.Models/IR/Classic/DSL/) - Reference implementation

---

## JSON Serialization with System.Text.Json

System.Text.Json is the recommended JSON library for .NET. When working with F# types, there are specific patterns and gotchas to be aware of.

**See also**: [Serialization Guide](./serialization-guide.md) for comprehensive serialization patterns across the project.

### Basic Serialization

```fsharp
#r "nuget: System.Text.Json, 9.0.0"

open System.Text.Json
open System.Text.Json.Serialization

// ✅ Good: Simple record type
type Config = {
    Port: int
    Host: string
    Timeout: int
}

let config = { Port = 8080; Host = "localhost"; Timeout = 30 }

// Serialize with options
let options = JsonSerializerOptions()
options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase
options.WriteIndented <- true

let json = JsonSerializer.Serialize(config, options)
// Output: { "port": 8080, "host": "localhost", "timeout": 30 }
```

### Common Gotchas

#### 1. F# Records Require Mutable Setters (By Default)

**Problem**: System.Text.Json by default requires mutable properties for deserialization.

```fsharp
// ❌ This will FAIL during deserialization
type User = {
    Name: string
    Age: int
}

let json = """{"name": "Alice", "age": 30}"""
let user = JsonSerializer.Deserialize<User>(json)
// Error: Cannot deserialize - no parameterless constructor or mutable properties
```

**Solution**: Use `FSharpJsonConverter` or enable record deserialization:

```fsharp
// ✅ Good: Use FSharp.SystemTextJson package
#r "nuget: FSharp.SystemTextJson, 1.3.13"

open System.Text.Json
open System.Text.Json.Serialization

let options = JsonSerializerOptions()
options.Converters.Add(JsonFSharpConverter())

let user = JsonSerializer.Deserialize<User>(json, options)
// Works correctly with immutable F# records
```

#### 2. Discriminated Unions Are Not Supported (By Default)

**Problem**: System.Text.Json doesn't understand F# discriminated unions out of the box.

```fsharp
// ❌ This will NOT serialize as expected
type Status =
    | Pending
    | InProgress of startTime: DateTime
    | Completed of result: string

let status = InProgress DateTime.Now
let json = JsonSerializer.Serialize(status)
// Output: {} or error
```

**Solution**: Use `FSharp.SystemTextJson` which handles unions properly:

```fsharp
// ✅ Good: Use FSharp.SystemTextJson
let options = JsonSerializerOptions()
options.Converters.Add(JsonFSharpConverter())

let json = JsonSerializer.Serialize(status, options)
// Output: {"Case":"InProgress","Fields":["2025-12-18T15:30:00Z"]}
```

#### 3. Option Types Serialize as Objects

**Problem**: F# `option` types don't serialize as null/value by default.

```fsharp
// ❌ Without FSharp.SystemTextJson
type Config = {
    Port: int option
    Host: string
}

let config = { Port = None; Host = "localhost" }
let json = JsonSerializer.Serialize(config)
// Output: {"Port":{},"Host":"localhost"} - Port is empty object, not null
```

**Solution**: Use `FSharp.SystemTextJson` or configure options:

```fsharp
// ✅ Good: Use FSharp.SystemTextJson
let options = JsonSerializerOptions()
options.Converters.Add(JsonFSharpConverter(
    unionEncoding = JsonUnionEncoding.Default,
    unionTagNamingPolicy = JsonNamingPolicy.CamelCase
))

let json = JsonSerializer.Serialize(config, options)
// Output: {"port":null,"host":"localhost"} - Port is null as expected
```

#### 4. JsonElement Reading for Dynamic JSON

When reading JSON with unknown structure, use `JsonElement`:

```fsharp
// ✅ Good: Active pattern for JsonElement property access
let (|JsonProperty|_|) (propertyName: string) (element: JsonElement) =
    let mutable prop = Unchecked.defaultof<JsonElement>
    if element.TryGetProperty(propertyName, &prop) then
        Some prop
    else
        None

// ✅ Good: Active pattern for JsonValueKind
let (|JsonString|JsonNumber|JsonBool|JsonNull|JsonArray|JsonObject|) (element: JsonElement) =
    match element.ValueKind with
    | JsonValueKind.String -> JsonString (element.GetString())
    | JsonValueKind.Number -> JsonNumber (element.GetInt64())
    | JsonValueKind.True -> JsonBool true
    | JsonValueKind.False -> JsonBool false
    | JsonValueKind.Null -> JsonNull
    | JsonValueKind.Array -> JsonArray (element.EnumerateArray() |> Seq.toList)
    | JsonValueKind.Object -> JsonObject element
    | _ -> JsonNull

// Usage
let doc = JsonDocument.Parse(json)
match doc.RootElement with
| JsonProperty "version" (JsonString version) -> printfn "Version: %s" version
| JsonProperty "count" (JsonNumber count) -> printfn "Count: %d" count
| _ -> printfn "Unknown structure"
```

#### 5. Null vs Option<T> in JSON

When working with C# APIs that use nullable reference types:

```fsharp
// ✅ Good: Handling nulls from JSON
type ApiResponse = {
    Data: string | null  // For C# interop
    Error: string option // For F# code
}

let parseResponse (json: string) : Result<ApiResponse, string> =
    try
        let response = JsonSerializer.Deserialize<ApiResponse>(json)
        // Convert null to Option
        let error = response.Error
        Ok response
    with ex ->
        Error ex.Message
```

### Best Practices for JSON in F# Scripts

```fsharp
// ✅ Good: Configure options once and reuse
let jsonOptions =
    let options = JsonSerializerOptions()
    options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    options.WriteIndented <- true
    options.Converters.Add(JsonFSharpConverter())
    options

// ✅ Good: Type-safe result serialization
type ScriptResult = {
    Success: bool
    Version: string option
    Errors: string list
    ExitCode: int
}

let serializeResult (result: ScriptResult) : string =
    JsonSerializer.Serialize(result, jsonOptions)

// ✅ Good: Safe deserialization with error handling
let deserializeConfig (json: string) : Result<Config, string> =
    try
        let config = JsonSerializer.Deserialize<Config>(json, jsonOptions)
        Ok config
    with
    | :? JsonException as ex -> Error $"Invalid JSON: {ex.Message}"
    | ex -> Error $"Deserialization error: {ex.Message}"
```

### CLI JSON Output Pattern

For CLI scripts that support `--json` output:

```fsharp
// ✅ Good: Separate human-readable and JSON output
let outputResult (result: ScriptResult) (jsonOutput: bool) =
    if jsonOutput then
        // ONLY JSON to stdout
        let json = JsonSerializer.Serialize(result, jsonOptions)
        printfn "%s" json
    else
        // Human-readable to stdout
        printfn "=== Results ==="
        printfn "Success: %b" result.Success
        result.Version |> Option.iter (printfn "Version: %s")
        if not result.Errors.IsEmpty then
            printfn "Errors:"
            result.Errors |> List.iter (printfn "  - %s")

// ✅ Good: Test JSON output is valid
// Test command: dotnet fsi script.fsx --json | jq .
```

### Common Patterns from prepare-release.fsx

```fsharp
// ✅ Pattern: Parse GitHub API response (JSON array)
let parseGitHubRuns (json: string) : Result<WorkflowRun list, string> =
    try
        let doc = JsonDocument.Parse(json)
        let runs =
            doc.RootElement.EnumerateArray()
            |> Seq.map (fun element ->
                {
                    Conclusion = element.GetProperty("conclusion").GetString()
                    DatabaseId = element.GetProperty("databaseId").GetInt64()
                    HeadSha = element.GetProperty("headSha").GetString()
                }
            )
            |> Seq.toList
        Ok runs
    with ex ->
        Error $"Failed to parse JSON: {ex.Message}"

// ✅ Pattern: Handle nullable JSON properties
let getConclusion (element: JsonElement) : string =
    let conclusionProp = element.GetProperty("conclusion")
    if conclusionProp.ValueKind = JsonValueKind.Null then
        "in_progress"
    else
        conclusionProp.GetString()
```

### Source Generators for AOT Compatibility

For Native AOT compilation, use source-generated serialization contexts:

```fsharp
// ✅ Good: Source-generated context for AOT
[<JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)>]
[<JsonSerializable(typeof<ScriptResult>)>]
[<JsonSerializable(typeof<Config>)>]
type MorphirJsonContext =
    inherit JsonSerializerContext

// Usage with AOT
let json = JsonSerializer.Serialize(result, MorphirJsonContext.Default.ScriptResult)
```

### Summary: JSON Serialization Checklist

- ✅ Use `FSharp.SystemTextJson` for F# types (records, unions, options)
- ✅ Configure `JsonSerializerOptions` once and reuse
- ✅ Use active patterns for reading `JsonElement` dynamically
- ✅ Handle `JsonValueKind.Null` explicitly
- ✅ Test `--json` output with `jq` to ensure valid JSON
- ✅ Separate logs (stderr) from JSON output (stdout)
- ✅ Use source generators for Native AOT scenarios
- ✅ Prefer `option` for F# code, nullable types for C# interop boundaries

---

## CLI Scripts (.fsx)

### Script Structure

Follow this structure for all `.fsx` scripts:

```fsharp
#!/usr/bin/env dotnet fsi
// Brief description
// Usage: dotnet fsi script.fsx [args]

#r "nuget: PackageName, Version"

open System

// ============================================================================
// Types
// ============================================================================

type ScriptArgs = { ... }
type ScriptResult = { ... }

// ============================================================================
// Utilities
// ============================================================================

let logInfo msg = eprintfn "[INFO] %s" msg

// ============================================================================
// Main Logic
// ============================================================================

let mainAsync (args: ScriptArgs) (ct: CancellationToken) : Async<ScriptResult> =
    async { ... }

// ============================================================================
// CLI Parsing and Entry Point
// ============================================================================

let main (args: string array) =
    // Parse args, run async logic, return exit code
    ...

exit (main fsi.CommandLineArgs.[1..])
```

### CLI Logging Standards

**CRITICAL**: Separate stdout and stderr properly:

```fsharp
let jsonOutput = args |> Array.contains "--json"

// Logs always go to stderr
let logInfo msg =
    if not jsonOutput then
        eprintfn "[INFO] %s" msg

let logError msg =
    eprintfn "[ERROR] %s" msg

// Results go to stdout
let outputResult result =
    if jsonOutput then
        let json = JsonSerializer.Serialize(result)
        printfn "%s" json  // stdout only
    else
        printfn "=== Results ===" // stdout
        // Human-readable output
```

### Argument Parsing with Argu

Use Argu for type-safe argument parsing:

```fsharp
#r "nuget: Argu, 6.2.4"

type Arguments =
    | [<Mandatory>] Version of string
    | [<AltCommandLine("-v")>] Verbose
    | Json
    | [<AltCommandLine("-t")>] Timeout of int

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Version _ -> "Version to process"
            | Verbose -> "Enable verbose output"
            | Json -> "Output as JSON"
            | Timeout _ -> "Timeout in minutes"

let parser = ArgumentParser.Create<Arguments>(programName = "script.fsx")
let results = parser.Parse(args)

let version = results.GetResult Version
let timeout = results.GetResult(Timeout, defaultValue = 30)
let verbose = results.Contains Verbose
```

---

## Testing

### Unit Tests (TUnit)

```fsharp
open TUnit.Core

[<Test>]
let ``parseChangelog returns correct count`` () =
    // Arrange
    let changelog = """
## [Unreleased]
- Added: Feature A
- Fixed: Bug B
"""

    // Act
    let result = parseChangelog changelog

    // Assert
    result.ChangeCount |> Assert.Equal 2
    result.Added |> Assert.Equal 1
    result.FixedCount |> Assert.Equal 1
```

### Property-Based Testing

```fsharp
open FsCheck

[<Property>]
let ``version parsing is reversible`` (version: string) =
    let parsed = parseVersion version
    match parsed with
    | Ok v -> formatVersion v = version
    | Error _ -> true  // Invalid versions are acceptable to reject
```

---

## Summary

Key F# principles for morphir-dotnet:

1. ✅ **Use active patterns** instead of complex if-then chains
2. ✅ **Make illegal states unrepresentable** with discriminated unions
3. ✅ **Prefer immutability** - use records and immutable collections
4. ✅ **Use Result<'T, 'Error>** for operations that can fail
5. ✅ **Support cancellation** in async workflows
6. ✅ **Separate stdout/stderr** in CLI scripts
7. ✅ **Use Argu** for CLI argument parsing
8. ✅ **Write exhaustive pattern matches** - handle all cases
9. ✅ **Prefer Option<'T>** over null in F# code
10. ✅ **Use nullable reference types (F# 9)** for C# interop boundaries
11. ✅ **Use FSharp.SystemTextJson** for JSON serialization with F# types
12. ✅ **Follow railway-oriented programming** for error handling
13. ✅ **Use CustomOperations** for query-style DSL syntax
14. ✅ **Use Yield/Delay/Run** for compositional, nested DSLs
15. ✅ **Combine both patterns** for flexible hybrid DSLs
16. ✅ **Avoid F# keyword conflicts** - Use Pascal case in CEs (Bool, String, Int)
17. ✅ **Consider InlineIfLambda** for high-performance CEs (F# 6+)

---

## References

- [F# Style Guide](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/)
- [F# Design Guidelines](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/conventions)
- [F# 9 Nullable Reference Types](https://learn.microsoft.com/en-us/dotnet/fsharp/whats-new/fsharp-9#nullable-reference-types)
- [FSharp.SystemTextJson](https://github.com/Tarmil/FSharp.SystemTextJson) - F# support for System.Text.Json
- [System.Text.Json Documentation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
- [Domain Modeling Made Functional](https://fsharpforfunandprofit.com/books/)
- [Railway Oriented Programming](https://fsharpforfunandprofit.com/rop/)
- [Serialization Guide](./serialization-guide.md) - Comprehensive serialization patterns (cross-language)
- [AGENTS.md](../../AGENTS.md) - Project-wide agent guidance
