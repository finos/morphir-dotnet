// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.RecordTests

open System
open System.Text.Json
open System.Text.Json.Serialization.Metadata
open Expecto
open Morphir.Myriad.SystemTextJson.Tests.Addresses
open Morphir.Myriad.SystemTextJson.Tests.Records

let private ada = { Name = "Ada"; Age = 36; Active = true }

let private combined () =
    JsonSerializerOptions(TypeInfoResolver = JsonTypeInfoResolver.Combine(RecordJson.Default, AddressJson.Default))

let private typeInfo<'T> (options: JsonSerializerOptions) = options.GetTypeInfo typeof<'T> :?> JsonTypeInfo<'T>

[<Tests>]
let tests =
    testList "records" [
        test "a record of primitives round-trips through its type info" {
            let json = JsonSerializer.Serialize(ada, RecordJson.Default.Person)
            Expect.equal json """{"Name":"Ada","Age":36,"Active":true}""" "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, RecordJson.Default.Person)) ada "the value read back"
        }

        test "a record field of a type in the same context uses its converter" {
            let options = combined ()
            let value = { Head = ada; Home = { Street = "Main"; Number = 7 } }
            let json = JsonSerializer.Serialize(value, typeInfo<Household> options)
            Expect.equal json """{"Head":{"Name":"Ada","Age":36,"Active":true},"Home":{"Street":"Main","Number":7}}""" "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, typeInfo<Household> options)) value "the value read back"
        }

        test "a field of a type from another context needs that context in the options" {
            let value = { Head = ada; Home = { Street = "Main"; Number = 7 } }
            Expect.throws (fun () -> JsonSerializer.Serialize(value, RecordJson.Default.Household) |> ignore) "Address is not in RecordJson"
        }

        test "Verify names the type that the options cannot resolve" {
            try
                RecordJson.Default.Verify RecordJson.Default.Options
                failtest "Verify accepted options that cannot resolve Address"
            with :? InvalidOperationException as error ->
                Expect.stringContains error.Message "Address" "the missing type"

            RecordJson.Default.Verify(combined ())
        }

        test "a context resolves only its own types" {
            Expect.isNotNull (RecordJson.Default.GetTypeInfo typeof<Person>) "Person"
            Expect.isNull (RecordJson.Default.GetTypeInfo typeof<Address>) "Address is in another context"
            Expect.isNull (RecordJson.Default.GetTypeInfo typeof<Guid>) "Guid"
        }
    ]
