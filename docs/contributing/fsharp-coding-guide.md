# F# Coding Guide for morphir-dotnet

This guide provides F#-specific coding standards and best practices for the morphir-dotnet project, including F# Interactive scripts in `.claude/skills/`.

## Table of Contents

1. [Pattern Matching and Value Extraction](#pattern-matching-and-value-extraction)
2. [Active Patterns](#active-patterns)
3. [Error Handling](#error-handling)
4. [Immutability and Data Structures](#immutability-and-data-structures)
5. [Async and Task Workflows](#async-and-task-workflows)
6. [Type Design](#type-design)
7. [CLI Scripts (.fsx)](#cli-scripts-fsx)
8. [Testing](#testing)

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
9. ✅ **Prefer Option<'T>** over null
10. ✅ **Follow railway-oriented programming** for error handling

---

## References

- [F# Style Guide](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/)
- [F# Design Guidelines](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/conventions)
- [Domain Modeling Made Functional](https://fsharpforfunandprofit.com/books/)
- [Railway Oriented Programming](https://fsharpforfunandprofit.com/rop/)
- [AGENTS.md](../../AGENTS.md) - Project-wide agent guidance
