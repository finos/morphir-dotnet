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
          "result", "morphirup-result-0.1.0-draft.1.schema.json"
          "workflow-case", "toolchain-workflow-case-0.1.0-draft.1.schema.json" ]

/// <summary>A schema's text with every pattern anchored as ECMA-262 anchors it: a final <c>$</c> without the m flag
/// matches only at the end, where .NET's, which JsonSchema.Net uses, also matches before a final line feed. So the
/// final unescaped <c>$</c> of each pattern becomes <c>\z</c>.</summary>
let ecmaAnchored (text: string) =
    let root = Nodes.JsonNode.Parse text |> nonNull

    let rec walk (node: Nodes.JsonNode | null) =
        match node with
        | :? Nodes.JsonObject as o ->
            for KeyValue(name, child) in List.ofSeq o do
                match child with
                | :? Nodes.JsonValue as v when name = "pattern" ->
                    let pattern = v.GetValue<string>()

                    if pattern.EndsWith "$" && not (pattern.EndsWith @"\$") then
                        o[name] <- Nodes.JsonValue.Create(pattern.Substring(0, pattern.Length - 1) + @"\z")
                | _ -> walk child
        | :? Nodes.JsonArray as a ->
            for child in List.ofSeq a do
                walk child
        | _ -> ()

    walk root
    root.ToJsonString()

let private schema (contract: string) =
    let text = File.ReadAllText(Path.Combine(Fixtures.schemas, schemaFiles[contract]))
    JsonSchema.FromText(ecmaAnchored text, BuildOptions(SchemaRegistry = SchemaRegistry()))

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

/// <summary>The schemas' shared <c>$defs.mepVersion</c>, read from the lock schema.</summary>
let private mepVersionDefinition () =
    use document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures.schemas, schemaFiles["lock"])))
    let definition = document.RootElement.GetProperty("$defs").GetProperty "mepVersion"
    nonNull (definition.GetProperty("pattern").GetString()), definition.GetProperty("maxLength").GetInt32()

/// <summary>Whether a string meets <c>$defs.mepVersion</c> as JSON Schema defines it: an ECMA-262 pattern, whose
/// final <c>$</c> without the m flag matches only at the end, unlike .NET's, which also matches before a final line
/// feed; and <c>maxLength</c> counted in code points.</summary>
let meetsMepVersion (text: string) =
    let pattern, maxLength = mepVersionDefinition ()

    let anchored =
        if pattern.EndsWith "$" then pattern.Substring(0, pattern.Length - 1) + @"\z" else pattern

    System.Text.RegularExpressions.Regex.IsMatch(text, anchored, System.Text.RegularExpressions.RegexOptions.CultureInvariant)
    && Seq.length (text.EnumerateRunes() |> Seq.cast<System.Text.Rune>) <= maxLength

