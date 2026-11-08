// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.TypeLoweringTests

open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.Frontend.FSharp.Tests.AnalysisSupport
open Morphir.Frontend.FSharp.Tests.LoweringSupport

let private fleet (lines: string list) =
    document
        "Fleet.fs"
        ((String.concat
            "\n"
            ([ "namespace Acme.CarRental"; "module Fleet =" ]
             @ (lines
                |> List.map (fun line -> $"    {line}"))))
         + "\n")

/// The v4 type definitions of module fleet, without source locations.
let private v4Types lines =
    let bytes = passed (buildAndWrite v4 version4 (planOf [ fleet lines ]))
    let root = jsonOf bytes

    let rec strip (node: JsonNode | null) =
        match node with
        | :? JsonObject as item ->
            for pair in List.ofSeq item do
                strip pair.Value

            item.Remove "source"
            |> ignore

            match item["attributes"] with
            | :? JsonObject as attributes when attributes.Count = 0 ->
                item.Remove "attributes"
                |> ignore
            | _ -> ()
        | :? JsonArray as items ->
            for child in items do
                strip child
        | _ -> ()

    let types =
        root["distribution"]["Library"]["def"]["modules"]["fleet"]["Public"]["types"]

    strip types
    types

/// The classic v3 type definitions of module fleet, after the classic reader accepted the document.
let private v3Types lines =
    let bytes = passed (buildAndWrite classic v3 (planOf [ fleet lines ]))
    use document = JsonDocument.Parse(System.ReadOnlyMemory<byte>(bytes))

    match Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement with
    | Ok read -> Expect.equal read.Version 3 "the classic reader reads version 3"
    | Error error -> failtestf "The classic reader refused the written document: %A" error

    let root = jsonOf bytes
    let distribution = root["distribution"].AsArray()
    let definition = distribution[3]
    let modules = definition["modules"].AsArray()
    let first = modules[0]
    let fleetModule = first.AsArray()
    fleetModule[1]["value"]["types"]

/// Expected JSON written by hand, with @INT@, @TEXT@, @CINT@ and @CTEXT@ for the common SDK types.
let private fill (json: string) =
    json
        .Replace("@INT@", """{"Reference":{"fqname":"morphir/SDK:basics#int"}}""")
        .Replace("@TEXT@", """{"Reference":{"fqname":"morphir/SDK:string#string"}}""")
        .Replace(
            "@CINT@",
            """["Reference",{},[[["morphir"],["s","d","k"]],[["basics"]],["int"]],[]]"""
        )
        .Replace(
            "@CTEXT@",
            """["Reference",{},[[["morphir"],["s","d","k"]],[["string"]],["string"]],[]]"""
        )

let private expectTypes actual (expected: string) message =
    expectJson actual (fill expected) message

