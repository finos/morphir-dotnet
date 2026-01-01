# Unified File Architecture for morphir-dotnet

This document defines a **unified file abstraction** that works across all morphir-dotnet components: Pipeline, F# Frontend, F# Backend, and any future frontends/backends.

## Current State Analysis

### ✅ What We Already Have: VFile (Pipeline Project)

The `Morphir.IR.Pipeline` project **already has** a VFile abstraction (inspired by unifiedJS):

```fsharp
// src/Morphir.IR.Pipeline/File.fs
type VFile = {
    Content: obj option                          // IR tree content
    Path: string option                          // File path
    History: string list                         // Transformation history
    Messages: VMessage list                      // Diagnostics
    Data: ImmutableDictionary<string, obj>      // Metadata
}

type VMessage = {
    Severity: MessageSeverity                   // Info | Warning | Error | Fatal
    Message: string                             // Human-readable message
    Position: SourceRange option                // Source location
    Source: string option                       // Plugin/phase name
    RuleId: string option                      // Rule ID (e.g., "IR-001")
}

type SourceRange = {
    Start: SourcePosition
    End: SourcePosition
}

type SourcePosition = {
    Line: int
    Column: int
    Offset: int option
}
```

**This is excellent!** It already implements:
- ✅ VFile concepts (file, content, history, messages, data)
- ✅ Rich diagnostics with source locations
- ✅ Metadata dictionary
- ✅ Transformation history

### ❌ What's Missing: Multi-File Support (DocumentTree)

The Pipeline currently handles **single files** flowing through transformations. It doesn't have:
- ❌ Hierarchical structure (tree of files/directories)
- ❌ Multi-file parsing (`.fsproj` projects)
- ❌ Cross-file relationships (imports, dependencies)
- ❌ Lazy loading

---

## Proposed: Unified Architecture

### Layer 1: VFile (Already Exists) ✅

**Location**: `Morphir.IR.Pipeline/File.fs`

**Purpose**: Single file abstraction with diagnostics and metadata

**No Changes Needed** - Already perfect for:
- Carrying IR through pipeline transformations
- Accumulating diagnostics from plugins
- Tracking transformation history
- Storing metadata

---

### Layer 2: VFileTree (NEW) 🆕

**Location**: `Morphir.IR.Pipeline/FileTree.fs` (new file in existing project)

**Project**: `Morphir.IR.Pipeline` (existing project - just add new file)

**Purpose**: Hierarchical multi-file structure for projects

**Why in Morphir.IR.Pipeline?**
- ✅ `VFile` already lives there
- ✅ Pipeline is the core transformation engine
- ✅ All frontends/backends will depend on Pipeline anyway
- ✅ No new project needed
- ✅ Keeps file abstractions together

