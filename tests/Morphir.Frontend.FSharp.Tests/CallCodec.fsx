// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// Loaded by FSI beside the F# that the backend generates from a compiled model. It decodes the
// JSON arguments of a call observation, calls the generated value and compares the result with
// the expected JSON value. The encodings are the MCK value codecs in conformance/steps/steps-1.md:
// codec 1 is the morphir-elm DataCodec shape, and codec 2 tags constructors with objects.
// Each check writes one line: "call <index> ok", "call <index> differs <json>" or
// "call <index> error <message>".

open System
open System.Globalization
open System.Reflection
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.FSharp.Reflection

let private fail (message: string) : 'T = raise (FormatException message)

let private isGeneric (definition: Type) (target: Type) =
    target.IsGenericType && target.GetGenericTypeDefinition() = definition

/// The words of a Morphir name, as Name.fromString finds them: "LargeSize" and "largeSize" are
/// both [large; size].
let private words (text: string) =
    [ for word in Text.RegularExpressions.Regex.Matches(text, "[a-zA-Z][a-z]*|[0-9]+") -> word.Value.ToLowerInvariant() ]

/// The camelCase form of a name, as Name.toCamelCase writes it.
let private camelCase (text: string) =
    match words text with
    | [] -> text
    | first :: rest -> first + String.concat "" [ for word in rest -> string (Char.ToUpperInvariant word.[0]) + word.Substring 1 ]

/// The Morphir name of an F# union case. The F# Result type calls Err "Error".
let private caseName (target: Type) (case: UnionCaseInfo) =
    if isGeneric typedefof<Result<_, _>> target && case.Name = "Error" then "Err" else case.Name

/// The union case of a value in codec 1: ["name"] or ["name", arguments...].
let private positional (target: Type) (element: JsonElement) =
    if element.ValueKind <> JsonValueKind.Array
       || element.GetArrayLength() = 0
       || element.[0].ValueKind <> JsonValueKind.String then
        fail "A constructor is a JSON array with the constructor name first"

    let name = element.[0].GetString()

    match FSharpType.GetUnionCases target |> Array.tryFind (fun case -> words (caseName target case) = words name) with
    | None -> fail ("Unknown constructor " + name)
    | Some case ->
        let fields = case.GetFields()

        if element.GetArrayLength() - 1 <> fields.Length then
            fail ("The arguments of " + name + " do not match its arity")

        case, fields, Array.ofSeq (Seq.skip 1 (element.EnumerateArray()))

/// The union case of a tagged value in codec 2: "Name", {"Name": value} or {"Name": [values]}.
let private tagged (element: JsonElement) =
    match element.ValueKind with
    | JsonValueKind.String -> element.GetString(), None
    | JsonValueKind.Object ->
        match List.ofSeq (element.EnumerateObject()) with
        | [ property ] -> property.Name, Some property.Value
        | _ -> fail "A constructor with arguments is an object with one member"
    | _ -> fail "A constructor is a JSON string or an object with one member"

