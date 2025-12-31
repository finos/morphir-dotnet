namespace Morphir.Json.Codecs

open System
open System.Text.Json
open System.Text.Json.Serialization
open Morphir.IR
open Morphir.IR.Versioning
open Morphir.Json

/// <summary>
/// NameCodec module provides JSON encoding and decoding for Name values.
/// Classic versions (v1-v3): Serialized as JSON arrays of lowercase strings. Format: ["first", "name", "segments"]
/// SemVer versions: Serialized as kebab-case strings. Format: "first-name-segments"
/// </summary>
module NameCodec =

    /// <summary>
    /// Converts a Name to kebab-case string format.
    /// </summary>
    let private toKebabCase (name: Name) : string =
        name |> Name.toList |> String.concat "-"

    /// <summary>
    /// Converts a kebab-case string to a Name.
    /// </summary>
    let private fromKebabCase (kebabCase: string) : Name =
        kebabCase.Split('-') |> Array.toList |> Name.fromList

    /// <summary>
    /// Writes a Name directly to a Utf8JsonWriter using the specified options.
    /// Classic versions: Array format ["foo", "bar"]
    /// SemVer versions: Kebab-case string "foo-bar"
    /// </summary>
    let writeToWithOptions (options: MorphirJsonOptions) (writer: Utf8JsonWriter) (name: Name) : unit =
        match options.FormatVersion with
        | Classic _ ->
            // Classic format: array of strings
            let segments = Name.toList name
            writer.WriteStartArray()
            for segment in segments do
                writer.WriteStringValue(segment)
            writer.WriteEndArray()
        | SemVer _ ->
            // SemVer format: kebab-case string
            writer.WriteStringValue(toKebabCase name)

    /// <summary>
    /// Writes a Name directly to a Utf8JsonWriter using default options (v3).
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (name: Name) : unit =
        writeToWithOptions MorphirJsonOptions.defaultOptions writer name

    /// <summary>
    /// Reads a Name directly from a Utf8JsonReader.
    /// Accepts both array format (Classic) and kebab-case string format (SemVer).
    /// Auto-detects format based on JSON token type.
    /// </summary>
    let readFromWithOptions (options: MorphirJsonOptions) (reader: byref<Utf8JsonReader>) : Result<Name, string> =
        match reader.TokenType with
        | JsonTokenType.StartArray ->
            // Classic format: array of strings
            let segments = ResizeArray<string>()
            let mutable error: string option = None
            while error.IsNone && reader.Read() && reader.TokenType <> JsonTokenType.EndArray do
                if reader.TokenType = JsonTokenType.String then
                    let segment = reader.GetString()
                    if not (isNull segment) then
                        segments.Add(segment.ToLowerInvariant())  // Normalize to lowercase to match Name invariant
                else
                    error <- Some $"Expected string in Name array, got {reader.TokenType}"
            match error with
            | Some msg -> Error msg
            | None -> Ok (Name.fromList (segments |> Seq.toList))

        | JsonTokenType.String ->
            // SemVer format: kebab-case string
            let kebabCase = reader.GetString()
            if isNull kebabCase then
                Error "Name string value is null"
            else
                Ok (fromKebabCase kebabCase)

        | _ ->
            Error $"Expected StartArray or String for Name, got {reader.TokenType}"

    /// <summary>
    /// Reads a Name directly from a Utf8JsonReader using default options.
    /// Auto-detects format based on JSON token type.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<Name, string> =
        readFromWithOptions MorphirJsonOptions.defaultOptions &reader

    /// <summary>
    /// Encodes a Name to a JsonElement using the specified options.
    /// Classic versions: Array format
    /// SemVer versions: Kebab-case string
    /// </summary>
    let encodeWithOptions (options: MorphirJsonOptions) (name: Name) : JsonElement =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writeToWithOptions options writer name
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes a Name to a JsonElement using default options (v3).
    /// </summary>
    let encode (name: Name) : JsonElement =
        encodeWithOptions MorphirJsonOptions.defaultOptions name

    /// <summary>
    /// Decodes a JsonElement to a Name using the specified options.
    /// Accepts both array format (Classic) and kebab-case string format (SemVer).
    /// </summary>
    let decodeWithOptions (options: MorphirJsonOptions) (element: JsonElement) : Result<Name, string> =
        match element.ValueKind with
        | JsonValueKind.Array ->
            // Classic format: array of strings
            let rawText = element.GetRawText()
            let mutable reader = Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(rawText))
            if reader.Read() then
                readFromWithOptions options &reader
            else
                Error "Failed to read JSON element"

        | JsonValueKind.String ->
            // SemVer format: kebab-case string
            let kebabCase = element.GetString()
            if isNull kebabCase then
                Error "Name string value is null"
            else
                Ok (fromKebabCase kebabCase)

        | _ ->
            Error $"Expected JSON array or string for Name, got {element.ValueKind}"

    /// <summary>
    /// Decodes a JsonElement to a Name using default options.
    /// Accepts both array format (Classic) and kebab-case string format (SemVer).
    /// </summary>
    let decode (element: JsonElement) : Result<Name, string> =
        decodeWithOptions MorphirJsonOptions.defaultOptions element


/// <summary>
/// JsonConverter for Name type with configurable format version.
/// Classic versions: Serializes as JSON array of string segments
/// SemVer versions: Serializes as kebab-case string
/// </summary>
type NameJsonConverter(options: MorphirJsonOptions) =
    inherit JsonConverter<Name>()

    /// <summary>
    /// Creates a NameJsonConverter with default options (v3).
    /// </summary>
    new() = NameJsonConverter(MorphirJsonOptions.defaultOptions)

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : Name =
        match NameCodec.readFromWithOptions options &reader with
        | Ok name -> name
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: Name, _options: JsonSerializerOptions) : unit =
        NameCodec.writeToWithOptions options writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<Name>

