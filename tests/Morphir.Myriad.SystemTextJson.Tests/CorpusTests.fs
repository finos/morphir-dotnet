// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The compatibility corpus: every base encoding with each flag, and each preset, against FSharp.SystemTextJson.
module Morphir.Myriad.SystemTextJson.Tests.CorpusTests

open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open Expecto
open Morphir.Myriad.SystemTextJson.Tests.Compatibility
open Morphir.Myriad.SystemTextJson.Tests.Corpus
open Morphir.Myriad.SystemTextJson.Tests.UntaggedCorpus

let private tagged: (string * JsonUnionEncoding * JsonSerializerContext) list =
    [
            "AdjacentTag", (JsonUnionEncoding.AdjacentTag), (C_AdjacentTag.Default :> JsonSerializerContext)
            "AdjacentTag_NamedFields", (JsonUnionEncoding.AdjacentTag ||| JsonUnionEncoding.NamedFields), (C_AdjacentTag_NamedFields.Default :> JsonSerializerContext)
            "AdjacentTag_UnwrapFieldlessTags", (JsonUnionEncoding.AdjacentTag ||| JsonUnionEncoding.UnwrapFieldlessTags), (C_AdjacentTag_UnwrapFieldlessTags.Default :> JsonSerializerContext)
            "AdjacentTag_UnwrapSingleFieldCases", (JsonUnionEncoding.AdjacentTag ||| JsonUnionEncoding.UnwrapSingleFieldCases), (C_AdjacentTag_UnwrapSingleFieldCases.Default :> JsonSerializerContext)
            "AdjacentTag_UnwrapRecordCases", (JsonUnionEncoding.AdjacentTag ||| JsonUnionEncoding.UnwrapRecordCases), (C_AdjacentTag_UnwrapRecordCases.Default :> JsonSerializerContext)
            "AdjacentTag_NamedFields_UnionFieldNamesFromTypes", (JsonUnionEncoding.AdjacentTag ||| JsonUnionEncoding.NamedFields ||| JsonUnionEncoding.UnionFieldNamesFromTypes), (C_AdjacentTag_NamedFields_UnionFieldNamesFromTypes.Default :> JsonSerializerContext)
            "AdjacentTag_UnwrapOption_UnwrapSingleCaseUnions", (JsonUnionEncoding.AdjacentTag ||| JsonUnionEncoding.UnwrapOption ||| JsonUnionEncoding.UnwrapSingleCaseUnions), (C_AdjacentTag_UnwrapOption_UnwrapSingleCaseUnions.Default :> JsonSerializerContext)
            "ExternalTag", (JsonUnionEncoding.ExternalTag), (C_ExternalTag.Default :> JsonSerializerContext)
            "ExternalTag_NamedFields", (JsonUnionEncoding.ExternalTag ||| JsonUnionEncoding.NamedFields), (C_ExternalTag_NamedFields.Default :> JsonSerializerContext)
            "ExternalTag_UnwrapFieldlessTags", (JsonUnionEncoding.ExternalTag ||| JsonUnionEncoding.UnwrapFieldlessTags), (C_ExternalTag_UnwrapFieldlessTags.Default :> JsonSerializerContext)
            "ExternalTag_UnwrapSingleFieldCases", (JsonUnionEncoding.ExternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases), (C_ExternalTag_UnwrapSingleFieldCases.Default :> JsonSerializerContext)
            "ExternalTag_UnwrapRecordCases", (JsonUnionEncoding.ExternalTag ||| JsonUnionEncoding.UnwrapRecordCases), (C_ExternalTag_UnwrapRecordCases.Default :> JsonSerializerContext)
            "ExternalTag_NamedFields_UnionFieldNamesFromTypes", (JsonUnionEncoding.ExternalTag ||| JsonUnionEncoding.NamedFields ||| JsonUnionEncoding.UnionFieldNamesFromTypes), (C_ExternalTag_NamedFields_UnionFieldNamesFromTypes.Default :> JsonSerializerContext)
            "ExternalTag_UnwrapOption_UnwrapSingleCaseUnions", (JsonUnionEncoding.ExternalTag ||| JsonUnionEncoding.UnwrapOption ||| JsonUnionEncoding.UnwrapSingleCaseUnions), (C_ExternalTag_UnwrapOption_UnwrapSingleCaseUnions.Default :> JsonSerializerContext)
            "InternalTag", (JsonUnionEncoding.InternalTag), (C_InternalTag.Default :> JsonSerializerContext)
            "InternalTag_NamedFields", (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields), (C_InternalTag_NamedFields.Default :> JsonSerializerContext)
            "InternalTag_UnwrapFieldlessTags", (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapFieldlessTags), (C_InternalTag_UnwrapFieldlessTags.Default :> JsonSerializerContext)
            "InternalTag_UnwrapSingleFieldCases", (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases), (C_InternalTag_UnwrapSingleFieldCases.Default :> JsonSerializerContext)
            "InternalTag_UnwrapRecordCases", (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapRecordCases), (C_InternalTag_UnwrapRecordCases.Default :> JsonSerializerContext)
            "InternalTag_NamedFields_UnionFieldNamesFromTypes", (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields ||| JsonUnionEncoding.UnionFieldNamesFromTypes), (C_InternalTag_NamedFields_UnionFieldNamesFromTypes.Default :> JsonSerializerContext)
            "InternalTag_UnwrapOption_UnwrapSingleCaseUnions", (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapOption ||| JsonUnionEncoding.UnwrapSingleCaseUnions), (C_InternalTag_UnwrapOption_UnwrapSingleCaseUnions.Default :> JsonSerializerContext)
            "Default", (JsonUnionEncoding.Default), (C_Default.Default :> JsonSerializerContext)
            "NewtonsoftLike", (JsonUnionEncoding.NewtonsoftLike), (C_NewtonsoftLike.Default :> JsonSerializerContext)
            "ThothLike", (JsonUnionEncoding.ThothLike), (C_ThothLike.Default :> JsonSerializerContext)
            "FSharpLuLike", (JsonUnionEncoding.FSharpLuLike), (C_FSharpLuLike.Default :> JsonSerializerContext)
    ]