```fsharp
namespace Morphir.IR.Pipeline

/// <summary>
/// Represents a tree of Morphir files (hierarchical structure).
/// Supports multi-file projects, directories, and lazy loading.
/// </summary>
type VFileTree = {
    /// <summary>Root path (directory or project file)</summary>
    Path: string

    /// <summary>Tree content (files and subtrees)</summary>
    Content: TreeContent list

    /// <summary>Tree-level metadata</summary>
    Metadata: ImmutableDictionary<string, obj>

    /// <summary>Configuration (for .fsproj or project-level settings)</summary>
    Config: TreeConfig
}

and TreeContent =
    /// <summary>Single file in the tree</summary>
    | File of file: VFile
    /// <summary>Subdirectory (nested tree)</summary>
    | Directory of tree: VFileTree

and TreeConfig = {
    /// <summary>Target framework (e.g., "net10.0")</summary>
    TargetFramework: string option

    /// <summary>Package references (name -> version)</summary>
    PackageReferences: ImmutableDictionary<string, string>

    /// <summary>Compile-time constants</summary>
    DefineConstants: string list

    /// <summary>Language version (e.g., "F# 9.0")</summary>
    LanguageVersion: string option

    /// <summary>Additional custom config</summary>
    CustomConfig: ImmutableDictionary<string, obj>
}

module VFileTree =
    /// <summary>Create empty file tree</summary>
    let empty: VFileTree = {
        Path = "."
        Content = []
        Metadata = ImmutableDictionary.Empty
        Config = {
            TargetFramework = None
            PackageReferences = ImmutableDictionary.Empty
            DefineConstants = []
            LanguageVersion = None
            CustomConfig = ImmutableDictionary.Empty
        }
    }

    /// <summary>Create tree from single file</summary>
    let fromFile (file: VFile): VFileTree =
        { empty with
            Path = file.Path |> Option.defaultValue "."
            Content = [File file]
        }

    /// <summary>Create tree from multiple files (flat)</summary>
    let fromFiles (files: VFile list): VFileTree =
        { empty with Content = files |> List.map File }

    /// <summary>Create tree from directory (recursive)</summary>
    let fromDirectory (path: string): VFileTree =
        // Scan directory, create nested tree structure
        // Group files by subdirectory
        // Recursively build tree
        ...

    /// <summary>Get all files (flattened)</summary>
    let rec allFiles (tree: VFileTree): VFile list =
        tree.Content
        |> List.collect (function
            | File f -> [f]
            | Directory dir -> allFiles dir)

    /// <summary>Find file by path</summary>
    let rec findFile (path: string) (tree: VFileTree): VFile option =
        tree.Content
        |> List.tryPick (function
            | File f when f.Path = Some path -> Some f
            | Directory dir -> findFile path dir
            | _ -> None)

    /// <summary>Get all errors from all files</summary>
    let allErrors (tree: VFileTree): (string * VMessage list) list =
        tree
        |> allFiles
        |> List.map (fun f ->
            let path = f.Path |> Option.defaultValue "unknown"
            let errors = VFile.errors f
            (path, errors))
        |> List.filter (fun (_, errors) -> not (List.isEmpty errors))

    /// <summary>Get tree statistics</summary>
    let stats (tree: VFileTree): TreeStats =
        let files = allFiles tree
        {
            TotalFiles = files.Length
            TotalErrors = tree |> allErrors |> List.sumBy (fun (_, e) -> e.Length)
            TotalWarnings = files |> List.sumBy (fun f -> VFile.warnings f |> List.length)
        }

and TreeStats = {
    TotalFiles: int
    TotalErrors: int
    TotalWarnings: int
}
```

---

### Layer 3: Frontend/Backend Integration

Each frontend/backend uses `VFile` and `VFileTree`:

#### F# Frontend Integration

```fsharp
namespace Morphir.Frontends.FSharp

open Morphir.IR.Pipeline

module FrontendAPI =
    /// Parse single in-memory source → VFile
    let parseInMemory (fileName: string) (source: string): Result<VFile, VFile> =
        // 1. Create empty VFile
        let vfile = VFile.empty
                    |> VFile.setData "fileName" fileName
                    |> VFile.setData "source" source

        // 2. Parse using FCS
        match Parser.parseSource (InMemory (fileName, source)) with
        | Ok parseResult ->
            // 3. Map to Morphir IR
            let distribution = (* generate IR *)

            // 4. Return VFile with IR content
            Ok { vfile with
                   Content = Some (box distribution)
                   History = vfile.History @ ["Parsed F# to IR"]
               }

        | Error err ->
            // 5. Return VFile with error message
            let vfileWithError =
                vfile
                |> VFile.error err None
                |> fun f -> { f with History = f.History @ ["Parse failed"] }

            Error vfileWithError

    /// Parse multiple files → VFileTree
    let parseFiles (filePaths: string list): Result<VFileTree, VFileTree> =
        // 1. Create VFile for each file path
        let vfiles =
            filePaths
            |> List.map (fun path ->
                VFile.fromPath path
                |> VFile.setData "source" (System.IO.File.ReadAllText path))

        // 2. Parse all files together (FCS project-level checking)
        match Parser.parseMultipleFiles (/* ... */) with
        | Ok parseResults ->
            // 3. Generate IR for each file
            let filesWithIR =
                List.zip vfiles parseResults
                |> List.map (fun (vfile, parseResult) ->
                    let distribution = (* generate IR from parseResult *)
                    { vfile with
                        Content = Some (box distribution)
                        History = vfile.History @ ["Parsed F# to IR"]
                    })

            // 4. Create VFileTree
            let tree = VFileTree.fromFiles filesWithIR
            Ok tree

        | Error err ->
            // 5. Return tree with error
            let tree = VFileTree.fromFiles vfiles
            // Add error to first file (or all files, depending on error type)
            Error tree

    /// Parse .fsproj project → VFileTree
    let parseProject (projectPath: string): Result<VFileTree, VFileTree> =
        // 1. Parse .fsproj to get project structure
        match ProjectParser.parse projectPath with
        | Ok projectInfo ->
            // 2. Create VFileTree from project structure
            let tree = VFileTree.fromDirectory (System.IO.Path.GetDirectoryName projectPath)

            // 3. Parse all files
            parseFiles projectInfo.SourceFiles

        | Error err ->
            Error (VFileTree.empty |> (* add error *))
```

