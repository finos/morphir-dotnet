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
///
/// Numbers need the shortest round-trip formatting and the correctly rounded parsing of .NET Core 3.0 and later. The
/// <c>netstandard2.0</c> build refuses to write a number, with
/// <see cref="T:System.PlatformNotSupportedException"/>, when it is loaded on another runtime such as .NET Framework,
/// where <c>ToString("R")</c> can give more digits than the shortest (<c>5e-324</c> as <c>4.94065645841247E-324</c>).
/// </remarks>
module Morphir.Json.CanonicalJson

#if NETSTANDARD2_0
open Morphir.Compat
#endif

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

/// <summary>
/// Refuses a runtime whose number formatting is not the shortest round trip: every runtime except .NET Core 3.x and
/// .NET 5 and later. The runtime is given by its <c>RuntimeInformation.FrameworkDescription</c> and its
/// <c>Environment.Version</c>; .NET Core 2.x describes itself as ".NET Core 4.6" with version 4.0, so a ".NET Core"
/// runtime must also have version 3.
/// </summary>
/// <exception cref="T:System.PlatformNotSupportedException">The runtime is not .NET Core 3.0 or later.</exception>
let internal checkRuntime (frameworkDescription: string) (version: Version) : unit =
    let isDotNet =
        frameworkDescription.Length > 5
        && frameworkDescription.StartsWith(".NET ", StringComparison.Ordinal)
        && Char.IsDigit frameworkDescription.[5]

    let supported =
        if frameworkDescription.StartsWith(".NET Core ", StringComparison.Ordinal) then version.Major = 3
        elif isDotNet then version.Major >= 5
        else false

    if not supported then
        raise (
            PlatformNotSupportedException(
                $"RFC 8785 canonical JSON numbers need .NET Core 3.0 or later; this runtime is {frameworkDescription} ({version})."
            )
        )

#if NETSTANDARD2_0
/// The check of the runtime this build is loaded on, made once.
let private runtimeChecked =
    lazy
        (checkRuntime
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            Environment.Version)
#endif

/// <summary>The ECMAScript form of a double (ECMA-262 Number::toString, RFC 8785 section 3.2.2.3).</summary>
/// <remarks>The <c>netstandard2.0</c> build refuses on a runtime other than .NET Core 3.0 or later, whose number
/// formatting is not the shortest round trip.</remarks>
/// <exception cref="T:System.ArgumentException">NaN or an infinity, which JSON cannot hold.</exception>
/// <exception cref="T:System.PlatformNotSupportedException">The <c>netstandard2.0</c> build is loaded on a runtime
/// other than .NET Core 3.0 or later, such as .NET Framework.</exception>
let number (value: float) : string =
#if NETSTANDARD2_0
    runtimeChecked.Force()
#endif
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

/// The text of the reader's current token, as written in the input.
let private tokenText (reader: byref<Utf8JsonReader>) = Encoding.UTF8.GetString reader.ValueSpan

/// The string of the reader's current string or property-name token.
let private tokenString (reader: byref<Utf8JsonReader>) =
    let raw = tokenText &reader
    if reader.ValueIsEscaped then unescape raw else (checkSurrogates raw; raw)

/// The reader's current number token as a double.
let private tokenNumber (reader: byref<Utf8JsonReader>) =
    let text = tokenText &reader

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

/// The JSON Pointer (RFC 6901) of a value, from the reversed path of its escaped reference tokens.
let private pointerOf (path: string list) =
    match path with
    | [] -> ""
    | _ -> "/" + String.Join("/", List.rev path)

/// A member name as a JSON Pointer reference token: <c>~</c> as <c>~0</c> and <c>/</c> as <c>~1</c>.
let private token (name: string) = name.Replace("~", "~0").Replace("/", "~1")

/// <summary>How <see cref="writeSourceForm"/> writes the members of an object, or of the objects inside an array.</summary>
[<RequireQualifiedAccess>]
type MemberOrder =
    /// <summary>Sorted by their UTF-16 code units, as RFC 8785 sorts them.</summary>
    | Sorted
    /// <summary>In their order in the value, for this object; the objects inside it are asked again.</summary>
    | Kept
    /// <summary>In their order in the value, for this object or array and every object inside it.</summary>
    | KeptThroughout

/// How a value is written: its numbers in the ECMAScript form of RFC 8785 or as their source lexemes, and the order of
/// the members of each object, by its JSON Pointer; no order function sorts every object.
type private Form = {
    SourceNumbers: bool
    Order: (string -> MemberOrder) option
}

