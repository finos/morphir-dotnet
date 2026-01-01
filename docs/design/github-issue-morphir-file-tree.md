# GitHub Issue: Implement VFileTree in Morphir.IR.Pipeline

**Type**: Feature
**Labels**: `feature`, `file-architecture`, `priority-p0`, `pipeline`
**Priority**: P0 (Blocks F# Backend and F# Frontend)
**Milestone**: v1.0.0
**Estimated Effort**: 1 week
**Project**: Morphir.IR.Pipeline (existing project - add new file)

---

## Description

Implement `VFileTree` - a hierarchical file structure abstraction for multi-file projects in the `Morphir.IR.Pipeline` project. This will be the **universal, primary abstraction** for file operations across all morphir-dotnet components (F# Backend, F# Frontend, future backends/frontends).

**Naming**: VFile and VFileTree (inspired by unifiedJS VFile). More concise than VFile/VFileTree.

**Key Decision**: VFileTree is the PRIMARY abstraction (not a conversion from flat maps). This departs from morphir-elm's flat FileMap approach in favor of better scalability, UX, and structure preservation.

**Related Design**: [Unified File Architecture](./unified-file-architecture.md)

---

## Context

### What Exists Today

✅ **VFile** (already in `Morphir.IR.Pipeline/File.fs`):
- Virtual file with content, path, diagnostics, metadata
- Perfect for single-file operations
- Already used by Pipeline

### What's Missing

❌ **Multi-file support**:
- No hierarchical structure (directories + files)
- No cross-file relationships
- No project-level metadata

---

## Acceptance Criteria

### Core Types (Add to `Morphir.IR.Pipeline/FileTree.fs`)

**Type Definitions**:
- [ ] Define `VFileTree` record type
- [ ] Define `TreeContent` discriminated union (`File | Directory`)
- [ ] Define `TreeConfig` for project-level configuration
- [ ] XML doc comments on all types

**Required Fields**:
- [ ] `Path: string` - Root path (directory or project file)
- [ ] `Content: TreeContent list` - Files and subdirectories
- [ ] `Metadata: ImmutableDictionary<string, obj>` - Tree-level metadata
- [ ] `Config: TreeConfig` - Project configuration

### Module Functions

**Creation Functions** (`VFileTree` module):
- [ ] `empty: VFileTree` - Create empty tree
- [ ] `create: string -> TreeConfig -> VFileTree` - Create with path and config
- [ ] `addFile: VFile -> VFileTree -> VFileTree` - Add file to tree
- [ ] `addDirectory: VFileTree -> VFileTree -> VFileTree` - Add subtree
- [ ] `fromFiles: VFile list -> VFileTree` - Build tree from file list

**Query Functions**:
- [ ] `allFiles: VFileTree -> VFile list` - Flatten to all files
- [ ] `findFile: string -> VFileTree -> VFile option` - Find by path
- [ ] `fileCount: VFileTree -> int` - Total files (recursive)
- [ ] `directoryCount: VFileTree -> int` - Total directories
- [ ] `hasErrors: VFileTree -> bool` - Check for errors in any file
- [ ] `collectMessages: VFileTree -> VMessage list` - All diagnostics

**Transformation Functions**:
- [ ] `map: (VFile -> VFile) -> VFileTree -> VFileTree` - Transform all files
- [ ] `filter: (VFile -> bool) -> VFileTree -> VFileTree` - Filter files
- [ ] `updateFile: string -> (VFile -> VFile) -> VFileTree -> VFileTree` - Update specific file

**Conversion Functions** (morphir-elm Compatibility):
- [ ] `toFileMap: VFileTree -> Map<string, VFile>` - Flatten to flat map (morphir-elm compatible)
- [ ] `fromFileMap: Map<string, VFile> -> VFileTree` - Build tree from flat map (infer structure)
- [ ] `toStringMap: VFileTree -> Map<string, string>` - Exact morphir-elm `Dict FilePath FileContent` equivalent

**I/O Functions**:
- [ ] `writeToDisk: string -> VFileTree -> Result<unit, string>` - Write tree to disk
- [ ] `loadFromDisk: string -> Result<VFileTree, string>` - Load tree from disk (future)

**Metadata Functions**:
- [ ] `setMetadata: string -> obj -> VFileTree -> VFileTree` - Set tree-level metadata
- [ ] `getMetadata: string -> VFileTree -> obj option` - Get tree-level metadata
- [ ] `statistics: VFileTree -> TreeStatistics` - Get tree statistics (file count, errors, warnings, etc.)

### TreeConfig Type

**Fields**:
- [ ] `Name: string option` - Project name
- [ ] `Version: string option` - Project version
- [ ] `OutputFormat: string option` - Output format preference (e.g., "fsharp", "csharp")
- [ ] `Extensions: ImmutableDictionary<string, obj>` - Plugin-specific configuration

### TreeStatistics Type

**Fields**:
- [ ] `TotalFiles: int` - Total number of files
- [ ] `TotalDirectories: int` - Total number of directories
- [ ] `ErrorCount: int` - Total error messages
- [ ] `WarningCount: int` - Total warning messages
- [ ] `InfoCount: int` - Total info messages

---

## Implementation Tasks

### 1. Create FileTree.fs

```bash
# Add new file to existing Morphir.IR.Pipeline project
touch src/Morphir.IR.Pipeline/FileTree.fs
# Update Morphir.IR.Pipeline.fsproj to include FileTree.fs AFTER File.fs
```

### 2. Define Core Types

```fsharp
namespace Morphir.IR.Pipeline

open System.Collections.Immutable

/// <summary>
/// Configuration for a file tree (project-level settings).
/// </summary>
type TreeConfig = {
    /// <summary>Project name</summary>
    Name: string option
    /// <summary>Project version</summary>
    Version: string option
    /// <summary>Output format preference</summary>
    OutputFormat: string option
    /// <summary>Plugin-specific configuration</summary>
    Extensions: ImmutableDictionary<string, obj>
}

/// <summary>
/// Content of a file tree node (file or directory).
/// </summary>
type TreeContent =
    | File of file: VFile
    | Directory of tree: VFileTree

/// <summary>
/// Represents a hierarchical tree of Morphir files.
/// This is the PRIMARY abstraction for multi-file operations in morphir-dotnet.
/// </summary>
and VFileTree = {
    /// <summary>Root path (directory or project file)</summary>
    Path: string
    /// <summary>Tree content (files and subdirectories)</summary>
    Content: TreeContent list
    /// <summary>Tree-level metadata</summary>
    Metadata: ImmutableDictionary<string, obj>
    /// <summary>Project configuration</summary>
    Config: TreeConfig
}

/// <summary>
/// Statistics about a file tree.
/// </summary>
type TreeStatistics = {
    TotalFiles: int
    TotalDirectories: int
    ErrorCount: int
    WarningCount: int
    InfoCount: int
}
```

### 3. Implement Module Functions

```fsharp
[<RequireQualifiedAccess>]
module TreeConfig =
    let empty: TreeConfig = {
        Name = None
        Version = None
        OutputFormat = None
        Extensions = ImmutableDictionary.Empty
    }

    let create (name: string) (version: string): TreeConfig =
        { empty with Name = Some name; Version = Some version }

[<RequireQualifiedAccess>]
module VFileTree =
    /// Create an empty file tree
    let empty: VFileTree = {
        Path = "."
        Content = []
        Metadata = ImmutableDictionary.Empty
        Config = TreeConfig.empty
    }

    /// Create a file tree with path and config
    let create (path: string) (config: TreeConfig): VFileTree =
        { empty with Path = path; Config = config }

    /// Add a file to the tree
    let addFile (file: VFile) (tree: VFileTree): VFileTree =
        { tree with Content = tree.Content @ [ File file ] }

    /// Add a subdirectory to the tree
    let addDirectory (subtree: VFileTree) (tree: VFileTree): VFileTree =
        { tree with Content = tree.Content @ [ Directory subtree ] }

    /// Build tree from list of files (flat structure)
    let fromFiles (files: VFile list): VFileTree =
        files
        |> List.fold (fun tree file -> addFile file tree) empty

    /// Get all files in the tree (flattened)
    let rec allFiles (tree: VFileTree): VFile list =
        tree.Content
        |> List.collect (function
            | File file -> [ file ]
            | Directory subtree -> allFiles subtree)

    /// Find a file by path
    let rec findFile (path: string) (tree: VFileTree): VFile option =
        tree.Content
        |> List.tryPick (function
            | File file when file.Path = Some path -> Some file
            | Directory subtree -> findFile path subtree
            | _ -> None)

    /// Count total files (recursive)
    let fileCount (tree: VFileTree): int =
        tree |> allFiles |> List.length

    /// Count total directories (recursive)
    let rec directoryCount (tree: VFileTree): int =
        tree.Content
        |> List.sumBy (function
            | File _ -> 0
            | Directory subtree -> 1 + directoryCount subtree)

    /// Check if tree has any errors
    let hasErrors (tree: VFileTree): bool =
        tree
        |> allFiles
        |> List.exists VFile.hasErrors

    /// Collect all diagnostic messages from all files
    let collectMessages (tree: VFileTree): VMessage list =
        tree
        |> allFiles
        |> List.collect (fun file -> file.Messages)

    /// Map a function over all files in the tree
    let rec map (f: VFile -> VFile) (tree: VFileTree): VFileTree =
        {
            tree with
                Content =
                    tree.Content
                    |> List.map (function
                        | File file -> File (f file)
                        | Directory subtree -> Directory (map f subtree))
        }

    /// Filter files in the tree
    let rec filter (predicate: VFile -> bool) (tree: VFileTree): VFileTree =
        {
            tree with
                Content =
                    tree.Content
                    |> List.choose (function
                        | File file when predicate file -> Some (File file)
                        | Directory subtree -> Some (Directory (filter predicate subtree))
                        | _ -> None)
        }

    /// Update a specific file by path
    let rec updateFile (path: string) (f: VFile -> VFile) (tree: VFileTree): VFileTree =
        {
            tree with
                Content =
                    tree.Content
                    |> List.map (function
                        | File file when file.Path = Some path -> File (f file)
                        | Directory subtree -> Directory (updateFile path f subtree)
                        | other -> other)
        }

    /// Flatten tree to morphir-elm style FileMap
    let toFileMap (tree: VFileTree): Map<string, VFile> =
        let rec flatten (basePath: string) (content: TreeContent list): (string * VFile) list =
            content
            |> List.collect (function
                | File file ->
                    let fullPath =
                        match file.Path with
                        | Some p -> System.IO.Path.Combine(basePath, p)
                        | None -> basePath
                    [ (fullPath, file) ]
                | Directory subtree ->
                    let dirPath = System.IO.Path.Combine(basePath, subtree.Path)
                    flatten dirPath subtree.Content)

        tree.Content
        |> flatten tree.Path
        |> Map.ofList

    /// Create tree from morphir-elm style FileMap (infer structure from paths)
    let fromFileMap (fileMap: Map<string, VFile>): VFileTree =
        // TODO: Implement grouping by directory path segments
        // For now, return flat tree
        {
            Path = "."
            Content = fileMap |> Map.toList |> List.map (snd >> File)
            Metadata = ImmutableDictionary.Empty
            Config = TreeConfig.empty
        }

    /// Flatten tree to simple string map (morphir-elm exact equivalent)
    let toStringMap (tree: VFileTree): Map<string, string> =
        tree
        |> toFileMap
        |> Map.map (fun _ file ->
            match file.Content with
            | Some content -> unbox<string> content
            | None -> "")

    /// Set tree-level metadata
    let setMetadata (key: string) (value: obj) (tree: VFileTree): VFileTree =
        { tree with Metadata = tree.Metadata.SetItem(key, value) }

    /// Get tree-level metadata
    let getMetadata (key: string) (tree: VFileTree): obj option =
        match tree.Metadata.TryGetValue(key) with
        | true, value -> Some value
        | false, _ -> None

    /// Get tree statistics
    let statistics (tree: VFileTree): TreeStatistics =
        let files = allFiles tree
        let messages = collectMessages tree

        {
            TotalFiles = fileCount tree
            TotalDirectories = directoryCount tree
            ErrorCount = messages |> List.filter (fun m -> m.Severity = Error || m.Severity = Fatal) |> List.length
            WarningCount = messages |> List.filter (fun m -> m.Severity = Warning) |> List.length
            InfoCount = messages |> List.filter (fun m -> m.Severity = Info) |> List.length
        }

    /// Write tree to disk
    let writeToDisk (outputDir: string) (tree: VFileTree): Result<unit, string> =
        try
            tree
            |> toFileMap
            |> Map.iter (fun relPath file ->
                let fullPath = System.IO.Path.Combine(outputDir, relPath)
                let dir = System.IO.Path.GetDirectoryName(fullPath)
                System.IO.Directory.CreateDirectory(dir) |> ignore
                System.IO.File.WriteAllText(fullPath, file.Content |> Option.defaultValue ""))
            Ok ()
        with
        | ex -> Error ex.Message
```

### 4. Write Tests

Create `tests/Morphir.IR.Pipeline.Tests/FileTreeTests.fs`:

```fsharp
module Morphir.IR.Pipeline.Tests.FileTreeTests

open TUnit.Core
open Morphir.IR.Pipeline

[<Test>]
let ``Create empty file tree`` () =
    let tree = VFileTree.empty
    tree.Path |> should equal "."
    tree.Content |> should be empty

[<Test>]
let ``Add file to tree`` () =
    let file = VFile.create "test.fs" "let x = 42"
    let tree = VFileTree.empty |> VFileTree.addFile file

    tree |> VFileTree.fileCount |> should equal 1
    tree |> VFileTree.allFiles |> List.head |> should equal file

[<Test>]
let ``Add subdirectory to tree`` () =
    let subTree = VFileTree.create "subdir" TreeConfig.empty
    let tree = VFileTree.empty |> VFileTree.addDirectory subTree

    tree |> VFileTree.directoryCount |> should equal 1

[<Test>]
let ``toFileMap flattens tree to Map`` () =
    let file1 = VFile.create "file1.fs" "code1"
    let file2 = VFile.create "file2.fs" "code2"

    let subTree = VFileTree.empty |> VFileTree.addFile file2
    let tree =
        VFileTree.empty
        |> VFileTree.addFile file1
        |> VFileTree.addDirectory subTree

    let fileMap = VFileTree.toFileMap tree
    fileMap |> Map.count |> should equal 2

[<Test>]
let ``fromFiles builds flat tree`` () =
    let files = [
        VFile.create "file1.fs" "code1"
        VFile.create "file2.fs" "code2"
    ]

    let tree = VFileTree.fromFiles files
    tree |> VFileTree.fileCount |> should equal 2

[<Test>]
let ``statistics calculates correctly`` () =
    let fileWithError =
        VFile.create "error.fs" "code"
        |> VFile.error "Test error" None

    let fileWithWarning =
        VFile.create "warning.fs" "code"
        |> VFile.warn "Test warning" None

    let tree =
        VFileTree.empty
        |> VFileTree.addFile fileWithError
        |> VFileTree.addFile fileWithWarning

    let stats = VFileTree.statistics tree
    stats.TotalFiles |> should equal 2
    stats.ErrorCount |> should equal 1
    stats.WarningCount |> should equal 1
```

### 5. Update Project File

Add to `src/Morphir.IR.Pipeline/Morphir.IR.Pipeline.fsproj`:

```xml
<ItemGroup>
  <Compile Include="File.fs" />
  <Compile Include="FileTree.fs" />  <!-- NEW -->
  <!-- ... other files ... -->
</ItemGroup>
```

### 6. Documentation

- [ ] Add XML doc comments to all public types and functions
- [ ] Create usage examples in README
- [ ] Link from unified-file-architecture.md
- [ ] Document morphir-elm migration patterns

---

## Success Criteria

- [ ] All types defined and compile successfully
- [ ] All module functions implemented
- [ ] All tests pass (≥80% coverage)
- [ ] Can create, query, transform, and write file trees
- [ ] Conversion to/from flat maps works correctly
- [ ] Documentation complete
- [ ] No breaking changes to existing `VFile` API

---

## Dependencies

- None (extends existing Morphir.IR.Pipeline project)

## Blocks

- F# Backend Issue #365 (Foundation)
- F# Frontend (all issues)
- Any future backends/frontends

---

## Related Documents

- [Unified File Architecture](./unified-file-architecture.md) - Complete design
- [PRD: F# Backend](./PRD-fsharp-backend.md) - Usage in backend
- [PRD: F# Frontend](./PRD-fsharp-frontend.md) - Usage in frontend

---

**Ready for GitHub Copilot**: This issue provides complete context for AI-assisted implementation.

**Estimated Effort**: 1 week (4-5 developer-days)

**Priority**: P0 (Critical - blocks backend and frontend work)
