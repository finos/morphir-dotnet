// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.MiddleEnd.Tests.DocumentationTests

open System
open System.IO
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4
open Morphir.MiddleEnd
open Morphir.MiddleEnd.Tests.ModelSupport

let private repositoryRoot =
    let rec up (directory: DirectoryInfo) =
        if File.Exists(Path.Combine(directory.FullName, "Morphir.slnx")) then directory.FullName
        else
            match directory.Parent with
            | null -> failwith "Morphir.slnx not found above the test source."
            | parent -> up parent
    up (DirectoryInfo __SOURCE_DIRECTORY__)

let private page = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "content", "docs", "pipeline", "middle-end.md")).Replace("\r\n", "\n")

/// The text between the markers of the example in the test source, as the page must show it.
let private exampleSource =
    let text = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "DocumentedRule.fs")).Replace("\r\n", "\n")
    let start = text.IndexOf("// <doc-example>\n") + "// <doc-example>\n".Length
    let finish = text.IndexOf("// </doc-example>")
    text.Substring(start, finish - start).TrimEnd()

[<Tests>]
let tests =
    testList "Middle-end documentation" [
        test "the page shows the example rule exactly as the test code has it" {
            Expect.isTrue (page.Contains exampleSource) "the F# block of the page is the text of DocumentedRule.fs between its markers"
        }

        test "the documented rule passes the catalog check" {
            Expect.isTrue (RuleCatalog.check [ DocumentedRule.identityLambda ]).IsPassed "catalog"
        }

        test "the documented pass runs on a model: the body of net changes from Apply (Lambda x x) price to price" {
            let model =
                CurrentModel.ofDistribution (
                    library
                        [ "orders/pricing",
                          moduleOf
                              []
                              [ "net",
                                expression
                                    [ "price", intType ]
                                    intType
                                    (Value.Apply(
                                        va,
                                        Value.Lambda(va, AsPattern(va, WildcardPattern va, name "x"), Value.Variable(va, name "x")),
                                        Value.Variable(va, name "price")
                                    )) ] ]
                    )

            let input: PipelineInput =
                { Artifact = artifact; Format = format; Snapshot = snapshot; Upstream = [] }

            match PassRunner.run input [ DocumentedRule.pass ] model |> Checked.tryValue with
            | Some outcome ->
                Expect.equal outcome.Report.Applications.Length 1 "the log has one record"
                Expect.equal outcome.Report.Applications.Head.Rule DocumentedRule.identityLambda.Id "the rule"

                match outcome.Model.Distribution with
                | Library(_, _, definition) ->
                    let modul = (OrderedMap.toList definition.Modules |> List.head |> snd).Value
                    match (OrderedMap.tryFind (name "net") modul.Values |> Option.get).Value.Value.Body with
                    | ExpressionBody(_, body) -> Expect.equal body (Value.Variable(va, name "price")) "the body is price"
                    | _ -> failtest "expression body expected"
                | _ -> failtest "library expected"
            | None -> failtest "the pass failed"
        }
    ]