#### F# Backend Integration

```fsharp
namespace Morphir.Backends.FSharp

open Morphir.IR.Pipeline

module BackendAPI =
    /// Generate F# from Morphir IR → VFile
    let generate (irFile: VFile): Result<VFile, VFile> =
        match irFile.Content with
        | Some content ->
            // 1. Unbox IR
            let distribution = unbox<Distribution> content

            // 2. Generate F# code
            let fsharpCode = (* generate F# code from IR *)

            // 3. Create output VFile
            Ok { irFile with
                   Content = Some (box fsharpCode)
                   History = irFile.History @ ["Generated F# from IR"]
               }

        | None ->
            Error (irFile |> VFile.error "No IR content to generate from" None)

    /// Generate F# from VFileTree → VFileTree
    let generateTree (tree: VFileTree): Result<VFileTree, VFileTree> =
        // Process all files in tree
        let processedFiles =
            tree
            |> VFileTree.allFiles
            |> List.map generate
            |> (* collect results *)

        VFileTree.fromFiles processedFiles
```

---

## Pipeline Integration

The Pipeline already processes `VFile` through transformations. We extend it to support `VFileTree`:

```fsharp
namespace Morphir.IR.Pipeline

/// <summary>
/// Processor that operates on VFileTree (multi-file projects)
/// </summary>
type TreeProcessor = {
    /// <summary>Process entire tree</summary>
    ProcessTree: VFileTree -> Result<VFileTree, VFileTree>

    /// <summary>Process individual file (for leaf operations)</summary>
    ProcessFile: VFile -> Result<VFile, VFile>
}

module TreeProcessor =
    /// <summary>Create processor from file processor (applies to each file)</summary>
    let fromFileProcessor (proc: VFile -> Result<VFile, VFile>): TreeProcessor =
        {
            ProcessTree = fun tree ->
                // Apply processor to each file in tree
                tree
                |> VFileTree.allFiles
                |> List.map proc
                |> (* collect results, rebuild tree *)
                |> Ok

            ProcessFile = proc
        }

    /// <summary>Pipeline builder for trees</summary>
    let pipeline = TreePipelineBuilder()

type TreePipelineBuilder() =
    member _.Yield(_) = TreeProcessor.empty

    [<CustomOperation("parseFiles")>]
    member _.ParseFiles(proc: TreeProcessor, parser: string list -> Result<VFileTree, VFileTree>) =
        { proc with ProcessTree = parser }

    [<CustomOperation("transform")>]
    member _.Transform(proc: TreeProcessor, transformer: TreeProcessor) =
        // Compose processors
        ...
```

---

## Relationship to morphir-elm's FileMap

### Design Philosophy: VFileTree as Primary

**KEY DECISION**: morphir-dotnet uses `VFileTree` as the **primary, universal abstraction** for all file operations. This is a **departure from morphir-elm's flat FileMap** approach.

**Rationale**:
- ✅ Better scalability for large projects (thousands of files)
- ✅ Explicit directory structure (no path parsing needed)
- ✅ Metadata at all levels (file AND directory)
- ✅ Lazy loading support (future)
- ✅ Better CLI UX (tree visualization)
- ✅ Natural fit for .fsproj parsing (already hierarchical)

