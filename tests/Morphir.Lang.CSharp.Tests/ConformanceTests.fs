// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.ConformanceTests

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.Conformance
open Morphir.Lang.CSharp

/// The product directory of the kit.
let private conformanceRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "conformance"))

let private options = {
    Suite = "frontend-csharp"
    VocabularyVersion = "1.4"
    IrVersions = [ "4" ]
    InputKinds = [ "files"; "directory"; "project file" ]
}

/// The shipped corpus, read once. CliAdapterTests uses it too.
let suite =
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
                  |> List.map (fun issue -> $"{issue.Path}:{issue.Line}: {issue.Code}: {issue.Message}")
                  |> String.concat "\n"))

let private identity =
    let assembly = CSharpFrontend.registration.GetType().Assembly
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

let private writers = WriterRegistry.Create [ V4IrWriter() :> IIrWriter ]

/// The library adapter that binds the in-process C# frontend.
let private adapter = FrontendAdapter.create identity CSharpFrontend.registration writers

/// The report of the library adapter. CliAdapterTests compares the CLI output with it.
let report =
    lazy
        ((Runner.run suite.Value adapter CancellationToken.None).GetAwaiter().GetResult())

let private describe (result: CaseResult) =
    let observed =
        match result.Observation with
        | None -> "no observation"
        | Some observation ->
            observation.Diagnostics
            |> List.map (fun key ->
                $"{key.Severity} {key.Code} {key.Uri}:{key.StartLine}:{key.StartCharacter}-{key.EndLine}:{key.EndCharacter}")
            |> String.concat "; "
            |> fun text -> $"success={observation.Success} [{text}]"

    $"{result.Case.Id} ({result.Case.Input.InputKind}, {result.Case.Input.IrVersion}): {result.Verdict} {observed}"

[<Tests>]
let tests =
    testSequenced
    <| testList
        "C# corpus through the library adapter"
        [ test "every case of the corpus passes for every input kind" {
              let failures =
                  report.Value.Results
                  |> List.filter (fun result ->
                      match result.Verdict with
                      | Verdict.Failed _ -> true
                      | _ -> false)

              if not failures.IsEmpty then
                  failtestf "%d case(s) fail:\n%s" failures.Length (failures |> List.map describe |> String.concat "\n")

              Expect.isTrue report.Value.Qualified "The in-process C# frontend qualifies"
          }

          test "the corpus runs the three input kinds for the current IR" {
              let inputs = report.Value.Results |> List.map (fun r -> r.Case.Input.InputKind) |> List.distinct |> List.sort
              Expect.equal inputs [ "directory"; "files"; "project file" ] "input kinds"

              let versions = report.Value.Results |> List.map (fun r -> r.Case.Input.IrVersion) |> List.distinct
              Expect.equal versions [ "4" ] "the current IR only"
          }

          test "every admission rule that the frontend can report has one refusal case" {
              let covered =
                  suite.Value.Cases
                  |> List.filter (fun case -> not case.Expected.Success)
                  |> List.collect (fun case -> case.Expected.Diagnostics |> List.map (fun d -> d.Code))
                  |> Set.ofList

              let rules =
                  Morphir.Lang.CSharp.Analyzers.Rules.All
                  // The rule that needs a second assembly cannot be shown to the frontend: its fixed references cannot bind it.
                  |> Seq.filter (fun rule -> rule.Id <> Morphir.Lang.CSharp.Analyzers.Rules.OutsideReference.Id)
                  |> Seq.map (fun rule -> rule.Code)
                  |> Set.ofSeq

              Expect.equal (Set.difference rules covered) Set.empty "no rule is without a case"
          }

          test "two runs give byte-identical reports" {
              let again = (Runner.run suite.Value adapter CancellationToken.None).GetAwaiter().GetResult()
              Expect.sequenceEqual (Report.toUtf8 again) (Report.toUtf8 report.Value) "the report is deterministic"
          } ]
    |> testLabel "PlatformSpecific"
