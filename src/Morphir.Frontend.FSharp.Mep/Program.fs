// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Mep.Program

open System
open System.Threading
open Morphir.Mep

/// Serves one MEP session on stdin and stdout. Console.Out goes to stderr before the F# compiler service starts, so
/// that no text from the compiler or a library can corrupt a frame on stdout.
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
        eprintfn "morphir-fsharp-frontend: %s" (failure.ToString())
        2
    | _ -> 0
