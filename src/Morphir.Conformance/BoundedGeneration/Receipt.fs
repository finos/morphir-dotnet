// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.BoundedGeneration

open System
open System.Globalization
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions
open global.Json.Schema

/// The expected identity and output digest of a case selected from the kit.
type CaseExpectation = {
    ContractSha256: string
    SuiteSha256: string
    CaseId: string
    ExpectedSha256: string
}

[<RequireQualifiedAccess>]
type OutcomeState =
    | Passed
    | Failed
    | NotRun

type Outcome = {
    State: OutcomeState
    Reasons: string list
}

type ReplayedReceipt = {
    Correctness: Outcome
    Resources: Outcome
}

type private Source = {
    Method: string
    EvidenceSha256: string
}

type private Measurement =
    | Measured of value: int64 * source: Source
    | Missing
    | NoMeasurement

type private Limit =
    | Enforced of ceiling: int64
    | NotEnforced of ceiling: int64
    | NoLimit

[<RequireQualifiedAccess>]
module Receipt =
    // Exact parent schema bytes. A receipt cannot register its own validation schema under this draft's ID.
    let private schemaSha256 =
        "DAEA631F7B6DED1FF981395D7477C66827FFDB9FD4F17A59458D1E1576602B07"

    let private memberOption name (object: JsonElement) =
        if object.ValueKind = JsonValueKind.Object then
            match object.TryGetProperty(name: string) with
            | true, value -> Some value
            | false, _ -> None
        else
            None

    let private text name (object: JsonElement) =
        object.GetProperty(name: string).GetString()
        |> nonNull

    // The schema caps integer values at the largest precise JSON integer. Decimal also reads integer-valued
    // numbers with a decimal point or exponent that JSON Schema accepts as integers.
    let private count name (object: JsonElement) =
        object.GetProperty(name: string).GetDecimal()
        |> int64

    let private pointerTokens (pointer: string) =
        if pointer = "" then
            Some []
        elif pointer.StartsWith("/", StringComparison.Ordinal) then
            let tokens = pointer.Substring(1).Split('/')

            if
                tokens
                |> Array.exists (fun token -> Regex.IsMatch(token, "~(?![01])"))
            then
                None
            else
                tokens
                |> Array.map (fun token -> token.Replace("~1", "/").Replace("~0", "~"))
                |> List.ofArray
                |> Some
        else
            None

    let private arrayIndex (token: string) =
        match Int32.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, index ->
            index >= 0
            && index.ToString(CultureInfo.InvariantCulture) = token
        | false, _ -> false

    // An optional name in the receipt does not become understood just because a JSON Pointer can find it there.
    // Follow only the property definitions, local definitions, and array items of the pinned schema.
    let private understoodCritical schema receipt =
        let rec understands node tokens =
            match tokens with
            | [] -> true
            | name :: rest ->
                match memberOption "$ref" node, memberOption "oneOf" node with
                | Some reference, _ ->
                    let reference =
                        reference.GetString()
                        |> nonNull

                    let prefix = "#/$defs/"

                    reference.StartsWith(prefix, StringComparison.Ordinal)
                    && (memberOption "$defs" schema
                        |> Option.bind (memberOption (reference.Substring(prefix.Length)))
                        |> Option.exists (fun target -> understands target tokens))
                | None, Some alternatives ->
                    alternatives.EnumerateArray()
                    |> Seq.exists (fun choice -> understands choice tokens)
                | None, None ->
                    match memberOption "properties" node, memberOption "items" node with
                    | Some properties, _ ->
                        memberOption name properties
                        |> Option.exists (fun child -> understands child rest)
                    | None, Some items ->
                        arrayIndex name
                        && understands items rest
                    | None, None -> false

        match memberOption "critical" receipt with
        | None -> true
        | Some pointers ->
            pointers.EnumerateArray()
            |> Seq.forall (fun pointer ->
                pointer.GetString()
                |> nonNull
                |> pointerTokens
                |> Option.exists (understands schema)
            )

    let private source (element: JsonElement) = {
        Method = text "method" element
        EvidenceSha256 = text "evidenceSha256" element
    }

    let private measurement (row: JsonElement) =
        match text "state" row with
        | "measured" -> Measured(count "value" row, source (row.GetProperty "source"))
        | "missing" -> Missing
        | _ -> NoMeasurement

    let private limit row =
        match text "state" row with
        | "enforced" -> Enforced(count "ceiling" row)
        | "not-enforced" -> NotEnforced(count "ceiling" row)
        | _ -> NoLimit

    let private measured =
        function
        | Measured(value, _) -> Some value
        | _ -> None

    let private ceiling =
        function
        | Enforced value
        | NotEnforced value -> Some value
        | NoLimit -> None

    let private isEnforced =
        function
        | Enforced _ -> true
        | _ -> false

    let private overLimit (measurement, limit) =
        match measured measurement, ceiling limit with
        | Some value, Some maximum -> value > maximum
        | _ -> false

    let private inconsistentProcessSamples (getMeasurement: string -> Measurement) =
        match getMeasurement "peakHostTreeResidentBytes" with
        | Measured(tree, treeSource) ->
            let sameSample =
                function
                | Measured(peak, source) when source = treeSource -> Some peak
                | _ -> None

            let host = sameSample (getMeasurement "peakHostResidentBytes")
            let descendants = getMeasurement "peakDescendantResidentBytes"

            match host, descendants with
            | Some peak, NoMeasurement ->
                tree
                <> peak
            | _ ->
                match host, sameSample descendants with
                | Some host, Some descendants ->
                    tree < max host descendants
                    || tree > host
                              + descendants
                | Some peak, None
                | None, Some peak -> tree < peak
                | None, None -> false
        | _ -> false

    let private claimed (element: JsonElement) =
        let state =
            match text "state" element with
            | "passed" -> OutcomeState.Passed
            | "failed" -> OutcomeState.Failed
            | _ -> OutcomeState.NotRun

        {
            State = state
            Reasons =
                element.GetProperty("reasons").EnumerateArray()
                |> Seq.map (fun reason ->
                    reason.GetString()
                    |> nonNull
                )
                |> Seq.toList
        }

    let private correctness (comparison: JsonElement) expected =
        match memberOption "observedSha256" comparison with
        | None -> {
            State = OutcomeState.NotRun
            Reasons = [ "comparison.missing" ]
          }
        | Some observed when observed.GetString() = expected -> {
            State = OutcomeState.Passed
            Reasons = []
          }
        | Some _ -> {
            State = OutcomeState.Failed
            Reasons = [ "comparison.mismatch" ]
          }

    let private resources (fixture: JsonElement) wasm getMeasurement getLimit =
        if text "scale" fixture = "small" then
            {
                State = OutcomeState.NotRun
                Reasons = [ "resource.small_case" ]
            }
        else
            let requiredPairs = [
                "peakHostTreeResidentBytes", "hostTreeResidentBytes"
                "maxInputBatchBytes", "inputBatchBytes"
                "maxOutputBatchBytes", "outputBatchBytes"
                "peakContextBytes", "contextBytes"
                "peakIndexBytes", "indexBytes"
                if wasm then
                    "peakGuestLinearMemoryBytes", "guestLinearMemoryBytes"
            ]

            let pairs names =
                names
                |> List.map (fun (measurementName, limitName) ->
                    getMeasurement measurementName, getLimit limitName
                )

            let required = pairs requiredPairs

            let decoded =
                pairs [
                    "peakDecodedResidentBytes", "decodedResidentBytes"
                    "peakDecodedResidentNodes", "decodedResidentNodes"
                ]

            let decodedComplete =
                decoded
                |> List.exists (fun (measuredValue, limitValue) ->
                    (measured measuredValue
                     |> Option.isSome)
                    && isEnforced limitValue
                )

            let missingDecodedMeasurement =
                not decodedComplete
                && (decoded
                    |> List.forall (
                        fst
                        >> measured
                        >> Option.isNone
                    )
                    || decoded
                       |> List.exists (fun (measurement, limit) ->
                           Option.isNone (measured measurement)
                           && isEnforced limit
                       ))

            let missingDecodedLimit =
                not decodedComplete
                && (decoded
                    |> List.forall (
                        snd
                        >> isEnforced
                        >> not
                    )
                    || decoded
                       |> List.exists (fun (measurement, limit) ->
                           Option.isSome (measured measurement)
                           && not (isEnforced limit)
                       ))

            let otherMissing =
                [ "peakHostResidentBytes"; "bytesRead"; "bytesWritten"; "elapsedMilliseconds" ]
                |> List.exists (
                    getMeasurement
                    >> measured
                    >> Option.isNone
                )

            let missingMeasurement =
                otherMissing
                || (getMeasurement "peakDescendantResidentBytes" = Missing)
                || (required
                    |> List.exists (
                        fst
                        >> measured
                        >> Option.isNone
                    ))
                || missingDecodedMeasurement

            let documentLimit = getLimit "documentBytes"

            let missingLimit =
                not (isEnforced documentLimit)
                || (required
                    |> List.exists (
                        snd
                        >> isEnforced
                        >> not
                    ))
                || missingDecodedLimit

            let documentOversize =
                ceiling documentLimit
                |> Option.exists (fun maximum -> count "largestDocumentBytes" fixture > maximum)

            let expanded = count "expandedInputBytes" fixture

            let tooSmall =
                [
                    "hostTreeResidentBytes"
                    if wasm then
                        "guestLinearMemoryBytes"
                ]
                |> List.exists (fun name ->
                    ceiling (getLimit name)
                    |> Option.exists (fun maximum ->
                        expanded
                        <= maximum
                    )
                )

            let ceilingExceeded =
                List.exists overLimit required
                || List.exists overLimit decoded

            let reasons = [
                if ceilingExceeded then
                    "resource.ceiling_exceeded"
                if documentOversize then
                    "resource.document_oversize"
                if tooSmall then
                    "resource.input_too_small"
                if missingLimit then
                    "resource.limit_not_enforced"
                if missingMeasurement then
                    "resource.measurement_missing"
            ]

            let state =
                if
                    ceilingExceeded
                    || documentOversize
                    || tooSmall
                then
                    OutcomeState.Failed
                elif
                    missingLimit
                    || missingMeasurement
                then
                    OutcomeState.NotRun
                else
                    OutcomeState.Passed

            { State = state; Reasons = reasons }

    let private replay schema expectation (receipt: JsonElement) =
        let kit = receipt.GetProperty "kit"
        let comparison = receipt.GetProperty "comparison"
        let measurements = receipt.GetProperty "measurements"
        let limits = receipt.GetProperty "limits"
        let wasm = text "execution" receipt = "wasm-generation"

        let getMeasurement (name: string) =
            measurements.GetProperty name
            |> measurement

        let getLimit (name: string) =
            limits.GetProperty name
            |> limit

        if not (understoodCritical schema receipt) then
            Error "unknown_critical"
        elif
            text "contractSha256" kit
            <> expectation.ContractSha256
            || text "suiteSha256" kit
               <> expectation.SuiteSha256
            || text "caseId" kit
               <> expectation.CaseId
        then
            Error "contradictory_evidence"
        elif
            text "expectedSha256" comparison
            <> expectation.ExpectedSha256
        then
            Error "expected_digest_mismatch"
        elif
            (not wasm
             && getMeasurement "peakGuestLinearMemoryBytes"
                <> NoMeasurement)
            || inconsistentProcessSamples getMeasurement
        then
            Error "contradictory_evidence"
        else
            let replayed = {
                Correctness = correctness comparison expectation.ExpectedSha256
                Resources = resources (receipt.GetProperty "fixture") wasm getMeasurement getLimit
            }

            if
                replayed.Correctness
                <> claimed (receipt.GetProperty "correctness")
                || replayed.Resources
                   <> claimed (receipt.GetProperty "resources")
            then
                Error "outcome_mismatch"
            else
                Ok replayed

    /// Check one receipt against the pinned schema and an independent case expectation.
    let check
        (schema: byte array)
        (expectation: CaseExpectation)
        (receipt: byte array)
        : Result<ReplayedReceipt, string> =
        let pinnedSchema = Array.copy schema

        if
            Convert.ToHexString(SHA256.HashData pinnedSchema)
            <> schemaSha256
        then
            Error "invalid_shape"
        else
            let options = JsonDocumentOptions(AllowDuplicateProperties = false)

            try
                use contract = JsonDocument.Parse(ReadOnlyMemory pinnedSchema, options)
                use report = JsonDocument.Parse(ReadOnlyMemory receipt, options)
                let schemaRoot = contract.RootElement
                let receiptRoot = report.RootElement

                let validator =
                    JsonSchema.FromText(
                        schemaRoot.GetRawText(),
                        BuildOptions(SchemaRegistry = SchemaRegistry())
                    )

                if validator.Evaluate(receiptRoot).IsValid then
                    replay schemaRoot expectation receiptRoot
                else
                    Error "invalid_shape"
            with :? JsonException ->
                Error "invalid_shape"
