namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text.Json
open Morphir.IR.Classic.Distribution
open Morphir.Json.Classic
open Morphir.Json.V4

/// <summary>How a model is stored: in one file, or as a document tree of files.</summary>
type ModelLayout =
    /// <summary>One file that holds the whole distribution.</summary>
    | SingleFile = 0
    /// <summary>A document tree: a manifest and one file per module, type and value.</summary>
    | DocumentTree = 1

/// <summary>
/// Why a model was not read, converted or written: a stable code, the file it concerns as a path from the tree's
/// root (null for a single file, or for the whole tree), and a message. A store error keeps its code.
/// </summary>
[<Sealed>]
type ConversionProblem(code: string, file: string | null, message: string) =
    /// <summary>The stable code, such as <c>missing_member</c> or <c>model.unsupported_version</c>.</summary>
    member _.Code = code
    /// <summary>The file, as a path from the tree's root, or null.</summary>
    member _.File = file
    /// <summary>What is wrong.</summary>
    member _.Message = message

    /// <summary>The code, the file and the message.</summary>
    override _.ToString() =
        match file with
        | null -> $"{code}: {message}"
        | path -> $"{code}: {path}: {message}"

/// <summary>
/// Content that a read dropped, which a caller reports and which does not stop the conversion: a stable code, the
/// file it concerns as a path from the tree's root, and a message that names the content.
/// </summary>
[<Sealed>]
type ConversionNotice(code: string, file: string | null, message: string) =
    /// <summary>
    /// The stable code: <c>tree.dropped_manifest_member</c> for a manifest member with no place in the model, or
    /// <c>tree.ignored_file</c> for a file that is not part of the model.
    /// </summary>
    member _.Code = code
    /// <summary>The file, as a path from the tree's root, or null.</summary>
    member _.File = file
    /// <summary>What was dropped.</summary>
    member _.Message = message

    /// <summary>The code, the file and the message.</summary>
    override _.ToString() =
        match file with
        | null -> $"{code}: {message}"
        | path -> $"{code}: {path}: {message}"

/// The forms that a stored model takes.
type internal ModelForm =
    | V4File of Morphir.IR.V4.Distribution
    | ClassicFile of Distribution<JsonElement, JsonElement>
    | TreeForm of StoredTree