let rec private readNode (form: Form) (throughout: bool) (path: string list) (reader: byref<Utf8JsonReader>) : Node =
    let orderHere () =
        match throughout, form.Order with
        | true, _ -> MemberOrder.KeptThroughout
        | false, Some order -> order (pointerOf path)
        | false, None -> MemberOrder.Sorted

    match reader.TokenType with
    | JsonTokenType.Null -> Literal "null"
    | JsonTokenType.True -> Literal "true"
    | JsonTokenType.False -> Literal "false"
    | JsonTokenType.Number when form.SourceNumbers -> Literal(tokenText &reader)
    | JsonTokenType.Number -> Literal(number (tokenNumber &reader))
    | JsonTokenType.String -> Text(tokenString &reader)
    | JsonTokenType.StartArray ->
        let inside = orderHere () = MemberOrder.KeptThroughout
        let items = ResizeArray()

        while reader.Read() && reader.TokenType <> JsonTokenType.EndArray do
            items.Add(readNode form inside (string items.Count :: path) &reader)

        Items(List.ofSeq items)
    | JsonTokenType.StartObject ->
        let order = orderHere ()
        let inside = order = MemberOrder.KeptThroughout
        let members = ResizeArray()
        let names = HashSet<string>(StringComparer.Ordinal)

        while reader.Read() && reader.TokenType <> JsonTokenType.EndObject do
            let name = tokenString &reader

            if not (names.Add name) then
                refuse $"the object has the member \"{name}\" twice"

            reader.Read() |> ignore
            members.Add((name, readNode form inside (token name :: path) &reader))

        if order = MemberOrder.Sorted then
            // Ordinal comparison compares UTF-16 code units as unsigned integers (RFC 8785 section 3.2.3).
            Members(members |> List.ofSeq |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b)))
        else
            Members(List.ofSeq members)
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

/// Reads a value in a form and writes it.
let private writeIn (form: Form) (element: JsonElement) : byte array =
    let mutable reader =
        Utf8JsonReader(ReadOnlySpan(Encoding.UTF8.GetBytes(element.GetRawText())), JsonReaderOptions(MaxDepth = MaxDepth))

    reader.Read() |> ignore
    let node = readNode form false [] &reader
    let output = StringBuilder()
    appendNode output node
    utf8.GetBytes(output.ToString())

/// <summary>The RFC 8785 canonical form of a JSON value, as UTF-8 bytes.</summary>
/// <exception cref="T:System.ArgumentException">The value holds a lone surrogate, a number outside the range of a
/// double, or an object with the same member twice.</exception>
/// <exception cref="T:System.PlatformNotSupportedException">The <c>netstandard2.0</c> build is loaded on a runtime
/// other than .NET Core 3.0 or later, such as .NET Framework, whose number formatting and parsing are not those of
/// RFC 8785. It refuses every value, numbers or not, so that the output never depends on what the value holds.</exception>
let write (element: JsonElement) : byte array =
#if NETSTANDARD2_0
    runtimeChecked.Force()
#endif
    writeIn { SourceNumbers = false; Order = None } element

/// <summary>
/// The written form of the compaction policy <c>morphir.compact/0.1.0-draft.1</c> (as the parent amends it), as UTF-8
/// bytes: RFC 8785 with two changes. Every number is written as its source lexeme, exactly as the value spells it,
/// never in the ECMAScript form, so <c>9007199254740993</c>, <c>1.0</c> and <c>1e2</c> stay as they are. And the
/// members of an object are sorted by their UTF-16 code units unless <paramref name="memberOrder"/>, given the JSON
/// Pointer (RFC 6901) of the object or of an array around it (<c>""</c> for the value itself), says to keep them in
/// their order: for that object (<c>Kept</c>, such as a v4 value's parameters), or for it and everything inside it
/// (<c>KeptThroughout</c>, such as the content of a document literal). Strings have the escapes of RFC 8785, and there
/// is no whitespace. C# calls this <c>WriteSourceForm</c>.
/// </summary>
/// <exception cref="T:System.ArgumentException">The value holds a lone surrogate, or an object with the same member
/// twice.</exception>
[<CompiledName("WriteSourceForm")>]
let writeSourceForm (memberOrder: string -> MemberOrder) (element: JsonElement) : byte array =
    writeIn { SourceNumbers = true; Order = Some memberOrder } element
