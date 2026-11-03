module Morphir.SDK.Tests.DecimalCultureTests

open System.Globalization
open Expecto
open Morphir.SDK

let private withCulture (name: string) action =
    let previous = CultureInfo.CurrentCulture

    try
        CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo name
        action ()
    finally
        CultureInfo.CurrentCulture <- previous

[<Tests>]
let tests =
    testSequenced
    <| testList "Decimal culture compatibility" [
        for culture in [ "de-DE"; "fr-FR" ] do
            testCase (
                culture
                + " parses invariant output without changing its value"
            )
            <| fun _ ->
                withCulture
                    culture
                    (fun () ->
                        for value in
                            [
                                1.5M
                                -1.5M
                                0.0000000000000000000000000001M
                                System.Decimal.MaxValue
                                System.Decimal.MinValue
                            ] do
                            Expect.equal
                                (Decimal.fromString (Decimal.toString value))
                                (Some value)
                                "Invariant SDK output roundtrips under the caller's culture"
                    )

            testCase (
                culture
                + " retains unambiguous localized input"
            )
            <| fun _ ->
                withCulture
                    culture
                    (fun () ->
                        Expect.equal
                            (Decimal.fromString "1,5")
                            (Some 1.5M)
                            "Localized comma decimals remain accepted"

                        Expect.equal
                            (Decimal.fromString " -1,5 ")
                            (Some -1.5M)
                            "Localized signs and whitespace remain accepted"
                    )

            testCase (
                culture
                + " gives canonical text priority and rejects invalid input"
            )
            <| fun _ ->
                withCulture
                    culture
                    (fun () ->
                        Expect.equal
                            (Decimal.fromString " +1.5 ")
                            (Some 1.5M)
                            "A canonical decimal point is never a grouping separator"

                        for text in [ "invalid"; ""; null ] do
                            Expect.equal
                                (Decimal.fromString text)
                                None
                                "Invalid input remains optional"
                    )
    ]