**morphir-elm Compatibility**: Always available via `toFileMap()` conversion function, but not the primary abstraction.

---

### morphir-elm FileMap Concept

In morphir-elm, backends generate a `FileMap`:

```elm
-- morphir-elm: src/Morphir/Scala/Backend.elm
type alias FileMap = Dict FilePath FileContent

mapDistribution : Options -> TestSuite -> Distribution -> Result Error FileMap
-- Returns: Dict FilePath FileContent
-- Example:
--   { "com/example/Types.scala" -> "package com.example\n\nobject Types { ... }"
--   , "com/example/Domain.scala" -> "package com.example\n\nobject Domain { ... }"
--   }
```

**Purpose**: Map from file paths to generated code content (flat dictionary)

**Limitation**: Loses directory structure information (reconstructed from paths)

---

### morphir-dotnet Approach

Our unified architecture provides **full-featured VFileTree** as primary with **compatibility layer** for morphir-elm interop:

#### 1. PRIMARY: `VFileTree` (Universal Abstraction)

**All backends generate trees directly**:

```fsharp
// F# Backend generates VFileTree (PRIMARY API)
module FSharpBackend.Generator =
    /// Generate F# code for Distribution
    /// Returns hierarchical tree structure
    let generate (distribution: Distribution<unit, unit>)
        : VFileTree =

        // Build hierarchical tree from modules
        let rec buildTree (basePath: string) (modules: Map<Path, Module.Definition<unit>>)
            : VFileTree =

            // Group modules by first path segment
            let grouped =
                modules
                |> Map.toList
                |> List.groupBy (fun (path, _) ->
                    match path with
                    | [] -> None
                    | first :: _ -> Some first)

            // Files at this level (leaf modules)
            let files =
                grouped
                |> List.filter (fun (key, _) -> key = None)
                |> List.collect (fun (_, mods) ->
                    mods |> List.map (fun (path, moduleDef) ->
                        let fileName = (List.last path) + ".fs"
                        let fsharpCode = generateModule moduleDef
                        File (
                            VFile.create fileName fsharpCode
                            |> VFile.setData "morphir.module-path" path
                            |> VFile.setData "morphir.generated-at" DateTime.UtcNow
                        )))

            // Subdirectories (nested modules)
            let subdirs =
                grouped
                |> List.choose (fun (key, mods) ->
                    match key with
                    | None -> None
                    | Some dirName ->
                        // Strip first segment from paths
                        let nestedMods =
                            mods
                            |> List.map (fun (path, moduleDef) ->
                                (path |> List.tail, moduleDef))
                            |> Map.ofList
                        Some (Directory (buildTree dirName nestedMods)))

            {
                Path = basePath
                Content = files @ subdirs
                Metadata = ImmutableDictionary.Empty
                Config = TreeConfig.default
            }

        buildTree "generated" (distribution.Modules)

// Result: VFileTree (hierarchical)
// Example:
//   VFileTree {
//       Path = "generated"
//       Content = [
//           Directory {
//               Path = "Morphir"
//               Content = [
//                   Directory {
//                       Path = "Reference"
//                       Content = [
//                           File (VFile { Path = "Model.fs", Content = "...", ... })
//                           File (VFile { Path = "Logic.fs", Content = "...", ... })
//                       ]
//                   }
//               ]
//           }
//       ]
//   }
```

**Advantages of VFileTree as Primary**:
- ✅ **Explicit structure**: No path parsing needed, hierarchy is first-class
- ✅ **Rich metadata**: At file AND directory levels
- ✅ **Better UX**: CLI can visualize tree (`tree` command)
- ✅ **Scalability**: Directory-level lazy loading (future)
- ✅ **Statistics**: Errors/warnings per directory
- ✅ **Natural**: Matches .fsproj structure directly

#### 2. COMPATIBILITY: `Map<string, VFile>` (morphir-elm Interop)

