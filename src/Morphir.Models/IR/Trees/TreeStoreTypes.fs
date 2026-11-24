namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.IO
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4

/// <summary>How a store compacts the names of the files it writes.</summary>
type CompactMode =
    /// <summary>Names are written through a name-compaction context (<c>ir/name-compaction</c>).</summary>
    | Names

/// <summary>How a store writes a tree.</summary>
type WriteOptions = {
    /// <summary>
    /// The longest physical path, in characters from the tree's root with the extension, that the store writes. A
    /// longer path has its stem cut, and the module file records the cut in <c>fileNames</c> (decision 0012).
    /// </summary>
    PathBudget: int
    /// <summary>The compaction of names, or none for plain files.</summary>
    Compact: CompactMode option
}

/// <summary>The serialization profile of a document tree.</summary>
[<RequireQualifiedAccess>]
type TreeProfile =
    /// <summary>The JSON profile (<c>manifest.json</c>, <c>module.json</c>, <c>*.type.json</c>, <c>*.value.json</c>).</summary>
    | Json
    /// <summary>The YAML profile (<c>manifest.yaml</c>, <c>module.yaml</c>, <c>*.type.yaml</c>, <c>*.value.yaml</c>).</summary>
    | Yaml

    /// <summary>The canonical profile name (<c>json</c> or <c>yaml</c>), for C# callers.</summary>
    member this.WireValue =
        match this with
        | Json -> "json"
        | Yaml -> "yaml"

    /// <summary>For C# callers: branches exhaustively on the serialization profile.</summary>
    member this.Match<'R>(json: Func<'R>, yaml: Func<'R>) : 'R =
        match this with
        | Json -> json.Invoke()
        | Yaml -> yaml.Invoke()

    /// <summary>The wire value.</summary>
    override this.ToString() = this.WireValue

/// <summary>Which serialization profiles a whole-tree read accepts.</summary>
[<RequireQualifiedAccess>]
type TreeProfileSelection =
    /// <summary>Accepts only the JSON profile.</summary>
    | JsonOnly
    /// <summary>Accepts only the YAML profile.</summary>
    | YamlOnly
    /// <summary>Accepts either a homogeneous JSON tree or a homogeneous YAML tree.</summary>
    | Any

    /// <summary>The canonical selection name (<c>json</c>, <c>yaml</c>, or <c>auto</c>), for C# callers.</summary>
    member this.WireValue =
        match this with
        | JsonOnly -> "json"
        | YamlOnly -> "yaml"
        | Any -> "auto"

    /// <summary>The wire value.</summary>
    override this.ToString() = this.WireValue

/// <summary>The limits of a read.</summary>
type ReadOptions = {
    /// <summary>The most tree files (JSON or YAML) that a read accepts.</summary>
    MaxFiles: int
    /// <summary>The most bytes of one file that a read accepts.</summary>
    MaxFileBytes: int64
}

