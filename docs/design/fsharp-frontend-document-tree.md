# F# Frontend: DocumentTree Architecture

This document proposes an enhanced architecture inspired by:
- **Laika's DocumentTree** (Scala): Hierarchical document structure with metadata
- **VFile from unifiedJS**: File abstraction with messages and history

## Problem Statement

The current flat design loses important structural information:

```fsharp
// Current: Flat list of files
type SourceInput =
    | MultipleFiles of files: (string * string) list  // No structure!

// Result: Flat map
modules: Map<Path, Module.Definition>  // Lost: directory hierarchy, metadata, diagnostics
```

**What we lose**:
- Directory structure (helpful for organizing large projects)
- Per-file metadata (parse time, diagnostics, source location)
- File history (transformations, imports, dependencies)
- Lazy loading capabilities
- Navigation (parent/child relationships)

---

## Proposed: VFile Abstraction

### 1. VFile Type (unifiedJS-inspired)

```fsharp
namespace Morphir.Frontends.FSharp

/// Virtual file abstraction (inspired by unifiedJS VFile)
type VFile = {
    /// Absolute or relative path
    Path: string

    /// File content (source code)
    Value: string

    /// Arbitrary metadata (extensible)
    Data: Map<string, obj>

    /// Diagnostic messages (errors, warnings, info)
    Messages: VFileMessage list

    /// File history (transformations, imports)
    History: string list

    /// Parent directory (if part of tree)
    Parent: VFile option
}

and VFileMessage = {
    Level: MessageLevel
    Message: string
    Location: SourceLocation option
    Code: string option  // e.g., "MORPHIR001"
    Suggestion: string option
}

and MessageLevel =
    | Info
    | Warning
    | Error

and SourceLocation = {
    File: string
    Line: int
    Column: int
    Length: int
}

module VFile =
    /// Create VFile from file path (reads from disk)
    let fromPath (path: string) : VFile =
        {
            Path = path
            Value = System.IO.File.ReadAllText(path)
            Data = Map.empty
            Messages = []
            History = [path]
            Parent = None
        }

    /// Create VFile from in-memory source
    let fromMemory (fileName: string) (source: string) : VFile =
        {
            Path = fileName
            Value = source
            Data = Map.empty
            Messages = []
            History = [fileName]
            Parent = None
        }

    /// Add metadata
    let withData (key: string) (value: obj) (vfile: VFile) : VFile =
        { vfile with Data = vfile.Data |> Map.add key value }

    /// Add message
    let addMessage (msg: VFileMessage) (vfile: VFile) : VFile =
        { vfile with Messages = msg :: vfile.Messages }

    /// Add to history
    let addHistory (entry: string) (vfile: VFile) : VFile =
        { vfile with History = entry :: vfile.History }

    /// Get all errors
    let errors (vfile: VFile) : VFileMessage list =
        vfile.Messages |> List.filter (fun m -> m.Level = Error)

    /// Get all warnings
    let warnings (vfile: VFile) : VFileMessage list =
        vfile.Messages |> List.filter (fun m -> m.Level = Warning)

    /// Has errors?
    let hasErrors (vfile: VFile) : bool =
        vfile |> errors |> List.isEmpty |> not
```

---

## Proposed: DocumentTree Type

### 2. DocumentTree (Laika-inspired)