**When morphir-elm compatibility needed** (conversion function):

```fsharp
// Generate hierarchical file tree
let mapPackageDefinitionToTree (packageDef: Package.Definition<unit>)
    : VFileTree =

    // 1. Group modules by namespace hierarchy
    let modulesByNamespace =
        packageDef.Modules
        |> Map.toList
        |> List.groupBy (fun (path, _) ->
            path |> List.take (List.length path - 1))  // Parent path

    // 2. Build tree recursively
    let rec buildTree (parentPath: Path) (modules: (Path * Module.Definition<unit>) list)
        : VFileTree =

        let files =
            modules
            |> List.map (fun (modulePath, moduleDef) ->
                let fsharpCode = generateModule moduleDef
                let fileName = (List.last modulePath) + ".fs"

                File (VFile.create fileName fsharpCode
                      |> VFile.setData "modulePath" modulePath))

        let subdirs =
            modulesByNamespace
            |> List.filter (fun (path, _) -> isChildOf parentPath path)
            |> List.map (fun (path, mods) ->
                Directory (buildTree path mods))

        {
            Path = Path.toString parentPath
            Content = files @ subdirs
            Metadata = ImmutableDictionary.Empty
            Config = TreeConfig.empty
        }

    buildTree [] (Map.toList packageDef.Modules)

// Result: VFileTree
// Example:
//   VFileTree {
//       Path = "Com/Example"
//       Content = [
//           File (VFile { Path = "Types.fs", ... })
//           Directory (VFileTree {
//               Path = "Com/Example/Domain"
//               Content = [
//                   File (VFile { Path = "Orders.fs", ... })
//                   File (VFile { Path = "Customers.fs", ... })
//               ]
//           })
//       ]
//   }
```

**Advantages over flat Map**:
- ✅ Preserves directory structure
- ✅ Better CLI UX (show tree, navigate hierarchy)
- ✅ Statistics per directory (errors, warnings, file count)
- ✅ Natural representation of project structure

### Compatibility Layer: Conversion Functions

```fsharp
/// Conversion between VFileTree (primary) and Map (compatibility)
[<RequireQualifiedAccess>]
module VFileTree =
    /// Flatten tree to morphir-elm style FileMap
    /// USE: For morphir-elm interop, simple backends
    /// PREFER: Keep using VFileTree when possible
    let toFileMap (tree: VFileTree): Map<string, VFile> =
        let rec flatten (basePath: string) (content: TreeContent list)
            : (string * VFile) list =
            content
            |> List.collect (function
                | File file ->
                    let fullPath =
                        match file.Path with
                        | Some p -> Path.Combine(basePath, p)
                        | None -> basePath
                    [ (fullPath, file) ]
                | Directory subtree ->
                    let dirPath = Path.Combine(basePath, subtree.Path)
                    flatten dirPath subtree.Content)

        tree.Content
        |> flatten tree.Path
        |> Map.ofList

    /// Create tree from morphir-elm style FileMap
    /// USE: When consuming morphir-elm outputs
    /// NOTE: Infers directory structure from paths
    let fromFileMap (fileMap: Map<string, VFile>): VFileTree =
        // Group files by directory path segments, build tree
        let groupByDirectory (files: (string * VFile) list)
            : Map<string option, (string * VFile) list> =
            files
            |> List.groupBy (fun (path, _) ->
                let parts = path.Split([| '/'; '\\' |], StringSplitOptions.RemoveEmptyEntries)
                if parts.Length > 1 then Some parts.[0] else None)
            |> Map.ofList

        let rec buildTree (basePath: string) (files: (string * VFile) list)
            : TreeContent list =
            let grouped = groupByDirectory files

            // Files at this level
            let leafFiles =
                grouped
                |> Map.tryFind None
                |> Option.defaultValue []
                |> List.map (fun (_, file) -> File file)

            // Subdirectories
            let subdirs =
                grouped
                |> Map.toList
                |> List.choose (fun (key, dirFiles) ->
                    match key with
                    | None -> None
                    | Some dirName ->
                        // Strip first segment from paths
                        let nestedFiles =
                            dirFiles
                            |> List.map (fun (path, file) ->
                                let parts = path.Split([| '/'; '\\' |], StringSplitOptions.RemoveEmptyEntries)
                                let relativePath = String.Join("/", parts |> Array.skip 1)
                                (relativePath, file))
                        Some (Directory {
                            Path = dirName
                            Content = buildTree dirName nestedFiles
                            Metadata = ImmutableDictionary.Empty
                            Config = TreeConfig.default
                        }))

            leafFiles @ subdirs

        {
            Path = "."
            Content = buildTree "." (fileMap |> Map.toList)
            Metadata = ImmutableDictionary.Empty
            Config = TreeConfig.default
        }

    /// Flatten tree to simple string map (morphir-elm exact equivalent)
    /// USE: ONLY for exact morphir-elm compatibility
    /// PREFER: Use toFileMap for richer metadata
    let toStringMap (tree: VFileTree): Map<string, string> =
        tree
        |> toFileMap
        |> Map.map (fun _ file ->
            match file.Content with
            | Some content -> unbox<string> content
            | None -> "")
```

