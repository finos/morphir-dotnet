/// <summary>
/// The JSON Canonicalization Scheme of RFC 8785 (JCS): object members sorted by the UTF-16 code units of their names,
/// numbers in the ECMAScript form of their IEEE 754 double, strings with only the escapes of RFC 8785 and raw
/// non-ASCII text, no whitespace, encoded as UTF-8. The compaction policy measures every size as the UTF-8 bytes of
/// this form, so two hosts weigh a file the same. No reflection is used, so the writer is trim- and AOT-safe.
/// </summary>
/// <remarks>
/// The writer reads the element's own JSON text, not its decoded strings: <c>JsonElement.GetString</c> replaces a lone
/// surrogate escape with U+FFFD, and RFC 8785 requires the writer to refuse it. Every refusal raises
/// <see cref="T:System.ArgumentException"/>: a lone surrogate, a number outside the double range, a member named twice
/// in one object.
/// </remarks>
module Morphir.Json.CanonicalJson

open System
open System.Collections.Generic
open System.Globalization
open System.Text
open System.Text.Json

/// UTF-8 that throws on a lone surrogate instead of writing U+FFFD.
let private utf8 = UTF8Encoding(false, true)

/// The depth of nesting the writer reads. The model's readers stop at 512 (V4JsonDecoding.MaxDepth); this is above it.
[<Literal>]
let private MaxDepth = 1024

let private refuse message = raise (ArgumentException("RFC 8785: " + message))

