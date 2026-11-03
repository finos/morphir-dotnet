namespace Morphir.IR.Pipeline.Plugins

open System
open System.Numerics

/// Target arithmetic operations whose constant result may be proved without an IR conversion.
[<RequireQualifiedAccess>]
type ArithmeticOperation =
    | Add
    | Subtract
    | Multiply
    | Divide

/// Pure proofs shared by classic and v4 adapters for the current F# numeric profile.
[<RequireQualifiedAccess>]
module ExactScalarFolding =
    let private inInt32Range value =
        value
        >= BigInteger Int32.MinValue
        && value
           <= BigInteger Int32.MaxValue

    /// An Int32 fold never accepts an out-of-profile operand or result.
    let tryInt32 operation left right =
        if
            not (
                inInt32Range left
                && inInt32Range right
            )
            || (operation = ArithmeticOperation.Divide
                && right = 0I)
        then
            None
        else
            let result =
                match operation with
                | ArithmeticOperation.Add ->
                    left
                    + right
                | ArithmeticOperation.Subtract ->
                    left
                    - right
                | ArithmeticOperation.Multiply ->
                    left
                    * right
                | ArithmeticOperation.Divide ->
                    left
                    / right

            if inInt32Range result then Some result else None

    /// A finite IEEE double is an exact binary rational.
    let private rational value =
        let bits = uint64 (BitConverter.DoubleToInt64Bits value)

        let exponent =
            int (
                (bits
                 >>> 52)
                &&& 0x7FFUL
            )

        let fraction =
            bits
            &&& 0x000FFFFFFFFFFFFFUL

        let mantissa =
            BigInteger(
                if exponent = 0 then
                    fraction
                else
                    fraction
                    ||| 0x0010000000000000UL
            )

        let mantissa =
            if
                bits
                >>> 63 = 0UL
            then
                mantissa
            else
                -mantissa

        let shift =
            if exponent = 0 then
                -1074
            else
                exponent
                - 1075

        if shift >= 0 then
            mantissa
            <<< shift,
            1I
        else
            mantissa,
            1I
            <<< -shift

    /// Finite results fold only when the mathematical result equals their exact binary rational.
    let tryFloat operation left right =
        if
            not (
                Double.IsFinite left
                && Double.IsFinite right
            )
            || (operation = ArithmeticOperation.Divide
                && right = 0.0)
        then
            None
        else
            let result =
                match operation with
                | ArithmeticOperation.Add ->
                    left
                    + right
                | ArithmeticOperation.Subtract ->
                    left
                    - right
                | ArithmeticOperation.Multiply ->
                    left
                    * right
                | ArithmeticOperation.Divide ->
                    left
                    / right

            if not (Double.IsFinite result) then
                None
            else
                let ln, ld = rational left
                let rn, rd = rational right

                let exactN, exactD =
                    match operation with
                    | ArithmeticOperation.Add ->
                        ln * rd
                        + rn * ld,
                        ld * rd
                    | ArithmeticOperation.Subtract ->
                        ln * rd
                        - rn * ld,
                        ld * rd
                    | ArithmeticOperation.Multiply -> ln * rn, ld * rd
                    | ArithmeticOperation.Divide -> ln * rd, ld * rn

                let actualN, actualD = rational result

                if
                    exactN
                    * actualD = actualN
                                * exactD
                then
                    Some result
                else
                    None
