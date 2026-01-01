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

[<RequireQualifiedAccess>]
module TreeConfig =
    /// <summary>
    /// Creates an empty tree configuration.
    /// </summary>
    let empty: TreeConfig = {
        Name = None
        Version = None
        OutputFormat = None
        Extensions = ImmutableDictionary.Empty
    }

    /// <summary>
    /// Creates a tree configuration with name and version.
    /// </summary>
    /// <param name="name">Project name</param>
    /// <param name="version">Project version</param>
    let create (name: string) (version: string): TreeConfig =
        { empty with Name = Some name; Version = Some version }

[<RequireQualifiedAccess>]
module VFileTree =
    /// <summary>
    /// Creates an empty file tree.
    /// </summary>
    let empty: VFileTree = {
        Path = "."
        Content = []
        Metadata = ImmutableDictionary.Empty
        Config = TreeConfig.empty
    }

    /// <summary>
    /// Creates a file tree with path and config.
    /// </summary>
    /// <param name="path">Root path</param>
    /// <param name="config">Tree configuration</param>
    let create (path: string) (config: TreeConfig): VFileTree =
        { empty with Path = path; Config = config }

    /// <summary>
    /// Adds a file to the tree.
    /// </summary>
    /// <param name="file">File to add</param>
    /// <param name="tree">Tree to add to</param>
    let addFile (file: VFile) (tree: VFileTree): VFileTree =
        { tree with Content = tree.Content @ [ File file ] }

    /// <summary>
    /// Adds a subdirectory to the tree.
    /// </summary>
    /// <param name="subtree">Subdirectory tree</param>
    /// <param name="tree">Tree to add to</param>
    let addDirectory (subtree: VFileTree) (tree: VFileTree): VFileTree =
        { tree with Content = tree.Content @ [ Directory subtree ] }

    /// <summary>
    /// Builds a tree from a list of files (flat structure).
    /// </summary>
    /// <param name="files">List of files</param>
    let fromFiles (files: VFile list): VFileTree =
        files
        |> List.fold (fun tree file -> addFile file tree) empty

    /// <summary>
    /// Gets all files in the tree (flattened, recursive).
    /// </summary>
    /// <param name="tree">Tree to flatten</param>
    let rec allFiles (tree: VFileTree): VFile list =
        tree.Content
        |> List.collect (function
            | File file -> [ file ]
            | Directory subtree -> allFiles subtree)

    /// <summary>
    /// Finds a file by path in the tree.
    /// </summary>
    /// <param name="path">Path to find</param>
    /// <param name="tree">Tree to search</param>
    let rec findFile (path: string) (tree: VFileTree): VFile option =
        tree.Content
        |> List.tryPick (function
            | File file when file.Path = Some path -> Some file
            | Directory subtree -> findFile path subtree
            | _ -> None)

    /// <summary>
    /// Counts total files in the tree (recursive).
    /// </summary>
    /// <param name="tree">Tree to count</param>
    let fileCount (tree: VFileTree): int =
        tree |> allFiles |> List.length

    /// <summary>
    /// Counts total directories in the tree (recursive).
    /// </summary>
    /// <param name="tree">Tree to count</param>
    let rec directoryCount (tree: VFileTree): int =
        tree.Content
        |> List.sumBy (function
            | File _ -> 0
            | Directory subtree -> 1 + directoryCount subtree)

    /// <summary>
    /// Checks if the tree has any error or fatal messages.
    /// </summary>
    /// <param name="tree">Tree to check</param>
    let hasErrors (tree: VFileTree): bool =
        tree
        |> allFiles
        |> List.exists VFile.hasErrors

    /// <summary>
    /// Collects all diagnostic messages from all files in the tree.
    /// </summary>
    /// <param name="tree">Tree to collect from</param>
    let collectMessages (tree: VFileTree): VMessage list =
        tree
        |> allFiles
        |> List.collect (fun file -> file.Messages)

    /// <summary>
    /// Maps a function over all files in the tree.
    /// </summary>
    /// <param name="f">Function to apply</param>
    /// <param name="tree">Tree to transform</param>
    let rec map (f: VFile -> VFile) (tree: VFileTree): VFileTree =
        {
            tree with
                Content =
                    tree.Content
                    |> List.map (function
                        | File file -> File (f file)
                        | Directory subtree -> Directory (map f subtree))
        }

    /// <summary>
    /// Filters files in the tree based on a predicate.
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="tree">Tree to filter</param>
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

    /// <summary>
    /// Updates a specific file by path in the tree.
    /// </summary>
    /// <param name="path">Path of file to update</param>
    /// <param name="f">Update function</param>
    /// <param name="tree">Tree to update</param>
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

    /// <summary>
    /// Flattens the tree to a morphir-elm style FileMap.
    /// </summary>
    /// <param name="tree">Tree to flatten</param>
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

    /// <summary>
    /// Creates a tree from a morphir-elm style FileMap (infers structure from paths).
    /// </summary>
    /// <param name="fileMap">Map of file paths to files</param>
    let fromFileMap (fileMap: Map<string, VFile>): VFileTree =
        // For now, return flat tree. Future enhancement: group by directory path segments
        {
            Path = "."
            Content = fileMap |> Map.toList |> List.map (snd >> File)
            Metadata = ImmutableDictionary.Empty
            Config = TreeConfig.empty
        }

    /// <summary>
    /// Flattens the tree to a simple string map (morphir-elm exact equivalent).
    /// </summary>
    /// <param name="tree">Tree to flatten</param>
    let toStringMap (tree: VFileTree): Map<string, string> =
        tree
        |> toFileMap
        |> Map.map (fun _ file ->
            match file.Content with
            | Some content -> unbox<string> content
            | None -> "")

    /// <summary>
    /// Sets tree-level metadata.
    /// </summary>
    /// <param name="key">Metadata key</param>
    /// <param name="value">Metadata value</param>
    /// <param name="tree">Tree to update</param>
    let setMetadata (key: string) (value: obj) (tree: VFileTree): VFileTree =
        { tree with Metadata = tree.Metadata.SetItem(key, value) }

    /// <summary>
    /// Gets tree-level metadata.
    /// </summary>
    /// <param name="key">Metadata key</param>
    /// <param name="tree">Tree to query</param>
    let getMetadata (key: string) (tree: VFileTree): obj option =
        match tree.Metadata.TryGetValue(key) with
        | true, value -> Some value
        | false, _ -> None

    /// <summary>
    /// Gets statistics about the tree.
    /// </summary>
    /// <param name="tree">Tree to analyze</param>
    let statistics (tree: VFileTree): TreeStatistics =
        let messages = collectMessages tree

        {
            TotalFiles = fileCount tree
            TotalDirectories = directoryCount tree
            ErrorCount = messages |> List.filter (fun m -> m.Severity = Error || m.Severity = Fatal) |> List.length
            WarningCount = messages |> List.filter (fun m -> m.Severity = Warning) |> List.length
            InfoCount = messages |> List.filter (fun m -> m.Severity = Info) |> List.length
        }

    /// <summary>
    /// Writes the tree to disk.
    /// </summary>
    /// <param name="outputDir">Output directory</param>
    /// <param name="tree">Tree to write</param>
    let writeToDisk (outputDir: string) (tree: VFileTree): Result<unit, string> =
        try
            tree
            |> toFileMap
            |> Map.iter (fun relPath file ->
                let fullPath = System.IO.Path.Combine(outputDir, relPath)
                let dir = System.IO.Path.GetDirectoryName(fullPath)
                System.IO.Directory.CreateDirectory(dir) |> ignore
                let content =
                    match file.Content with
                    | Some c -> c.ToString()
                    | None -> ""
                System.IO.File.WriteAllText(fullPath, content))
            Result.Ok ()
        with ex ->
            Result.Error ex.Message
