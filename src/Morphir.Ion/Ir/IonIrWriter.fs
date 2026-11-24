namespace Morphir.Ion

open System
open System.Text.Json
open Morphir.Files
open Morphir.IR
open Morphir.IR.Trees
open Morphir.Json.Classic
open Morphir.Json.V4

/// <summary>The format versions the Ion IR format carries, which the writer and its callers check alike.</summary>
[<RequireQualifiedAccess>]
module IonIrVersions =
    /// <summary>
    /// The exact release a header states for a format version spelling: <c>3</c> and <c>3.0.0</c> give <c>3.0.0</c>,
    /// <c>3.1.0</c> gives <c>3.1.0</c>, and <c>4</c> and <c>4.0.0</c> give <c>4.0.0</c>. Every other spelling, such as
    /// <c>3.99.0</c>, <c>4.0</c> or <c>4.1.0-draft.1</c>, is not carried.
    /// </summary>
    let release (version: string) : string option =
        match version with
        | "3"
        | "3.0.0" -> Some "3.0.0"
        | "3.1.0" -> Some "3.1.0"
        | "4"
        | "4.0.0" -> Some "4.0.0"
        | _ -> None

    /// <summary>Whether the Ion IR format carries a format version.</summary>
    let carries (version: Morphir.IR.Versioning.FormatVersion) : bool =
        (release (Morphir.IR.Versioning.version version)).IsSome