```fsharp
namespace Morphir.Frontends.FSharp

/// Hierarchical document tree (inspired by Laika)
type DocumentTree = {
    /// Path to this tree node (directory or project)
    Path: string

    /// Tree content (documents and subtrees)
    Content: TreeContent list

    /// Tree-level metadata
    Metadata: Map<string, obj>

    /// Configuration (compile options, etc.)
    Config: TreeConfig
}

and TreeContent =
    /// Single document (F# source file)
    | Document of doc: FSharpDocument
    /// Subtree (subdirectory)
    | Subtree of tree: DocumentTree

and FSharpDocument = {
    /// VFile (file abstraction)
    VFile: VFile

    /// Parsed AST (lazy, only parsed on demand)
    ParsedAST: Lazy<Result<Parser.ParseResult, string>>

    /// Generated Morphir IR (lazy, only generated on demand)
    GeneratedIR: Lazy<Result<Module.Definition<unit>, string>>

    /// Dependencies (other documents this depends on)
    Dependencies: Set<string>

    /// Document-level metadata
    Metadata: Map<string, obj>
}

and TreeConfig = {
    /// Target framework
    TargetFramework: string option

    /// NuGet package references
    PackageReferences: Map<string, string>  // name -> version

    /// Compile-time constants
    DefineConstants: string list

    /// F# language version
    LanguageVersion: string option
}

module DocumentTree =
    /// Create tree from .fsproj file
    let fromProject (projectPath: string) : DocumentTree =
        // 1. Parse .fsproj
        let projectDir = System.IO.Path.GetDirectoryName(projectPath)
        let compileItems = ProjectParser.getCompileItems(projectPath)

        // 2. Build tree structure
        let documents =
            compileItems
            |> List.map (fun filePath ->
                let vfile = VFile.fromPath filePath
                let doc = {
                    VFile = vfile
                    ParsedAST = lazy (Parser.parseSource (FilePath filePath))
                    GeneratedIR = lazy (Error "Not yet generated")
                    Dependencies = Set.empty
                    Metadata = Map.empty
                }
                Document doc)

        {
            Path = projectDir
            Content = documents
            Metadata = Map.empty
            Config = {
                TargetFramework = Some "net10.0"
                PackageReferences = Map.empty
                DefineConstants = []
                LanguageVersion = Some "9.0"
            }
        }

    /// Create tree from directory (finds all .fs files)
    let fromDirectory (dirPath: string) : DocumentTree =
        let files =
            System.IO.Directory.GetFiles(dirPath, "*.fs", SearchOption.AllDirectories)
            |> Array.toList

        // Group files by subdirectory to create subtrees
        let grouped =
            files
            |> List.groupBy (fun f -> System.IO.Path.GetDirectoryName(f))

        // Recursively build tree
        buildTreeRecursive dirPath grouped

    /// Create tree from multiple files (flat)
    let fromFiles (files: string list) : DocumentTree =
        let documents =
            files
            |> List.map (fun filePath ->
                let vfile = VFile.fromPath filePath
                let doc = {
                    VFile = vfile
                    ParsedAST = lazy (Parser.parseSource (FilePath filePath))
                    GeneratedIR = lazy (Error "Not yet generated")
                    Dependencies = Set.empty
                    Metadata = Map.empty
                }
                Document doc)

        {
            Path = "."
            Content = documents
            Metadata = Map.empty
            Config = {
                TargetFramework = None
                PackageReferences = Map.empty
                DefineConstants = []
                LanguageVersion = None
            }
        }

    /// Get all documents (flattened)
    let rec allDocuments (tree: DocumentTree) : FSharpDocument list =
        tree.Content
        |> List.collect (function
            | Document doc -> [doc]
            | Subtree subtree -> allDocuments subtree)

    /// Get all VFiles (flattened)
    let allVFiles (tree: DocumentTree) : VFile list =
        tree |> allDocuments |> List.map (fun d -> d.VFile)

    /// Find document by path
    let rec findDocument (path: string) (tree: DocumentTree) : FSharpDocument option =
        tree.Content
        |> List.tryPick (function
            | Document doc when doc.VFile.Path = path -> Some doc
            | Subtree subtree -> findDocument path subtree
            | _ -> None)

    /// Parse all documents (triggers lazy parsing)
    let parseAll (tree: DocumentTree) : DocumentTree =
        let parseDoc (doc: FSharpDocument) =
            // Force lazy parsing
            let _ = doc.ParsedAST.Value
            doc

        let rec parseTree (tree: DocumentTree) =
            let newContent =
                tree.Content
                |> List.map (function
                    | Document doc -> Document (parseDoc doc)
                    | Subtree subtree -> Subtree (parseTree subtree))
            { tree with Content = newContent }

        parseTree tree

    /// Generate IR for all documents (triggers lazy generation)
    let generateIRAll (tree: DocumentTree) : Distribution =
        // 1. Parse all documents
        let parsedTree = parseAll tree

        // 2. Resolve dependencies
        let docs = allDocuments parsedTree
        let resolvedDocs = DependencyResolver.resolveDependencies docs

        // 3. Generate IR for each document
        let modules =
            resolvedDocs
            |> List.map (fun doc ->
                // Force lazy IR generation
                match doc.GeneratedIR.Value with
                | Ok moduleDef ->
                    let modulePath = Path.fromString (System.IO.Path.GetFileNameWithoutExtension doc.VFile.Path)
                    Some (modulePath, moduleDef)
                | Error _ -> None)
            |> List.choose id
            |> Map.ofList

        // 4. Create Distribution
        let packageName = Path.fromString (System.IO.Path.GetFileName tree.Path)
        Distribution.Library (packageName, Map.empty, Package.Definition.create modules)

    /// Get all errors from all documents
    let allErrors (tree: DocumentTree) : (string * VFileMessage list) list =
        tree
        |> allVFiles
        |> List.map (fun vfile -> (vfile.Path, VFile.errors vfile))
        |> List.filter (fun (_, errors) -> not (List.isEmpty errors))

    /// Get tree statistics
    let stats (tree: DocumentTree) : TreeStats =
        let docs = allDocuments tree
        {
            TotalFiles = docs.Length
            TotalLines = docs |> List.sumBy (fun d -> d.VFile.Value.Split('\n').Length)
            TotalErrors = tree |> allErrors |> List.sumBy (fun (_, e) -> e.Length)
            TotalWarnings = tree |> allVFiles |> List.sumBy (fun vf -> VFile.warnings vf |> List.length)
        }

and TreeStats = {
    TotalFiles: int
    TotalLines: int
    TotalErrors: int
    TotalWarnings: int
}
```

