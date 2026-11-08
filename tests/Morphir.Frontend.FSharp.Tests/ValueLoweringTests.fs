// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.ValueLoweringTests

open System.Text
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

/// A copy of a JSON value without "attributes" members. A missing value stays missing.
let rec private strip (node: JsonNode | null) =
    // Option.toObj gives the nullable type without a return annotation, which Fantomas 5 cannot parse.
    let copied (value: JsonNode) = Option.toObj (Some value)

    match node with
    | :? JsonObject as item ->
        let copy = JsonObject()

        for pair in item do
            if
                pair.Key
                <> "attributes"
            then
                copy[pair.Key] <- strip pair.Value

        copied copy
    | :? JsonArray as items ->
        let copy = JsonArray()

        for child in items do
            copy.Add(strip child)

        copied copy
    | null -> null
    | other -> copied (other.DeepClone())

let private v4Document lines =
    jsonOf (passed (buildAndWrite v4 version4 (planOf [ fleet lines ])))

let private valuesOf (root: JsonNode | null) =
    root["distribution"]["Library"]["def"]["modules"]["fleet"]["Public"]["values"]

/// The v4 ExpressionBody of one value, without attributes.
let private v4Value lines (name: string) =
    let values = valuesOf (v4Document lines)
    strip values[name]["Public"]["ExpressionBody"]

let private int = """{"Reference":{"fqname":"morphir/SDK:basics#int"}}"""

let private fill (json: string) = json.Replace("@INT@", int)

let private expectValue actual expected message =
    expectJson actual (fill expected) message

/// Every value and pattern node object in a v4 value, without the type expressions.
let rec private nodes (node: JsonNode | null) : JsonObject list =
    match node with
    | :? JsonObject as item ->
        let own =
            match item["attributes"] with
            | :? JsonObject -> [ item ]
            | _ -> []

        let children =
            item
            |> Seq.filter (fun pair ->
                pair.Key
                <> "attributes"
                && pair.Key
                   <> "inputTypes"
                && pair.Key
                   <> "outputType"
            )
            |> Seq.collect (fun pair -> nodes pair.Value)
            |> List.ofSeq

        own
        @ children
    | :? JsonArray as items ->
        items
        |> Seq.collect nodes
        |> List.ofSeq
    | _ -> []

