// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The .NET call checker of the MCK behavioral verdict. The F# backend generates F# from the
/// compiled model, and FSI runs each call observation against it. The round-trip check uses the
/// same generation.
module Morphir.Frontend.FSharp.Tests.GeneratedCalls

open System
open System.Collections.Concurrent
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Morphir.CodeGeneration.FSharp
open Morphir.Conformance
open Morphir.IR
open Morphir.IR.Model
open Morphir.Testing

/// One generated F# module, in the dependency order of the manifest.
type GeneratedSource = {
    File: string
    ModulePath: string
    Source: string
}

let private versioned (version: string) (json: string) : Result<VersionedModel, string> =
    let classic =
        match Versioning.parse version with
        | Ok(Versioning.Classic number) ->
            Ok(
                number
                <= 3
            )
        | Ok(Versioning.SemVer semantic) ->
            Ok(
                semantic.Major
                <= 3
            )
        | Error error -> Error $"Invalid IR version {version}: {error}"

    match classic, SourceFormatVersion.parse version with
    | Error message, _ -> Error message
    | _, Error error -> Error $"Invalid IR version {version}: {error}"
    | Ok isClassic, Ok sourceVersion ->
        use document = JsonDocument.Parse json

        if isClassic then
            Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement
            |> Result.map (fun read -> {
                SourceVersion = sourceVersion
                Distribution = DistributionModel.Classic read.Distribution
            })
            |> Result.mapError (fun error -> $"The classic reader refused the model: {error}")
        else
            Morphir.Json.V4.V4Json.readDistribution document.RootElement
            |> Result.map (fun read -> {
                SourceVersion = sourceVersion
                Distribution = DistributionModel.V4 read.Distribution
            })
            |> Result.mapError (fun error -> $"The v4 reader refused the model: {error}")

/// Generates F# from a compiled model with the F# backend.
let generate (version: string) (json: string) : Result<GeneratedSource list, string> =
    versioned version json
    |> Result.bind (fun model ->
        let tree =
            Transpiler.generateModel model (Morphir.IR.Pipeline.VFile.fromPath "model.json")

        if Morphir.IR.Pipeline.VFileTree.hasErrors tree then
            Morphir.IR.Pipeline.VFileTree.collectMessages tree
            |> List.map (fun message ->
                let rule = Option.defaultValue "" message.RuleId
                $"{rule}: {message.Message}"
            )
            |> String.concat "; "
            |> fun messages -> Error $"The F# backend refused the model: {messages}"
        else
            Morphir.IR.Pipeline.VFileTree.tryGet Transpiler.manifest tree
            |> Option.defaultValue []
            |> List.map (fun entry ->
                match
                    Morphir.IR.Pipeline.VFileTree.findFile entry.Location tree
                    |> Option.map Morphir.IR.Pipeline.VFile.readText
                with
                | Some(Ok source) ->
                    Ok
                        {
                            File = entry.Location
                            ModulePath = entry.ModulePath
                            Source = source
                        }
                | _ -> Error $"The generated module {entry.Location} cannot be read"
            )
            |> List.fold
                (fun sources item ->
                    match sources, item with
                    | Ok sources, Ok item ->
                        Ok(
                            sources
                            @ [ item ]
                        )
                    | Error message, _
                    | _, Error message -> Error message
                )
                (Ok [])
    )

let private codec =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "CallCodec.fsx"))

/// A verbatim F# string literal that holds the text.
let private literal (text: string) =
    "@\""
    + text.Replace("\"", "\"\"")
    + "\""

/// The F# name of a called value, such as Acme.Demo.Pricing.seats for "Pricing.seats" in the
/// package acme/demo. The module is the generated module of that path.
let private target (package: string) (sources: GeneratedSource list) (value: string) =
    let split = value.LastIndexOf '.'
    let modulePath = value.Substring(0, split)

    match
        CanonicalNames.parsePath package,
        sources
        |> List.exists (fun source -> source.ModulePath = modulePath)
    with
    | Error message, _ -> Error $"Invalid package name {package}: {message}"
    | Ok _, false -> Error $"The generated F# has no module {modulePath}"
    | Ok path, true ->
        Ok(
            Emit.packagePath (PackageName path)
            + "."
            + modulePath
            + "."
            + Emit.ident (value.Substring(split + 1))
        )

let private consumer
    (package: string)
    (sources: GeneratedSource list)
    (calls: CallExpectation list)
    =
    calls
    |> List.mapi (fun index call ->
        target package sources call.Value
        |> Result.map (fun name ->
            $"CallCodec.check {index} {name} {literal call.Arguments} {literal call.Result}"
        )
    )
    |> List.fold
        (fun lines line ->
            match lines, line with
            | Ok lines, Ok line ->
                Ok(
                    lines
                    @ [ line ]
                )
            | Error message, _
            | _, Error message -> Error message
        )
        (Ok [])
    |> Result.map (String.concat "\n")

/// Reads the lines of the codec, one for each call, and returns the first difference.
let private verdict (calls: CallExpectation list) (output: string) =
    let lines =
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun line -> line.TrimEnd '\r')
        |> Array.filter (fun line -> line.StartsWith("call ", StringComparison.Ordinal))

    if
        lines.Length
        <> calls.Length
    then
        Error $"Expected {calls.Length} call results from FSI, got {lines.Length}: {output}"
    else
        Array.zip (Array.ofList calls) lines
        |> Array.tryPick (fun (call, line) ->
            match line.Split(' ', 4) with
            | [| _; _; "ok" |] -> None
            | [| _; _; "differs"; actual |] ->
                Some
                    $"Calling {call.Value} with {call.Arguments} returned {actual}; expected {call.Result}"
            | _ -> Some $"Calling {call.Value} with {call.Arguments} failed: {line}"
        )
        |> function
            | Some difference -> Error difference
            | None -> Ok()

/// Runs FSI once for each distinct generated source and call list. Every input kind of one IR
/// family gives the same F#, so most case variants reuse an earlier run.
let private runs = ConcurrentDictionary<string, Lazy<Result<string, string>>>()

/// The call checker that the .NET runner uses for the behavioral verdict.
let checker: CallChecker = {
    Name = "morphir-dotnet F# backend + FSI"
    Check =
        fun input model calls cancellationToken ->
            Task.Run(
                (fun () ->
                    generate input.IrVersion model
                    |> Result.bind (fun sources ->
                        consumer input.Package sources calls
                        |> Result.bind (fun code ->
                            let modules =
                                ("CallCodec.fsx", codec)
                                :: [ for source in sources -> source.File, source.Source ]

                            let key =
                                String.concat "\u0000" [
                                    for name, text in modules do
                                        name
                                        text
                                    code
                                ]

                            runs
                                .GetOrAdd(
                                    key,
                                    fun _ ->
                                        lazy
                                            (GenerationCompiler.tryExecuteWithReferences
                                                []
                                                modules
                                                code)
                                )
                                .Value
                            |> Result.mapError (fun error ->
                                $"FSI could not run the generated F#: {error}"
                            )
                            |> Result.bind (verdict calls)
                        )
                    )
                ),
                cancellationToken
            )
}
