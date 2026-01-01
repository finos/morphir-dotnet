module Morphir.SDK.Tests.MaybeTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Maybe" [
        testList "map" [
            test "transforms Some values" {
                Maybe.map ((*) 2) (Some 5)
                |> Expect.equal "Should be Some 10" (Some 10)
            }

            test "returns None for None" {
                Maybe.map ((*) 2) None
                |> Expect.equal "Should be None" None
            }
        ]

        testList "andThen" [
            test "chains Some values" {
                let half x = if x % 2 = 0 then Some (x / 2) else None
                
                Maybe.andThen half (Some 10)
                |> Expect.equal "Should be Some 5" (Some 5)
            }

            test "returns None if first is None" {
                let half x = if x % 2 = 0 then Some (x / 2) else None
                
                Maybe.andThen half None
                |> Expect.equal "Should be None" None
            }

            test "returns None if function returns None" {
                let half x = if x % 2 = 0 then Some (x / 2) else None
                
                Maybe.andThen half (Some 11)
                |> Expect.equal "Should be None" None
            }
        ]

        testList "withDefault" [
            test "returns value for Some" {
                Maybe.withDefault 0 (Some 42)
                |> Expect.equal "Should be 42" 42
            }

            test "returns default for None" {
                Maybe.withDefault 0 None
                |> Expect.equal "Should be 0" 0
            }
        ]

        testList "toResult" [
            test "converts Some to Ok" {
                Maybe.toResult "error" (Some 42)
                |> Expect.equal "Should be Ok 42" (Ok 42)
            }

            test "converts None to Error" {
                Maybe.toResult "error" None
                |> Expect.equal "Should be Error" (Error "error")
            }
        ]

        testList "fromResult" [
            test "converts Ok to Some" {
                Maybe.fromResult (Ok 42)
                |> Expect.equal "Should be Some 42" (Some 42)
            }

            test "converts Error to None" {
                Maybe.fromResult (Error "error")
                |> Expect.equal "Should be None" None
            }
        ]

        testList "map2" [
            test "combines two Some values" {
                Maybe.map2 (+) (Some 3) (Some 4)
                |> Expect.equal "Should be Some 7" (Some 7)
            }

            test "returns None if first is None" {
                Maybe.map2 (+) None (Some 4)
                |> Expect.equal "Should be None" None
            }

            test "returns None if second is None" {
                Maybe.map2 (+) (Some 3) None
                |> Expect.equal "Should be None" None
            }
        ]

        testList "map3" [
            test "combines three Some values" {
                Maybe.map3 (fun a b c -> a + b + c) (Some 1) (Some 2) (Some 3)
                |> Expect.equal "Should be Some 6" (Some 6)
            }

            test "returns None if any is None" {
                Maybe.map3 (fun a b c -> a + b + c) (Some 1) None (Some 3)
                |> Expect.equal "Should be None" None
            }
        ]
    ]
