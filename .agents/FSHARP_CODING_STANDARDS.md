# F# Coding Standards

Enforceable F# standards for `*.fs`, `*.fsi`, `*.fsx`, and `*.fsproj` in `finos/morphir-dotnet`. Read [CODING_STANDARDS.md](../CODING_STANDARDS.md) first.

- **Crossing the C# boundary?** Also follow [.agents/skills/fsharp-csharp-interop/SKILL.md](skills/fsharp-csharp-interop/SKILL.md) and run `fsharp-csharp-interop-reviewer`.
- **Deep-dive reference:** [docs/contributing/fsharp-coding-guide.md](../docs/contributing/fsharp-coding-guide.md) (extended CE, active pattern, and script examples).

---

## Rules

### FS-1: Smart constructors for constrained values
Wrap constrained domain values (names, IDs, versions, paths) in a single-case DU with a `private` constructor and expose a `tryCreate`/`Parse` function.

```fsharp
// Follow
type NameSegment = private NameSegment of string

module NameSegment =
    let tryCreate (value: string) =
        if String.IsNullOrWhiteSpace value then Error "NameSegment cannot be empty"
        else Ok (NameSegment (value.Trim()))

    let value (NameSegment v) = v

// Avoid: unvalidated public constructor or raw string passed everywhere
type NameSegment = NameSegment of string
let processSegment (segment: string) = ...
```

### FS-2: Discriminated unions for state and `[<RequireQualifiedAccess>]`
Model mutually exclusive states as DUs instead of boolean flags with optional fields. Mark public DUs and modules `[<RequireQualifiedAccess>]` to prevent case shadowing.

```fsharp
// Follow
[<RequireQualifiedAccess>]
type VerificationOutcome =
    | Valid of filesChecked: int
    | Invalid of errors: ValidationError list

// Avoid: boolean flags allowing impossible combinations
type VerificationOutcome = {
    IsValid: bool
    FilesChecked: int option
    Errors: ValidationError list option
}
```

### FS-3: Exhaustive matching and active patterns
Keep `match` expressions exhaustive on domain DUs so adding a case produces a compiler warning/error everywhere it must be handled. Extract complex conditional checks into active patterns.

```fsharp
// Follow
let (|NonEmpty|_|) (s: string) =
    if String.IsNullOrWhiteSpace s then None else Some (s.Trim())

let describe outcome =
    match outcome with
    | VerificationOutcome.Valid count -> $"Valid ({count} files)"
    | VerificationOutcome.Invalid errs -> $"Invalid ({errs.Length} errors)"

// Avoid: wildcard swallowing future cases, or nested if/elif chains
let describe outcome =
    match outcome with
    | VerificationOutcome.Valid count -> $"Valid ({count} files)"
    | _ -> "Failed" // Hides newly added union cases!
```

### FS-4: Railway-oriented error handling with `Result`
Return `Result<'Ok, 'Error>` with a typed error DU for expected failures and compose with `Result.bind` / `Result.map`. Reserve exceptions for unrecoverable bugs.

```fsharp
// Follow
[<RequireQualifiedAccess>]
type ConfigError =
    | MissingField of field: string
    | InvalidVersion of raw: string

let loadConfig input =
    input
    |> parseVersion
    |> Result.bind validateFields

// Avoid: throwing for expected validation failures
let loadConfig input =
    if String.IsNullOrEmpty input.Version then
        failwith "Missing version"
```

### FS-5: Immutability and `option` over `null`
Use immutable records, lists, maps, and sets by default. Represent absence internally with `option` (or `voption` in hot paths), never `null` or `Unchecked.defaultof`.

```fsharp
// Follow
let updated = { person with Age = person.Age + 1 }
let greeting name = name |> Option.defaultValue "anonymous"

// Avoid: mutable state or null checks inside F# domain code
let mutable age = 0
let greeting (name: string) =
    if isNull name then "anonymous" else name
```

*Note: At a C# boundary, convert `T | null` to `option` at entry (`Option.ofObj`) and back at exit (`Option.toObj`) per `fsharp-csharp-interop`.*

### FS-6: Async, `task`, and cancellation
Use `task { ... }` with explicit `CancellationToken` at .NET/C# boundaries (and `Async` only when staying inside F#). Never block synchronously with `.Result`, `.Wait()`, or `Async.RunSynchronously` inside async workflows.

