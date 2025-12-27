namespace Morphir.Json.Codecs

open System
open System.Text.Json
open System.Text.Json.Serialization
open Morphir.IR
open Morphir.Json

/// <summary>
/// PathCodec module provides JSON encoding and decoding for Path values.
/// Paths are serialized as JSON arrays of Name arrays.
/// Format: [["name", "1"], ["name", "2"]]
/// </summary>
module PathCodec =

    /// <summary>
    /// Writes a Path directly to a Utf8JsonWriter using the specified options.
    /// </summary>
    let writeToWithOptions (options: MorphirJsonOptions) (writer: Utf8JsonWriter) (path: Path) : unit =
        let names = Path.toList path
        writer.WriteStartArray()
        for name in names do
            NameCodec.writeToWithOptions options writer name
        writer.WriteEndArray()

    /// <summary>
    /// Writes a Path directly to a Utf8JsonWriter using default options (v3).
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (path: Path) : unit =
        writeToWithOptions MorphirJsonOptions.defaultOptions writer path

    /// <summary>
    /// Reads a Path directly from a Utf8JsonReader using the specified options.
    /// </summary>
    let readFromWithOptions (options: MorphirJsonOptions) (reader: byref<Utf8JsonReader>) : Result<Path, string> =
        if reader.TokenType <> JsonTokenType.StartArray then
            Error $"Expected StartArray for Path, got {reader.TokenType}"
        else
            let names = ResizeArray<Name>()
            let mutable error: string option = None
            while error.IsNone && reader.Read() && reader.TokenType <> JsonTokenType.EndArray do
                // Names can be either arrays (Classic) or strings (SemVer)
                if reader.TokenType = JsonTokenType.StartArray || reader.TokenType = JsonTokenType.String then
                    match NameCodec.readFromWithOptions options &reader with
                    | Ok name -> names.Add(name)
                    | Error msg -> error <- Some msg
                else
                    error <- Some $"Expected StartArray or String for Name in Path, got {reader.TokenType}"
            match error with
            | Some msg -> Error msg
            | None -> Ok (Path.fromList (names |> Seq.toList))

    /// <summary>
    /// Reads a Path directly from a Utf8JsonReader using default options.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<Path, string> =
        readFromWithOptions MorphirJsonOptions.defaultOptions &reader

    /// <summary>
    /// Encodes a Path to a JsonElement using the specified options.
    /// </summary>
    let encodeWithOptions (options: MorphirJsonOptions) (path: Path) : JsonElement =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writeToWithOptions options writer path
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Encodes a Path to a JsonElement using default options (v3).
    /// </summary>
    let encode (path: Path) : JsonElement =
        encodeWithOptions MorphirJsonOptions.defaultOptions path

    /// <summary>
    /// Decodes a JsonElement to a Path using the specified options.
    /// </summary>
    let decodeWithOptions (options: MorphirJsonOptions) (element: JsonElement) : Result<Path, string> =
        if element.ValueKind <> JsonValueKind.Array then
            Error $"Expected JSON array for Path, got {element.ValueKind}"
        else
            let rawText = element.GetRawText()
            let mutable reader = Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(rawText))
            if reader.Read() then
                readFromWithOptions options &reader
            else
                Error "Failed to read JSON element"

    /// <summary>
    /// Decodes a JsonElement to a Path using default options.
    /// </summary>
    let decode (element: JsonElement) : Result<Path, string> =
        decodeWithOptions MorphirJsonOptions.defaultOptions element


/// <summary>
/// JsonConverter for Path type with configurable format version.
/// </summary>
type PathJsonConverter(options: MorphirJsonOptions) =
    inherit JsonConverter<Path>()

    /// <summary>
    /// Creates a PathJsonConverter with default options (v3).
    /// </summary>
    new() = PathJsonConverter(MorphirJsonOptions.defaultOptions)

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : Path =
        match PathCodec.readFromWithOptions options &reader with
        | Ok path -> path
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: Path, _options: JsonSerializerOptions) : unit =
        PathCodec.writeToWithOptions options writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<Path>


