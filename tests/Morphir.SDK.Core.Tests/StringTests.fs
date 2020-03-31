module Morphir.SDK.StringTests

open Morphir.SDK.Testing
open Morphir.SDK
open Morphir.SDK.Maybe

[<Tests>]
let tests =
    let combiningTests =
        describe "Combining Strings" [
            testCase "uncons non-empty" <| fun _ -> Expect.equal (Just ('a', "bc")) (String.uncons "abc")
            testCase "uncons empty" <| fun _ -> Expect.equal Nothing (String.uncons "")
        ]

    describe "String" [ combiningTests ]
