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
        with _ ->
            // Any parser failure is a refusal, never a crash: Utf8JsonReader stops at depth 64 before recursing.
            refuse

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

    /// The deepest nesting a document may have: the JSON reader's limit, and the TOML bound below.
    let maxDepth = 64

    /// <summary>
    /// Whether TOML bytes nest arrays and inline tables deeper than <paramref name="limit"/>. CsToml parses nested
    /// values recursively: 10,000 levels overflow the stack, which ends the process and cannot be caught, and such a
    /// document is only about 20 KB. So the brackets and braces are counted first, outside strings and comments, with
    /// TOML's own lexical rules: basic strings with escapes, literal strings, both multi-line forms (whose closing
    /// delimiter may be followed by up to two more quotes) and comments to the end of the line.
    /// </summary>
    let internal tomlNestsDeeperThan (limit: int) (bytes: byte array) =
        let n = bytes.Length
        let at i = if i < n then bytes[i] else 0uy
        let mutable i = 0
        let mutable depth = 0
        let mutable deeper = false

        let triple (quote: byte) j = at j = quote && at (j + 1) = quote && at (j + 2) = quote

        while not deeper && i < n do
            match bytes[i] with
            | 0x23uy -> // '#': a comment runs to the end of the line
                while i < n && bytes[i] <> 0x0Auy do
                    i <- i + 1
            | (0x22uy | 0x27uy) as quote when triple quote i -> // a multi-line string
                let escapes = quote = 0x22uy
                i <- i + 3

                while i < n && not (triple quote i) do
                    i <- i + (if escapes && bytes[i] = 0x5Cuy then 2 else 1)

                i <- i + 3

                // """"" closes with up to two quotes of content before the delimiter.
                let mutable extra = 0

                while extra < 2 && at i = quote do
                    i <- i + 1
                    extra <- extra + 1

                i <- i - 1
            | (0x22uy | 0x27uy) as quote -> // a one-line string ends at its quote or the end of the line
                let escapes = quote = 0x22uy
                i <- i + 1

                while i < n && bytes[i] <> quote && bytes[i] <> 0x0Auy do
                    i <- i + (if escapes && bytes[i] = 0x5Cuy then 2 else 1)
            | 0x5Buy
            | 0x7Buy -> // '[' or '{'
                depth <- depth + 1
                deeper <- depth > limit
            | 0x5Duy
            | 0x7Duy -> depth <- max 0 (depth - 1)
            | _ -> ()

            i <- i + 1

        deeper

    /// <summary>
    /// Parses TOML 1.0.0 bytes. Bytes that are not strict UTF-8 or not TOML 1.0.0, including repeated keys, are
    /// <c>invalid_document</c> at the empty path, and so is nesting deeper than <see cref="maxDepth"/>, which is refused
    /// before CsToml sees the bytes. Any exception the parser raises is a refusal, never a crash. A TOML document's
    /// root is always a table.
    /// </summary>
    let parseToml (bytes: byte array) : Result<Value, ReaderRefusal> =
        try
            strictUtf8.GetString bytes |> ignore

            if tomlNestsDeeperThan maxDepth bytes then
                refuse
            else
                let document = CsToml.CsTomlSerializer.Deserialize<CsToml.TomlDocument>(ReadOnlySpan bytes, null)
                Ok(readToml document.RootNode 0)
        with _ ->
            refuse
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
        let normalized = Nfc.normalize text
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
