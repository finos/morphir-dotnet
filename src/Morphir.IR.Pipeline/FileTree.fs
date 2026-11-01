namespace Morphir.IR.Pipeline

open System
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
    /// <summary>A file. Its path is relative to the directory that contains it.</summary>
    | File of file: VFile
    /// <summary>A subdirectory. Its path is relative to the directory that contains it.</summary>
    | Directory of tree: VFileTree

/// <summary>
/// Represents a hierarchical tree of Morphir files.
/// This is the PRIMARY abstraction for multi-file operations in morphir-dotnet.
/// </summary>
/// <remarks>
/// A file's location is the path of each directory above it joined to its own path, not counting
/// the root tree's path. For example, Model.fs in a Morphir directory of a tree rooted at
/// "generated" has the location "Morphir/Model.fs". Locations use forward slashes on every platform.
/// </remarks>
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
    /// <summary>Total number of files, including those in subdirectories</summary>
    TotalFiles: int
    /// <summary>Total number of directories below the root</summary>
    TotalDirectories: int
    /// <summary>Total number of error and fatal messages</summary>
    ErrorCount: int
    /// <summary>Total number of warning messages</summary>
    WarningCount: int
    /// <summary>Total number of informational messages</summary>
    InfoCount: int
}

/// <summary>
/// A reason a tree cannot be written safely.
/// </summary>
[<RequireQualifiedAccess>]
type TreeWriteProblem =
    /// <summary>The file has no usable path, so it has no location to write to.</summary>
    | FileWithoutPath of file: VFile
    /// <summary>The location is rooted or contains "..", so it is outside the output directory.</summary>
    | PathOutsideOutputDirectory of location: string
    /// <summary>More than one file has this location, so writing would lose a file.</summary>
    | DuplicateLocation of location: string

/// <summary>
/// The reason <c>VFileTree.writeToDisk</c> failed.
/// </summary>
[<RequireQualifiedAccess>]
type TreeWriteError =
    /// <summary>The tree failed validation and nothing was written. Lists every problem found.</summary>
    | InvalidTree of problems: TreeWriteProblem list
    /// <summary>Writing failed. Files written before the failure remain on disk.</summary>
    | WriteFailed of outputDir: string * cause: exn

/// <summary>
/// The reason <c>VFileTree.loadFromDisk</c> failed.
/// </summary>
[<RequireQualifiedAccess>]
type TreeLoadError =
    /// <summary>The path does not exist or is not a directory.</summary>
    | DirectoryNotFound of path: string
    /// <summary>Reading the directory or a file in it failed.</summary>
    | LoadFailed of path: string * cause: exn

/// <summary>
/// Functions for working with tree write errors.
/// </summary>
[<RequireQualifiedAccess>]
module TreeWriteError =
    /// <summary>
    /// Describes an error in text suitable for showing to a person.
    /// </summary>
    /// <param name="error">The error to describe</param>
    let describe (error: TreeWriteError): string =
        let describeProblem (problem: TreeWriteProblem): string =
            match problem with
            | TreeWriteProblem.FileWithoutPath _ -> "a file has no path"
            | TreeWriteProblem.PathOutsideOutputDirectory location ->
                "path is outside the output directory: " + location
            | TreeWriteProblem.DuplicateLocation location -> "more than one file has the path: " + location

        match error with
        | TreeWriteError.InvalidTree problems -> String.Join("; ", problems |> List.map describeProblem)
        | TreeWriteError.WriteFailed (outputDir, cause) -> "could not write to " + outputDir + ": " + cause.Message

/// <summary>
/// Functions for working with tree load errors.
/// </summary>
[<RequireQualifiedAccess>]
module TreeLoadError =
    /// <summary>
    /// Describes an error in text suitable for showing to a person.
    /// </summary>
    /// <param name="error">The error to describe</param>
    let describe (error: TreeLoadError): string =
        match error with
        | TreeLoadError.DirectoryNotFound path -> "directory not found: " + path
        | TreeLoadError.LoadFailed (path, cause) -> "could not load " + path + ": " + cause.Message

/// <summary>
/// Functions for creating tree configurations.
/// </summary>
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

