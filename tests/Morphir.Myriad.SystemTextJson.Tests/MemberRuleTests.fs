// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.MemberRuleTests

open System.Text.Json
open Expecto
open Morphir.Myriad.SystemTextJson.Tests.Members

/// Reads the JSON and returns the JsonException, or fails the test when reading succeeds.
let private refused (read: unit -> 'T) =
    try
        read () |> ignore
        failtest "the JSON was accepted"
        Unchecked.defaultof<JsonException>
    with :? JsonException as error ->
        error

let private contact = { FirstName = "Ada"; Nickname = None; Email = "ada@example.org"; Phone = ValueNone }

[<Tests>]
let tests =
    testList "record member rules" [
        test "the context naming policy and JsonName give the member names, and absent options are left out" {
            Expect.equal (JsonSerializer.Serialize(contact, MemberJson.Default.Contact)) """{"firstName":"Ada","e-mail":"ada@example.org"}""" "the JSON"
        }

        test "present options are written as their value" {
            let value = { contact with Nickname = Some "Countess"; Phone = ValueSome "123" }
            let json = JsonSerializer.Serialize(value, MemberJson.Default.Contact)
            Expect.equal json """{"firstName":"Ada","nickname":"Countess","e-mail":"ada@example.org","phone":"123"}""" "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, MemberJson.Default.Contact)) value "the value read back"
        }

        test "every JsonName is accepted when reading, and a missing option reads as None" {
            let value = JsonSerializer.Deserialize("""{"firstName":"Ada","email":"ada@example.org"}""", MemberJson.Default.Contact)
            Expect.equal value contact "the value"
        }

        test "an unknown member is ignored by default" {
            let value = JsonSerializer.Deserialize("""{"firstName":"Ada","e-mail":"ada@example.org","extra":{"a":[1,2]}}""", MemberJson.Default.Contact)
            Expect.equal value contact "the value"
        }

        test "a missing required member fails and names the member" {
            let error = refused (fun () -> JsonSerializer.Deserialize("""{"e-mail":"ada@example.org"}""", MemberJson.Default.Contact))
            Expect.stringContains error.Message "firstName" "the member"
            Expect.stringContains error.Message "Contact" "the type"
        }

        test "null for a member that is not an option fails with the member, the JSON path and the position" {
            let error = refused (fun () -> JsonSerializer.Deserialize("""{"firstName":null,"e-mail":"x"}""", MemberJson.Default.Contact))
            Expect.stringContains error.Message "firstName" "the member"
            Expect.equal error.Path "$" "the path of the value that the converter reads"
            Expect.equal (error.BytePositionInLine |> Option.ofNullable) (Some 17L) "the position of the null"
        }

        test "a member that appears twice fails and names the member" {
            let error = refused (fun () -> JsonSerializer.Deserialize("""{"firstName":"A","firstName":"B","e-mail":"x"}""", MemberJson.Default.Contact))
            Expect.stringContains error.Message "firstName" "the member"
        }

        test "two names of one member count as the same member" {
            let error = refused (fun () -> JsonSerializer.Deserialize("""{"firstName":"A","e-mail":"x","email":"y"}""", MemberJson.Default.Contact))
            Expect.stringContains error.Message "e-mail" "the member"
        }

        test "a JsonRecord attribute overrides the naming policy and writes absent options as null" {
            let json = JsonSerializer.Serialize({ FirstName = "Ada"; MiddleName = None }, MemberJson.Default.Strict)
            Expect.equal json """{"first_name":"Ada","middle_name":null}""" "the JSON"
        }

        test "a JsonRecord attribute can refuse unknown members" {
            let error = refused (fun () -> JsonSerializer.Deserialize("""{"first_name":"Ada","middle_name":null,"extra":1}""", MemberJson.Default.Strict))
            Expect.stringContains error.Message "extra" "the unknown member"
        }

        test "AllowNullFields accepts null where the field type accepts null" {
            match JsonSerializer.Deserialize("""{"text":null,"count":1}""", MemberJson.Default.Loose) with
            | null -> failtest "no value was read"
            | value ->
                Expect.isNull (box value.Text) "the text is null"
                Expect.equal value.Count 1 "the count"
        }

        test "AllowNullFields does not accept null for a value type" {
            let error = refused (fun () -> JsonSerializer.Deserialize("""{"text":"a","count":null}""", MemberJson.Default.Loose))
            Expect.stringContains error.Message "count" "the member"
        }
    ]
