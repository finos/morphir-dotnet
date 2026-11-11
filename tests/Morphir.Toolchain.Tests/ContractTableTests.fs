// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The reader's tables equal the tables of the pinned pages and schemas, read from the files rather than copied.
module Morphir.Toolchain.Tests.ContractTableTests

open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open Morphir.Toolchain

let private page (name: string) = File.ReadAllText(Path.Combine(Fixtures.pages, name + ".md"))

/// The text of a page between a heading and the next heading of the same or a higher level.
let private section (text: string) (heading: string) =
    let start = text.IndexOf(heading + "\n")
    Expect.isGreaterThanOrEqual start 0 $"the page has the heading {heading}"
    let level = heading.IndexOf ' '
    let rest = text.Substring(start + heading.Length)
    let next = Regex.Match(rest, $"(?m)^#{{1,{level}}} ")
    if next.Success then rest.Substring(0, next.Index) else rest

let private schemaEnum (schema: string) (pointer: string list) =
    use document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures.schemas, schema)))
    let element = pointer |> List.fold (fun (e: JsonElement) name -> e.GetProperty name) document.RootElement
    [ for item in element.EnumerateArray() -> nonNull (item.GetString()) ]

let private normalized (text: string) = text.Replace("\r\n", "\n")

[<Tests>]
let tests =
    testList "Contract tables" [
        testCase "every stable code has the page's diagnostic kind, in the page's order" (fun () ->
            let table = section (normalized (page "index")) "## Stable codes"

            let rows =
                Regex.Matches(table, @"(?m)^\| `([a-z_]+)` \| `([a-z-]+)` \|")
                |> Seq.map (fun row -> row.Groups[1].Value, row.Groups[2].Value)
                |> Seq.toList

            Expect.equal (List.length rows) (List.length Code.all) "the page lists every code"

            let ours = Code.all |> List.map (fun (code, name) -> name, DiagnosticKind.name (Code.kind code))
            Expect.equal ours rows "the code-to-kind table")

        testCase "the platform order is the page's platform table" (fun () ->
            let table = section (normalized (page "index")) "### Platforms"

            let rows =
                Regex.Matches(table, @"(?m)^\| `([a-z0-9-]+)` \|") |> Seq.map (fun row -> row.Groups[1].Value) |> Seq.toList

            Expect.equal (Platform.all |> List.map snd) rows "platform table order")

        testCase "the delivery cases are the page's" (fun () ->
            let table = section (normalized (page "index")) "### Tagged cases"

            let rows =
                Regex.Matches(table, @"(?m)^\| `([a-z-]+)` \|")
                |> Seq.map (fun row -> row.Groups[1].Value)
                |> Seq.filter ((<>) "delivery")
                |> Seq.toList

            Expect.equal (Delivery.all |> List.map snd) rows "delivery cases")

        testCase "the credential names are the page's" (fun () ->
            let text = section (normalized (page "index")) "### Credentials"
            let sentence = Regex.Match(text, "equals one of (.*?), compared ASCII case-insensitively").Groups[1].Value
            let names = Regex.Matches(sentence, "`([^`]+)`") |> Seq.map (fun m -> m.Groups[1].Value) |> Seq.toList
            Expect.equal (names |> List.map (fun n -> n.ToLowerInvariant())) (Reading.credentialNames |> List.map (fun n -> n.ToLowerInvariant())) "credential names")

        testList "understood critical paths are exactly the schema's critical enum" [
            for schema, understood in
                [ "toolchain-request-0.1.0-draft.1.schema.json", RequestDocument.understoodCriticalPaths
                  "toolset-recipe-0.1.0-draft.1.schema.json", RecipeDocument.understoodCriticalPaths
                  "toolchain-lock-0.1.0-draft.1.schema.json", LockDocument.understoodCriticalPaths
                  "toolchain-receipt-0.1.0-draft.1.schema.json", ReceiptDocument.understoodCriticalPaths
                  "morphirup-result-0.1.0-draft.1.schema.json", ResultDocument.understoodCriticalPaths ] ->
                testCase schema (fun () ->
                    let listed = schemaEnum schema [ "properties"; "critical"; "items"; "enum" ]
                    Expect.equal (List.length listed) (List.length (List.distinct listed)) "the enum has no repeats"
                    Expect.equal understood (set listed) "understood paths")
        ]

        testCase "the result commands and diagnostic kinds are the schema's" (fun () ->
            let schema = "morphirup-result-0.1.0-draft.1.schema.json"
            Expect.equal (Command.all |> List.map snd) (schemaEnum schema [ "properties"; "command"; "enum" ]) "commands"

            Expect.equal
                (DiagnosticKind.all |> List.map snd)
                (schemaEnum schema [ "$defs"; "diagnostic"; "properties"; "kind"; "enum" ])
                "diagnostic kinds")

        testCase "every document schema pins schemaVersion to this reader's draft" (fun () ->
            // The six toolchain contract schemas the pin holds; a finos/morphir checkout's schema folder also holds the
            // schemas of every other contract.
            let names = [ "morphirup-result"; "toolchain-lock"; "toolchain-receipt"; "toolchain-request"; "toolchain-workflow-case"; "toolset-recipe" ]

            for schema in names |> List.map (fun name -> Path.Combine(Fixtures.schemas, $"{name}-0.1.0-draft.1.schema.json")) do
                use document = JsonDocument.Parse(File.ReadAllBytes schema)
                let pinned = document.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const")
                Expect.equal (pinned.GetString()) ContractVersion.current (Path.GetFileName schema))
    ]