/// <summary>
/// PackageNameCodec module provides JSON encoding and decoding for PackageName values.
/// PackageNames are serialized the same as Paths (array of Name arrays).
/// </summary>
module PackageNameCodec =

    /// <summary>
    /// Writes a PackageName directly to a Utf8JsonWriter using the specified options.
    /// </summary>
    let writeToWithOptions (options: MorphirJsonOptions) (writer: Utf8JsonWriter) (packageName: PackageName) : unit =
        packageName |> PackageName.packageNameToPath |> PathCodec.writeToWithOptions options writer

    /// <summary>
    /// Writes a PackageName directly to a Utf8JsonWriter using default options (v3).
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (packageName: PackageName) : unit =
        writeToWithOptions MorphirJsonOptions.defaultOptions writer packageName

    /// <summary>
    /// Reads a PackageName directly from a Utf8JsonReader using the specified options.
    /// </summary>
    let readFromWithOptions (options: MorphirJsonOptions) (reader: byref<Utf8JsonReader>) : Result<PackageName, string> =
        PathCodec.readFromWithOptions options &reader
        |> Result.map PackageName.packageName

    /// <summary>
    /// Reads a PackageName directly from a Utf8JsonReader using default options.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<PackageName, string> =
        readFromWithOptions MorphirJsonOptions.defaultOptions &reader

    /// <summary>
    /// Encodes a PackageName to a JsonElement using the specified options.
    /// </summary>
    let encodeWithOptions (options: MorphirJsonOptions) (packageName: PackageName) : JsonElement =
        packageName |> PackageName.packageNameToPath |> PathCodec.encodeWithOptions options

    /// <summary>
    /// Encodes a PackageName to a JsonElement using default options (v3).
    /// </summary>
    let encode (packageName: PackageName) : JsonElement =
        encodeWithOptions MorphirJsonOptions.defaultOptions packageName

    /// <summary>
    /// Decodes a JsonElement to a PackageName using the specified options.
    /// </summary>
    let decodeWithOptions (options: MorphirJsonOptions) (element: JsonElement) : Result<PackageName, string> =
        PathCodec.decodeWithOptions options element
        |> Result.map PackageName.packageName

    /// <summary>
    /// Decodes a JsonElement to a PackageName using default options.
    /// </summary>
    let decode (element: JsonElement) : Result<PackageName, string> =
        decodeWithOptions MorphirJsonOptions.defaultOptions element


/// <summary>
/// JsonConverter for PackageName type with configurable format version.
/// </summary>
type PackageNameJsonConverter(options: MorphirJsonOptions) =
    inherit JsonConverter<PackageName>()

    /// <summary>
    /// Creates a PackageNameJsonConverter with default options (v3).
    /// </summary>
    new() = PackageNameJsonConverter(MorphirJsonOptions.defaultOptions)

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : PackageName =
        match PackageNameCodec.readFromWithOptions options &reader with
        | Ok packageName -> packageName
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: PackageName, _options: JsonSerializerOptions) : unit =
        PackageNameCodec.writeToWithOptions options writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<PackageName>


/// <summary>
/// ModulePathCodec module provides JSON encoding and decoding for ModulePath values.
/// ModulePaths are serialized the same as Paths (array of Name arrays).
/// </summary>
module ModulePathCodec =

    /// <summary>
    /// Writes a ModulePath directly to a Utf8JsonWriter using the specified options.
    /// </summary>
    let writeToWithOptions (options: MorphirJsonOptions) (writer: Utf8JsonWriter) (modulePath: ModulePath) : unit =
        modulePath |> ModulePath.modulePathToPath |> PathCodec.writeToWithOptions options writer

    /// <summary>
    /// Writes a ModulePath directly to a Utf8JsonWriter using default options (v3).
    /// </summary>
    let writeTo (writer: Utf8JsonWriter) (modulePath: ModulePath) : unit =
        writeToWithOptions MorphirJsonOptions.defaultOptions writer modulePath

    /// <summary>
    /// Reads a ModulePath directly from a Utf8JsonReader using the specified options.
    /// </summary>
    let readFromWithOptions (options: MorphirJsonOptions) (reader: byref<Utf8JsonReader>) : Result<ModulePath, string> =
        PathCodec.readFromWithOptions options &reader
        |> Result.map ModulePath.modulePath

    /// <summary>
    /// Reads a ModulePath directly from a Utf8JsonReader using default options.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<ModulePath, string> =
        readFromWithOptions MorphirJsonOptions.defaultOptions &reader

    /// <summary>
    /// Encodes a ModulePath to a JsonElement using the specified options.
    /// </summary>
    let encodeWithOptions (options: MorphirJsonOptions) (modulePath: ModulePath) : JsonElement =
        modulePath |> ModulePath.modulePathToPath |> PathCodec.encodeWithOptions options

    /// <summary>
    /// Encodes a ModulePath to a JsonElement using default options (v3).
    /// </summary>
    let encode (modulePath: ModulePath) : JsonElement =
        encodeWithOptions MorphirJsonOptions.defaultOptions modulePath

    /// <summary>
    /// Decodes a JsonElement to a ModulePath using the specified options.
    /// </summary>
    let decodeWithOptions (options: MorphirJsonOptions) (element: JsonElement) : Result<ModulePath, string> =
        PathCodec.decodeWithOptions options element
        |> Result.map ModulePath.modulePath

    /// <summary>
    /// Decodes a JsonElement to a ModulePath using default options.
    /// </summary>
    let decode (element: JsonElement) : Result<ModulePath, string> =
        decodeWithOptions MorphirJsonOptions.defaultOptions element


/// <summary>
/// JsonConverter for ModulePath type with configurable format version.
/// </summary>
type ModulePathJsonConverter(options: MorphirJsonOptions) =
    inherit JsonConverter<ModulePath>()

    /// <summary>
    /// Creates a ModulePathJsonConverter with default options (v3).
    /// </summary>
    new() = ModulePathJsonConverter(MorphirJsonOptions.defaultOptions)

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : ModulePath =
        match ModulePathCodec.readFromWithOptions options &reader with
        | Ok modulePath -> modulePath
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: ModulePath, _options: JsonSerializerOptions) : unit =
        ModulePathCodec.writeToWithOptions options writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<ModulePath>

