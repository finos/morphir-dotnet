module Morphir.IR.Tests.TypeTests

open Morphir.SDK.Testing
open Morphir.IR
open Morphir.IR.SDK

[<Tests>]
let tests =
    let toStringTests =
        describe "ToString" [
            describe "When Type is a Reference:" [
                for (index, input, expected) in
                    [
                        Basics.intType (), "Morphir.SDK.Basics.Int"
                        Basics.floatType (), "Morphir.SDK.Basics.Float"
                        Dict.dictType () (Char.charType ()) (String.stringType ()),
                        "Morphir.SDK.Dict.Dict Morphir.SDK.Char.Char Morphir.SDK.String.String"
                    ]
                    |> List.mapi (fun index (input, expected) -> (index, input, expected)) do
                    test $"Testcase %i{index} should pass [expected = {expected}]" {
                        Type.toString input
                        |> Expect.equal expected
                    }
            ]
            describe "When Type is a Variable" [
                for (input, expected) in [ "a", "a"; "Result", "result"; "FizzBuzz", "fizzBuzz" ] do
                    test $"Given a variable of {input} then it should return {expected}" {
                        let type_ =
                            Name.fromString input
                            |> Type.variable ()

                        Type.toString type_
                        |> Expect.equal expected
                    }

            ]
            describe "When Type is a Unit:" [
                test "it should return the proper string" {
                    let type_ = Type.unit ()

                    Type.toString type_
                    |> Expect.equal "()"
                }
            ]
        ]

    describe "TypeTests" [
        toStringTests
    ]
