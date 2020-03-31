module Morphir.SDK.StringTests

open Morphir.SDK.Testing
open Morphir.SDK
open Morphir.SDK.Maybe

[<Tests>]
let tests =
    let combiningTests =
        describe "Combining Strings" [
            testCase "cons" <| fun _ -> Expect.equal  "The truth is out there" (String.cons 'T' "he truth is out there")
            testCase "uncons non-empty" <| fun _ -> Expect.equal (Just ('a', "bc")) (String.uncons "abc")
            testCase "uncons empty" <| fun _ -> Expect.equal Nothing (String.uncons "")

            testCase "join spaces" <| fun _ -> Expect.equal "cat dog cow" (String.join " " [ "cat"; "dog"; "cow" ])
            testCase "join slashes" <| fun _  -> Expect.equal "home/steve/Desktop" (String.join "/" [ "home"; "steve"; "Desktop" ])
            testCase "join - make it Hawaiian" <| fun _ -> Expect.equal "Hawaiian" (String.join "a" ["H"; "w"; "ii"; "n"])
            testCase "join - animals" <| fun _ -> Expect.equal "cat dog cow" (String.join " " ["cat"; "dog"; "cow"])
            testCase "join - path" <| fun _ -> Expect.equal "home/evan/Desktop" (String.join "/" ["home"; "evan"; "Desktop"])

        ]

    describe "String" [ combiningTests ]
