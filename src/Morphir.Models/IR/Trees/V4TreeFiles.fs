namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Text
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4

/// <summary>What the manifest file of a v4 tree holds: the manifest, the path budget the tree was written
/// against, and the members that have no place in the model.</summary>
type internal V4ManifestFile = {
    /// <summary>The manifest.</summary>
    Manifest: V4Manifest
    /// <summary>The path budget of the tree (decision 0012).</summary>
    PathBudget: int
    /// <summary>A notice for each of <c>version</c> and <c>created</c> that the file has.</summary>
    Dropped: TreeNotice list
}

/// <summary>What the module file of a v4 tree holds.</summary>
type internal V4ModuleFile = {
    /// <summary>The module path that the file states in <c>path</c> or <c>module</c>.</summary>
    Path: ModulePath
    /// <summary>The module document: access, doc, and the names of the types and values in document order.</summary>
    Document: V4ModuleDocument
    /// <summary>Each name whose stem was cut for the path budget, with its stem, in document order.</summary>
    FileNames: (Name * string) list
    /// <summary>The types that the file holds inline rather than in files of their own, in document order.</summary>
    InlineTypes: (Name * V4TypeDocument) list
    /// <summary>The values that the file holds inline, in document order.</summary>
    InlineValues: (Name * V4ValueDocument) list
}

