// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.Converters

open System
open System.Text.Json
open System.Text.Json.Serialization

/// A user converter for one member: upper case when written, lower case when read.
type UpperCaseConverter() =
    inherit JsonConverter<string>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        match reader.GetString() with
        | null -> ""
        | text -> text.ToLowerInvariant()

    override _.Write(writer: Utf8JsonWriter, value: string, _options: JsonSerializerOptions) =
        writer.WriteStringValue(value.ToUpperInvariant())

/// A type with a user converter: written as "a,b".
[<JsonConverter(typeof<PairConverter>)>]
type Pair = { A: int; B: int }

/// The converter of Pair.
and PairConverter() =
    inherit JsonConverter<Pair>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        match reader.GetString() with
        | null -> raise (JsonException "A pair was expected.")
        | text ->
            match text.Split ',' with
            | [| a; b |] -> { A = int a; B = int b }
            | _ -> raise (JsonException "A pair was expected.")

    override _.Write(writer: Utf8JsonWriter, value: Pair, _options: JsonSerializerOptions) =
        writer.WriteStringValue($"{value.A},{value.B}")

/// A member with a user converter, a member without one, and a member of a type with a user converter.
type Labelled =
    {
        [<JsonConverter(typeof<UpperCaseConverter>)>]
        Label: string
        Plain: string
        Pair: Pair
    }
