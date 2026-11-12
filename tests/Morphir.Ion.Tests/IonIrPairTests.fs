/// The vendored reference files state what the reader must build, so a bug in this reader cannot change what
/// the tests expect. Every JSON and Ion file here comes from the pinned reference CLI.
module Morphir.Ion.Tests.IonIrPairTests

open System
open System.Globalization
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open Morphir.Ion
open Morphir.Ion.Tests
open Morphir.Json.Classic
open Morphir.Json.V4

let private jsonOptions = JsonDocumentOptions(MaxDepth = 8192)

let private readJsonV4 (bytes: byte array) =
    use document = JsonDocument.Parse(ReadOnlyMemory bytes, jsonOptions)

    match V4Json.readDistribution document.RootElement with
    | Ok read -> read.Distribution
    | Error error -> failtestf "The v4 JSON reader refused the file: %A" error

let private readJsonClassic (bytes: byte array) =
    use document = JsonDocument.Parse(ReadOnlyMemory bytes, jsonOptions)

    match ClassicJson.readDistribution document.RootElement with
    | Ok read -> read.Distribution
    | Error error -> failtestf "The classic JSON reader refused the file: %A" error

let private readIon (bytes: byte array) =
    match IonIr.read IonReadLimits.defaults (ReadOnlyMemory bytes) with
    | Ok document -> document
    | Error error -> failtestf "The Ion reader refused the file: %A" error

/// Checks that an Ion file and a JSON file build one IR value.
let private expectSameIr (ion: byte array) (json: byte array) =
    match readIon ion with
    | ClassicIr(_, fromIon) ->
        let fromJson = readJsonClassic json

        Expect.equal
            (ClassicComparison.comparable fromIon)
            (ClassicComparison.comparable fromJson)
            "The Ion and JSON readers build one classic value"
    | V4Ir(_, fromIon) ->
        let fromJson = readJsonV4 json
        Expect.equal fromIon fromJson "The Ion and JSON readers build one v4 value"

let private stems (folder: string) (suffix: string) =
    Fixtures.names
    |> Array.filter (fun name ->
        name.StartsWith folder
        && name.EndsWith suffix
        && not (name.Substring(folder.Length).Contains '/')
    )
    |> Array.map (fun name ->
        name.Substring(
            folder.Length,
            name.Length
            - folder.Length
            - suffix.Length
        )
    )
    |> List.ofArray

let private pairNames = stems "pairs/" ".ion"
let private recordNames = stems "records/" ".record.ion"

/// The rows of PROVENANCE.md that hold a path and a full hash.
let private provenanceRows () = [
    for row in
        Regex.Matches(
            Fixtures.text "PROVENANCE.md",
            @"(?m)^\| `((?:pairs|records)/[^`]+)` \| `([0-9a-f]{64})` \|"
        ) -> row.Groups[1].Value, row.Groups[2].Value
]

[<Tests>]
let tests =
    testList "The result matches the JSON reader" [
        testList "Reference pairs" [
            for name in pairNames do
                testCase name
                <| fun () ->
                    expectSameIr
                        (Fixtures.bytes $"pairs/{name}.ion")
                        (Fixtures.bytes $"pairs/{name}.json")
        ]

        testCase "The named reference pairs are all present"
        <| fun () ->
            Expect.containsAll
                pairNames
                [
                    "v3-daily-rate"
                    "greeting-example"
                    "rule-set-example"
                    "direct-rules-example"
                    "complete-example"
                    "books-and-records-example"
                    "v3-library-nodes"
                    "v3-specs"
                    "v4-library-nodes"
                    "v4-specs"
                    "v4-application"
                ]
                "Every case of task 6.1"
        |> testLabel "PlatformSpecific"

        testCase "Document numbers read the same under a culture with a different minus sign"
        <| fun () ->
            let before = CultureInfo.CurrentCulture

            try
                // Swedish writes the minus sign as U+2212, so culture-sensitive formatting of an exponent shows.
                CultureInfo.CurrentCulture <- CultureInfo "sv-SE"

                Expect.equal
                    (CultureInfo.CurrentCulture.NumberFormat.NegativeSign)
                    "−"
                    "The culture is the one under test"

                let json = Fixtures.bytes "records/document-numbers.json"
                expectSameIr (Fixtures.bytes "records/document-numbers.record.ion") json
                expectSameIr (Fixtures.bytes "records/document-numbers.datagram.ion") json
            finally
                CultureInfo.CurrentCulture <- before
        |> testLabel "PlatformSpecific"

        testList "Hand-written records" [
            for name in recordNames do
                testCase $"{name} reads as its datagram"
                <| fun () ->
                    let record = readIon (Fixtures.bytes $"records/{name}.record.ion")
                    let datagram = readIon (Fixtures.bytes $"records/{name}.datagram.ion")

                    match record, datagram with
                    | ClassicIr(_, a), ClassicIr(_, b) ->
                        Expect.equal
                            (ClassicComparison.comparable a)
                            (ClassicComparison.comparable b)
                            "One value"
                    | V4Ir(_, a), V4Ir(_, b) -> Expect.equal a b "One value"
                    | a, b -> failtestf "The layouts read to different families: %A, %A" a b

                testCase $"{name} reads as the pinned CLI's JSON for it"
                <| fun () ->
                    let json = Fixtures.bytes $"records/{name}.json"
                    expectSameIr (Fixtures.bytes $"records/{name}.record.ion") json
                    expectSameIr (Fixtures.bytes $"records/{name}.datagram.ion") json
        ]

        testCase "Every record vector has its three files"
        <| fun () ->
            Expect.isGreaterThan recordNames.Length 3 "Vectors are found"

            for name in recordNames do
                for suffix in [ ".datagram.ion"; ".json" ] do
                    Expect.contains Fixtures.names $"records/{name}{suffix}" "The file exists"
        |> testLabel "PlatformSpecific"

        testCase "The vendored pairs and records match their recorded hashes"
        <| fun () ->
            let rows =
                provenanceRows ()
                |> Map.ofList

            let files =
                Fixtures.names
                |> Array.filter (fun name ->
                    name.StartsWith "pairs/"
                    || name.StartsWith "records/"
                )

            Expect.isGreaterThan files.Length 30 "The vendored files are found"

            for file in files do
                match rows.TryFind file with
                | Some expected ->
                    Expect.equal (Fixtures.sha256 (Fixtures.bytes file)) expected file
                | None -> failtestf "%s has no row in PROVENANCE.md" file

            for file, _ in Map.toList rows do
                Expect.contains files file "A row names a file that exists"
        |> testLabel "PlatformSpecific"
    ]