/// <summary>
/// The four kinds of file of a v4 document tree, in the JSON profile: the manifest, a module, a type and a value
/// (finos/morphir <c>docs/spec/ir/schemas/v4/document-tree-files.md</c> and the schema
/// <c>morphir-ir-v4-document-tree-files.json</c>). A type, a value and an entry point are spelled with the codecs
/// of a single v4 document, <c>V4Json</c> and <c>V4JsonWriter</c>, so a tree file and a single file spell them
/// alike. A reader ignores a top-level <c>$meta</c> (decision 0014), and a writer never writes one.
/// </summary>
/// <remarks>
/// Where the parent's page and schema disagree, this follows the schema and the kit: an inline module entry is an
/// access-controlled definition in any of the spellings that <c>V4Json</c> reads, and a definition in a type or
/// value file has its access as the key (<c>{ "Public": { … } }</c>), as the kit's cases spell it.
/// </remarks>
module internal V4TreeFiles =
    open V4JsonDecoding

    /// <summary>The reserved member that a reader ignores and a writer never writes (decision 0014).</summary>
    [<Literal>]
    let MetaMember = "$meta"

    let private guarded (read: unit -> 'a) : Result<'a, V4JsonError> =
        try
            Ok(read ())
        with
        | V4JsonFailure error -> Error error
        | :? InsufficientExecutionStackException ->
            Error
                {
                    Rule = "depth_limit_exceeded"
                    Location = "$"
                    Message = "The JSON is nested too deeply to read."
                }

    /// The members of a tree file's root, after the format version check, without $meta.
    let private fileMembers (root: JsonElement) : Members =
        let values = members "$" root

        V4JsonDistributions.formatVersion values
        |> ignore

        checkDuplicates "$" root
        without MetaMember values

    /// The bytes of a written file as text, ending with a line break.
    let toText (bytes: byte array) : string =
        Encoding.UTF8.GetString bytes
        + "\n"

    let private kindText (kind: V4DistributionKind) =
        match kind with
        | V4DistributionKind.LibraryKind -> "Library"
        | V4DistributionKind.SpecsKind -> "Specs"
        | V4DistributionKind.ApplicationKind -> "Application"

    let private kindOf location element =
        match text location element with
        | "Library" -> V4DistributionKind.LibraryKind
        | "Specs" -> V4DistributionKind.SpecsKind
        | "Application" -> V4DistributionKind.ApplicationKind
        | other ->
            fail "invalid_distribution_shape" location $"'{other}' is not a distribution kind."

    let private accessOf location element =
        match text location element with
        | "Public" -> Public
        | "Private" -> Private
        | other -> fail "invalid_access" location $"'{other}' is not an access level."

    /// The schema's minimum path budget.
    [<Literal>]
    let MinimumPathBudget = 64

    let private pathBudgetOf location (element: JsonElement) =
        let mutable budget = 0

        if
            element.ValueKind = JsonValueKind.Number
            && element.TryGetInt32(&budget)
            && budget
               >= MinimumPathBudget
        then
            budget
        else
            fail
                "invalid_path_budget"
                location
                $"A path budget is a whole number of characters, at least {MinimumPathBudget}."

    let private layoutOf location element =
        match text location element with
        | "VfsMode"
        | "Classic" -> ()
        | other -> fail "invalid_type" location $"'{other}' is not a layout."

    let private formatVersion (json: Utf8JsonWriter) = json.WriteNumber("formatVersion", 4)

    let private names (json: Utf8JsonWriter) location (memberName: string) (items: Name list) =
        json.WritePropertyName memberName
        json.WriteStartArray()

        items
        |> List.iteri (fun index item ->
            V4JsonWriter.nameNode json (itemAt (memberAt location memberName) index) item
        )

        json.WriteEndArray()

    /// <summary>
    /// Writes a manifest file: <c>formatVersion</c>, <c>distribution</c>, <c>package</c>, <c>pathBudget</c>, then
    /// <c>dependencies</c> when there are some and <c>entryPoints</c> for an <c>Application</c>.
    /// </summary>
    let writeManifest (pathBudget: int) (manifest: V4Manifest) : Result<byte array, V4WriteError> =
        V4JsonWriter.writeWith
            true
            (fun json ->
                json.WriteStartObject()
                formatVersion json
                json.WriteString("distribution", kindText manifest.Kind)
                json.WritePropertyName "package"
                let (PackageName package) = manifest.Package
                V4JsonWriter.pathNode json (memberAt "$" "package") package
                json.WriteNumber("pathBudget", pathBudget)

                if not manifest.Dependencies.IsEmpty then
                    json.WritePropertyName "dependencies"
                    json.WriteStartArray()

                    manifest.Dependencies
                    |> List.iteri (fun index (PackageName dependency) ->
                        V4JsonWriter.pathNode
                            json
                            (itemAt (memberAt "$" "dependencies") index)
                            dependency
                    )

                    json.WriteEndArray()

                if manifest.Kind = V4DistributionKind.ApplicationKind then
                    json.WritePropertyName "entryPoints"

                    V4JsonWriter.entryPointsNode
                        json
                        (memberAt "$" "entryPoints")
                        manifest.EntryPoints

                json.WriteEndObject()
            )

    /// <summary>
    /// Reads a manifest file. <c>version</c> and <c>created</c> are dropped with a notice each; <c>layout</c> is
    /// checked and dropped, as it describes the tree; <c>pathBudget</c> is required (decision 0012).
    /// </summary>
    let readManifest (root: JsonElement) : Result<V4ManifestFile, V4JsonError> =
        guarded (fun () ->
            let values = fileMembers root

            closed
                "$"
                [
                    "formatVersion"
                    "distribution"
                    "package"
                    "pathBudget"
                    "version"
                    "created"
                    "layout"
                    "dependencies"
                    "entryPoints"
                ]
                values

            let kind = required "$" [ "distribution" ] kindOf values
            let package = PackageName(required "$" [ "package" ] path values)
            let budget = required "$" [ "pathBudget" ] pathBudgetOf values

            let dropped =
                [ "version"; "created" ]
                |> List.choose (fun memberName ->
                    optional "$" [ memberName ] text values
                    |> Option.map (fun _ -> DroppedManifestMember memberName)
                )

            optional "$" [ "layout" ] layoutOf values
            |> ignore

            let dependencies =
                optional "$" [ "dependencies" ] (listOf path) values
                |> Option.defaultValue []
                |> List.map PackageName

            let entryPoints =
                optional "$" [ "entryPoints" ] V4JsonDistributions.entryPoints values
                |> Option.defaultValue []

            {
                Manifest = {
                    Kind = kind
                    Package = package
                    Dependencies = dependencies
                    EntryPoints = entryPoints
                }
                PathBudget = budget
                Dropped = dropped
            }
        )

    /// <summary>
    /// Writes a module file: <c>formatVersion</c>, <c>path</c>, <c>access</c> only for a private module, <c>doc</c>,
    /// <c>types</c> and <c>values</c> as arrays of names, and <c>fileNames</c> when a stem was cut.
    /// </summary>
    let writeModule
        (ModulePath modulePath)
        (document: V4ModuleDocument)
        (fileNames: (Name * string) list)
        : Result<byte array, V4WriteError> =
        V4JsonWriter.writeWith
            true
            (fun json ->
                json.WriteStartObject()
                formatVersion json
                json.WritePropertyName "path"
                V4JsonWriter.pathNode json (memberAt "$" "path") modulePath

                match document.Access with
                | Some Private -> json.WriteString("access", "Private")
                | Some Public
                | None -> ()

                V4JsonWriter.docMember json "$" document.Doc
                names json "$" "types" document.TypeNames
                names json "$" "values" document.ValueNames

                if not fileNames.IsEmpty then
                    json.WritePropertyName "fileNames"
                    json.WriteStartObject()

                    for fileName, stem in fileNames do
                        json.WritePropertyName(CanonicalNames.nameToString fileName)
                        json.WriteStringValue stem

                    json.WriteEndObject()

                json.WriteEndObject()
            )

    /// The names of a module's types or values: an array of names, or an object of inline entries.
    let private entries
        (decodeInline: string -> JsonElement -> 'document)
        location
        (element: JsonElement)
        =
        match element.ValueKind with
        | JsonValueKind.Array -> listOf name location element, []
        | JsonValueKind.Object ->
            let found = nameMap decodeInline location element
            List.map fst found, found
        | _ ->
            fail "invalid_type" location "Expected an array of names or an object of definitions."

    let private fileNamesOf location (element: JsonElement) =
        members location element
        |> List.map (fun (key, item) ->
            let here = memberAt location key
            let fileName = nameText here key
            let stem = text here item

            match TreeStems.parseStem stem with
            | Ok(CutStem(prefix, digest)) when TreeStems.matchesCut prefix digest fileName ->
                fileName, stem
            | _ ->
                fail
                    "invalid_file_name"
                    here
                    $"'{stem}' is not the cut stem of '{key}': a cut stem is a prefix of the escaped name, '__' and the first eight hex digits of its SHA-256."
        )

    /// A module's doc: a string, or an array of strings that are its lines (the schema's ModuleManifestFile).
    let private moduleDocOf location (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Array ->
            listOf text location element
            |> String.concat "\n"
        | _ -> text location element

    /// <summary>
    /// Reads a module file. A module of a package definition (<paramref name="holdsDefinitions"/>) with no
    /// <c>access</c> is public, the default of the parent's "access (module manifest)"; a module of a package
    /// specification keeps what it states, which <c>V4DocumentTree.join</c> checks. Module annotations have no place
    /// in a 4.0.0 module file, so the document has none.
    /// </summary>
    let readModule
        (holdsDefinitions: bool)
        (root: JsonElement)
        : Result<V4ModuleFile, V4JsonError> =
        guarded (fun () ->
            let values = fileMembers root

            closed
                "$"
                [
                    "formatVersion"
                    "path"
                    "module"
                    "access"
                    "doc"
                    "types"
                    "values"
                    "fileNames"
                ]
                values

            let modulePath = ModulePath(required "$" [ "path"; "module" ] path values)

            let access =
                match optional "$" [ "access" ] accessOf values with
                | Some stated -> Some stated
                | None when holdsDefinitions -> Some Public
                | None -> None

            let inlineType location element =
                if holdsDefinitions then
                    DefinedType(V4JsonDistributions.controlledTypeDefinition location element)
                else
                    SpecifiedType(V4JsonDistributions.typeSpecification location element)

            let inlineValue location element =
                if holdsDefinitions then
                    DefinedValue(V4JsonDistributions.controlledValueDefinition location element)
                else
                    SpecifiedValue(V4JsonDistributions.valueSpecification location element)

            let typeNames, inlineTypes =
                optional "$" [ "types" ] (entries inlineType) values
                |> Option.defaultValue ([], [])

            let valueNames, inlineValues =
                optional "$" [ "values" ] (entries inlineValue) values
                |> Option.defaultValue ([], [])

            let fileNames =
                optional "$" [ "fileNames" ] fileNamesOf values
                |> Option.defaultValue []

            for fileName, _ in fileNames do
                if
                    not (List.contains fileName typeNames)
                    && not (List.contains fileName valueNames)
                then
                    fail
                        "invalid_file_name"
                        (memberAt (memberAt "$" "fileNames") (CanonicalNames.nameToString fileName))
                        $"'{CanonicalNames.nameToString fileName}' is in fileNames, and the module lists no type or value of that name."

            {
                Path = modulePath
                Document = {
                    Access = access
                    Doc = optional "$" [ "doc" ] moduleDocOf values
                    Annotations = []
                    TypeNames = typeNames
                    ValueNames = valueNames
                }
                FileNames = fileNames
                InlineTypes = inlineTypes
                InlineValues = inlineValues
            }
        )

    let private writeNode
        (name: Name)
        (writeBody: Utf8JsonWriter -> unit)
        : Result<byte array, V4WriteError> =
        V4JsonWriter.writeWith
            true
            (fun json ->
                json.WriteStartObject()
                formatVersion json
                json.WritePropertyName "name"
                V4JsonWriter.nameNode json (memberAt "$" "name") name
                writeBody json
                json.WriteEndObject()
            )

    /// <summary>Writes a type file: <c>formatVersion</c>, <c>name</c>, then <c>def</c> or <c>spec</c>.</summary>
    let writeType (name: Name) (document: V4TypeDocument) : Result<byte array, V4WriteError> =
        writeNode
            name
            (fun json ->
                match document with
                | DefinedType definition ->
                    json.WritePropertyName "def"
                    V4JsonWriter.typeDefinitionNode json (memberAt "$" "def") definition
                | SpecifiedType specification ->
                    json.WritePropertyName "spec"
                    V4JsonWriter.typeSpecificationNode json (memberAt "$" "spec") specification
            )

    /// <summary>Writes a value file: <c>formatVersion</c>, <c>name</c>, then <c>def</c> or <c>spec</c>.</summary>
    let writeValue (name: Name) (document: V4ValueDocument) : Result<byte array, V4WriteError> =
        writeNode
            name
            (fun json ->
                match document with
                | DefinedValue definition ->
                    json.WritePropertyName "def"
                    V4JsonWriter.valueDefinitionNode json (memberAt "$" "def") definition
                | SpecifiedValue specification ->
                    json.WritePropertyName "spec"
                    V4JsonWriter.valueSpecificationNode json (memberAt "$" "spec") specification
            )

    /// Reads a type or value file: its name and exactly one of def and spec.
    let private readNode
        (defined: string -> JsonElement -> 'document)
        (specified: string -> JsonElement -> 'document)
        root
        =
        guarded (fun () ->
            let values = fileMembers root
            closed "$" [ "formatVersion"; "name"; "def"; "spec" ] values
            let nodeName = required "$" [ "name" ] name values

            let document =
                match hasMember "def" values, hasMember "spec" values with
                | true, true ->
                    fail
                        "invalid_distribution_shape"
                        (memberAt "$" "spec")
                        "A node file has exactly one of def and spec."
                | true, false -> required "$" [ "def" ] defined values
                | false, true -> required "$" [ "spec" ] specified values
                | false, false -> fail "missing_member" "$" "A node file has def or spec."

            nodeName, document
        )

    /// <summary>Reads a type file: its name, and a <c>DefinedType</c> for <c>def</c> or a <c>SpecifiedType</c> for
    /// <c>spec</c>.</summary>
    let readType (root: JsonElement) : Result<Name * V4TypeDocument, V4JsonError> =
        readNode
            (fun location element ->
                DefinedType(V4JsonDistributions.controlledTypeDefinition location element)
            )
            (fun location element ->
                SpecifiedType(V4JsonDistributions.typeSpecification location element)
            )
            root

    /// <summary>Reads a value file: its name, and a <c>DefinedValue</c> for <c>def</c> or a <c>SpecifiedValue</c>
    /// for <c>spec</c>.</summary>
    let readValue (root: JsonElement) : Result<Name * V4ValueDocument, V4JsonError> =
        readNode
            (fun location element ->
                DefinedValue(V4JsonDistributions.controlledValueDefinition location element)
            )
            (fun location element ->
                SpecifiedValue(V4JsonDistributions.valueSpecification location element)
            )
            root
