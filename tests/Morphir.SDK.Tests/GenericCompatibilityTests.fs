module Morphir.SDK.Tests.GenericCompatibilityTests

open System
open Expecto
open Morphir.SDK

type Offset =
    | Offset of int

    static member (+)(Offset value, delta: int) =
        Offset(
            value
            + delta
        )

[<Tests>]
let tests =
    testList "Generic arithmetic compatibility" [
        testCase "Int64 arithmetic outside the admitted Int32 profile retains its previous behavior"
        <| fun _ ->
            Expect.equal (Basics.add Int64.MaxValue 1L) Int64.MinValue "Int64 addition still wraps"

            Expect.equal
                (Basics.subtract Int64.MinValue 1L)
                Int64.MaxValue
                "Int64 subtraction still wraps"

            Expect.equal (Basics.multiply Int64.MaxValue 2L) -2L "Int64 multiplication still wraps"
            Expect.equal (Basics.negate Int64.MinValue) Int64.MinValue "Int64 negation still wraps"
        testCase "generic addition retains different operand and result types"
        <| fun _ -> Expect.equal (Basics.add (Offset 4) 3) (Offset 7) "custom SRTP addition"
        testCase "generic remainder retains non-Int32 operands"
        <| fun _ ->
            Expect.equal (Basics.remainderBy 3L 10L) 1L "Int64 remainder"
            Expect.equal (Basics.remainderBy 3.0 10.5) 1.5 "Float remainder"
    ]
