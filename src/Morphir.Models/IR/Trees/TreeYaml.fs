namespace Morphir.IR.Trees

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open YamlDotNet.Core
open YamlDotNet.Core.Events

exception private TreeYamlFailure of code: string * location: string * position: string * message: string

[<RequireQualifiedAccess>]
type private YamlAtom =
    | String of string
    | Number of string
    | Boolean of bool
    | Null

type private YamlFrame =
    | MappingFrame of path: string list * depth: int * keys: HashSet<string>
    | SequenceFrame of path: string list * depth: int * nextIndex: int ref

[<RequireQualifiedAccess>]
module internal TreeYaml =
    let private integer = Regex(@"\A[+-]?[0-9]+\z", RegexOptions.CultureInvariant)

    let private decimal =
        Regex(
            @"\A[+-]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][+-]?[0-9]+)?\z",
            RegexOptions.CultureInvariant
        )

    let private leadingZero = Regex(@"\A[+-]?0[0-9]", RegexOptions.CultureInvariant)

    let private basedInteger =
        Regex(@"\A[+-]?0([oO][0-7]+|[xX][0-9a-fA-F]+)\z", RegexOptions.CultureInvariant)

    let private pointer path =
        match path with
        | [] -> "/"
        | _ ->
            path
            |> List.rev
            |> List.map (fun (part: string) ->
                "/"
                + part.Replace("~", "~0").Replace("/", "~1")
            )
            |> String.concat ""

    let private fail path (event: ParsingEvent | null) code message =
        let position =
            match event with
            | null -> ""
            | found -> $" (line {found.Start.Line}, column {found.Start.Column})"

        raise (TreeYamlFailure(code, pointer path, position, message))

    let private numericLexeme (value: string) =
        let text =
            if value.StartsWith("+", StringComparison.Ordinal) then
                value.Substring(1)
            else
                value

        let exponent = text.IndexOfAny([| 'e'; 'E' |])

        let whole, tail =
            if exponent < 0 then
                text, ""
            else
                text.Substring(0, exponent), text.Substring(exponent)

        let whole =
            if whole.StartsWith(".", StringComparison.Ordinal) then
                "0"
                + whole
            elif whole.StartsWith("-.", StringComparison.Ordinal) then
                "-0"
                + whole.Substring(1)
            else
                whole

        let whole =
            if whole.EndsWith(".", StringComparison.Ordinal) then
                whole
                + "0"
            else
                whole

        whole
        + tail

    let private scalar path (value: Scalar) =
        let text = value.Value

        if
            value.Style
            <> ScalarStyle.Plain
        then
            YamlAtom.String text
        else
            match text with
            | "true"
            | "True"
            | "TRUE" -> YamlAtom.Boolean true
            | "false"
            | "False"
            | "FALSE" -> YamlAtom.Boolean false
            | "null"
            | "Null"
            | "NULL"
            | "~"
            | "" -> YamlAtom.Null
            | _ when
                [ ".inf"; "+.inf"; "-.inf"; ".nan" ]
                |> List.exists (fun token ->
                    String.Equals(text, token, StringComparison.OrdinalIgnoreCase)
                )
                ->
                fail path value "invalid_literal" "Non-finite numbers are not part of the profile."
            | _ when basedInteger.IsMatch text ->
                fail
                    path
                    value
                    "invalid_literal"
                    "Write decimal. Octal and hexadecimal numerals are not part of the profile."
            | _ when
                integer.IsMatch text
                || decimal.IsMatch text
                ->
                if leadingZero.IsMatch text then
                    fail path value "invalid_literal" "Leading zeros are not part of the profile."

                YamlAtom.Number(numericLexeme text)
            | _ -> YamlAtom.String text

    let private readDocumentDetailed
        contextual
        (maxDepth: int)
        (maxEvents: int)
        (maxNodes: int)
        (maxOutputBytes: int64 option)
        (ct: CancellationToken)
        (file: string)
        (bytes: byte array)
        : Result<JsonElement * int * int64, TreeDiagnostic list> =
        ct.ThrowIfCancellationRequested()
        let mutable activePath = []

        let refused code location position message =
            Error [ StoreErrors.locatedWithPosition code (Some file) location position message ]

        try
            let utf8 = UTF8Encoding(false, true)
            let text = utf8.GetString bytes

            let text =
                if text.StartsWith("\uFEFF", StringComparison.Ordinal) then
                    text.Substring(1)
                else
                    text

            use source = new StringReader(text)
            let scanner = Scanner(source) :> IScanner

            let nextToken move =
                let moved = move ()

                if moved then
                    match scanner.Current with
                    | :? YamlDotNet.Core.Tokens.VersionDirective
                    | :? YamlDotNet.Core.Tokens.TagDirective ->
                        let mark = scanner.CurrentPosition

                        raise (
                            TreeYamlFailure(
                                "unsupported_yaml_feature",
                                pointer activePath,
                                $" (line {mark.Line}, column {mark.Column})",
                                "Directives are not part of the profile."
                            )
                        )
                    | _ -> ()

                moved

            // DocumentStart includes implicit default tags. Reject only explicit
            // directives at the token boundary, before parser defaults are added.
            let strictScanner =
                { new IScanner with
                    member _.Current = scanner.Current
                    member _.CurrentPosition = scanner.CurrentPosition
                    member _.MoveNext() = nextToken scanner.MoveNext

                    member _.MoveNextWithoutConsuming() =
                        nextToken scanner.MoveNextWithoutConsuming

                    member _.ConsumeCurrent() = scanner.ConsumeCurrent()
                }

            let parser = Parser(strictScanner)
            let mutable cursor = parser.Current
            let mutable count = 0
            let mutable nodes = 0
            let mutable duplicate: (string list * ParsingEvent) option = None

            let advance path =
                ct.ThrowIfCancellationRequested()
                activePath <- path

                if
                    count
                    >= maxEvents
                then
                    fail path cursor "yaml_event_limit" "The YAML event budget was exceeded."

                count <- count + 1
                cursor <- if parser.MoveNext() then parser.Current else null

            let checkNode path =
                match cursor with
                | :? AnchorAlias ->
                    fail
                        path
                        cursor
                        "unsupported_yaml_feature"
                        "Anchors and aliases are not part of the profile."
                | :? NodeEvent as node when not node.Anchor.IsEmpty ->
                    fail
                        path
                        cursor
                        "unsupported_yaml_feature"
                        "Anchors and aliases are not part of the profile."
                | :? NodeEvent as node when not node.Tag.IsEmpty ->
                    fail
                        path
                        cursor
                        "unsupported_yaml_feature"
                        "Explicit tags are not part of the profile."
                | _ -> ()

            use output = new MemoryStream()
            use writer = new Utf8JsonWriter(output, JsonWriterOptions(MaxDepth = maxDepth))

            let checkOutputSize path =
                match maxOutputBytes with
                | Some limit when writer.BytesCommitted + int64 writer.BytesPending > limit ->
                    raise (
                        TreeYamlFailure(
                            "tree.file_too_large",
                            pointer path,
                            "",
                            "The transcoded YAML document exceeds the byte limit."
                        )
                    )
                | _ -> ()

            let frames = Stack<YamlFrame>()

            let startValue path depth =
                if depth > maxDepth then
                    fail path cursor "depth_limit_exceeded" "The YAML nesting limit was exceeded."

                if nodes >= maxNodes then
                    fail path cursor "tree.document_node_limit" "The decoded document exceeds the available maxNodes for this exchange."
                nodes <- nodes + 1

                checkNode path

                match cursor with
                | :? Scalar as item ->
                    match scalar path item with
                    | YamlAtom.String value -> writer.WriteStringValue value
                    | YamlAtom.Number value ->
                        writer.WriteRawValue(value, skipInputValidation = false)
                    | YamlAtom.Boolean value -> writer.WriteBooleanValue value
                    | YamlAtom.Null -> writer.WriteNullValue()

                    checkOutputSize path
                    advance path
                | :? MappingStart ->
                    writer.WriteStartObject()
                    checkOutputSize path
                    advance path
                    frames.Push(MappingFrame(path, depth, HashSet<string>(StringComparer.Ordinal)))
                | :? SequenceStart ->
                    writer.WriteStartArray()
                    checkOutputSize path
                    advance path
                    frames.Push(SequenceFrame(path, depth, ref 0))
                | _ -> fail path cursor "invalid_yaml" "Expected a YAML value."

            advance []
            advance []

            if not (cursor :? DocumentStart) then
                fail [] cursor "invalid_yaml" "Expected exactly one YAML document."

            advance []
            checkNode []

            if not (cursor :? MappingStart) then
                fail [] cursor "invalid_type" "The root must be a mapping."

            startValue [] 1

            while frames.Count > 0 do
                match frames.Peek() with
                | MappingFrame(path, depth, keys) ->
                    if cursor :? MappingEnd then
                        writer.WriteEndObject()
                        checkOutputSize path
                        advance path
                        frames.Pop() |> ignore
                    else
                        checkNode path

                        let key, keyEvent =
                            match cursor with
                            | :? Scalar as item ->
                                match scalar path item with
                                | YamlAtom.String key -> key, item
                                | _ -> fail path item "invalid_type" "Mapping keys must be strings."
                            | _ -> fail path cursor "invalid_type" "Mapping keys must be strings."

                        let child =
                            key
                            :: path

                        if key = "<<" then
                            fail
                                child
                                keyEvent
                                "unsupported_yaml_feature"
                                "Merge keys are not part of the profile."

                        if not (keys.Add key) then
                            if contextual then
                                // The schema marker may occur later than the duplicate.
                                if duplicate.IsNone then
                                    duplicate <- Some(child, keyEvent)
                            else
                                fail child keyEvent "duplicate_member" "Duplicate mapping key."

                        writer.WritePropertyName key
                        checkOutputSize child
                        advance child
                        startValue child (depth + 1)
                | SequenceFrame(path, depth, nextIndex) ->
                    if cursor :? SequenceEnd then
                        writer.WriteEndArray()
                        checkOutputSize path
                        advance path
                        frames.Pop() |> ignore
                    else
                        let index = nextIndex.Value
                        nextIndex.Value <- index + 1
                        startValue (index.ToString(CultureInfo.InvariantCulture) :: path) (depth + 1)

            if not (cursor :? DocumentEnd) then
                fail [] cursor "invalid_yaml" "Expected the end of the YAML document."

            advance []

            if not (cursor :? StreamEnd) then
                fail [] cursor "invalid_yaml" "Expected exactly one YAML document."

            writer.Flush()
            checkOutputSize []
            let encoded = ReadOnlyMemory(output.GetBuffer(), 0, int output.Length)
            use document = JsonDocument.Parse(encoded, JsonDocumentOptions(MaxDepth = maxDepth))
            let root = document.RootElement

            match duplicate with
            | Some(path, keyEvent) ->
                let envelope = fst (root.TryGetProperty "$schema")

                let inContext =
                    match path with
                    | [ _ ]
                    | [ _; "context" ] -> true
                    | [ _; ("names" | "packagePaths" | "modulePaths" | "fqNames"); "context" ] ->
                        true
                    | _ -> false

                let code =
                    if
                        envelope
                        && inContext
                    then
                        "context.duplicate_entry"
                    else
                        "duplicate_member"

                fail path keyEvent code "Duplicate mapping key."
            | None -> Ok(root.Clone(), nodes, int64 encoded.Length)
        with
        | TreeYamlFailure(code, location, position, message) -> refused code location position message
        | :? YamlException as error ->
            refused
                "invalid_yaml"
                (pointer activePath)
                $" (line {error.Start.Line}, column {error.Start.Column})"
                error.Message
        | :? DecoderFallbackException -> refused "invalid_yaml" "/" "" "Expected UTF-8 input."

    let readLimited maxDepth maxEvents file bytes =
        readDocumentDetailed false maxDepth maxEvents Int32.MaxValue None CancellationToken.None file bytes
        |> Result.map (fun (root, _, _) -> root)
        |> Result.mapError StoreErrors.toStoreErrors

    let readCounted contextual maxDepth maxNodes file bytes =
        readDocumentDetailed contextual maxDepth 1_000_000 maxNodes None CancellationToken.None file bytes
        |> Result.map (fun (root, nodes, _) -> root, nodes)
        |> Result.mapError StoreErrors.toStoreErrors

    let readCountedDetailed contextual maxDepth maxNodes maxOutputBytes ct file bytes =
        readDocumentDetailed contextual maxDepth 1_000_000 maxNodes maxOutputBytes ct file bytes

    let read maxDepth file bytes =
        readLimited maxDepth 1_000_000 file bytes

    let private isAsciiWhitespace (b: byte) =
        b = 0x20uy || b = 0x09uy || b = 0x0Duy || b = 0x0Auy

    let private isWhitespaceOrEof (span: ReadOnlySpan<byte>) (index: int) =
        index >= span.Length || isAsciiWhitespace span[index]

    let private startsWithToken (span: ReadOnlySpan<byte>) (index: int) (token: string) =
        if span.Length - index < token.Length then
            false
        else
            let mutable matched = true
            let mutable k = 0

            while matched && k < token.Length do
                if span[index + k] <> byte token[k] then
                    matched <- false

                k <- k + 1

            matched && isWhitespaceOrEof span (index + token.Length)

    let looksLikeYamlDocument (bytes: ReadOnlyMemory<byte>) (ct: CancellationToken) : bool =
        ct.ThrowIfCancellationRequested()
        let mutable span = bytes.Span

        if
            span.IsEmpty
            || (span.Length >= 4
                && span[0] = 0xE0uy
                && span[1] = 0x01uy
                && span[2] = 0x00uy
                && span[3] = 0xEAuy)
        then
            false
        else
            if span.Length >= 3 && span[0] = 0xEFuy && span[1] = 0xBBuy && span[2] = 0xBFuy then
                span <- span.Slice 3

            let mutable i = 0

            while i < span.Length && isAsciiWhitespace span[i] do
                if (i &&& 1023) = 0 then
                    ct.ThrowIfCancellationRequested()

                i <- i + 1

            if i >= span.Length then
                false
            else
                let first = span[i]

                if first = byte '#' then
                    true
                elif first = byte '%' && (startsWithToken span i "%YAML" || startsWithToken span i "%TAG") then
                    true
                elif startsWithToken span i "---" then
                    true
                elif first = byte '[' then
                    false
                else
                    let fastDecision =
                        if first = byte '{' then
                            let mutable j = i + 1

                            while j < span.Length && isAsciiWhitespace span[j] do
                                if (j &&& 1023) = 0 then
                                    ct.ThrowIfCancellationRequested()

                                j <- j + 1

                            if j >= span.Length then Some false
                            elif span[j] = byte '"' || span[j] = byte '}' then Some false
                            elif span[j] = byte '#' then Some true
                            elif span[j] = byte '?' && isWhitespaceOrEof span (j + 1) then Some true
                            else None
                        else
                            None

                    match fastDecision with
                    | Some decided -> decided
                    | None ->
                        try
                            ct.ThrowIfCancellationRequested()
                            let encoding = UTF8Encoding(encoderShouldEmitUTF8Identifier = false, throwOnInvalidBytes = true)
                            let text = encoding.GetString(span.Slice(i).ToArray())
                            use reader = new StringReader(text)
                            let parser = Parser(reader)

                            if not (parser.MoveNext()) || not (parser.Current :? StreamStart) then
                                false
                            else
                                ct.ThrowIfCancellationRequested()

                                if not (parser.MoveNext()) then
                                    false
                                else
                                    match parser.Current with
                                    | :? DocumentStart as docStart when not docStart.IsImplicit -> true
                                    | :? DocumentStart ->
                                        ct.ThrowIfCancellationRequested()

                                        if not (parser.MoveNext()) then
                                            false
                                        else
                                            match parser.Current with
                                            | :? MappingStart as mapping when mapping.Style = MappingStyle.Block -> true
                                            | :? MappingStart as mapping when mapping.Style = MappingStyle.Flow ->
                                                ct.ThrowIfCancellationRequested()

                                                if not (parser.MoveNext()) then
                                                    false
                                                else
                                                    match parser.Current with
                                                    | :? Scalar as scalar when
                                                        scalar.Style = ScalarStyle.Plain
                                                        || scalar.Style = ScalarStyle.SingleQuoted
                                                        ->
                                                        // Advance past the first flow key to its value. When YAML synthesizes
                                                        // an implicit empty null value (for example truncated "{broken"), step
                                                        // one more event so EOF triggers YamlException (false) instead of
                                                        // misclassifying truncated JSON-like input as YAML.
                                                        ct.ThrowIfCancellationRequested()

                                                        if not (parser.MoveNext()) then
                                                            false
                                                        else
                                                            match parser.Current with
                                                            | :? Scalar as value when
                                                                value.Style = ScalarStyle.Plain
                                                                && value.Value.Length = 0
                                                                && value.Start = value.End
                                                                ->
                                                                ct.ThrowIfCancellationRequested()
                                                                parser.MoveNext()
                                                            | _ -> true
                                                    | _ -> false
                                            | _ -> false
                                    | _ -> false
                        with
                        | :? YamlException
                        | :? DecoderFallbackException -> false
