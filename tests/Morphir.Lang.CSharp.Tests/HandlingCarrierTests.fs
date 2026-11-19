// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The metadata carrier of a handling option in the current IR (OPT-1, task 6.1): the extensions of the type attributes
/// of the node that the option is for, as {"morphir-dotnet": {"options": {...}}}.
module Morphir.Lang.CSharp.Tests.HandlingCarrierTests

open System.Text
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Tests.BackendSupport

/// The type through the V4 JSON writer and reader, as a model file of `morphir generate` holds it.
let private throughJson (t: Type) : Type * string =
    match V4JsonWriter.writeType false t with
    | Error error -> failtestf "write failed: %A" error
    | Ok bytes ->
        use document = JsonDocument.Parse(bytes: byte array)

        match V4Json.readType document.RootElement with
        | Error error -> failtestf "read failed: %A" error
        | Ok read -> read, Encoding.UTF8.GetString bytes

let private attributesOf (t: Type) =
    match t with
    | Type.Variable(a, _)
    | Type.Reference(a, _, _)
    | Type.Tuple(a, _)
    | Type.Record(a, _)
    | Type.ExtensibleRecord(a, _, _)
    | Type.Function(a, _, _)
    | Type.Unit a -> a

let private options: HandlingOptions = {
    HandlingOptions.empty with
        Traversal = Some TraversalForm.Functional
        TailRecursion = Some TailRecursionForm.LocalFunction
        FloatEquality = Some FloatEquality.Ieee
}

[<Tests>]
let tests =
    testList "Handling option carrier" [
        test "an option goes through the V4 JSON of a model file and the option record reads it back" {
            let carried = HandlingCarrier.write options intT
            let read, json = throughJson carried

            Expect.equal read carried "the JSON keeps the carrier"
            Expect.stringContains json "\"morphir-dotnet\":{\"options\":{" "the carrier is under the key of morphir-dotnet"
            Expect.stringContains json "\"traversal\":\"functional\"" "the option has the word of its configuration key"
            Expect.stringContains json "\"tail-recursion\":\"local-function\"" "the option has the word of its configuration key"
            Expect.stringContains json "\"float-equality\":\"ieee\"" "the option has the word of its configuration key"
            Expect.equal (HandlingCarrier.read read) options "the option record reads the carrier back"
        }

        test "the carrier keeps the value classes of EQL-2 on the same type variable" {
            let classes = { ta with Extensions = Some(ValueClasses.carrier [ Comparable ]) }
            let carried = HandlingCarrier.write { HandlingOptions.empty with Traversal = Some TraversalForm.Loop } (Type.Variable(classes, name "a"))
            let read, _ = throughJson carried

            Expect.equal (ValueClasses.ofExtensions (attributesOf read).Extensions) [ Comparable ] "the classes stay"
            Expect.equal (HandlingCarrier.read read).Traversal (Some TraversalForm.Loop) "the option is there too"
        }

        test "a type with no carrier gives no option, and an empty option record writes no carrier" {
            Expect.equal (HandlingCarrier.read intT) HandlingOptions.empty "no carrier, no option"
            Expect.equal (HandlingCarrier.write HandlingOptions.empty intT) intT "no option, no carrier"
        }

        test "a carrier with a word that is not a value of the option gives no value for that option" {
            let extensions = """{"morphir-dotnet":{"options":{"traversal":"sideways","float-equality":"clr"}}}"""
            use document = JsonDocument.Parse("""{"Reference":{"fqname":"morphir/SDK:basics#int","attributes":{"extensions":""" + extensions + "}}}")

            match V4Json.readType document.RootElement with
            | Error error -> failtestf "read failed: %A" error
            | Ok read -> Expect.equal (HandlingCarrier.read read) { HandlingOptions.empty with FloatEquality = Some FloatEquality.Clr } "only the known word is read"
        }
    ]
