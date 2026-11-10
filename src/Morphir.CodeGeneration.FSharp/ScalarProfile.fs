namespace Morphir.CodeGeneration.FSharp

open System
open System.Globalization
open System.Numerics
open Morphir.IR.V4

/// Exact lowering to the existing F# Int32/double/decimal/BMP-char ABI.
module internal ScalarProfile =
    let private decimalValue (lexeme: string) =
        let mutable value = 0M

        if
            not (Decimal.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, &value))
            || not (DecimalLexeme.holdsExactly lexeme value)
        then
            Decode.fail
                "literal-range"
                "$"
                "The decimal cannot be held exactly by the F# decimal representation."

        let exponentAt = lexeme.IndexOfAny [| 'e'; 'E' |]

        let mantissa, exponent =
            if exponentAt < 0 then
                lexeme, BigInteger.Zero
            else
                lexeme.Substring(0, exponentAt),
                BigInteger.Parse(
                    lexeme.Substring(
                        exponentAt
                        + 1
                    ),
                    CultureInfo.InvariantCulture
                )

        let point = mantissa.IndexOf '.'

        let fraction =
            if point < 0 then
                0
            else
                mantissa.Length
                - point
                - 1

        let scale =
            max
                BigInteger.Zero
                (BigInteger fraction
                 - exponent)

        let bits = Decimal.GetBits value

        let heldScale =
            (bits.[3]
             >>> 16)
            &&& 255

        if
            scale > 28I
            || BigInteger heldScale
               <> scale
        then
            Decode.fail
                "literal-range"
                "$"
                "The decimal scale cannot be retained by the F# decimal representation."

        let text = TargetSyntax.decimalLiteral value

        if
            value = 0M
            && lexeme.StartsWith("-", StringComparison.Ordinal)
            && not (text.StartsWith("-", StringComparison.Ordinal))
        then
            "-" + text
        else
            text

    let render location literal =
        try
            match literal with
            | BoolLiteral value -> if value then "true" else "false"
            | StringLiteral value -> TargetSyntax.stringLiteral value
            | CharLiteral value ->
                if
                    String.IsNullOrEmpty value
                    || value.Length
                       <> 1
                    || Char.IsSurrogate value[0]
                then
                    Decode.fail
                        "literal-range"
                        location
                        "The F# Char representation requires one BMP Unicode scalar."

                TargetSyntax.charLiteral value[0]
            | IntegerLiteral value -> TargetSyntax.integer value
            | FloatLiteral value -> TargetSyntax.floatLiteral value.Value
            | DecimalLiteral value -> decimalValue (DecimalLexeme.value value)
            | DocumentLiteral _ ->
                Decode.fail
                    "unsupported-document-literal"
                    location
                    "Schema-less document literals have no F# representation in this profile."
        with GenerationFailure(code, _, message) ->
            Decode.fail code location message
