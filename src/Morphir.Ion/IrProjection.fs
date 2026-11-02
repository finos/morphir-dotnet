namespace Morphir.Ion

open System
open System.Buffers
open System.Collections.Generic
open System.Globalization
open System.Numerics
open System.Runtime.CompilerServices
open System.Text
open System.Text.Json

/// Projects an Ion IR value to the JSON the IR readers take. Ion data that JSON has no form for is refused, not changed.
module internal IrProjection =
    /// The first thing in the value that JSON cannot carry.
    exception private Refused of IonIrError

    let private refuse path message =
        raise (
            Refused
                {
                    Code = "ir_not_json_compatible"
                    Message = $"The ir value at {path} cannot be written as JSON: {message}."
                    Path = Some path
                    Location = None
                }
        )

    /// The most zeros a small decimal is padded with before it is written with an exponent.
    let private maxPadding = 64L

    /// The JSON text of a decimal, which is the Ion text with <c>d</c> written as <c>e</c>.
    /// Scale zero, whose Ion text ends in a point, is written with the exponent <c>e0</c>.
    let private decimalText (value: IonDecimal) =
        let digits =
            BigInteger.Abs(value.Coefficient).ToString(CultureInfo.InvariantCulture)

        let sign =
            if
                value.Coefficient.Sign < 0
                || (value.Coefficient.IsZero
                    && value.IsNegativeZero)
            then
                "-"
            else
                ""

        let scale = int64 value.Scale

        let padding =
            scale
            - int64 digits.Length

        if
            scale > 0L
            && padding < 0L
        then
            let split =
                digits.Length
                - value.Scale

            sign
            + digits.Substring(0, split)
            + "."
            + digits.Substring split
        elif
            scale > 0L
            && padding
               <= maxPadding
        then
            sign
            + "0."
            + String('0', int padding)
            + digits
        else
            // The exponent form allocates nothing in proportion to the scale. Scale zero is {digits}e0.
            sign
            + digits
            + "e"
            + (-scale).ToString(CultureInfo.InvariantCulture)

    let rec private write (writer: Utf8JsonWriter) (path: string) (value: IonValue) =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        if not value.Annotations.IsEmpty then
            refuse path "annotations have no JSON form"

        match value.Data with
        | IonData.Null IonNullType.Null -> writer.WriteNullValue()
        | IonData.Null _ -> refuse path "a typed null has no JSON form"
        | IonData.Bool flag -> writer.WriteBooleanValue flag
        | IonData.Int number ->
            writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture), false)
        | IonData.Decimal number -> writer.WriteRawValue(decimalText number, false)
        | IonData.String text -> writer.WriteStringValue text
        | IonData.Float _ -> refuse path "a float has no exact JSON number"
        | IonData.Timestamp _ -> refuse path "a timestamp has no JSON form"
        | IonData.Symbol _ -> refuse path "a symbol has no JSON form"
        | IonData.Clob _ -> refuse path "a clob has no JSON form"
        | IonData.Blob _ -> refuse path "a blob has no JSON form"
        | IonData.Sexp _ -> refuse path "an S-expression has no JSON form"
        | IonData.List items ->
            writer.WriteStartArray()

            items
            |> List.iteri (fun index item ->
                write
                    writer
                    (path
                     + "["
                     + index.ToString(CultureInfo.InvariantCulture)
                     + "]")
                    item
            )

            writer.WriteEndArray()
        | IonData.Struct members ->
            let seen = HashSet<string>(StringComparer.Ordinal)
            writer.WriteStartObject()

            for name, item in members do
                match name with
                | IonSymbol.Zero -> refuse path "symbol zero has no JSON form as a field name"
                | IonSymbol.Text name ->
                    let child = $"{path}.{name}"

                    if not (seen.Add name) then
                        refuse child "the field name appears more than once"

                    writer.WritePropertyName name
                    write writer child item

            writer.WriteEndObject()

    let private failure code message =
        Result.Error
            {
                Code = code
                Message = message
                Path = Some "$"
                Location = None
            }

    /// The UTF-8 JSON bytes for an Ion value. Paths in errors start at <c>$</c>, the value itself.
    let toJsonBytes (value: IonValue) : Result<byte array, IonIrError> =
        try
            let buffer = ArrayBufferWriter<byte>()
            let options = JsonWriterOptions(MaxDepth = Int32.MaxValue)

            do
                use writer = new Utf8JsonWriter(buffer, options)
                write writer "$" value
                writer.Flush()

            Result.Ok(buffer.WrittenSpan.ToArray())
        with
        | Refused error -> Result.Error error
        | :? InsufficientExecutionStackException ->
            failure "depth_limit_exceeded" "The ir value nests too deeply to write as JSON."
        | :? ArgumentException as error ->
            failure
                "ir_not_json_compatible"
                $"The ir value cannot be written as JSON: {error.Message}"
        | :? InvalidOperationException as error ->
            failure
                "ir_not_json_compatible"
                $"The ir value cannot be written as JSON: {error.Message}"

    /// The JSON text for an Ion value.
    let toJson (value: IonValue) : Result<string, IonIrError> =
        toJsonBytes value
        |> Result.map Encoding.UTF8.GetString