[<Tests>]
let tests =
    testList "F# type lowering" [
        test "a record and a union, in v4" {
            expectTypes
                (v4Types [
                    "type Car = { Plate: string; Seats: int }"
                    "type Status = Available | Rented of days: int"
                ])
                """{
                  "car": {"Public": {"TypeAliasDefinition": {"typeParams": [], "typeExp":
                    {"Record": {"fields": {"plate": @TEXT@, "seats": @INT@}}}}}},
                  "status": {"Public": {"CustomTypeDefinition": {"typeParams": [], "access": "Public",
                    "constructors": {"available": [], "rented": [["days", @INT@]]}}}}
                }"""
                "v4 types"
        }

        test "a record and a union, in classic version 3" {
            expectTypes
                (v3Types [
                    "type Car = { Plate: string; Seats: int }"
                    "type Status = Available | Rented of days: int"
                ])
                """[
                  [["car"], {"access": "Public", "value": {"doc": "", "value": ["TypeAliasDefinition", [],
                    ["Record", {}, [{"name": ["plate"], "tpe": @CTEXT@}, {"name": ["seats"], "tpe": @CINT@}]]]}}],
                  [["status"], {"access": "Public", "value": {"doc": "", "value": ["CustomTypeDefinition", [],
                    {"access": "Public", "value": [[["available"], []], [["rented"], [[["days"], @CINT@]]]]}]}}]
                ]"""
                "classic types"
        }

        test "standard generic types map to the SDK with the SDK Result argument order" {
            expectTypes
                (v4Types [ "type Booking = { Outcome: Result<int option list, string> }" ])
                """{"booking": {"Public": {"TypeAliasDefinition": {"typeParams": [], "typeExp":
                  {"Record": {"fields": {"outcome": {"Reference": {"fqname": "morphir/SDK:result#result", "args": [
                    @TEXT@,
                    {"Reference": {"fqname": "morphir/SDK:list#list", "args": [
                      {"Reference": {"fqname": "morphir/SDK:maybe#maybe", "args": [@INT@]}}]}}]}}}}}}}}}"""
                "Result<int option list, string>"
        }

        test "every primitive maps to its SDK type" {
            let types =
                v4Types [
                    "type Primitives = { A: bool; B: int; C: int64; D: float; E: decimal; F: char; G: string; H: unit }"
                ]

            let fields =
                types["primitives"]["Public"]["TypeAliasDefinition"]["typeExp"]["Record"]["fields"]

            let fqName (name: string) =
                match fields[name]["Reference"] with
                | null -> fields[name].ToJsonString()
                | reference -> reference["fqname"].GetValue<string>()

            Expect.equal
                ([ "a"; "b"; "c"; "d"; "e"; "f"; "g"; "h" ]
                 |> List.map fqName)
                [
                    "morphir/SDK:basics#bool"
                    "morphir/SDK:basics#int"
                    "morphir/SDK:int#int-64"
                    "morphir/SDK:basics#float"
                    "morphir/SDK:decimal#decimal"
                    "morphir/SDK:char#char"
                    "morphir/SDK:string#string"
                    """{"Unit":{}}"""
                ]
                "SDK types"
        }

        test "abbreviations, generic parameters, tuples and functions" {
            expectTypes
                (v4Types [
                    "type Plate = string"
                    "type Pair<'a> = 'a * 'a"
                    "type Pricing = int -> decimal"
                    "type Garage = { Plates: Plate list; Spots: Pair<int>; Rate: Pricing }"
                ])
                """{
                  "plate": {"Public": {"TypeAliasDefinition": {"typeParams": [], "typeExp": @TEXT@}}},
                  "pair": {"Public": {"TypeAliasDefinition": {"typeParams": ["a"], "typeExp":
                    {"Tuple": {"elements": [{"Variable": {"name": "a"}}, {"Variable": {"name": "a"}}]}}}}},
                  "pricing": {"Public": {"TypeAliasDefinition": {"typeParams": [], "typeExp":
                    {"Function": {"parameterType": @INT@, "returnType": {"Reference": {"fqname": "morphir/SDK:decimal#decimal"}}}}}}},
                  "garage": {"Public": {"TypeAliasDefinition": {"typeParams": [], "typeExp": {"Record": {"fields": {
                    "plates": {"Reference": {"fqname": "morphir/SDK:list#list", "args": [{"Reference": {"fqname": "acme/car-rental:fleet#plate"}}]}},
                    "spots": {"Reference": {"fqname": "acme/car-rental:fleet#pair", "args": [@INT@]}},
                    "rate": {"Reference": {"fqname": "acme/car-rental:fleet#pricing"}}}}}}}}
                }"""
                "aliases"
        }

        test "positional union fields are arg1, arg2, and a tuple field stays one argument" {
            expectTypes
                (v4Types [ "type Shape = Pair of int * string | Tupled of (int * string)" ])
                """{"shape": {"Public": {"CustomTypeDefinition": {"typeParams": [], "access": "Public", "constructors": {
                  "pair": [["arg-1", @INT@], ["arg-2", @TEXT@]],
                  "tupled": [["arg-1", {"Tuple": {"elements": [@INT@, @TEXT@]}}]]}}}}}"""
                "constructor arguments"
        }

        test "a generic union keeps its type parameter" {
            expectTypes
                (v4Types [ "type Box<'value> = Empty | Full of 'value" ])
                """{"box": {"Public": {"CustomTypeDefinition": {"typeParams": ["value"], "access": "Public", "constructors": {
                  "empty": [], "full": [["arg-1", {"Variable": {"name": "value"}}]]}}}}}"""
                "generic union"
        }

        test "a private union representation has private constructors" {
            let types = v4Types [ "type Token = private Token of string" ]

            let access = types["token"]["Public"]["CustomTypeDefinition"]["access"]

            Expect.equal (access.GetValue<string>()) "Private" "constructor access"
        }

        test "a type without a Morphir mapping is refused and named" {
            let plan = planOf [ fleet [ "type Bag = { Items: ResizeArray<int>; Size: byte }" ] ]

            for family, version in [ v4, version4; classic, v3 ] do
                let diagnostics = failed (buildAndWrite family version plan)

                Expect.equal
                    (diagnostics
                     |> List.map (fun diagnostic -> codeOf diagnostic))
                    [ "fsharp.unsupported_reference"; "fsharp.unsupported_reference" ]
                    "both fields"

                Expect.stringContains
                    diagnostics[0].Message
                    "System.Collections.Generic.List`1"
                    "names the F# type"

                Expect.stringContains diagnostics[1].Message "System.Byte" "names the F# type"
        }

        test "both families build the same program" {
            let plan =
                planOf [
                    fleet [
                        "type Plate = string"
                        "type Car = { Plate: Plate; Seats: int }"
                        "type Status = Available | Rented of days: int"
                    ]
                ]

            for family, version in
                [
                    v4, version4
                    classic, v3
                    classic, Versioning.Classic 1
                    classic, Versioning.Classic 2
                ] do
                Expect.isTrue
                    ((buildAndWrite family version plan).IsPassed)
                    $"{family.Family} {version}"
        }
    ]
