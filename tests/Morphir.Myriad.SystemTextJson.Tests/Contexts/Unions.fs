// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.Unions

open Morphir.Myriad.SystemTextJson

/// A record that a union case holds.
type Point = { X: int; Y: int }

/// Every kind of case: no field, one named field, one unnamed field, two fields, a record field.
type Shape =
    | NoArgs
    | OneArg of x: int
    | Unnamed of int
    | TwoArgs of a: int * b: string
    | WithRecord of Point

/// One case with one field: UnwrapSingleCaseUnions writes the field alone.
type Wrapped = Wrapped of string

/// Cases with no field.
type Choice =
    | A
    | B

/// A record with an option, a fieldless union and a single-case union.
type Holder = { Value: int option; Choice: Choice; Wrapped: Wrapped }

/// The MEP ExtensionType shape: strings with their own names, read with no regard to letter case.
[<JsonUnion(BaseEncoding = (JsonUnionEncoding.AdjacentTag ||| JsonUnionEncoding.UnwrapFieldlessTags), TagCaseInsensitive = true)>]
type ExtensionType =
    | [<JsonName "frontend">] Frontend
    | [<JsonName "validator">] Validator

/// An object tagged by a kind member, with camel-case tags.
[<JsonUnion(BaseEncoding = (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields), TagName = "kind", TagNaming = JsonNaming.CamelCase)>]
type Problem =
    | TypeMismatch of expected: string * found: string
    | Missing of name: string

/// The MEP request id: a number or a string, chosen by the JSON token kind.
[<JsonUnion(BaseEncoding = (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.SelectByTokenKind))>]
type RequestId =
    | Number of uint64
    | Text of string

/// An untyped JSON value: each case has a different JSON token kind.
[<JsonUnion(BaseEncoding = (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.SelectByTokenKind))>]
type JsonValue =
    | Text of string
    | Amount of float
    | Flag of bool
    | Items of JsonValue list
    | Members of Map<string, JsonValue>

/// The classic Morphir IR shape: an array that starts with the tag.
[<JsonUnion(BaseEncoding = JsonUnionEncoding.ThothLike)>]
type Literal =
    | BoolLiteral of bool
    | StringLiteral of string
    | UnitLiteral

/// A recursive union.
type Tree =
    | Leaf
    | Node of Tree list

/// The tag must come first without AllowUnorderedTag.
[<JsonUnion(BaseEncoding = JsonUnionEncoding.AdjacentTag)>]
type Ordered = Ordered of int