/// <summary>
/// Writes a Morphir distribution in the canonical text of the Ion IR format: a single-file datagram, or a document
/// tree. The output reads back through <see cref="IonIr"/> and <see cref="IonIrTree"/> as the same distribution.
/// </summary>
/// <remarks>
/// The text is UTF-8 Ion 1.0 with the layout of draft <c>0.1.0-draft.1</c>: one member, item or operand per line at
/// two spaces for each level, bare symbols where Ion allows them, and every empty optional list left out. The header
/// states the exact release of the document: <c>3.0.0</c> or <c>3.1.0</c> for a v3 library, <c>3.1.0</c> for a v3
/// specs distribution, and <c>4.0.0</c> for v4 (see <see cref="IonIrVersions"/>). A write fails
/// with an <see cref="IonIrError"/>, never an exception: <c>unsupported_format_version_major</c> or
/// <c>unsupported_format_version_minor</c> for an IR the format does not carry, <c>specs_before_3_1</c> for v3 specs
/// below <c>3.1.0</c>, <c>unrepresentable_attribute</c> for a
/// v3 attribute with no Ion spelling, <c>unsupported_node</c> for a node Ion cannot hold, <c>invalid_path_budget</c>
/// and <c>path_over_budget</c> for a tree whose paths do not fit.
/// </remarks>
[<RequireQualifiedAccess>]
module IonIrWriter =
    /// <summary>The path budget a tree states when the caller names none.</summary>
    [<Literal>]
    let DefaultPathBudget = 4000

    /// <summary>The smallest path budget a tree accepts, as for the JSON and YAML trees.</summary>
    [<Literal>]
    let MinimumPathBudget = 64

    let private refuse code message : 'a =
        raise (
            IonIrFailure
                {
                    Code = code
                    Message = message
                    Path = None
                    Location = None
                }
        )

    /// The exact release of a version spelling, or the refusal of one the format does not carry.
    let private releaseOf (stated: string) : string =
        match IonIrVersions.release stated with
        | Some release -> release
        | None when stated.Split('.')[0] = "3" || stated.Split('.')[0] = "4" ->
            refuse
                "unsupported_format_version_minor"
                $"The Ion IR format carries formatVersion 3.0.0, 3.1.0 and 4.0.0, not {stated}."
        | None -> refuse "unsupported_format_version_major" $"The Ion IR format carries IR 3 and 4, not {stated}."

    /// The exact release a document's header states, or the refusal of a version or kind the format does not carry.
    let private checkVersion (document: IrDocument) : string =
        let stated, isSpecs, isClassic =
            match document with
            | ClassicIr(version, Morphir.IR.Classic.Distribution.Specs _) -> version, true, true
            | ClassicIr(version, _) -> version, false, true
            | V4Ir(version, _) -> version, false, false

        match IonIrVersions.release stated, isClassic with
        | Some "4.0.0", false -> "4.0.0"
        | Some "3.1.0", true -> "3.1.0"
        | Some "3.0.0", true when not isSpecs -> "3.0.0"
        | Some "3.0.0", true ->
            refuse "specs_before_3_1" $"A v3 specs distribution needs formatVersion 3.1.0, not {stated}."
        | Some release, _ ->
            let family = if isClassic then "classic" else "v4"

            refuse
                "unsupported_format_version_major"
                $"formatVersion {release} does not belong to the {family} IR that the document holds."
        | None, _ when stated.Split('.')[0] = "3" || stated.Split('.')[0] = "4" ->
            refuse
                "unsupported_format_version_minor"
                $"The Ion IR format carries formatVersion 3.0.0, 3.1.0 and 4.0.0, not {stated}."
        | None, _ ->
            refuse "unsupported_format_version_major" $"The Ion IR format carries IR 3 and 4, not {stated}."

    let private datagram (document: IrDocument) =
        let release = checkVersion document

        match document with
        | ClassicIr(_, distribution) -> ClassicIonWriter.datagram release distribution
        | V4Ir(_, distribution) -> V4IonWriter.datagram distribution

    /// <summary>Writes a distribution as one canonical Ion datagram, footer last.</summary>
    let write (document: IrDocument) : Result<byte array, IonIrError> =
        IonIr.withRoom Result.Error (fun () -> Ok(IonTextWriter.encode (datagram document)))

    /// <summary>The canonical Ion text of a distribution, as <see cref="write"/> encodes it.</summary>
    let writeText (document: IrDocument) : Result<string, IonIrError> =
        IonIr.withRoom Result.Error (fun () -> Ok(IonTextWriter.render (datagram document)))

    // Document trees.

    let private stems (Path names) = names |> List.map TreeStems.escapeName

    let private directory (segments: string list) = String.concat "/" segments + "/"

    let private ownDirectory (PackageName package) (ModulePath modulePath) =
        directory ("pkg" :: stems package @ stems modulePath)

    let private dependencyDirectory (PackageName package) (ModulePath modulePath) =
        directory ("deps" :: stems package @ [ TreeStems.BareVersionSegment ] @ stems modulePath)

    /// A file of a tree: its location and its top-level values.
    type private TreeFile = string * IonText list

    /// The stem of a member file, cut when the path does not fit the budget. A cut stem no longer spells the name, so
    /// the file then states it.
    let private memberFile
        (budget: int)
        (folder: string)
        (suffix: string)
        (name: Name)
        (element: Name option -> IonText)
        : TreeFile =
        let ending = suffix + ".ion"
        let whole = folder + TreeStems.escapeName name + ending

        if whole.Length <= budget then
            whole, [ element None ]
        else
            match TreeStems.cut (budget - folder.Length - ending.Length) name with
            | Ok stem -> folder + stem + ending, [ element (Some name) ]
            | Result.Error reason ->
                refuse
                    "path_over_budget"
                    $"The path {whole} has {whole.Length} characters, and no cut stem fits the path budget of {budget}: {reason}"

    let private moduleFile (budget: int) (folder: string) (element: IonText) : TreeFile =
        let location = folder + "module.ion"

        if location.Length > budget then
            refuse
                "path_over_budget"
                $"The path {location} has {location.Length} characters, and the path budget is {budget}; only a type or value stem can be cut."

        location, [ element ]

    let private v4Files (budget: int) (distribution: Morphir.IR.V4.Distribution) : TreeFile list =
        let package, own =
            match distribution with
            | Morphir.IR.V4.Library(package, _, _)
            | Morphir.IR.V4.Specs(package, _, _)
            | Morphir.IR.V4.Application(package, _, _, _) -> package, package

        ignore own

        let definitionFiles folderOf (definition: Morphir.IR.V4.PackageDefinition) =
            Morphir.IR.V4.OrderedMap.toList definition.Modules
            |> List.collect (fun (modulePath, entry) ->
                let folder = folderOf modulePath

                moduleFile budget folder (V4IonWriter.moduleDefinition None false entry)
                :: (Morphir.IR.V4.OrderedMap.toList entry.Value.Types
                    |> List.map (fun (name, item) ->
                        memberFile budget folder ".type" name (fun stated -> V4IonWriter.typeDefinition stated item)
                    ))
                @ (Morphir.IR.V4.OrderedMap.toList entry.Value.Values
                   |> List.map (fun (name, item) ->
                       memberFile budget folder ".value" name (fun stated -> V4IonWriter.valueDefinition stated item)
                   ))
            )

        let specificationFiles folderOf (specification: Morphir.IR.V4.PackageSpecification) =
            Morphir.IR.V4.OrderedMap.toList specification.Modules
            |> List.collect (fun (modulePath, entry: Morphir.IR.V4.ModuleSpecification) ->
                let folder = folderOf modulePath

                moduleFile budget folder (V4IonWriter.moduleSpecification None false entry)
                :: (Morphir.IR.V4.OrderedMap.toList entry.Types
                    |> List.map (fun (name, item) ->
                        memberFile budget folder ".type" name (fun stated -> V4IonWriter.typeSpecification stated item)
                    ))
                @ (Morphir.IR.V4.OrderedMap.toList entry.Values
                   |> List.map (fun (name, item) ->
                       memberFile budget folder ".value" name (fun stated -> V4IonWriter.valueSpecification stated item)
                   ))
            )

        let manifest: TreeFile =
            "manifest.ion",
            V4IonWriter.header (Some budget) distribution
            :: V4IonWriter.dependencies false distribution

        let dependencyFiles, ownFiles =
            match distribution with
            | Morphir.IR.V4.Library(_, deps, definition) ->
                (Morphir.IR.V4.OrderedMap.toList deps
                 |> List.collect (fun (name, spec) -> specificationFiles (dependencyDirectory name) spec)),
                definitionFiles (ownDirectory package) definition
            | Morphir.IR.V4.Specs(_, deps, specification) ->
                (Morphir.IR.V4.OrderedMap.toList deps
                 |> List.collect (fun (name, spec) -> specificationFiles (dependencyDirectory name) spec)),
                specificationFiles (ownDirectory package) specification
            | Morphir.IR.V4.Application(_, deps, definition, _) ->
                (Morphir.IR.V4.OrderedMap.toList deps
                 |> List.collect (fun (name, dependency) -> definitionFiles (dependencyDirectory name) dependency)),
                definitionFiles (ownDirectory package) definition

        manifest :: dependencyFiles @ ownFiles

    let private classicFiles
        (budget: int)
        (release: string)
        (distribution: Morphir.IR.Classic.Distribution.Distribution<JsonElement, JsonElement>)
        : TreeFile list =
        let package = Morphir.IR.Classic.Distribution.packageName distribution

        let specificationFiles folderOf (specification: Morphir.IR.Classic.Package.PackageSpecification<JsonElement>) =
            Map.toList specification.Modules
            |> List.collect (fun (modulePath, entry) ->
                let folder = folderOf modulePath

                moduleFile budget folder (ClassicIonWriter.moduleSpecification None false entry)
                :: (Map.toList entry.Types
                    |> List.map (fun (name, item) ->
                        memberFile budget folder ".type" name (fun stated -> ClassicIonWriter.typeSpecification stated item)
                    ))
                @ (Map.toList entry.Values
                   |> List.map (fun (name, item) ->
                       memberFile budget folder ".value" name (fun stated -> ClassicIonWriter.valueSpecification stated item)
                   ))
            )

        let manifest: TreeFile =
            "manifest.ion",
            ClassicIonWriter.header (Some budget) release distribution
            :: ClassicIonWriter.dependencies false distribution

        let dependencyFiles =
            Morphir.IR.Classic.Distribution.dependencies distribution
            |> Map.toList
            |> List.collect (fun (name, spec) -> specificationFiles (dependencyDirectory name) spec)

        let ownFiles =
            match distribution with
            | Morphir.IR.Classic.Distribution.Library(_, _, definition) ->
                Map.toList definition.Modules
                |> List.collect (fun (modulePath, entry) ->
                    let folder = ownDirectory package modulePath

                    moduleFile budget folder (ClassicIonWriter.moduleDefinition None entry)
                    :: (Map.toList entry.Value.Types
                        |> List.map (fun (name, item) ->
                            memberFile
                                budget
                                folder
                                ".type"
                                name
                                (fun stated -> ClassicIonWriter.typeDefinition None stated item)
                        ))
                    @ (Map.toList entry.Value.Values
                       |> List.map (fun (name, item) ->
                           memberFile
                               budget
                               folder
                               ".value"
                               name
                               (fun stated -> ClassicIonWriter.valueDefinition None stated item)
                       ))
                )
            | Morphir.IR.Classic.Distribution.Specs(_, _, specification) ->
                specificationFiles (ownDirectory package) specification

        manifest :: dependencyFiles @ ownFiles

    let private treeFiles (pathBudget: int) (document: IrDocument) =
        if pathBudget < MinimumPathBudget then
            refuse "invalid_path_budget" $"The path budget is {pathBudget}; a tree's budget is at least {MinimumPathBudget}."

        let release = checkVersion document

        let files =
            match document with
            | ClassicIr(_, distribution) -> classicFiles pathBudget release distribution
            | V4Ir(_, distribution) -> v4Files pathBudget distribution

        // Two cut stems can share a prefix and a digest; one path for two files would lose one of them. A file
        // system that ignores case would merge two paths that differ only in case, as the JSON tree writer checks too.
        match
            files
            |> List.groupBy (fun (location, _) -> location.ToUpperInvariant())
            |> List.tryFind (fun (_, group) -> group.Length > 1)
        with
        | Some(_, (location, _) :: _) ->
            refuse
                "stem_collision"
                $"Two documents have one file, {location}, so each document has no file of its own. Give the tree a larger path budget."
        | _ -> ()

        files
        |> List.sortWith (fun (first, _) (second, _) -> String.CompareOrdinal(first, second))

    /// <summary>
    /// The files of the canonical Ion document tree of a distribution, each as its location from the tree's root and
    /// its bytes, in ordinal order of location. The manifest states <paramref name="pathBudget"/>.
    /// </summary>
    /// <remarks>
    /// The manifest holds the header and names each dependency. Each module directory holds <c>module.ion</c>, with
    /// only the module's own members, and one <c>&lt;stem&gt;.type.ion</c> or <c>&lt;stem&gt;.value.ion</c> for each type
    /// and value. A file leaves out every name its path supplies. A member path longer than the budget has its stem cut,
    /// and that file states its name.
    /// </remarks>
    let writeTreeFiles (pathBudget: int) (document: IrDocument) : Result<(string * byte array) list, IonIrError> =
        IonIr.withRoom
            Result.Error
            (fun () ->
                Ok(
                    treeFiles pathBudget document
                    |> List.map (fun (location, values) -> location, IonTextWriter.encode values)
                )
            )

    /// <summary>The canonical Ion document tree of a distribution, as a flat <see cref="VFileTree"/>.</summary>
    let writeTree (pathBudget: int) (document: IrDocument) : Result<VFileTree, IonIrError> =
        writeTreeFiles pathBudget document
        |> Result.map (fun files ->
            files
            |> List.map (fun (location, bytes) -> VFile.ofBytes location (ReadOnlyMemory bytes))
            |> VFileTree.fromFiles
        )

    let private jsonError rule location message : IonIrError = {
        Code = rule
        Message = message
        Path = None
        Location = Some location
    }

    let private isV4Root (root: JsonElement) =
        match root.ValueKind with
        | JsonValueKind.Object ->
            match root.TryGetProperty "formatVersion" with
            | true, version when version.ValueKind = JsonValueKind.Number -> version.GetRawText() = "4"
            | true, version when version.ValueKind = JsonValueKind.String ->
                (version.GetString() |> nonNull).StartsWith("4", StringComparison.Ordinal)
            | _ -> false
        | _ -> false

    /// The format version a JSON document states, as it spells it: an integer's digits, or a string.
    let private statedVersion (root: JsonElement) =
        match root.TryGetProperty "formatVersion" with
        | true, version when version.ValueKind = JsonValueKind.Number -> Some(version.GetRawText())
        | true, version when version.ValueKind = JsonValueKind.String -> version.GetString() |> Option.ofObj
        | _ -> None

    /// <summary>
    /// Reads a Morphir IR JSON document into the <see cref="IrDocument"/> the Ion writer takes: v4 through
    /// <c>V4Json</c>, and classic IR through <c>ClassicJson</c>. A JSON reader's error keeps its rule as <c>Code</c>
    /// and its JSON location as <c>Location</c>.
    /// </summary>
    let readJson (json: ReadOnlyMemory<byte>) : Result<IrDocument, IonIrError> =
        IonIr.withRoom Result.Error (fun () ->
        try
            use parsed = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 1_000_000))
            let root = parsed.RootElement

            if isV4Root root then
                match V4Json.readDistribution root with
                | Ok read -> Ok(V4Ir(releaseOf read.FormatVersion, read.Distribution))
                | Result.Error error -> Result.Error(jsonError error.Rule error.Location error.Message)
            else
                match ClassicJson.readDistribution root with
                | Ok read ->
                    // The release check sees the version as the JSON spells it: 3 is 3.0.0 and "3.1.0" stays 3.1.0, and an
                    // unsupported release such as "3.0.1" is refused here, so no caller can relabel it.
                    let stated = statedVersion root |> Option.defaultValue (string read.Version)
                    Ok(ClassicIr(releaseOf stated, read.Distribution))
                | Result.Error error -> Result.Error(jsonError error.Rule error.Location error.Message)
        with :? JsonException as error ->
            Result.Error {
                Code = "invalid_json"
                Message = error.Message
                Path = None
                Location = None
            })

    /// <summary>Reads Morphir IR JSON and writes it as one canonical Ion datagram.</summary>
    let ofJson (json: ReadOnlyMemory<byte>) : Result<byte array, IonIrError> = readJson json |> Result.bind write

    /// <summary>
    /// Reads Morphir IR JSON and writes it as one canonical Ion datagram that states <paramref name="release"/> in
    /// place of the JSON's version. A writer that writes a release the JSON profile cannot state, such as a v3 library
    /// at <c>3.1.0</c>, names it here. The release must be one the format carries for the document's family and kind.
    /// </summary>
    let ofJsonAs (release: Morphir.IR.Versioning.FormatVersion) (json: ReadOnlyMemory<byte>) : Result<byte array, IonIrError> =
        let stated = Morphir.IR.Versioning.version release

        readJson json
        |> Result.map (fun document ->
            match document with
            | ClassicIr(_, distribution) -> ClassicIr(stated, distribution)
            | V4Ir(_, distribution) -> V4Ir(stated, distribution)
        )
        |> Result.bind write

    /// <summary>Reads Morphir IR JSON and writes it as the files of a canonical Ion document tree.</summary>
    let ofJsonTree (pathBudget: int) (json: ReadOnlyMemory<byte>) : Result<(string * byte array) list, IonIrError> =
        readJson json |> Result.bind (writeTreeFiles pathBudget)

