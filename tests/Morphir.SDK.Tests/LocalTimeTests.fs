module Morphir.SDK.Tests.LocalTimeTests

open Expecto
open Expecto.Flip
open Morphir.SDK
open System

[<Tests>]
let tests =
    testList "LocalTime" [
        test "fromParts creates time" {
            LocalTime.fromParts 10 30 45 0
            |> Expect.isSome "Should create valid time"
        }

        test "fromParts rejects invalid time" {
            LocalTime.fromParts 25 30 45 0
            |> Expect.isNone "Should reject invalid hour"
        }

        test "hour extracts hour" {
            let time = TimeOnly(10, 30, 45)
            LocalTime.hour time
            |> Expect.equal "Should be 10" 10
        }

        test "minute extracts minute" {
            let time = TimeOnly(10, 30, 45)
            LocalTime.minute time
            |> Expect.equal "Should be 30" 30
        }

        test "second extracts second" {
            let time = TimeOnly(10, 30, 45)
            LocalTime.second time
            |> Expect.equal "Should be 45" 45
        }

        test "addHours adds hours" {
            let time = TimeOnly(10, 30, 45)
            LocalTime.addHours 2 time
            |> fun t -> LocalTime.hour t
            |> Expect.equal "Should be 12" 12
        }

        test "toIsoString formats time" {
            let time = TimeOnly(10, 30, 45)
            LocalTime.toIsoString time
            |> Expect.equal "Should be '10:30:45'" "10:30:45"
        }

        test "fromIsoString parses time" {
            LocalTime.fromIsoString "10:30:45"
            |> Expect.isSome "Should parse valid time"
        }
    ]
