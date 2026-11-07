// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The compatibility corpus for tagged encodings. Many contexts, one for each encoding, read this file.
namespace Morphir.Myriad.SystemTextJson.Tests.Corpus

/// A record that a union case holds.
type Point = { X: int; Y: int }

/// Every kind of case.
type Shape =
    | NoArgs
    | OneArg of x: int
    | Unnamed of int
    | TwoArgs of a: int * b: string
    | WithRecord of Point

/// One case with one field.
type Wrapped = Wrapped of string

/// Cases with no field.
type Choice =
    | Yes
    | No

/// Options, a list of unions, a fieldless union and a single-case union in one record.
type Bag =
    {
        Maybe: int option
        Absent: int option
        MaybeValue: int voption
        Shapes: Shape list
        Choice: Choice
        Wrapped: Wrapped
    }
