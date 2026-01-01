# F# Frontend Usage Scenarios

This document describes all supported usage scenarios for the F# Frontend (F# → Morphir IR parser).

## Overview

The F# Frontend provides three distinct usage modes:
1. **In-Memory API** - For try-morphir, REPL, testing scenarios
2. **CLI - Multiple Files** - For parsing multiple F# files without .fsproj
3. **CLI - Project Files** - For parsing complete .fsproj projects

---

## Scenario 1: In-Memory Parsing (try-morphir Integration)

**Use Case**: Online playground, REPL, testing, dynamic code generation

**API**: `FrontendAPI.parseInMemory`

### Example: try-morphir Integration

```fsharp
// try-morphir web service endpoint
module TryMorphir =
    open Morphir.Frontends.FSharp
    open System.Text.Json

    [<HttpPost("/api/fsharp/parse")>]
    let parseHandler (request: ParseRequest) : Task<IResult> =
        task {
            // User submits F# source code via web form
            let source = request.SourceCode

            // Parse in-memory (no file I/O)
            match FrontendAPI.parseInMemory "UserCode.fs" source with
            | Ok distribution ->
                // Serialize to JSON
                let json = Distribution.toJson distribution

                // Return to client
                return Results.Ok({| Success = true; IR = json |})

            | Error errors ->
                // Return compilation errors
                return Results.BadRequest({| Success = false; Errors = errors |})
        }
```

### Example: Unit Testing

```fsharp
[<Fact>]
let ``Parse simple record type`` () =
    let source = """
namespace Test

type Customer = {
    Id: int
    Name: string
}
"""

    match FrontendAPI.parseInMemory "Test.fs" source with
    | Ok distribution ->
        // Assert IR structure
        let modules = distribution |> Distribution.getModules
        modules |> Map.containsKey (Path.fromString "Test") =! true

    | Error errors ->
        failwithf "Parse failed: %A" errors
```

### Example: F# Interactive (FSI/REPL)

```fsharp
// Load F# Frontend in FSI
#r "nuget: Morphir.Frontends.FSharp"

open Morphir.Frontends.FSharp

// Define F# code as string
let source = """
namespace Domain

type Order = {
    OrderId: int
    Total: decimal
}

module Orders =
    let calculateTax (order: Order) = order.Total * 0.08m
"""

// Parse to IR
let result = FrontendAPI.parseInMemory "Orders.fs" source

// Inspect IR
match result with
| Ok distribution ->
    printfn "Successfully parsed!"
    let json = Distribution.toJsonPretty distribution
    printfn "%s" json
| Error errors ->
    errors |> List.iter (printfn "Error: %s")
```

### API Signature

```fsharp
module FrontendAPI =
    /// Parse single in-memory F# source to Morphir IR
    val parseInMemory : fileName:string -> source:string -> Result<Distribution, string list>
```

**Key Features**:
- ✅ No file I/O (all in-memory)
- ✅ Fast (no disk access)
- ✅ Sandboxed (safe for untrusted code)
- ✅ Simple API (2 parameters)
- ✅ Returns errors as list of strings

---

## Scenario 2: Multiple Files Without Project File

**Use Case**: Parse a set of F# files without creating/maintaining .fsproj

**API**: `FrontendAPI.parseFiles` or CLI

### Example: CLI - Multiple Files

```bash
# Parse specific files (explicit list)
$ morphir fsharp parse Types.fs Domain.fs Calculator.fs --output morphir-ir.json
✓ Parsing Types.fs
✓ Parsing Domain.fs
✓ Parsing Calculator.fs (depends on Types, Domain)
✓ Resolved 12 cross-file references
✓ Generated morphir-ir.json

# Parse all .fs files in directory
$ morphir fsharp parse src/ --output morphir-ir.json
✓ Found 15 files in src/
✓ Parsing Types.fs
✓ Parsing Domain.fs
✓ ... (13 more files)
✓ Generated morphir-ir.json

# Parse files matching glob pattern
$ morphir fsharp parse "src/**/*.fs" --output morphir-ir.json
```

### Example: Programmatic API

```fsharp
// Batch processing: Parse multiple F# files
let files = [
    "src/Types.fs"
    "src/Domain.fs"
    "src/Calculator.fs"
    "src/Validation.fs"
]

match FrontendAPI.parseFiles files with
| Ok distribution ->
    // Save to morphir-ir.json
    let json = Distribution.toJsonPretty distribution
    System.IO.File.WriteAllText("output/morphir-ir.json", json)
    printfn "✓ Generated morphir-ir.json"

| Error errors ->
    eprintfn "Errors:"
    errors |> List.iter (eprintfn "  - %s")
    exit 1
```

### Example: Script-Based Build

