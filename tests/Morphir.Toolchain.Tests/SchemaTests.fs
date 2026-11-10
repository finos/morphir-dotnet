// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The pinned JSON Schemas, applied with JsonSchema.Net (test project only). They check the manifests' schema claims
/// and every writer's output, so a reader and writer that share a mistake cannot hide it behind each other.
/// </summary>
module Morphir.Toolchain.Tests.SchemaTests

open System.IO
open System.Text.Json
open Expecto
open Json.Schema
open Morphir.Toolchain
open Morphir.Toolchain.Tests.Manifest

let private schemaFiles =
    Map.ofList
        [ "request", "toolchain-request-0.1.0-draft.1.schema.json"
          "recipe", "toolset-recipe-0.1.0-draft.1.schema.json"
          "lock", "toolchain-lock-0.1.0-draft.1.schema.json"
          "receipt", "toolchain-receipt-0.1.0-draft.1.schema.json"
          "result", "morphirup-result-0.1.0-draft.1.schema.json" ]

let private schema (contract: string) =
    let text = File.ReadAllText(Path.Combine(Fixtures.schemas, schemaFiles[contract]))
    JsonSchema.FromText(text, BuildOptions(SchemaRegistry = SchemaRegistry()))

/// Whether a value validates, with the first failing location for the message.
let validates (contract: string) (value: Value) =
    use document = JsonDocument.Parse(JsonText.toBytes value)
    let results = (schema contract).Evaluate(document.RootElement, EvaluationOptions(OutputFormat = OutputFormat.List))

    let failure =
        (match results.Details with
         | null -> []
         | details -> List.ofSeq details)
        |> List.tryPick (fun detail ->
            match detail.Errors with
            | NonNull errors when not detail.IsValid && errors.Count > 0 -> Some $"{detail.InstanceLocation}: {Seq.head errors.Values}"
            | _ -> None)

    results.IsValid, defaultArg failure ""

let expectValid (contract: string) (what: string) (value: Value) =
    let valid, failure = validates contract value
    Expect.isTrue valid $"{what} validates against the {contract} schema: {failure}"

/// <summary>JSON as a schema validator sees it: a parser that keeps one of two members with the same name, the last.
/// The readers refuse a repeated name, so a case the schema accepts for that reason is validated this way.</summary>
let rec private keepingOne (element: JsonElement) : Value =
    match element.ValueKind with
    | JsonValueKind.Object ->
        let members = [ for p in element.EnumerateObject() -> p.Name, keepingOne p.Value ]
        let last name = members |> List.findBack (fst >> (=) name) |> snd
        Value.Object(members |> List.map fst |> List.distinct |> List.map (fun name -> name, last name))
    | JsonValueKind.Array -> Value.Array [ for item in element.EnumerateArray() -> keepingOne item ]
    | JsonValueKind.String -> Value.String(nonNull (element.GetString()))
    | JsonValueKind.Number -> Value.Number(element.GetRawText())
    | JsonValueKind.True -> Value.Boolean true
    | JsonValueKind.False -> Value.Boolean false
    | _ -> Value.Null

/// The value a fixture parses to: JSON, or the JSON value a TOML request parses to.
let private tryParsed (contract: string) (bytes: byte array) =
    if contract = "request" then Syntax.parseToml bytes else Syntax.parseJson bytes

let private parsed (contract: string) (bytes: byte array) =
    match tryParsed contract bytes with
    | Ok value -> value
    | Error _ -> failtest "the fixture does not parse"

/// The value a schema validates for a case the schema accepts: the strict parse, or for JSON with a repeated member
/// name, which the readers refuse, the value a parser that keeps one member gives.
let private accepted (contract: string) (bytes: byte array) =
    match tryParsed contract bytes with
    | Ok value -> value
    | Error _ when contract <> "request" ->
        use document = JsonDocument.Parse(bytes, JsonDocumentOptions(AllowDuplicateProperties = true))
        keepingOne document.RootElement
    | Error _ -> failtest "the fixture does not parse"