/// <summary>
/// Functions for creating, querying, transforming, converting and writing file trees.
/// </summary>
[<RequireQualifiedAccess>]
module VFileTree =
    /// <summary>True for a path anchored outside the tree, such as /a, \a or C:\a.</summary>
    let private isRooted (path: string): bool =
        path.StartsWith '/' || path.StartsWith '\\' || (path.Length > 1 && path[1] = ':')

    /// <summary>Splits a path on either separator, dropping empty and "." segments.</summary>
    let private pathSegments (path: string): string list =
        path.Split([| '/'; '\\' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.filter (fun segment -> segment <> ".")
        |> List.ofArray

    /// <summary>Joins a path to a normalized base path. A rooted path ignores the base.</summary>
    let private resolvePath (basePath: string) (path: string): string =
        let relative = String.Join("/", pathSegments path)

        if isRooted path then path.Replace('\\', '/')
        elif basePath = "" then relative
        elif relative = "" then basePath
        else basePath + "/" + relative

    /// <summary>The location of a file in a directory, if the file has a usable path.</summary>
    let private fileLocation (basePath: string) (file: VFile): string option =
        file.Path
        |> Option.filter (fun path -> not (List.isEmpty (pathSegments path)) || isRooted path)
        |> Option.map (resolvePath basePath)

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

    /// <summary>Every file that has a location, paired with that location, in tree order.</summary>
    let private locatedFiles (tree: VFileTree): (string * VFile) list =
        let rec collect (basePath: string) (content: TreeContent list) =
            content
            |> List.collect (function
                | File file ->
                    fileLocation basePath file
                    |> Option.map (fun location -> location, file)
                    |> Option.toList
                | Directory subtree -> collect (resolvePath basePath subtree.Path) subtree.Content)

        collect "" tree.Content

    /// <summary>Finds a file by its exact location, or else by the first matching VFile path.</summary>
    let private tryLocate (path: string) (tree: VFileTree): (string * VFile) option =
        let located = locatedFiles tree
        let wanted = resolvePath "" path

        located
        |> List.tryFind (fun (location, _) -> location = wanted)
        |> Option.orElseWith (fun () -> located |> List.tryFind (fun (_, file) -> file.Path = Some path))

    /// <summary>
    /// Finds a file by path in the tree. The path can be the file's location, such as
    /// "subdir/a.fs" with either separator. If no file has that location, the first file whose own
    /// VFile path equals the given path is returned.
    /// </summary>
    /// <param name="path">Path to find</param>
    /// <param name="tree">Tree to search</param>
    let findFile (path: string) (tree: VFileTree): VFile option =
        tryLocate path tree |> Option.map snd

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
    /// Filters files in the tree based on a predicate. Directories are kept, even when no files
    /// remain in them.
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
    /// Updates the file that <see cref="findFile"/> would return for the same path. The tree is
    /// unchanged if there is no such file.
    /// </summary>
    /// <param name="path">Path of file to update</param>
    /// <param name="f">Update function</param>
    /// <param name="tree">Tree to update</param>
    let updateFile (path: string) (f: VFile -> VFile) (tree: VFileTree): VFileTree =
        match tryLocate path tree with
        | None -> tree
        | Some (target, _) ->
            let rec update (basePath: string) (current: VFileTree): VFileTree =
                {
                    current with
                        Content =
                            current.Content
                            |> List.map (function
                                | File file when fileLocation basePath file = Some target -> File (f file)
                                | Directory subtree -> Directory (update (resolvePath basePath subtree.Path) subtree)
                                | other -> other)
                }

            update "" tree

    /// <summary>
    /// Flattens the tree to a morphir-elm style FileMap, keyed by file location. Keys are relative
    /// to the tree root and use forward slashes. A file with no path has no key and is left out.
    /// If two files have the same location, the last one in the tree is kept.
    /// </summary>
    /// <param name="tree">Tree to flatten</param>
    let toFileMap (tree: VFileTree): Map<string, VFile> =
        tree |> locatedFiles |> Map.ofList

    /// <summary>
    /// Creates a tree from a morphir-elm style FileMap, inferring directories from the keys.
    /// The key decides where a file goes. The file's path becomes its name within that directory,
    /// and a changed path is added to the file's history. A rooted key stays at the top of the
    /// tree with the key as its path, and a key that names no location leaves the file unchanged.
    /// </summary>
    /// <param name="fileMap">Map of file paths to files</param>
    let fromFileMap (fileMap: Map<string, VFile>): VFileTree =
        let relocate (path: string) (file: VFile): VFile =
            if file.Path = Some path then
                file
            else
                { file with Path = Some path; History = file.History @ [ path ] }

        let rec build (entries: (string list * VFile) list): TreeContent list =
            let leaves, nested =
                entries |> List.partition (fun (segments, _) -> segments.Length = 1)

            let files =
                leaves |> List.map (fun (segments, file) -> File (relocate segments.Head file))

            let directories =
                nested
                |> List.groupBy (fun (segments, _) -> segments.Head)
                |> List.map (fun (name, members) ->
                    Directory {
                        empty with
                            Path = name
                            Content = build (members |> List.map (fun (segments, file) -> segments.Tail, file))
                    })

            files @ directories

        let topLevel, inTree =
            fileMap
            |> Map.toList
            |> List.partition (fun (key, _) -> isRooted key || List.isEmpty (pathSegments key))

        let topLevelFiles =
            topLevel
            |> List.map (fun (key, file) ->
                if isRooted key then File (relocate (resolvePath "" key) file) else File file)

        let inferred =
            inTree |> List.map (fun (key, file) -> pathSegments key, file) |> build

        { empty with Content = topLevelFiles @ inferred }

    /// <summary>The text of a file: its content's string form, or empty when it has no content.</summary>
    let private contentText (file: VFile): string =
        file.Content
        |> Option.map (fun content -> content.ToString())
        |> Option.defaultValue ""

    /// <summary>
    /// Flattens the tree to a simple string map (morphir-elm exact equivalent), with the same keys
    /// as <see cref="toFileMap"/>. Non-string content is converted using ToString(), and a file
    /// with no content maps to an empty string.
    /// </summary>
    /// <param name="tree">Tree to flatten</param>
    let toStringMap (tree: VFileTree): Map<string, string> =
        tree
        |> toFileMap
        |> Map.map (fun _ file -> contentText file)

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

    /// <summary>The reasons a tree cannot be written safely. Empty when it can.</summary>
    let private writeProblems (tree: VFileTree): TreeWriteProblem list =
        let located = locatedFiles tree

        let pathless =
            tree
            |> allFiles
            |> List.filter (fun file -> Option.isNone (fileLocation "" file))
            |> List.map TreeWriteProblem.FileWithoutPath

        let outside =
            located
            |> List.map fst
            |> List.filter (fun location -> isRooted location || List.contains ".." (pathSegments location))
            |> List.map TreeWriteProblem.PathOutsideOutputDirectory

        let duplicates =
            located
            |> List.countBy fst
            |> List.filter (fun (_, count) -> count > 1)
            |> List.map (fst >> TreeWriteProblem.DuplicateLocation)

        pathless @ outside @ duplicates

    /// <summary>
    /// Writes the tree's files under an output directory, each at its location. The root tree's
    /// path is not part of the output path. Directories are created only as needed for files.
    /// </summary>
    /// <remarks>
    /// Nothing is written if any file has no path, if a location is rooted or contains "..", or if
    /// two files share a location. A failure while writing can leave earlier files on disk.
    /// </remarks>
    /// <param name="outputDir">Output directory</param>
    /// <param name="tree">Tree to write</param>
    /// <returns>Ok when every file was written, otherwise the reason it failed.</returns>
    let writeToDisk (outputDir: string) (tree: VFileTree): Result<unit, TreeWriteError> =
        match writeProblems tree with
        | [] ->
            try
                for location, file in locatedFiles tree do
                    let fullPath = System.IO.Path.Combine(outputDir, location)
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)) |> ignore
                    System.IO.File.WriteAllText(fullPath, contentText file)

                Result.Ok ()
            with ex ->
                Result.Error (TreeWriteError.WriteFailed (outputDir, ex))
        | problems -> Result.Error (TreeWriteError.InvalidTree problems)

    /// <summary>
    /// Loads a directory into a tree, reading every file as text. The tree's path is the directory
    /// given, and each file and subdirectory path is its own name. Each directory lists its files
    /// first and then its subdirectories, both sorted by name.
    /// </summary>
    /// <remarks>
    /// Files load as strings, with UTF-8 assumed unless the file has a byte order mark, so this
    /// does not preserve binary files. Hidden files and directories such as .git are included, and
    /// empty directories are kept. Symbolic links to directories are skipped because they can form
    /// cycles. Symbolic links to files are read.
    /// </remarks>
    /// <param name="rootDir">Directory to load</param>
    /// <returns>The tree, or the reason the directory could not be loaded.</returns>
    let loadFromDisk (rootDir: string): Result<VFileTree, TreeLoadError> =
        let rec loadContent (directory: System.IO.DirectoryInfo): TreeContent list =
            let files =
                directory.EnumerateFiles()
                |> Seq.sortBy (fun file -> file.Name)
                |> Seq.map (fun file -> File (VFile.create file.Name (System.IO.File.ReadAllText file.FullName)))
                |> List.ofSeq

            let directories =
                directory.EnumerateDirectories()
                |> Seq.filter (fun subdirectory ->
                    not (subdirectory.Attributes.HasFlag System.IO.FileAttributes.ReparsePoint))
                |> Seq.sortBy (fun subdirectory -> subdirectory.Name)
                |> Seq.map (fun subdirectory ->
                    Directory { empty with Path = subdirectory.Name; Content = loadContent subdirectory })
                |> List.ofSeq

            files @ directories

        try
            if System.IO.Directory.Exists rootDir then
                Result.Ok { empty with Path = rootDir; Content = loadContent (System.IO.DirectoryInfo rootDir) }
            else
                Result.Error (TreeLoadError.DirectoryNotFound rootDir)
        with ex ->
            Result.Error (TreeLoadError.LoadFailed (rootDir, ex))