/// <summary>
/// A model in one of its stored forms: a v4 or a classic v3 single file, or a v4 or a classic v3 document tree. It
/// is the C# surface of a conversion: it reads a single file, takes the result of a store read, converts between the
/// layouts through <c>split</c> and <c>join</c>, and writes a single file or gives the tree that a store writes. It
/// never changes the IR version, and it makes no file-system call; the caller reads and writes the files.
/// </summary>
/// <remarks>
/// Each <c>Try</c> member sets exactly one of its result and its problems. A classic v1 or v2 single file is refused
/// with <c>model.unsupported_version</c>: the document-tree specifications define trees for IR 3.1.0 and IR 4 only.
/// </remarks>
[<Sealed>]
type StoredModel internal (form: ModelForm) =
    static let problemsOf (errors: StoreError list) : IReadOnlyList<ConversionProblem> =
        [| for error in errors -> ConversionProblem(error.Code, Option.toObj error.File, error.Message) |]

    static let one code (message: string) : IReadOnlyList<ConversionProblem> =
        [| ConversionProblem(code, null, message) |]

    static let noticeOf (notice: TreeNotice) =
        match notice with
        | DroppedManifestMember name ->
            ConversionNotice(
                "tree.dropped_manifest_member",
                "manifest.json",
                $"The manifest member '{name}' has no place in the single-file model and was dropped."
            )
        | IgnoredFile path ->
            ConversionNotice(
                "tree.ignored_file",
                path,
                $"The file '{path}' is not part of the model and was ignored."
            )

    static let isV4 (root: JsonElement) =
        if root.ValueKind <> JsonValueKind.Object then
            false
        else
            match root.TryGetProperty "formatVersion" with
            | true, version when version.ValueKind = JsonValueKind.Number -> version.GetRawText() = "4"
            | true, version when version.ValueKind = JsonValueKind.String ->
                match version.GetString() with
                | null -> false
                | text -> text = "4" || text.StartsWith("4.", StringComparison.Ordinal)
            | _ -> false

    member internal _.Form = form

    /// <summary>The layout of this form.</summary>
    member _.Layout =
        match form with
        | TreeForm _ -> ModelLayout.DocumentTree
        | V4File _
        | ClassicFile _ -> ModelLayout.SingleFile

    /// <summary>The IR version of the model, as text: <c>"4"</c>, or <c>"3"</c> for a classic v3 model.</summary>
    member _.IrVersion =
        match form with
        | V4File _
        | TreeForm(V4Tree _) -> "4"
        | ClassicFile _
        | TreeForm(ClassicTree _) -> "3"

    /// <summary>
    /// The tree of a <see cref="ModelLayout.DocumentTree"/> form, for <c>DocumentTreeStore.write</c>. Throws
    /// <see cref="InvalidOperationException"/> for a single file.
    /// </summary>
    member _.Tree: StoredTree =
        match form with
        | TreeForm tree -> tree
        | V4File _
        | ClassicFile _ -> invalidOp "A single-file model has no tree; convert it to a document tree first."

    /// <summary>The layout and the IR version.</summary>
    override this.ToString() =
        let layout =
            match this.Layout with
            | ModelLayout.DocumentTree -> "document tree"
            | _ -> "single file"

        $"IR {this.IrVersion} {layout}"

    /// <summary>
    /// Reads a single-file model: a <c>formatVersion</c> of 4 or <c>4.x</c> as IR 4 (<c>V4Json</c>), any other as a
    /// classic model (<c>ClassicJson</c>), which must be IR 3. A codec error keeps its rule as the code, with its JSON
    /// location at the start of the message.
    /// </summary>
    static member TryReadSingleFile
        (
            root: JsonElement,
            [<Out; MaybeNullWhen(false)>] model: byref<StoredModel>,
            [<Out; MaybeNullWhen(true)>] problems: byref<IReadOnlyList<ConversionProblem>>
        ) : bool =
        let read =
            if isV4 root then
                match V4Json.readDistribution root with
                | Ok document -> Ok(StoredModel(V4File document.Distribution))
                | Error error -> Error(one error.Rule $"{error.Location}: {error.Message}")
            else
                match ClassicJson.readDistribution root with
                | Ok document when document.Version = 3 -> Ok(StoredModel(ClassicFile document.Distribution))
                | Ok document ->
                    Error(
                        one
                            "model.unsupported_version"
                            $"The model is IR {document.Version}. model convert converts IR 3 and IR 4 models, which document trees exist for."
                    )
                | Error error -> Error(one error.Rule $"{error.Location}: {error.Message}")

        match read with
        | Ok read ->
            model <- read
            problems <- Unchecked.defaultof<_>
            true
        | Error found ->
            model <- Unchecked.defaultof<_>
            problems <- found
            false

    /// <summary>
    /// Takes the result of <c>DocumentTreeStore.read</c>: the tree as a model with the read's notices and the
    /// documents whose files were compacted (<c>ReadReport.CompactedFiles</c>: the manifest first, then by file; none
    /// for a plain tree), or the store's errors as problems, each with its code and file.
    /// </summary>
    static member TryOfTreeRead
        (
            read: Result<StoredTree * ReadReport, StoreError list>,
            [<Out; MaybeNullWhen(false)>] model: byref<StoredModel>,
            [<Out; MaybeNullWhen(false)>] notices: byref<IReadOnlyList<ConversionNotice>>,
            [<Out; MaybeNullWhen(false)>] compactedFiles: byref<IReadOnlyList<DocumentAddress>>,
            [<Out; MaybeNullWhen(true)>] problems: byref<IReadOnlyList<ConversionProblem>>
        ) : bool =
        match read with
        | Ok(tree, report) ->
            model <- StoredModel(TreeForm tree)
            notices <- [| for notice in report.Notices -> noticeOf notice |]
            compactedFiles <- List.toArray report.CompactedFiles
            problems <- Unchecked.defaultof<_>
            true
        | Error errors ->
            model <- Unchecked.defaultof<_>
            notices <- Unchecked.defaultof<_>
            compactedFiles <- Unchecked.defaultof<_>
            problems <- problemsOf errors
            false

    /// <summary>
    /// Converts to <paramref name="layout"/>: a single file splits into a tree (<c>V4DocumentTree.split</c>,
    /// <c>ClassicDocumentTree.split</c>), a tree joins into a single file (<c>join</c>, whose errors keep their
    /// <c>tree.</c> codes), and a form already in the layout is returned as it is.
    /// </summary>
    member this.TryConvert
        (
            layout: ModelLayout,
            [<Out; MaybeNullWhen(false)>] converted: byref<StoredModel>,
            [<Out; MaybeNullWhen(true)>] problems: byref<IReadOnlyList<ConversionProblem>>
        ) : bool =
        let joined (result: Result<'d, TreeError list>) (toForm: 'd -> ModelForm) =
            match result with
            | Ok distribution -> Ok(StoredModel(toForm distribution))
            | Error errors ->
                Error(
                    [| for error in errors -> ConversionProblem(error.Code, null, error.Message) |]
                    :> IReadOnlyList<_>
                )

        let result =
            match layout, form with
            | ModelLayout.DocumentTree, V4File distribution ->
                Ok(StoredModel(TreeForm(V4Tree(V4DocumentTree.split distribution))))
            | ModelLayout.DocumentTree, ClassicFile distribution ->
                Ok(StoredModel(TreeForm(ClassicTree(ClassicDocumentTree.split distribution))))
            | ModelLayout.SingleFile, TreeForm(V4Tree tree) -> joined (V4DocumentTree.join tree) V4File
            | ModelLayout.SingleFile, TreeForm(ClassicTree tree) ->
                joined (ClassicDocumentTree.join tree) ClassicFile
            | ModelLayout.DocumentTree, TreeForm _
            | ModelLayout.SingleFile, (V4File _ | ClassicFile _) -> Ok this
            | _ -> invalidArg (nameof layout) $"'{int layout}' is not a model layout."

        match result with
        | Ok model ->
            converted <- model
            problems <- Unchecked.defaultof<_>
            true
        | Error found ->
            converted <- Unchecked.defaultof<_>
            problems <- found
            false

    /// <summary>
    /// Writes a single-file form as indented JSON: IR 4 with <c>"formatVersion": 4</c>, a classic <c>Library</c> with
    /// <c>"formatVersion": 3</c> and a classic <c>Specs</c> with <c>"formatVersion": "3.1.0"</c>, classic attributes
    /// as they were read. With <paramref name="compact"/>, the file is written under the writer policy
    /// <c>morphir.compact/0.1.0-draft.1</c> (<c>CompactionPolicy</c>) and stays one file: an envelope in canonical JSON
    /// when an alias is worth its cost, or else the plain document in canonical JSON. <paramref name="compacted"/>
    /// says which: true for an envelope. Throws <see cref="InvalidOperationException"/> for a tree.
    /// </summary>
    member _.TryWriteSingleFile
        (
            compact: bool,
            [<Out; MaybeNullWhen(false)>] bytes: byref<byte array>,
            [<Out>] compacted: byref<bool>,
            [<Out; MaybeNullWhen(true)>] problems: byref<IReadOnlyList<ConversionProblem>>
        ) : bool =
        let plain, family =
            match form with
            | V4File distribution ->
                V4JsonWriter.writeDistribution true distribution
                |> Result.mapError (fun error -> one error.Rule $"{error.Location}: {error.Message}"),
                Morphir.IR.Files.IrFamily.V4
            | ClassicFile distribution ->
                let profile =
                    match distribution with
                    | Library _ -> ClassicWireProfile.v3
                    | Specs _ -> ClassicWireProfile.v3_1_0

                ClassicJsonWriter.writeJsonAttributes profile true distribution
                |> Result.mapError (fun error -> one error.Rule $"{error.Location}: {error.Message}"),
                Morphir.IR.Files.IrFamily.Classic3
            | TreeForm _ -> invalidOp "A document tree is written by DocumentTreeStore.write, not as a single file."

        let written =
            plain
            |> Result.bind (fun plain ->
                if not compact then
                    Ok(plain, false)
                else
                    // The writers bound the nesting; the canonical JSON of the policy refuses a file deeper than it reads.
                    use document =
                        JsonDocument.Parse(ReadOnlyMemory<byte>(plain), JsonDocumentOptions(MaxDepth = Int32.MaxValue))

                    Morphir.IR.Files.CompactionPolicy.compact family document.RootElement
                    |> Result.mapError problemsOf
            )

        match written with
        | Ok(written, aliased) ->
            bytes <- written
            compacted <- aliased
            problems <- Unchecked.defaultof<_>
            true
        | Error found ->
            bytes <- Unchecked.defaultof<_>
            compacted <- false
            problems <- found
            false

    /// <summary>
    /// Writes a single-file form, plain or under the writer policy, as the overload with <c>compacted</c> does, for a
    /// caller that does not ask whether an alias was worth its cost.
    /// </summary>
    member this.TryWriteSingleFile
        (
            compact: bool,
            [<Out; MaybeNullWhen(false)>] bytes: byref<byte array>,
            [<Out; MaybeNullWhen(true)>] problems: byref<IReadOnlyList<ConversionProblem>>
        ) : bool =
        let mutable compacted = false
        this.TryWriteSingleFile(compact, &bytes, &compacted, &problems)

    /// <summary>
    /// The options of <c>DocumentTreeStore.write</c>: the default path budget, with name compaction when
    /// <paramref name="compact"/> is true.
    /// </summary>
    static member WriteOptions(compact: bool) : WriteOptions = {
        WriteOptions.defaults with
            Compact = if compact then Some Names else None
    }

    /// <summary>
    /// Takes the result of <c>DocumentTreeStore.write</c>: the files it gives, or its errors as problems. The files
    /// are generic, so this model code names no file type.
    /// </summary>
    static member TryGetWritten<'files>
        (
            written: Result<'files, StoreError list>,
            [<Out; MaybeNullWhen(false)>] files: byref<'files>,
            [<Out; MaybeNullWhen(true)>] problems: byref<IReadOnlyList<ConversionProblem>>
        ) : bool =
        match written with
        | Ok written ->
            files <- written
            problems <- Unchecked.defaultof<_>
            true
        | Error errors ->
            files <- Unchecked.defaultof<_>
            problems <- problemsOf errors
            false