---

## Enhanced FrontendAPI

### 3. Updated API with VFile and DocumentTree

```fsharp
module FrontendAPI =
    open Morphir.Models.IR.Classic

    /// Parse from VFile
    let parseVFile (vfile: VFile) : Result<Distribution, VFile> =
        match Parser.parseInMemory vfile.Path vfile.Value with
        | Ok parseResult ->
            // Generate IR
            let moduleDef = ModuleMapper.mapModule parseResult.CheckResults parseResult.ParsedInput
            let distribution = (* ... generate distribution ... *)
            Ok distribution

        | Error err ->
            // Add error to VFile messages
            let errorMsg = {
                Level = Error
                Message = err
                Location = None
                Code = None
                Suggestion = None
            }
            let vfileWithError = VFile.addMessage errorMsg vfile
            Error vfileWithError

    /// Parse from DocumentTree
    let parseDocumentTree (tree: DocumentTree) : Result<Distribution, DocumentTree> =
        try
            let distribution = DocumentTree.generateIRAll tree

            // Check for errors
            let errors = DocumentTree.allErrors tree
            if List.isEmpty errors then
                Ok distribution
            else
                // Return tree with errors
                Error tree
        with ex ->
            Error tree

    /// Parse single in-memory source (convenience)
    let parseInMemory (fileName: string) (source: string) : Result<Distribution, VFile> =
        let vfile = VFile.fromMemory fileName source
        parseVFile vfile

    /// Parse multiple files (convenience)
    let parseFiles (filePaths: string list) : Result<Distribution, DocumentTree> =
        let tree = DocumentTree.fromFiles filePaths
        parseDocumentTree tree

    /// Parse .fsproj project (convenience)
    let parseProject (projectPath: string) : Result<Distribution, DocumentTree> =
        let tree = DocumentTree.fromProject projectPath
        parseDocumentTree tree
```

---

## Benefits of DocumentTree Approach