/// <summary>
/// Options for reading a whole document tree across profiles with explicit file, byte, nesting-depth and node
/// budgets. Construct via <see cref="Defaults"/> and the <c>With*</c> members, or <see cref="Create"/>.
/// </summary>
type TreeReadOptions =
    private
        {
            MaxFilesValue: int
            MaxFileBytesValue: int64
            MaxTotalBytesValue: int64
            MaxDepthValue: int
            MaxNodesValue: int
            ProfilesValue: TreeProfileSelection
        }

    /// <summary>The most tree files (JSON or YAML) that a read accepts.</summary>
    member this.MaxFiles = this.MaxFilesValue
    /// <summary>The most bytes of one file that a read accepts.</summary>
    member this.MaxFileBytes = this.MaxFileBytesValue
    /// <summary>The most aggregate decoded or expanded bytes accepted across the manifest and all members of the tree.</summary>
    member this.MaxTotalBytes = this.MaxTotalBytesValue
    /// <summary>The deepest JSON or YAML nesting accepted for any file in the tree.</summary>
    member this.MaxDepth = this.MaxDepthValue
    /// <summary>The most decoded JSON or YAML nodes accepted across the entire tree.</summary>
    member this.MaxNodes = this.MaxNodesValue
    /// <summary>Which serialization profile(s) the read accepts.</summary>
    member this.Profiles = this.ProfilesValue

    /// <summary>Creates validated options. Throws <see cref="ArgumentOutOfRangeException"/> if any limit is not positive.</summary>
    static member Create
        (
            maxFiles: int,
            maxFileBytes: int64,
            maxDepth: int,
            maxNodes: int,
            profiles: TreeProfileSelection
        ) : TreeReadOptions =
        if maxFiles <= 0 then
            raise (ArgumentOutOfRangeException(nameof maxFiles, "MaxFiles must be positive."))

        if maxFileBytes <= 0L then
            raise (ArgumentOutOfRangeException(nameof maxFileBytes, "MaxFileBytes must be positive."))

        if maxDepth <= 0 then
            raise (ArgumentOutOfRangeException(nameof maxDepth, "MaxDepth must be positive."))

        if maxNodes <= 0 then
            raise (ArgumentOutOfRangeException(nameof maxNodes, "MaxNodes must be positive."))

        {
            MaxFilesValue = maxFiles
            MaxFileBytesValue = maxFileBytes
            MaxTotalBytesValue = Int64.MaxValue
            MaxDepthValue = maxDepth
            MaxNodesValue = maxNodes
            ProfilesValue = profiles
        }

    /// <summary>Default limits accepting both JSON and YAML profiles.</summary>
    static member Defaults: TreeReadOptions =
        TreeReadOptions.Create(
            1_000_000,
            256L * 1024L * 1024L,
            Morphir.Json.V4.V4JsonDecoding.MaxDepth,
            Int32.MaxValue,
            TreeProfileSelection.Any
        )

    /// <summary>Returns a copy with <paramref name="profiles"/>. For F# and C# callers.</summary>
    member this.WithProfiles(profiles: TreeProfileSelection) : TreeReadOptions =
        { this with ProfilesValue = profiles }

    /// <summary>Returns a copy with <paramref name="maxFiles"/>. For F# and C# callers.</summary>
    member this.WithMaxFiles(maxFiles: int) : TreeReadOptions =
        { TreeReadOptions.Create(maxFiles, this.MaxFileBytesValue, this.MaxDepthValue, this.MaxNodesValue, this.ProfilesValue) with
            MaxTotalBytesValue = this.MaxTotalBytesValue
        }

    /// <summary>Returns a copy with <paramref name="maxFileBytes"/>. For F# and C# callers.</summary>
    member this.WithMaxFileBytes(maxFileBytes: int64) : TreeReadOptions =
        { TreeReadOptions.Create(this.MaxFilesValue, maxFileBytes, this.MaxDepthValue, this.MaxNodesValue, this.ProfilesValue) with
            MaxTotalBytesValue = this.MaxTotalBytesValue
        }

    /// <summary>Returns a copy with <paramref name="maxTotalBytes"/>. For F# and C# callers.</summary>
    member this.WithMaxTotalBytes(maxTotalBytes: int64) : TreeReadOptions =
        if maxTotalBytes <= 0L then
            raise (ArgumentOutOfRangeException(nameof maxTotalBytes, "MaxTotalBytes must be positive."))

        { this with MaxTotalBytesValue = maxTotalBytes }

    /// <summary>Returns a copy with <paramref name="maxDepth"/>. For F# and C# callers.</summary>
    member this.WithMaxDepth(maxDepth: int) : TreeReadOptions =
        { TreeReadOptions.Create(this.MaxFilesValue, this.MaxFileBytesValue, maxDepth, this.MaxNodesValue, this.ProfilesValue) with
            MaxTotalBytesValue = this.MaxTotalBytesValue
        }

    /// <summary>Returns a copy with <paramref name="maxNodes"/>. For F# and C# callers.</summary>
    member this.WithMaxNodes(maxNodes: int) : TreeReadOptions =
        { TreeReadOptions.Create(this.MaxFilesValue, this.MaxFileBytesValue, this.MaxDepthValue, maxNodes, this.ProfilesValue) with
            MaxTotalBytesValue = this.MaxTotalBytesValue
        }

    /// <summary>A summary of the options.</summary>
    override this.ToString() =
        $"TreeReadOptions(Profiles={this.ProfilesValue}, MaxFiles={this.MaxFilesValue}, MaxFileBytes={this.MaxFileBytesValue}, MaxTotalBytes={this.MaxTotalBytesValue}, MaxDepth={this.MaxDepthValue}, MaxNodes={this.MaxNodesValue})"

