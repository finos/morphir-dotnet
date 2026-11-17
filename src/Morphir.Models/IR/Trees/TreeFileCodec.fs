namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Text
open System.Text.Json
open Morphir.IR
open Morphir.Json.V4

/// <summary>
/// The envelope that the files of a v3 and a v4 document tree share: the members that hold canonical strings (a
/// manifest's package, budget and dependencies, a module's path, names, doc and <c>fileNames</c>, a node file's name),
/// the reserved <c>$meta</c>, and the shape of a module and a node file. Each IR version gives its own
/// <c>formatVersion</c> and payloads (<c>V4TreeFiles</c>, <c>ClassicTreeFiles</c>). The envelope is read
/// with the hand-written JSON helpers of <c>V4JsonDecoding</c> and written with <c>V4JsonWriter</c>, so a reader
/// gives a <c>V4JsonError</c> and a writer a <c>V4WriteError</c> for either version.
/// </summary>
module internal TreeFileCodec =
    open V4JsonDecoding

    /// <summary>The reserved member that a reader ignores and a writer never writes (decision 0014).</summary>
    [<Literal>]
    let MetaMember = "$meta"

    /// The schema's minimum path budget.
    [<Literal>]
    let MinimumPathBudget = 64

    /// Runs a reader that raises <c>V4JsonFailure</c>, as a result.
    let guarded (read: unit -> 'a) : Result<'a, V4JsonError> =
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
    let fileMembers (checkVersion: Members -> unit) (root: JsonElement) : Members =
        let values = members "$" root
        checkVersion values
        checkDuplicates "$" root
        without MetaMember values

    /// The bytes of a written file as text, ending with a line break.
    let toText (bytes: byte array) : string =
        Encoding.UTF8.GetString bytes
        + "\n"

    /// Whether an access member says Private rather than Public. Each IR version has its own access type.
    let isPrivateAccess location element =
        match text location element with
        | "Public" -> false
        | "Private" -> true
        | other -> fail "invalid_access" location $"'{other}' is not an access level."

    let pathBudgetOf location (element: JsonElement) =
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
    /// <c>dependencies</c> when there are some, then what <paramref name="writeRest"/> writes.
    /// </summary>
    let writeManifest
        (writeVersion: Utf8JsonWriter -> unit)
        (distribution: string)
        (PackageName package)
        (pathBudget: int)
        (dependencies: PackageName list)
        (writeRest: Utf8JsonWriter -> unit)
        : Result<byte array, V4WriteError> =
        V4JsonWriter.writeWith
            true
            (fun json ->
                json.WriteStartObject()
                writeVersion json
                json.WriteString("distribution", distribution)
                json.WritePropertyName "package"
                V4JsonWriter.pathNode json (memberAt "$" "package") package
                json.WriteNumber("pathBudget", pathBudget)

                if not dependencies.IsEmpty then
                    json.WritePropertyName "dependencies"
                    json.WriteStartArray()

                    dependencies
                    |> List.iteri (fun index (PackageName dependency) ->
                        V4JsonWriter.pathNode
                            json
                            (itemAt (memberAt "$" "dependencies") index)
                            dependency
                    )

                    json.WriteEndArray()

                writeRest json
                json.WriteEndObject()
            )

    /// The package, the path budget and the dependencies of a manifest file.
    let manifestMembers (values: Members) =
        let package = PackageName(required "$" [ "package" ] path values)
        let budget = required "$" [ "pathBudget" ] pathBudgetOf values

        let dependencies =
            optional "$" [ "dependencies" ] (listOf path) values
            |> Option.defaultValue []
            |> List.map PackageName

        package, budget, dependencies

    /// <summary>
    /// Writes a module file: <c>formatVersion</c>, <c>path</c>, <c>access</c> only for a private module, <c>doc</c>,
    /// <c>types</c> and <c>values</c> as arrays of names, and <c>fileNames</c> when a stem was cut.
    /// </summary>
    let writeModule
        (writeVersion: Utf8JsonWriter -> unit)
        (ModulePath modulePath)
        (isPrivate: bool)
        (doc: string option)
        (typeNames: Name list)
        (valueNames: Name list)
        (fileNames: (Name * string) list)
        : Result<byte array, V4WriteError> =
        V4JsonWriter.writeWith
            true
            (fun json ->
                json.WriteStartObject()
                writeVersion json
                json.WritePropertyName "path"
                V4JsonWriter.pathNode json (memberAt "$" "path") modulePath

                if isPrivate then
                    json.WriteString("access", "Private")

                V4JsonWriter.docMember json "$" doc
                names json "$" "types" typeNames
                names json "$" "values" valueNames

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
    /// Reads the members of a module file that both versions share, after the caller's check of its members: the
    /// path (by <paramref name="pathAliases"/>), the access (<paramref name="accessIn"/>), the doc, the types and values
    /// as names or inline entries, and <c>fileNames</c>, whose every name the module lists.
    /// </summary>
    let readModule
        (pathAliases: string list)
        (accessIn: Members -> 'access)
        (inlineType: string -> JsonElement -> 'typ)
        (inlineValue: string -> JsonElement -> 'value)
        (document: 'access -> string option -> Name list -> Name list -> 'moduleDoc)
        (values: Members)
        : TreeModuleFile<'moduleDoc, 'typ, 'value> =
        let modulePath = ModulePath(required "$" pathAliases path values)
        let access = accessIn values

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
            Document =
                document access (optional "$" [ "doc" ] moduleDocOf values) typeNames valueNames
            FileNames = fileNames
            InlineTypes = inlineTypes
            InlineValues = inlineValues
        }

    /// <summary>Writes a type or value file: <c>formatVersion</c>, <c>name</c>, then what <paramref name="writeBody"/>
    /// writes, its <c>def</c> or <c>spec</c>.</summary>
    let writeNode
        (writeVersion: Utf8JsonWriter -> unit)
        (name: Name)
        (writeBody: Utf8JsonWriter -> unit)
        : Result<byte array, V4WriteError> =
        V4JsonWriter.writeWith
            true
            (fun json ->
                json.WriteStartObject()
                writeVersion json
                json.WritePropertyName "name"
                V4JsonWriter.nameNode json (memberAt "$" "name") name
                writeBody json
                json.WriteEndObject()
            )

    /// Reads a type or value file: its name and exactly one of def and spec.
    let readNode
        (checkVersion: Members -> unit)
        (defined: string -> JsonElement -> 'document)
        (specified: string -> JsonElement -> 'document)
        root
        =
        guarded (fun () ->
            let values = fileMembers checkVersion root
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