/// Decodes a JSON value of the given type in value codec 1 or 2.
let rec decode (codec: int) (target: Type) (element: JsonElement) : obj =
    let decode = decode codec

    if target = typeof<int> then
        box (element.GetInt32())
    elif target = typeof<int64> then
        box (element.GetInt64())
    elif target = typeof<float> then
        box (element.GetDouble())
    elif target = typeof<bool> then
        box (element.GetBoolean())
    elif target = typeof<string> then
        match element.ValueKind with
        | JsonValueKind.String -> box (element.GetString())
        | _ -> fail "A String is a JSON string"
    elif target = typeof<char> then
        match element.ValueKind with
        | JsonValueKind.String when element.GetString().Length = 1 -> box (element.GetString().[0])
        | _ -> fail "A Char is a JSON string of one UTF-16 code unit"
    elif target = typeof<decimal> then
        match element.ValueKind with
        | JsonValueKind.String ->
            box (Decimal.Parse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture))
        | _ -> fail "A Decimal is a JSON string"
    elif target = typeof<unit> then
        match codec, element.ValueKind with
        | 1, _ -> fail "Value codec 1 has no rule for Unit"
        | _, JsonValueKind.Array when element.GetArrayLength() = 0 -> box ()
        | _ -> fail "Unit is an empty JSON array"
    elif isGeneric typedefof<list<_>> target then
        let item = target.GetGenericArguments().[0]
        let empty = target.GetProperty("Empty").GetValue(null)
        let cons = target.GetMethod("Cons")

        List.foldBack
            (fun value rest -> cons.Invoke(null, [| value; rest |]))
            [ for value in element.EnumerateArray() -> decode item value ]
            empty
    elif isGeneric typedefof<option<_>> target then
        let cases = FSharpType.GetUnionCases target
        let item = target.GetGenericArguments().[0]

        if codec = 1 then
            if isGeneric typedefof<option<_>> item then
                fail "Value codec 1 cannot encode Maybe (Maybe a); use value codec 2"

            match element.ValueKind with
            | JsonValueKind.Null -> FSharpValue.MakeUnion(cases.[0], [||])
            | _ -> FSharpValue.MakeUnion(cases.[1], [| decode item element |])
        else
            match tagged element with
            | "Nothing", None -> FSharpValue.MakeUnion(cases.[0], [||])
            | "Just", Some value -> FSharpValue.MakeUnion(cases.[1], [| decode item value |])
            | _ -> fail "A Maybe is \"Nothing\" or {\"Just\": value}"
    elif codec = 2 && isGeneric typedefof<Result<_, _>> target then
        let cases = FSharpType.GetUnionCases target
        let arguments = target.GetGenericArguments()

        match tagged element with
        | "Ok", Some value -> FSharpValue.MakeUnion(cases.[0], [| decode arguments.[0] value |])
        | "Err", Some value -> FSharpValue.MakeUnion(cases.[1], [| decode arguments.[1] value |])
        | _ -> fail "A Result is {\"Ok\": value} or {\"Err\": value}"
    elif FSharpType.IsTuple target then
        let types = FSharpType.GetTupleElements target
        let values = List.ofSeq (element.EnumerateArray())

        if values.Length <> types.Length then
            fail "A tuple is a JSON array with one element for each item"

        FSharpValue.MakeTuple(Array.map2 decode types (Array.ofList values), target)
    elif FSharpType.IsRecord target then
        let fields = FSharpType.GetRecordFields target

        let members =
            element.EnumerateObject()
            |> Seq.map (fun item -> item.Name, item.Value)
            |> Map.ofSeq

        if members.Count <> fields.Length then
            fail "A record is a JSON object with one member for each field"

        FSharpValue.MakeRecord(
            target,
            fields
            |> Array.map (fun field ->
                match Map.tryFind field.Name members with
                | Some value -> decode field.PropertyType value
                | None -> fail ("The record has no member " + field.Name))
        )
    elif FSharpType.IsUnion target && codec = 1 then
        let case, fields, values = positional target element

        FSharpValue.MakeUnion(
            case,
            Array.map2 (fun (field: PropertyInfo) item -> decode field.PropertyType item) fields values
        )
    elif FSharpType.IsUnion target then
        let name, value = tagged element

        match FSharpType.GetUnionCases target |> Array.tryFind (fun case -> case.Name = name) with
        | None -> fail ("Unknown constructor " + name)
        | Some case ->
            let fields = case.GetFields()

            let values =
                match fields.Length, value with
                | 0, None -> [||]
                | 1, Some value -> [| value |]
                | count, Some value when
                    count > 1
                    && value.ValueKind = JsonValueKind.Array
                    && value.GetArrayLength() = count
                    ->
                    Array.ofSeq (value.EnumerateArray())
                | _ -> fail ("The arguments of " + name + " do not match its arity")

            FSharpValue.MakeUnion(
                case,
                Array.map2 (fun (field: PropertyInfo) item -> decode field.PropertyType item) fields values
            )
    else
        fail ("The value encoding has no rule for " + target.FullName)

/// The JSON text of an encoded value. The JSON null of codec 1 has no JsonNode.
let private text (node: JsonNode) =
    if isNull (box node) then "null" else node.ToJsonString()

