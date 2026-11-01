// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad

open System
open Myriad.Core
open Morphir.CodeGeneration.FSharp
open Morphir.IR.Pipeline

/// Converts one selected module from a classic Morphir library into F# source.
[<MyriadGenerator("morphir-fsharp")>]
type MorphirFSharpGenerator() =
    interface IMyriadGenerator with
        member _.ValidInputExtensions = seq { ".json" }

        member _.Generate(context: GeneratorContext) =
            let selected =
                match context.AdditionalParameters.TryGetValue("MorphirModule") with
                | true, value when not (String.IsNullOrWhiteSpace value) -> value
                | _ ->
                    invalidArg
                        "MorphirModule"
                        "Select a module using the MorphirModule additional parameter."

            let tree = Transpiler.generate (VFile.fromDisk context.InputFilename)

            if VFileTree.hasErrors tree then
                let message =
                    VFileTree.collectMessages tree
                    |> List.filter (fun message ->
                        message.Severity = MessageSeverity.Error
                        || message.Severity = MessageSeverity.Fatal)
                    |> List.map VMessage.describe
                    |> String.concat Environment.NewLine

                Console.Error.WriteLine message
                invalidOp message
            else
                let entry =
                    VFileTree.tryGet Transpiler.manifest tree
                    |> Option.defaultValue []
                    |> List.tryFind (fun entry -> entry.ModulePath = selected)

                match entry |> Option.bind (fun entry -> VFileTree.findFile entry.Location tree) with
                | None ->
                    invalidArg
                        "MorphirModule"
                        $"Module '{selected}' does not exist in '{context.InputFilename}'."
                | Some file ->
                    match VFile.readText file with
                    | Result.Ok source -> Output.Source source
                    | Result.Error error -> invalidOp (ContentError.describe error)