### 1. **Preserves Structure**
```fsharp
// Before: Flat
MultipleFiles [
    "src/Types.fs"
    "src/Domain/Orders.fs"
    "src/Domain/Customers.fs"
]

// After: Hierarchical
DocumentTree {
    Path = "src/"
    Content = [
        Document { VFile = { Path = "Types.fs"; ... } }
        Subtree {
            Path = "src/Domain/"
            Content = [
                Document { VFile = { Path = "Orders.fs"; ... } }
                Document { VFile = { Path = "Customers.fs"; ... } }
            ]
        }
    ]
}
```

### 2. **Rich Metadata**
```fsharp
// Attach metadata to files
let vfile =
    VFile.fromPath "Calculator.fs"
    |> VFile.withData "parseTime" (TimeSpan.FromMilliseconds 42.0)
    |> VFile.withData "hash" "abc123..."
    |> VFile.withData "author" "John Doe"

// Attach metadata to tree
let tree =
    DocumentTree.fromProject "MyProject.fsproj"
    |> DocumentTree.withMetadata "version" "1.0.0"
    |> DocumentTree.withMetadata "buildId" "build-12345"
```

### 3. **Lazy Parsing**
```fsharp
// Only parse when needed
let tree = DocumentTree.fromDirectory "src/"  // No parsing yet!

// Find specific document
let doc = DocumentTree.findDocument "src/Calculator.fs" tree

// Force parsing for this document only
let parseResult = doc.ParsedAST.Value  // Lazy.Value triggers parsing
```

### 4. **Diagnostic Tracking**
```fsharp
// Parse with diagnostics
let tree = DocumentTree.fromProject "MyProject.fsproj"
let parsedTree = DocumentTree.parseAll tree

// Get all errors
let errors = DocumentTree.allErrors parsedTree
errors |> List.iter (fun (filePath, msgs) ->
    printfn "File: %s" filePath
    msgs |> List.iter (fun msg ->
        printfn "  %s:%d:%d: %s"
            msg.Location.Value.File
            msg.Location.Value.Line
            msg.Location.Value.Column
            msg.Message))
```

### 5. **Transformation Pipeline**
```fsharp
// Pipeline: VFile → ParsedAST → IR → F# (round-trip)
let vfile =
    VFile.fromPath "Calculator.fs"
    |> VFile.addHistory "Read from disk"

let parseResult = Parser.parseSource (FilePath vfile.Path)
let vfile2 = vfile |> VFile.addHistory "Parsed to AST"

let ir = (* generate IR *)
let vfile3 = vfile2 |> VFile.addHistory "Generated IR"

let generatedFSharp = FSharpBackend.generate ir
let vfile4 = vfile3 |> VFile.addHistory "Generated F# from IR"

// Track full history
vfile4.History
// ["Generated F# from IR"; "Generated IR"; "Parsed to AST"; "Read from disk"; "Calculator.fs"]
```

---

## Integration with try-morphir

### Example: try-morphir with VFile

```fsharp
// try-morphir web service
[<HttpPost("/api/fsharp/parse")>]
let parseHandler (request: ParseRequest) : Task<IResult> =
    task {
        // Create VFile from user input
        let vfile =
            VFile.fromMemory "UserCode.fs" request.SourceCode
            |> VFile.withData "userId" request.UserId
            |> VFile.withData "sessionId" request.SessionId
            |> VFile.withData "timestamp" DateTime.UtcNow

        // Parse
        match FrontendAPI.parseVFile vfile with
        | Ok distribution ->
            // Success
            let json = Distribution.toJson distribution
            return Results.Ok({|
                Success = true
                IR = json
                Messages = vfile.Messages  // Include warnings/info
            |})

        | Error vfileWithErrors ->
            // Parse failed, return errors with source locations
            let errors =
                vfileWithErrors
                |> VFile.errors
                |> List.map (fun msg -> {|
                    Level = string msg.Level
                    Message = msg.Message
                    Line = msg.Location |> Option.map (fun l -> l.Line)
                    Column = msg.Location |> Option.map (fun l -> l.Column)
                    Code = msg.Code
                    Suggestion = msg.Suggestion
                |})

            return Results.BadRequest({|
                Success = false
                Errors = errors
            |})
    }
```

---

## CLI Integration

### Example: CLI with DocumentTree