[<Tests>]
let tests =
    let cases = documents ()

    testList "JSON Schemas" [
        testList "a mep-version string meets $defs.mepVersion exactly when it is valid" [
            // index.md: a refused string the pattern accepts would say so with "schema": "accepts"; no case does.
            for case in bindings () do
                match case with
                | MepVersion(name, version, expected) ->
                    yield testCase name (fun () -> Expect.equal (meetsMepVersion version) (expected = Valid) name)
                | _ -> ()
        ]

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

/// <summary>What the workflow case states about its files and steps that does not hold: each source, expected IR and
/// expected generated file has the SHA-256 the case states, and each generated file its byte count; and each step's
/// operation is <c>compile</c> or <c>generate</c>, the two the readiness workflow page defines.</summary>
let workflowProblems (case: JsonElement) =
    let digest (file: string) =
        Fixtures.bytesIn Fixtures.workflow file |> System.Security.Cryptography.SHA256.HashData |> System.Convert.ToHexStringLower

    [ for source in case.GetProperty("sources").EnumerateArray() do
          let file = nonNull (source.GetProperty("file").GetString())

          if digest file <> nonNull (source.GetProperty("sha256").GetString()) then
              yield $"{file} does not have its stated SHA-256"

      for step in case.GetProperty("steps").EnumerateArray() do
          let id = nonNull (step.GetProperty("id").GetString())

          match nonNull (step.GetProperty("operation").GetString()) with
          | "compile"
          | "generate" -> ()
          | other -> yield $"step {id} has the operation {other}, which the workflow does not define"

          let expected = step.GetProperty "expected"

          match expected.TryGetProperty "ir" with
          | true, ir ->
              let file = nonNull (ir.GetProperty("file").GetString())

              if digest file <> nonNull (ir.GetProperty("sha256").GetString()) then
                  yield $"{file} does not have its stated SHA-256"
          | false, _ -> ()

          match expected.TryGetProperty "sets" with
          | true, sets ->
              for set in sets.EnumerateArray() do
                  for entry in set.GetProperty("files").EnumerateArray() do
                      let file = nonNull (entry.GetProperty("file").GetString())
                      let bytes = Fixtures.bytesIn Fixtures.workflow file

                      if int64 bytes.Length <> entry.GetProperty("bytes").GetInt64() then
                          yield $"{file} does not have its stated byte count"

                      if digest file <> nonNull (entry.GetProperty("sha256").GetString()) then
                          yield $"{file} does not have its stated SHA-256"
          | false, _ -> () ]

let private caseValue () =
    match Syntax.parseJson (File.ReadAllBytes(Path.Combine(Fixtures.workflow, "case.json"))) with
    | Ok value -> value
    | Error _ -> failtest "case.json does not parse"

/// The case with the reviewer's made-up step appended.
let private withMadeUpStep () =
    match caseValue () with
    | Value.Object members ->
        Value.Object(
            members
            |> List.map (fun (name, value) ->
                match name, value with
                | "steps", Value.Array steps ->
                    name,
                    Value.Array(
                        steps
                        @ [ Value.Object
                                [ "id", Value.String "made-up"
                                  "operation", Value.String "made-up-operation"
                                  "providers", Value.Array []
                                  "expected", Value.Object [ "outcome", Value.String "success-without-error-diagnostics" ] ] ]
                    )
                | _ -> name, value)
        )
    | _ -> failtest "case.json is not an object"

[<Tests>]
let workflowCaseTests =
    testList "The readiness workflow case" [
        testCase "case.json validates against the pinned workflow case schema" (fun () -> expectValid "workflow-case" "case.json" (caseValue ()))

        testCase "every file the case states has its stated SHA-256 and byte count, and every step a defined operation" (fun () ->
            use case = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures.workflow, "case.json")))
            Expect.isEmpty (workflowProblems case.RootElement) "no problems")

        testCase "a made-up step is refused by the schema and by the case checks" (fun () ->
            let edited = withMadeUpStep ()
            let valid, _ = validates "workflow-case" edited
            Expect.isFalse valid "the schema refuses the made-up step"
            use case = JsonDocument.Parse(JsonText.toBytes edited)
            Expect.contains (workflowProblems case.RootElement) "step made-up has the operation made-up-operation, which the workflow does not define" "the case check refuses it")

        testCase "a generated file whose stated size is wrong is found" (fun () ->
            use case = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.workflow, "case.json")).Replace("\"bytes\": 112", "\"bytes\": 113"))
            Expect.contains (workflowProblems case.RootElement) "expected/generated/Pricing.fs does not have its stated byte count" "the size")

        testCase "ECMA-262 anchoring refuses a trailing line feed the .NET pattern accepts" (fun () ->
            let schema = """{"type":"string","pattern":"^a$"}"""
            let evaluate (text: string) (instance: string) =
                use document = JsonDocument.Parse(JsonSerializer.Serialize instance)
                (JsonSchema.FromText text).Evaluate(document.RootElement).IsValid

            Expect.isTrue (evaluate schema "a\n") ".NET's $ accepts a final line feed"
            Expect.isFalse (evaluate (ecmaAnchored schema) "a\n") "anchored, it is refused"
            Expect.isTrue (evaluate (ecmaAnchored schema) "a") "and the value itself still matches")
    ]
