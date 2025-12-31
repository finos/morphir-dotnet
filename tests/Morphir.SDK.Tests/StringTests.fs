module Morphir.SDK.Tests.StringTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "String" [
        test "isEmpty checks for empty string" {
            String.isEmpty ""
            |> Expect.isTrue "Should be true"
        }

        test "length returns string length" {
            String.length "hello"
            |> Expect.equal "Should be 5" 5
        }

        test "reverse reverses string" {
            String.reverse "hello"
            |> Expect.equal "Should be olleh" "olleh"
        }

        test "toUpper converts to uppercase" {
            String.toUpper "hello"
            |> Expect.equal "Should be HELLO" "HELLO"
        }

        test "toLower converts to lowercase" {
            String.toLower "HELLO"
            |> Expect.equal "Should be hello" "hello"
        }

        test "startsWith checks prefix" {
            String.startsWith "hel" "hello"
            |> Expect.isTrue "Should start with hel"
        }

        test "endsWith checks suffix" {
            String.endsWith "lo" "hello"
            |> Expect.isTrue "Should end with lo"
        }

        test "contains checks substring" {
            String.contains "ell" "hello"
            |> Expect.isTrue "Should contain ell"
        }

        test "split splits by separator" {
            String.split "," "a,b,c"
            |> Expect.equal "Should be [a; b; c]" ["a"; "b"; "c"]
        }

        test "join joins with separator" {
            String.join ", " ["a"; "b"; "c"]
            |> Expect.equal "Should be 'a, b, c'" "a, b, c"
        }

        test "append concatenates strings" {
            String.append "hello" " world"
            |> Expect.equal "Should be 'hello world'" "hello world"
        }
    ]
