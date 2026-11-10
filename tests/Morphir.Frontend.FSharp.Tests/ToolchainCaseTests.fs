// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The compile step of the finos/morphir toolchain case fsharp-compile-generate, checked through the compatibility
/// kit: the case's source compiles with the in-process F# frontend (the library adapter) to the case's expected IR,
/// compared by the kit's runner. The case files are copied in tests/Fixtures/Toolchain (see PROVENANCE.md there).
module Morphir.Frontend.FSharp.Tests.ToolchainCaseTests

open System
open System.IO
open System.Security.Cryptography
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

    Path.Combine(find (DirectoryInfo AppContext.BaseDirectory), "tests", "Fixtures", "Toolchain", "fsharp-compile-generate")

let private sha256 (path: string) =
    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes path))

[<Tests>]
let tests =
    testList "Toolchain case fsharp-compile-generate" [
        test "The fixture files are the case's: their SHA-256 are those that case.json names" {
            Expect.equal
                (sha256 (Path.Combine(caseDirectory, "source", "Pricing.fs")))
                "f924e05d37b0517ac8777b495691ad86207f2b1dc7f5b6e038e6f6e370817d6c"
                "source/Pricing.fs"

            Expect.equal
                (sha256 (Path.Combine(caseDirectory, "expected", "ir-4.0.0.json")))
                "b1035d065ecfddc557476d7203afe29c83a9340abc27300aaeeb911490c30cb7"
                "expected/ir-4.0.0.json"
        }

        test "Through the kit: source/Pricing.fs compiles with the F# frontend to expected/ir-4.0.0.json" {
            let input = {
                Package = "acme/demo"
                ExposedModules = [ "Pricing" ]
                Documents = [
                    {
                        Uri = "Pricing.fs"
                        Text = File.ReadAllText(Path.Combine(caseDirectory, "source", "Pricing.fs"))
                    }
                ]
                InputKind = "files"
                IrVersion = "4"
            }

            let case = {
                Id = "fsharp-compile-generate"
                File = "case.json"
                Line = 1
                Input = input
                Expected = {
                    Success = true
                    Diagnostics = []
                    AllowNonErrorDiagnostics = false
                    ModelJson = Some(File.ReadAllText(Path.Combine(caseDirectory, "expected", "ir-4.0.0.json")))
                }
                Calls = []
                Exclusion = None
            }

            let suite = {
                Cases = [ case ]
                Sha256 = sha256 (Path.Combine(caseDirectory, "expected", "ir-4.0.0.json"))
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
