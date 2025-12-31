module Morphir.SDK.Tests.LocalDateTests

open Expecto
open Expecto.Flip
open Morphir.SDK
open System

[<Tests>]
let tests =
    testList "LocalDate" [
        test "fromParts creates date" {
            LocalDate.fromParts 2025 12 31
            |> Expect.isSome "Should create valid date"
        }

        test "fromParts rejects invalid date" {
            LocalDate.fromParts 2025 13 31
            |> Expect.isNone "Should reject invalid month"
        }

        test "year extracts year" {
            let date = DateOnly(2025, 12, 31)
            LocalDate.year date
            |> Expect.equal "Should be 2025" 2025
        }

        test "month extracts month" {
            let date = DateOnly(2025, 12, 31)
            LocalDate.month date
            |> Expect.equal "Should be 12" 12
        }

        test "day extracts day" {
            let date = DateOnly(2025, 12, 31)
            LocalDate.day date
            |> Expect.equal "Should be 31" 31
        }

        test "addDays adds days" {
            let date = DateOnly(2025, 12, 31)
            LocalDate.addDays 1 date
            |> fun d -> LocalDate.year d
            |> Expect.equal "Should be 2026" 2026
        }

        test "toIsoString formats date" {
            let date = DateOnly(2025, 12, 31)
            LocalDate.toIsoString date
            |> Expect.equal "Should be '2025-12-31'" "2025-12-31"
        }

        test "fromIsoString parses date" {
            LocalDate.fromIsoString "2025-12-31"
            |> Expect.isSome "Should parse valid date"
        }
    ]
