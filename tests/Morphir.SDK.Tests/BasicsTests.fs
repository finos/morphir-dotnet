module Morphir.SDK.Tests.BasicsTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Basics" [
        testList "Order" [
            test "compare returns LT for less than" {
                Basics.compare 1 2
                |> Expect.equal "Should be LT" LT
            }

            test "compare returns EQ for equal" {
                Basics.compare 5 5
                |> Expect.equal "Should be EQ" EQ
            }

            test "compare returns GT for greater than" {
                Basics.compare 10 3
                |> Expect.equal "Should be GT" GT
            }
        ]

        testList "Arithmetic" [
            test "add works correctly" {
                Basics.add 3 4
                |> Expect.equal "Should be 7" 7
            }

            test "subtract works correctly" {
                Basics.subtract 10 3
                |> Expect.equal "Should be 7" 7
            }

            test "multiply works correctly" {
                Basics.multiply 4 5
                |> Expect.equal "Should be 20" 20
            }

            test "divide works correctly" {
                Basics.divide 20 4
                |> Expect.equal "Should be 5" 5
            }

            test "abs works for negative numbers" {
                Basics.abs -5
                |> Expect.equal "Should be 5" 5
            }

            test "abs works for positive numbers" {
                Basics.abs 5
                |> Expect.equal "Should be 5" 5
            }

            test "negate works correctly" {
                Basics.negate 5
                |> Expect.equal "Should be -5" -5
            }
        ]

        testList "Min/Max" [
            test "max returns larger value" {
                Basics.max 3 7
                |> Expect.equal "Should be 7" 7
            }

            test "min returns smaller value" {
                Basics.min 3 7
                |> Expect.equal "Should be 3" 3
            }

            test "clamp keeps value in range" {
                Basics.clamp 0 10 5
                |> Expect.equal "Should be 5" 5
            }

            test "clamp restricts to low bound" {
                Basics.clamp 0 10 (-5)
                |> Expect.equal "Should be 0" 0
            }

            test "clamp restricts to high bound" {
                Basics.clamp 0 10 15
                |> Expect.equal "Should be 10" 10
            }
        ]

        testList "Modulo" [
            test "modBy handles positive numbers" {
                Basics.modBy 3 10
                |> Expect.equal "Should be 1" 1
            }

            test "modBy handles negative dividend correctly" {
                Basics.modBy 3 (-10)
                |> Expect.equal "Should be 2" 2
            }

            test "remainderBy works correctly" {
                Basics.remainderBy 3 10
                |> Expect.equal "Should be 1" 1
            }
        ]

        testList "Power and Sqrt" [
            test "pow works correctly" {
                Basics.pow 2.0 3
                |> Expect.equal "Should be 8.0" 8.0
            }

            test "sqrt works correctly" {
                Basics.sqrt 16.0
                |> Expect.equal "Should be 4.0" 4.0
            }
        ]
    ]
