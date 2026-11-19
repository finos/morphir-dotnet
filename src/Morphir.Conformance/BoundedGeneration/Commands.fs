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

/// File admission and rendering for pinned synthetic and real scale receipt replay.
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

    // PR finos/morphir#1198, head 305623ec52a96868a5a2e9cac7e80951b6030f73.
    // This is an independent pin, never a digest taken from the receipt.
    static member private ScaleCasesSha256 = "cde3dc58b49c229bac45af57126c2aa8b2f62c6e06766c84dd7f99bb8b0c4910"

    /// Read the byte-pinned real A/B case source and replay with the same Receipt.check used by controls.
    /// For C# callers. Optional fixture modelSha256/documentCount are checked when present.
    static member CheckScaleReport(schemaPath: string | null, casesPath: string | null, receiptPath: string | null) =
        let refused code = ReceiptCommands.Render(1, Some(code, "The scale receipt was refused by the pinned case admission or receipt checker."), None)
        try
            let path name (value: string | null) =
                match value with
                | null -> invalidArg name "A file path is required."
                | value -> value
            let schema = File.ReadAllBytes(path "schemaPath" schemaPath)
            let cases = File.ReadAllBytes(path "casesPath" casesPath)
            let digest = Convert.ToHexString(SHA256.HashData cases).ToLowerInvariant()
            if digest <> ReceiptCommands.ScaleCasesSha256 then
                refused "case_expectation_source_mismatch"
            else
                let bytes = File.ReadAllBytes(path "receiptPath" receiptPath)
                let options = JsonDocumentOptions(AllowDuplicateProperties = false)
                use source = JsonDocument.Parse(ReadOnlyMemory cases, options)
                use receipt = JsonDocument.Parse(ReadOnlyMemory bytes, options)
                let root = source.RootElement
                let report = receipt.RootElement
                let text name (element: JsonElement) = element.GetProperty(name: string).GetString() |> nonNull
                let caseId = text "caseId" (report.GetProperty "kit")
                let selected =
                    root.GetProperty("cases").EnumerateArray()
                    |> Seq.tryFind (fun item -> text "id" item = caseId)
                match selected with
                | None -> refused "contradictory_evidence"
                | Some selected ->
                    let expectation = {
                        ContractSha256 = text "contractSha256" root
                        SuiteSha256 = ReceiptCommands.ScaleCasesSha256
                        CaseId = text "id" selected
                        ExpectedSha256 = text "expectedSha256" (selected.GetProperty "comparison")
                    }
                    // Schema, exact expectation, contradictions and outcome replay remain owned by Receipt.check.
                    match Receipt.check schema expectation bytes with
                    | Error code -> refused code
                    | Ok replayed ->
                        let same name (expected: JsonElement) (actual: JsonElement) =
                            JsonElement.DeepEquals(expected.GetProperty(name: string), actual.GetProperty(name: string))
                        let fixture = report.GetProperty "fixture"
                        let expectedFixture = selected.GetProperty "fixture"
                        let optionalSourceMatches name =
                            match fixture.TryGetProperty(name: string) with
                            | false, _ -> true
                            | true, actual -> JsonElement.DeepEquals(expectedFixture.GetProperty name, actual)
                        let admitted =
                            [ "scale"; "expandedInputBytes"; "largestDocumentBytes" ]
                            |> List.forall (fun name -> same name expectedFixture fixture)
                        let tupleMatches =
                            root.GetProperty("tuple").EnumerateObject()
                            |> Seq.forall (fun field -> same field.Name (root.GetProperty "tuple") (report.GetProperty "tuple"))
                        if not (admitted && tupleMatches
                                && optionalSourceMatches "modelSha256" && optionalSourceMatches "documentCount"
                                && same "execution" root report && same "target" root report
                                && text "artifactSha256" (report.GetProperty "guest") = text "guestSha256" root
                                && same "kind" (selected.GetProperty "comparison") (report.GetProperty "comparison")) then
                            refused "contradictory_evidence"
                        else
                            let exitCode =
                                if replayed.Correctness.State = OutcomeState.Passed && replayed.Resources.State = OutcomeState.Passed then 0 else 1
                            ReceiptCommands.Render(exitCode, None, Some replayed)
        with
        | :? JsonException -> refused "invalid_shape"
        | :? Collections.Generic.KeyNotFoundException -> refused "invalid_shape"
        | :? InvalidOperationException -> refused "invalid_shape"
        | :? IOException as error -> ReceiptCommands.Render(2, Some("mck.io", error.Message), None)
        | :? UnauthorizedAccessException as error -> ReceiptCommands.Render(2, Some("mck.io", error.Message), None)
        | :? ArgumentException as error -> ReceiptCommands.Render(2, Some("mck.io", error.Message), None)
        | :? NotSupportedException as error -> ReceiptCommands.Render(2, Some("mck.io", error.Message), None)
