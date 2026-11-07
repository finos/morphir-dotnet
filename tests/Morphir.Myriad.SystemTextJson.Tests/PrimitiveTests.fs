// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.PrimitiveTests

open System
open System.Text.Json
open Expecto
open Morphir.Myriad.SystemTextJson.Tests.Numbers
open Morphir.Myriad.SystemTextJson.Tests.LenientNumbers

let private refused (read: unit -> 'T) (fragment: string) =
    try
        read () |> ignore
        failtestf "the JSON was accepted (%s)" fragment
    with :? JsonException ->
        ()

let private maxima =
    { B = Byte.MaxValue; SB = SByte.MaxValue; I16 = Int16.MaxValue; U16 = UInt16.MaxValue; I32 = Int32.MaxValue; U32 = UInt32.MaxValue; I64 = Int64.MaxValue; U64 = UInt64.MaxValue }

let private minima =
    { B = Byte.MinValue; SB = SByte.MinValue; I16 = Int16.MinValue; U16 = UInt16.MinValue; I32 = Int32.MinValue; U32 = UInt32.MinValue; I64 = Int64.MinValue; U64 = UInt64.MinValue }

let private integersJson (values: string list) =
    let names = [ "B"; "SB"; "I16"; "U16"; "I32"; "U32"; "I64"; "U64" ]
    "{" + (List.zip names values |> List.map (fun (n, v) -> $"\"{n}\":{v}") |> String.concat ",") + "}"

let private maximaText = [ "255"; "127"; "32767"; "65535"; "2147483647"; "4294967295"; "9223372036854775807"; "18446744073709551615" ]

[<Tests>]
let tests =
    testList "primitives" [
        test "every integer type keeps its largest value exactly" {
            let json = JsonSerializer.Serialize(maxima, NumberJson.Default.Integers)
            Expect.equal json (integersJson maximaText) "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, NumberJson.Default.Integers)) maxima "the value read back"
        }

        test "every integer type keeps its smallest value exactly" {
            let json = JsonSerializer.Serialize(minima, NumberJson.Default.Integers)
            Expect.equal json (integersJson [ "0"; "-128"; "-32768"; "0"; "-2147483648"; "0"; "-9223372036854775808"; "0" ]) "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, NumberJson.Default.Integers)) minima "the value read back"
        }

        test "a number out of range for its type is refused" {
            for index, tooLarge in List.indexed [ "256"; "128"; "32768"; "65536"; "2147483648"; "4294967296"; "9223372036854775808"; "18446744073709551616" ] do
                let values = maximaText |> List.mapi (fun i v -> if i = index then tooLarge else v)
                refused (fun () -> JsonSerializer.Deserialize(integersJson values, NumberJson.Default.Integers)) tooLarge
        }

        test "a negative number for an unsigned type is refused" {
            refused (fun () -> JsonSerializer.Deserialize(integersJson [ "0"; "0"; "0"; "0"; "0"; "0"; "0"; "-1" ], NumberJson.Default.Integers)) "-1"
        }

        test "a fraction for an integer type is refused" {
            refused (fun () -> JsonSerializer.Deserialize(integersJson [ "0"; "0"; "0"; "0"; "1.5"; "0"; "0"; "0" ], NumberJson.Default.Integers)) "1.5"
        }

        test "a number in a string is refused by default" {
            refused (fun () -> JsonSerializer.Deserialize(integersJson [ "0"; "0"; "0"; "0"; "\"1\""; "0"; "0"; "0" ], NumberJson.Default.Integers)) "\"1\""
        }

        test "floating-point and decimal values round-trip" {
            let value = { F32 = 0.25f; F64 = 0.1; Dec = Decimal.MaxValue }
            let json = JsonSerializer.Serialize(value, NumberJson.Default.Reals)
            Expect.equal json """{"F32":0.25,"F64":0.1,"Dec":79228162514264337593543950335}""" "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, NumberJson.Default.Reals)) value "the value read back"
        }

        test "the other primitive types use the System.Text.Json formats" {
            let raw = JsonDocument.Parse("""{"a":[1,true]}""").RootElement.Clone()

            let value =
                {
                    Flag = true
                    Text = "é \"quoted\""
                    Letter = 'A'
                    Id = Guid("0f8fad5b-d9cb-469f-a165-70867728950e")
                    At = DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours 1.0)
                    When = DateTime(2026, 11, 7, 10, 0, 0)
                    Day = DateOnly(2026, 11, 7)
                    Time = TimeOnly(10, 30, 15)
                    Span = TimeSpan(1, 2, 3)
                    Raw = raw
                }

            let json = JsonSerializer.Serialize(value, NumberJson.Default.Scalars)

            Expect.equal
                json
                """{"Flag":true,"Text":"\u00E9 \u0022quoted\u0022","Letter":"A","Id":"0f8fad5b-d9cb-469f-a165-70867728950e","At":"2026-11-07T10:00:00+01:00","When":"2026-11-07T10:00:00","Day":"2026-11-07","Time":"10:30:15","Span":"01:02:03","Raw":{"a":[1,true]}}"""
                "the JSON"

            match JsonSerializer.Deserialize(json, NumberJson.Default.Scalars) with
            | null -> failtest "no value was read"
            | back ->
                Expect.equal { back with Raw = raw } value "the value read back"
                Expect.equal (back.Raw.GetRawText()) (raw.GetRawText()) "the JSON element"
        }

        test "a char must be a string of one character" {
            let scalars (letter: string) =
                $"""{{"Flag":true,"Text":"","Letter":{letter},"Id":"0f8fad5b-d9cb-469f-a165-70867728950e","At":"2026-11-07T10:00:00+01:00","When":"2026-11-07T10:00:00","Day":"2026-11-07","Time":"10:30:15","Span":"01:02:03","Raw":{{}}}}"""

            for letter in [ "\"AB\""; "\"\""; "65" ] do
                refused (fun () -> JsonSerializer.Deserialize(scalars letter, NumberJson.Default.Scalars)) letter
        }

        test "a member with JsonNumberHandling reads a number in a string; the other member does not" {
            Expect.equal (JsonSerializer.Deserialize("""{"Count":"42","Strict":1}""", NumberJson.Default.Counted)) { Count = 42; Strict = 1 } "the value"
            refused (fun () -> JsonSerializer.Deserialize("""{"Count":42,"Strict":"1"}""", NumberJson.Default.Counted)) "Strict"
        }

        test "a context setting reads numbers in strings everywhere, also in collections" {
            let value = JsonSerializer.Deserialize("""{"Count":"42","Ratio":"0.5","Counts":["1",2]}""", LenientJson.Default.Reading)
            Expect.equal value { Count = 42L; Ratio = 0.5; Counts = [ 1; 2 ] } "the value"
            Expect.equal (JsonSerializer.Serialize(value, LenientJson.Default.Reading)) """{"Count":42,"Ratio":0.5,"Counts":[1,2]}""" "numbers are written as numbers"
        }
    ]
