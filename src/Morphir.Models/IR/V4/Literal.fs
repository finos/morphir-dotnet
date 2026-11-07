namespace Morphir.IR.V4

open System
open System.Globalization
open System.Numerics

/// <summary>
/// A finite float and the lexeme it was read from, if any. Equality uses the value alone, so <c>4.0</c>
/// and <c>4e+0</c> are equal and each keeps its lexeme.
/// </summary>
[<CustomEquality; NoComparison>]
type FloatValue =
    private
        {
            value: float
            lexeme: string option
        }

    /// <summary>The finite value.</summary>
    member this.Value = this.value

    /// <summary>The lexeme, if one was retained.</summary>
    member this.Lexeme = this.lexeme

    override this.Equals(other: obj) =
        match other with
        | :? FloatValue as that -> this.value = that.value
        | _ -> false

    override this.GetHashCode() = this.value.GetHashCode()

/// <summary>Functions for <see cref="FloatValue"/>.</summary>
[<RequireQualifiedAccess>]
module FloatValue =
    /// <summary>Builds a float literal's value, refusing NaN and the infinities.</summary>
    let create (value: float) (lexeme: string option) : Result<FloatValue, string> =
        if Double.IsFinite value then
            Ok { value = value; lexeme = lexeme }
        else
            Error "A float literal must be finite."

/// <summary>
/// A decimal literal's lexeme, kept exactly as written. Two decimals are the same literal only when their
/// lexemes are the same, so <c>10.50</c> and <c>10.5</c> differ.
/// </summary>
type DecimalLexeme = private DecimalLexeme of string

/// <summary>Functions for <see cref="DecimalLexeme"/>.</summary>
[<RequireQualifiedAccess>]
module DecimalLexeme =
    let private isDigit (c: char) =
        c >= '0'
        && c <= '9'

    /// Matches sign? ( digits ( "." digits? )? | "." digits ) exponent?
    let private isLexeme (text: string) =
        let digitsFrom start =
            let mutable i = start

            while i < text.Length
                  && isDigit text[i] do
                i <- i + 1

            i

        let isSign (c: char) =
            c = '+'
            || c = '-'

        let start =
            if
                text.Length > 0
                && isSign text[0]
            then
                1
            else
                0

        let wholeEnd = digitsFrom start

        let hasPoint =
            wholeEnd < text.Length
            && text[wholeEnd] = '.'

        let fractionEnd =
            if hasPoint then
                digitsFrom (
                    wholeEnd
                    + 1
                )
            else
                wholeEnd

        let hasDigits =
            wholeEnd > start
            || fractionEnd > wholeEnd
                             + 1

        if not hasDigits then
            false
        elif fractionEnd = text.Length then
            true
        elif
            text[fractionEnd]
            <> 'e'
            && text[fractionEnd]
               <> 'E'
        then
            false
        else
            let afterE =
                fractionEnd
                + 1

            let exponentStart =
                if
                    afterE < text.Length
                    && isSign text[afterE]
                then
                    afterE + 1
                else
                    afterE

            let exponentEnd = digitsFrom exponentStart

            exponentEnd > exponentStart
            && exponentEnd = text.Length

    /// <summary>Builds a lexeme, refusing text outside the v4 decimal grammar.</summary>
    let create (lexeme: string | null) : Result<DecimalLexeme, string> =
        match lexeme with
        | null -> Error "'' is not a decimal lexeme."
        | lexeme when not (isLexeme lexeme) -> Error $"'{lexeme}' is not a decimal lexeme."
        | lexeme -> Ok(DecimalLexeme lexeme)

    /// <summary>The lexeme as written.</summary>
    let value (DecimalLexeme lexeme) : string = lexeme

    /// A decimal's digits without leading or trailing zeros, and the power of ten they are multiplied by.
    let private normalized (digits: string) (exponent: BigInteger) =
        let trimmed = digits.TrimStart('0')

        if trimmed = "" then
            "", BigInteger.Zero
        else
            let significant = trimmed.TrimEnd('0')

            significant,
            exponent
            + BigInteger(
                trimmed.Length
                - significant.Length
            )

    /// <summary>
    /// Whether a <see cref="Decimal"/> holds exactly the number a lexeme spells. Parsing rounds a lexeme of
    /// more than 28 digits silently, so a reader that must not round checks the result here. The lexeme must
    /// satisfy the decimal grammar.
    /// </summary>
    let holdsExactly (lexeme: string) (number: decimal) : bool =
        let unsigned = lexeme.TrimStart('+', '-')
        let exponentAt = unsigned.IndexOfAny [| 'e'; 'E' |]

        let mantissa, exponent =
            if exponentAt < 0 then
                unsigned, BigInteger.Zero
            else
                unsigned.Substring(0, exponentAt),
                BigInteger.Parse(
                    unsigned.Substring(
                        exponentAt
                        + 1
                    ),
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture
                )

        let pointAt = mantissa.IndexOf '.'

        let digits, fractionLength =
            if pointAt < 0 then
                mantissa, 0
            else
                mantissa.Remove(pointAt, 1),
                mantissa.Length
                - pointAt
                - 1

        let held =
            let bits = Decimal.GetBits number

            let scale =
                (bits[3]
                 >>> 16)
                &&& 0xFF

            let coefficient =
                BigInteger(uint32 bits[0])
                + (BigInteger(uint32 bits[1])
                   <<< 32)
                + (BigInteger(uint32 bits[2])
                   <<< 64)

            normalized (coefficient.ToString(CultureInfo.InvariantCulture)) (BigInteger -scale)

        normalized
            digits
            (exponent
             - BigInteger fractionLength) = held

/// <summary>A literal constant. A <see cref="DocumentLiteral"/> cannot appear in a literal pattern.</summary>
type Literal =
    /// <summary>A boolean.</summary>
    | BoolLiteral of bool
    /// <summary>One Unicode scalar, which may take two UTF-16 units.</summary>
    | CharLiteral of string
    /// <summary>A string.</summary>
    | StringLiteral of string
    /// <summary>A whole number of any size.</summary>
    | IntegerLiteral of BigInteger
    /// <summary>A finite float.</summary>
    | FloatLiteral of FloatValue
    /// <summary>A decimal, as its lexeme.</summary>
    | DecimalLiteral of DecimalLexeme
    /// <summary>A schema-less document.</summary>
    | DocumentLiteral of DocumentValue
