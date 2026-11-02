namespace Morphir.Json.V4

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4

/// <summary>Why IR v4 JSON could not be read.</summary>
type V4JsonError = {
    /// <summary>A stable rule identifier, such as <c>invalid_name</c>.</summary>
    Rule: string
    /// <summary>The JSON location of the problem.</summary>
    Location: string
    /// <summary>A readable description.</summary>
    Message: string
}

exception internal V4JsonFailure of V4JsonError

/// Shared helpers for walking JSON by hand. They raise <see cref="V4JsonFailure"/>, which the public readers
/// turn into a result. They use no runtime reflection.
module internal V4JsonDecoding =
    /// An object's members in document order.
    type Members = (string * JsonElement) list

    let fail rule location message : 'a =
        raise (
            V4JsonFailure
                {
                    Rule = rule
                    Location = location
                    Message = message
                }
        )

    let memberAt (location: string) (name: string) =
        let isPlain =
            name <> ""
            && name
               |> Seq.forall (fun c ->
                   Char.IsAsciiLetterOrDigit c
                   || c = '-'
                   || c = '_'
               )

        if isPlain then
            location
            + "."
            + name
        else
            location
            + "[\""
            + name
            + "\"]"

    let itemAt (location: string) (index: int) =
        location
        + "["
        + string index
        + "]"

    /// The deepest nesting a reader accepts, the same as the Ion reader's default. The root counts as one.
    /// A caller may parse JSON with any maximum depth, and decoding recurses.
    [<Literal>]
    let MaxDepth = 512

    /// Guards each recursive decoding step on a thread with an unusually small stack.
    let enterNode () =
        RuntimeHelpers.EnsureSufficientExecutionStack()

    /// Decodes JSON text. A string or member name holding a lone surrogate is valid JSON syntax but has no
    /// .NET string, and decoding it raises InvalidOperationException.
    let private decodedText rule location (decode: unit -> string) =
        try
            decode ()
        with :? InvalidOperationException ->
            fail rule location "The text holds a lone surrogate."

    /// Reads a JSON string, using the given rule for anything that is not readable text.
    let stringOf rule location (element: JsonElement) =
        if
            element.ValueKind
            <> JsonValueKind.String
        then
            fail rule location "Expected a string."

        decodedText rule location (fun () -> element.GetString())

    /// Reads a member's name. The location is the object's, since the name itself may not be printable.
    let private memberName location (property: JsonProperty) =
        decodedText "invalid_type" location (fun () -> property.Name)

    let rec private checkMembers depth location (element: JsonElement) =
        if depth > MaxDepth then
            fail "depth_limit_exceeded" location "The JSON is nested too deeply to read."

        match element.ValueKind with
        | JsonValueKind.Object ->
            let seen = HashSet<string>(StringComparer.Ordinal)

            for property in element.EnumerateObject() do
                let key = memberName location property
                let here = memberAt location key

                if not (seen.Add key) then
                    fail "duplicate_member" here $"The member '{key}' appears more than once."

                checkMembers (depth + 1) here property.Value
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.iteri (fun i item -> checkMembers (depth + 1) (itemAt location i) item)
        | _ -> ()

    /// Refuses JSON past the depth limit and a repeated member in any object, since JsonElement lookups
    /// would silently take one of them. This runs before decoding, so decoding recursion stays bounded.
    let checkDuplicates location element = checkMembers 1 location element

    let members location (element: JsonElement) : Members =
        if
            element.ValueKind
            <> JsonValueKind.Object
        then
            fail "invalid_type" location "Expected an object."

        [
            for property in element.EnumerateObject() ->
                memberName location property, property.Value
        ]

    let items location (element: JsonElement) =
        if
            element.ValueKind
            <> JsonValueKind.Array
        then
            fail "invalid_type" location "Expected an array."

        element.EnumerateArray()
        |> Seq.toList

    /// Decodes each array item at its own location.
    let listOf decode location element =
        items location element
        |> List.mapi (fun i item -> decode (itemAt location i) item)

    let text location element =
        stringOf "invalid_type" location element

    /// Refuses any member outside the allowed names.
    let closed location (allowed: string list) (values: Members) =
        for name, _ in values do
            if not (List.contains name allowed) then
                fail "unknown_member" (memberAt location name) $"Unknown member '{name}'."

    let hasMember name (values: Members) =
        values
        |> List.exists (fun (candidate, _) -> candidate = name)

    let without name (values: Members) =
        values
        |> List.filter (fun (candidate, _) ->
            candidate
            <> name
        )

    /// Finds a member by its canonical name or an accepted legacy one, and refuses more than one of them.
    let tryPick location (aliases: string list) (values: Members) =
        match
            values
            |> List.filter (fun (name, _) -> List.contains name aliases)
        with
        | [] -> None
        | [ found ] -> Some found
        | _ :: (second, _) :: _ ->
            fail
                "unknown_member"
                (memberAt location second)
                $"The member '{second}' repeats an earlier spelling."

    let optional location aliases decode values =
        tryPick location aliases values
        |> Option.map (fun (name, element) -> decode (memberAt location name) element)

    let required location (aliases: string list) decode values =
        match tryPick location aliases values with
        | Some(name, element) -> decode (memberAt location name) element
        | None -> fail "missing_member" location $"Missing required member '{aliases.Head}'."

    /// Finds the one tag a wrapper object holds, and refuses anything beside it.
    let tagWith rule location (tags: string list) (values: Members) =
        match
            values
            |> List.tryFind (fun (name, _) -> List.contains name tags)
        with
        | None -> fail rule location "The object is not a node this position allows."
        | Some(tag, payload) ->
            match
                values
                |> List.tryFind (fun (name, _) ->
                    name
                    <> tag
                )
            with
            | Some(extra, _) ->
                fail
                    "unknown_member"
                    (memberAt location extra)
                    $"Unknown member '{extra}' beside '{tag}'."
            | None -> tag, payload

    let nodeTag location tags values =
        tagWith "unknown_node" location tags values

    /// Reads a payload that must be an empty object.
    let emptyPayload location element =
        closed location [] (members location element)

    let private invalidName location result =
        match result with
        | Ok value -> value
        | Error message -> fail "invalid_name" location message

    let nameText location input =
        invalidName location (CanonicalNames.parseName input)

    let fqNameText location input =
        invalidName location (CanonicalNames.parseFQName input)

    /// Reads the text of a name, path or FQName.
    let nameString location element =
        stringOf "invalid_name" location element

    /// Reads a canonical name string or a legacy word array.
    let name location (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String -> nameText location (nameString location element)
        | JsonValueKind.Array ->
            let words =
                element.EnumerateArray()
                |> Seq.map (nameString location)
                |> Seq.toList

            invalidName location (CanonicalNames.parseLegacyName words)
        | _ -> fail "invalid_type" location "Expected a name."

    /// Reads a canonical path string or a legacy array of names.
    let path location (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String ->
            invalidName location (CanonicalNames.parsePath (nameString location element))
        | JsonValueKind.Array ->
            match listOf name location element with
            | [] -> fail "invalid_name" location "A path needs at least one name."
            | names -> Path names
        | _ -> fail "invalid_type" location "Expected a path."

    /// Reads a canonical FQName string or a legacy [package, module, name] array.
    let fqName location (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String -> fqNameText location (nameString location element)
        | JsonValueKind.Array ->
            match items location element with
            | [ package; modulePath; local ] -> {
                PackagePath = PackageName(path (itemAt location 0) package)
                ModulePath = ModulePath(path (itemAt location 1) modulePath)
                LocalName = name (itemAt location 2) local
              }
            | _ -> fail "invalid_name" location "A legacy FQName is [package, module, name]."
        | _ -> fail "invalid_type" location "Expected an FQName."

    /// Whether a bare string is written as an FQName rather than a name.
    let looksQualified (input: string) =
        input.Contains ':'
        || input.Contains '#'

    /// Refuses a name that appears twice among ordered entries.
    let uniqueNames location (entries: (Name * 'v) list) =
        let seen = HashSet<Name>()

        for key, _ in entries do
            if not (seen.Add key) then
                fail
                    "duplicate_member"
                    location
                    $"The name '{CanonicalNames.nameToString key}' appears more than once."

        entries

    /// Reads an object keyed by name in document order. Two spellings of one name are a duplicate.
    let nameMap decode location element =
        members location element
        |> List.map (fun (key, item) ->
            let here = memberAt location key
            nameText here key, decode here item
        )
        |> uniqueNames location

    let orderedMap location (describe: 'k -> string) entries =
        match OrderedMap.ofList entries with
        | Ok map -> map
        | Error key ->
            fail "duplicate_member" location $"The key '{describe key}' appears more than once."

    /// Reads an object keyed by name into a map whose order does not count.
    let orderedNameMap decode location element =
        nameMap decode location element
        |> orderedMap location CanonicalNames.nameToString

    /// Reads an object keyed by path into a map whose order does not count.
    let orderedPathMap wrap describe decode location element =
        members location element
        |> List.map (fun (key, item) ->
            let here = memberAt location key
            wrap (invalidName here (CanonicalNames.parsePath key)), decode here item
        )
        |> orderedMap location describe

    let mergeDoc location (outer: string option) (inner: Documented<'t>) =
        match outer, inner.Doc with
        | Some _, Some _ ->
            fail
                "duplicate_member"
                (memberAt location "doc")
                "The nested wrapper states a second doc."
        | Some _, None -> { inner with Doc = outer }
        | None, _ -> inner

    /// Reads a flattened doc beside its variant, or the nested { doc, value } wrapper that decision 0010
    /// accepts for one release.
    let rec documentedMembers
        (location: string)
        decodeMembers
        decodeOther
        (values: Members)
        : Documented<'t> =
        let doc = optional location [ "doc" ] text values
        let rest = without "doc" values

        if hasMember "value" rest then
            closed location [ "value" ] rest

            required location [ "value" ] (documented decodeMembers decodeOther) rest
            |> mergeDoc location doc
        else
            {
                Doc = doc
                Value = decodeMembers location rest
            }

    /// Reads a documented object, or hands any other shape to <paramref name="decodeOther"/>.
    and documented decodeMembers decodeOther location (element: JsonElement) : Documented<'t> =
        if element.ValueKind = JsonValueKind.Object then
            documentedMembers location decodeMembers decodeOther (members location element)
        else
            {
                Doc = None
                Value = decodeOther location element
            }

    let notAnObject location (_: JsonElement) : 't =
        fail "invalid_type" location "Expected an object."

    let private accessTags = [ "Public"; "Private"; "public"; "private"; "pub" ]

    let private accessOf location (spelling: string) =
        match spelling with
        | "Public"
        | "public"
        | "pub" -> Public
        | "Private"
        | "private" -> Private
        | other -> fail "invalid_access" location $"'{other}' is not an access level."

    /// Reads the three spellings of access control: the access level as the key, access beside the
    /// flattened definition, and the legacy { access, value } shape.
    let accessControlled decodeMembers decodeElement location element : AccessControlled<'t> =
        let values = members location element

        if
            values
            |> List.exists (fun (key, _) -> List.contains key accessTags)
        then
            let tag, payload = nodeTag location accessTags values

            {
                Access = accessOf location tag
                Value = decodeElement (memberAt location tag) payload
            }
        else
            match
                values
                |> List.tryFind (fun (key, _) -> key = "access")
            with
            | Some(_, spelling) ->
                let here = memberAt location "access"

                {
                    Access = accessOf here (text here spelling)
                    Value = decodeMembers location (without "access" values)
                }
            | None -> fail "invalid_access" location "The entry states no access level."
