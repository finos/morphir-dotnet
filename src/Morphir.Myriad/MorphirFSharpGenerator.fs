// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad

open System
open System.IO
open Myriad.Core
open Morphir.CodeGeneration.FSharp

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

            match
                Transpiler.Generate(context.InputFilename, File.ReadAllText context.InputFilename)
            with
            | Error diagnostics ->
                let message =
                    diagnostics
                    |> List.map (fun d ->
                        $"{d.InputPath}: {d.Category} at {d.Location}: {d.Message}"
                    )
                    |> String.concat Environment.NewLine

                Console.Error.WriteLine message
                invalidOp message
            | Ok modules ->
                match
                    modules
                    |> List.tryFind (fun m -> m.ModulePath = selected)
                with
                | Some m -> Output.Source m.Source
                | None ->
                    invalidArg
                        "MorphirModule"
                        $"Module '{selected}' does not exist in '{context.InputFilename}'."
