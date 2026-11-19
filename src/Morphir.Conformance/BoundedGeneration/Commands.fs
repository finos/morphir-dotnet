// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.BoundedGeneration

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// A rendered replay result for the C# command boundary.
[<Sealed>]
type ReceiptCommandResult internal (exitCode: int, output: string) =
    /// Zero for two passes, one for refusal or incomplete/failed evidence, two for usage or I/O errors.
    member _.ExitCode = exitCode
    /// Exactly one JSON document with separate error, correctness and resources members.
    member _.Output = output
    override _.ToString() = $"exit {exitCode}"

/// File admission and rendering for the initial frozen synthetic receipt replay route.
[<AbstractClass; Sealed>]
type ReceiptCommands =
    // Parent spec/mck/bounded-generation/receipt.controls.json, also pinned by FixtureCatalog.
    static member private ControlsSha256 = "B5BE6A8BBCC8A24306499ED58C529180569E75DF0A392DA511AC014B5FB88F3E"

    static member private Render(exitCode: int, error: (string * string) option, replayed: ReplayedReceipt option) =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject()
        writer.WriteString("command", "mck bounded-generation report-check")
        writer.WriteNumber("exitCode", exitCode)
        writer.WritePropertyName "error"
        match error with
        | None -> writer.WriteNullValue()
        | Some(code, message) ->
            writer.WriteStartObject()
            writer.WriteString("code", code)
            writer.WriteString("message", message)
            writer.WriteEndObject()
        let outcome (name: string) (value: Outcome option) =
            writer.WritePropertyName(name: string)
            match value with
            | None -> writer.WriteNullValue()
            | Some decision ->
                writer.WriteStartObject()
                let state =
                    match decision.State with
                    | OutcomeState.Passed -> "passed"
                    | OutcomeState.Failed -> "failed"
                    | OutcomeState.NotRun -> "not-run"
                writer.WriteString("state", state)
                writer.WriteStartArray "reasons"
                for reason in decision.Reasons do writer.WriteStringValue(reason: string)
                writer.WriteEndArray()
                writer.WriteEndObject()
        outcome "correctness" (replayed |> Option.map _.Correctness)
        outcome "resources" (replayed |> Option.map _.Resources)
        writer.WriteEndObject()
        writer.Flush()
        ReceiptCommandResult(exitCode, Encoding.UTF8.GetString(stream.ToArray()))

    /// Render a System.CommandLine admission error for C# callers.
    static member UsageError(message: string) =
        ReceiptCommands.Render(2, Some("mck.usage", message), None)

    /// Read explicit local paths and replay with Receipt.check. No adapter or network is used.
    static member CheckReport(schemaPath: string | null, controlsPath: string | null, receiptPath: string | null) =
        try
            let path (name: string) (value: string | null) =
                match value with
                | null -> invalidArg name "A file path is required."
                | value -> value
            let schema = File.ReadAllBytes(path "schemaPath" schemaPath)
            let controls = File.ReadAllBytes(path "controlsPath" controlsPath)
            if Convert.ToHexString(SHA256.HashData controls) <> ReceiptCommands.ControlsSha256 then
                ReceiptCommands.Render(1, Some("case_expectation_source_mismatch", "The controls bytes do not match the frozen parent expectation source."), None)
            else
                use source = JsonDocument.Parse(ReadOnlyMemory controls, JsonDocumentOptions(AllowDuplicateProperties = false))
                let expected = source.RootElement.GetProperty "caseExpectation"
                let text name = expected.GetProperty(name: string).GetString() |> nonNull
                let expectation = {
                    ContractSha256 = text "contractSha256"
                    SuiteSha256 = text "suiteSha256"
                    CaseId = text "caseId"
                    ExpectedSha256 = text "expectedSha256"
                }
                match Receipt.check schema expectation (File.ReadAllBytes(path "receiptPath" receiptPath)) with
                | Error code -> ReceiptCommands.Render(1, Some(code, "The receipt was refused by the pinned receipt checker."), None)
                | Ok replayed ->
                    let exitCode =
                        if replayed.Correctness.State = OutcomeState.Passed && replayed.Resources.State = OutcomeState.Passed then 0 else 1
                    ReceiptCommands.Render(exitCode, None, Some replayed)
        with
        | :? IOException as error -> ReceiptCommands.Render(2, Some("mck.io", error.Message), None)
        | :? UnauthorizedAccessException as error -> ReceiptCommands.Render(2, Some("mck.io", error.Message), None)
        | :? ArgumentException as error -> ReceiptCommands.Render(2, Some("mck.io", error.Message), None)
        | :? NotSupportedException as error -> ReceiptCommands.Render(2, Some("mck.io", error.Message), None)