/// Encodes a value of the given type in value codec 1 or 2.
let rec encode (codec: int) (target: Type) (value: obj) : JsonNode =
    let encode = encode codec
    let node (value: JsonNode) = value
    let array (values: JsonNode seq) = node (JsonArray(Array.ofSeq values))

    let constructor (name: string) (values: JsonNode list) =
        match codec, values with
        | 1, _ -> array (node (JsonValue.Create(camelCase name)) :: values)
        | _, [] -> node (JsonValue.Create name)
        | _, [ single ] -> node (JsonObject(dict [ (name, single) ]))
        | _, many -> node (JsonObject(dict [ (name, array many) ]))

    if target = typeof<int> then
        node (JsonValue.Create(unbox<int> value))
    elif target = typeof<int64> then
        node (JsonValue.Create(unbox<int64> value))
    elif target = typeof<float> then
        node (JsonValue.Create(unbox<float> value))
    elif target = typeof<bool> then
        node (JsonValue.Create(unbox<bool> value))
    elif target = typeof<string> then
        node (JsonValue.Create(unbox<string> value))
    elif target = typeof<char> then
        node (JsonValue.Create(string (unbox<char> value)))
    elif target = typeof<decimal> then
        node (JsonValue.Create((unbox<decimal> value).ToString(CultureInfo.InvariantCulture)))
    elif target = typeof<unit> then
        if codec = 1 then fail "Value codec 1 has no rule for Unit"
        array []
    elif isGeneric typedefof<list<_>> target then
        let item = target.GetGenericArguments().[0]
        array [ for element in (value :?> Collections.IEnumerable) -> encode item element ]
    elif isGeneric typedefof<option<_>> target then
        let item = target.GetGenericArguments().[0]

        match codec, FSharpValue.GetUnionFields(value, target) with
        | 1, (case, [| inner |]) when case.Name = "Some" -> encode item inner
        | 1, _ -> Unchecked.defaultof<JsonNode>
        | _, (case, [| inner |]) when case.Name = "Some" -> constructor "Just" [ encode item inner ]
        | _ -> constructor "Nothing" []
    elif FSharpType.IsTuple target then
        array (Array.map2 encode (FSharpType.GetTupleElements target) (FSharpValue.GetTupleFields value))
    elif FSharpType.IsRecord target then
        let fields = FSharpType.GetRecordFields target
        let values = FSharpValue.GetRecordFields value

        node (
            JsonObject(
                Array.map2
                    (fun (field: PropertyInfo) item ->
                        Collections.Generic.KeyValuePair(field.Name, encode field.PropertyType item))
                    fields
                    values
            )
        )
    elif FSharpType.IsUnion target then
        let case, values = FSharpValue.GetUnionFields(value, target)

        constructor
            (caseName target case)
            (List.ofArray (
                Array.map2 (fun (field: PropertyInfo) item -> encode field.PropertyType item) (case.GetFields()) values
            ))
    else
        fail ("The value encoding has no rule for " + target.FullName)

/// Calls the value with the arguments decoded in the value codec, and compares the result with
/// the expected value.
let check (index: int) (codec: int) (value: 'T) (arguments: string) (expected: string) =
    let line (text: string) =
        printfn "call %d %s" index (text.ReplaceLineEndings " ")

    try
        use argumentDocument = JsonDocument.Parse arguments
        use expectedDocument = JsonDocument.Parse expected

        let rec apply (target: Type) (current: obj) (remaining: JsonElement list) =
            match remaining with
            | [] -> target, current
            | argument :: rest ->
                if not (FSharpType.IsFunction target) then
                    fail "The call has more arguments than the value has parameters"

                let input, output = FSharpType.GetFunctionElements target
                let invoke = target.GetMethod("Invoke")

                let result =
                    try
                        invoke.Invoke(current, [| decode codec input argument |])
                    with :? TargetInvocationException as error when not (isNull error.InnerException) ->
                        raise error.InnerException

                apply output result rest

        let resultType, actual =
            apply typeof<'T> (box value) (List.ofSeq (argumentDocument.RootElement.EnumerateArray()))

        if FSharpType.IsFunction resultType then
            fail "The call has fewer arguments than the value has parameters"

        let wanted = decode codec resultType expectedDocument.RootElement

        if LanguagePrimitives.GenericEquality actual wanted then
            line "ok"
        else
            line ("differs " + text (encode codec resultType actual))
    with error ->
        line ("error " + error.GetType().Name + ": " + error.Message)
