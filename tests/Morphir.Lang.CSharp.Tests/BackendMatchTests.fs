// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.BackendMatchTests

open Expecto
open Morphir.IR.V4
open Morphir.Lang.CSharp.Tests.BackendSupport

let private boolPattern (flag: bool) = LiteralPattern(va, BoolLiteral flag)
let private intPattern (value: int) = LiteralPattern(va, IntegerLiteral(System.Numerics.BigInteger value))
let private wild = WildcardPattern va
let private tuplePattern (items: Pattern list) = TuplePattern(va, items)
let private tupleOf (items: Value list) = Value.Tuple(va, items)
let private tupleT (items: Type list) = Type.Tuple(ta, items)

/// Calls a static method of a type of the assembly with the arguments, and gives the text of the result.
let private invoke (assembly: System.Reflection.Assembly) (typeName: string) (methodName: string) (arguments: obj list) : string =
    match assembly.GetType typeName with
    | null -> failwithf "no type %s" typeName
    | found ->
        match found.GetMethod methodName with
        | null -> failwithf "no method %s" methodName
        | method' -> string (method'.Invoke(null, Array.ofList (arguments |> List.map box)))

let private matches (values: (string * ValueDefinition) list) = library [ "matches", moduleOf [] values ]

[<Tests>]
let tests =
    testList "C# backend on Roslyn: match arms" [
        test "a match on True and False has no extra arm, and the program gives the model result" {
            let flag = definition [ "b", boolT ] intT (patternMatch (variable "b") [ boolPattern true, int 1; boolPattern false, int 0 ])
            let files = generate (matches [ "flag", flag ])
            Expect.isFalse (files["p_Acme.Geo/m_MatchesModule.g.cs"].Contains "Non-exhaustive") "no arm after true and false"
            Expect.equal (run files "Acme.Geo.Matches" "Flag" [ box true ]) "1" "true"
            Expect.equal (run files "Acme.Geo.Matches" "Flag" [ box false ]) "0" "false"
        }

        test "a repeated literal arm is not written, and the first arm wins" {
            let pick = definition [ "n", intT ] intT (patternMatch (variable "n") [ intPattern 1, int 10; intPattern 1, int 20; wild, int 0 ])
            let files = generate (matches [ "pick", pick ])
            Expect.equal (run files "Acme.Geo.Matches" "Pick" [ box 1 ]) "10" "the first arm"
            Expect.equal (run files "Acme.Geo.Matches" "Pick" [ box 2 ]) "0" "the wildcard"
        }

        test "a constructor arm that an earlier arm covers is not written" {
            let model =
                library
                    [ "shapes",
                      moduleOf
                          [ "shape", custom [] [ "circle", [ "radius", intT ]; "square", [ "edge", intT ] ] ]
                          [ "size",
                            definition
                                [ "s", own "shapes" "shape" [] ]
                                intT
                                (patternMatch
                                    (variable "s")
                                    [ constructorPattern "shapes#circle" [ asPattern "r" ], variable "r"
                                      constructorPattern "shapes#circle" [ wild ], int 0
                                      constructorPattern "shapes#square" [ asPattern "e" ], variable "e" ]) ] ]

            let files = generate model
            let assembly = load files
            let circle = make assembly "Acme.Geo.Shapes+Circle" [ box 3 ]
            Expect.equal (invoke assembly "Acme.Geo.Shapes" "Size" [ circle ]) "3" "the first circle arm"
        }

        test "a match on every pair of True and False has no extra arm" {
            let both =
                definition
                    [ "a", boolT; "b", boolT ]
                    intT
                    (patternMatch
                        (tupleOf [ variable "a"; variable "b" ])
                        [ tuplePattern [ boolPattern true; boolPattern true ], int 3
                          tuplePattern [ boolPattern true; boolPattern false ], int 2
                          tuplePattern [ boolPattern false; wild ], int 0 ])

            let files = generate (matches [ "both", both ])
            Expect.isFalse (files["p_Acme.Geo/m_MatchesModule.g.cs"].Contains "Non-exhaustive") "no arm after the four pairs"
            Expect.equal (run files "Acme.Geo.Matches" "Both" [ box true; box false ]) "2" "true and false"
            Expect.equal (run files "Acme.Geo.Matches" "Both" [ box false; box true ]) "0" "false and true"
        }

        test "a match that does not name every value still ends with the failure of the model" {
            let some = definition [ "a", boolT; "b", boolT ] intT (patternMatch (tupleOf [ variable "a"; variable "b" ]) [ tuplePattern [ boolPattern true; wild ], int 1 ])
            let files = generate (matches [ "some", some ])
            Expect.stringContains files["p_Acme.Geo/m_MatchesModule.g.cs"] "Non-exhaustive" "the last arm fails"
            Expect.equal (run files "Acme.Geo.Matches" "Some" [ box true; box false ]) "1" "the first arm"
        }

        test "a negative literal given to a wrapper case is in parentheses" {
            let model =
                library
                    [ "measures",
                      moduleOf
                          [ "meters", alias [] intT
                            "label", alias [] stringT
                            "measure", custom [] [ "meters", [ "meters", own "measures" "meters" [] ]; "label", [ "label", own "measures" "label" [] ] ] ]
                          [ "below",
                            definition
                                []
                                (own "measures" "measure" [])
                                (apply (constructor "measures#meters") (Value.Literal(va, IntegerLiteral(System.Numerics.BigInteger -1))))
                            "below-value",
                            definition
                                []
                                intT
                                (patternMatch
                                    (reference "measures#below")
                                    [ constructorPattern "measures#meters" [ asPattern "m" ], variable "m"
                                      constructorPattern "measures#label" [ wild ], int 0 ]) ] ]

            let files = generate model
            Expect.equal (run files "Acme.Geo.Measures" "BelowValue" []) "-1" "the wrapped value"
        }

        test "a negative literal is in parentheses before a member access" {
            let text = definition [] intT (call2 (sdkRef "basics#max") (Value.Literal(va, IntegerLiteral(System.Numerics.BigInteger -5))) (int 2))
            let files = generate (matches [ "larger", text ])
            Expect.equal (run files "Acme.Geo.Matches" "Larger" []) "2" "max of -5 and 2"
        }
    ]

let private points =
    library
        [ "points",
          moduleOf
              [ "point", alias [] (recordT [ "x", intT; "y", intT ])
                "swapped", alias [] (recordT [ "y", intT; "x", intT ]) ]
              [ "make", definition [] (own "points" "swapped" []) (call2 (constructor "points#swapped") (int 1) (int 2))
                "first-of",
                definition
                    [ "p", own "points" "swapped" [] ]
                    intT
                    (patternMatch (variable "p") [ constructorPattern "points#swapped" [ asPattern "a"; asPattern "b" ], variable "a" ]) ] ]

[<Tests>]
let recordOrder =
    testList "C# backend on Roslyn: record field order" [
        test "a record alias with another field order gives its values by name to the shared record" {
            let files = generate points
            // swapped y = 1, x = 2 is the record with x = 2 and y = 1.
            Expect.equal (run files "Acme.Geo.Points" "Make" []) "Point { X = 2, Y = 1 }" "the fields by name"
        }

        test "a positional pattern of a record alias with another field order binds its fields by name" {
            let files = generate points
            let assembly = load files
            let point = make assembly "Acme.Geo.Points+Point" [ box 2; box 1 ]
            // The first field of swapped is y.
            Expect.equal (invoke assembly "Acme.Geo.Points" "FirstOf" [ point ]) "1" "y"
        }
    ]