### Migration Guide: morphir-elm FileMap → morphir-dotnet

#### Scenario 1: Simple Backend (Flat Map)

**morphir-elm (Elm)**:
```elm
mapDistribution : Options -> Distribution -> Result Error FileMap
mapDistribution options dist =
    let
        files =
            dist.packageDefinition.modules
                |> Dict.toList
                |> List.map (\(modulePath, moduleDef) ->
                    let
                        filePath = modulePathToFilePath modulePath
                        content = generateScalaCode moduleDef
                    in
                    (filePath, content))
                |> Dict.fromList
    in
    Ok files
```

**morphir-dotnet (F#)**:
```fsharp
let mapDistribution (options: BackendOptions) (dist: Distribution<unit, unit>)
    : Result<Map<string, VFile>, string> =

    let files =
        dist.PackageDefinition.Modules
        |> Map.toList
        |> List.map (fun (modulePath, moduleDef) ->
            let filePath = modulePathToFilePath modulePath
            let content = generateFSharpCode moduleDef

            // Create VFile instead of plain string
            let file =
                VFile.create filePath content
                |> VFile.setData "modulePath" modulePath
                |> VFile.info "Generated from IR"

            (filePath, file))
        |> Map.ofList

    Ok files
```

#### Scenario 2: Advanced Backend (Tree)

**morphir-dotnet only** (no morphir-elm equivalent):
```fsharp
let mapDistributionToTree (options: BackendOptions) (dist: Distribution<unit, unit>)
    : Result<VFileTree, string> =

    let tree = mapPackageDefinitionToTree dist.PackageDefinition
    Ok tree

// CLI can show tree structure:
// $ morphir gen fsharp morphir-ir.json --show-tree
// 📁 Com/Example/
//   📄 Types.fs (123 lines)
//   📁 Domain/
//     📄 Orders.fs (256 lines)
//     📄 Customers.fs (189 lines)
```

### Usage in F# Backend

From [github-issues-fsharp-backend.md](./github-issues-fsharp-backend.md#phase-4-cli-integration):

```fsharp
// Phase 4: CLI Integration
module Generator =
    /// Render FileMap to disk (morphir-elm style)
    let renderFileMap (fileMap: Map<string, VFile>) (outputDir: string): Result<unit, string> =
        fileMap
        |> Map.iter (fun filePath file ->
            let fullPath = Path.Combine(outputDir, filePath)
            let dir = Path.GetDirectoryName(fullPath)

            // Create directory if needed
            if not (Directory.Exists(dir)) then
                Directory.CreateDirectory(dir) |> ignore

            // Write file content
            match file.Content with
            | Some content ->
                File.WriteAllText(fullPath, unbox<string> content)

                // Log generation (use file history)
                file.History
                |> List.iter (fun entry -> printfn "  %s" entry)

            | None ->
                eprintfn "Warning: %s has no content" filePath)

        Ok ()

    /// Render FileTree to disk (advanced, preserves structure)
    let renderFileTree (tree: VFileTree) (outputDir: string): Result<unit, string> =
        let rec writeTree (basePath: string) (tree: VFileTree) =
            tree.Content
            |> List.iter (function
                | File file ->
                    match file.Path, file.Content with
                    | Some filePath, Some content ->
                        let fullPath = Path.Combine(basePath, filePath)
                        File.WriteAllText(fullPath, unbox<string> content)
                    | _ -> ()

                | Directory subTree ->
                    let subPath = Path.Combine(basePath, subTree.Path)
                    Directory.CreateDirectory(subPath) |> ignore
                    writeTree subPath subTree)

        writeTree outputDir tree
        Ok ()
```

---

## Example: End-to-End Pipeline

### Scenario 1: F# → IR → JSON (Single File)

```fsharp
open Morphir.IR.Pipeline
open Morphir.Frontends.FSharp
open Morphir.Backends.Json

// Parse F# source (in-memory)
let source = """
namespace MyDomain

type Customer = {
    Id: int
    Name: string
}
"""

let result =
    FrontendAPI.parseInMemory "Customer.fs" source  // Returns: Result<VFile, VFile>
    |> Result.bind (fun file ->
        // Generate JSON from IR
        JsonBackend.generate file)  // Returns: Result<VFile, VFile>

match result with
| Ok file ->
    // file.Content = JSON string
    let json = unbox<string> file.Content.Value
    printfn "Generated JSON: %s" json

    // Check diagnostics
    let warnings = VFile.warnings file
    warnings |> List.iter (fun msg ->
        printfn "Warning: %s" msg.Message)

| Error fileWithErrors ->
    // Print all errors
    let errors = VFile.errors fileWithErrors
    errors |> List.iter (fun msg ->
        printfn "Error at %A: %s" msg.Position msg.Message)
```

### Scenario 2: F# Project → IR → F# (Round-trip)

```fsharp
open Morphir.IR.Pipeline
open Morphir.Frontends.FSharp
open Morphir.Backends.FSharp

// Parse entire project
let result =
    FrontendAPI.parseProject "MyProject/MyProject.fsproj"  // Returns: Result<VFileTree, VFileTree>
    |> Result.bind (fun tree ->
        // Generate F# from IR (round-trip)
        BackendAPI.generateTree tree)  // Returns: Result<VFileTree, VFileTree>

match result with
| Ok tree ->
    // Get statistics
    let stats = VFileTree.stats tree
    printfn "Processed %d files" stats.TotalFiles
    printfn "Warnings: %d, Errors: %d" stats.TotalWarnings stats.TotalErrors

    // Write generated F# files to disk
    tree
    |> VFileTree.allFiles
    |> List.iter (fun file ->
        match file.Path, file.Content with
        | Some path, Some content ->
            let fsharpCode = unbox<string> content
            System.IO.File.WriteAllText(path, fsharpCode)
        | _ -> ())

| Error treeWithErrors ->
    // Print all errors
    let errors = VFileTree.allErrors treeWithErrors
    errors |> List.iter (fun (filePath, msgs) ->
        printfn "Errors in %s:" filePath
        msgs |> List.iter (fun msg ->
            printfn "  %s" msg.Message))
```

### Scenario 3: Pipeline with Transformations

```fsharp
open Morphir.IR.Pipeline

// Create pipeline: F# → IR → Validate → Optimize → JSON
let proc = pipeline {
    // Parse F# file
    uses (FSharpFrontend.parser)

    // Validate IR
    uses (ValidatePlugin.plugin)

    // Optimize IR
    uses (OptimizePlugin.plugin)

    // Generate JSON
    stringify (JsonBackend.compiler)
}

// Run pipeline on file
let inputFile = VFile.fromPath "Calculator.fs"
let result = proc.Process(inputFile)

match result with
| Ok outputFile ->
    // outputFile.Content = JSON string
    // outputFile.Messages = all diagnostics from all phases

    // Check for warnings from any phase
    let warnings = VFile.warnings outputFile
    warnings |> List.iter (fun msg ->
        printfn "[%s] %s" (msg.Source |> Option.defaultValue "unknown") msg.Message)

| Error fileWithErrors ->
    // Print errors
    VFile.errors fileWithErrors
    |> List.iter (fun msg ->
        printfn "Error: %s" msg.Message)
```

---

## Benefits of Unified Architecture

| Benefit | Description |
|---------|-------------|
| **Consistency** | All frontends/backends use same file abstraction |
| **Composability** | VFile flows through pipelines seamlessly |
| **Rich Diagnostics** | Source locations, rule IDs, severity levels |
| **History Tracking** | Track transformations: "F# → IR → Optimized → JSON" |
| **Metadata** | Extensible data dictionary for custom attributes |
| **Multi-File Support** | VFileTree for projects |
| **Lazy Loading** | (Future) Parse files on demand |
| **Error Accumulation** | Collect all errors, don't fail fast |

---

## Migration Path

### Phase 1: Use VFile in F# Frontend (M0-M1)
- F# Frontend returns `Result<VFile, VFile>` instead of `Result<Distribution, string list>`
- Diagnostics use `VMessage` with source locations
- Keep simple API for try-morphir

### Phase 2: Add VFileTree (M3)
- Implement `VFileTree` in `Morphir.IR.Pipeline`
- F# Frontend uses `VFileTree` for multi-file projects
- Keep flat result for backward compatibility (map tree → list of files)

### Phase 3: Pipeline Integration (M4)
- Extend Pipeline to support `TreeProcessor`
- Add `pipeline { parseFiles ... }` syntax
- CLI uses `VFileTree` for better diagnostics

### Phase 4: Standardize Across All Components (Future)
- All frontends return `VFile` or `VFileTree`
- All backends accept `VFile` or `VFileTree`
- All transformations operate on `VFile`

---

## Recommended API Surface

```fsharp
// Simple API (single file)
FrontendAPI.parseInMemory : string -> string -> Result<VFile, VFile>

// Advanced API (multi-file)
FrontendAPI.parseFiles : string list -> Result<VFileTree, VFileTree>
FrontendAPI.parseProject : string -> Result<VFileTree, VFileTree>

// Backend API (single file)
BackendAPI.generate : VFile -> Result<VFile, VFile>

// Backend API (multi-file)
BackendAPI.generateTree : VFileTree -> Result<VFileTree, VFileTree>

// Pipeline API (existing)
MorphirProcessor.process : VFile -> Result<VFile, VFile>

// Pipeline API (new, multi-file)
TreeProcessor.processTree : VFileTree -> Result<VFileTree, VFileTree>
```

---

## Open Questions

1. **Lazy Loading**: Should `VFileTree` support lazy loading (don't parse until accessed)?
   - **Recommendation**: Yes, in Phase 3+. Use `Lazy<Result<VFile, VFile>>`

2. **Tree Navigation**: Should we add parent references?
   - **Recommendation**: Yes, but keep optional to avoid circular references

3. **Serialization**: Should `VFileTree` be serializable (for caching)?
   - **Recommendation**: Yes, implement `toJson/fromJson` for caching parsed trees

4. **Backward Compatibility**: How do we handle existing code that expects flat lists?
   - **Recommendation**: Keep flat APIs, add tree APIs as new overloads

---

## References

- [VFile (Existing)](../../src/Morphir.IR.Pipeline/File.fs)
- [MorphirProcessor (Existing)](../../src/Morphir.IR.Pipeline/Processor.fs)
- [F# Frontend PRD](./PRD-fsharp-frontend.md)
- [F# Frontend DocumentTree Proposal](./fsharp-frontend-document-tree.md)
- [Laika DocumentTree](https://typelevel.org/Laika/latest/04-customizing-laika/04-document-ast.html)
- [unifiedJS VFile](https://github.com/vfile/vfile)

---

**Document Status**: Proposal
**Last Updated**: 2025-12-31
**Next Steps**:
1. Review with team
2. Implement `VFileTree` in `Morphir.IR.Pipeline`
3. Update F# Frontend to use `VFile`
4. Extend Pipeline to support `TreeProcessor`
