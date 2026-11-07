// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.UnionTests

open System.Text.Json
open System.Text.Json.Serialization
open Expecto
open Morphir.Myriad.SystemTextJson.Tests.Compatibility
open Morphir.Myriad.SystemTextJson.Tests.Unions

let private refused (read: unit -> 'T) (fragment: string) =
    try
        read () |> ignore
        failtestf "the JSON was accepted (%s)" fragment
        Unchecked.defaultof<JsonException>
    with :? JsonException as error ->
        error

let private shapes =
    [
        NoArgs, """{"Case":"NoArgs"}"""
        OneArg 1, """{"Case":"OneArg","Fields":[1]}"""
        Unnamed 2, """{"Case":"Unnamed","Fields":[2]}"""
        TwoArgs(3, "t"), """{"Case":"TwoArgs","Fields":[3,"t"]}"""
        WithRecord { X = 4; Y = 5 }, """{"Case":"WithRecord","Fields":[{"X":4,"Y":5}]}"""
    ]

[<Tests>]
let tests =
    testList "unions" [
        test "the Default preset writes each case as FSharp.SystemTextJson does, and reads it back" {
            for value, expected in shapes do
                let json = JsonSerializer.Serialize(value, UnionJson.Default.Shape)
                Expect.equal json expected "the JSON"
                expectSameAsReference (JsonFSharpOptions.Default()) json value
                Expect.equal (JsonSerializer.Deserialize(json, UnionJson.Default.Shape)) value "the value read back"
        }

        test "a fieldless union, a single-case union and an option inside a record" {
            let value = { Value = None; Choice = B; Wrapped = Wrapped "s" }
            let json = JsonSerializer.Serialize(value, UnionJson.Default.Holder)
            Expect.equal json """{"Value":null,"Choice":{"Case":"B"},"Wrapped":"s"}""" "the JSON"
            expectSameAsReference (JsonFSharpOptions.Default()) json value
            Expect.equal (JsonSerializer.Deserialize(json, UnionJson.Default.Holder)) value "the value read back"
        }

        test "AllowUnorderedTag accepts the tag after the fields" {
            Expect.equal (JsonSerializer.Deserialize("""{"Fields":[1],"Case":"OneArg"}""", UnionJson.Default.Shape)) (OneArg 1) "the value"
        }

        test "without AllowUnorderedTag the tag must come first" {
            let error = refused (fun () -> JsonSerializer.Deserialize("""{"Fields":[1],"Case":"Ordered"}""", UnionJson.Default.Ordered)) "unordered"
            Expect.stringContains error.Message "must come first" "the reason"
        }

        test "an unknown tag fails and names the union" {
            let error = refused (fun () -> JsonSerializer.Deserialize("""{"Case":"Nope"}""", UnionJson.Default.Shape)) "Nope"
            Expect.stringContains error.Message "Shape" "the union"
            Expect.stringContains error.Message "Nope" "the tag"
        }

        test "fieldless cases with JsonName are strings, read with no regard to letter case" {
            Expect.equal (JsonSerializer.Serialize(Validator, UnionJson.Default.ExtensionType)) "\"validator\"" "the JSON"
            Expect.equal (JsonSerializer.Deserialize("\"VALIDATOR\"", UnionJson.Default.ExtensionType)) Validator "case-insensitive"
            let error = refused (fun () -> JsonSerializer.Deserialize("\"workspace\"", UnionJson.Default.ExtensionType)) "workspace"
            Expect.stringContains error.Message "ExtensionType" "the union"
        }

        test "InternalTag with NamedFields uses the kind member and camel-case tags" {
            let value = TypeMismatch("Int", "String")
            let json = JsonSerializer.Serialize(value, UnionJson.Default.Problem)
            Expect.equal json """{"kind":"typeMismatch","expected":"Int","found":"String"}""" "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, UnionJson.Default.Problem)) value "the value read back"
            Expect.equal (JsonSerializer.Deserialize("""{"kind":"missing","name":"x"}""", UnionJson.Default.Problem)) (Missing "x") "the other case"
            // The attribute sets the whole encoding, without AllowUnorderedTag.
            refused (fun () -> JsonSerializer.Deserialize("""{"name":"x","kind":"missing"}""", UnionJson.Default.Problem)) "tag after a field" |> ignore
        }

        test "SelectByTokenKind reads a number or a string request id, and keeps the token" {
            for value, json in [ RequestId.Number 18446744073709551615UL, "18446744073709551615"; RequestId.Text "req-1", "\"req-1\"" ] do
                Expect.equal (JsonSerializer.Serialize(value, UnionJson.Default.RequestId)) json "the JSON"
                Expect.equal (JsonSerializer.Deserialize(json, UnionJson.Default.RequestId)) value "the value read back"

            refused (fun () -> JsonSerializer.Deserialize("1.5", UnionJson.Default.RequestId)) "1.5" |> ignore
            refused (fun () -> JsonSerializer.Deserialize("true", UnionJson.Default.RequestId)) "true" |> ignore
        }

        test "SelectByTokenKind reads an untyped JSON value of every kind" {
            let json = """{"a":["x",1.5,true,{"b":[]}]}"""
            let value = Members(Map [ "a", Items [ Text "x"; Amount 1.5; Flag true; Members(Map [ "b", Items [] ]) ] ])
            Expect.equal (JsonSerializer.Deserialize(json, UnionJson.Default.JsonValue)) value "the value"
            Expect.equal (JsonSerializer.Serialize(value, UnionJson.Default.JsonValue)) json "the JSON"
        }

        test "ThothLike writes the classic IR array shape" {
            for value, expected in [ BoolLiteral true, """["BoolLiteral",true]"""; StringLiteral "s", """["StringLiteral","s"]"""; UnitLiteral, "\"UnitLiteral\"" ] do
                let json = JsonSerializer.Serialize(value, UnionJson.Default.Literal)
                Expect.equal json expected "the JSON"
                expectSameAsReference (JsonFSharpOptions.ThothLike()) json value
                Expect.equal (JsonSerializer.Deserialize(json, UnionJson.Default.Literal)) value "the value read back"
        }

        test "a recursive union round-trips" {
            let value = Node [ Leaf; Node [ Leaf ] ]
            let json = JsonSerializer.Serialize(value, UnionJson.Default.Tree)
            Expect.equal json """{"Case":"Node","Fields":[[{"Case":"Leaf"},{"Case":"Node","Fields":[[{"Case":"Leaf"}]]}]]}""" "the JSON"
            expectSameAsReference (JsonFSharpOptions.Default()) json value
            Expect.equal (JsonSerializer.Deserialize(json, UnionJson.Default.Tree)) value "the value read back"
        }

        test "a case with the wrong number of fields fails" {
            refused (fun () -> JsonSerializer.Deserialize("""{"Case":"TwoArgs","Fields":[3]}""", UnionJson.Default.Shape)) "one field" |> ignore
        }
    ]
