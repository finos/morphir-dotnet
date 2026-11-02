module Morphir.Json.Tests.V4.V4KitTests

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open Morphir.IR.V4
open Morphir.Json.V4
open Morphir.Json.Tests.V4.KitCases

let private parse (json: string) (read: JsonElement -> Result<'a, V4JsonError>) =
    use document = JsonDocument.Parse json

    read document.RootElement
    |> Result.map box

let private value json = parse json V4Json.readValue
let private typ json = parse json V4Json.readType
let private pattern json = parse json V4Json.readPattern
let private document json = parse json V4Json.readDistribution

let private distribution body =
    document $"""{{ "formatVersion": 4, "distribution": {body} }}"""

let private specModule body =
    distribution
        $"""{{ "Specs": {{ "packageName": "t", "spec": {{ "modules": {{ "m": {body} }} }} }} }}"""

let private defModule body =
    distribution
        $"""{{ "Library": {{ "packageName": "t", "def": {{ "modules": {{ "m": {{ "Public": {body} }} }} }} }} }}"""

/// Places a kit input where its node belongs, using only the public readers.
let private readerFor (case: KitCase) : string -> Result<obj, V4JsonError> =
    let node =
        case.Tags
        |> List.tryPick (fun tag ->
            if tag.StartsWith "@node:" then
                Some(tag.Substring 6)
            else
                None
        )

    match node with
    | Some "Value" -> value
    | Some "Type" -> typ
    | Some "Pattern" -> pattern
    | Some "Literal" -> fun json -> value $"""{{ "Literal": {json} }}"""
    | Some "Name" -> fun json -> value $"""{{ "Variable": {json} }}"""
    | Some "FQName" -> fun json -> value $"""{{ "Reference": {json} }}"""
    | Some "Path" -> fun json -> distribution $"""{{ "Specs": {{ "packageName": {json} }} }}"""
    | Some "FormatVersion" ->
        fun json ->
            document
                $"""{{ "formatVersion": {json}, "distribution": {{ "Library": {{ "packageName": "t" }} }} }}"""
    | Some "Distribution" -> document
    | Some "TypeSpecification" -> fun json -> specModule $"""{{ "types": {{ "t": {json} }} }}"""
    | Some "ValueSpecification" -> fun json -> specModule $"""{{ "values": {{ "v": {json} }} }}"""
    | Some "ModuleSpecification" -> specModule
    | Some "TypeDefinition" ->
        fun json -> defModule $"""{{ "types": {{ "t": {{ "Public": {json} }} }} }}"""
    | Some "ValueDefinition" ->
        fun json -> defModule $"""{{ "values": {{ "v": {{ "Public": {json} }} }} }}"""
    | Some "AccessControlledTypeDefinition" ->
        fun json -> defModule $"""{{ "types": {{ "t": {json} }} }}"""
    | Some "AccessControlledValueDefinition" ->
        fun json -> defModule $"""{{ "values": {{ "v": {json} }} }}"""
    | other -> failtestf "The kit case %s has a node with no reader: %A" case.Id other

/// Runs a kit case: every spelling it accepts reads to one value, and every refusal has its diagnostic.
let private run (case: KitCase) =
    let read = readerFor case

    let accepted =
        case.Steps
        |> List.choose (fun step ->
            match step with
            | Canonical json
            | Accepts json -> Some(json, read json)
            | AcceptsFile path ->
                Some(
                    path,
                    document (
                        File.ReadAllText(Path.Combine(fixtureRoot, "v4", Path.GetFileName path))
                    )
                )
            | ReadsAsList _
            | Rejects _ -> None
        )

    let values =
        accepted
        |> List.map (fun (json, result) ->
            match result with
            | Ok read -> read
            | Error error -> failtestf "%s should be accepted, got %A" json error
        )

    match values with
    | [] -> ()
    | first :: rest ->
        for other in rest do
            Expect.equal other first "Every accepted spelling of a case reads to one value"

    for step in case.Steps do
        match step with
        | Rejects(json, diagnostic) ->
            match read json with
            | Ok result -> failtestf "%s should be rejected with %s, read %A" json diagnostic result
            | Error error ->
                Expect.equal error.Rule diagnostic $"The diagnostic for {json}"
                Expect.isNotEmpty error.Location "A refusal has a location"
        | ReadsAsList json ->
            match read json with
            | Ok result ->
                match result with
                | :? Value as read ->
                    match read with
                    | Value.List _ -> ()
                    | other -> failtestf "%s should read as a List, read %A" json other
                | other -> failtestf "%s should read as a value, read %A" json other
            | Error error -> failtestf "%s should read as a List, got %A" json error
        | Canonical _
        | Accepts _
        | AcceptsFile _ -> ()

let private features = [
    "names.feature"
    "values.feature"
    "types.feature"
    "patterns-and-literals.feature"
    "definitions.feature"
    "distributions.feature"
]

/// The rows of the provenance table that this group's vendored files must appear in.
let private provenanceRows () =
    let text = File.ReadAllText(Path.Combine(fixtureRoot, "PROVENANCE.md"))

    [
        for row in Regex.Matches(text, @"(?m)^\| `([^`]+)` \| `[^`]+` \| `([0-9a-f]{64})` \|") ->
            row.Groups[1].Value, row.Groups[2].Value
    ]
    |> Map.ofList

let private sha256 (file: string) =
    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes file))

[<Tests>]
let tests =
    testList "IR v4 kit" [
        testList "Kit spellings" [
            for feature in features do
                testList feature [
                    for case in casesOf feature do
                        testCase $"{case.Id} {case.Title}"
                        <| fun () -> run case
                ]
        ]

        testCase "The parser finds every accepted JSON input in values.feature"
        <| fun () ->
            let text = File.ReadAllText(Path.Combine(fixtureRoot, "kit", "values.feature"))
            let inText = Regex.Matches(text, @"a reader of JSON (accepts|reads) ").Count

            let parsed =
                casesOf "values.feature"
                |> List.collect (fun case -> case.Steps)
                |> List.filter (fun step ->
                    match step with
                    | Accepts _
                    | ReadsAsList _ -> true
                    | Canonical _
                    | Rejects _
                    | AcceptsFile _ -> false
                )
                |> List.length

            Expect.isGreaterThan inText 0 "values.feature has accepted spellings"
            Expect.equal parsed inText "No accepted spelling is lost"

        testCase "The vendored v4, kit and draft files match their recorded hashes"
        <| fun () ->
            let rows = provenanceRows ()

            let files = [
                yield "ion-draft.md"
                for folder in [ "v4"; "kit" ] do
                    for file in Directory.GetFiles(Path.Combine(fixtureRoot, folder)) do
                        yield $"{folder}/{Path.GetFileName file}"
            ]

            Expect.isGreaterThan files.Length 8 "The vendored files are found"

            for file in files do
                match rows.TryFind file with
                | Some expected ->
                    Expect.equal (sha256 (Path.Combine(fixtureRoot, file))) expected file
                | None -> failtestf "%s has no row in PROVENANCE.md" file
    ]
