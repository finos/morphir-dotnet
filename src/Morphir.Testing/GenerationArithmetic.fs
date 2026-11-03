namespace Morphir.Testing

/// Shared optimization expectations, independent of generated output from any other version.
module GenerationArithmetic =
    type Case = {
        Id: string
        Kind: string
        Consumer: string
        Expected: string
        Folds: bool
    }

    let cases = [
        {
            Id = "arithmetic.integer"
            Kind = "integer"
            Consumer = "printfn \"%d\" Car.Rental.Arithmetic.answer"
            Expected = "42"
            Folds = true
        }
        {
            Id = "arithmetic.exact-float"
            Kind = "float"
            Consumer =
                "printfn \"%016X\" (System.BitConverter.DoubleToInt64Bits Car.Rental.Arithmetic.answer)"
            Expected = "4010000000000000"
            Folds = true
        }
        {
            Id = "arithmetic.inexact-float"
            Kind = "inexact-float"
            Consumer =
                "printfn \"%016X\" (System.BitConverter.DoubleToInt64Bits Car.Rental.Arithmetic.answer)"
            Expected = "4340000000000000"
            Folds = false
        }
        {
            Id = "arithmetic.inexact-decimal"
            Kind = "inexact-decimal"
            Consumer =
                "let value = Car.Rental.Arithmetic.answer\nlet bits = System.Decimal.GetBits value\nprintfn \"%s|%d\" (value.ToString(System.Globalization.CultureInfo.InvariantCulture)) ((bits.[3] >>> 16) &&& 255)"
            Expected = "7922816251426433759354395033.5|1"
            Folds = false
        }
        {
            Id = "arithmetic.user-function"
            Kind = "user"
            Consumer = "printfn \"%d\" Car.Rental.Arithmetic.answer"
            Expected = "99"
            Folds = false
        }
    ]
