# Functional Programming Patterns Knowledge Base

**Task**: Task 1.3 - Functional Programming Pattern Library (Issue #317)
**Created**: 2025-12-24
**Purpose**: Comprehensive guide to functional programming patterns for the Morphir Application Architect skill, with F# and C# implementations focused on morphir-dotnet

## Table of Contents

1. [Monads](#1-monads)
2. [Functors](#2-functors)
3. [Applicatives](#3-applicatives)
4. [Lenses and Optics](#4-lenses-and-optics)
5. [Railway-Oriented Programming](#5-railway-oriented-programming)
6. [Algebraic Effects and Free Monads](#6-algebraic-effects-and-free-monads)
7. [Fold Patterns (Catamorphisms)](#7-fold-patterns-catamorphisms)
8. [Recursion Schemes](#8-recursion-schemes)
9. [Phantom Types](#9-phantom-types)
10. [Higher-Kinded Types](#10-higher-kinded-types)
11. [Continuation-Passing Style](#11-continuation-passing-style)
12. [Trampolining](#12-trampolining)
13. [Lazy Evaluation](#13-lazy-evaluation)
14. [Immutability Patterns](#14-immutability-patterns)
15. [Parser Combinators](#15-parser-combinators)
16. [Dependency Injection (Reader Monad)](#16-dependency-injection-reader-monad)
17. [Event Sourcing Patterns](#17-event-sourcing-patterns)
18. [Bridging Patterns (FP ↔ OO)](#18-bridging-patterns-fp--oo)

---

## Overview

This knowledge base documents **18 core functional programming patterns** with implementations in both F# and C# where applicable. Each pattern includes:
- **Definition**: What the pattern is and when to use it
- **Laws**: Mathematical laws (where applicable)
- **F# Implementation**: Idiomatic F# code
- **C# Implementation**: C# equivalent (where practical)
- **Morphir Usage**: How it's used or could be used in morphir-dotnet
- **Trade-offs**: Benefits and drawbacks

---

## 1. Monads

### Definition

A monad is a design pattern that allows chaining operations while maintaining context. Formally, a monad is a type constructor `M<T>` with two operations:
- **return/pure**: `T -> M<T>` (wraps a value)
- **bind (>>=)**: `M<T> -> (T -> M<U>) -> M<U>` (chains operations)

### Laws

Monads must satisfy three laws:

1. **Left Identity**: `return a >>= f ≡ f a`
2. **Right Identity**: `m >>= return ≡ m`
3. **Associativity**: `(m >>= f) >>= g ≡ m >>= (\x -> f x >>= g)`

### Common Monads

#### 1.1 Option/Maybe Monad

**Purpose**: Represent optional values without null.

**F# Implementation**:
```fsharp
// F# built-in option type
type Option<'T> =
    | Some of 'T
    | None

// Bind operator
let (>>=) opt f =
    match opt with
    | Some value -> f value
    | None -> None

// Map (functor)
let map f opt =
    match opt with
    | Some value -> Some (f value)
    | None -> None

// Usage
let safeDivide x y =
    if y = 0 then None
    else Some (x / y)

let result =
    Some 10
    >>= fun x -> safeDivide x 2
    >>= fun x -> safeDivide x 5
// Result: Some 1
```

**C# Implementation**:
```csharp
public static class OptionExtensions
{
    public static Option<U> Bind<T, U>(this Option<T> option, Func<T, Option<U>> f) =>
        option.IsSome ? f(option.Value) : Option<U>.None;

    public static Option<U> Map<T, U>(this Option<T> option, Func<T, U> f) =>
        option.IsSome ? Option<U>.Some(f(option.Value)) : Option<U>.None;
}

// Usage
var result = Some(10)
    .Bind(x => SafeDivide(x, 2))
    .Bind(x => SafeDivide(x, 5));
```

**Morphir Usage**: Optional type parameters, nullable field access in IR.

#### 1.2 Result/Either Monad

**Purpose**: Represent success or failure with error information.

**F# Implementation**:
```fsharp
// F# built-in Result type
type Result<'T, 'Error> =
    | Ok of 'T
    | Error of 'Error

// Bind operator
let (>>=) result f =
    match result with
    | Ok value -> f value
    | Error err -> Error err

// Map operator (functor)
let map f result =
    match result with
    | Ok value -> Ok (f value)
    | Error err -> Error err

// MapError: Transform error without affecting success
let mapError f result =
    match result with
    | Ok value -> Ok value
    | Error err -> Error (f err)

// Usage in validation
type ValidationError =
    | EmptyName
    | InvalidAge of int
    | MissingEmail

let validateName name =
    if String.IsNullOrWhiteSpace(name) then Error EmptyName
    else Ok name

let validateAge age =
    if age < 0 || age > 150 then Error (InvalidAge age)
    else Ok age

let validatePerson name age =
    validateName name
    >>= fun validName ->
        validateAge age
        >>= fun validAge ->
            Ok { Name = validName; Age = validAge }
```

**C# Implementation**:
```csharp
public abstract record Result<T, E>
{
    public sealed record Ok(T Value) : Result<T, E>;
    public sealed record Error(E ErrorValue) : Result<T, E>;
}

public static class ResultExtensions
{
    public static Result<U, E> Bind<T, E, U>(
        this Result<T, E> result,
        Func<T, Result<U, E>> f) =>
        result switch
        {
            Result<T, E>.Ok(var value) => f(value),
            Result<T, E>.Error(var err) => new Result<U, E>.Error(err),
            _ => throw new InvalidOperationException()
        };

    public static Result<U, E> Map<T, E, U>(
        this Result<T, E> result,
        Func<T, U> f) =>
        result switch
        {
            Result<T, E>.Ok(var value) => new Result<U, E>.Ok(f(value)),
            Result<T, E>.Error(var err) => new Result<U, E>.Error(err),
            _ => throw new InvalidOperationException()
        };
}
```

**Morphir Usage**:
- IR validation (`src/Morphir.Tooling/Features/VerifyIR/VerifyIR.cs`)
- CLI error handling
- Codec roundtrip tests

**Current Usage in morphir-dotnet**:
```csharp
// From src/Morphir.Tooling/Features/VerifyIR/VerifyIR.cs (pattern)
public async Task<Result<VerifyIRResult, VerifyIRError>> ExecuteAsync(...)
{
    return await ValidateSchema(irPath)
        .Bind(schema => LoadIR(irPath))
        .Bind(ir => ValidateAgainstSchema(ir, schema))
        .Map(ir => new VerifyIRResult(ir));
}
```

#### 1.3 List Monad

**Purpose**: Non-deterministic computation, branching.

**F# Implementation**:
```fsharp
// Bind for list (flatMap/SelectMany)
let (>>=) list f = List.collect f list

// Example: Generate all pairs
let pairs =
    [1; 2; 3]
    >>= fun x ->
        ['a'; 'b']
        >>= fun y ->
            [(x, y)]
// Result: [(1,'a'); (1,'b'); (2,'a'); (2,'b'); (3,'a'); (3,'b')]
```

**C# LINQ Implementation**:
```csharp
// LINQ's SelectMany is bind for IEnumerable
var pairs =
    from x in new[] { 1, 2, 3 }
    from y in new[] { 'a', 'b' }
    select (x, y);
```

**Morphir Usage**: Generating test cases, cross-product operations in IR transformations.

#### 1.4 State Monad

**Purpose**: Thread state through computations.

**F# Implementation**:
```fsharp
type State<'S, 'A> = State of ('S -> 'A * 'S)

module State =
    let run (State f) state = f state

    let returnState value = State (fun state -> (value, state))

    let bind (State f) g =
        State (fun state ->
            let (value, newState) = f state
            let (State h) = g value
            h newState
        )

    let get = State (fun state -> (state, state))
    let put newState = State (fun _ -> ((), newState))
    let modify f = State (fun state -> ((), f state))

// Example: Counter with state
let increment =
    State.get
    |> State.bind (fun count ->
        State.put (count + 1)
        |> State.bind (fun () ->
            State.returnState count
        )
    )

let (result, finalState) = State.run increment 0
// result = 0, finalState = 1
```

**Morphir Usage**: IR transformations with state tracking (variable renaming, fresh name generation).

#### 1.5 Reader Monad

**Purpose**: Dependency injection, configuration threading.

**F# Implementation**:
```fsharp
type Reader<'Env, 'A> = Reader of ('Env -> 'A)

module Reader =
    let run (Reader f) env = f env

    let returnReader value = Reader (fun _ -> value)

    let bind (Reader f) g =
        Reader (fun env ->
            let value = f env
            let (Reader h) = g value
            h env
        )

    let ask = Reader (fun env -> env)

// Example: Configuration-based computation
type Config = { BaseUrl: string; Timeout: int }

let fetchData path =
    Reader.ask
    |> Reader.bind (fun config ->
        Reader.returnReader $"{config.BaseUrl}/{path}"
    )

let result = Reader.run (fetchData "api/users") { BaseUrl = "https://api.com"; Timeout = 30 }
// result = "https://api.com/api/users"
```

**Morphir Usage**: Threading configuration through IR transformations, backend code generation.

#### 1.6 IO Monad (Async/Task)

**Purpose**: Encapsulate side effects.

**F# Async (Built-in)**:
```fsharp
let fetchData url = async {
    use client = new HttpClient()
    let! response = client.GetStringAsync(url) |> Async.AwaitTask
    return response
}

let processData = async {
    let! data1 = fetchData "https://api1.com"
    let! data2 = fetchData "https://api2.com"
    return data1 + data2
}
```

**C# Task (Built-in)**:
```csharp
async Task<string> FetchData(string url)
{
    using var client = new HttpClient();
    return await client.GetStringAsync(url);
}

async Task<string> ProcessData()
{
    var data1 = await FetchData("https://api1.com");
    var data2 = await FetchData("https://api2.com");
    return data1 + data2;
}
```

**Morphir Usage**: File I/O, network requests, CLI operations.

### Trade-offs

**Benefits**:
- Composability: Chain operations cleanly
- Error handling: Explicit error propagation (Result)
- Testability: Pure functions easier to test
- Type safety: Compile-time guarantees

**Drawbacks**:
- Learning curve: Unfamiliar to OO developers
- Verbosity: Can be verbose without language support
- Performance: Allocations for wrapper types

---

## 2. Functors

### Definition

A functor is a type constructor `F<T>` with a `map` operation:
- **map**: `(T -> U) -> F<T> -> F<U>`

Functors preserve structure while transforming contents.

### Laws

1. **Identity**: `map id ≡ id`
2. **Composition**: `map (f ∘ g) ≡ map f ∘ map g`

### Implementations

#### 2.1 List Functor

**F# Implementation**:
```fsharp
// Built-in List.map
let numbers = [1; 2; 3]
let doubled = List.map (fun x -> x * 2) numbers
// Result: [2; 4; 6]
```

**C# LINQ Implementation**:
```csharp
var numbers = new[] { 1, 2, 3 };
var doubled = numbers.Select(x => x * 2);
// Result: [2, 4, 6]
```

#### 2.2 Option Functor

**F# Implementation**:
```fsharp
let maybeNumber = Some 5
let doubled = Option.map (fun x -> x * 2) maybeNumber
// Result: Some 10

let nothing = None
let result = Option.map (fun x -> x * 2) nothing
// Result: None
```

#### 2.3 Tree Functor (Morphir Type)

**F# Implementation**:
```fsharp
// From morphir-dotnet Classic IR
let rec mapType (f: 'a -> 'b) (typ: Type<'a>) : Type<'b> =
    match typ with
    | Variable (attrs, name) -> Variable (f attrs, name)
    | Reference (attrs, fqName, typeParams) ->
        Reference (f attrs, fqName, List.map (mapType f) typeParams)
    | Tuple (attrs, elementTypes) ->
        Tuple (f attrs, List.map (mapType f) elementTypes)
    | Record (attrs, fields) ->
        Record (f attrs, List.map (mapField f) fields)
    | ExtensibleRecord (attrs, varName, fields) ->
        ExtensibleRecord (f attrs, varName, List.map (mapField f) fields)
    | Function (attrs, param, ret) ->
        Function (f attrs, mapType f param, mapType f ret)
    | Unit attrs -> Unit (f attrs)

and mapField (f: 'a -> 'b) (field: Field<'a>) : Field<'b> =
    { Name = field.Name; Type = mapType f field.Type }
```

**Morphir Usage**:

**Transforming AST Attributes**:
```fsharp
// Add source locations to untyped IR
let addSourceInfo: Type<unit> -> Type<SourceSpan> =
    mapType (fun () -> generateSourceSpan())

// Remove attributes for serialization
let stripAttributes: Type<SourceSpan> -> Type<unit> =
    mapType (fun _ -> ())
```

### Trade-offs

**Benefits**:
- Uniform API across different types
- Composition: `map f . map g = map (f . g)`
- Parallelizable: Independent transformations

**Drawbacks**:
- Cannot change structure (use monad for that)
- Allocates new structure even for identity

---

## 3. Applicatives

### Definition

An applicative functor is stronger than a functor but weaker than a monad. It provides:
- **pure**: `T -> F<T>` (lift value)
- **apply (<*>)**: `F<(T -> U)> -> F<T> -> F<U>` (apply wrapped function)

### Laws

1. **Identity**: `pure id <*> v ≡ v`
2. **Composition**: `pure (∘) <*> u <*> v <*> w ≡ u <*> (v <*> w)`
3. **Homomorphism**: `pure f <*> pure x ≡ pure (f x)`
4. **Interchange**: `u <*> pure y ≡ pure ($ y) <*> u`

### Use Cases

#### 3.1 Validation (Accumulating Errors)

**F# Implementation**:
```fsharp
type Validation<'T, 'Error> =
    | Success of 'T
    | Failure of 'Error list

module Validation =
    let pure value = Success value

    let apply fValidation xValidation =
        match fValidation, xValidation with
        | Success f, Success x -> Success (f x)
        | Failure errs1, Failure errs2 -> Failure (errs1 @ errs2)
        | Failure errs, _ -> Failure errs
        | _, Failure errs -> Failure errs

    let map2 f xVal yVal =
        apply (apply (pure f) xVal) yVal

    let map3 f xVal yVal zVal =
        apply (apply (apply (pure f) xVal) yVal) zVal

// Example: Validate person with accumulating errors
type Person = { Name: string; Age: int; Email: string }

let validateName name =
    if String.IsNullOrWhiteSpace(name) then
        Failure ["Name cannot be empty"]
    else
        Success name

let validateAge age =
    if age < 0 || age > 150 then
        Failure [$"Invalid age: {age}"]
    else
        Success age

let validateEmail email =
    if not (email.Contains("@")) then
        Failure ["Invalid email format"]
    else
        Success email

let validatePerson name age email =
    Validation.map3
        (fun n a e -> { Name = n; Age = a; Email = e })
        (validateName name)
        (validateAge age)
        (validateEmail email)

// Usage
let result1 = validatePerson "" -5 "invalid"
// Result: Failure ["Name cannot be empty"; "Invalid age: -5"; "Invalid email format"]

let result2 = validatePerson "Alice" 30 "alice@example.com"
// Result: Success { Name = "Alice"; Age = 30; Email = "alice@example.com" }
```

**Key Difference from Monad**:
- **Monad (bind)**: Short-circuits on first error (railway-oriented programming)
- **Applicative**: Accumulates all errors for better user feedback

**Morphir Usage**:

**IR Validation with Error Accumulation**:
```fsharp
let validateModule (moduleDef: ModuleDefinition<'a>) : Validation<ModuleDefinition<'a>, ValidationError> =
    Validation.map2
        (fun types values -> { Types = types; Values = values })
        (validateTypes moduleDef.Types)
        (validateValues moduleDef.Values)
// Collects all type errors AND all value errors
```

### Trade-offs

**Benefits**:
- Error accumulation: Better UX
- Parallelization: Independent operations
- Less powerful = more optimizations possible

**Drawbacks**:
- Cannot depend on previous results (use monad for that)
- More complex to implement than functor

---

## 4. Lenses and Optics

### Definition

Lenses provide composable getters and setters for immutable data structures.

**Lens**:
```fsharp
type Lens<'S, 'A> = {
    Get: 'S -> 'A
    Set: 'A -> 'S -> 'S
}
```

### Laws

1. **Get-Put**: `set (get s) s ≡ s` (setting what you got changes nothing)
2. **Put-Get**: `get (set a s) ≡ a` (getting what you set returns that value)
3. **Put-Put**: `set a2 (set a1 s) ≡ set a2 s` (setting twice is like setting once)

### Implementation

#### 4.1 Basic Lens

**F# Implementation**:
```fsharp
type Lens<'S, 'A> = {
    Get: 'S -> 'A
    Set: 'A -> 'S -> 'S
}

module Lens =
    let get lens source = lens.Get source
    let set lens value source = lens.Set value source
    let modify lens f source = set lens (f (get lens source)) source

    // Lens composition
    let compose (outer: Lens<'A, 'B>) (inner: Lens<'B, 'C>) : Lens<'A, 'C> =
        {
            Get = fun a -> inner.Get (outer.Get a)
            Set = fun c a -> outer.Set (inner.Set c (outer.Get a)) a
        }

    let (>>>) = compose

// Example: Person with address
type Address = { Street: string; City: string; Zip: string }
type Person = { Name: string; Age: int; Address: Address }

// Lenses
let nameLens = { Get = fun p -> p.Name; Set = fun n p -> { p with Name = n } }
let addressLens = { Get = fun p -> p.Address; Set = fun a p -> { p with Address = a } }
let cityLens = { Get = fun a -> a.City; Set = fun c a -> { a with City = c } }

// Composed lens: Person -> City
let personCityLens = addressLens >>> cityLens

// Usage
let person = {
    Name = "Alice"
    Age = 30
    Address = { Street = "123 Main St"; City = "Springfield"; Zip = "12345" }
}

let updatedPerson = Lens.set personCityLens "Shelbyville" person
// Result: { ... Address = { ... City = "Shelbyville" ... } ... }
```

#### 4.2 Morphir IR Lens Example

**F# Implementation**:
```fsharp
// Lenses for ModuleDefinition
let typesLens: Lens<ModuleDefinition<'a>, Map<Name, TypeDefinition<'a>>> =
    {
        Get = fun m -> m.Types
        Set = fun t m -> { m with Types = t }
    }

let valuesLens: Lens<ModuleDefinition<'a>, Map<Name, ValueDefinition<'a>>> =
    {
        Get = fun m -> m.Values
        Set = fun v m -> { m with Values = v }
    }

// Lens for accessing a specific type by name
let typeByNameLens (name: Name) : Lens<ModuleDefinition<'a>, TypeDefinition<'a> option> =
    {
        Get = fun m -> Map.tryFind name m.Types
        Set = fun tOpt m ->
            match tOpt with
            | Some t -> { m with Types = Map.add name t m.Types }
            | None -> { m with Types = Map.remove name m.Types }
    }

// Usage: Update a specific type in a module
let updateType name f moduleDef =
    Lens.modify (typeByNameLens name) (Option.map f) moduleDef
```

#### 4.3 C# Lenses (Manual)

**C# Implementation (using with expressions)**:
```csharp
// C# doesn't have first-class lens support, but records with `with` provide similar functionality
public record Address(string Street, string City, string Zip);
public record Person(string Name, int Age, Address Address);

// Manual lens-like pattern
public static class PersonLenses
{
    public static Person SetCity(Person person, string city) =>
        person with { Address = person.Address with { City = city } };

    public static string GetCity(Person person) =>
        person.Address.City;
}

// Usage
var person = new Person("Alice", 30, new Address("123 Main", "Springfield", "12345"));
var updated = PersonLenses.SetCity(person, "Shelbyville");
```

### Morphir Usage

**IR Transformations**:
- Update nested types in ModuleDefinition
- Modify specific fields in records
- Compose transformations cleanly

**Benefits**:
- Composability: Lens composition for nested updates
- Immutability: Safe updates without mutation
- Reusability: Lenses can be reused across codebase

**Trade-offs**:
- Boilerplate: Manual lens creation is verbose (use code generation)
- Learning curve: Unfamiliar concept
- Performance: Extra allocations for composition

**Note**: morphir-dotnet has a placeholder LensGenerator in `src/Morphir.Internal.CodeGeneration/Generators/LensGenerator.fs` for future implementation.

---

## 5. Railway-Oriented Programming

### Definition

Railway-Oriented Programming (ROP) is a functional error handling pattern that uses the Result type to create a pipeline of operations. Visualize computation as a railway track with two rails: success and failure.

**Concept** (from Scott Wlaschin):
- **Success rail**: Happy path, operations continue
- **Failure rail**: Error occurred, skip remaining operations
- **Switch**: Functions that can derail to failure rail
- **Map**: Functions that stay on success rail

### Core Operators

**F# Implementation**:
```fsharp
module Result =
    // Bind (>>=): Railway switch - can change rails
    let bind f result =
        match result with
        | Ok value -> f value
        | Error err -> Error err

    // Map: Stay on success rail
    let map f result =
        match result with
        | Ok value -> Ok (f value)
        | Error err -> Error err

    // MapError: Transform error without affecting success
    let mapError f result =
        match result with
        | Ok value -> Ok value
        | Error err -> Error (f err)

    // Operators for composition
    let (>>=) = bind
    let (<!>) = map
    let (>=>) f g = fun x -> f x >>= g  // Kleisli composition

// Example: Validation pipeline
type ValidationError =
    | InvalidFormat of string
    | OutOfRange of string * int * int
    | EmptyValue of string

let validateNotEmpty fieldName value =
    if String.IsNullOrWhiteSpace(value) then
        Error (EmptyValue fieldName)
    else
        Ok value

let validateFormat fieldName pattern value =
    if System.Text.RegularExpressions.Regex.IsMatch(value, pattern) then
        Ok value
    else
        Error (InvalidFormat $"{fieldName} doesn't match {pattern}")

let validateRange fieldName min max value =
    if value >= min && value <= max then
        Ok value
    else
        Error (OutOfRange (fieldName, min, max))

// Composition: All validations must pass
let validateEmail email =
    validateNotEmpty "email" email
    >>= validateFormat "email" @".+@.+\..+"

let validateAge age =
    Ok age
    >>= validateRange "age" 0 150

// Pipeline: Short-circuits on first error
let validatePerson name email age =
    Ok (fun n e a -> { Name = n; Email = e; Age = a })
    <!> validateNotEmpty "name" name
    >>= fun f -> validateEmail email >>= fun e -> Ok (f e)
    >>= fun f -> validateAge age >>= fun a -> Ok (f a)
```

### Morphir-dotnet Usage

**IR Validation**:
```csharp
// From src/Morphir.Tooling/Features/VerifyIR/VerifyIR.cs (pattern)
public async Task<Result<VerifyIRResult, VerifyIRError>> ExecuteAsync(string irPath)
{
    return await LoadSchema(irPath)
        .Bind(schema => ParseIR(irPath).Map(ir => (schema, ir)))
        .Bind(tuple => ValidateIR(tuple.schema, tuple.ir))
        .Map(validIR => new VerifyIRResult(validIR));
}
```

**IR Transformation Pipeline**:
```fsharp
let transformIR (ir: Distribution) : Result<Distribution, TransformError> =
    Ok ir
    >>= validateDistribution
    >>= optimizeTypes
    >>= inlineSmallFunctions
    >>= removeUnusedDefinitions
    >>= validateFinalIR
// Short-circuits on first transformation error
```

### Benefits

- **Explicit Error Handling**: No hidden exceptions
- **Composability**: Chain operations cleanly
- **Short-Circuiting**: Stop on first error
- **Type Safety**: Compiler ensures error handling

### Trade-offs

- **Verbosity**: More code than exception-based error handling
- **Learning Curve**: Unfamiliar to imperative developers
- **Performance**: Allocations for Result wrappers

---

## 6. Algebraic Effects and Free Monads

### Definition

**Algebraic Effects**: Separate effect declaration from implementation, allowing multiple interpretations.

**Free Monad**: A monad constructed from a functor, allowing you to build an AST of operations that can be interpreted later.

### Free Monad Implementation

**F# Implementation**:
```fsharp
type Free<'F, 'A> =
    | Pure of 'A
    | Free of 'F<Free<'F, 'A>>

module Free =
    let rec bind f = function
        | Pure a -> f a
        | Free fa -> Free (fmap (bind f) fa)

    let returnFree a = Pure a
    let liftF fa = Free (fmap Pure fa)

// Example: Console IO DSL
type ConsoleF<'Next> =
    | PrintLine of string * 'Next
    | ReadLine of (string -> 'Next)

module ConsoleF =
    let fmap f = function
        | PrintLine (msg, next) -> PrintLine (msg, f next)
        | ReadLine k -> ReadLine (k >> f)

type Console<'A> = Free<ConsoleF, 'A>

module Console =
    let printLine msg : Console<unit> =
        Free.liftF (PrintLine (msg, ()))

    let readLine : Console<string> =
        Free.liftF (ReadLine id)

// Interpreter 1: Real console
let rec interpretConsoleIO = function
    | Pure a -> a
    | Free (PrintLine (msg, next)) ->
        printfn "%s" msg
        interpretConsoleIO next
    | Free (ReadLine k) ->
        let input = System.Console.ReadLine()
        interpretConsoleIO (k input)

// Interpreter 2: Test interpreter
let interpretConsoleTest inputs program =
    let mutable outputs = []
    let mutable inputList = inputs

    let rec go = function
        | Pure a -> (a, List.rev outputs)
        | Free (PrintLine (msg, next)) ->
            outputs <- msg :: outputs
            go next
        | Free (ReadLine k) ->
            match inputList with
            | input :: rest ->
                inputList <- rest
                go (k input)
            | [] -> go (k "")

    go program
```

### Morphir Application: IR Construction DSL

**Proposed Usage**:
```fsharp
type MorphirF<'Next> =
    | DefineType of Name * TypeDefinition * 'Next
    | DefineValue of Name * ValueDefinition * 'Next
    | ImportModule of Path * 'Next

// Interpreter: Build ModuleDefinition
let interpretToModule builder =
    let rec go types values = function
        | Pure () -> { Types = types; Values = values }
        | Free (DefineType (name, typeDef, next)) ->
            go (Map.add name typeDef types) values next
        | Free (DefineValue (name, valueDef, next)) ->
            go types (Map.add name valueDef values) next
        | Free (ImportModule (path, next)) ->
            go types values next

    go Map.empty Map.empty builder
```

### Benefits

- **Testability**: Multiple interpreters for same program
- **Separation of Concerns**: Logic vs execution
- **Composability**: Build complex programs from simple operations

### Trade-offs

- **Complexity**: Requires understanding of free structures
- **Performance**: Interpretation overhead
- **Boilerplate**: Functor instances and interpreters

---

## 7. Fold Patterns (Catamorphisms)

### Definition

**Catamorphism** (fold): Consume/destruct a structure from bottom-up.

### F# List Fold

**F# Implementation**:
```fsharp
// Left fold (tail-recursive)
let rec foldl f acc list =
    match list with
    | [] -> acc
    | x :: xs -> foldl f (f acc x) xs

// Right fold (not tail-recursive)
let rec foldr f list acc =
    match list with
    | [] -> acc
    | x :: xs -> f x (foldr f xs acc)

// Example: Sum
let sum = foldl (+) 0 [1; 2; 3; 4; 5]  // 15

// Example: Reverse
let reverse = foldl (fun acc x -> x :: acc) []
```

### Morphir Type Catamorphism

**F# Implementation**:
```fsharp
// Fold over Type structure
let rec foldType
    (varCase: 'a -> Name -> 'b)
    (refCase: 'a -> FQName -> 'b list -> 'b)
    (tupleCase: 'a -> 'b list -> 'b)
    (recordCase: 'a -> (Name * 'b) list -> 'b)
    (extRecCase: 'a -> Name -> (Name * 'b) list -> 'b)
    (funcCase: 'a -> 'b -> 'b -> 'b)
    (unitCase: 'a -> 'b)
    (typ: Type<'a>) : 'b =
    match typ with
    | Variable (attrs, name) -> varCase attrs name
    | Reference (attrs, fqName, typeParams) ->
        let foldedParams = List.map (foldType varCase refCase tupleCase recordCase extRecCase funcCase unitCase) typeParams
        refCase attrs fqName foldedParams
    | Tuple (attrs, elementTypes) ->
        let foldedElements = List.map (foldType varCase refCase tupleCase recordCase extRecCase funcCase unitCase) elementTypes
        tupleCase attrs foldedElements
    | Record (attrs, fields) ->
        let foldedFields = fields |> List.map (fun f ->
            (f.Name, foldType varCase refCase tupleCase recordCase extRecCase funcCase unitCase f.Type)
        )
        recordCase attrs foldedFields
    | ExtensibleRecord (attrs, varName, fields) ->
        let foldedFields = fields |> List.map (fun f ->
            (f.Name, foldType varCase refCase tupleCase recordCase extRecCase funcCase unitCase f.Type)
        )
        extRecCase attrs varName foldedFields
    | Function (attrs, param, ret) ->
        let foldedParam = foldType varCase refCase tupleCase recordCase extRecCase funcCase unitCase param
        let foldedRet = foldType varCase refCase tupleCase recordCase extRecCase funcCase unitCase ret
        funcCase attrs foldedParam foldedRet
    | Unit attrs -> unitCase attrs

// Example: Count type constructors
let countTypes typ =
    foldType
        (fun _ _ -> 1)                      // Variable
        (fun _ _ ps -> 1 + List.sum ps)     // Reference
        (fun _ es -> 1 + List.sum es)       // Tuple
        (fun _ fs -> 1 + List.sumBy snd fs) // Record
        (fun _ _ fs -> 1 + List.sumBy snd fs) // ExtensibleRecord
        (fun _ p r -> 1 + p + r)            // Function
        (fun _ -> 1)                        // Unit
        typ

// Example: Pretty-print type
let prettyPrintType typ =
    foldType
        (fun _ name -> Name.toString name)
        (fun _ fqName params ->
            let paramStr = if List.isEmpty params then "" else $"<{String.concat ", " params}>"
            $"{FQName.toString fqName}{paramStr}"
        )
        (fun _ elements -> $"({String.concat ", " elements})")
        (fun _ fields ->
            let fieldStrs = fields |> List.map (fun (n, t) -> $"{Name.toString n}: {t}")
            $"{{{String.concat ", " fieldStrs}}}"
        )
        (fun _ varName fields ->
            let fieldStrs = fields |> List.map (fun (n, t) -> $"{Name.toString n}: {t}")
            $"{{ {Name.toString varName} | {String.concat ", " fieldStrs} }}"
        )
        (fun _ param ret -> $"{param} -> {ret}")
        (fun _ -> "()")
        typ
```

### Morphir Usage

**IR Transformations**:
- Validate IR
- Collect statistics
- Generate code

### Benefits

- **Recursive Data**: Natural fit for tree structures
- **Separation of Concerns**: Traversal logic separate from processing
- **Composability**: Combine folds

### Trade-offs

- **Stack Safety**: Right folds not tail-recursive
- **Learning Curve**: Unfamiliar terminology
- **Performance**: Can be less efficient than specialized traversals

---

## 8. Recursion Schemes

### Definition

Recursion schemes are generalized patterns for recursive data processing, separating recursion from business logic.

**Note**: morphir-dotnet currently uses direct recursion (simpler, more readable). Recursion schemes are documented for future advanced transformations.

### Fixed-Point Types

**F# Implementation**:
```fsharp
// Fixed-point type
type Fix<'F> = Fix of 'F<Fix<'F>>

// Example: List without built-in recursion
type ListF<'A, 'R> =
    | Nil
    | Cons of 'A * 'R

type List<'A> = Fix<ListF<'A>>

// Catamorphism: Fold a fixed-point structure
let rec cata (alg: 'F<'A> -> 'A) (Fix f: Fix<'F>) : 'A =
    alg (fmap (cata alg) f)
```

### Recommendation for Morphir

**Current Approach**: Direct recursion in Classic IR (simpler, more readable)
**Future Consideration**: Recursion schemes for advanced transformations

---

## 9. Phantom Types

### Definition

Phantom types are type parameters that don't appear in the runtime representation but enforce compile-time constraints.

### F# Units of Measure

**F# Implementation**:
```fsharp
[<Measure>] type USD
[<Measure>] type EUR
[<Measure>] type meter
[<Measure>] type second

let price = 100.0<USD>
let exchangeRate = 1.2<EUR/USD>
let converted = price * exchangeRate  // Type: float<EUR>

// Compile error: cannot add USD and EUR
// let invalid = price + converted

let distance = 100.0<meter>
let time = 10.0<second>
let speed = distance / time  // Type: float<meter/second>
```

### Phantom Type Example: IR Versioning

**F# Implementation**:
```fsharp
type V1
type V2
type V3

type IR<'Version> = {
    Version: string
    Packages: Package list
}

// Migration functions enforce version progression
let migrateV1toV2 (ir: IR<V1>) : IR<V2> =
    { ir with Version = "2.0" }

let migrateV2toV3 (ir: IR<V2>) : IR<V3> =
    { ir with Version = "3.0" }

// Compile error if trying to skip a version
// let invalid (ir: IR<V1>) : IR<V3> = migrateV2toV3 ir

// Correct: Chain migrations
let migrateV1toV3 (ir: IR<V1>) : IR<V3> =
    ir |> migrateV1toV2 |> migrateV2toV3
```

### Morphir Application

**Proposed Usage**: IR Validation Stages
```fsharp
type Unvalidated
type Validated

type ModuleDefinition<'ValidationState> = {
    Types: Map<Name, TypeDefinition>
    Values: Map<Name, ValueDefinition>
}

let validateModule (m: ModuleDefinition<Unvalidated>) : Result<ModuleDefinition<Validated>, ValidationError> =
    (* ... validation logic ... *)

let optimizeModule (m: ModuleDefinition<Validated>) : ModuleDefinition<Validated> =
    (* ... optimization (only on validated IR) ... *)

// Cannot optimize unvalidated module (compile error)
// let invalid (m: ModuleDefinition<Unvalidated>) = optimizeModule m
```

### Benefits

- **Type Safety**: Prevent invalid state transitions at compile time
- **Documentation**: Types express intent
- **Zero Runtime Cost**: Phantom types erased at runtime

### Trade-offs

- **Complexity**: Requires advanced type system knowledge
- **Rigidity**: Can be overly restrictive

---

## 10. Higher-Kinded Types

### Definition

Higher-kinded types (HKTs) abstract over type constructors like `List`, `Option`, `Result`.

**Note**: F# and C# don't have native HKT support. Encoding is complex and boilerplate-heavy.

### Recommendation for Morphir

**Avoid** HKT encoding in morphir-dotnet due to complexity and lack of native support. Specific implementations (List.map, Option.map) are clearer.

---

## 11. Continuation-Passing Style

### Definition

Continuation-Passing Style (CPS) passes control flow explicitly as a function.

**F# Implementation**:
```fsharp
// CPS factorial (tail-recursive)
let rec factorialCPS n cont =
    if n <= 1 then cont 1
    else factorialCPS (n - 1) (fun result -> cont (n * result))

let fact5 = factorialCPS 5 id  // 120
```

### Morphir Application

**Deep AST Traversal** (stack-safe):
```fsharp
let rec traverseTypeCPS onLeaf onNode typ cont =
    match typ with
    | Variable (attrs, name) -> cont (onLeaf attrs name)
    | Function (attrs, param, ret) ->
        traverseTypeCPS onLeaf onNode param (fun paramResult ->
            traverseTypeCPS onLeaf onNode ret (fun retResult ->
                cont (onNode attrs paramResult retResult)
            )
        )
    // ... other cases
```

### Recommendation for Morphir

**Use sparingly**: Only for deep recursion where stack overflow is a concern. Prefer direct recursion for readability.

---

## 12. Trampolining

### Definition

Trampolining achieves stack-safe recursion by bouncing between a caller and recursive function.

**F# Implementation**:
```fsharp
type Trampoline<'A> =
    | Done of 'A
    | More of (unit -> Trampoline<'A>)

let rec run = function
    | Done value -> value
    | More thunk -> run (thunk ())

// Stack-safe sum
let rec sumTrampoline n acc =
    if n <= 0 then Done acc
    else More (fun () -> sumTrampoline (n - 1) (acc + n))

let sum n = run (sumTrampoline n 0)
// sum 100000 works without stack overflow
```

### Morphir Usage

**Use when**: Deep AST traversals where stack overflow is possible
**Avoid when**: Shallow recursion (direct recursion is clearer)

---

## 13. Lazy Evaluation

### Definition

Lazy evaluation defers computation until the value is needed.

**F# Implementation**:
```fsharp
// Lazy value
let expensiveComputation = lazy (
    printfn "Computing..."
    42
)

let result = expensiveComputation.Value  // Prints "Computing..."
let result2 = expensiveComputation.Value  // Cached, doesn't print

// Infinite sequence
let rec naturals n = seq {
    yield n
    yield! naturals (n + 1)
}

let firstTen = naturals 0 |> Seq.take 10 |> Seq.toList
```

**Morphir Application**:
```fsharp
// Lazy IR loading
let lazyPackage path = lazy (loadPackageFromDisk path)
let package = lazyPackage "Morphir.SDK"
let types = package.Value.Types  // Load happens here
```

### Benefits

- Performance: Avoid unnecessary computations
- Infinite Structures: Model infinite sequences
- Memoization: Cache expensive computations

---

## 14. Immutability Patterns

### Persistent Data Structures

**F# Implementation**:
```fsharp
// F# Map (persistent red-black tree)
let map1 = Map.empty |> Map.add "a" 1 |> Map.add "b" 2
let map2 = map1 |> Map.add "c" 3
// map1 and map2 share structure
```

**Structural Sharing**:
```fsharp
let list1 = [1; 2; 3]
let list2 = 0 :: list1
// list2 shares tail with list1
```

**C# Immutable Collections**:
```csharp
using System.Collections.Immutable;

var map1 = ImmutableDictionary<string, int>.Empty
    .Add("a", 1)
    .Add("b", 2);
var map2 = map1.Add("c", 3);
// Structural sharing
```

### Morphir Usage

All IR types in morphir-dotnet are immutable:
- F# Classic IR: Records with immutable fields
- C# Modern IR: Records with init-only properties
- Collections: F# Map, ImmutableDictionary

**Benefits**:
- Thread Safety
- Debugging: Values can't change unexpectedly
- Time Travel: Keep old versions for undo/redo

---

## 15. Parser Combinators

### Definition

Parser combinators are higher-order functions that combine simpler parsers into complex ones.

**F# Implementation**:
```fsharp
type Parser<'A> = Parser of (string -> Result<'A * string, string>)

module Parser =
    let run (Parser f) input = f input

    let returnParser value = Parser (fun input -> Ok (value, input))

    let bind (Parser p) f =
        Parser (fun input ->
            match p input with
            | Ok (value, remaining) ->
                let (Parser p2) = f value
                p2 remaining
            | Error err -> Error err
        )

    let (>>=) = bind

    let orElse (Parser p1) (Parser p2) =
        Parser (fun input ->
            match p1 input with
            | Ok result -> Ok result
            | Error _ -> p2 input
        )

    let (<|>) = orElse

    let many (Parser p) =
        Parser (fun input ->
            let rec loop acc input =
                match p input with
                | Ok (value, remaining) -> loop (value :: acc) remaining
                | Error _ -> Ok (List.rev acc, input)
            loop [] input
        )
```

### Morphir Usage (Future)

Parser combinators could be used for:
- Parsing Morphir IR from text format
- Building DSLs for IR transformations

**Current**: Use JSON deserialization (simpler, faster)

---

## 16. Dependency Injection (Reader Monad)

### Definition

The Reader monad threads read-only environment/configuration through computations.

**See [Monads > Reader Monad](#15-reader-monad) for implementation.**

### Morphir Application

**Configuration Threading**:
```fsharp
type MorphirConfig = {
    OutputPath: string
    OptimizationLevel: int
    TargetBackend: string
    DebugMode: bool
}

type CodeGen<'A> = ReaderT<MorphirConfig, Result<_, string>, 'A>

let generateModule (moduleDef: ModuleDefinition) : CodeGen<string> =
    getConfig >>= fun config ->
    // Use config throughout code generation
    (* ... *)
```

### Benefits

- Explicit Dependencies
- Testability: Easy to provide mock configurations
- Composability: Chain operations with consistent config access

---

## 17. Event Sourcing Patterns

### Definition

Event sourcing persists state changes as an append-only log of events.

**F# Implementation**:
```fsharp
type Event =
    | TypeAdded of Name * TypeDefinition
    | TypeRemoved of Name
    | ValueAdded of Name * ValueDefinition
    | ValueRemoved of Name

let applyEvent state event =
    match event with
    | TypeAdded (name, typeDef) ->
        { state with Types = Map.add name typeDef state.Types }
    | TypeRemoved name ->
        { state with Types = Map.remove name state.Types }
    | ValueAdded (name, valueDef) ->
        { state with Values = Map.add name valueDef state.Values }
    | ValueRemoved name ->
        { state with Values = Map.remove name state.Values }

let replay events =
    let initialState = { Types = Map.empty; Values = Map.empty }
    List.fold applyEvent initialState events
```

### Morphir Application

**IR Change Tracking**:
```fsharp
type IREvent =
    | DistributionCreated of Distribution
    | PackageAdded of Path * Package
    | ModuleAdded of Path * ModulePath * ModuleDefinition
    | TypeModified of Path * ModulePath * Name * TypeDefinition

let trackIRChanges (events: IREvent list) : Distribution =
    List.fold applyIREvent emptyDistribution events
```

### Benefits

- Auditability: Complete history
- Debugging: Replay to understand failures
- Temporal Queries: "What was the state at time T?"

---

## 18. Bridging Patterns (FP ↔ OO)

### 18.1 F# to C# Interop

#### Option to Nullable

**F# Side**:
```fsharp
let toNullable (opt: 'T option) : 'T | null =
    match opt with
    | Some value -> value
    | None -> null

let fromNullable (value: 'T | null) : 'T option =
    if isNull value then None else Some value
```

**C# Side**:
```csharp
var fsharpOption = FSharpModule.GetValue();
var csharpValue = fsharpOption.IsSome ? fsharpOption.Value : null;
```

#### Result to Exception

**F# Side**:
```fsharp
let toException (result: Result<'T, string>) : 'T =
    match result with
    | Ok value -> value
    | Error msg -> failwith msg
```

**C# Side**:
```csharp
try
{
    var result = FSharpModule.DivideException(10, 2);
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

### 18.2 Exposing F# Functions to C#

#### Curried Functions

**F# (tupled for C#)**:
```fsharp
// Expose tupled version
let addTupled (x, y) = x + y

// F# can still curry
let add x y = addTupled (x, y)
```

**C# Usage**:
```csharp
var result = FSharpModule.AddTupled(5, 3);
```

### 18.3 Morphir Classic IR to Modern IR

**Conversion Functions**:
```fsharp
// F# Classic to C# Modern
let rec classicToCSharp (typ: ClassicType<unit>) : CSharpType =
    match typ with
    | Variable (_, name) -> CSharpType.Variable(name) :> CSharpType
    | Reference (_, fqName, typeParams) ->
        let csParams = List.map classicToCSharp typeParams |> Seq.ofList
        CSharpType.Reference(fqName, csParams) :> CSharpType
    // ... other cases

// C# Modern to F# Classic
let rec csharpToClassic (typ: CSharpType) : ClassicType<unit> =
    match typ with
    | :? CSharpType.Variable as v -> Variable ((), v.Name)
    | :? CSharpType.Reference as r ->
        let fsharpParams = Seq.map csharpToClassic r.TypeParameters |> List.ofSeq
        Reference ((), r.TypeName, fsharpParams)
    // ... other cases
```

### Benefits

- Best of Both Worlds: F# for complex logic, C# for tooling
- Incremental Adoption: Gradually migrate between languages
- Team Flexibility: Different team members can work in preferred language

### Trade-offs

- Conversion Overhead: Marshalling between types
- Impedance Mismatch: Some patterns don't translate cleanly
- Maintenance: Keep both representations in sync

---

## Summary

This knowledge base documents **18 core functional programming patterns** for morphir-dotnet:

### Core Abstractions (1-3)
1. **Monads** - Option, Result, List, State, Reader, IO/Async
2. **Functors** - Map operations preserving structure
3. **Applicatives** - Independent operations, validation with error accumulation

### Lenses and Error Handling (4-5)
4. **Lenses & Optics** - Composable getters/setters for immutable data
5. **Railway-Oriented Programming** - Result-based error handling pipelines

### Advanced Patterns (6-8)
6. **Algebraic Effects & Free Monads** - DSL construction with multiple interpreters
7. **Fold Patterns (Catamorphisms)** - Bottom-up structure consumption
8. **Recursion Schemes** - Generalized recursive data processing

### Type-Level Programming (9-10)
9. **Phantom Types** - Compile-time constraints (units of measure, IR versioning)
10. **Higher-Kinded Types** - Abstraction over type constructors (limited in F#/C#)

### Control Flow (11-12)
11. **Continuation-Passing Style** - Explicit control flow, stack safety
12. **Trampolining** - Stack-safe recursion for deep ASTs

### Performance and State (13-14)
13. **Lazy Evaluation** - Deferred computation, memoization
14. **Immutability Patterns** - Persistent data structures, structural sharing

### Parsing and Dependency Injection (15-16)
15. **Parser Combinators** - Monadic parsing (future DSLs)
16. **Dependency Injection (Reader)** - Configuration threading

### System Patterns (17-18)
17. **Event Sourcing** - Append-only event logs, IR evolution tracking
18. **Bridging Patterns (FP ↔ OO)** - F#/C# interop, Classic IR to Modern IR

### Current Usage in morphir-dotnet

**Heavily Used**:
- ADTs (discriminated unions in F#, sealed records in C#)
- Immutable data structures (F# Map, ImmutableDictionary)
- Option types (F# option, C# nullable reference types)
- Railway-Oriented Programming (Result types in IR validation)
- Computation Expressions (Morphir.Live with Fun.Blazor)

**Partially Used**:
- Lenses (LensGenerator placeholder)
- Lazy Evaluation (F# seq, lazy values)
- Trampolining (potential for deep AST traversals)

**Recommended for Future**:
- Free Monads (IR construction DSL)
- Parser Combinators (text-based DSLs)
- Event Sourcing (IR evolution tracking)
- Algebraic Effects (transformation interpreters)

---

**Related Documents**:
- [Language Design Patterns](./language-design-patterns.md)
- [Computation Expressions for AST Modeling](./computation-expressions-for-ast.md)
- [Visitor Pattern Implementations](./visitor-pattern-implementations.md)
- [Compiler Services and Metaprogramming](./compiler-services-metaprogramming.md)
- [F# Coding Guide](../../docs/contributing/fsharp-coding-guide.md)
- [AGENTS.md](../../AGENTS.md)

**External Resources**:
- [F# for Fun and Profit - Railway Oriented Programming](https://fsharpforfunandprofit.com/rop/)
- [F# for Fun and Profit - Understanding Parser Combinators](https://fsharpforfunandprofit.com/posts/understanding-parser-combinators/)
- [Functional Programming in F# (Scott Wlaschin)](https://fsharpforfunandprofit.com/)
- [Category Theory for Programmers (Bartosz Milewski)](https://bartoszmilewski.com/2014/10/28/category-theory-for-programmers-the-preface/)
