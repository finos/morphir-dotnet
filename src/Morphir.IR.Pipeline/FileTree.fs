namespace Morphir.IR.Pipeline

open System

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
    Extensions: PropertyBag
}

/// <summary>
/// Content of a file tree node (file or directory).
/// </summary>
type TreeNode =
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
    Content: TreeNode list
    /// <summary>Tree-level metadata</summary>
    Metadata: PropertyBag
    /// <summary>Diagnostics that belong to the tree rather than to one of its files</summary>
    Messages: VMessage list
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
    /// <summary>
    /// More than one file has this location, compared case-insensitively on every platform so that
    /// output is portable, or a file's location is the manifest's.
    /// </summary>
    | DuplicateLocation of location: string
    /// <summary>The output root or a directory on the way to this location is a symbolic link.</summary>
    | SymbolicLinkInPath of location: string
    /// <summary>The tree reports errors, so it must not replace existing output.</summary>
    | TreeHasErrors of errors: VMessage list
    /// <summary>The tree's declared order does not list exactly the tree's locations.</summary>
    | DeclaredOrderMismatch of unexpected: string list * missing: string list
    /// <summary>The previous manifest holds an entry that is not a safe location.</summary>
    | InvalidManifestEntry of entry: string

/// <summary>
/// The reason publishing a tree failed.
/// </summary>
[<RequireQualifiedAccess>]
type TreeWriteError =
    /// <summary>The tree failed validation and nothing was written. Lists every problem found.</summary>
    | InvalidTree of problems: TreeWriteProblem list
    /// <summary>
    /// Writing failed. A failure while staging leaves existing output unchanged. A failure while
    /// moving staged files into place can leave a mix of old and new files.
    /// </summary>
    | WriteFailed of outputDir: string * cause: exn

/// <summary>
/// Whether publishing tracks the files it owns in a manifest.
/// </summary>
[<RequireQualifiedAccess>]
type ManifestMode =
    /// <summary>No manifest is written, and no files are removed.</summary>
    | NoManifest
    /// <summary>
    /// A JSON manifest with this file name, directly under the output root, lists the published
    /// locations. Files listed by the previous manifest and no longer in the tree are deleted.
    /// </summary>
    | Manifest of fileName: string

/// <summary>
/// Where and how to publish a tree.
/// </summary>
type PublishOptions = {
    /// <summary>The directory that receives the files</summary>
    OutputRoot: string
    /// <summary>Whether to track published files in a manifest</summary>
    Manifest: ManifestMode
}

