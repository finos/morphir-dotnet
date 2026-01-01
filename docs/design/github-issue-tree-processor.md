# GitHub Issue: Implement TreeProcessor in Morphir.IR.Pipeline

**Type**: Feature
**Labels**: `feature`, `file-architecture`, `priority-p1`, `pipeline`
**Priority**: P1 (Enhances VFileTree support)
**Milestone**: v1.0.0
**Estimated Effort**: 3-5 days
**Project**: Morphir.IR.Pipeline (existing project - add new file)

---

## Description

Implement `TreeProcessor` - pipeline support for processing `VFileTree` structures in the `Morphir.IR.Pipeline` project. This enables multi-file transformations while maintaining the hierarchical structure and aggregating diagnostics across all files.

**Related Design**: [Unified File Architecture](./unified-file-architecture.md#layer-3-pipeline-integration)

---

## Context

### What Exists Today

✅ **VFile** (in `Morphir.IR.Pipeline/File.fs`):
- Virtual file with content, path, diagnostics, metadata
- Used by existing `MorphirProcessor` for single-file operations

✅ **MorphirProcessor** (in `Morphir.IR.Pipeline/Processor.fs`):
- Processes single `VFile` through transformation pipeline
- Supports computation expression syntax

### What's Missing

❌ **Multi-file pipeline support**:
- No way to process `VFileTree` (hierarchical multi-file structures)
- No aggregation of diagnostics across files
- No directory-level transformations

---

## Acceptance Criteria

### Core Types (Add to `Morphir.IR.Pipeline/TreeProcessor.fs`)

**Type Definitions**:
- [ ] Define `TreeProcessor` record type
- [ ] Define computation expression builder `TreePipelineBuilder`
- [ ] XML doc comments on all types

### TreeProcessor Type

**Fields**:
- [ ] `ProcessTree: VFileTree -> Result<VFileTree, VFileTree>` - Process entire tree
- [ ] `ProcessFile: VFile -> Result<VFile, VFile>` - Process individual files (for leaf operations)
- [ ] `Name: string option` - Processor name (for diagnostics)

### Module Functions

**Creation Functions** (`TreeProcessor` module):
- [ ] `empty: TreeProcessor` - Create empty processor (identity)
- [ ] `fromFileProcessor: (VFile -> Result<VFile, VFile>) -> TreeProcessor` - Lift file processor to tree processor
- [ ] `fromMorphirProcessor: MorphirProcessor -> TreeProcessor` - Convert existing processor

**Composition Functions**:
- [ ] `compose: TreeProcessor -> TreeProcessor -> TreeProcessor` - Sequential composition
- [ ] `parallel: TreeProcessor list -> TreeProcessor` - Parallel processing (independent transformations)

**Execution Functions**:
- [ ] `run: TreeProcessor -> VFileTree -> Result<VFileTree, VFileTree>` - Execute processor on tree
- [ ] `runOnFiles: TreeProcessor -> VFile list -> Result<VFile list, VFile list>` - Execute on file list

**Diagnostics Functions**:
- [ ] `collectDiagnostics: VFileTree -> Map<string, VMessage list>` - Aggregate diagnostics by file
- [ ] `hasErrors: VFileTree -> bool` - Check if any file has errors
- [ ] `summarize: VFileTree -> ProcessorSummary` - Get processing summary

### Computation Expression Builder

**Custom Operations**:
- [ ] `Yield` - Create empty processor
- [ ] `parseTree` - Parse tree structure
- [ ] `transformTree` - Transform entire tree
- [ ] `mapFiles` - Apply transformation to each file
- [ ] `filterFiles` - Filter files based on predicate
- [ ] `aggregateDiagnostics` - Collect diagnostics
- [ ] `validateTree` - Validate tree structure

### ProcessorSummary Type

**Fields**:
- [ ] `TotalFiles: int` - Total files processed
- [ ] `SuccessCount: int` - Files processed successfully
- [ ] `ErrorCount: int` - Files with errors
- [ ] `WarningCount: int` - Total warnings
- [ ] `ProcessingTime: TimeSpan option` - Time taken (optional)

---

## Implementation Tasks

### 1. Create TreeProcessor.fs

```bash
# Add new file to existing Morphir.IR.Pipeline project
touch src/Morphir.IR.Pipeline/TreeProcessor.fs
# Update Morphir.IR.Pipeline.fsproj to include TreeProcessor.fs AFTER FileTree.fs
```

### 2. Define Core Types

```fsharp
namespace Morphir.IR.Pipeline

open System

/// <summary>
/// Summary of tree processing results.
/// </summary>
type ProcessorSummary = {
    TotalFiles: int
    SuccessCount: int
    ErrorCount: int
    WarningCount: int
    ProcessingTime: TimeSpan option
}

/// <summary>
/// Processor that operates on VFileTree (multi-file projects).
/// Supports both tree-level and file-level transformations.
/// </summary>
type TreeProcessor = {
    /// <summary>Process entire tree</summary>
    ProcessTree: VFileTree -> Result<VFileTree, VFileTree>

    /// <summary>Process individual file (for leaf operations)</summary>
    ProcessFile: VFile -> Result<VFile, VFile>

    /// <summary>Processor name (for diagnostics)</summary>
    Name: string option
}
```

### 3. Implement Module Functions

```fsharp
[<RequireQualifiedAccess>]
module TreeProcessor =
    /// Create empty (identity) processor
    let empty: TreeProcessor = {
        ProcessTree = Ok
        ProcessFile = Ok
        Name = None
    }

    /// Create processor from file processor (applies to each file)
    let fromFileProcessor (name: string option) (proc: VFile -> Result<VFile, VFile>): TreeProcessor =
        {
            ProcessTree = fun tree ->
                // Apply processor to each file in tree
                let rec processTree (t: VFileTree): Result<VFileTree, VFileTree> =
                    let processedContent =
                        t.Content
                        |> List.map (function
                            | File file ->
                                match proc file with
                                | Ok processedFile -> Ok (File processedFile)
                                | Error errorFile -> Error (File errorFile)
                            | Directory subtree ->
                                match processTree subtree with
                                | Ok processedSubtree -> Ok (Directory processedSubtree)
                                | Error errorSubtree -> Error (Directory errorSubtree))

                    // Check if any errors occurred
                    let errors = processedContent |> List.choose (function | Error e -> Some e | _ -> None)

                    if errors.IsEmpty then
                        let successContent = processedContent |> List.choose (function | Ok c -> Some c | _ -> None)
                        Ok { t with Content = successContent }
                    else
                        // Return tree with errors
                        Error { t with Content = errors }

                processTree tree

            ProcessFile = proc
            Name = name
        }

    /// Convert MorphirProcessor to TreeProcessor
    let fromMorphirProcessor (processor: MorphirProcessor): TreeProcessor =
        fromFileProcessor processor.Name processor.Process

    /// Compose two processors sequentially
    let compose (first: TreeProcessor) (second: TreeProcessor): TreeProcessor =
        {
            ProcessTree = fun tree ->
                first.ProcessTree tree
                |> Result.bind second.ProcessTree

            ProcessFile = fun file ->
                first.ProcessFile file
                |> Result.bind second.ProcessFile

            Name =
                match first.Name, second.Name with
                | Some n1, Some n2 -> Some $"{n1} >> {n2}"
                | Some n, None | None, Some n -> Some n
                | None, None -> None
        }

    /// Run multiple processors in parallel (all must succeed)
    let parallel (processors: TreeProcessor list): TreeProcessor =
        {
            ProcessTree = fun tree ->
                let results = processors |> List.map (fun p -> p.ProcessTree tree)

                // Check if all succeeded
                let errors = results |> List.choose (function | Error e -> Some e | _ -> None)

                if errors.IsEmpty then
                    // All succeeded - return last result
                    results |> List.last
                else
                    // Return first error
                    Error (errors |> List.head)

            ProcessFile = fun file ->
                let results = processors |> List.map (fun p -> p.ProcessFile file)

                // Check if all succeeded
                let errors = results |> List.choose (function | Error e -> Some e | _ -> None)

                if errors.IsEmpty then
                    results |> List.last
                else
                    Error (errors |> List.head)

            Name = Some "Parallel processors"
        }

    /// Execute processor on tree
    let run (processor: TreeProcessor) (tree: VFileTree): Result<VFileTree, VFileTree> =
        processor.ProcessTree tree

    /// Execute processor on file list
    let runOnFiles (processor: TreeProcessor) (files: VFile list): Result<VFile list, VFile list> =
        let results = files |> List.map processor.ProcessFile

        let errors = results |> List.choose (function | Error e -> Some e | _ -> None)

        if errors.IsEmpty then
            Ok (results |> List.choose (function | Ok f -> Some f | _ -> None))
        else
            Error errors

    /// Collect diagnostics from all files in tree
    let collectDiagnostics (tree: VFileTree): Map<string, VMessage list> =
        tree
        |> VFileTree.allFiles
        |> List.map (fun file ->
            let path = file.Path |> Option.defaultValue "unknown"
            (path, file.Messages))
        |> Map.ofList

    /// Check if tree has errors
    let hasErrors (tree: VFileTree): bool =
        VFileTree.hasErrors tree

    /// Summarize processing results
    let summarize (tree: VFileTree): ProcessorSummary =
        let stats = VFileTree.statistics tree
        {
            TotalFiles = stats.TotalFiles
            SuccessCount = stats.TotalFiles - stats.ErrorCount
            ErrorCount = stats.ErrorCount
            WarningCount = stats.WarningCount
            ProcessingTime = None
        }
```

### 4. Implement Computation Expression Builder

```fsharp
/// <summary>
/// Computation expression builder for tree pipelines.
/// Enables pipeline { ... } syntax for multi-file processing.
/// </summary>
type TreePipelineBuilder() =
    member _.Yield(_) = TreeProcessor.empty

    [<CustomOperation("parseTree")>]
    member _.ParseTree(proc: TreeProcessor, parser: VFileTree -> Result<VFileTree, VFileTree>) =
        { proc with ProcessTree = parser }

    [<CustomOperation("transformTree")>]
    member _.TransformTree(proc: TreeProcessor, transformer: TreeProcessor) =
        TreeProcessor.compose proc transformer

    [<CustomOperation("mapFiles")>]
    member _.MapFiles(proc: TreeProcessor, mapper: VFile -> Result<VFile, VFile>) =
        let fileProc = TreeProcessor.fromFileProcessor None mapper
        TreeProcessor.compose proc fileProc

    [<CustomOperation("filterFiles")>]
    member _.FilterFiles(proc: TreeProcessor, predicate: VFile -> bool) =
        let filterProc = TreeProcessor.fromFileProcessor (Some "FilterFiles") (fun file ->
            if predicate file then Ok file else Error file)
        TreeProcessor.compose proc filterProc

    [<CustomOperation("aggregateDiagnostics")>]
    member _.AggregateDiagnostics(proc: TreeProcessor) =
        { proc with
            ProcessTree = fun tree ->
                match proc.ProcessTree tree with
                | Ok resultTree ->
                    // Log summary
                    let summary = TreeProcessor.summarize resultTree
                    printfn "Processed %d files (%d errors, %d warnings)"
                        summary.TotalFiles summary.ErrorCount summary.WarningCount
                    Ok resultTree
                | Error errorTree ->
                    Error errorTree
        }

/// <summary>
/// Pipeline builder for tree processing.
/// </summary>
let treePipeline = TreePipelineBuilder()
```

### 5. Write Tests

Create `tests/Morphir.IR.Pipeline.Tests/TreeProcessorTests.fs`:

```fsharp
module Morphir.IR.Pipeline.Tests.TreeProcessorTests

open TUnit.Core
open Morphir.IR.Pipeline

[<Test>]
let ``Empty processor returns tree unchanged`` () =
    let tree = VFileTree.empty
    let result = TreeProcessor.run TreeProcessor.empty tree

    match result with
    | Ok resultTree -> resultTree |> should equal tree
    | Error _ -> failwith "Should not error"

[<Test>]
let ``fromFileProcessor applies to all files`` () =
    let file1 = VFile.create "file1.fs" "content1"
    let file2 = VFile.create "file2.fs" "content2"
    let tree = VFileTree.fromFiles [file1; file2]

    // Processor that adds metadata
    let addMetadata file =
        Ok (VFile.setData "processed" true file)

    let processor = TreeProcessor.fromFileProcessor (Some "AddMetadata") addMetadata

    match TreeProcessor.run processor tree with
    | Ok resultTree ->
        let files = VFileTree.allFiles resultTree
        files |> List.forall (fun f -> VFile.getData "processed" f = Some (box true))
        |> should be true
    | Error _ -> failwith "Should not error"

[<Test>]
let ``compose chains processors`` () =
    let file = VFile.create "test.fs" "content"
    let tree = VFileTree.fromFiles [file]

    let proc1 = TreeProcessor.fromFileProcessor (Some "Proc1") (fun f ->
        Ok (VFile.setData "step1" true f))

    let proc2 = TreeProcessor.fromFileProcessor (Some "Proc2") (fun f ->
        Ok (VFile.setData "step2" true f))

    let composed = TreeProcessor.compose proc1 proc2

    match TreeProcessor.run composed tree with
    | Ok resultTree ->
        let files = VFileTree.allFiles resultTree
        files |> List.head |> VFile.getData "step1" |> should equal (Some (box true))
        files |> List.head |> VFile.getData "step2" |> should equal (Some (box true))
    | Error _ -> failwith "Should not error"

[<Test>]
let ``collectDiagnostics aggregates messages`` () =
    let fileWithError =
        VFile.create "error.fs" "content"
        |> VFile.error "Test error" None

    let fileWithWarning =
        VFile.create "warning.fs" "content"
        |> VFile.warn "Test warning" None

    let tree = VFileTree.fromFiles [fileWithError; fileWithWarning]
    let diagnostics = TreeProcessor.collectDiagnostics tree

    diagnostics |> Map.count |> should equal 2
    diagnostics |> Map.containsKey "error.fs" |> should be true
    diagnostics |> Map.containsKey "warning.fs" |> should be true

[<Test>]
let ``summarize provides processing statistics`` () =
    let fileWithError =
        VFile.create "error.fs" "content"
        |> VFile.error "Error" None

    let fileOk = VFile.create "ok.fs" "content"

    let tree = VFileTree.fromFiles [fileWithError; fileOk]
    let summary = TreeProcessor.summarize tree

    summary.TotalFiles |> should equal 2
    summary.ErrorCount |> should equal 1

[<Test>]
let ``treePipeline computation expression works`` () =
    let file = VFile.create "test.fs" "content"
    let tree = VFileTree.fromFiles [file]

    let pipeline = treePipeline {
        mapFiles (fun f -> Ok (VFile.setData "processed" true f))
        aggregateDiagnostics
    }

    match TreeProcessor.run pipeline tree with
    | Ok resultTree ->
        let files = VFileTree.allFiles resultTree
        files |> List.head |> VFile.getData "processed" |> should equal (Some (box true))
    | Error _ -> failwith "Should not error"
```

### 6. Update Project File

Add to `src/Morphir.IR.Pipeline/Morphir.IR.Pipeline.fsproj`:

```xml
<ItemGroup>
  <Compile Include="File.fs" />
  <Compile Include="FileTree.fs" />
  <Compile Include="TreeProcessor.fs" />  <!-- NEW -->
  <Compile Include="Processor.fs" />
  <!-- ... other files ... -->
</ItemGroup>
```

### 7. Documentation

- [ ] Add XML doc comments to all public types and functions
- [ ] Create usage examples in unified-file-architecture.md
- [ ] Document integration with MorphirProcessor
- [ ] Document computation expression syntax

---

## Usage Examples

### Example 1: Simple File Transformation

```fsharp
open Morphir.IR.Pipeline

// Create processor that adds metadata to all files
let addTimestamp =
    TreeProcessor.fromFileProcessor (Some "AddTimestamp") (fun file ->
        Ok (VFile.setData "timestamp" DateTime.UtcNow file))

// Run on tree
let tree = VFileTree.fromFiles [file1; file2]
match TreeProcessor.run addTimestamp tree with
| Ok resultTree -> printfn "Success!"
| Error errorTree -> printfn "Errors: %A" (TreeProcessor.collectDiagnostics errorTree)
```

### Example 2: Pipeline Syntax

```fsharp
let pipeline = treePipeline {
    // Parse F# files
    parseTree FSharpFrontend.parse

    // Transform to IR
    mapFiles (fun file ->
        // Each file contains F# AST, transform to IR
        IRMapper.mapFile file)

    // Validate IR
    mapFiles IRValidator.validate

    // Aggregate diagnostics
    aggregateDiagnostics
}

let result = TreeProcessor.run pipeline myTree
```

### Example 3: Composition

```fsharp
// Compose multiple processors
let fullPipeline =
    parseProcessor
    |> TreeProcessor.compose transformProcessor
    |> TreeProcessor.compose validateProcessor

let result = TreeProcessor.run fullPipeline tree
```

---

## Success Criteria

- [ ] All types defined and compile successfully
- [ ] All module functions implemented
- [ ] All tests pass (≥80% coverage)
- [ ] Can process VFileTree with file-level transformations
- [ ] Can compose processors
- [ ] Computation expression syntax works
- [ ] Documentation complete
- [ ] Integrates with existing MorphirProcessor

---

## Dependencies

- VFile (exists in Morphir.IR.Pipeline/File.fs)
- VFileTree (new - see related issue)
- MorphirProcessor (exists in Morphir.IR.Pipeline/Processor.fs)

## Blocks

- F# Frontend multi-file parsing (enhanced by this)
- F# Backend tree generation (enhanced by this)

---

## Related Documents

- [Unified File Architecture](./unified-file-architecture.md) - Complete design
- [GitHub Issue: VFileTree](./github-issue-morphir-file-tree.md) - Related issue

---

**Ready for Implementation**: This issue provides complete context and code examples.

**Estimated Effort**: 3-5 developer-days

**Priority**: P1 (Enhances VFileTree functionality, not blocking but highly valuable)
