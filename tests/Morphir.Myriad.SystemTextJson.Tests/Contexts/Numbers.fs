// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.Numbers

open System
open System.Text.Json
open System.Text.Json.Serialization

/// Every integer type.
type Integers =
    {
        B: byte
        SB: sbyte
        I16: int16
        U16: uint16
        I32: int
        U32: uint32
        I64: int64
        U64: uint64
    }

/// Every floating-point and decimal type.
type Reals = { F32: float32; F64: float; Dec: decimal }

/// The other primitive types.
type Scalars =
    {
        Flag: bool
        Text: string
        Letter: char
        Id: Guid
        At: DateTimeOffset
        When: DateTime
        Day: DateOnly
        Time: TimeOnly
        Span: TimeSpan
        Raw: JsonElement
    }

/// One member that accepts a number in a string, and one that does not.
type Counted =
    {
        [<JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)>]
        Count: int
        Strict: int
    }