/// <summary>
/// What publishing a tree did, as locations relative to the output root.
/// </summary>
type PublishResult = {
    /// <summary>Files created or replaced</summary>
    Written: string list
    /// <summary>Files already byte-identical, and left untouched</summary>
    Unchanged: string list
    /// <summary>Files deleted because the previous manifest listed them and the tree does not</summary>
    Removed: string list
}

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
            | TreeWriteProblem.SymbolicLinkInPath location -> "a directory on the way to this path is a symbolic link: " + location
            | TreeWriteProblem.TreeHasErrors errors ->
                "the tree has errors: " + String.Join("; ", errors |> List.map VMessage.describe)
            | TreeWriteProblem.DeclaredOrderMismatch (unexpected, missing) ->
                "the declared order does not match the tree's files. Not in the tree: "
                + String.Join(", ", unexpected)
                + ". Not in the order: "
                + String.Join(", ", missing)
            | TreeWriteProblem.InvalidManifestEntry entry -> "the previous manifest has an unsafe entry: " + entry

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
        Extensions = PropertyBag.empty
    }

    /// <summary>
    /// Creates a tree configuration with name and version.
    /// </summary>
    /// <param name="name">Project name</param>
    /// <param name="version">Project version</param>
    let create (name: string) (version: string): TreeConfig =
        { empty with Name = Some name; Version = Some version }

    /// <summary>
    /// Stores a typed plugin setting.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="value">The value</param>
    /// <param name="config">The configuration to update</param>
    let set (property: Property<'T>) (value: 'T) (config: TreeConfig): TreeConfig =
        { config with Extensions = PropertyBag.set property value config.Extensions }

    /// <summary>
    /// Reads a typed plugin setting.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="config">The configuration to query</param>
    let tryGet (property: Property<'T>) (config: TreeConfig): 'T option =
        PropertyBag.tryGet property config.Extensions

    /// <summary>
    /// Removes a typed plugin setting.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="config">The configuration to update</param>
    let remove (property: Property<'T>) (config: TreeConfig): TreeConfig =
        { config with Extensions = PropertyBag.remove property config.Extensions }

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
        Metadata = PropertyBag.empty
        Messages = []
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
        let rec collect (basePath: string) (content: TreeNode list) =
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
    /// Collects all diagnostic messages from the tree, its directories and its files.
    /// </summary>
    /// <param name="tree">Tree to collect from</param>
    let rec collectMessages (tree: VFileTree): VMessage list =
        tree.Messages
        @ (tree.Content
           |> List.collect (function
               | File file -> file.Messages
               | Directory subtree -> collectMessages subtree))

    /// <summary>
    /// Checks if the tree, any directory in it or any of its files has an error or fatal message.
    /// </summary>
    /// <param name="tree">Tree to check</param>
    let hasErrors (tree: VFileTree): bool =
        collectMessages tree
        |> List.exists (fun message -> message.Severity = Error || message.Severity = Fatal)

    /// <summary>
    /// Adds a diagnostic that belongs to the tree rather than to one of its files.
    /// </summary>
    /// <param name="message">The message</param>
    /// <param name="tree">Tree to update</param>
    let report (message: VMessage) (tree: VFileTree): VFileTree =
        { tree with Messages = tree.Messages @ [ message ] }

    /// <summary>
    /// Merges an overlay tree onto a base tree. Files and messages from the overlay come after the
    /// base's, and overlay metadata wins. Files are not checked for duplicate locations, which
    /// publishing rejects.
    /// </summary>
    /// <param name="overlay">The tree to add</param>
    /// <param name="tree">The base tree</param>
    let merge (overlay: VFileTree) (tree: VFileTree): VFileTree =
        { tree with
            Content = tree.Content @ overlay.Content
            Messages = tree.Messages @ overlay.Messages
            Metadata = PropertyBag.merge overlay.Metadata tree.Metadata
        }

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

        let rec build (entries: (string list * VFile) list): TreeNode list =
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

    /// <summary>
    /// Flattens the tree to a simple string map (morphir-elm exact equivalent), with the same keys
    /// as <see cref="toFileMap"/>. This reads every file's content, and a file with no content maps
    /// to an empty string.
    /// </summary>
    /// <param name="tree">Tree to flatten</param>
    /// <returns>The map, or the first error from reading a file.</returns>
    let toStringMap (tree: VFileTree): Result<Map<string, string>, ContentError> =
        tree
        |> locatedFiles
        |> List.fold
            (fun result (location, file) ->
                result
                |> Result.bind (fun map ->
                    VFile.readText file |> Result.map (fun text -> Map.add location text map)))
            (Result.Ok Map.empty)
    /// <summary>
    /// Stores a typed value in the tree-level metadata.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="value">The value</param>
    /// <param name="tree">Tree to update</param>
    let set (property: Property<'T>) (value: 'T) (tree: VFileTree): VFileTree =
        { tree with Metadata = PropertyBag.set property value tree.Metadata }

    /// <summary>
    /// Reads a typed value from the tree-level metadata.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="tree">Tree to query</param>
    let tryGet (property: Property<'T>) (tree: VFileTree): 'T option =
        PropertyBag.tryGet property tree.Metadata

    /// <summary>
    /// Removes a typed value from the tree-level metadata.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="tree">Tree to update</param>
    let remove (property: Property<'T>) (tree: VFileTree): VFileTree =
        { tree with Metadata = PropertyBag.remove property tree.Metadata }
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
    /// The dependency or declared order of a tree's locations, independent of directory structure.
    /// A manifest lists locations in this order, or sorted when this is absent.
    /// </summary>
    let declaredOrder = Property.create<string list> "morphir.tree.declared-order"

    /// <summary>True when a path read back from a manifest is a plain relative location.</summary>
    let private isSafeManifestEntry (entry: string): bool =
        not (String.IsNullOrWhiteSpace entry)
        && not (isRooted entry)
        && entry.Replace('\\', '/').Split('/')
           |> Array.forall (fun segment -> segment <> "" && segment <> "." && segment <> "..")

    /// <summary>The output root's full path without a trailing separator.</summary>
    let private fullRoot (root: string): string =
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath root)

    /// <summary>True when a location resolves to a path inside the output root.</summary>
    let private staysInside (root: string) (location: string): bool =
        let rootPath = fullRoot root
        let resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootPath, location))
        resolved.StartsWith(rootPath + string System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)

    let private isSymbolicLink (directory: string): bool =
        System.IO.Directory.Exists directory
        && not (isNull (System.IO.DirectoryInfo(directory).LinkTarget))

    /// <summary>True when a directory between the output root and a location is a symbolic link.</summary>
    let private linkInPath (root: string) (location: string): bool =
        let parents = pathSegments location |> List.rev |> List.tail |> List.rev

        parents
        |> List.scan (fun directory segment -> System.IO.Path.Combine(directory, segment)) (fullRoot root)
        |> List.tail
        |> List.exists isSymbolicLink

    /// <summary>Reads the raw entries of a manifest. A missing manifest has no entries.</summary>
    let private readManifestEntries (options: PublishOptions): string list =
        match options.Manifest with
        | ManifestMode.NoManifest -> []
        | ManifestMode.Manifest fileName ->
            let path = System.IO.Path.Combine(options.OutputRoot, fileName)

            if System.IO.File.Exists path then
                use document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText path)

                [ for entry in document.RootElement.EnumerateArray() -> entry.GetString() ]
            else
                []

    /// <summary>The order a tree's locations are listed in a manifest.</summary>
    let private manifestOrder (tree: VFileTree): string list =
        match tryGet declaredOrder tree with
        | Some order -> order
        | None -> locatedFiles tree |> List.map fst |> List.sort

    /// <summary>Every reason a tree cannot be published safely. Empty when it can.</summary>
    let private publishProblems (options: PublishOptions) (tree: VFileTree): TreeWriteProblem list =
        let located = locatedFiles tree |> List.map fst
        let root = options.OutputRoot

        let pathless =
            tree
            |> allFiles
            |> List.filter (fun file -> Option.isNone (fileLocation "" file))
            |> List.map TreeWriteProblem.FileWithoutPath

        let isOutside (location: string) =
            isRooted location
            || List.contains ".." (pathSegments location)
            || not (staysInside root location)

        let outside, inside = located |> List.partition isOutside

        let duplicates =
            located
            |> List.groupBy (fun location -> location.ToUpperInvariant())
            |> List.filter (fun (_, group) -> group.Length > 1)
            |> List.map (fun (_, group) -> TreeWriteProblem.DuplicateLocation group.Head)

        let manifestCollisions =
            match options.Manifest with
            | ManifestMode.NoManifest -> []
            | ManifestMode.Manifest fileName ->
                located
                |> List.filter (fun location -> String.Equals(location, fileName, StringComparison.OrdinalIgnoreCase))
                |> List.map TreeWriteProblem.DuplicateLocation

        let links =
            if isSymbolicLink (fullRoot root) then
                [ TreeWriteProblem.SymbolicLinkInPath root ]
            else
                inside
                |> List.filter (linkInPath root)
                |> List.map TreeWriteProblem.SymbolicLinkInPath

        let errors =
            match collectMessages tree |> List.filter (fun message -> message.Severity = Error || message.Severity = Fatal) with
            | [] -> []
            | messages -> [ TreeWriteProblem.TreeHasErrors messages ]

        let order =
            match tryGet declaredOrder tree with
            | None -> []
            | Some declared ->
                let unexpected = declared |> List.filter (fun location -> not (List.contains location located))
                let missing = located |> List.filter (fun location -> not (List.contains location declared))

                if List.isEmpty unexpected && List.isEmpty missing then
                    []
                else
                    [ TreeWriteProblem.DeclaredOrderMismatch (unexpected, missing) ]

        let manifestEntries =
            readManifestEntries options
            |> List.filter (fun entry -> not (isSafeManifestEntry entry))
            |> List.map TreeWriteProblem.InvalidManifestEntry

        pathless
        @ (outside |> List.map TreeWriteProblem.PathOutsideOutputDirectory)
        @ duplicates
        @ manifestCollisions
        @ links
        @ errors
        @ order
        @ manifestEntries

    /// <summary>
    /// Checks whether a tree can be published, without writing anything. Use this to fail early
    /// before expensive work.
    /// </summary>
    /// <param name="options">Where and how the tree would be published</param>
    /// <param name="tree">Tree to check</param>
    /// <returns>Ok, or an error listing every problem found.</returns>
    let validatePublish (options: PublishOptions) (tree: VFileTree): Result<unit, TreeWriteError> =
        try
            match publishProblems options tree with
            | [] -> Result.Ok ()
            | problems -> Result.Error (TreeWriteError.InvalidTree problems)
        with ex ->
            Result.Error (TreeWriteError.WriteFailed (options.OutputRoot, ex))

    /// <summary>
    /// Reads the locations listed by the manifest in an output root, in order. A missing manifest
    /// has no locations. Every entry is checked, because the file could have been changed.
    /// </summary>
    /// <param name="options">The output root and manifest</param>
    let readManifest (options: PublishOptions): Result<string list, TreeWriteError> =
        try
            let entries = readManifestEntries options

            match entries |> List.filter (fun entry -> not (isSafeManifestEntry entry)) with
            | [] -> Result.Ok entries
            | invalid -> Result.Error (TreeWriteError.InvalidTree (invalid |> List.map TreeWriteProblem.InvalidManifestEntry))
        with ex ->
            Result.Error (TreeWriteError.WriteFailed (options.OutputRoot, ex))

    /// <summary>
    /// Takes the lock that serializes publishers of an output root. It lives in a per-user temp
    /// directory so that publishing leaves nothing extra in the output root.
    /// </summary>
    let private acquireLock (root: string): System.IO.FileStream =
        let rootPath = fullRoot root
        let identity = if OperatingSystem.IsWindows() then rootPath.ToUpperInvariant() else rootPath

        let name =
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes identity))

        let directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "morphir-publish-locks")
        System.IO.Directory.CreateDirectory directory |> ignore
        let path = System.IO.Path.Combine(directory, name + ".lock")

        let rec attempt (tries: int) =
            try
                new System.IO.FileStream(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None)
            with :? System.IO.IOException when tries < 100 ->
                System.Threading.Thread.Sleep 100
                attempt (tries + 1)

        attempt 0

    /// <summary>Writes a file's content to a path, keeping bytes and streams byte for byte.</summary>
    let private writeContent (path: string) (content: VFileContent option): unit =
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName path) |> ignore

        match content with
        | None -> System.IO.File.WriteAllBytes(path, [||])
        | Some value ->
            match VFileContent.openRead value with
            | Result.Error (ContentError.ReadFailed (_, cause)) -> raise cause
            | Result.Ok source ->
                use source = source
                use target = System.IO.File.Create path
                source.CopyTo target

    /// <summary>Compares two files without loading them whole.</summary>
    let private sameContent (first: string) (second: string): bool =
        if System.IO.FileInfo(first).Length <> System.IO.FileInfo(second).Length then
            false
        else
            use a = System.IO.File.OpenRead first
            use b = System.IO.File.OpenRead second
            let bufferA = Array.zeroCreate<byte> 81920
            let bufferB = Array.zeroCreate<byte> 81920

            let rec compare () =
                let countA = a.ReadAtLeast(bufferA.AsSpan(), bufferA.Length, false)
                let countB = b.ReadAtLeast(bufferB.AsSpan(), bufferB.Length, false)

                if countA <> countB then
                    false
                elif countA = 0 then
                    true
                elif bufferA.AsSpan(0, countA).SequenceEqual(ReadOnlySpan<byte>(bufferB, 0, countB)) then
                    compare ()
                else
                    false

            compare ()

    /// <summary>Renders manifest text, without reflection-based serialization.</summary>
    let private manifestText (locations: string list): string =
        use buffer = new System.IO.MemoryStream()

        do
            use writer = new System.Text.Json.Utf8JsonWriter(buffer)
            writer.WriteStartArray()

            for location in locations do
                writer.WriteStringValue location

            writer.WriteEndArray()

        System.Text.Encoding.UTF8.GetString(buffer.ToArray()) + Environment.NewLine

    /// <summary>
    /// Publishes a tree under an output root so that files cannot escape the root, cannot silently
    /// overwrite each other, and failed output never replaces good output.
    /// </summary>
    /// <remarks>
    /// The whole tree is validated first, and nothing is written if any problem is found. All
    /// content is then written to a private staging directory inside the root. Only after every
    /// file is staged do files move into place, under a lock shared by publishers running as the
    /// same user. Identical files are left untouched. With a manifest, it is written after the
    /// files, and files listed by the previous manifest and no longer in the tree are deleted. An
    /// I/O failure during the moves can leave a mix of old and new files, which the next
    /// publication corrects.
    /// </remarks>
    /// <param name="options">Where and how to publish</param>
    /// <param name="tree">Tree to publish</param>
    /// <returns>What was written, unchanged and removed, or the reason publishing failed.</returns>
    let publish (options: PublishOptions) (tree: VFileTree): Result<PublishResult, TreeWriteError> =
        match validatePublish options tree with
        | Result.Error error -> Result.Error error
        | Result.Ok () ->
            let root = options.OutputRoot

            try
                System.IO.Directory.CreateDirectory root |> ignore
                let stage = System.IO.Path.Combine(root, ".stage-" + Guid.NewGuid().ToString "N")
                System.IO.Directory.CreateDirectory stage |> ignore

                try
                    let located = locatedFiles tree

                    for location, file in located do
                        writeContent (System.IO.Path.Combine(stage, location)) file.Content

                    use _lock = acquireLock root

                    // Check again now that this publisher holds the lock, because another one
                    // could have changed the root or its manifest.
                    match publishProblems options tree with
                    | _ :: _ as problems -> Result.Error (TreeWriteError.InvalidTree problems)
                    | [] ->
                        let previous = readManifestEntries options

                        let unchanged, changed =
                            located
                            |> List.map fst
                            |> List.partition (fun location ->
                                let destination = System.IO.Path.Combine(root, location)

                                System.IO.File.Exists destination
                                && sameContent (System.IO.Path.Combine(stage, location)) destination)

                        for location in changed do
                            let destination = System.IO.Path.Combine(root, location)
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName destination) |> ignore
                            System.IO.File.Move(System.IO.Path.Combine(stage, location), destination, true)

                        let removed =
                            match options.Manifest with
                            | ManifestMode.NoManifest -> []
                            | ManifestMode.Manifest fileName ->
                                let text = manifestText (manifestOrder tree)
                                let manifestPath = System.IO.Path.Combine(root, fileName)

                                if not (System.IO.File.Exists manifestPath) || System.IO.File.ReadAllText manifestPath <> text then
                                    let staged = System.IO.Path.Combine(stage, fileName)
                                    System.IO.File.WriteAllText(staged, text)
                                    System.IO.File.Move(staged, manifestPath, true)

                                let current = located |> List.map fst

                                previous
                                |> List.filter (fun entry ->
                                    not (current |> List.exists (fun location -> String.Equals(location, entry, StringComparison.OrdinalIgnoreCase)))
                                    && System.IO.File.Exists(System.IO.Path.Combine(root, entry)))

                        for entry in removed do
                            System.IO.File.Delete(System.IO.Path.Combine(root, entry))

                        Result.Ok { Written = changed; Unchanged = unchanged; Removed = removed }
                finally
                    if System.IO.Directory.Exists stage then
                        System.IO.Directory.Delete(stage, true)
            with ex ->
                Result.Error (TreeWriteError.WriteFailed (root, ex))

    /// <summary>
    /// Publishes the tree's files under an output directory with no manifest. The root tree's path
    /// is not part of the output path. Directories are created only as needed for files. See
    /// <c>publish</c> for the guarantees.
    /// </summary>
    /// <param name="outputDir">Output directory</param>
    /// <param name="tree">Tree to write</param>
    /// <returns>Ok when every file was written, otherwise the reason it failed.</returns>
    let writeToDisk (outputDir: string) (tree: VFileTree): Result<unit, TreeWriteError> =
        publish { OutputRoot = outputDir; Manifest = ManifestMode.NoManifest } tree
        |> Result.map ignore

    /// <summary>
    /// Loads a directory into a tree without reading any file content. Each file's content is
    /// opened on demand, so a missing or unreadable file surfaces when its content is read. The
    /// tree's path is the directory given, and each file and subdirectory path is its own name.
    /// Each directory lists its files first and then its subdirectories, both sorted by name.
    /// </summary>
    /// <remarks>
    /// Hidden files and directories such as .git are included, and empty directories are kept.
    /// Symbolic links to directories are skipped because they can form cycles. Symbolic links to
    /// files are read.
    /// </remarks>
    /// <param name="rootDir">Directory to load</param>
    /// <returns>The tree, or the reason the directory could not be listed.</returns>
    let loadFromDisk (rootDir: string): Result<VFileTree, TreeLoadError> =
        let rec loadContent (directory: System.IO.DirectoryInfo): TreeNode list =
            let files =
                directory.EnumerateFiles()
                |> Seq.sortBy (fun file -> file.Name)
                |> Seq.map (fun file ->
                    let fullName = file.FullName
                    let source = StreamSource(fun () -> System.IO.File.OpenRead fullName :> System.IO.Stream)
                    File(VFile.ofStream file.Name source))
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