let private written (contract: string) (bytes: byte array) : Value =
    let fail (refusal: ReaderRefusal) = failtestf "refused: %A" refusal

    match contract with
    | "lock" -> LockDocument.read bytes |> Result.map LockDocument.value |> Result.defaultWith fail
    | "receipt" -> ReceiptDocument.read bytes |> Result.map ReceiptDocument.value |> Result.defaultWith fail
    | "result" -> ResultDocument.read bytes |> Result.map ResultDocument.value |> Result.defaultWith fail
    | other -> failtestf "no writer for %s" other

[<Tests>]
let tests =
    let cases = documents ()

    testList "JSON Schemas" [
        testList "the manifest's schema claims hold" [
            for case in cases do
                match case.Expected, case.Schema with
                | Valid, _
                | _, Some "accepts" ->
                    yield
                        testCase $"accepts {case.Document}" (fun () ->
                            expectValid case.Contract case.Document (accepted case.Contract (Fixtures.bytes case.Document)))
                | _, Some "rejects" ->
                    yield
                        testCase $"rejects {case.Document}" (fun () ->
                            let valid, _ = validates case.Contract (parsed case.Contract (Fixtures.bytes case.Document))
                            Expect.isFalse valid $"{case.Document} is refused by the {case.Contract} schema")
                | _, Some "unparsable" ->
                    // No schema applies to bytes that are not JSON or TOML at all, such as JSON with a byte order mark.
                    yield
                        testCase $"cannot parse {case.Document}" (fun () ->
                            Expect.isError (tryParsed case.Contract (Fixtures.bytes case.Document)) $"{case.Document} does not parse")
                | _, Some other -> failwithf "%s has a schema claim this suite does not understand: %s" case.Document other
                | _ -> ()
        ]

        testList "every writer's output validates" [
            for case in cases do
                if case.Expected = Valid && List.contains case.Contract [ "lock"; "receipt"; "result" ] then
                    yield
                        testCase $"written {case.Document}" (fun () ->
                            expectValid case.Contract case.Document (written case.Contract (Fixtures.bytes case.Document)))

            yield
                testCase "planned setup results and the locks they would write" (fun () ->
                    for request, platform in
                        [ "request/approved.toml", Platform.WinX64
                          "request/approved.toml", Platform.LinuxArm64
                          "request/team-platforms-external-backend.toml", Platform.OsxArm64
                          "request/exact-host-version.toml", Platform.LinuxX64 ] do
                        let digested, lock = PlanningTests.planned request platform

                        match ResultDocument.create Command.Setup Outcome.Planned (Some digested) None [] with
                        | Ok result -> expectValid "result" $"the plan of {request} on {platform}" (ResultDocument.value result)
                        | Error reason -> failtest reason

                        expectValid "lock" $"the lock of {request}" (LockDocument.value lock))

            yield
                testCase "command line results" (fun () ->
                    use directorySandbox = CliTests.tempDirectory ()
                    let directory = directorySandbox.Work

                    for args in
                        [ CliTests.planArgs directory "request/approved.toml"
                          CliTests.planArgs directory "request/negative/draft-2.toml"
                          CliTests.planArgs directory "request/negative/credential-member.toml"
                          CliTests.planArgs directory "request/approved.toml" @ [ "--expect-plan"; String.replicate 64 "0" ] ] do
                        for platform in [ Platform.WinX64; Platform.LinuxMuslArm64 ] do
                            let run = CliTests.runIn directory platform args

                            match Syntax.parseJson run.StdoutBytes with
                            | Ok value -> expectValid "result" (String.concat " " args) value
                            | Error _ -> failtestf "no result for %A" args)

            yield
                testCase "a request written from selector options" (fun () ->
                    expectValid "request" "the written request" (parsed "request" (Fixtures.bytes "request/approved.toml")))
        ]
    ]