```fsharp
// build.fsx - F# build script
#r "nuget: Morphir.Frontends.FSharp"

open Morphir.Frontends.FSharp
open System.IO

// Find all F# source files
let sourceFiles =
    Directory.GetFiles("src", "*.fs", SearchOption.AllDirectories)
    |> Array.toList

printfn "Found %d F# files" sourceFiles.Length

// Parse to IR
match FrontendAPI.parseFiles sourceFiles with
| Ok distribution ->
    // Generate morphir-ir.json
    let json = Distribution.toJsonPretty distribution
    File.WriteAllText("dist/morphir-ir.json", json)

    // Also generate F# code from IR (round-trip)
    let generatedFsharp = FSharpBackend.generate distribution
    File.WriteAllText("dist/Generated.fs", generatedFsharp)

    printfn "✓ Build complete"

| Error errors ->
    eprintfn "Build failed:"
    errors |> List.iter (eprintfn "  %s")
    exit 1
```

### Important: File Order Matters

F# requires source files in **dependency order**. The parser automatically resolves dependencies, but you can provide hints:

```bash
# Manual order (dependencies first)
$ morphir fsharp parse \
    Types.fs \          # No dependencies
    Domain.fs \         # Depends on Types
    Calculator.fs \     # Depends on Types, Domain
    --output morphir-ir.json

# Auto-detect order (parser analyzes `open` statements)
$ morphir fsharp parse src/*.fs --output morphir-ir.json
```

The parser uses FSharp.Compiler.Service to:
1. Analyze `open` statements
2. Build dependency graph
3. Re-order files if needed
4. Report circular dependencies as errors

### API Signature

```fsharp
module FrontendAPI =
    /// Parse multiple F# files from disk (auto-resolves dependencies)
    val parseFiles : filePaths:string list -> Result<Distribution, string list>
```

**Key Features**:
- ✅ No .fsproj required
- ✅ Auto-detects dependencies (`open` statements)
- ✅ Re-orders files if needed
- ✅ Validates cross-file type references
- ✅ Reports circular dependencies

---

## Scenario 3: Project Files (.fsproj)

**Use Case**: Parse complete F# projects with all dependencies and settings

**API**: `FrontendAPI.parseProject` or CLI

### Example: CLI - Parse Project

```bash
# Parse entire project
$ morphir fsharp parse MyProject/MyProject.fsproj --output morphir-ir.json
✓ Parsing MyProject.fsproj
✓ Found 25 source files
✓ Resolving dependencies...
✓ Parsing Types.fs
✓ Parsing Domain.fs
✓ ... (23 more files)
✓ Resolved 87 cross-file references
✓ Generated morphir-ir.json (2.5MB)

# Parse project in watch mode (re-parse on change)
$ morphir fsharp parse MyProject/MyProject.fsproj --watch --output morphir-ir.json
👁️ Watching MyProject/ for changes...
✓ Parsed 25 files → morphir-ir.json
[file changed: src/Calculator.fs]
✓ Re-parsed → morphir-ir.json
```

### Example: Programmatic API

```fsharp
// Parse .fsproj project
match FrontendAPI.parseProject "MyProject/MyProject.fsproj" with
| Ok distribution ->
    // Generate morphir-ir.json
    let json = Distribution.toJsonPretty distribution
    System.IO.File.WriteAllText("morphir-ir.json", json)

    printfn "✓ Generated morphir-ir.json"
    printfn "  Modules: %d" (distribution |> Distribution.getModules |> Map.count)
    printfn "  Types: %d" (distribution |> Distribution.getAllTypes |> Seq.length)
    printfn "  Values: %d" (distribution |> Distribution.getAllValues |> Seq.length)

| Error errors ->
    errors |> List.iter (eprintfn "Error: %s")
    exit 1
```

### Example: CI/CD Integration

```yaml
# .github/workflows/morphir.yml
name: Generate Morphir IR

on: [push]

jobs:
  generate-ir:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'

      - name: Install Morphir Frontend
        run: dotnet tool install -g Morphir.Frontends.FSharp.Cli

      - name: Generate Morphir IR
        run: morphir fsharp parse src/MyProject.fsproj --output morphir-ir.json

      - name: Validate IR
        run: morphir verify morphir-ir.json

      - name: Upload IR artifact
        uses: actions/upload-artifact@v3
        with:
          name: morphir-ir
          path: morphir-ir.json
```

### .fsproj Features Supported

The parser extracts:
- ✅ **Source files** (`<Compile Include="..." />`) in correct order
- ✅ **NuGet package references** (Morphir.SDK, FSharp.Core, etc.)
- ✅ **Project references** (other .fsproj files)
- ✅ **Compile-time constants** (`<DefineConstants>`)
- ✅ **Target framework** (.NET version)

**Example .fsproj**:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Types.fs" />
    <Compile Include="Domain.fs" />
    <Compile Include="Calculator.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Morphir.SDK" Version="0.3.0" />
  </ItemGroup>
</Project>
```

### API Signature

```fsharp
module FrontendAPI =
    /// Parse F# project file (.fsproj)
    val parseProject : projectPath:string -> Result<Distribution, string list>
