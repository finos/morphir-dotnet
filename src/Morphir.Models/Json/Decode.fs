namespace Morphir.Json

open System.Text.Json

/// <summary>
/// Decoder type: a function that transforms a JsonElement into a Result containing
/// either the decoded F# value or an error message.
/// Inspired by Thoth.Json's decoder pattern.
/// </summary>
type Decoder<'a> = JsonElement -> Result<'a, string>

/// <summary>
/// Decode module provides composable decoder functions for parsing JSON into F# values.
/// This follows the Thoth.Json/Elm decoder pattern for type-safe, composable JSON decoding.
/// All decoders return Result types for explicit error handling.
/// </summary>
module Decode =

    /// <summary>
    /// Decodes JSON null to unit.
    /// </summary>
    let unit (element: JsonElement) : Result<unit, string> =
        match element.ValueKind with
        | JsonValueKind.Null -> Ok ()
        | _ -> Error $"Expected null for unit, got {element.ValueKind}"

    /// <summary>
    /// Decodes a string value.
    /// </summary>
    let string (element: JsonElement) : Result<string, string> =
        match element.ValueKind with
        | JsonValueKind.String -> Ok (element.GetString())
        | _ -> Error $"Expected string, got {element.ValueKind}"

    /// <summary>
    /// Decodes an integer value.
    /// </summary>
    let int (element: JsonElement) : Result<int, string> =
        match element.ValueKind with
        | JsonValueKind.Number ->
            try
                Ok (element.GetInt32())
            with ex ->
                Error $"Failed to decode integer: {ex.Message}"
        | _ -> Error $"Expected number, got {element.ValueKind}"

    /// <summary>
    /// Decodes a boolean value.
    /// </summary>
    let bool (element: JsonElement) : Result<bool, string> =
        match element.ValueKind with
        | JsonValueKind.True -> Ok true
        | JsonValueKind.False -> Ok false
        | _ -> Error $"Expected boolean, got {element.ValueKind}"

    /// <summary>
    /// Decodes a JSON array using the provided decoder for each element.
    /// </summary>
    let list (decoder: Decoder<'a>) (element: JsonElement) : Result<'a list, string> =
        match element.ValueKind with
        | JsonValueKind.Array ->
            let mutable results = []
            let mutable error = None
            let enumerator = element.EnumerateArray()

            for item in enumerator do
                match error with
                | Some _ -> () // Skip if we already have an error
                | None ->
                    match decoder item with
                    | Ok value -> results <- value :: results
                    | Error msg -> error <- Some msg

            match error with
            | Some msg -> Error msg
            | None -> Ok (List.rev results)
        | _ -> Error $"Expected array, got {element.ValueKind}"

    /// <summary>
    /// Decodes a JSON array using the provided decoder for each element, returning an array.
    /// </summary>
    let array (decoder: Decoder<'a>) (element: JsonElement) : Result<'a array, string> =
        list decoder element
        |> Result.map List.toArray

    /// <summary>
    /// Decodes a field from a JSON object.
    /// </summary>
    let field (fieldName: string) (decoder: Decoder<'a>) (element: JsonElement) : Result<'a, string> =
        match element.ValueKind with
        | JsonValueKind.Object ->
            match element.TryGetProperty(fieldName) with
            | true, prop -> decoder prop
            | false, _ -> Error $"Field '{fieldName}' not found"
        | _ -> Error $"Expected object, got {element.ValueKind}"

    /// <summary>
    /// Decodes a field from a JSON object with a default value if the field is missing.
    /// </summary>
    let fieldOr (fieldName: string) (defaultValue: 'a) (decoder: Decoder<'a>) (element: JsonElement) : Result<'a, string> =
        match element.ValueKind with
        | JsonValueKind.Object ->
            match element.TryGetProperty(fieldName) with
            | true, prop -> decoder prop
            | false, _ -> Ok defaultValue
        | _ -> Error $"Expected object, got {element.ValueKind}"

    /// <summary>
    /// Decodes an element at a specific index in a JSON array.
    /// </summary>
    let index (idx: int) (decoder: Decoder<'a>) (element: JsonElement) : Result<'a, string> =
        match element.ValueKind with
        | JsonValueKind.Array ->
            let mutable currentIdx = 0
            let mutable found = None

            for item in element.EnumerateArray() do
                if currentIdx = idx then
                    found <- Some item
                currentIdx <- currentIdx + 1

            match found with
            | Some item -> decoder item
            | None -> Error $"Index {idx} out of bounds (array length: {currentIdx})"
        | _ -> Error $"Expected array, got {element.ValueKind}"

    /// <summary>
    /// Maps a decoder's result using the provided function.
    /// </summary>
    let map (f: 'a -> 'b) (decoder: Decoder<'a>) : Decoder<'b> =
        fun element ->
            decoder element
            |> Result.map f

    /// <summary>
    /// Combines two decoders using a mapping function.
    /// </summary>
    let map2 (f: 'a -> 'b -> 'c) (decoderA: Decoder<'a>) (decoderB: Decoder<'b>) : Decoder<'c> =
        fun element ->
            match decoderA element, decoderB element with
            | Ok a, Ok b -> Ok (f a b)
            | Error msg, _ -> Error msg
            | _, Error msg -> Error msg

    /// <summary>
    /// Decodes one of several possible values, trying each decoder in order.
    /// Returns the first successful decode or the last error.
    /// </summary>
    let oneOf (decoders: Decoder<'a> list) (element: JsonElement) : Result<'a, string> =
        let rec tryDecoders decoders lastError =
            match decoders with
            | [] -> Error (lastError |> Option.defaultValue "All decoders failed")
            | decoder :: rest ->
                match decoder element with
                | Ok value -> Ok value
                | Error msg -> tryDecoders rest (Some msg)

        tryDecoders decoders None

    /// <summary>
    /// Always succeeds with the given value, ignoring the JSON element.
    /// </summary>
    let succeed (value: 'a) : Decoder<'a> =
        fun _element -> Ok value

    /// <summary>
    /// Always fails with the given error message.
    /// </summary>
    let fail (message: string) : Decoder<'a> =
        fun _element -> Error message
