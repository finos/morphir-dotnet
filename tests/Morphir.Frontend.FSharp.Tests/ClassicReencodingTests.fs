// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Classic re-encoding (MEP-3): a classic model compiled at 3.0.0, read into its typed form and written at
/// classic version 1, 2 or 3, gives the same bytes as the in-process compile at that version. The MEP host uses
/// this path for versions 1 and 2, which have no tuple. A build with no IR v4 support gives the same classic 3
/// output as a build with it.
module Morphir.Frontend.FSharp.Tests.ClassicReencodingTests

open System.Text
open System.Text.Json
open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.Conformance
open Morphir.Frontend.FSharp
open Morphir.IR
open Morphir.IR.Model
open Morphir.Json.Classic

let private observe (input: CompileInput) =
    match (CorpusTests.adapter.Observe input CancellationToken.None).GetAwaiter().GetResult() with
    | Ok { ModelJson = Some json } -> json
    | other -> failtest $"The case compiles: {other}"

let private reencode (json: string) (version: int) =
    use document = JsonDocument.Parse json

    match ClassicJson.readTypedDistribution document.RootElement with
    | Error error -> failtest $"The typed reader refuses the model: {error}"
    | Ok typed ->
        let target = { Family = ModelFamily.Classic; Version = Versioning.Classic version }

        match Checked.tryValue ((ClassicIrWriter() :> IIrWriter).Write(target, WriteOptions.Compact, FamilyModel.Classic typed.Distribution)) with
        | Some bytes -> Encoding.UTF8.GetString bytes
        | None -> failtest "The classic writer refuses the typed model"

[<Tests>]
let tests =
    testSequenced
    <| testList "Classic re-encoding (MEP-3)" [
        test "A model compiled at classic 3 and written at 1, 2 or 3 equals the in-process compile at that version" {
            let cases =
                CorpusTests.suite.Value.Cases
                |> List.filter (fun case ->
                    case.Expected.Success
                    && case.Exclusion.IsNone
                    && case.Input.InputKind = "files"
                    && case.Input.IrVersion = "3"
                )

            Expect.isNonEmpty cases "The corpus has successful classic cases"

            for case in cases do
                let three = observe case.Input

                for version in [ 1; 2; 3 ] do
                    let expected = observe { case.Input with IrVersion = string version }
                    Expect.equal (reencode three version) expected $"{case.Id}: classic {version}"
        }

        test "Classic output without v4: a build with only the classic builder and writer equals the full build at classic 3" {
            let classicWriters = WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter ]

            let classicOnly =
                FSharpFrontend.create (FamilyBuilderRegistry.Create [ ClassicFamilyBuilder() :> IFamilyBuilder ]) classicWriters

            Expect.isFalse
                (classicOnly.Capability.Outputs |> Seq.exists (fun output -> output.Family = ModelFamily.V4))
                "The build has no IR v4 support"

            let identity = { Name = "morphir-frontend-fsharp"; Version = "test"; Sha256 = String.replicate 64 "e" }
            let withoutV4 = FrontendAdapter.create identity classicOnly classicWriters

            let full =
                FrontendAdapter.create identity FSharpFrontend.registration (WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ])

            let observeWith (adapter: Adapter) (input: CompileInput) =
                match (adapter.Observe input CancellationToken.None).GetAwaiter().GetResult() with
                | Ok observation -> observation
                | Error failure -> failtest $"The case is observed: {failure}"

            let cases =
                CorpusTests.suite.Value.Cases
                |> List.filter (fun case ->
                    case.Expected.Success
                    && case.Exclusion.IsNone
                    && case.Input.InputKind = "files"
                    && case.Input.IrVersion = "3"
                )

            Expect.isNonEmpty cases "The corpus has successful classic 3 cases"

            for case in cases do
                let actual = observeWith withoutV4 case.Input
                let expected = observeWith full case.Input
                Expect.isTrue actual.Success $"{case.Id}: the compile without v4 succeeds"
                Expect.isSome actual.ModelJson $"{case.Id}: the compile without v4 writes IR"
                Expect.equal actual.ModelJson expected.ModelJson $"{case.Id}: the classic 3 bytes"
                Expect.equal actual.Diagnostics expected.Diagnostics $"{case.Id}: the diagnostics"
        }
    ]
