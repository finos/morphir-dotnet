namespace Morphir.Json.Codecs

open System
open System.Text.Json
open System.Text.Json.Serialization
open Morphir.IR

/// <summary>
/// NameCodec module provides JSON encoding and decoding for Name values.
/// Names are serialized as JSON arrays of lowercase strings.
/// Format: ["first", "name", "segments"]
/// </summary>
module NameCodec =

    /// <summary>
    /// Writes a Name directly to a Utf8JsonWriter.
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (name: Name) : unit =
        let segments = Name.toList name
        writer.WriteStartArray()
        for segment in segments do
            writer.WriteStringValue(segment)
        writer.WriteEndArray()

    /// <summary>
    /// Reads a Name directly from a Utf8JsonReader.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<Name, string> =
        if reader.TokenType <> JsonTokenType.StartArray then
            Error $"Expected StartArray for Name, got {reader.TokenType}"
        else
            let segments = ResizeArray<string>()
            let mutable error: string option = None
            while error.IsNone && reader.Read() && reader.TokenType <> JsonTokenType.EndArray do
                if reader.TokenType = JsonTokenType.String then
                    let segment = reader.GetString()
                    if not (isNull segment) then
                        segments.Add(segment)
                else
                    error <- Some $"Expected string in Name array, got {reader.TokenType}"
            match error with
            | Some msg -> Error msg
            | None -> Ok (Name.fromList (segments |> Seq.toList))

    /// <summary>
    /// Encodes a Name to a JsonElement (array of strings).
    /// </summary>
    let encode (name: Name) : JsonElement =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writeTo writer name
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Decodes a JsonElement to a Name.
    /// Expects a JSON array of strings.
    /// </summary>
    let decode (element: JsonElement) : Result<Name, string> =
        if element.ValueKind <> JsonValueKind.Array then
            Error $"Expected JSON array for Name, got {element.ValueKind}"
        else
            // Get a reader from the element's raw text and use readFrom
            let rawText = element.GetRawText()
            let mutable reader = Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(rawText))
            if reader.Read() then
                readFrom &reader
            else
                Error "Failed to read JSON element"


/// <summary>
/// JsonConverter for Name type.
/// Serializes Name as a JSON array of lowercase string segments.
/// </summary>
type NameJsonConverter() =
    inherit JsonConverter<Name>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : Name =
        match NameCodec.readFrom &reader with
        | Ok name -> name
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: Name, _options: JsonSerializerOptions) : unit =
        NameCodec.writeTo writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<Name>

