module Morphir.SDK.Tests.ResultTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Result" [
        test "map transforms Ok values" {
            Result.map ((*) 2) (Ok 5)
            |> Expect.equal "Should be Ok 10" (Ok 10)
        }

        test "map leaves Error unchanged" {
            Result.map ((*) 2) (Error "fail")
            |> Expect.equal "Should be Error" (Error "fail")
        }

        test "mapError transforms Error values" {
            Result.mapError String.toUpper (Error "fail")
            |> Expect.equal "Should be Error FAIL" (Error "FAIL")
        }

        test "andThen chains Ok values" {
            let half x = if x % 2 = 0 then Ok (x / 2) else Error "odd"
            
            Result.andThen half (Ok 10)
            |> Expect.equal "Should be Ok 5" (Ok 5)
        }

        test "withDefault returns value for Ok" {
            Result.withDefault 0 (Ok 42)
            |> Expect.equal "Should be 42" 42
        }

        test "withDefault returns default for Error" {
            Result.withDefault 0 (Error "fail")
            |> Expect.equal "Should be 0" 0
        }

        test "map2 combines two Ok values" {
            Result.map2 (+) (Ok 3) (Ok 4)
            |> Expect.equal "Should be Ok 7" (Ok 7)
        }

        test "map2 returns first Error" {
            Result.map2 (+) (Error "e1") (Ok 4)
            |> Expect.equal "Should be Error e1" (Error "e1")
        }
    ]
