// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Scripted backend guests for the host adapter tests. The test executable runs one when it starts with
/// --fake-backend-guest <scenario>. Each speaks MEP 0.2.0-draft.1 over stdio, as morphir-fsharp-backend does.
module Morphir.CodeGeneration.FSharp.Extension.Tests.FakeBackendGuests

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.CodeGeneration.FSharp.Extension
open Morphir.Mep

let private tuple codec profile version : ModelTransportTuple =
    { Operation = ModelOperation.GenerateModel
      Profile = profile
      Codec = codec
      EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.1"
      IrVersion = SemanticVersion.Create version }

/// A guest with the real guest's identity and backend claim, the given tuples, and a generateModel handler.
let private guestWith (tuples: ModelTransportTuple list) (handler: MethodHandler) =
    let support = { FSharpGuest.support with Tuples = Array.ofList tuples }

    let capabilities =
        { FSharpGuest.capabilities with
            ModelTransport = Some support }

    let claims =
        { FSharpGuest.claims with
            Extension =
                { FSharpGuest.extension with
                    Id = "org.example.fake-backend" }
            Capabilities =
                readOnlyDict
                    [ "backend", JsonSerializer.SerializeToElement(FSharpGuest.backend, MepJson.Default.BackendCapability)
                      "modelTransport", JsonSerializer.SerializeToElement(support, MepJson.Default.ModelTransportSupport) ] }

    Guest(
        { Claims = claims
          Capabilities = capabilities
          Methods = readOnlyDict [ Method.GenerateModel.WireValue, handler ] }
    )

let private json4 = tuple ModelCodec.MorphirJson "morphir-ir" "4.0.0"

/// A result with the given artifacts, as raw JSON.
let private result (artifacts: string) =
    use document =
        JsonDocument.Parse("""{"success":true,"artifacts":""" + artifacts + ""","diagnostics":[]}""")

    document.RootElement.Clone()

let private answer (json: JsonElement) =
    Handlers.raw (fun _ _ -> Task.FromResult(Ok json))

/// Runs a scenario on stdin and stdout and returns the exit code.
let run (scenario: string) =
    let input = Console.OpenStandardInput()
    let output = Console.OpenStandardOutput()
    Console.SetOut Console.Error

    let guest =
        match scenario with
        // Claims only an Ion generateModel tuple, which the host does not offer.
        | "ion-only" -> guestWith [ tuple ModelCodec.IonBinary "morphir-model" "4.0.0" ] (answer (result "[]"))
        // Claims only IR 4.0.0 and answers with one file.
        | "only-v4" -> guestWith [ json4 ] (answer (result """[{"path":"Only.fs","content":"module Only","binary":false}]"""))
        // Returns a path outside the output directory.
        | "unsafe-path" -> guestWith [ json4 ] (answer (result """[{"path":"../escape.fs","content":"x","binary":false}]"""))
        // Returns two paths that differ only in case.
        | "case-collision" ->
            guestWith
                [ json4 ]
                (answer (result """[{"path":"Ab.fs","content":"x","binary":false},{"path":"AB.fs","content":"y","binary":false}]"""))
        // Returns a result that is not a generate result.
        | "malformed-result" ->
            guestWith
                [ json4 ]
                (Handlers.raw (fun _ _ ->
                    use document = JsonDocument.Parse """{"success":"yes"}"""
                    Task.FromResult(Ok(document.RootElement.Clone()))))
        // Exits while it handles generateModel.
        | "exit-mid-request" ->
            guestWith
                [ json4 ]
                (Handlers.raw (fun _ _ ->
                    output.Flush()
                    Environment.Exit 3
                    Task.FromResult(Ok(result "[]"))))
        // Blocks synchronously in the generateModel handler. The guest dispatches on its read loop, so it never
        // reads shutdown or exit either. It writes its process id to stderr first.
        | "blocks-read-loop" ->
            eprintfn "fake-guest-pid=%d" Environment.ProcessId

            guestWith
                [ json4 ]
                (Handlers.raw (fun _ _ ->
                    Thread.Sleep Timeout.Infinite
                    TaskCompletionSource<Result<JsonElement, RpcError>>().Task))
        // Never answers generateModel.
        | "never-answers" ->
            guestWith [ json4 ] (Handlers.raw (fun _ _ -> TaskCompletionSource<Result<JsonElement, RpcError>>().Task))
        // Answers generateModel with a frame header above the 64 MiB frame limit, then stops writing.
        | "oversized-frame" ->
            guestWith
                [ json4 ]
                (Handlers.raw (fun _ _ ->
                    let header = Encoding.ASCII.GetBytes "Content-Length: 104857600\r\n\r\n"
                    output.Write(header, 0, header.Length)
                    output.Flush()
                    TaskCompletionSource<Result<JsonElement, RpcError>>().Task))
        | other -> failwith $"Unknown fake backend guest scenario {other}"

    match guest.RunAsync(input, output, CancellationToken.None).GetAwaiter().GetResult() with
    | ConnectionEnd.FramingFailed _ -> 2
    | _ -> 0
