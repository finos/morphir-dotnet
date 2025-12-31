namespace Morphir.Json

open System.Text.Json
open System.IO

/// <summary>
/// Encoder type: a function that transforms an F# value into a JsonElement.
/// Inspired by Thoth.Json's encoder pattern.
/// </summary>
type Encoder<'a> = 'a -> JsonElement

/// <summary>
/// Encode module provides composable encoder functions for building JSON from F# values.
/// This follows the Thoth.Json/Elm encoder pattern for type-safe, composable JSON encoding.
/// </summary>
module Encode =

    /// <summary>
    /// Encodes unit as JSON null.
    /// </summary>
    let unit (_value: unit) : JsonElement =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteNullValue()
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes a string value.
    /// </summary>
    let string (value: string) : JsonElement =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStringValue(value)
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes an integer value.
    /// </summary>
    let int (value: int) : JsonElement =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteNumberValue(value)
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes a boolean value.
    /// </summary>
    let bool (value: bool) : JsonElement =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteBooleanValue(value)
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes a list of values using the provided encoder.
    /// </summary>
    let list (encoder: Encoder<'a>) (values: 'a list) : JsonElement =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartArray()
        for value in values do
            let element = encoder value
            element.WriteTo(writer)
        writer.WriteEndArray()
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes an array of values using the provided encoder.
    /// </summary>
    let array (encoder: Encoder<'a>) (values: 'a array) : JsonElement =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartArray()
        for value in values do
            let element = encoder value
            element.WriteTo(writer)
        writer.WriteEndArray()
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes a JSON object from a list of key-value pairs.
    /// </summary>
    let object (properties: (string * JsonElement) list) : JsonElement =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject()
        for (key, value) in properties do
            writer.WritePropertyName(key)
            value.WriteTo(writer)
        writer.WriteEndObject()
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes a Map as a JSON object.
    /// </summary>
    let map (keyEncoder: Encoder<'k>) (valueEncoder: Encoder<'v>) (map: Map<'k, 'v>) : JsonElement =
        let properties =
            map
            |> Map.toList
            |> List.map (fun (k, v) ->
                let keyElement = keyEncoder k
                let keyStr =
                    match keyElement.ValueKind with
                    | JsonValueKind.String -> keyElement.GetString()
                    | _ -> keyElement.GetRawText()
                (keyStr, valueEncoder v))
        object properties

    /// <summary>
    /// Helper to encode a JSON array from JsonElement values.
    /// Used when you already have JsonElements and need to combine them into an array.
    /// </summary>
    let arrayOfElements (elements: JsonElement list) : JsonElement =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartArray()
        for element in elements do
            element.WriteTo(writer)
        writer.WriteEndArray()
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()
