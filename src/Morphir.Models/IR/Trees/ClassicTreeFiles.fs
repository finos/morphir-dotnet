namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic.AccessControlled
open Morphir.Json.Classic
open Morphir.Json.V4

/// <summary>What the manifest file of a classic v3 tree holds: the manifest, and the path budget the tree was written
/// against.</summary>
type internal ClassicManifestFile = {
    /// <summary>The manifest, with the single-file label of its kind.</summary>
    Manifest: ClassicManifest
    /// <summary>The path budget of the tree (decision 0012).</summary>
    PathBudget: int
}

/// <summary>What the module file of a classic v3 tree holds, with attributes as <c>ClassicJson</c> reads them.</summary>
type internal ClassicModuleFile =
    TreeModuleFile<ClassicModuleDocument, ClassicTypeDocument<JsonElement>, ClassicValueDocument<JsonElement, JsonElement>>

/// <summary>
/// The four kinds of file of a classic v3 document tree, in the JSON profile (finos/morphir
/// <c>docs/spec/ir/schemas/v3/document-tree-files.md</c>): the envelope of a v4 tree file, in which every file says
/// <c>"formatVersion": "3.1.0"</c>, around the classic v3 JSON of each entry. A <c>def</c> or <c>spec</c> payload
/// is spelled as the entry of a single v3 file's module list without its name (<c>ClassicJson</c>,
/// <c>ClassicJsonWriter</c> with the v3 profile), and keeps its attributes as they are. A reader ignores a top-level
/// <c>$meta</c> (decision 0014), and a writer never writes one.
/// </summary>
/// <remarks>
/// A file whose <c>formatVersion</c> is not the string <c>"3.1.0"</c> is refused with <c>version_mismatch</c>, and a
/// manifest with <c>tree.v3_version</c>. A v3 manifest has no member beside <c>formatVersion</c>,
/// <c>distribution</c>, <c>package</c>, <c>pathBudget</c> and <c>dependencies</c>; a module specification has no
/// <c>access</c>, and a module file has no <c>module</c> alias for <c>path</c>: each is an <c>unknown_member</c>. A
/// payload that is not the classic JSON of its kind is <c>invalid_type</c>.
/// </remarks>
module internal ClassicTreeFiles =
    open V4JsonDecoding

    /// <summary>The <c>formatVersion</c> of every file of a v3 tree.</summary>
    [<Literal>]
    let FormatVersion = "3.1.0"

    /// The format version check of a v3 tree file: the string "3.1.0", or the rule's error.
    let private checkVersionWith (rule: string) (values: Members) =
        match
            values
            |> List.filter (fun (key, _) -> key = "formatVersion")
        with
        | [] -> fail "missing_format_version" "$" "The file states no formatVersion."
        | [ _, element ] when
            element.ValueKind = JsonValueKind.String
            && element.GetString() = FormatVersion
            ->
            ()
        | [ _, element ] ->
            fail
                rule
                (memberAt "$" "formatVersion")
                $"Every file of a v3 tree says \"formatVersion\": \"{FormatVersion}\", and this one says {element.GetRawText()}."
        | _ ->
            fail
                "duplicate_format_version"
                (memberAt "$" "formatVersion")
                "The member 'formatVersion' appears more than once."

    let private checkVersion = checkVersionWith "version_mismatch"

    let private fileMembers = TreeFileCodec.fileMembers checkVersion

    let private formatVersion (json: Utf8JsonWriter) =
        json.WriteString("formatVersion", FormatVersion)

    let private kindText (kind: ClassicDistributionKind) =
        match kind with
        | ClassicDistributionKind.LibraryKind -> "Library"
        | ClassicDistributionKind.SpecsKind -> "Specs"

    let private kindOf location element =
        match text location element with
        | "Library" -> ClassicDistributionKind.LibraryKind
        | "Specs" -> ClassicDistributionKind.SpecsKind
        | other ->
            fail "invalid_distribution_shape" location $"'{other}' is not a v3 distribution kind."

    /// A payload decoded by the classic reader, whose failure is the payload's invalid_type.
    let private payload (decode: string -> JsonElement -> 'a) location element =
        try
            decode location element
        with
        | ClassicJsonFailure error -> fail "invalid_type" error.Location error.Message
        | :? InvalidOperationException as problem -> fail "invalid_type" location problem.Message
        | :? FormatException as problem -> fail "invalid_type" location problem.Message

    let private typeDocument holdsDefinitions location element =
        if holdsDefinitions then
            ClassicTypeDocument.DefinedType(
                payload ClassicJsonDecoder.typeDefinitionEntry location element
            )
        else
            ClassicTypeDocument.SpecifiedType(
                payload ClassicJsonDecoder.typeSpecificationEntry location element
            )

    let private valueDocument holdsDefinitions location element =
        if holdsDefinitions then
            ClassicValueDocument.DefinedValue(
                payload ClassicJsonDecoder.valueDefinitionEntry location element
            )
        else
            ClassicValueDocument.SpecifiedValue(
                payload ClassicJsonDecoder.valueSpecificationEntry location element
            )

    /// <summary>
    /// Writes a manifest file: <c>formatVersion</c>, <c>distribution</c>, <c>package</c>, <c>pathBudget</c>, then
    /// <c>dependencies</c> when there are some.
    /// </summary>
    let writeManifest
        (pathBudget: int)
        (manifest: ClassicManifest)
        : Result<byte array, V4WriteError> =
        TreeFileCodec.writeManifest
            formatVersion
            (kindText manifest.Kind)
            manifest.Package
            pathBudget
            manifest.Dependencies
            ignore

    /// <summary>
    /// Reads a manifest file. Its <c>formatVersion</c> is <c>"3.1.0"</c>, or the file is refused with
    /// <c>tree.v3_version</c>; the manifest records the single-file label of its kind.
    /// </summary>
    let readManifest (root: JsonElement) : Result<ClassicManifestFile, V4JsonError> =
        TreeFileCodec.guarded (fun () ->
            let values = TreeFileCodec.fileMembers (checkVersionWith "tree.v3_version") root

            closed
                "$"
                [ "formatVersion"; "distribution"; "package"; "pathBudget"; "dependencies" ]
                values

            let kind = required "$" [ "distribution" ] kindOf values
            let package, budget, dependencies = TreeFileCodec.manifestMembers values

            {
                Manifest = {
                    Kind = kind
                    Package = package
                    Dependencies = dependencies
                    SingleFileVersion = ClassicDocumentTree.singleFileVersionOf kind
                }
                PathBudget = budget
            }
        )

    /// <summary>
    /// Writes a module file: <c>formatVersion</c>, <c>path</c>, <c>access</c> only for a private module, <c>doc</c>,
    /// <c>types</c> and <c>values</c> as arrays of names, and <c>fileNames</c> when a stem was cut.
    /// </summary>
    let writeModule
        (modulePath: ModulePath)
        (document: ClassicModuleDocument)
        (fileNames: (Name * string) list)
        : Result<byte array, V4WriteError> =
        TreeFileCodec.writeModule
            formatVersion
            modulePath
            (document.Access = Some Private)
            document.Doc
            document.TypeNames
            document.ValueNames
            fileNames

    /// <summary>
    /// Reads a module file. A module of a package definition (<paramref name="holdsDefinitions"/>) with no
    /// <c>access</c> is public; a module specification has no <c>access</c>.
    /// </summary>
    let readModule
        (holdsDefinitions: bool)
        (root: JsonElement)
        : Result<ClassicModuleFile, V4JsonError> =
        TreeFileCodec.guarded (fun () ->
            // The attributes of an inline entry outlive the document they were parsed from.
            let values = fileMembers (root.Clone())

            closed
                "$"
                [
                    "formatVersion"
                    "path"
                    if holdsDefinitions then
                        "access"
                    "doc"
                    "types"
                    "values"
                    "fileNames"
                ]
                values

            let access values =
                if holdsDefinitions then
                    match optional "$" [ "access" ] TreeFileCodec.isPrivateAccess values with
                    | Some true -> Some Private
                    | Some false
                    | None -> Some Public
                else
                    None

            TreeFileCodec.readModule
                [ "path" ]
                access
                (typeDocument holdsDefinitions)
                (valueDocument holdsDefinitions)
                (fun access doc typeNames valueNames -> {
                    Access = access
                    Doc = doc
                    TypeNames = typeNames
                    ValueNames = valueNames
                })
                values
        )

    /// A payload written by the classic writer into the file's JSON, or the writer's error.
    let private writePayload
        (memberName: string)
        (write: ClassicWireProfile -> Utf8JsonWriter -> 'a -> Result<unit, ClassicWriteError>)
        (entry: 'a)
        (failure: ClassicWriteError option ref)
        (json: Utf8JsonWriter)
        =
        json.WritePropertyName memberName

        match write ClassicWireProfile.v3 json entry with
        | Ok() -> ()
        | Error error -> failure.Value <- Some error

    /// A node file, or the classic writer's error of its payload.
    let private writeNodeWith name (body: ClassicWriteError option ref -> Utf8JsonWriter -> unit) =
        let failure = ref None
        let written = TreeFileCodec.writeNode formatVersion name (body failure)

        match failure.Value with
        | Some error ->
            Error
                {
                    Rule = error.Rule
                    Location = error.Location
                    Message = error.Message
                }
        | None -> written

    /// <summary>Writes a type file: <c>formatVersion</c>, <c>name</c>, then <c>def</c> or <c>spec</c>.</summary>
    let writeType
        (name: Name)
        (document: ClassicTypeDocument<JsonElement>)
        : Result<byte array, V4WriteError> =
        writeNodeWith
            name
            (fun failure json ->
                match document with
                | ClassicTypeDocument.DefinedType definition ->
                    writePayload
                        "def"
                        ClassicJsonWriter.writeTypeDefinitionEntry
                        definition
                        failure
                        json
                | ClassicTypeDocument.SpecifiedType specification ->
                    writePayload
                        "spec"
                        ClassicJsonWriter.writeTypeSpecificationEntry
                        specification
                        failure
                        json
            )

    /// <summary>Writes a value file: <c>formatVersion</c>, <c>name</c>, then <c>def</c> or <c>spec</c>.</summary>
    let writeValue
        (name: Name)
        (document: ClassicValueDocument<JsonElement, JsonElement>)
        : Result<byte array, V4WriteError> =
        writeNodeWith
            name
            (fun failure json ->
                match document with
                | ClassicValueDocument.DefinedValue definition ->
                    writePayload
                        "def"
                        ClassicJsonWriter.writeValueDefinitionEntry
                        definition
                        failure
                        json
                | ClassicValueDocument.SpecifiedValue specification ->
                    writePayload
                        "spec"
                        ClassicJsonWriter.writeValueSpecificationEntry
                        specification
                        failure
                        json
            )

    /// <summary>Reads a type file: its name, and a <c>DefinedType</c> for <c>def</c> or a <c>SpecifiedType</c> for
    /// <c>spec</c>.</summary>
    let readType
        (root: JsonElement)
        : Result<Name * ClassicTypeDocument<JsonElement>, V4JsonError> =
        TreeFileCodec.readNode checkVersion (typeDocument true) (typeDocument false) (root.Clone())

    /// <summary>Reads a value file: its name, and a <c>DefinedValue</c> for <c>def</c> or a <c>SpecifiedValue</c>
    /// for <c>spec</c>.</summary>
    let readValue
        (root: JsonElement)
        : Result<Name * ClassicValueDocument<JsonElement, JsonElement>, V4JsonError> =
        TreeFileCodec.readNode
            checkVersion
            (valueDocument true)
            (valueDocument false)
            (root.Clone())