/// <summary>C# members of <see cref="IonIrWriter"/>.</summary>
[<AbstractClass; Sealed>]
type IonIrWriting =
    /// <summary>
    /// Whether the Ion IR format carries a format version: <c>3</c>, <c>3.0.0</c>, <c>3.1.0</c>, <c>4</c> or
    /// <c>4.0.0</c>. The writer refuses every other version.
    /// </summary>
    static member Carries(version: Morphir.IR.Versioning.FormatVersion) : bool = IonIrVersions.carries version

    /// <summary>
    /// The exact release a header states for a format version, such as <c>3.0.0</c> for <c>3</c>, or null when the
    /// format does not carry the version.
    /// </summary>
    static member Release(version: Morphir.IR.Versioning.FormatVersion) : string | null =
        IonIrVersions.release (Morphir.IR.Versioning.version version) |> Option.toObj

    /// <summary>
    /// Reads Morphir IR JSON and writes it as one canonical Ion datagram. It sets <paramref name="ion"/> and returns
    /// true, or sets <paramref name="failure"/> and returns false.
    /// </summary>
    static member TryWriteJson
        (
            json: ReadOnlyMemory<byte>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)>] ion:
                byref<byte array>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(true)>] failure:
                byref<IonIrError>
        ) : bool =
        IonIrWriting.Assign(IonIrWriter.ofJson json, &ion, &failure)

    /// <summary>
    /// Reads Morphir IR JSON and writes it as one canonical Ion datagram that states <paramref name="formatVersion"/>,
    /// such as <c>3.1.0</c>, in place of the JSON's version (see <see cref="IonIrWriter.ofJsonAs"/>).
    /// </summary>
    static member TryWriteJson
        (
            json: ReadOnlyMemory<byte>,
            formatVersion: Morphir.IR.Versioning.FormatVersion,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)>] ion:
                byref<byte array>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(true)>] failure:
                byref<IonIrError>
        ) : bool =
        // C# can pass null for a non-nullable reference; that is a caller error, not a decode failure.
        if isNull (box formatVersion) then
            nullArg (nameof formatVersion)

        IonIrWriting.Assign(IonIrWriter.ofJsonAs formatVersion json, &ion, &failure)

    static member private Assign
        (result: Result<byte array, IonIrError>, ion: byref<byte array>, failure: byref<IonIrError>)
        : bool =
        match result with
        | Ok bytes ->
            ion <- bytes
            failure <- Unchecked.defaultof<_>
            true
        | Result.Error error ->
            ion <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>
    /// Reads Morphir IR JSON and writes it as the files of a canonical Ion document tree, each as its location from the
    /// tree's root and its bytes, in ordinal order of location.
    /// </summary>
    static member TryWriteJsonTree
        (
            json: ReadOnlyMemory<byte>,
            pathBudget: int,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)>] files:
                byref<System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<string, byte array>>>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(true)>] failure:
                byref<IonIrError>
        ) : bool =
        match IonIrWriter.ofJsonTree pathBudget json with
        | Ok written ->
            files <-
                [|
                    for location, bytes in written -> System.Collections.Generic.KeyValuePair(location, bytes)
                |]
            failure <- Unchecked.defaultof<_>
            true
        | Result.Error error ->
            files <- Unchecked.defaultof<_>
            failure <- error
            false
