// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Buffers
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Encodings.Web
open System.Text.Json

/// <summary>
/// A document value as its syntax parses it, before any member is interpreted. JSON and TOML both parse to this tree,
/// so every reader rule, the credential scan and the canonical encoding run on one representation. Object members keep
/// document order; numbers keep their exact spelling so that <c>1.0</c> and <c>1e3</c> can be refused.
/// </summary>
[<RequireQualifiedAccess>]
type Value =
    | Object of members: (string * Value) list
    | Array of items: Value list
    | String of text: string
    | Number of spelling: string
    | Boolean of bool
    | Null
    /// A TOML value with no JSON counterpart, such as a date-time or a float, named by its TOML type.
    | Other of tomlType: string

/// Parsing JSON and TOML bytes into <see cref="T:Morphir.Toolchain.Value"/>.
module Syntax =

    let private refuse = Error { Code = ReaderCode.InvalidDocument; Path = DocumentPath.root }

    let rec private readJson (reader: byref<Utf8JsonReader>) : Value =
        match reader.TokenType with
        | JsonTokenType.StartObject ->
            let members = ResizeArray()
            let names = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            reader.Read() |> ignore

            while reader.TokenType <> JsonTokenType.EndObject do
                let name = nonNull (reader.GetString())

                if not (names.Add name) then
                    raise (JsonException $"duplicate member {name}")

                reader.Read() |> ignore
                members.Add(name, readJson &reader)
                reader.Read() |> ignore

            Value.Object(List.ofSeq members)
        | JsonTokenType.StartArray ->
            let items = ResizeArray()
            reader.Read() |> ignore

            while reader.TokenType <> JsonTokenType.EndArray do
                items.Add(readJson &reader)
                reader.Read() |> ignore

            Value.Array(List.ofSeq items)
        | JsonTokenType.String -> Value.String(nonNull (reader.GetString()))
        | JsonTokenType.Number -> Value.Number(Encoding.UTF8.GetString(reader.ValueSpan))
        | JsonTokenType.True -> Value.Boolean true
        | JsonTokenType.False -> Value.Boolean false
        | JsonTokenType.Null -> Value.Null
        | other -> raise (JsonException $"unexpected token {other}")

    /// <summary>
    /// Parses JSON bytes. Bytes that are not strict UTF-8 JSON, a byte order mark, comments, trailing commas, a
    /// repeated member name in one object, trailing content or a root that is not an object are all
    /// <c>invalid_document</c> at the empty path.
    /// </summary>
    let parseJson (bytes: byte array) : Result<Value, ReaderRefusal> =
        try
            strictUtf8.GetString bytes |> ignore
            let options = JsonReaderOptions(CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 64)
            let mutable reader = Utf8JsonReader(ReadOnlySpan bytes, options)

            if not (reader.Read()) then
                refuse
            else
                let value = readJson &reader

                if reader.Read() then
                    refuse
                else
                    match value with
                    | Value.Object _ -> Ok value
                    | _ -> refuse
        with
        | :? JsonException
        | :? DecoderFallbackException
        | :? InvalidOperationException -> refuse

    let rec private readToml (node: CsToml.TomlDocumentNode) (depth: int) : Value =
        if depth > 64 then
            raise (InvalidDataException "TOML nesting is deeper than 64 levels")

        if
            node.HasNodeOnly
            || node.IsTableHeader
            || node.ValueType = CsToml.Values.TomlValueType.Empty
            || node.ValueType = CsToml.Values.TomlValueType.Table
            || node.ValueType = CsToml.Values.TomlValueType.InlineTable
        then
            let members = ResizeArray()
            let mutable children = node.GetEnumerator()

            while children.MoveNext() do
                let child = children.Current
                members.Add(child.Key.GetString(), readToml child.Value (depth + 1))

            Value.Object(List.ofSeq members)
        else
            match node.ValueType with
            | CsToml.Values.TomlValueType.String -> Value.String(node.GetString())
            | CsToml.Values.TomlValueType.Integer ->
                Value.Number(node.GetInt64().ToString(Globalization.CultureInfo.InvariantCulture))
            | CsToml.Values.TomlValueType.Boolean -> Value.Boolean(node.GetBool())
            | CsToml.Values.TomlValueType.Array ->
                let count = node.GetArray().Count
                Value.Array [ for index in 0 .. count - 1 -> readToml node[index] (depth + 1) ]
            | other -> Value.Other(other.ToString())

    /// <summary>
    /// Parses TOML 1.0.0 bytes. Bytes that are not strict UTF-8 or not TOML 1.0.0, including repeated keys, are
    /// <c>invalid_document</c> at the empty path. A TOML document's root is always a table.
    /// </summary>
    let parseToml (bytes: byte array) : Result<Value, ReaderRefusal> =
        try
            strictUtf8.GetString bytes |> ignore
            let document = CsToml.CsTomlSerializer.Deserialize<CsToml.TomlDocument>(ReadOnlySpan bytes, null)
            Ok(readToml document.RootNode 0)
        with
        | :? DecoderFallbackException
        | :? CsToml.Error.CsTomlException
        | :? InvalidDataException
        | :? InvalidOperationException
        | :? FormatException -> refuse

