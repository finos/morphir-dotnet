namespace Morphir.Json.Codecs

open System
open System.Text.Json
open System.Text.Json.Serialization
open Morphir.IR

/// <summary>
/// FQNameCodec module provides JSON encoding and decoding for FQName values.
/// FQNames are serialized as a 3-element JSON array: [packagePath, modulePath, localName]
/// Format: [[["package"], ["name"]], [["module"], ["path"]], ["local", "name"]]
/// </summary>
module FQNameCodec =

    /// <summary>
    /// Writes an FQName directly to a Utf8JsonWriter.
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (fqName: FQName) : unit =
        writer.WriteStartArray()
        // Package path
        PackageNameCodec.writeTo writer fqName.PackagePath
        // Module path
        ModulePathCodec.writeTo writer fqName.ModulePath
        // Local name
        NameCodec.writeTo writer fqName.LocalName
        writer.WriteEndArray()

    /// <summary>
    /// Reads an FQName directly from a Utf8JsonReader.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<FQName, string> =
        if reader.TokenType <> JsonTokenType.StartArray then
            Error $"Expected StartArray for FQName, got {reader.TokenType}"
        else
            // Read package path
            if not (reader.Read()) then
                Error "Unexpected end of JSON when reading FQName package path"
            else
                match PackageNameCodec.readFrom &reader with
                | Error msg -> Error $"Failed to read FQName package path: {msg}"
                | Ok packagePath ->
                    // Read module path
                    if not (reader.Read()) then
                        Error "Unexpected end of JSON when reading FQName module path"
                    else
                        match ModulePathCodec.readFrom &reader with
                        | Error msg -> Error $"Failed to read FQName module path: {msg}"
                        | Ok modulePath ->
                            // Read local name
                            if not (reader.Read()) then
                                Error "Unexpected end of JSON when reading FQName local name"
                            else
                                match NameCodec.readFrom &reader with
                                | Error msg -> Error $"Failed to read FQName local name: {msg}"
                                | Ok localName ->
                                    // Read closing array bracket
                                    if not (reader.Read()) || reader.TokenType <> JsonTokenType.EndArray then
                                        Error "Expected EndArray for FQName"
                                    else
                                        Ok { PackagePath = packagePath; ModulePath = modulePath; LocalName = localName }

    /// <summary>
    /// Encodes an FQName to a JsonElement.
    /// </summary>
    let encode (fqName: FQName) : JsonElement =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writeTo writer fqName
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Decodes a JsonElement to an FQName.
    /// Expects a 3-element JSON array: [packagePath, modulePath, localName]
    /// </summary>
    let decode (element: JsonElement) : Result<FQName, string> =
        if element.ValueKind <> JsonValueKind.Array then
            Error $"Expected JSON array for FQName, got {element.ValueKind}"
        else
            let rawText = element.GetRawText()
            let mutable reader = Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(rawText))
            if reader.Read() then
                readFrom &reader
            else
                Error "Failed to read JSON element"


/// <summary>
/// JsonConverter for FQName type.
/// Serializes FQName as a 3-element JSON array.
/// </summary>
type FQNameJsonConverter() =
    inherit JsonConverter<FQName>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : FQName =
        match FQNameCodec.readFrom &reader with
        | Ok fqName -> fqName
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: FQName, _options: JsonSerializerOptions) : unit =
        FQNameCodec.writeTo writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<FQName>