/// The shared work allowance for one bounded operation.
[<Sealed>]
type BoundedTreeWorkBudget(limit: int64) =
    let gate = obj ()
    let mutable remaining = limit

    do if limit <= 0L then invalidArg (nameof limit) "A work budget must be positive."

    /// Charge one unit per started 4 KiB block, without overflowing on a large byte count.
    static member Blocks(bytes: int64) =
        bytes / 4096L + (if bytes % 4096L = 0L then 0L else 1L)

    /// Cap a read or write by the current allowance before it starts.
    member _.CapBytes(bytes: int64) =
        lock gate (fun () ->
            if remaining < BoundedTreeWorkBudget.Blocks bytes then remaining * 4096L else bytes)

    /// Charge actual work before admitting it. A failed charge exhausts the allowance.
    member _.TryCharge(units: int64) =
        if units < 0L then invalidArg (nameof units) "Work cannot be negative."
        lock gate (fun () ->
            if units > remaining then
                remaining <- 0L
                false
            else
                remaining <- remaining - units
                true)

/// <summary>What a read found besides the tree.</summary>
type ReadReport = {
    /// <summary>
    /// Content that the read dropped: the manifest's <c>version</c> and <c>created</c>, and each package metadata
    /// file that it ignored.
    /// </summary>
    Notices: TreeNotice list
    /// <summary>The documents whose files were compacted. A plain tree has none.</summary>
    CompactedFiles: DocumentAddress list
}

/// <summary>Why a store did not read or write a tree: a stable code, the file it concerns, and a message.</summary>
type StoreError =
    {
        /// <summary>
        /// The stable code: a <c>tree.</c> code for a rule of the tree, such as <c>tree.stem_collision</c>, or the
        /// rule of the JSON codec, such as <c>unknown_member</c>.
        /// </summary>
        Code: string
        /// <summary>The file, as a physical path from the tree's root, or none for an error of the whole tree.</summary>
        File: string option
        /// <summary>What is wrong.</summary>
        Message: string
    }

    /// <summary>For C# callers: the file, as a physical path from the tree's root, or null for an error of the whole tree.</summary>
    member this.FileOrNull: string | null = Option.toObj this.File

    /// <summary>The code, the file and the message.</summary>
    override this.ToString() =
        match this.File with
        | Some file -> $"{this.Code}: {file}: {this.Message}"
        | None -> $"{this.Code}: {this.Message}"

/// <summary>
/// A structured store diagnostic that carries an optional codec member location (JSONPath or JSON Pointer)
/// separately from <see cref="Message"/>, avoiding string splitting on punctuation inside member names.
/// </summary>
[<Sealed>]
type TreeDiagnostic internal (code: string, file: string option, location: string option, position: string, message: string, legacyMessage: string option) =
    /// <summary>The stable diagnostic code.</summary>
    member _.Code = code
    /// <summary>The file, as a physical path from the tree's root, or null for an error of the whole tree.</summary>
    member _.File: string | null = Option.toObj file
    /// <summary>The file option for F# callers within this assembly and its test suite.</summary>
    member internal _.FileOption = file
    /// <summary>The structured codec location (JSONPath or JSON Pointer), or null when the diagnostic has none.</summary>
    member _.Location: string | null = Option.toObj location
    /// <summary>The location option for F# callers within this assembly and its test suite.</summary>
    member internal _.LocationOption = location
    /// <summary>What is wrong, without any leading location prefix.</summary>
    member _.Message =
        if String.IsNullOrEmpty position then message else $"{message}{position}"

    /// <summary>Converts to a legacy <see cref="StoreError"/> preserving the formatted location prefix in <see cref="StoreError.Message"/>.</summary>
    member internal _.ToStoreError() : StoreError =
        let formatted =
            match legacyMessage with
            | Some legacy -> legacy
            | None ->
                match location with
                | Some loc -> $"{loc}{position}: {message}"
                | None -> if String.IsNullOrEmpty position then message else $"{message}{position}"

        {
            Code = code
            File = file
            Message = formatted
        }

    /// <summary>The code, file, location, and message.</summary>
    override this.ToString() = this.ToStoreError().ToString()

/// <summary>A tree that a store reads or writes: a v4 tree or a classic v3 tree.</summary>
type StoredTree =
    /// <summary>A v4 tree.</summary>
    | V4Tree of V4DocumentTree
    /// <summary>A classic v3 tree, with the attributes as <c>ClassicJson</c> reads them.</summary>
    | ClassicTree of ClassicDocumentTree<JsonElement, JsonElement>

