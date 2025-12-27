namespace Morphir.Json.Codecs

open System
open System.Text.Json
open System.Text.Json.Serialization
open Morphir.IR

/// <summary>
/// PathCodec module provides JSON encoding and decoding for Path values.
/// Paths are serialized as JSON arrays of Name arrays.
/// Format: [["name", "1"], ["name", "2"]]
/// </summary>
module PathCodec =

    /// <summary>
    /// Writes a Path directly to a Utf8JsonWriter.
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (path: Path) : unit =
        let names = Path.toList path
        writer.WriteStartArray()
        for name in names do
            NameCodec.writeTo writer name
        writer.WriteEndArray()

    /// <summary>
    /// Reads a Path directly from a Utf8JsonReader.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<Path, string> =
        if reader.TokenType <> JsonTokenType.StartArray then
            Error $"Expected StartArray for Path, got {reader.TokenType}"
        else
            let names = ResizeArray<Name>()
            let mutable error: string option = None
            while error.IsNone && reader.Read() && reader.TokenType <> JsonTokenType.EndArray do
                if reader.TokenType = JsonTokenType.StartArray then
                    match NameCodec.readFrom &reader with
                    | Ok name -> names.Add(name)
                    | Error msg -> error <- Some msg
                else
                    error <- Some $"Expected StartArray for Name in Path, got {reader.TokenType}"
            match error with
            | Some msg -> Error msg
            | None -> Ok (Path.fromList (names |> Seq.toList))

    /// <summary>
    /// Encodes a Path to a JsonElement (array of Name arrays).
    /// </summary>
    let encode (path: Path) : JsonElement =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writeTo writer path
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Decodes a JsonElement to a Path.
    /// Expects a JSON array of Name arrays.
    /// </summary>
    let decode (element: JsonElement) : Result<Path, string> =
        if element.ValueKind <> JsonValueKind.Array then
            Error $"Expected JSON array for Path, got {element.ValueKind}"
        else
            let rawText = element.GetRawText()
            let mutable reader = Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(rawText))
            if reader.Read() then
                readFrom &reader
            else
                Error "Failed to read JSON element"


/// <summary>
/// JsonConverter for Path type.
/// Serializes Path as a JSON array of Name arrays.
/// </summary>
type PathJsonConverter() =
    inherit JsonConverter<Path>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : Path =
        match PathCodec.readFrom &reader with
        | Ok path -> path
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: Path, _options: JsonSerializerOptions) : unit =
        PathCodec.writeTo writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<Path>


/// <summary>
/// PackageNameCodec module provides JSON encoding and decoding for PackageName values.
/// PackageNames are serialized the same as Paths (array of Name arrays).
/// </summary>
module PackageNameCodec =

    /// <summary>
    /// Encodes a PackageName to a JsonElement.
    /// </summary>
    let encode (packageName: PackageName) : JsonElement =
        packageName |> PackageName.packageNameToPath |> PathCodec.encode

    /// <summary>
    /// Decodes a JsonElement to a PackageName.
    /// </summary>
    let decode (element: JsonElement) : Result<PackageName, string> =
        PathCodec.decode element
        |> Result.map PackageName.packageName

    /// <summary>
    /// Writes a PackageName directly to a Utf8JsonWriter.
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (packageName: PackageName) : unit =
        packageName |> PackageName.packageNameToPath |> PathCodec.writeTo writer

    /// <summary>
    /// Reads a PackageName directly from a Utf8JsonReader.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<PackageName, string> =
        PathCodec.readFrom &reader
        |> Result.map PackageName.packageName


/// <summary>
/// JsonConverter for PackageName type.
/// Serializes PackageName as a JSON array of Name arrays.
/// </summary>
type PackageNameJsonConverter() =
    inherit JsonConverter<PackageName>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : PackageName =
        match PackageNameCodec.readFrom &reader with
        | Ok packageName -> packageName
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: PackageName, _options: JsonSerializerOptions) : unit =
        PackageNameCodec.writeTo writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<PackageName>


/// <summary>
/// ModulePathCodec module provides JSON encoding and decoding for ModulePath values.
/// ModulePaths are serialized the same as Paths (array of Name arrays).
/// </summary>
module ModulePathCodec =

    /// <summary>
    /// Encodes a ModulePath to a JsonElement.
    /// </summary>
    let encode (modulePath: ModulePath) : JsonElement =
        modulePath |> ModulePath.modulePathToPath |> PathCodec.encode

    /// <summary>
    /// Decodes a JsonElement to a ModulePath.
    /// </summary>
    let decode (element: JsonElement) : Result<ModulePath, string> =
        PathCodec.decode element
        |> Result.map ModulePath.modulePath

    /// <summary>
    /// Writes a ModulePath directly to a Utf8JsonWriter.
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (modulePath: ModulePath) : unit =
        modulePath |> ModulePath.modulePathToPath |> PathCodec.writeTo writer

    /// <summary>
    /// Reads a ModulePath directly from a Utf8JsonReader.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<ModulePath, string> =
        PathCodec.readFrom &reader
        |> Result.map ModulePath.modulePath


/// <summary>
/// JsonConverter for ModulePath type.
/// Serializes ModulePath as a JSON array of Name arrays.
/// </summary>
type ModulePathJsonConverter() =
    inherit JsonConverter<ModulePath>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : ModulePath =
        match ModulePathCodec.readFrom &reader with
        | Ok modulePath -> modulePath
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: ModulePath, _options: JsonSerializerOptions) : unit =
        ModulePathCodec.writeTo writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<ModulePath>

