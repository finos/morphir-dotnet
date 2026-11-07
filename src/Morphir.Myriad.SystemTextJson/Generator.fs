// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson

open System
open System.IO
open Myriad.Core
open Morphir.Myriad.SystemTextJson.Generator

/// Generates System.Text.Json converters and a JsonSerializerContext for the F# types in one input file.
/// The FSharpJsonContext item passes the context settings as Myriad additional parameters.
[<MyriadGenerator("morphir-system-text-json")>]
type MorphirSystemTextJsonGenerator() =
    interface IMyriadGenerator with
        member _.ValidInputExtensions = seq { ".fs" }

        member _.Generate(context: GeneratorContext) =
            let result =
                ContextSettings.parse context.AdditionalParameters
                |> Result.bind (fun settings -> Generation.generate settings context.InputFilename (File.ReadAllText context.InputFilename))

            match result with
            | Ok code -> Output.Source code
            | Error errors ->
                let text = errors |> List.map GenerationError.describe

                // The MSBuild task reads stderr: one error on each line.
                for line in text do
                    Console.Error.WriteLine("error MSTJ0001: " + line)

                invalidOp (String.Join(Environment.NewLine, text))