/// <summary>
/// A whole document tree that was read and joined into a <see cref="Morphir.IR.Model.VersionedModel"/>,
/// together with the serialization profile it was read from and read-report projections for C# callers.
/// </summary>
[<Sealed>]
type LoadedTreeModel internal
    (
        model: Morphir.IR.Model.VersionedModel,
        profile: TreeProfile,
        tree: StoredTree,
        report: ReadReport,
        compactedLocations: string list
    ) =
    let notices =
        report.Notices
        |> List.map string
        |> List.toArray
        :> System.Collections.Generic.IReadOnlyList<string>

    let compactedFiles =
        compactedLocations
        |> List.toArray
        :> System.Collections.Generic.IReadOnlyList<string>

    /// <summary>The joined model with its exact declared manifest <c>formatVersion</c>.</summary>
    member _.Model = model
    /// <summary>The serialization profile of the tree (<c>json</c> or <c>yaml</c>).</summary>
    member _.Profile = profile
    /// <summary>Notices recorded while reading the tree (dropped manifest fields or ignored package metadata files).</summary>
    member _.Notices: System.Collections.Generic.IReadOnlyList<string> = notices
    /// <summary>Relative paths of tree documents that were read from compacted name-context envelopes.</summary>
    member _.CompactedFiles: System.Collections.Generic.IReadOnlyList<string> = compactedFiles
    /// <summary>The underlying stored tree for internal F# callers.</summary>
    member internal _.Tree = tree
    /// <summary>The read report for internal F# callers.</summary>
    member internal _.Report = report
    /// <summary>The profile and declared format version.</summary>
    override _.ToString() = $"LoadedTreeModel({profile.WireValue}, {model.SourceVersion.Text})"

/// <summary>Functions for <see cref="WriteOptions"/>.</summary>
[<RequireQualifiedAccess>]
module WriteOptions =
    /// <summary>The path budget of a tree when the caller gives none: 4000 characters (decision 0012).</summary>
    [<Literal>]
    let DefaultPathBudget = 4000

    /// <summary>A path budget of 4000 and no compaction.</summary>
    let defaults: WriteOptions = {
        PathBudget = DefaultPathBudget
        Compact = None
    }

/// <summary>Functions for <see cref="ReadOptions"/>.</summary>
[<RequireQualifiedAccess>]
module ReadOptions =
    /// <summary>At most a million files of at most 256 MiB each.</summary>
    let defaults: ReadOptions = {
        MaxFiles = 1_000_000
        MaxFileBytes =
            256L
            * 1024L
            * 1024L
    }

/// <summary>
/// What the module file of a tree holds, for the module, type and value documents of one IR version: the module's
/// path, its document, the stems that the path budget cut, and the types and values it holds inline.
/// </summary>
type internal TreeModuleFile<'moduleDoc, 'typ, 'value> = {
    /// <summary>The module path that the file states.</summary>
    Path: ModulePath
    /// <summary>The module document: access, doc, and the names of the types and values in document order.</summary>
    Document: 'moduleDoc
    /// <summary>Each name whose stem was cut for the path budget, with its stem, in document order.</summary>
    FileNames: (Name * string) list
    /// <summary>The types that the file holds inline rather than in files of their own, in document order.</summary>
    InlineTypes: (Name * 'typ) list
    /// <summary>The values that the file holds inline, in document order.</summary>
    InlineValues: (Name * 'value) list
}

/// The helper of the store's modules: a store error and structured tree diagnostics.
module internal StoreErrors =
    /// <summary>A store error.</summary>
    let error code file message : StoreError = {
        Code = code
        File = file
        Message = message
    }

    let diagnostic code file message =
        TreeDiagnostic(code, file, None, "", message, None)

    let located code file location message =
        TreeDiagnostic(code, file, Some location, "", message, None)

    let locatedLegacy code file location message legacyMessage =
        TreeDiagnostic(code, file, Some location, "", message, Some legacyMessage)

    let locatedWithPosition code file location position message =
        TreeDiagnostic(code, file, Some location, position, message, None)

    let ofStoreError (e: StoreError) =
        TreeDiagnostic(e.Code, e.File, None, "", e.Message, None)

    let toStoreErrors (diagnostics: TreeDiagnostic list) =
        diagnostics |> List.map (fun d -> d.ToStoreError())

    let workLimit file =
        error "extension.bounded_work_limit" file "Processing exceeds the negotiated maxSessionWorkUnits."
