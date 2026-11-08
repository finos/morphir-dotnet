// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.CorpusTests

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.Conformance
open Morphir.Frontend.FSharp
open Morphir.Frontend.FSharp.Analysis

/// The product directory of the kit.
let private conformanceRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "conformance"))

let private options = {
    Suite = "frontend-fsharp"
    VocabularyVersion = "1.0"
    IrVersions = [ "1"; "2"; "3"; "4" ]
    InputKinds = [ "files"; "directory"; "project file" ]
}

let private suite =
    lazy
        (let artifacts = [
            for file in Directory.GetFiles(conformanceRoot, "*", SearchOption.AllDirectories) ->
                {
                    Path = Path.GetRelativePath(conformanceRoot, file).Replace('\\', '/')
                    Bytes = File.ReadAllBytes file
                }
         ]

         match Corpus.read options artifacts with
         | Ok suite -> suite
         | Error issues ->
             failtestf
                 "The corpus is not valid:\n%s"
                 (issues
                  |> List.map (fun issue ->
                      $"{issue.Path}:{issue.Line}: {issue.Code}: {issue.Message}"
                  )
                  |> String.concat "\n"))

/// The library assembly that implements the frontend, with its version and SHA-256.
let private identity =
    let assembly = FSharpFrontend.registration.GetType().Assembly
    let name = assembly.GetName()

    let version =
        match assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> string name.Version
        | attribute -> attribute.InformationalVersion

    {
        Name = string name.Name
        Version = version
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes assembly.Location))
    }

let private writers =
    WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ]

let private run () =
    let adapter = FrontendAdapter.create identity FSharpFrontend.registration writers

    (Runner.run suite.Value adapter CancellationToken.None).GetAwaiter().GetResult()

let private report = lazy (run ())

let private describe (result: CaseResult) =
    let observed =
        match result.Observation with
        | None -> "no observation"
        | Some observation ->
            observation.Diagnostics
            |> List.map (fun key ->
                $"{key.Severity} {key.Code} {key.Uri}:{key.StartLine}:{key.StartCharacter}-{key.EndLine}:{key.EndCharacter}"
            )
            |> String.concat "; "
            |> fun text -> $"success={observation.Success} [{text}]"

    $"{result.Case.Id} ({result.Case.Input.InputKind}, {result.Case.Input.IrVersion}): {result.Verdict} {observed}"

[<Tests>]
let tests =
    testSequenced
    <| testList "F# corpus through the library adapter (MCK-1)" [
        test "Every case of the corpus passes for IR versions 1 to 4 and every input kind" {
            let failures =
                report.Value.Results
                |> List.filter (fun result ->
                    match result.Verdict with
                    | Verdict.Failed _ -> true
                    | _ -> false
                )

            if not failures.IsEmpty then
                failtestf
                    "%d case(s) fail:\n%s"
                    failures.Length
                    (failures
                     |> List.map describe
                     |> String.concat "\n")

            Expect.isTrue report.Value.Qualified "The in-process F# frontend qualifies"

            Expect.equal
                (report.Value.Results
                 |> List.map (fun result -> result.Case.Input.IrVersion)
                 |> List.distinct
                 |> List.sort)
                [ "1"; "2"; "3"; "4" ]
                "Every IR version runs"
        }

        test
            "Every catalog code has one refusal case, and every refusal case expects one catalog code" {
            let catalog =
                AdmissionCatalog.rules
                |> List.map (fun rule -> rule.Code)
                |> Set.ofList

            let refusals =
                suite.Value.Cases
                |> List.filter (fun case -> not case.Expected.Success)
                |> List.groupBy (fun case -> case.Id)

            for id, cases in refusals do
                for case in cases do
                    match case.Expected.Diagnostics with
                    | [ diagnostic ] ->
                        Expect.isTrue
                            (catalog.Contains diagnostic.Code)
                            $"{id} expects the catalog code {diagnostic.Code}"
                    | diagnostics ->
                        failtest $"{id} expects {diagnostics.Length} diagnostics, not one"

            let covered =
                refusals
                |> List.collect (fun (_, cases) ->
                    cases
                    |> List.collect (fun case ->
                        case.Expected.Diagnostics
                        |> List.map (fun diagnostic -> diagnostic.Code)
                    )
                )
                |> Set.ofList

            Expect.equal
                (Set.difference catalog covered)
                Set.empty
                "No catalog code is without a case"

            let perCode =
                refusals
                |> List.countBy (fun (_, cases) -> cases.Head.Expected.Diagnostics.Head.Code)
                |> List.filter (fun (_, count) -> count > 1)

            Expect.isEmpty perCode "One case per catalog code"
        }

        test "Two runs give byte-identical reports" {
            Expect.sequenceEqual
                (Report.toUtf8 (run ()))
                (Report.toUtf8 report.Value)
                "The report is deterministic"
        }
    ]