```

**Key Features**:
- ✅ Respects .fsproj compile order (critical for F#)
- ✅ Resolves NuGet package references
- ✅ Supports multi-project solutions
- ✅ Validates project structure
- ✅ Handles conditional compilation

---

## Comparison Matrix

| Feature | In-Memory | Multiple Files | Project File |
|---------|-----------|---------------|--------------|
| **API** | `parseInMemory` | `parseFiles` | `parseProject` |
| **CLI** | ❌ Not exposed | ✅ `morphir fsharp parse file1.fs file2.fs` | ✅ `morphir fsharp parse proj.fsproj` |
| **File I/O** | ❌ None | ✅ Read from disk | ✅ Read from disk |
| **Dependency Resolution** | ❌ Single file only | ✅ Auto-detects via `open` | ✅ Uses .fsproj order |
| **Cross-file References** | ❌ Not supported | ✅ Supported | ✅ Supported |
| **NuGet Dependencies** | ❌ Not checked | ❌ Not checked | ✅ Parsed from .fsproj |
| **Compile Order** | N/A | ✅ Auto-detected | ✅ From .fsproj |
| **Use Case** | try-morphir, REPL, testing | Ad-hoc parsing, scripts | Production builds, CI/CD |
| **Performance** | ⚡ Fastest | ⚡ Fast | 🐢 Slower (more setup) |

---

## Advanced Scenarios

### Scenario 4: Hybrid (In-Memory + File References)

**Use Case**: try-morphir with imports from standard library

```fsharp
// User's code (in-memory)
let userCode = """
namespace MyApp

open Morphir.SDK.Maybe  // Import from Morphir.SDK (on disk)

type User = {
    Name: string
    Email: string option  // Uses Maybe
}
"""

// Parse with hybrid sources
let options = {
    Sources = InMemory ("UserCode.fs", userCode)
    TargetNamespace = Some "MyApp"
    IncludeDependencies = true  // Resolve Morphir.SDK from NuGet
}

match FrontendAPI.parseToIR options with
| Ok distribution ->
    // IR includes both user code and Morphir.SDK references
    ...
```

### Scenario 5: Incremental Parsing (Future)

**Use Case**: IDE integration, watch mode optimization

```fsharp
// Future API (not in MVP)
type IncrementalParser = {
    // Cache parsed results
    Cache: Map<string, ParseResult>

    // Re-parse only changed files
    ParseIncremental: changedFile:string -> Result<Distribution, string list>
}
```

### Scenario 6: Source Generators (Future)

**Use Case**: Generate F# from Morphir IR at compile time

```fsharp
// Future: F# Source Generator using frontend for validation
[<Generator>]
type MorphirSourceGenerator() =
    interface ISourceGenerator with
        member this.Execute(context) =
            // 1. Find .fsharp-ir files
            // 2. Parse IR
            // 3. Generate F# using backend
            // 4. Validate round-trip using frontend
            ...
```

---

## Error Handling

All APIs return `Result<Distribution, string list>`:

```fsharp
type ParseError =
    | SyntaxError of fileName:string * line:int * column:int * message:string
    | TypeError of fileName:string * line:int * column:int * message:string
    | UnsupportedFeature of fileName:string * feature:string * suggestion:string
    | CircularDependency of files:string list
    | MissingFile of fileName:string

// Friendly error messages
match result with
| Error errors ->
    errors |> List.iter (fun err ->
        match err with
        | SyntaxError (file, line, col, msg) ->
            eprintfn "%s:%d:%d: Syntax error: %s" file line col msg
        | UnsupportedFeature (file, feature, suggestion) ->
            eprintfn "%s: Unsupported feature '%s'. %s" file feature suggestion
        | CircularDependency files ->
            eprintfn "Circular dependency detected: %s" (String.concat " → " files)
        | _ -> ())
```

---

## Performance Considerations

| Scenario | Files | Time (Approx) | Notes |
|----------|-------|---------------|-------|
| In-Memory (single file) | 1 | < 100ms | Fastest, no I/O |
| Multiple Files (10 files) | 10 | < 500ms | I/O + FCS overhead |
| Project File (100 files) | 100 | < 5s | Full type checking |
| Large Project (1000 files) | 1000 | < 30s | Parallel parsing possible |

**Optimization Tips**:
- Use in-memory API for try-morphir (no I/O)
- Use multiple files for small batches (< 20 files)
- Use project file for production (handles complexity)
- Enable caching for watch mode (future)
- Use parallel parsing for large projects (future)

---

## References

- [PRD: F# Frontend](./PRD-fsharp-frontend.md) - Complete requirements
- [F# Frontend Maturity Milestones](./fsharp-frontend-maturity-milestones.md) - Implementation roadmap
- [GitHub Issues: F# Frontend](./github-issues-fsharp-frontend.md) - Task breakdown

---

**Document Status**: Draft
**Last Updated**: 2025-12-31
**Next Review**: After M0 implementation
