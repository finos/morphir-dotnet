module Morphir.IR.Tests.Trees.TreeYamlTests

open System
open System.Text
open System.Text.Json
open Expecto
open Morphir.IR.Trees

let private location = "pkg/acme/orders/domain/module.yaml"

let private parse depth (text: string) =
    TreeYaml.read depth location (Encoding.UTF8.GetBytes text)

let private parsed text =
    match parse 32 text with
    | Ok root -> root
    | Error errors -> failtestf "Expected YAML to be admitted: %A" errors

[<Tests>]
let tests =
    testList "Strict YAML tree documents" [
        test "Preserves mapping order and exact numeric kinds" {
            let root =
                parsed
                    "formatVersion: 4.0\ncount: +4\namount: +.5e3\nwhole: 5.\nsigned: -0\nexact: 1.50E+39\n"

            Expect.sequenceEqual
                (root.EnumerateObject()
                 |> Seq.map (fun item -> item.Name, item.Value.GetRawText()))
                [
                    "formatVersion", "4.0"
                    "count", "4"
                    "amount", "0.5e3"
                    "whole", "5.0"
                    "signed", "-0"
                    "exact", "1.50E+39"
                ]
                "Keep the YAML scalar spelling once it is representable in JSON"
        }

        test "Resolves only plain scalars and never infers timestamps" {
            let root =
                parsed
                    "values: [true, False, null, ~, \"4.0\", 2026-01-15]\nnotes: |\n  two lines\n  of text\n"

            let items = root.GetProperty("values")
            Expect.equal items[0].ValueKind JsonValueKind.True "Plain boolean"
            Expect.equal items[1].ValueKind JsonValueKind.False "Plain boolean"
            Expect.equal items[2].ValueKind JsonValueKind.Null "Plain null"
            Expect.equal items[3].ValueKind JsonValueKind.Null "Plain tilde null"
            Expect.equal (items[4].GetString()) "4.0" "Quoted number stays a string"
            Expect.equal (items[5].GetString()) "2026-01-15" "A date-like scalar stays a string"

            Expect.equal
                (root.GetProperty("notes").GetString())
                "two lines\nof text\n"
                "Block string"
        }

        for name, text, code in
            [
                "duplicate", "values: {order: 1, order: 2}\n", "duplicate_member"
                "anchor", "one: &saved 1\n", "unsupported_yaml_feature"
                "alias", "one: *saved\n", "unsupported_yaml_feature"
                "tag", "one: !!int 1\n", "unsupported_yaml_feature"
                "merge", "nest: {<<: {one: 1}}\n", "unsupported_yaml_feature"
                "version directive", "%YAML 1.2\n---\none: 1\n", "unsupported_yaml_feature"
                "tag directive",
                "%TAG !app! tag:example.invalid,2026:\n---\none: 1\n",
                "unsupported_yaml_feature"
                "default tag directive",
                "%TAG !! tag:yaml.org,2002:\n---\none: 1\n",
                "unsupported_yaml_feature"
                "numeric key", "1: text\n", "invalid_type"
                "leading zero", "one: 01.5\n", "invalid_literal"
                "non-finite", "one: -.INF\n", "invalid_literal"
                "hexadecimal", "one: 0xF\n", "invalid_literal"
                "octal", "one: 0o17\n", "invalid_literal"
                "second document", "---\none: 1\n---\ntwo: 2\n", "invalid_yaml"
                "empty stream", "", "invalid_yaml"
                "broken flow", "one: [2,\n", "invalid_yaml"
            ] do
            test $"Refuses {name} at the selected document" {
                let errors =
                    parse 32 text
                    |> StoreTestKit.refusal

                Expect.equal
                    (StoreTestKit.codesAndFiles errors)
                    [ code, Some location ]
                    "The YAML profile diagnostic"
            }

        test "Tracks the parent and escaped key in a duplicate diagnostic" {
            let errors =
                parse 32 "'a/b': {'~order': 1, '~order': 2}\n"
                |> StoreTestKit.refusal

            let error = List.exactlyOne errors
            Expect.equal error.Code "duplicate_member" "The key is duplicated"

            Expect.stringContains
                error.Message
                "/a~1b/~0order"
                "The JSON pointer names the duplicate key"
        }

        test "Rejects depth before typed decoding" {
            let errors =
                parse 2 "one: {two: {three: {four: 0}}}\n"
                |> StoreTestKit.refusal

            Expect.equal
                (StoreTestKit.codesAndFiles errors)
                [ "depth_limit_exceeded", Some location ]
                "The YAML nesting cap"
        }

        test "Stops parsing when the YAML event budget is exhausted" {
            let bytes = Encoding.UTF8.GetBytes "values: [1, 2, 3, 4]\n"

            let errors =
                TreeYaml.readLimited 32 6 location bytes
                |> StoreTestKit.refusal

            Expect.equal
                (StoreTestKit.codesAndFiles errors)
                [ "yaml_event_limit", Some location ]
                "The YAML event cap"
        }
    ]