/// <summary>The ECMAScript form of a double (ECMA-262 Number::toString, RFC 8785 section 3.2.2.3).</summary>
/// <exception cref="T:System.ArgumentException">NaN or an infinity, which JSON cannot hold.</exception>
let number (value: float) : string =
    if Double.IsNaN value || Double.IsInfinity value then
        refuse $"{value} is not a JSON number"
    elif value = 0.0 then
        "0"
    else
        // "R" is the shortest text that reads back as the same double (.NET Core 3.0 and later). It is split into its
        // significant digits and the position of the decimal point, then laid out as ECMAScript does.
        let text = (abs value).ToString("R", CultureInfo.InvariantCulture)

        let mantissa, exponent =
            match text.IndexOfAny [| 'E'; 'e' |] with
            | -1 -> text, 0
            | at -> text.Substring(0, at), Int32.Parse(text.Substring(at + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)

        let integral, fraction =
            match mantissa.IndexOf '.' with
            | -1 -> mantissa, ""
            | at -> mantissa.Substring(0, at), mantissa.Substring(at + 1)

        let allDigits = integral + fraction
        let leadingZeros = allDigits.Length - allDigits.TrimStart('0').Length
        let digits = allDigits.TrimStart('0').TrimEnd('0')
        // value = 0.digits × 10^point
        let point = integral.Length + exponent - leadingZeros
        let k = digits.Length

        let laidOut =
            if k <= point && point <= 21 then
                digits + String('0', point - k)
            elif 0 < point && point <= 21 then
                digits.Substring(0, point) + "." + digits.Substring point
            elif -6 < point && point <= 0 then
                "0." + String('0', -point) + digits
            else
                let e = point - 1
                let sign = if e >= 0 then "+" else "-"
                let significand = if k = 1 then digits else digits.Substring(0, 1) + "." + digits.Substring 1
                significand + "e" + sign + string (abs e)

        if value < 0.0 then "-" + laidOut else laidOut

let private hex = "0123456789abcdef"

/// Appends a string literal with the escapes of RFC 8785 section 3.2.2.2 and nothing else.
let private appendString (output: StringBuilder) (value: string) =
    output.Append '"' |> ignore

    for c in value do
        match c with
        | '"' -> output.Append "\\\"" |> ignore
        | '\\' -> output.Append "\\\\" |> ignore
        | '\b' -> output.Append "\\b" |> ignore
        | '\t' -> output.Append "\\t" |> ignore
        | '\n' -> output.Append "\\n" |> ignore
        | '\012' -> output.Append "\\f" |> ignore
        | '\r' -> output.Append "\\r" |> ignore
        | c when c < ' ' ->
            output.Append("\\u00").Append(hex.[int c >>> 4]).Append(hex.[int c &&& 0xF]) |> ignore
        | c -> output.Append c |> ignore

    output.Append '"' |> ignore

/// Refuses a string that holds a lone surrogate.
let private checkSurrogates (value: string) =
    let mutable i = 0

    while i < value.Length do
        let c = value.[i]

        if Char.IsHighSurrogate c && i + 1 < value.Length && Char.IsLowSurrogate value.[i + 1] then
            i <- i + 2
        elif Char.IsSurrogate c then
            refuse $"the string holds the lone surrogate U+{int c:X4}"
        else
            i <- i + 1

/// <summary>The UTF-8 bytes of a string as a JCS string literal, with its quotes and escapes.</summary>
/// <exception cref="T:System.ArgumentException">The string holds a lone surrogate.</exception>
let byteLength (value: string) : int =
    checkSurrogates value
    let literal = StringBuilder(value.Length + 2)
    appendString literal value
    utf8.GetByteCount(literal.ToString())

/// The text of a JSON string token (between its quotes, with its escapes) as UTF-16 code units, lone surrogates kept.
let private unescape (token: string) =
    let output = StringBuilder(token.Length)
    let mutable i = 0

    while i < token.Length do
        let c = token.[i]

        if c = '\\' then
            match token.[i + 1] with
            | 'u' ->
                output.Append(char (Int32.Parse(token.Substring(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)))
                |> ignore

                i <- i + 6
            | escaped ->
                output.Append(
                    match escaped with
                    | 'b' -> '\b'
                    | 'f' -> '\012'
                    | 'n' -> '\n'
                    | 'r' -> '\r'
                    | 't' -> '\t'
                    | other -> other // '"', '\\' and '/'
                )
                |> ignore

                i <- i + 2
        else
            output.Append c |> ignore
            i <- i + 1

    let value = output.ToString()
    checkSurrogates value
    value

/// The string of the reader's current string or property-name token.
let private tokenString (reader: byref<Utf8JsonReader>) =
    let raw =
#if NETSTANDARD2_0
        Encoding.UTF8.GetString(reader.ValueSpan.ToArray())
#else
        Encoding.UTF8.GetString reader.ValueSpan
#endif
    if reader.ValueIsEscaped then unescape raw else (checkSurrogates raw; raw)

/// The reader's current number token as a double.
let private tokenNumber (reader: byref<Utf8JsonReader>) =
    let text =
#if NETSTANDARD2_0
        Encoding.UTF8.GetString(reader.ValueSpan.ToArray())
#else
        Encoding.UTF8.GetString reader.ValueSpan
#endif
    let parsed =
        try
            Double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
        with :? OverflowException ->
            Double.PositiveInfinity

    if Double.IsNaN parsed || Double.IsInfinity parsed then
        refuse $"{text} is outside the range of a double"

    parsed

/// One value of the input, read so that its members can be sorted before they are written.
type private Node =
    | Literal of string
    | Text of string
    | Items of Node list
    | Members of (string * Node) list

let rec private readNode (reader: byref<Utf8JsonReader>) : Node =
    match reader.TokenType with
    | JsonTokenType.Null -> Literal "null"
    | JsonTokenType.True -> Literal "true"
    | JsonTokenType.False -> Literal "false"
    | JsonTokenType.Number -> Literal(number (tokenNumber &reader))
    | JsonTokenType.String -> Text(tokenString &reader)
    | JsonTokenType.StartArray ->
        let items = ResizeArray()

        while reader.Read() && reader.TokenType <> JsonTokenType.EndArray do
            items.Add(readNode &reader)

        Items(List.ofSeq items)
    | JsonTokenType.StartObject ->
        let members = ResizeArray()
        let names = HashSet<string>(StringComparer.Ordinal)

        while reader.Read() && reader.TokenType <> JsonTokenType.EndObject do
            let name = tokenString &reader

            if not (names.Add name) then
                refuse $"the object has the member \"{name}\" twice"

            reader.Read() |> ignore
            members.Add((name, readNode &reader))

        // Ordinal comparison compares UTF-16 code units as unsigned integers (RFC 8785 section 3.2.3).
        Members(members |> List.ofSeq |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b)))
    | other -> refuse $"unexpected token {other}"

let rec private appendNode (output: StringBuilder) (node: Node) =
    match node with
    | Literal text -> output.Append text |> ignore
    | Text value -> appendString output value
    | Items items ->
        output.Append '[' |> ignore

        items
        |> List.iteri (fun index item ->
            if index > 0 then
                output.Append ',' |> ignore

            appendNode output item)

        output.Append ']' |> ignore
    | Members members ->
        output.Append '{' |> ignore

        members
        |> List.iteri (fun index (name, value) ->
            if index > 0 then
                output.Append ',' |> ignore

            appendString output name
            output.Append ':' |> ignore
            appendNode output value)

        output.Append '}' |> ignore

/// <summary>The RFC 8785 canonical form of a JSON value, as UTF-8 bytes.</summary>
/// <exception cref="T:System.ArgumentException">The value holds a lone surrogate, a number outside the range of a
/// double, or an object with the same member twice.</exception>
let write (element: JsonElement) : byte array =
    let mutable reader =
        Utf8JsonReader(ReadOnlySpan(Encoding.UTF8.GetBytes(element.GetRawText())), JsonReaderOptions(MaxDepth = MaxDepth))

    reader.Read() |> ignore
    let node = readNode &reader
    let output = StringBuilder()
    appendNode output node
    utf8.GetBytes(output.ToString())
