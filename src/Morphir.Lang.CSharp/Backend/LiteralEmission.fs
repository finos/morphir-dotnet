// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.CSharp

open System
open System.Globalization
open System.Numerics
open System.Threading
open Morphir.IR

module internal LiteralEmission =
    type Literal =
        | Bool of bool
        | Int of BigInteger
        | Float of double
        | Decimal of decimal
        | String of string
        | Char of char

    let sdk modul local =
        FQName.fqName
            (PackageName.packageNameFromString "morphir.s_d_k")
            (ModulePath.modulePathFromString modul)
            (Name.fromString local)

    let shape =
        function
        | Bool _ -> TypeEmission.Named(sdk "basics" "bool", [])
        | Int _ -> TypeEmission.Named(sdk "basics" "int", [])
        | Float _ -> TypeEmission.Named(sdk "basics" "float", [])
        | Decimal _ -> TypeEmission.Named(sdk "decimal" "decimal", [])
        | String _ -> TypeEmission.Named(sdk "string" "string", [])
        | Char _ -> TypeEmission.Named(sdk "char" "char", [])

    let private loss location message =
        Admission.fail "representation-loss" location message

    let private validString (ct: CancellationToken) location (text: string) =
        let mutable index = 0

        while index < text.Length do
            ct.ThrowIfCancellationRequested()

            if Char.IsHighSurrogate text[index] then
                if
                    index + 1
                    >= text.Length
                    || not (Char.IsLowSurrogate text[index + 1])
                then
                    loss location "The string is not well-formed UTF-16."

                index <- index + 2
            elif Char.IsLowSurrogate text[index] then
                loss location "The string is not well-formed UTF-16."
            else
                index <- index + 1

        text

    let private integer location number =
        if
            number < BigInteger Int32.MinValue
            || number > BigInteger Int32.MaxValue
        then
            loss location "The integer is outside the checked Int32 profile."

        Int number

    let private character location value =
        if Char.IsSurrogate value then
            loss location "Char requires one non-surrogate BMP scalar."

        Char value

    let classic ct location =
        function
        | Morphir.IR.Classic.BoolLiteral value -> Bool value
        | Morphir.IR.Classic.WholeNumberLiteral value -> integer location (BigInteger value)
        | Morphir.IR.Classic.FloatLiteral value -> Float value
        | Morphir.IR.Classic.DecimalLiteral value -> Decimal value
        | Morphir.IR.Classic.StringLiteral value -> String(validString ct location value)
        | Morphir.IR.Classic.CharLiteral value -> character location value

    let v4 ct location =
        function
        | Morphir.IR.V4.BoolLiteral value -> Bool value
        | Morphir.IR.V4.IntegerLiteral value -> integer location value
        | Morphir.IR.V4.FloatLiteral value -> Float value.Value
        | Morphir.IR.V4.DecimalLiteral value ->
            let text = Morphir.IR.V4.DecimalLexeme.value value

            match
                System.Decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
            with
            | true, number when Morphir.IR.V4.DecimalLexeme.holdsExactly text number ->
                Decimal number
            | _ -> loss location "The decimal cannot be represented exactly by Decimal96."
        | Morphir.IR.V4.StringLiteral value -> String(validString ct location value)
        | Morphir.IR.V4.CharLiteral value ->
            if
                value.Length
                <> 1
            then
                loss location "Char requires one non-surrogate BMP scalar."

            character location value[0]
        | Morphir.IR.V4.DocumentLiteral _ ->
            Admission.fail
                "document-literal"
                location
                "Document literals have no admitted runtime representation."

    let text (ct: CancellationToken) literal =
        let escape (value: string) =
            value
            |> Seq.map (fun c ->
                ct.ThrowIfCancellationRequested()

                "\\u"
                + (int c).ToString("x4", CultureInfo.InvariantCulture)
            )
            |> String.concat ""

        match literal with
        | Bool true -> "true"
        | Bool false -> "false"
        | Int value -> value.ToString(CultureInfo.InvariantCulture)
        | Decimal value ->
            value.ToString(CultureInfo.InvariantCulture)
            + "m"
        | Float value when Double.IsNaN value -> "double.NaN"
        | Float value when Double.IsPositiveInfinity value -> "double.PositiveInfinity"
        | Float value when Double.IsNegativeInfinity value -> "double.NegativeInfinity"
        | Float value when
            value = 0.0
            && BitConverter.DoubleToInt64Bits(value) < 0L
            ->
            "-0.0d"
        | Float value ->
            value.ToString("R", CultureInfo.InvariantCulture)
            + "d"
        | String value ->
            "\""
            + escape value
            + "\""
        | Char value ->
            "'"
            + escape (string value)
            + "'"
