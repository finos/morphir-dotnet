namespace Morphir.Testing

open System.Numerics

/// One scalar inventory and independent consumer expectations for every representation adapter.
module GenerationScalars =
    type Scalar =
        | Integer of BigInteger
        | Decimal of string
        | Float of float
        | Character of string
        | Text of string
        | Boolean of bool

    type Case = {
        Id: string
        Input: Scalar
        Consumer: string
        Expected: string
        Refusal: string option
        ClassicUnavailable: string option
    }

    let private integer = "printfn \"%d\" Car.Rental.Literals.value"

    let private decimal =
        "let value = Car.Rental.Literals.value\nlet bits = System.Decimal.GetBits value\nprintfn \"%s|%d\" (value.ToString(System.Globalization.CultureInfo.InvariantCulture)) ((bits.[3] >>> 16) &&& 255)"

    let private float =
        "printfn \"%016X\" (System.BitConverter.DoubleToInt64Bits Car.Rental.Literals.value)"

    let private make id input consumer expected = {
        Id =
            "literal."
            + id
        Input = input
        Consumer = consumer
        Expected = expected
        Refusal = None
        ClassicUnavailable = None
    }

    let private refuse unavailable case =
        { case with
            Refusal = Some "literal-range"
            ClassicUnavailable = unavailable
        }

    let cases = [
        make "int-min" (Integer(BigInteger -2147483648L)) integer "-2147483648"
        make "int-max" (Integer(BigInteger 2147483647L)) integer "2147483647"
        make "int-overflow" (Integer(BigInteger 2147483648L)) integer ""
        |> refuse None
        make "int-underflow" (Integer(BigInteger -2147483649L)) integer ""
        |> refuse None
        make "big-integer" (Integer(BigInteger.Pow(2I, 80))) integer ""
        |> refuse (Some "Classic typed integers are Int64 and cannot hold this value")
        make "decimal-scale" (Decimal "10.50") decimal "10.50|2"
        make "decimal-exponent" (Decimal "1.250e1") decimal "12.50|2"
        make
            "decimal-max"
            (Decimal "79228162514264337593543950335")
            decimal
            "79228162514264337593543950335|0"
        make
            "decimal-scale-28"
            (Decimal "1.0000000000000000000000000000")
            decimal
            "1.0000000000000000000000000000|28"
        make "decimal-rounding" (Decimal "1.00000000000000000000000000001") decimal ""
        |> refuse (Some "Classic typed Decimal cannot retain this precision")
        make "decimal-scale-29" (Decimal "1.00000000000000000000000000000") decimal ""
        |> refuse (Some "Classic typed Decimal cannot retain scale 29")
        make "decimal-overflow" (Decimal "79228162514264337593543950336") decimal ""
        |> refuse (Some "Classic typed Decimal cannot retain this magnitude")
        make
            "decimal-negative-zero"
            (Decimal "-0.00")
            "let bits = System.Decimal.GetBits Car.Rental.Literals.value\nprintfn \"%08X\" bits.[3]"
            "80020000"
        make "float" (Float 1.25) float "3FF4000000000000"
        make "float-negative-zero" (Float -0.0) float "8000000000000000"
        make "float-smallest" (Float System.Double.Epsilon) float "0000000000000001"
        make "float-largest" (Float System.Double.MaxValue) float "7FEFFFFFFFFFFFFF"
        make "BMP-char" (Character "α") "printfn \"%d\" (int Car.Rental.Literals.value)" "945"
        make "supplementary-char" (Character "😀") "" ""
        |> refuse (Some "Classic Char holds one UTF-16 unit rather than this Unicode scalar")
        make
            "escaped-string"
            (Text "quote\"\\\n😀")
            "printfn \"%s\" (System.Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes Car.Rental.Literals.value))"
            "71756F7465225C0AF09F9880"
        make "boolean" (Boolean true) "printfn \"%b\" Car.Rental.Literals.value" "true"
    ]