```fsharp
// Follow
let readModelAsync (path: string) (ct: CancellationToken) : Task<string> =
    task {
        return! File.ReadAllTextAsync(path, ct)
    }

// Avoid: blocking async calls or dropping cancellation
let readModel path =
    File.ReadAllTextAsync(path).Result
```

### FS-7: Computation expressions for DSLs
Use computation expressions for IR/AST construction DSLs:
- `CustomOperation` for flat, query-like configuration builders (`fqName { package ...; module' ...; local ... }`).
- `Yield`/`Delay`/`Run` (or hybrid) for nested compositional structures (`type' { record [ ... ] }`).
- Mark hot-path CE combinators with `[<InlineIfLambda>]` to avoid closure allocations.

```fsharp
// Follow
let id = fqName {
    package [ "morphir"; "sdk" ]
    module' [ "basics" ]
    local "int"
}

// Avoid: deeply nested raw constructor calls when a DSL builder exists
let id = FQName(Path([ Name([ "morphir" ]); Name([ "sdk" ]) ]), ...)
```

### FS-8: AOT-safe formatting and serialization
Never use `%A`, `sprintf "%A"`, `printfn "%A"`, or default reflection-based `System.Text.Json` serialization in shipped code. Override `ToString()` explicitly on types formatted as text, and use explicit/Myriad/source-generated JSON codecs.

```fsharp
// Follow
type ModuleName =
    private ModuleName of string
    override this.ToString() =
        let (ModuleName value) = this
        value

let formatError (err: ConfigError) =
    match err with
    | ConfigError.MissingField field -> $"Missing field: {field}"
    | ConfigError.InvalidVersion raw -> $"Invalid version: {raw}"

// Avoid: reflection-based %A formatting in shipped code
let formatError (err: ConfigError) = sprintf "%A" err
```

### FS-9: Formatting, project settings, and `.fsx` scripts
- Follow `.editorconfig` / Fantomas (`max_line_length = 100`, Stroustrup style, 4-space indentation, LF line endings).
- Use `Directory.Build.props` TFM properties (`$(MorphirLibraryTargetFrameworks)`, etc.) — never literal TFMs.
- In `.fsx` CLI scripts: write diagnostics to `stderr` (`eprintfn`), data only to `stdout` (`printfn`), and return explicit exit codes.

```fsharp
// Follow (in .fsx scripts)
eprintfn "Validating %s..." path
printfn "%s" jsonOutput

// Avoid: mixing diagnostic logs into stdout
printfn "Validating %s..." path
printfn "%s" jsonOutput
```

### FS-10: F#/C# boundary delegation
Whenever a public F# type, member, or module is called by a C# project, follow all rules in [.agents/skills/fsharp-csharp-interop/SKILL.md](skills/fsharp-csharp-interop/SKILL.md) (`<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, trim/AOT properties, `TryParse` with `[<MaybeNullWhen>]`, `Match` members, no F# `list`/`Map`/`Set`/`option`/curried functions on the C# surface) and run `fsharp-csharp-interop-reviewer`.

---

## F# Review Checklist

Report findings as `path:line: rule FS-N: problem. fix.`

- [ ] `FS-1`: Constrained domain values use private-case DUs with smart constructors.
- [ ] `FS-2`: Mutually exclusive states use DUs with `[<RequireQualifiedAccess>]`, not boolean flag bags.
- [ ] `FS-3`: Pattern matches on domain DUs are exhaustive (no lazy `_ ->` hiding cases); complex checks use active patterns.
- [ ] `FS-4`: Expected failures return `Result<'Ok, 'Error>` with typed error DUs, not exceptions.
- [ ] `FS-5`: Code is immutable and uses `option` internally instead of `null`/`mutable`/`Unchecked.defaultof`.
- [ ] `FS-6`: .NET async boundaries use `task { ... }` + `CancellationToken`; no `.Result`/`.Wait()`.
- [ ] `FS-7`: DSL/IR builders use appropriate CE patterns (`CustomOperation` vs compositional) cleanly.
- [ ] `FS-8`: No `%A` formatting or reflection JSON in shipped code; `ToString()` overridden where formatted.
- [ ] `FS-9`: Matches Fantomas/`.editorconfig` rules, uses `Directory.Build.props` TFM properties, and scripts keep stdout/stderr separate.
- [ ] `FS-10`: Any C#-facing surface complies with `fsharp-csharp-interop` and was checked by `fsharp-csharp-interop-reviewer`.
