// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Scripted guests for host tests. The test executable runs one when it starts with --fake-guest <scenario>.
module Morphir.Compilation.Mep.Tests.FakeGuests

open System
open System.Text
open System.Threading
open System.Threading.Tasks
open Morphir.Frontend.FSharp.Extension
open Morphir.Mep

/// A guest that selects only the 4.0.0 compileModel tuple. Its compileModel handler writes a line to stderr, so a
/// test can see whether the host sent a request.
let private onlyV4 () =
    let support =
        { FSharpGuest.support with
            Tuples = [| FSharpGuest.tuple "4.0.0" |] }

    let handler =
        Handlers.raw (fun _ _ ->
            eprintfn "fake-guest: compileModel was sent"
            Task.FromResult(Error(RpcErrors.create ErrorCode.InternalError)))

    Guest(
        { Claims = FSharpGuest.claims
          Capabilities =
            { FSharpGuest.capabilities with
                ModelTransport = Some support }
          Methods = readOnlyDict [ Method.CompileModel.WireValue, handler ] }
    )

/// A guest that supports only an Ion compileModel tuple, so it selects none of the host's JSON tuples and the
/// agreement is empty. Its compileModel handler writes a line to stderr, so a test can see whether the host sent a
/// request.
let private ionOnly () =
    let support =
        { FSharpGuest.support with
            Tuples =
                [| { FSharpGuest.tuple "4.0.0" with
                       Profile = "morphir-model"
                       Codec = ModelCodec.IonBinary } |] }

    let handler =
        Handlers.raw (fun _ _ ->
            eprintfn "fake-guest: ion-only compileModel was sent"
            Task.FromResult(Error(RpcErrors.create ErrorCode.InternalError)))

    Guest(
        { Claims = FSharpGuest.claims
          Capabilities =
            { FSharpGuest.capabilities with
                ModelTransport = Some support }
          Methods = readOnlyDict [ Method.CompileModel.WireValue, handler ] }
    )

/// A guest that writes a log line to stdout, outside a frame, when it gets compileModel.
let private stdoutLog (output: IO.Stream) =
    let handler =
        Handlers.raw (fun _ _ ->
            let line = Encoding.UTF8.GetBytes "compiling Pricing.fs\n"
            output.Write(line, 0, line.Length)
            output.Flush()
            Task.FromResult(Error(RpcErrors.create ErrorCode.InternalError)))

    Guest(
        { Claims = FSharpGuest.claims
          Capabilities = FSharpGuest.capabilities
          Methods = readOnlyDict [ Method.CompileModel.WireValue, handler ] }
    )

/// A guest that never answers compileModel and claims no cancellation, so it also ignores $/cancelRequest.
let private neverAnswers () =
    let handler =
        Handlers.raw (fun _ _ -> TaskCompletionSource<Result<Text.Json.JsonElement, RpcError>>().Task)

    Guest(
        { Claims = FSharpGuest.claims
          Capabilities = FSharpGuest.capabilities
          Methods = readOnlyDict [ Method.CompileModel.WireValue, handler ] }
    )

/// A guest whose compileModel handler blocks synchronously before it returns its task. The guest dispatches on its
/// read loop, so it never reads shutdown or exit either. It writes its process id to stderr first.
let private blocksReadLoop () =
    eprintfn "fake-guest-pid=%d" Environment.ProcessId

    let handler =
        Handlers.raw (fun _ _ ->
            Thread.Sleep Timeout.Infinite
            TaskCompletionSource<Result<Text.Json.JsonElement, RpcError>>().Task)

    Guest(
        { Claims = FSharpGuest.claims
          Capabilities = FSharpGuest.capabilities
          Methods = readOnlyDict [ Method.CompileModel.WireValue, handler ] }
    )

/// A guest whose failed compileModel result has a diagnostic position that does not fit in a signed 32-bit number.
let private hugePosition () =
    let handler =
        Handlers.raw (fun _ _ ->
            use document =
                Text.Json.JsonDocument.Parse
                    """{"success":false,"diagnostics":[{"severity":"error","code":"fake.huge","message":"far away","location":{"uri":"src/Pricing.fs","range":{"start":{"line":4294967295,"character":4294967295},"end":{"line":4294967295,"character":4294967295}}}}]}"""

            Task.FromResult(Ok(document.RootElement.Clone())))

    Guest(
        { Claims = FSharpGuest.claims
          Capabilities = FSharpGuest.capabilities
          Methods = readOnlyDict [ Method.CompileModel.WireValue, handler ] }
    )

/// Runs a scripted guest on stdin and stdout until the session ends.
let run (scenario: string) =
    let output = Console.OpenStandardOutput()
    let input = Console.OpenStandardInput()
    Console.SetOut Console.Error

    let guest =
        match scenario with
        | "only-v4" -> onlyV4 ()
        | "ion-only" -> ionOnly ()
        | "stdout-log" -> stdoutLog output
        | "never-answers" -> neverAnswers ()
        | "blocks-read-loop" -> blocksReadLoop ()
        | "huge-position" -> hugePosition ()
        | other -> failwith $"Unknown fake guest scenario {other}"

    guest.RunAsync(input, output, CancellationToken.None).GetAwaiter().GetResult() |> ignore
    0
