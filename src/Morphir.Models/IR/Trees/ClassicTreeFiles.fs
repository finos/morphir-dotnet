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
/// The codes are the parent's (its "Validation Rules"). A file whose <c>formatVersion</c> is not the string
/// <c>"3.1.0"</c>, the manifest included, is refused with <c>version_mismatch</c> at <c>#/formatVersion</c>. A v3
/// manifest has no member beside <c>formatVersion</c>, <c>distribution</c>, <c>package</c>, <c>pathBudget</c> and
/// <c>dependencies</c>; a module specification has no <c>access</c>, and a module file has no <c>module</c> alias for
/// <c>path</c>: each is an <c>unknown_member</c>. A <c>def</c> wrapper has only <c>access</c> and <c>value</c>, a
/// documented entry only <c>doc</c> and <c>value</c>: another member is <c>unknown_member</c> at that member, as
/// in a single file. A <c>spec</c> with <c>access</c> is the parent's <c>invalid_distribution_shape</c> at
/// <c>#/spec</c>. A payload that is not the classic JSON of its
/// kind is <c>invalid_type</c>, and a member below the wrapper that no classic v3 shape has, at any depth, is
/// <c>unknown_member</c> at that member (<c>ClassicMembers</c>), so no conversion drops it.
/// </remarks>
module internal ClassicTreeFiles =
    open V4JsonDecoding

    /// <summary>The <c>formatVersion</c> of every file of a v3 tree.</summary>
    [<Literal>]
    let FormatVersion = "3.1.0"

    /// The format version check of a v3 tree file: the string "3.1.0", or version_mismatch.
    let private checkVersion (values: Members) =
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
                "version_mismatch"
                (memberAt "$" "formatVersion")
                $"Every file of a v3 tree says \"formatVersion\": \"{FormatVersion}\", and this one says {element.GetRawText()}."
        | _ ->
            fail
                "duplicate_format_version"
                (memberAt "$" "formatVersion")
                "The member 'formatVersion' appears more than once."

    let private fileMembers = TreeFileCodec.fileMembers checkVersion

    let checkFileVersion root =
        TreeFileCodec.guarded (fun () ->
            fileMembers root
            |> ignore
        )

    let readNodeHeader holdsDefinitions root =
        TreeFileCodec.readNode checkVersion (fun _ _ -> ()) (fun _ _ -> ()) holdsDefinitions root

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

    /// A payload decoded by the classic reader, whose failure is the payload's invalid_type, then checked for a member
    /// that the classic model has no place for (ClassicMembers), which is unknown_member at that member.
    let private payload
        (decode: string -> JsonElement -> 'a)
        (members: string -> JsonElement -> unit)
        location
        element
        =
        try
            let decoded = decode location element
            members location element
            decoded
        with
        | ClassicJsonFailure error when error.Rule = "unknown_member" ->
            fail "unknown_member" error.Location error.Message
        | ClassicJsonFailure error -> fail "invalid_type" error.Location error.Message
        | :? InvalidOperationException as problem -> fail "invalid_type" location problem.Message
        | :? FormatException as problem -> fail "invalid_type" location problem.Message

    let private shape location message =
        fail "invalid_distribution_shape" location message

    /// A documented entry, { "doc", "value" }, has no other member: another is unknown_member at that member. An entry
    /// without a doc is the bare entry, as the classic writer writes it.
    let private checkDocumented location (element: JsonElement) =
        let mutable doc = Unchecked.defaultof<JsonElement>

        if
            element.ValueKind = JsonValueKind.Object
            && element.TryGetProperty("doc", &doc)
        then
            for key, _ in members location element do
                if
                    key
                    <> "doc"
                    && key
                       <> "value"
                then
                    fail
                        "unknown_member"
                        (memberAt location key)
                        $"Unknown member '{key}': a documented entry has doc and value, and the classic v3 model has no place for another member."

    /// A def wrapper, { "access", "value" }, has no other member (another is unknown_member at that member), and its
    /// value is a documented entry.
    let private checkDefinition location (element: JsonElement) =
        if element.ValueKind = JsonValueKind.Object then
            for key, item in members location element do
                match key with
                | "access" -> ()
                | "value" -> checkDocumented (memberAt location "value") item
                | other ->
                    fail
                        "unknown_member"
                        (memberAt location other)
                        $"Unknown member '{other}': a def payload has access and value, and the classic v3 model has no place for another member."

    /// A spec payload is a documented entry with no access.
    let private checkSpecification location (element: JsonElement) =
        let mutable access = Unchecked.defaultof<JsonElement>

        if
            element.ValueKind = JsonValueKind.Object
            && element.TryGetProperty("access", &access)
        then
            shape location "A spec payload has no access."

        checkDocumented location element

    let private typeDocument holdsDefinitions location element =
        if holdsDefinitions then
            checkDefinition location element

            ClassicTypeDocument.DefinedType(
                payload
                    ClassicJsonDecoder.typeDefinitionEntry
                    ClassicMembers.typeDefinitionEntry
                    location
                    element
            )
        else
            checkSpecification location element

            ClassicTypeDocument.SpecifiedType(
                payload
                    ClassicJsonDecoder.typeSpecificationEntry
                    ClassicMembers.typeSpecificationEntry
                    location
                    element
            )

    let private valueDocument holdsDefinitions location element =
        if holdsDefinitions then
            checkDefinition location element

            ClassicValueDocument.DefinedValue(
                payload
                    ClassicJsonDecoder.valueDefinitionEntry
                    ClassicMembers.valueDefinitionEntry
                    location
                    element
            )
        else
            checkSpecification location element

            ClassicValueDocument.SpecifiedValue(
                payload
                    ClassicJsonDecoder.valueSpecificationEntry
                    ClassicMembers.valueSpecificationEntry
                    location
                    element
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
    /// <c>version_mismatch</c>; the manifest records the single-file label of its kind.
    /// </summary>
    let readManifest (root: JsonElement) : Result<ClassicManifestFile, V4JsonError> =
        TreeFileCodec.guarded (fun () ->
            let values = fileMembers root

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
    let private readModuleWith inlineType inlineValue (holdsDefinitions: bool) (root: JsonElement) =
        TreeFileCodec.guarded (fun () ->
            let values = fileMembers root

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
                inlineType
                inlineValue
                (fun access doc typeNames valueNames -> {
                    Access = access
                    Doc = doc
                    TypeNames = typeNames
                    ValueNames = valueNames
                })
                values
        )

    let readModule
        (holdsDefinitions: bool)
        (root: JsonElement)
        : Result<ClassicModuleFile, V4JsonError> =
        // The attributes of an inline entry outlive the document they were parsed from.
        readModuleWith
            (typeDocument holdsDefinitions)
            (valueDocument holdsDefinitions)
            holdsDefinitions
            (root.Clone())

    /// Reads inventory metadata and inline names without decoding their payloads.
    let readModuleInventory holdsDefinitions =
        readModuleWith (fun _ _ -> ()) (fun _ _ -> ()) holdsDefinitions

    let readInlineType holdsDefinitions location (element: JsonElement) =
        TreeFileCodec.guarded (fun () -> typeDocument holdsDefinitions location (element.Clone()))

    let readInlineValue holdsDefinitions location (element: JsonElement) =
        TreeFileCodec.guarded (fun () -> valueDocument holdsDefinitions location (element.Clone()))

    /// A payload written by the classic writer into the file's JSON. The writer's error stops the write, which gives
    /// it as its error.
    let private writePayload
        (memberName: string)
        (write: ClassicWireProfile -> Utf8JsonWriter -> 'a -> Result<unit, ClassicWriteError>)
        (entry: 'a)
        (json: Utf8JsonWriter)
        =
        json.WritePropertyName memberName

        match write ClassicWireProfile.v3 json entry with
        | Ok() -> ()
        | Error error ->
            V4JsonWriter.failWith
                {
                    Rule = error.Rule
                    Location = error.Location
                    Message = error.Message
                }

    /// <summary>Writes a type file: <c>formatVersion</c>, <c>name</c>, then <c>def</c> or <c>spec</c>.</summary>
    let writeType
        (name: Name)
        (document: ClassicTypeDocument<JsonElement>)
        : Result<byte array, V4WriteError> =
        TreeFileCodec.writeNode
            formatVersion
            name
            (fun json ->
                match document with
                | ClassicTypeDocument.DefinedType definition ->
                    writePayload "def" ClassicJsonWriter.writeTypeDefinitionEntry definition json
                | ClassicTypeDocument.SpecifiedType specification ->
                    writePayload
                        "spec"
                        ClassicJsonWriter.writeTypeSpecificationEntry
                        specification
                        json
            )

    /// <summary>Writes a value file: <c>formatVersion</c>, <c>name</c>, then <c>def</c> or <c>spec</c>.</summary>
    let writeValue
        (name: Name)
        (document: ClassicValueDocument<JsonElement, JsonElement>)
        : Result<byte array, V4WriteError> =
        TreeFileCodec.writeNode
            formatVersion
            name
            (fun json ->
                match document with
                | ClassicValueDocument.DefinedValue definition ->
                    writePayload "def" ClassicJsonWriter.writeValueDefinitionEntry definition json
                | ClassicValueDocument.SpecifiedValue specification ->
                    writePayload
                        "spec"
                        ClassicJsonWriter.writeValueSpecificationEntry
                        specification
                        json
            )

    /// <summary>Reads a type file: its name, and a <c>DefinedType</c> for <c>def</c> in a package definition or a
    /// <c>SpecifiedType</c> for <c>spec</c> in a package specification.</summary>
    let readType
        (holdsDefinitions: bool)
        (root: JsonElement)
        : Result<Name * ClassicTypeDocument<JsonElement>, V4JsonError> =
        TreeFileCodec.readNode
            checkVersion
            (typeDocument true)
            (typeDocument false)
            holdsDefinitions
            (root.Clone())

    /// <summary>Reads a value file: its name, and a <c>DefinedValue</c> for <c>def</c> in a package definition or a
    /// <c>SpecifiedValue</c> for <c>spec</c> in a package specification.</summary>
    let readValue
        (holdsDefinitions: bool)
        (root: JsonElement)
        : Result<Name * ClassicValueDocument<JsonElement, JsonElement>, V4JsonError> =
        TreeFileCodec.readNode
            checkVersion
            (valueDocument true)
            (valueDocument false)
            holdsDefinitions
            (root.Clone())

    [<Struct>]
    type private ClassicDecimalWork =
        | CheckValue of valueElem: JsonElement
        | CheckPattern of patternElem: JsonElement
        | CheckDefinition of defElem: JsonElement

    /// Reads up to 6 elements of a JSON array in a single O(1) EnumerateArray pass without index-based O(N) lookups.
    let private readUpTo6 (element: JsonElement) =
        let mutable count = 0
        let mutable e0 = Unchecked.defaultof<JsonElement>
        let mutable e1 = Unchecked.defaultof<JsonElement>
        let mutable e2 = Unchecked.defaultof<JsonElement>
        let mutable e3 = Unchecked.defaultof<JsonElement>
        let mutable e4 = Unchecked.defaultof<JsonElement>
        let mutable enumerator = element.EnumerateArray()

        while count < 6 && enumerator.MoveNext() do
            match count with
            | 0 -> e0 <- enumerator.Current
            | 1 -> e1 <- enumerator.Current
            | 2 -> e2 <- enumerator.Current
            | 3 -> e3 <- enumerator.Current
            | 4 -> e4 <- enumerator.Current
            | _ -> ()

            count <- count + 1

        count, e0, e1, e2, e3, e4

    let private tryReadPair (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Array then
            ValueNone
        else
            let mutable count = 0
            let mutable first = Unchecked.defaultof<JsonElement>
            let mutable second = Unchecked.defaultof<JsonElement>
            let mutable enumerator = element.EnumerateArray()

            while count < 3 && enumerator.MoveNext() do
                match count with
                | 0 -> first <- enumerator.Current
                | 1 -> second <- enumerator.Current
                | _ -> ()

                count <- count + 1

            if count = 2 then
                ValueSome(struct (first, second))
            else
                ValueNone

    let private checkDecimalLiteral (element: JsonElement) : string option =
        if element.ValueKind <> JsonValueKind.Array then
            None
        else
            let count, tagElem, dataElem, _, _, _ = readUpTo6 element

            if count = 2 && tagElem.ValueKind = JsonValueKind.String then
                let tag = tagElem.GetString()

                if tag = "DecimalLiteral" || tag = "decimal_literal" then
                    let rawOpt =
                        match dataElem.ValueKind with
                        | JsonValueKind.String ->
                            match dataElem.GetString() with
                            | null -> None
                            | text -> Some text
                        | JsonValueKind.Number -> Some(dataElem.GetRawText())
                        | _ -> None

                    match rawOpt with
                    | Some raw ->
                        match Morphir.IR.V4.DecimalLexeme.create raw with
                        | Ok _ when not (Morphir.IR.V4.DecimalLexeme.isLosslessClassicDecimal raw) -> Some raw
                        | _ -> None
                    | None -> None
                else
                    None
            else
                None

    /// Unwraps Classic `{ "access", "value" }` and optional `{ "doc", "value" }` around a `ValueDefinition` object.
    let private tryUnwrapValueDefinitionEntry (entry: JsonElement) : JsonElement voption =
        if entry.ValueKind <> JsonValueKind.Object then
            ValueNone
        else
            let mutable afterAccess = Unchecked.defaultof<JsonElement>

            if entry.TryGetProperty("value", &afterAccess) && afterAccess.ValueKind = JsonValueKind.Object then
                let mutable docElem = Unchecked.defaultof<JsonElement>
                let mutable afterDoc = Unchecked.defaultof<JsonElement>

                if afterAccess.TryGetProperty("doc", &docElem) && afterAccess.TryGetProperty("value", &afterDoc) then
                    ValueSome afterDoc
                else
                    ValueSome afterAccess
            else
                ValueNone

    /// Grammar-aware, O(N), cancellable iterative scan of Classic IR 3.1 `Value` and `Pattern` positions for
    /// decimal literals that lose precision, scale, or sign under single-document Classic JSON serialization.
    /// Skips all opaque attribute slots (`elem[1]`), type definitions, and type expressions.
    let private findLossyDecimalInValueEntry (ct: System.Threading.CancellationToken) (entry: JsonElement) : string option =
        match tryUnwrapValueDefinitionEntry entry with
        | ValueNone -> None
        | ValueSome defElem ->
            let stack = System.Collections.Generic.Stack<ClassicDecimalWork>()
            stack.Push(CheckDefinition defElem)
            let mutable lossy: string option = None

            let pushArrayItems (wrap: JsonElement -> ClassicDecimalWork) (arrElem: JsonElement) =
                if arrElem.ValueKind = JsonValueKind.Array then
                    for item in arrElem.EnumerateArray() do
                        ct.ThrowIfCancellationRequested()
                        stack.Push(wrap item)

            let pushPairSecond (wrap: JsonElement -> ClassicDecimalWork) (pairsElem: JsonElement) =
                if pairsElem.ValueKind = JsonValueKind.Array then
                    for pair in pairsElem.EnumerateArray() do
                        ct.ThrowIfCancellationRequested()

                        match tryReadPair pair with
                        | ValueSome(struct (_, second)) -> stack.Push(wrap second)
                        | ValueNone -> ()

            while lossy.IsNone && stack.Count > 0 do
                ct.ThrowIfCancellationRequested()

                match stack.Pop() with
                | CheckDefinition defObj ->
                    if defObj.ValueKind = JsonValueKind.Object then
                        let mutable body = Unchecked.defaultof<JsonElement>

                        if defObj.TryGetProperty("body", &body) then
                            stack.Push(CheckValue body)

                | CheckPattern patElem ->
                    if patElem.ValueKind = JsonValueKind.Array then
                        let count, tagElem, _attr, e2, e3, _ = readUpTo6 patElem

                        if tagElem.ValueKind = JsonValueKind.String then
                            match tagElem.GetString(), count with
                            | ("LiteralPattern" | "literal_pattern"), 3 ->
                                lossy <- checkDecimalLiteral e2
                            | ("AsPattern" | "as_pattern"), 4 ->
                                stack.Push(CheckPattern e2)
                            | ("TuplePattern" | "tuple_pattern"), 3 ->
                                pushArrayItems CheckPattern e2
                            | ("ConstructorPattern" | "constructor_pattern"), 4 ->
                                pushArrayItems CheckPattern e3
                            | ("HeadTailPattern" | "head_tail_pattern"), 4 ->
                                stack.Push(CheckPattern e3)
                                stack.Push(CheckPattern e2)
                            | _ -> ()

                | CheckValue valElem ->
                    if valElem.ValueKind = JsonValueKind.Array then
                        let count, tagElem, _attr, e2, e3, e4 = readUpTo6 valElem

                        if tagElem.ValueKind = JsonValueKind.String then
                            match tagElem.GetString(), count with
                            | ("Literal" | "literal"), 3 ->
                                lossy <- checkDecimalLiteral e2
                            | ("Tuple" | "tuple"), 3
                            | ("List" | "list"), 3 ->
                                pushArrayItems CheckValue e2
                            | ("Record" | "record"), 3 ->
                                pushPairSecond CheckValue e2
                            | ("Field" | "field"), 4 ->
                                stack.Push(CheckValue e2)
                            | ("Apply" | "apply"), 4 ->
                                stack.Push(CheckValue e3)
                                stack.Push(CheckValue e2)
                            | ("Lambda" | "lambda"), 4 ->
                                stack.Push(CheckValue e3)
                                stack.Push(CheckPattern e2)
                            | ("LetDefinition" | "let_definition"), 5 ->
                                stack.Push(CheckValue e4)
                                stack.Push(CheckDefinition e3)
                            | ("LetRecursion" | "let_recursion"), 4 ->
                                stack.Push(CheckValue e3)
                                pushPairSecond CheckDefinition e2
                            | ("Destructure" | "destructure"), 5 ->
                                stack.Push(CheckValue e4)
                                stack.Push(CheckValue e3)
                                stack.Push(CheckPattern e2)
                            | ("IfThenElse" | "if_then_else"), 5 ->
                                stack.Push(CheckValue e4)
                                stack.Push(CheckValue e3)
                                stack.Push(CheckValue e2)
                            | ("PatternMatch" | "pattern_match"), 4 ->
                                if e3.ValueKind = JsonValueKind.Array then
                                    for pair in e3.EnumerateArray() do
                                        ct.ThrowIfCancellationRequested()

                                        match tryReadPair pair with
                                        | ValueSome(struct (casePat, caseVal)) ->
                                            stack.Push(CheckValue caseVal)
                                            stack.Push(CheckPattern casePat)
                                        | ValueNone -> ()

                                stack.Push(CheckValue e2)
                            | ("UpdateRecord" | "update_record"), 4 ->
                                pushPairSecond CheckValue e3
                                stack.Push(CheckValue e2)
                            | _ -> ()

            lossy

    let findLossyDecimalInInlineValue
        (ct: System.Threading.CancellationToken)
        (holdsDefinitions: bool)
        (element: JsonElement)
        : string option =
        if holdsDefinitions then
            findLossyDecimalInValueEntry ct element
        else
            None

    let findLossyDecimalInSplitValue
        (ct: System.Threading.CancellationToken)
        (holdsDefinitions: bool)
        (document: JsonElement)
        : string option =
        if holdsDefinitions && document.ValueKind = JsonValueKind.Object then
            let mutable defEntry = Unchecked.defaultof<JsonElement>

            if document.TryGetProperty("def", &defEntry) then
                findLossyDecimalInValueEntry ct defEntry
            else
                None
        else
            None

    /// Grammar-aware, O(N), cancellable scan of a single Classic IR document (v1, v2, or v3 Library) for
    /// decimal literals in expression or pattern positions that lose precision, scale, or sign under
    /// single-document Classic JSON serialization. Skips opaque attributes, type definitions, and type expressions.
    let findLossyDecimalInSingleDocument
        (ct: System.Threading.CancellationToken)
        (document: JsonElement)
        : string option =
        let checkModule (modEntry: JsonElement) =
            let mutable modAccess = Unchecked.defaultof<JsonElement>

            let foundModAccess =
                match tryReadPair modEntry with
                | ValueSome(struct (_, second)) ->
                    modAccess <- second
                    true
                | ValueNone ->
                    modEntry.ValueKind = JsonValueKind.Object
                    && modEntry.TryGetProperty("def", &modAccess)

            if not foundModAccess || modAccess.ValueKind <> JsonValueKind.Object then
                None
            else
                let mutable modDef = Unchecked.defaultof<JsonElement>
                let mutable valuesElem = Unchecked.defaultof<JsonElement>

                if
                    modAccess.TryGetProperty("value", &modDef)
                    && modDef.ValueKind = JsonValueKind.Object
                    && modDef.TryGetProperty("values", &valuesElem)
                    && valuesElem.ValueKind = JsonValueKind.Array
                then
                    let mutable lossy: string option = None
                    let mutable enumerator = valuesElem.EnumerateArray()

                    while lossy.IsNone && enumerator.MoveNext() do
                        ct.ThrowIfCancellationRequested()

                        match tryReadPair enumerator.Current with
                        | ValueSome(struct (_, valAccessEntry)) ->
                            lossy <- findLossyDecimalInValueEntry ct valAccessEntry
                        | ValueNone -> ()

                    lossy
                else
                    None

        if document.ValueKind <> JsonValueKind.Object then
            None
        else
            let mutable distElem = Unchecked.defaultof<JsonElement>

            if
                not (document.TryGetProperty("distribution", &distElem))
                || distElem.ValueKind <> JsonValueKind.Array
            then
                None
            else
                let count, kindElem, _pkgElem, _depsElem, pkgDefElem, _ = readUpTo6 distElem

                if
                    count = 4
                    && kindElem.ValueKind = JsonValueKind.String
                    && (kindElem.GetString() = "Library" || kindElem.GetString() = "library")
                    && pkgDefElem.ValueKind = JsonValueKind.Object
                then
                    let mutable modulesElem = Unchecked.defaultof<JsonElement>

                    if
                        pkgDefElem.TryGetProperty("modules", &modulesElem)
                        && modulesElem.ValueKind = JsonValueKind.Array
                    then
                        let mutable lossy: string option = None
                        let mutable enumerator = modulesElem.EnumerateArray()

                        while lossy.IsNone && enumerator.MoveNext() do
                            ct.ThrowIfCancellationRequested()
                            lossy <- checkModule enumerator.Current

                        lossy
                    else
                        None
                else
                    None
