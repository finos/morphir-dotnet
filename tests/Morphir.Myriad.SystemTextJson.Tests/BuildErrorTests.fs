// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.BuildErrorTests

open System.Collections.Generic
open Expecto
open Morphir.Myriad.SystemTextJson.Generator

let private settings =
    match ContextSettings.parse (Dictionary<string, string>(dict [ "Name", "AppJson"; "Namespace", "Sample.Json" ])) with
    | Ok settings -> settings
    | Error _ -> failwith "the test settings are not valid"

/// Generates from the source, and returns the build lines of the errors.
let private errors (source: string) =
    match Generation.generate settings "Types.fs" ("namespace Sample\n\n" + source) with
    | Ok _ -> failtest "the input was accepted"
    | Error errors -> errors |> List.map GenerationError.toBuildLine

/// Checks that one error has each part of the text.
let private expectError (source: string) (parts: string list) =
    let lines = errors source

    let matching =
        lines |> List.filter (fun line -> parts |> List.forall (fun (part: string) -> line.Contains part))

    Expect.isNonEmpty matching $"an error has each of {parts}; the errors are {lines}"

[<Tests>]
let tests =
    testList "build errors" [
        test "a generic user type names the file, line, type and reason" {
            expectError "type Page<'T> = { Items: 'T list }\n" [ "Types.fs(3,"; "error MSTJ0010"; "Page"; "generic" ]
        }

        test "a user type with a reserved built-in name is refused" {
            expectError "type Guid = { Value: string }\n" [ "Types.fs(3,"; "Guid"; "built-in" ]
        }

        test "an unknown attribute argument is refused" {
            expectError "[<JsonRecord(Colour = JsonNaming.CamelCase)>]\ntype R = { A: int }\n" [ "R"; "Colour"; "not known" ]
        }

        test "an attribute argument with a value of the wrong kind is refused" {
            expectError "[<JsonRecord(Naming = 3)>]\ntype R = { A: int }\n" [ "R"; "Naming" ]
        }

        test "JsonName and JsonPropertyName on one field contradict each other" {
            expectError "type R =\n    { [<JsonName \"a\"; JsonPropertyName \"b\">] A: int }\n" [ "Types.fs(4,"; "R.A"; "JsonName"; "JsonPropertyName" ]
        }

        test "a field type with no JSON form names the member" {
            expectError "type R = { Values: seq<int> }\n" [ "R.Values"; "seq" ]
        }

        test "an option of an option is refused" {
            expectError "type R = { Value: int }\ntype S = { Items: Map<string, R list option option> }\n" [ "S.Items"; "option of an option" ]
        }

        test "JsonNumberHandling on a field that is not a number is refused" {
            expectError "type R = { [<JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)>] Names: string list }\n" [ "R.Names"; "JsonNumberHandling" ]
        }

        test "a record with a private representation is refused" {
            expectError "type R = private { A: int }\n" [ "R"; "private" ]
        }

        test "an untagged union whose cases share a token kind is refused and names the union" {
            let source = "[<JsonUnion(BaseEncoding = (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.SelectByTokenKind))>]\ntype Id =\n    | Small of int\n    | Large of int64\n"
            expectError source [ "Id"; "token kind"; "Number" ]
        }

        test "an untagged union whose cases share a member name is refused and names both cases" {
            let source = "[<JsonUnion(BaseEncoding = JsonUnionEncoding.Untagged)>]\ntype U =\n    | First of x: int\n    | Second of x: string\n"
            expectError source [ "U"; "First"; "Second"; "'x'" ]
        }

        test "two base encodings on one union are refused" {
            expectError "[<JsonUnion(BaseEncoding = (JsonUnionEncoding.AdjacentTag ||| JsonUnionEncoding.ExternalTag))>]\ntype U = | A | B\n" [ "U"; "more than one base encoding" ]
        }

        test "every error is reported, not only the first" {
            let lines = errors "type A<'T> = { X: 'T }\ntype B = { Y: seq<int> }\n"
            Expect.hasLength lines 2 "two errors"
        }
    ]