```bash
# Parse project and show tree structure
$ morphir fsharp parse MyProject.fsproj --show-tree
📁 src/
  📄 Types.fs (142 lines, 0 errors, 0 warnings)
  📁 Domain/
    📄 Orders.fs (256 lines, 0 errors, 1 warning)
    📄 Customers.fs (189 lines, 0 errors, 0 warnings)
  📄 Calculator.fs (98 lines, 0 errors, 0 warnings)

Total: 4 files, 685 lines, 0 errors, 1 warning

# Parse with diagnostics
$ morphir fsharp parse src/ --verbose
✓ Parsing src/Types.fs (42ms)
✓ Parsing src/Domain/Orders.fs (67ms)
⚠ Warning in src/Domain/Orders.fs:42:9: Unused value 'temp'
✓ Parsing src/Domain/Customers.fs (53ms)
✓ Parsing src/Calculator.fs (31ms)
✓ Resolving dependencies (12ms)
✓ Generating IR (89ms)
✓ Generated morphir-ir.json (2.3MB)

Total time: 294ms
```

---

## Comparison: Current vs Enhanced

| Feature | Current (Flat) | Enhanced (DocumentTree) |
|---------|---------------|------------------------|
| **Structure** | Flat list | Hierarchical tree |
| **Metadata** | None | Per-file and per-tree |
| **Diagnostics** | String list | Rich VFileMessage with locations |
| **Lazy Loading** | No | Yes (Lazy<T>) |
| **History** | No | Yes (transformation tracking) |
| **Navigation** | No | Yes (parent/child, find) |
| **Statistics** | Manual | Built-in (TreeStats) |
| **Error Reporting** | `string list` | VFile with messages |
| **try-morphir** | Supported | Better (metadata, messages) |

---

## Migration Path

### Phase 1: Introduce VFile (M1)
- Add `VFile` type
- Update Parser to return `VFile` with diagnostics
- Keep existing flat `SourceInput` API

### Phase 2: Add DocumentTree (M3)
- Add `DocumentTree` type
- Update `parseProject` to use DocumentTree internally
- Keep existing flat result for backward compatibility

### Phase 3: Expose DocumentTree (M4)
- Add new API: `parseDocumentTreeAdvanced`
- Update CLI to support `--show-tree`
- Update try-morphir to use VFile

### Phase 4: Deprecate Flat API (Future)
- Mark flat APIs as `[<Obsolete>]`
- Migrate all consumers to DocumentTree
- Remove flat APIs in next major version

---

## Recommendations

1. **Adopt VFile immediately (M0-M1)**: Simple, low overhead, big diagnostic benefit
2. **Add DocumentTree gradually (M3)**: Start with internal use, expose in M4
3. **Keep flat API for simple cases**: `parseInMemory` doesn't need DocumentTree
4. **Use DocumentTree for complex scenarios**: Multi-file, .fsproj, CLI

### Suggested API Surface

```fsharp
// Simple (flat, for try-morphir)
FrontendAPI.parseInMemory : string -> string -> Result<Distribution, VFile>

// Advanced (tree, for projects)
FrontendAPI.parseProject : string -> Result<Distribution, DocumentTree>
FrontendAPI.parseDocumentTree : DocumentTree -> Result<Distribution, DocumentTree>

// Low-level (for power users)
VFile.fromPath : string -> VFile
DocumentTree.fromProject : string -> DocumentTree
DocumentTree.parseAll : DocumentTree -> DocumentTree
DocumentTree.generateIRAll : DocumentTree -> Distribution
```

---

## References

- [Laika DocumentTree](https://typelevel.org/Laika/latest/04-customizing-laika/04-document-ast.html#the-documenttree-type)
- [unifiedJS VFile](https://github.com/vfile/vfile)
- [F# Frontend PRD](./PRD-fsharp-frontend.md)
- [F# Frontend Usage Scenarios](./fsharp-frontend-usage-scenarios.md)

---

**Document Status**: Proposal
**Last Updated**: 2025-12-31
**Next Steps**: Review with team, decide on adoption timeline
