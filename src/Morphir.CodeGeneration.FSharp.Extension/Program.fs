// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.CodeGeneration.FSharp.Extension.Program

open System
open System.Threading
open Morphir.Mep

/// Serves one MEP session on stdin and stdout. Console.Out goes to stderr before the generator runs, so that no text
/// from the generator or a library can corrupt a frame on stdout.
[<EntryPoint>]
let main _ =
    let output = Console.OpenStandardOutput()
    let input = Console.OpenStandardInput()
    Console.SetOut Console.Error

    use cancellation = new CancellationTokenSource()

    Console.CancelKeyPress.Add(fun args ->
        args.Cancel <- true
        cancellation.Cancel())

    let guest = FSharpGuest.registration ()

    match guest.RunAsync(input, output, cancellation.Token).GetAwaiter().GetResult() with
    | ConnectionEnd.FramingFailed failure ->
        eprintfn "morphir-fsharp-backend: %s" (failure.ToString())
        2
    | _ -> 0
