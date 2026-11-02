namespace Morphir.CodeGeneration.FSharp

open System
open System.Globalization
open System.Numerics
open System.Text.Json

/// F# target spellings shared by both semantic-model adapters.
module internal TargetSyntax =
    let sdkConstructor =
        function
        | "maybe.just" -> Some("Some", 1)
        | "maybe.nothing" -> Some("None", 0)
        | "result.ok" -> Some("Ok", 1)
        | "result.err" -> Some("Error", 1)
        | "basics.l_t"
        | "basics.lt" -> Some("Morphir.SDK.LT", 0)
        | "basics.e_q"
        | "basics.eq" -> Some("Morphir.SDK.EQ", 0)
        | "basics.g_t"
        | "basics.gt" -> Some("Morphir.SDK.GT", 0)
        | _ -> None

    let sdkValue =
        function
        | "basics.add" -> Some "(+)"
        | "basics.subtract" -> Some "(-)"
        | "basics.multiply" -> Some "(*)"
        | "basics.divide" -> Some "(/)"
        | "basics.equal" -> Some "(=)"
        | "basics.notequal" -> Some "(<>)"
        | "basics.lessthan" -> Some "(<)"
        | "basics.greaterthan" -> Some "(>)"
        | "basics.lessthanorequal" -> Some "(<=)"
        | "basics.greaterthanorequal" -> Some "(>=)"
        | "basics.compare" -> Some "Morphir.SDK.Basics.compare"
        | "basics.identity" -> Some "id"
        | "list.map" -> Some "List.map"
        | "maybe.map" -> Some "Option.map"
        | "result.map" -> Some "Result.map"
        | _ -> None

    let stringLiteral (value: string) =
        "\""
        + JsonEncodedText.Encode(value).ToString()
        + "\""

    let charLiteral (value: char) = $"'\\u{int value:X4}'"

    let integer (value: BigInteger) =
        if
            value < BigInteger Int32.MinValue
            || value > BigInteger Int32.MaxValue
        then
            Decode.fail "literal-range" "$" "The mapped Morphir Int range is Int32."

        value.ToString(CultureInfo.InvariantCulture)

    let floatLiteral (value: float) =
        if not (Double.IsFinite value) then
            Decode.fail "literal-range" "$" "Non-finite float literal."

        let text = value.ToString("R", CultureInfo.InvariantCulture)

        if
            text.Contains('.')
            || text.Contains('E')
        then
            text
        else
            text
            + ".0"

    let decimalLiteral (value: decimal) =
        let text =
            value.ToString(CultureInfo.InvariantCulture)
            + "M"

        let bits = Decimal.GetBits value

        if
            value = 0M
            && bits.[3] < 0
        then
            "-" + text
        else
            text

    let sdkType name arguments =
        let application target =
            target
            + "<"
            + String.concat ", " arguments
            + ">"

        match name, arguments with
        | "basics.int", [] -> Some "int"
        | "basics.float", [] -> Some "float"
        | "basics.bool", []
        | "bool.bool", [] -> Some "bool"
        | "basics.order", [] -> Some "Morphir.SDK.Order"
        | "string.string", [] -> Some "string"
        | "char.char", [] -> Some "char"
        | "decimal.decimal", [] -> Some "decimal"
        | "list.list", [ _ ] -> Some(application "list")
        | "maybe.maybe", [ _ ] -> Some(application "option")
        | "result.result", [ error; success ] -> Some($"Result<{success}, {error}>")
        | _ -> None