let private untagged: (string * JsonUnionEncoding * JsonSerializerContext) list =
    [
            "Untagged", (JsonUnionEncoding.Untagged), (U_Untagged.Default :> JsonSerializerContext)
            "Untagged_UnwrapFieldlessTags", (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.UnwrapFieldlessTags), (U_Untagged_UnwrapFieldlessTags.Default :> JsonSerializerContext)
            "Untagged_UnwrapRecordCases", (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.UnwrapRecordCases), (U_Untagged_UnwrapRecordCases.Default :> JsonSerializerContext)
            "Untagged_UnionFieldNamesFromTypes", (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.UnionFieldNamesFromTypes), (U_Untagged_UnionFieldNamesFromTypes.Default :> JsonSerializerContext)
            "Untagged_UnwrapOption_UnwrapSingleCaseUnions", (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.UnwrapOption ||| JsonUnionEncoding.UnwrapSingleCaseUnions), (U_Untagged_UnwrapOption_UnwrapSingleCaseUnions.Default :> JsonSerializerContext)
    ]

let private shapes = [ NoArgs; OneArg 1; Unnamed 2; TwoArgs(3, "t"); WithRecord { X = 4; Y = 5 } ]

let private bag =
    { Maybe = Some 1; Absent = None; MaybeValue = ValueSome 2; Shapes = shapes; Choice = No; Wrapped = Wrapped "w" }

let private places = [ Nowhere; Named "n"; Coded(7, "eu"); Exact { Lat = 1.5; Long = -2.25 } ]

let private trip = { Stops = places; Note = Some "x" }

/// Checks one value in one context: the JSON equals FSharp.SystemTextJson's, and it reads back to the value.
let private check (name: string) (encoding: JsonUnionEncoding) (context: JsonSerializerContext) (value: 'T) =
    let info = nonNull (context.GetTypeInfo typeof<'T>) :?> JsonTypeInfo<'T>
    let json = JsonSerializer.Serialize(value, info)
    let reference = reference (JsonFSharpOptions(encoding)) value
    Expect.equal json reference $"{name}: the JSON equals FSharp.SystemTextJson for %A{value}"
    Expect.equal (JsonSerializer.Deserialize(json, info)) value $"{name}: the value read back"

[<Tests>]
let tests =
    testList "compatibility corpus" [
        for name, encoding, context in tagged do
            test name {
                for shape in shapes do
                    check name encoding context shape

                for choice in [ Yes; No ] do
                    check name encoding context choice

                check name encoding context (Wrapped "w")
                check name encoding context bag
                check name encoding context { bag with MaybeValue = ValueNone; Maybe = None }
            }

        for name, encoding, context in untagged do
            test name {
                for place in places do
                    check name encoding context place

                check name encoding context trip
                check name encoding context { trip with Note = None }
            }
    ]
