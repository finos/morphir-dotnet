namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// <summary>
/// Writes a JSON value as canonical YAML, the text form of the Morphir YAML profile
/// (<c>docs/spec/ir/schemas/v4/yaml-profile.md</c>, "Canonical writer"). The YAML reader of the profile reads the text
/// back as the same JSON value: the same members in the same order, and the same number lexemes.
/// </summary>
/// <remarks>
/// A mapping is a block mapping with two-space indentation, and an empty mapping is <c>{}</c>. A sequence is a flow
/// sequence, <c>[a, b]</c>, when it holds no mapping at any depth, and a block sequence of <c>- </c> items otherwise; an
/// empty sequence is <c>[]</c>. A string is plain unless plain YAML would read it as something else or it holds YAML
/// syntax, and then it is double-quoted with JSON escapes. A number keeps its JSON lexeme. The text ends with one line
/// feed and has no tag, anchor, alias, comment, directive, document marker or block scalar. A key whose written scalar
/// is longer than the 1,024 characters YAML allows an implicit key is an explicit key, <c>? key</c>, with its value after
/// <c>:</c> on the next line. A mapping that repeats a member, or has the member <c>&lt;&lt;</c>, has no spelling in the
/// profile and is refused.
/// </remarks>
[<RequireQualifiedAccess>]
module CanonicalYaml =
    let private integer = Regex(@"\A[-+]?[0-9]+\z", RegexOptions.CultureInvariant)

    let private float =
        Regex(@"\A[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?\z", RegexOptions.CultureInvariant)

    /// A whole octal or hexadecimal token, as the profile's reader refuses it (TreeYaml). A string that only starts like
    /// one, such as <c>0xylophone</c>, is a plain string.
    let private based =
        Regex(@"\A[-+]?0([oO][0-7]+|[xX][0-9a-fA-F]+)\z", RegexOptions.CultureInvariant)

    let private keywords =
        set [ "true"; "True"; "TRUE"; "false"; "False"; "FALSE"; "null"; "Null"; "NULL"; "~" ]

    let private indicators = "-?:,[]{}#&*!|>'\"%@`"

    let private flowIndicators = "[]{},:#"

    let private isNonFinite (text: string) =
        [ ".inf"; "+.inf"; "-.inf"; ".nan" ]
        |> List.exists (fun token -> String.Equals(text, token, StringComparison.OrdinalIgnoreCase))

    /// Whether plain resolution would read the text as something other than this string.
    let private resolvesToOther (text: string) =
        keywords.Contains text
        || integer.IsMatch text
        || float.IsMatch text
        || based.IsMatch text
        || isNonFinite text

    /// A character that the profile's reader or another YAML reader does not take raw: a control character, the
    /// byte-order mark, a line or paragraph separator, or the noncharacters U+FFFE and U+FFFF. JSON text gives no half of a surrogate pair: System.Text.Json refuses to read one.
    let private mustEscape (text: string) (index: int) =
        let c = text[index]

        Char.IsControl c
        || c = '\uFEFF'
        || c = '\u2028'
        || c = '\u2029'
        || c = '\uFFFE'
        || c = '\uFFFF'

    let private hasEscape (text: string) =
        Seq.exists (mustEscape text) (seq { 0 .. text.Length - 1 })

    /// Whether the text is a YAML document marker, <c>---</c> or <c>...</c> alone or before white space.
    let private isMarker (text: string) =
        [ "---"; "..." ]
        |> List.exists (fun marker ->
            text = marker
            || text.StartsWith(marker + " ", StringComparison.Ordinal)
            || text.StartsWith(marker + "\t", StringComparison.Ordinal)
        )

    /// Whether a string may be written plain: rule 4 of the canonical writer. A document marker at the start of a line
    /// would end the document, so <c>...</c> alone is quoted too; <c>...suffix</c> stays plain.
    let isPlain (inFlow: bool) (text: string) =
        not (
            text = ""
            || resolvesToOther text
            || indicators.IndexOf text[0] >= 0
            || text[0] = ' '
            || text.EndsWith(" ", StringComparison.Ordinal)
            || text.EndsWith(":", StringComparison.Ordinal)
            || isMarker text
            || text.Contains(": ")
            || text.Contains(" #")
            || hasEscape text
            || (inFlow && Seq.exists (fun (c: char) -> flowIndicators.IndexOf c >= 0) text)
        )

    /// A double-quoted scalar with JSON escapes. Every character that is not a control character stays as UTF-8.
    let private quoted (builder: StringBuilder) (text: string) =
        builder.Append '"' |> ignore

        for index in 0 .. text.Length - 1 do
            match text[index] with
            | '"' -> builder.Append "\\\""
            | '\\' -> builder.Append "\\\\"
            | '\n' -> builder.Append "\\n"
            | '\r' -> builder.Append "\\r"
            | '\t' -> builder.Append "\\t"
            | '\b' -> builder.Append "\\b"
            | '\f' -> builder.Append "\\f"
            | c when mustEscape text index -> builder.Append("\\u").Append((int c).ToString("x4", CultureInfo.InvariantCulture))
            | c -> builder.Append c
            |> ignore

        builder.Append '"' |> ignore

    let private text (builder: StringBuilder) (inFlow: bool) (value: string) =
        if isPlain inFlow value then
            builder.Append value |> ignore
        else
            quoted builder value

    let private isEmptyContainer (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Object -> Seq.isEmpty (element.EnumerateObject())
        | JsonValueKind.Array -> element.GetArrayLength() = 0
        | _ -> false

    /// Whether a sequence holds a mapping at any depth, which makes it a block sequence. It walks a stack, not the call
    /// stack, so a deep sequence cannot overflow it.
    let private holdsMapping (element: JsonElement) =
        let pending = Collections.Generic.Stack<JsonElement>()
        pending.Push element
        let mutable found = false

        while not found && pending.Count > 0 do
            let current = pending.Pop()

            match current.ValueKind with
            | JsonValueKind.Object -> found <- true
            | JsonValueKind.Array ->
                for item in current.EnumerateArray() do
                    if item.ValueKind = JsonValueKind.Object || item.ValueKind = JsonValueKind.Array then
                        pending.Push item
            | _ -> ()

        found

    /// A value written on the line it starts: a scalar, an empty container or a flow sequence.
    let rec private inline' (builder: StringBuilder) (inFlow: bool) (element: JsonElement) =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        match element.ValueKind with
        | JsonValueKind.String -> text builder inFlow (element.GetString() |> nonNull)
        | JsonValueKind.Number -> builder.Append(element.GetRawText()) |> ignore
        | JsonValueKind.True -> builder.Append "true" |> ignore
        | JsonValueKind.False -> builder.Append "false" |> ignore
        | JsonValueKind.Null -> builder.Append "null" |> ignore
        | JsonValueKind.Object -> builder.Append "{}" |> ignore
        | JsonValueKind.Array ->
            builder.Append '[' |> ignore
            let mutable first = true

            for item in element.EnumerateArray() do
                if not first then
                    builder.Append ", " |> ignore

                first <- false
                inline' builder true item

            builder.Append ']' |> ignore
        | kind -> invalidArg (nameof element) $"A JSON value of kind {kind} has no YAML form."

    /// Whether a value starts a block on the next lines rather than staying on its line.
    let private isBlock (element: JsonElement) =
        not (isEmptyContainer element)
        && (element.ValueKind = JsonValueKind.Object
            || (element.ValueKind = JsonValueKind.Array && holdsMapping element))

    let private indent (builder: StringBuilder) (level: int) = builder.Append(' ', level) |> ignore

    /// The longest implicit key YAML allows, in characters of its written scalar.
    [<Literal>]
    let private MaxImplicitKey = 1024

    /// A block mapping at the column. The first member is written on the current line when the caller already wrote
    /// the indentation or a <c>- </c>. A key whose written scalar is longer than an implicit key may be is written as an
    /// explicit key, <c>? key</c> with its value after <c>:</c> on the next line.
    let rec private mapping (builder: StringBuilder) (column: int) (startsInline: bool) (element: JsonElement) =
        RuntimeHelpers.EnsureSufficientExecutionStack()
        let mutable first = true
        let seen = Collections.Generic.HashSet<string>(StringComparer.Ordinal)

        for property in element.EnumerateObject() do
            if property.Name = "<<" then
                invalidArg
                    (nameof element)
                    "The mapping key << is a merge key, which the YAML profile refuses even when it is quoted."

            if not (seen.Add property.Name) then
                invalidArg
                    (nameof element)
                    ("The mapping repeats the member \""
                     + JsonEncodedText.Encode(property.Name).ToString()
                     + "\", which the YAML profile refuses as duplicate_member.")

            if not (first && startsInline) then
                indent builder column

            first <- false
            let key = StringBuilder()
            text key false property.Name

            if key.Length > MaxImplicitKey then
                builder.Append("? ").Append(key).Append('\n') |> ignore
                indent builder column
            else
                builder.Append key |> ignore

            builder.Append ':' |> ignore
            member' builder column property.Value

    and private member' (builder: StringBuilder) (column: int) (value: JsonElement) =
        if isBlock value then
            builder.Append '\n' |> ignore

            if value.ValueKind = JsonValueKind.Object then
                mapping builder (column + 2) false value
            else
                sequence builder (column + 2) false value
        else
            builder.Append ' ' |> ignore
            inline' builder false value
            builder.Append '\n' |> ignore

    /// A block sequence at the column. A mapping or sequence item starts on its <c>- </c> line.
    and private sequence (builder: StringBuilder) (column: int) (startsInline: bool) (element: JsonElement) =
        RuntimeHelpers.EnsureSufficientExecutionStack()
        let mutable first = true

        for item in element.EnumerateArray() do
            if not (first && startsInline) then
                indent builder column

            first <- false
            builder.Append "- " |> ignore

            if isBlock item then
                if item.ValueKind = JsonValueKind.Object then
                    mapping builder (column + 2) true item
                else
                    sequence builder (column + 2) true item
            else
                inline' builder false item
                builder.Append '\n' |> ignore

    /// <summary>
    /// The canonical YAML text of a JSON value, ending in one line feed. A value with no YAML spelling in the profile,
    /// a mapping key <c>&lt;&lt;</c>, raises <see cref="ArgumentException"/>; <see cref="ofJson"/> returns it as an error.
    /// </summary>
    [<CompiledName("Write")>]
    let write (element: JsonElement) : string =
        let builder = StringBuilder()

        match element.ValueKind with
        | JsonValueKind.Object when isBlock element -> mapping builder 0 false element
        | JsonValueKind.Array when isBlock element -> sequence builder 0 false element
        | _ ->
            inline' builder false element
            builder.Append '\n' |> ignore

        builder.ToString()

    /// <summary>The UTF-8 bytes of <see cref="write"/>, with no byte-order mark.</summary>
    [<CompiledName("WriteBytes")>]
    let writeBytes (element: JsonElement) : byte array =
        UTF8Encoding(false).GetBytes(write element)

    let private jsonOptions = JsonDocumentOptions(MaxDepth = 1_000_000)

    /// <summary>
    /// Writes JSON text as canonical YAML. Text that is not JSON gives the parser's message, and a value with no YAML
    /// spelling in the profile, such as a mapping key <c>&lt;&lt;</c>, gives the reason. A deep value is written
    /// on a thread with a large stack when the caller's stack runs short.
    /// </summary>
    [<CompiledName("OfJson")>]
    let ofJson (json: ReadOnlyMemory<byte>) : Result<byte array, string> =
        let run () =
            try
                use document = JsonDocument.Parse(json, jsonOptions)
                Ok(writeBytes document.RootElement)
            with
            | :? JsonException as error -> Error error.Message
            | :? ArgumentException as error -> Error error.Message
            // A string with half of a surrogate pair, which no UTF-8 text can hold.
            | :? InvalidOperationException as error -> Error error.Message

        try
            run ()
        with :? InsufficientExecutionStackException ->
            let mutable result = Error "The document nests too deeply to write."

            let thread =
                Threading.Thread(
                    (fun () ->
                        try
                            result <- run ()
                        with :? InsufficientExecutionStackException ->
                            ()
                    ),
                    256 * 1024 * 1024
                )

            thread.Start()
            thread.Join()
            result

    /// <summary>
    /// The YAML location of a file of a JSON document tree: <c>.json</c> becomes <c>.yaml</c>, the only extension a
    /// canonical YAML tree uses, so <c>manifest.json</c> is <c>manifest.yaml</c>.
    /// </summary>
    [<CompiledName("TreeLocation")>]
    let treeLocation (location: string) =
        if location.EndsWith(".json", StringComparison.Ordinal) then
            location.Substring(0, location.Length - ".json".Length) + ".yaml"
        else
            location

    /// <summary>
    /// The canonical YAML files of a JSON document tree: each <c>.json</c> file at its <see cref="treeLocation"/>, with
    /// the same value. A file that is not JSON fails with its location and the parser's message.
    /// </summary>
    let ofJsonTree (files: (string * ReadOnlyMemory<byte>) list) : Result<(string * byte array) list, string> =
        let rec convert done' remaining =
            match remaining with
            | [] -> Ok(List.rev done')
            | (location: string, bytes) :: rest ->
                match ofJson bytes with
                | Ok yaml -> convert ((treeLocation location, yaml) :: done') rest
                | Error message -> Error $"{location}: {message}"

        convert [] files

/// <summary>C# members of <see cref="CanonicalYaml"/>.</summary>
[<AbstractClass; Sealed>]
type CanonicalYamlWriter =
    /// <summary>
    /// Writes JSON text as canonical YAML. It sets <paramref name="yaml"/> and returns true, or sets
    /// <paramref name="failure"/> to the reason (the JSON parser's message, or a value with no YAML spelling) and returns
    /// false.
    /// </summary>
    static member TryWriteJson
        (
            json: ReadOnlyMemory<byte>,
            [<Out; MaybeNullWhen(false)>] yaml: byref<byte array>,
            [<Out; MaybeNullWhen(true)>] failure: byref<string>
        ) : bool =
        match CanonicalYaml.ofJson json with
        | Ok bytes ->
            yaml <- bytes
            failure <- Unchecked.defaultof<_>
            true
        | Error message ->
            yaml <- Unchecked.defaultof<_>
            failure <- message
            false

    /// <summary>The canonical YAML text of a JSON value.</summary>
    static member Write(element: JsonElement) : string = CanonicalYaml.write element
