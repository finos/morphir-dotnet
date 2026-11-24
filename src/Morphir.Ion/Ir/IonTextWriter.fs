namespace Morphir.Ion

open System
open System.Globalization
open System.Numerics
open System.Runtime.CompilerServices
open System.Text

/// An Ion value as the canonical writer emits it. Each case has one spelling, so the text follows from the value.
type internal IonText =
    /// A symbol. It is bare when its text is a legal unquoted Ion symbol, and single-quoted otherwise.
    | Symbol of string
    /// A string, double-quoted.
    | Text of string
    | Integer of BigInteger
    /// A decimal, written as this exact Ion text, such as <c>1.50</c> or <c>1.5d3</c>.
    | DecimalText of string
    /// A finite float, written in its shortest form with an exponent, such as <c>1.5e0</c>.
    | Float of float
    | Bool of bool
    /// The untyped null, <c>null</c>.
    | Null
    /// A struct, its members in order.
    | Struct of (string * IonText) list
    | List of IonText list
    | Sexp of IonText list
    /// A value with Ion annotations, such as <c>public::def::module::{ }</c>.
    | Annotated of string list * IonText

/// The fixed spellings of the Ion IR format that the writers share.
module internal IonIrFormat =
    /// The exact contract version that a draft.1 writer states.
    [<Literal>]
    let IonVersion = "0.1.0-draft.1"

    /// The last value of a single-file datagram.
    let footer = Annotated([ "morphir_footer" ], Struct [])

/// The canonical Ion 1.0 text layout of the Morphir Ion draft: one member, item or operand per line at two spaces
/// for each level, a comma after every struct member and list item, and no comma between operands. A file is its
/// top-level values, each followed by one line feed.
module internal IonTextWriter =
    /// The Ion keywords and forms that a bare identifier would not read back as a symbol.
    let private reserved =
        set [ "null"; "true"; "false"; "nan" ]

    let private isIdentifierStart (c: char) =
        (c >= 'a' && c <= 'z')
        || (c >= 'A' && c <= 'Z')
        || c = '_'
        || c = '$'

    let private isIdentifierPart (c: char) =
        isIdentifierStart c
        || (c >= '0' && c <= '9')

    /// Whether a symbol can be written without quotes: an Ion identifier that is not a keyword and not a symbol ID
    /// such as <c>$10</c>.
    let isBareSymbol (text: string) =
        text.Length > 0
        && isIdentifierStart text[0]
        && Seq.forall isIdentifierPart text
        && not (reserved.Contains text)
        && not (
            text[0] = '$'
            && text.Length > 1
            && Seq.forall Char.IsAsciiDigit (text.Substring 1)
        )

    /// Writes text between quotes. The draft's named escapes cover the common controls. Any other control
    /// character, which Ion text does not allow raw, is written as a two-digit <c>\x</c> escape.
    let private appendQuoted (builder: StringBuilder) (quote: char) (text: string) =
        builder.Append quote
        |> ignore

        for c in text do
            match c with
            | '\000' -> builder.Append "\\0"
            | '\a' -> builder.Append "\\a"
            | '\b' -> builder.Append "\\b"
            | '\t' -> builder.Append "\\t"
            | '\n' -> builder.Append "\\n"
            | '\v' -> builder.Append "\\v"
            | '\f' -> builder.Append "\\f"
            | '\r' -> builder.Append "\\r"
            | '\\' -> builder.Append "\\\\"
            | '"' -> builder.Append "\\\""
            | '\'' -> builder.Append "\\'"
            | c when c < ' ' || c = '\u007F' ->
                builder.Append("\\x").Append((int c).ToString("x2", CultureInfo.InvariantCulture))
            | c -> builder.Append c
            |> ignore

        builder.Append quote
        |> ignore

    let private appendSymbol (builder: StringBuilder) (text: string) =
        if isBareSymbol text then
            builder.Append text
            |> ignore
        else
            appendQuoted builder '\'' text

    /// The shortest digits that read back as the same double, in scientific form: one digit before the point, the
    /// other digits after it, and the exponent, such as <c>1.5e0</c>, <c>1e2</c> or <c>-2.5e-7</c>.
    let floatText (value: float) =
        let shortest = value.ToString("R", CultureInfo.InvariantCulture)
        let negative = shortest.StartsWith '-'
        let unsigned = if negative then shortest.Substring 1 else shortest
        let exponentAt = unsigned.IndexOf 'E'

        let mantissa, power =
            if exponentAt < 0 then
                unsigned, 0
            else
                unsigned.Substring(0, exponentAt),
                Int32.Parse(unsigned.Substring(exponentAt + 1), CultureInfo.InvariantCulture)

        let pointAt = mantissa.IndexOf '.'
        let whole = if pointAt < 0 then mantissa.Length else pointAt
        let digits = mantissa.Replace(".", "")
        let significant = digits.TrimStart '0'
        let sign = if negative then "-" else ""

        if significant = "" then
            sign + "0e0"
        else
            let leading = digits.Length - significant.Length
            let exponent = power + whole - leading - 1
            let kept = significant.TrimEnd '0'

            let body =
                if kept.Length = 1 then
                    kept
                else
                    kept.Substring(0, 1) + "." + kept.Substring 1

            sign + body + "e" + exponent.ToString(CultureInfo.InvariantCulture)

    let private indent (builder: StringBuilder) (level: int) =
        builder.Append(' ', 2 * level)
        |> ignore

    let rec private append (builder: StringBuilder) (level: int) (value: IonText) =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        match value with
        | Symbol text -> appendSymbol builder text
        | Text text -> appendQuoted builder '"' text
        | Integer number ->
            builder.Append(number.ToString(CultureInfo.InvariantCulture))
            |> ignore
        | DecimalText text ->
            builder.Append text
            |> ignore
        | Float number ->
            builder.Append(floatText number)
            |> ignore
        | Bool flag ->
            builder.Append(if flag then "true" else "false")
            |> ignore
        | Null ->
            builder.Append "null"
            |> ignore
        | Annotated(names, inner) ->
            for name in names do
                appendSymbol builder name

                builder.Append "::"
                |> ignore

            append builder level inner
        | Struct members ->
            builder.Append "{\n"
            |> ignore

            for name, item in members do
                indent builder (level + 1)
                appendSymbol builder name

                builder.Append ": "
                |> ignore

                append builder (level + 1) item

                builder.Append ",\n"
                |> ignore

            indent builder level

            builder.Append '}'
            |> ignore
        | List items ->
            builder.Append "[\n"
            |> ignore

            for item in items do
                indent builder (level + 1)
                append builder (level + 1) item

                builder.Append ",\n"
                |> ignore

            indent builder level

            builder.Append ']'
            |> ignore
        | Sexp items ->
            builder.Append "(\n"
            |> ignore

            for item in items do
                indent builder (level + 1)
                append builder (level + 1) item

                builder.Append '\n'
                |> ignore

            indent builder level

            builder.Append ')'
            |> ignore

    /// The text of top-level values: each value starts a line and ends with one line feed.
    let render (values: IonText list) : string =
        let builder = StringBuilder()

        for value in values do
            append builder 0 value

            builder.Append '\n'
            |> ignore

        builder.ToString()

    /// The UTF-8 bytes of top-level values, with no byte-order mark.
    let encode (values: IonText list) : byte array =
        UTF8Encoding(false).GetBytes(render values)