[<Tests>]
let tests =
    testList "F# value lowering" [
        test "a curried function with a local binding has two inputs and a let-definition body" {
            expectValue
                (v4Value
                    [
                        "let combine (a: int) (b: int) = a"
                        "let total (rate: int) (days: int) = let basis = combine rate days in basis"
                    ]
                    "total")
                """{"inputTypes": {"rate": @INT@, "days": @INT@}, "outputType": @INT@, "body":
                  {"LetDefinition": {"name": "basis", "definition": {"ExpressionBody": {"inputTypes": {}, "outputType": @INT@, "body":
                    {"Apply": {"function": {"Apply": {"function": {"Reference": {"fqname": "acme/car-rental:fleet#combine"}},
                                                      "argument": {"Variable": {"name": "rate"}}}},
                               "argument": {"Variable": {"name": "days"}}}}}},
                   "in": {"Variable": {"name": "basis"}}}}}"""
                "total"
        }

        test "a record update sets only the named fields" {
            expectValue
                (v4Value
                    [
                        "type Car = { Plate: string; Seats: int }"
                        "let grow car = { car with Seats = 4 }"
                    ]
                    "grow")
                """{"inputTypes": {"car": {"Reference": {"fqname": "acme/car-rental:fleet#car"}}},
                  "outputType": {"Reference": {"fqname": "acme/car-rental:fleet#car"}},
                  "body": {"UpdateRecord": {"target": {"Variable": {"name": "car"}},
                    "fields": {"seats": {"Literal": {"literal": {"IntegerLiteral": 4}}}}}}}"""
                "grow"
        }

        test "an ordered match keeps its cases, their order and their bindings" {
            let body =
                v4Value
                    [
                        "type Status = Available | Rented of days: int"
                        "let describe s = match s with | Rented 0 -> 0 | Rented n -> n | Available -> 10"
                    ]
                    "describe"

            expectValue
                body["body"]
                """{"PatternMatch": {"value": {"Variable": {"name": "s"}}, "cases": [
                  {"pattern": {"ConstructorPattern": {"fqname": "acme/car-rental:fleet#rented",
                     "patterns": [{"LiteralPattern": {"literal": {"IntegerLiteral": 0}}}]}},
                   "body": {"Literal": {"literal": {"IntegerLiteral": 0}}}},
                  {"pattern": {"ConstructorPattern": {"fqname": "acme/car-rental:fleet#rented",
                     "patterns": [{"AsPattern": {"pattern": {"WildcardPattern": {}}, "name": "n"}}]}},
                   "body": {"Variable": {"name": "n"}}},
                  {"pattern": {"ConstructorPattern": {"fqname": "acme/car-rental:fleet#available", "patterns": []}},
                   "body": {"Literal": {"literal": {"IntegerLiteral": 10}}}}]}}"""
                "match"
        }

        test
            "reduced-away and nested matches keep their source structure, and the subject is emitted once" {
            let lines = [
                "let compute (x: int) = x"
                "let variable (x: int) = match compute x with | n -> n"
                "let tuple (a: int) (b: string) = match (a, b) with | (n, _) -> n"
                "let literal (x: int) = match x with | 0 -> \"zero\" | _ -> \"other\""
                "let unit () = match () with | () -> 1"
                "let nested (a: int) (b: int) ="
                "    match a with"
                "    | 0 -> 1"
                "    | _ ->"
                "        match b with"
                "        | 0 -> 2"
                "        | _ -> 3"
            ]

            let values = valuesOf (v4Document lines)

            let body (name: string) =
                strip values[name]["Public"]["ExpressionBody"]["body"]

            expectValue
                (body "variable")
                """{"PatternMatch": {"value": {"Apply": {"function": {"Reference": {"fqname": "acme/car-rental:fleet#compute"}},
                  "argument": {"Variable": {"name": "x"}}}}, "cases": [
                  {"pattern": {"AsPattern": {"pattern": {"WildcardPattern": {}}, "name": "n"}}, "body": {"Variable": {"name": "n"}}}]}}"""
                "variable match"

            expectValue
                (body "tuple")
                """{"PatternMatch": {"value": {"Tuple": {"elements": [{"Variable": {"name": "a"}}, {"Variable": {"name": "b"}}]}}, "cases": [
                  {"pattern": {"TuplePattern": {"patterns": [{"AsPattern": {"pattern": {"WildcardPattern": {}}, "name": "n"}}, {"WildcardPattern": {}}]}},
                   "body": {"Variable": {"name": "n"}}}]}}"""
                "tuple match"

            let literalCases = (body "literal")["PatternMatch"]["cases"]
            Expect.equal (literalCases.AsArray().Count) 2 "literal match keeps both cases"

            expectValue
                ((body "unit")["Lambda"]["body"])
                """{"PatternMatch": {"value": {"Unit": {}}, "cases": [{"pattern": {"UnitPattern": {}}, "body": {"Literal": {"literal": {"IntegerLiteral": 1}}}}]}}"""
                "unit match"

            expectValue
                (body "nested")
                """{"PatternMatch": {"value": {"Variable": {"name": "a"}}, "cases": [
                  {"pattern": {"LiteralPattern": {"literal": {"IntegerLiteral": 0}}}, "body": {"Literal": {"literal": {"IntegerLiteral": 1}}}},
                  {"pattern": {"WildcardPattern": {}}, "body": {"PatternMatch": {"value": {"Variable": {"name": "b"}}, "cases": [
                    {"pattern": {"LiteralPattern": {"literal": {"IntegerLiteral": 0}}}, "body": {"Literal": {"literal": {"IntegerLiteral": 2}}}},
                    {"pattern": {"WildcardPattern": {}}, "body": {"Literal": {"literal": {"IntegerLiteral": 3}}}}]}}}]}}"""
                "nested match"
        }

        test "constructor patterns keep the field arity of their case" {
            let body =
                v4Value
                    [
                        "type Shape = Pair of int * string | Tupled of (int * string)"
                        "let get shape ="
                        "    match shape with"
                        "    | Pair (a, _) -> a"
                        "    | Tupled (a, _) -> a"
                    ]
                    "get"

            let caseNode = body["body"]["PatternMatch"]["cases"]
            let cases = caseNode.AsArray()

            let arguments (index: int) =
                let case = cases[index]
                let patterns = case["pattern"]["ConstructorPattern"]["patterns"]
                patterns.AsArray()

            Expect.equal ((arguments 0).Count) 2 "two fields"
            Expect.equal ((arguments 1).Count) 1 "one tuple field"
            let tupleField = (arguments 1)[0]
            Expect.isNotNull (tupleField["TuplePattern"]) "the one field is a tuple pattern"
        }

        test "a lambda and its argument pattern carry their checked types and v4 sources" {
            let values = valuesOf (v4Document [ "let increment = fun (x: int) -> x" ])
            let lambda = values["increment"]["Public"]["ExpressionBody"]["body"]["Lambda"]
            let lambdaAttributes = lambda["attributes"]
            let pattern = lambda["pattern"]["AsPattern"]
            let patternAttributes = pattern["attributes"]

            expectValue
                (strip (lambdaAttributes["inferredType"]))
                """{"Function": {"parameterType": @INT@, "returnType": @INT@}}"""
                "lambda type"

            expectValue (strip (patternAttributes["inferredType"])) "@INT@" "pattern type"
            Expect.isNotNull (lambdaAttributes["source"]) "lambda source"
            Expect.isNotNull (patternAttributes["source"]) "pattern source"
        }

        test "a polymorphic match result keeps its generic function type" {
            let body =
                v4Value
                    [
                        "let identity = match () with | () -> fun value -> value"
                        "let number = identity 1"
                        "let word = identity \"x\""
                    ]
                    "identity"

            expectValue
                body["outputType"]
                """{"Function": {"parameterType": {"Variable": {"name": "a"}}, "returnType": {"Variable": {"name": "a"}}}}"""
                "generic"
        }

        test "parameter and let patterns keep the supported pattern set with checked types" {
            let lines = [
                "type Box = Box of int"
                "let first (a, _) = a"
                "let open' (Box n) = n"
                "let both ((a, b) as pair) = pair"
                "let local (x: int * string) = let (n, _) = x in n"
            ]

            let values = valuesOf (v4Document lines)

            let body (name: string) =
                strip values[name]["Public"]["ExpressionBody"]["body"]

            Expect.isNotNull ((body "first")["Lambda"]["pattern"]["TuplePattern"]) "tuple parameter"

            Expect.isNotNull
                ((body "open")["Lambda"]["pattern"]["ConstructorPattern"])
                "constructor parameter"

            Expect.isNotNull
                ((body "both")["Lambda"]["pattern"]["AsPattern"]["pattern"]["TuplePattern"])
                "as parameter"

            Expect.isNotNull
                ((body "local")["Destructure"]["pattern"]["TuplePattern"])
                "let pattern"
        }

        test "every value and pattern node carries an inferred type and a v4 source" {
            let root =
                v4Document [
                    "type Car = { Plate: string; Seats: int }"
                    "type Status = Available | Rented of days: int"
                    "let make () = { Plate = \"A\"; Seats = 2 }"
                    "let seats (car: Car) = car.Seats"
                    "let pick b = if b then Rented 3 else Available"
                    "let items = [ 1; 2 ]"
                    "let rec count xs = match xs with | [] -> 0 | _ :: rest -> count rest"
                    "let classify = function | Available -> \"free\" | Rented _ -> \"busy\""
                ]

            let values = valuesOf root
            let all = nodes values

            Expect.isNonEmpty all "nodes"

            for node in all do
                let attributes = node["attributes"]

                Expect.isNotNull
                    (attributes["inferredType"])
                    $"inferred type on {node.ToJsonString()}"

                Expect.isNotNull (attributes["source"]) $"source on {node.ToJsonString()}"
        }

        test "no generated evidence binding reaches either family" {
            let compiled =
                planOf [
                    fleet [
                        "let describe (n: int) = match n with | 0 -> \"zero\" | m -> string m"
                            .Replace("string m", "\"other\"")
                        "let pick (x: int) = match x with | y -> y"
                    ]
                ]

            for family, version in [ v4, version4; classic, v3 ] do
                let text = Encoding.UTF8.GetString(passed (buildAndWrite family version compiled))
                Expect.isFalse (text.Contains "morphir-evidence") $"{version}: no generated name"
                Expect.isFalse (text.Contains "evidence") $"{version}: no evidence name"
        }

        test "both families lower the same values, and classic v3 is read back" {
            let compiled =
                planOf [
                    fleet [
                        "type Car = { Plate: string; Seats: int }"
                        "type Status = Available | Rented of days: int"
                        "let make () = { Plate = \"A\"; Seats = 2 }"
                        "let grow car = { car with Seats = 4 }"
                        "let describe s = match s with | Rented 0 -> 0 | Rented n -> n | Available -> 10"
                        "let swap = fun (a, b) -> (b, a)"
                    ]
                ]

            for version in [ Versioning.Classic 1; Versioning.Classic 2; v3 ] do
                let bytes = passed (buildAndWrite classic version compiled)
                use document = JsonDocument.Parse(System.ReadOnlyMemory<byte>(bytes))

                match Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement with
                | Ok _ -> ()
                | Error error -> failtestf "classic %A was not read back: %A" version error

            passed (buildAndWrite v4 version4 compiled)
            |> ignore
        }

        test
            "a constructor with several fields takes the tuple's elements, and a tuple-valued field keeps the tuple" {
            let values =
                valuesOf (
                    v4Document [
                        "type Shape = Pair of int * string | Tupled of (int * string)"
                        "let pair = Pair (1, \"a\")"
                        "let tupled = Tupled (1, \"a\")"
                    ]
                )

            let body (name: string) =
                strip values[name]["Public"]["ExpressionBody"]["body"]

            expectValue
                (body "pair")
                """{"Apply": {"function": {"Apply": {"function": {"Constructor": {"fqname": "acme/car-rental:fleet#pair"}},
                  "argument": {"Literal": {"literal": {"IntegerLiteral": 1}}}}},
                  "argument": {"Literal": {"literal": {"StringLiteral": "a"}}}}}"""
                "two fields"

            expectValue
                (body "tupled")
                """{"Apply": {"function": {"Constructor": {"fqname": "acme/car-rental:fleet#tupled"}},
                  "argument": {"Tuple": {"elements": [{"Literal": {"literal": {"IntegerLiteral": 1}}}, {"Literal": {"literal": {"StringLiteral": "a"}}}]}}}}"""
                "one tuple field"
        }

        test "an explicit type application is lowered with the compiler's instantiation" {
            let values =
                valuesOf (
                    v4Document [
                        "let identity (x: 'T) = x"
                        "let one = identity<int> 1"
                        "let text = identity<string>"
                    ]
                )

            expectValue
                (strip values["one"]["Public"]["ExpressionBody"]["body"])
                """{"Apply": {"function": {"Reference": {"fqname": "acme/car-rental:fleet#identity"}},
                  "argument": {"Literal": {"literal": {"IntegerLiteral": 1}}}}}"""
                "applied"

            expectValue
                (strip values["text"]["Public"]["ExpressionBody"]["outputType"])
                """{"Function": {"parameterType": {"Reference": {"fqname": "morphir/SDK:string#string"}},
                  "returnType": {"Reference": {"fqname": "morphir/SDK:string#string"}}}}"""
                "instantiated value"
        }

        test
            "different local names with one Morphir name are refused, and same-name shadowing is kept" {
            let collision =
                planOf [ fleet [ "let f (fooBar: int) = let foo_bar = 2 in fooBar" ] ]

            for family, version in [ v4, version4; classic, v3 ] do
                let diagnostics = failed (buildAndWrite family version collision)

                Expect.equal
                    (diagnostics
                     |> List.map codeOf)
                    [ "fsharp.name_collision" ]
                    $"{version}"

            let shadowing = planOf [ fleet [ "let f (x: int) = let x = 2 in x" ] ]

            passed (buildAndWrite v4 version4 shadowing)
            |> ignore
        }

        test "a value that a family cannot hold is refused by that family's builder" {
            // F# literals cannot exceed int64, the classic limit; the builders' own tests cover integers that
            // only v4 holds. A non-finite float is refused by both families, from the same lowering.
            let compiled = planOf [ fleet [ "let huge = 1e400" ] ]

            for family, version in [ classic, v3; v4, version4 ] do
                Expect.equal
                    (failed (buildAndWrite family version compiled)
                     |> List.map codeOf)
                    [ "morphir.unrepresentable" ]
                    $"{version} refuses"
        }
    ]