/// Writing values as indented JSON for standard output and files.
module JsonText =

    let rec private write (writer: Utf8JsonWriter) (value: Value) =
        match value with
        | Value.Object members ->
            writer.WriteStartObject()

            for name, item in members do
                writer.WritePropertyName name
                write writer item

            writer.WriteEndObject()
        | Value.Array items ->
            writer.WriteStartArray()

            for item in items do
                write writer item

            writer.WriteEndArray()
        | Value.String text -> writer.WriteStringValue text
        | Value.Number spelling -> writer.WriteRawValue(spelling, false)
        | Value.Boolean flag -> writer.WriteBooleanValue flag
        | Value.Null
        | Value.Other _ -> writer.WriteNullValue()

    /// Indented UTF-8 JSON with a final line feed. Non-ASCII text is written as itself: the output is a document for
    /// tools and terminals, never embedded in HTML.
    let toBytes (value: Value) =
        let buffer = ArrayBufferWriter<byte>()

        let options =
            JsonWriterOptions(Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n")

        use writer = new Utf8JsonWriter(buffer, options)
        write writer value
        writer.Flush()
        Array.append (buffer.WrittenSpan.ToArray()) [| byte '\n' |]

/// <summary>
/// The canonical JSON of the manager-result page, which <c>planSha256</c> hashes: no whitespace, strings in NFC with
/// only <c>"</c> and <c>\</c> escaped, members sorted by the UTF-8 bytes of their encoded keys including the quotes,
/// and integers in plain decimal. The bytes are not always valid JSON; they are only hashed.
/// </summary>
module Canonical =

    let private encodeString (text: string) =
        let normalized = text.Normalize(NormalizationForm.FormC)
        let escaped = normalized.Replace("\\", "\\\\").Replace("\"", "\\\"")
        Encoding.UTF8.GetBytes("\"" + escaped + "\"")

    let private isPlainInteger (spelling: string) =
        spelling.Length > 0
        && Seq.forall isAsciiDigit spelling
        && (spelling = "0" || spelling[0] <> '0')

    let rec private encode (output: MemoryStream) (path: DocumentPath) (value: Value) : Result<unit, ReaderRefusal> =
        let put (bytes: byte array) = output.Write(bytes, 0, bytes.Length)

        match value with
        | Value.Object members ->
            let keyed =
                members
                |> List.map (fun (name, item) -> name, encodeString name, item)
                |> List.sortWith (fun (_, a, _) (_, b, _) -> (ReadOnlySpan a).SequenceCompareTo(ReadOnlySpan b))

            let keys = keyed |> List.map (fun (_, key, _) -> Convert.ToHexString key)

            if List.length (List.distinct keys) <> List.length keys then
                // Two member names with the same NFC form would encode the same key twice.
                Error { Code = ReaderCode.InvalidDocument; Path = path }
            else
                put [| byte '{' |]

                result {
                    for index, (name, key, item) in List.indexed keyed do
                        if index > 0 then
                            put [| byte ',' |]

                        put key
                        put [| byte ':' |]
                        do! encode output (path.Child name) item

                    put [| byte '}' |]
                }
        | Value.Array items ->
            put [| byte '[' |]

            result {
                for index, item in List.indexed items do
                    if index > 0 then
                        put [| byte ',' |]

                    do! encode output (path.At index) item

                put [| byte ']' |]
            }
        | Value.String text ->
            put (encodeString text)
            Ok()
        | Value.Number spelling when isPlainInteger spelling ->
            put (Encoding.ASCII.GetBytes spelling)
            Ok()
        | Value.Number _
        | Value.Other _ -> Error { Code = ReaderCode.InvalidDocument; Path = path }
        | Value.Boolean true ->
            put "true"B
            Ok()
        | Value.Boolean false ->
            put "false"B
            Ok()
        | Value.Null ->
            put "null"B
            Ok()

    /// The canonical bytes of a value found at <paramref name="path"/>. A number that is not a plain non-negative
    /// integer cannot be encoded and is <c>invalid_document</c> at its member.
    let bytesAt (path: DocumentPath) (value: Value) : Result<byte array, ReaderRefusal> =
        use output = new MemoryStream()
        encode output path value |> Result.map (fun () -> output.ToArray())

    /// The canonical bytes of a value.
    let bytes (value: Value) = bytesAt DocumentPath.root value

/// SHA-256 of exact bytes as 64 lowercase hexadecimal digits.
module Digest =
    let ofBytes (bytes: byte array) = SHA256.HashData bytes |> Convert.ToHexStringLower
