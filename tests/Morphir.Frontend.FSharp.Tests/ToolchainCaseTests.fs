// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The compile step of the finos/morphir toolchain case fsharp-compile-generate, checked through the compatibility
/// kit: the case's source compiles with the in-process F# frontend (the library adapter) to the case's expected IR,
/// compared by the kit's runner. The case files are those of the pinned contract, under
/// tests/Fixtures/Toolchain/workflows (see tests/Fixtures/Toolchain/PROVENANCE.md). The package, the exposed modules,
/// the sources, the input kind, the IR version and the digests come from the pinned case.json, not from this file.
module Morphir.Frontend.FSharp.Tests.ToolchainCaseTests

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.Conformance
open Morphir.Frontend.FSharp
open Morphir.IR

let private caseDirectory =
    let rec find (directory: DirectoryInfo | null) =
        match directory with
        | null -> failtest "The repository root has no Morphir.slnx"
        | found when File.Exists(Path.Combine(found.FullName, "Morphir.slnx")) -> found.FullName
        | found -> find found.Parent

    Path.Combine(find (DirectoryInfo AppContext.BaseDirectory), "tests", "Fixtures", "Toolchain", "workflows", "fsharp-compile-generate")

let private sha256 (path: string) =
    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes path))

/// A source of the case: its URI, its file under the case folder and the SHA-256 that case.json names.
type private CaseSource = { Uri: string; File: string; Sha256: string }

/// What the compile step of the case asks for, as case.json states it.
type private CompileCase =
    { Package: string
      ExposedModules: string list
      Sources: CaseSource list
      InputKind: string
      IrVersion: string
      ExpectedIr: string
      ExpectedIrSha256: string }

let private text (element: JsonElement) (name: string) =
    match element.GetProperty(name).GetString() with
    | null -> failtest $"case.json: {name} is a string"
    | value -> value

/// The compile step of a case.json document.
let private compileCase (json: string) : CompileCase =
    use document = JsonDocument.Parse json
    let root = document.RootElement
    let package = root.GetProperty "package"

    let step =
        root.GetProperty("steps").EnumerateArray()
        |> Seq.filter (fun step -> text step "operation" = "compile")
        |> Seq.exactlyOne

    let ir = step.GetProperty("expected").GetProperty "ir"

    { Package = text package "name"
      ExposedModules = [ for item in package.GetProperty("exposedModules").EnumerateArray() -> string (item.GetString()) ]
      Sources =
        [ for source in root.GetProperty("sources").EnumerateArray() ->
              { Uri = text source "uri"; File = text source "file"; Sha256 = text source "sha256" } ]
      InputKind = text step "input"
      IrVersion = string (ir.GetProperty("formatVersion").GetInt32())
      ExpectedIr = text ir "file"
      ExpectedIrSha256 = text ir "sha256" }

let private pinned () =
    compileCase (File.ReadAllText(Path.Combine(caseDirectory, "case.json")))

/// The kit's input for a compile case, with the case's source texts.
let private inputOf (case: CompileCase) =
    { Package = case.Package
      ExposedModules = case.ExposedModules
      Documents =
        [ for source in case.Sources ->
              { Uri = source.Uri
                Text = File.ReadAllText(Path.Combine(caseDirectory, source.File)) } ]
      InputKind = case.InputKind
      IrVersion = case.IrVersion }

[<Tests>]
let tests =
    testList "Toolchain case fsharp-compile-generate" [
        test "The fixture files are the case's: their SHA-256 are those that case.json names" {
            let case = pinned ()
            Expect.isNonEmpty case.Sources "the case has sources"

            for source in case.Sources do
                Expect.equal (sha256 (Path.Combine(caseDirectory, source.File))) source.Sha256 source.File

            Expect.equal (sha256 (Path.Combine(caseDirectory, case.ExpectedIr))) case.ExpectedIrSha256 case.ExpectedIr
        }

        test "The kit's input comes from case.json: another package, other modules or another IR version reach the input" {
            let json = File.ReadAllText(Path.Combine(caseDirectory, "case.json"))
            let case = compileCase json
            Expect.equal (inputOf case).Package case.Package "the package of case.json"

            let changedDocument = JsonNode.Parse json |> nonNull
            let package = changedDocument["package"] |> nonNull
            package["name"] <- JsonValue.Create "acme/changed"
            package["exposedModules"] <- JsonArray(JsonValue.Create "Changed" :> JsonNode)

            let compileStep =
                (changedDocument["steps"] |> nonNull).AsArray()
                |> Seq.map nonNull
                |> Seq.filter (fun step -> (step["operation"] |> nonNull).GetValue<string>() = "compile")
                |> Seq.exactlyOne

            let expected = compileStep["expected"] |> nonNull
            let ir = expected["ir"] |> nonNull
            ir["formatVersion"] <- JsonValue.Create 3
            let changed = compileCase (changedDocument.ToJsonString())

            let input = inputOf changed
            Expect.equal input.Package "acme/changed" "the package"
            Expect.equal input.ExposedModules [ "Changed" ] "the exposed modules"
            Expect.equal input.IrVersion "3" "the IR version"
        }

        test "Through the kit: the case's source compiles with the F# frontend to its expected IR" {
            let compile = pinned ()
            let input = inputOf compile
            let expectedIr = Path.Combine(caseDirectory, compile.ExpectedIr)

            let case = {
                Id = "fsharp-compile-generate"
                File = "case.json"
                Line = 1
                Input = input
                Expected = {
                    Success = true
                    Diagnostics = []
                    AllowNonErrorDiagnostics = false
                    ModelJson = Some(File.ReadAllText expectedIr)
                }
                Calls = []
                Exclusion = None
            }

            let suite = {
                Cases = [ case ]
                Sha256 = sha256 expectedIr
                VocabularyVersion = "1.1"
            }

            let identity = { Name = "morphir-frontend-fsharp"; Version = "test"; Sha256 = String.replicate 64 "f" }

            let adapter =
                FrontendAdapter.create identity FSharpFrontend.registration (WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ])

            let verdictOf (suite: CaseSet) =
                match (Runner.run suite adapter CancellationToken.None).GetAwaiter().GetResult().Results with
                | [ result ] -> result.Verdict
                | other -> failtest $"One result: {other}"

            Expect.equal (verdictOf suite) Passed "The kit's verdict"

            // A control: the same run against an expectation with one field renamed fails, so the pass compares.
            let tampered = {
                case with
                    Expected = {
                        case.Expected with
                            ModelJson = case.Expected.ModelJson |> Option.map (fun json -> json.Replace("\"seats\"", "\"seatz\""))
                    }
            }

            Expect.notEqual tampered.Expected.ModelJson case.Expected.ModelJson "the control changes the expectation"

            match verdictOf { suite with Cases = [ tampered ] } with
            | Failed _ -> ()
            | other -> failtest $"The kit fails a different model: {other}"
        }
    ]
    |> testLabel "PlatformSpecific"
