namespace Morphir.Json.Codecs

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Serialization
open Morphir.IR.Classic.Literal
open Morphir.IR.Versioning
open Morphir.Json

/// <summary>
/// LiteralCodec module provides JSON encoding and decoding for Literal values.
/// Literals are serialized as 2-element JSON arrays: [tag, value]
/// Tags vary by format version:
///   v1, v2: snake_case with _literal suffix (e.g., "bool_literal", "string_literal")
///   v3+: PascalCase (e.g., "BoolLiteral", "StringLiteral")
/// </summary>
module LiteralCodec =

    /// <summary>
    /// Tag names for v3+ format (PascalCase).
    /// </summary>
    module Tags =
        let boolLiteral = "BoolLiteral"
        let charLiteral = "CharLiteral"
        let stringLiteral = "StringLiteral"
        let wholeNumberLiteral = "WholeNumberLiteral"
        let floatLiteral = "FloatLiteral"
        let decimalLiteral = "DecimalLiteral"

    /// <summary>
    /// Gets the tag for a literal based on format version.
    /// </summary>
    let private getTag (version: FormatVersion) (baseName: string) : string =
        TagNaming.literalTag version baseName

    /// <summary>
    /// Writes a Literal directly to a Utf8JsonWriter using the specified format version.
    /// </summary>
    let writeTo (version: FormatVersion) (writer: Utf8JsonWriter) (literal: Literal) : unit =
        writer.WriteStartArray()
        match literal with
        | BoolLiteral value ->
            writer.WriteStringValue(getTag version Tags.boolLiteral)
            writer.WriteBooleanValue(value)
        | CharLiteral value ->
            writer.WriteStringValue(getTag version Tags.charLiteral)
            writer.WriteStringValue(string value)
        | StringLiteral value ->
            writer.WriteStringValue(getTag version Tags.stringLiteral)
            writer.WriteStringValue(value)
        | WholeNumberLiteral value ->
            writer.WriteStringValue(getTag version Tags.wholeNumberLiteral)
            writer.WriteNumberValue(value)
        | FloatLiteral value ->
            writer.WriteStringValue(getTag version Tags.floatLiteral)
            writer.WriteNumberValue(value)
        | DecimalLiteral value ->
            writer.WriteStringValue(getTag version Tags.decimalLiteral)
            // Decimal is written as a string to preserve precision
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture))
        writer.WriteEndArray()

    /// <summary>
    /// Encodes a Literal to a JsonElement using the specified format version.
    /// </summary>
    let encode (version: FormatVersion) (literal: Literal) : JsonElement =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writeTo version writer literal
        writer.Flush()
        stream.Position <- 0L
        JsonDocument.Parse(stream).RootElement.Clone()

    /// <summary>
    /// Normalizes a tag for comparison (handles both v1/v2 and v3+ formats).
    /// </summary>
    let private normalizeTag (tag: string) : string =
        // Remove _literal suffix if present, convert to lowercase for comparison
        let normalized =
            if tag.EndsWith("_literal", StringComparison.OrdinalIgnoreCase) then
                tag.Substring(0, tag.Length - 8)
            else if tag.EndsWith("Literal", StringComparison.Ordinal) then
                tag.Substring(0, tag.Length - 7)
            else
                tag
        normalized.ToLowerInvariant()

    /// <summary>
    /// Reads a Literal directly from a Utf8JsonReader.
    /// Accepts both v1/v2 and v3+ tag formats.
    /// </summary>
    let readFrom (reader: byref<Utf8JsonReader>) : Result<Literal, string> =
        if reader.TokenType <> JsonTokenType.StartArray then
            Error $"Expected StartArray for Literal, got {reader.TokenType}"
        else
            // Read tag
            if not (reader.Read()) || reader.TokenType <> JsonTokenType.String then
                Error "Expected string tag for Literal"
            else
                let tag = reader.GetString()
                let normalizedTag = normalizeTag tag

                // Read value
                if not (reader.Read()) then
                    Error "Expected value for Literal"
                else
                    let result =
                        match normalizedTag with
                        | "bool" ->
                            if reader.TokenType = JsonTokenType.True then
                                Ok (BoolLiteral true)
                            elif reader.TokenType = JsonTokenType.False then
                                Ok (BoolLiteral false)
                            else
                                Error $"Expected boolean value for BoolLiteral, got {reader.TokenType}"

                        | "char" ->
                            if reader.TokenType = JsonTokenType.String then
                                let str = reader.GetString()
                                if String.IsNullOrEmpty(str) then
                                    Error "CharLiteral value cannot be empty"
                                elif str.Length > 1 then
                                    Error $"CharLiteral value must be a single character, got '{str}'"
                                else
                                    Ok (CharLiteral str.[0])
                            else
                                Error $"Expected string value for CharLiteral, got {reader.TokenType}"

                        | "string" ->
                            if reader.TokenType = JsonTokenType.String then
                                Ok (StringLiteral (reader.GetString()))
                            else
                                Error $"Expected string value for StringLiteral, got {reader.TokenType}"

                        | "wholenumber" | "int" | "integer" ->
                            if reader.TokenType = JsonTokenType.Number then
                                try
                                    Ok (WholeNumberLiteral (reader.GetInt64()))
                                with _ ->
                                    Error "Failed to parse WholeNumberLiteral value"
                            else
                                Error $"Expected number value for WholeNumberLiteral, got {reader.TokenType}"

                        | "float" ->
                            if reader.TokenType = JsonTokenType.Number then
                                try
                                    Ok (FloatLiteral (reader.GetDouble()))
                                with _ ->
                                    Error "Failed to parse FloatLiteral value"
                            else
                                Error $"Expected number value for FloatLiteral, got {reader.TokenType}"

                        | "decimal" ->
                            if reader.TokenType = JsonTokenType.Number then
                                try
                                    Ok (DecimalLiteral (reader.GetDecimal()))
                                with _ ->
                                    Error "Failed to parse DecimalLiteral value"
                            elif reader.TokenType = JsonTokenType.String then
                                match Decimal.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture) with
                                | true, value -> Ok (DecimalLiteral value)
                                | false, _ -> Error "Failed to parse DecimalLiteral string value"
                            else
                                Error $"Expected number or string value for DecimalLiteral, got {reader.TokenType}"

                        | _ ->
                            Error $"Unknown Literal tag: {tag}"

                    // Read closing array bracket
                    match result with
                    | Error _ -> result
                    | Ok _ ->
                        if not (reader.Read()) || reader.TokenType <> JsonTokenType.EndArray then
                            Error "Expected EndArray for Literal"
                        else
                            result

    /// <summary>
    /// Decodes a JsonElement to a Literal.
    /// Accepts both v1/v2 and v3+ tag formats.
    /// </summary>
    let decode (element: JsonElement) : Result<Literal, string> =
        if element.ValueKind <> JsonValueKind.Array then
            Error $"Expected JSON array for Literal, got {element.ValueKind}"
        else
            let rawText = element.GetRawText()
            let mutable reader = Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(rawText))
            if reader.Read() then
                readFrom &reader
            else
                Error "Failed to read JSON element"


/// <summary>
/// JsonConverter for Literal type with configurable format version.
/// Serializes Literal as a 2-element JSON array with version-appropriate tags.
/// </summary>
type LiteralJsonConverter(options: MorphirJsonOptions) =
    inherit JsonConverter<Literal>()

    /// <summary>
    /// Creates a LiteralJsonConverter with default options (v3).
    /// </summary>
    new() = LiteralJsonConverter(MorphirJsonOptions.defaultOptions)

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : Literal =
        match LiteralCodec.readFrom &reader with
        | Ok literal -> literal
        | Error msg -> raise (JsonException(msg))

    override _.Write(writer: Utf8JsonWriter, value: Literal, _options: JsonSerializerOptions) : unit =
        LiteralCodec.writeTo options.FormatVersion writer value

    override _.CanConvert(typeToConvert: Type) : bool =
        typeToConvert = typeof<Literal>

