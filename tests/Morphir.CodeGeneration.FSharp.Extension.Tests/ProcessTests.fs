// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The guest as a separate process behind the Morphir.Mep host session, over Content-Length framing on stdio:
/// negotiation, refusal of a tuple that the session does not agree, and the same F# files as the built-in backend
/// for the same checked-in IR.
module Morphir.CodeGeneration.FSharp.Extension.Tests.ProcessTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.CodeGeneration.FSharp.Extension
open Morphir.CodeGeneration.FSharp.Extension.Tests.GuestSupport
open Morphir.Mep
open Morphir.TestSupport
open Morphir.Tooling.Features.Generate

let private limits: ModelTransportLimits =
    { MaxEncodedBytes = 60 * 1024 * 1024
      MaxDecodedBytes = 45 * 1024 * 1024
      MaxDepth = 4096
      MaxNodes = 50_000_000 }

let private offerOf (tuples: ModelTransportTuple list) : ModelTransportSupport =
    { Tuples = Array.ofList tuples
      Limits = limits
      Guarantees = [||] }

let private ionGenerate version =
    { generateTuple version with
        Profile = "morphir-model"
        Codec = ModelCodec.IonBinary }

let private compileJson version =
    { generateTuple version with
        Operation = ModelOperation.CompileModel }

/// What one process session gave: the value of the conversation, the exit code and the stdout that the host
/// session did not read as frames.
type private Run<'T> =
    { Value: 'T
      ExitCode: int
      Rest: string
      StandardError: string }

/// Runs the guest as a separate process (dotnet morphir-fsharp-backend.dll) through the shared test harness: the
/// process gate, a deadline, no window, and process-tree cleanup on timeout or on a failed conversation. A
/// Morphir.Mep host session with the offer talks to the child's stdio. After the conversation it shuts the session
/// down, exits, and waits for the end of the connection.
let private withGuest (offer: ModelTransportSupport) (conversation: HostSession -> CancellationToken -> Task<'T>) =
    let dotnet =
        match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
        | null -> "dotnet"
        | path -> path

    use sandbox = TestSandbox.Create "morphir-backend-guest-"
    let start = ChildProcess.StartInfo(dotnet, Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-backend.dll"))
    start.WorkingDirectory <- sandbox.Work
    sandbox.Apply start
    let value = ref None

    let converse (input: Stream) (output: Stream) (cancellation: CancellationToken) : Task =
        task {
            let host =
                HostSession(
                    output,
                    input,
                    { Kind = PeerKind.Cli
                      Name = "morphir-dotnet-tests"
                      Version = "1.0.0" },
                    offer
                )

            let connection = host.RunAsync cancellation
            let! result = conversation host cancellation
            value.Value <- Some result
            let! shutdown = host.ShutdownAsync cancellation
            Expect.isOk shutdown "shutdown"
            do! host.ExitAsync()
            match! connection with
            | ConnectionEnd.FramingFailed failure -> failtest $"The guest wrote output that is not a frame: {failure}"
            | ConnectionEnd.EndOfStream
            | ConnectionEnd.Stopped -> ()
        }

    let result =
        ChildProcess
            .ConverseAsync(start, TimeSpan.FromMinutes 3.0, Func<_, _, _, _> converse, CancellationToken.None, null)
            .GetAwaiter()
            .GetResult()

    { Value = value.Value |> Option.defaultWith (fun () -> failtest "The conversation gave no value")
      ExitCode = result.ExitCode
      Rest = result.StandardOutput
      StandardError = result.StandardError }

let private initialize (host: HostSession) (cancellation: CancellationToken) =
    task {
        match! host.InitializeAsync cancellation with
        | Ok session -> return session
        | Error failure -> return failtest $"initialize: {failure}"
    }

let private generate (host: HostSession) (request: GenerateModelRequest) (cancellation: CancellationToken) =
    let parameters = JsonSerializer.SerializeToElement(request, MepJson.Default.GenerateModelRequest)
    host.RequestAsync(Method.GenerateModel, Nullable parameters, cancellation)

let private read (element: JsonElement) =
    match JsonSerializer.Deserialize(element, MepJson.Default.GenerateResult) with
    | null -> failtest "The result is null"
    | result -> result

/// The files that the built-in backend generates through its public seams: the generate model loader and
/// FSharpGenerationBackend, the provider that morphir generate selects for the target fsharp.
let private builtIn (path: string) (options: (string * string) list) : Map<string, string> =
    let loader = GenerationModelLoader(BoundedModelCapture())

    let loaded =
        match loader.LoadAsync(ModelInput(path, InputFormat.Json), ModelReadLimits.Default, CancellationToken.None).AsTask().Result with
        | :? Outcome<LoadedModel>.Success as success -> success.Value
        | failure -> failtest $"The built-in loader reads {path}: {failure}"

    let backend = FSharpGenerationBackend()

    let values =
        [ for key, json in options ->
              use document = JsonDocument.Parse json
              KeyValuePair(key, document.RootElement.Clone()) ]

    let validated =
        match backend.ValidateOptions(BackendOptions values) with
        | :? Outcome<ValidatedBackendOptions>.Success as success -> success.Value
        | failure -> failtest $"The built-in accepts the options: {failure}"

    match
        backend.GenerateAsync(BackendGenerationRequest(loaded, backend.Descriptor.Target, validated), CancellationToken.None).AsTask().Result
    with
    | :? Outcome<GenerationArtifacts>.Success as success ->
        Morphir.IR.Pipeline.VFileTree.toFileMap success.Value.Tree
        |> Map.map (fun location file ->
            match Morphir.IR.Pipeline.VFile.readText file with
            | Ok text -> text
            | Error failure -> failtest $"{location} reads as text: {failure}")
    | failure -> failtest $"The built-in generates {path}: {failure}"

[<Tests>]
let tests =
    testSequenced
    <| testList
        "F# backend MEP guest process"
        [ testCase "A host negotiates with the guest process: the claims, and only the claimed tuples in host order"
          <| fun _ ->
              let offer =
                  offerOf [ ionGenerate "4.0.0"; generateTuple "4.0.0"; compileJson "4.0.0"; generateTuple "3.0.0" ]

              let run =
                  withGuest offer (fun host cancellation ->
                      task {
                          let! described = host.DescribeAsync cancellation
                          let! session = initialize host cancellation
                          return described, session
                      })

              let described, session = run.Value

              match described with
              | Ok claims ->
                  Expect.equal
                      (JsonSerializer.Serialize(claims, MepJson.Default.CapabilityClaimSet))
                      (JsonSerializer.Serialize(FSharpGuest.claims, MepJson.Default.CapabilityClaimSet))
                      "describe returns the claim set"
              | Error error -> failtest $"describe: {error}"

              Expect.equal session.ProtocolVersion (ProtocolVersion.Create "0.2.0-draft.1") "The agreed MEP version"
              Expect.equal session.Extension.Id "org.finos.morphir.fsharp-backend" "The guest identity"
              Expect.equal session.Capabilities.Backend (Some FSharpGuest.backend) "The backend capability"

              Expect.sequenceEqual
                  (session.Capabilities.ModelTransport |> Option.map _.Tuples |> Option.defaultValue [||])
                  [ generateTuple "4.0.0"; generateTuple "3.0.0" ]
                  "Only the JSON generateModel tuples, in host order"

              Expect.equal run.ExitCode 0 $"The guest exits with 0 after exit: {run.StandardError}"
              Expect.equal run.Rest "" "The guest writes only frames to stdout"

          testCase "In a process session, a request in a tuple that is not agreed is refused with -32602 and not served; a valid request is then served"
          <| fun _ ->
              let run =
                  withGuest (offerOf [ generateTuple "4.0.0" ]) (fun host cancellation ->
                      task {
                          let! session = initialize host cancellation
                          let! refused = generate host (generateRequest "3.0.0") cancellation
                          let! served = generate host (generateRequest "4.0.0") cancellation
                          return session, refused, served
                      })

              let session, refused, served = run.Value

              Expect.sequenceEqual
                  (session.Capabilities.ModelTransport |> Option.map _.Tuples |> Option.defaultValue [||])
                  [ generateTuple "4.0.0" ]
                  "The host offered only 4.0.0"

              match refused with
              | Error error ->
                  Expect.equal error.Code -32602 "-32602"
                  Expect.equal error.Message "Model admission refused: unsupported_tuple" "The admission code"
              | Ok result -> failtest $"A 3.0.0 request is not served: {result}"

              match served with
              | Ok result -> Expect.isTrue (read result).Success "The 4.0.0 request is served"
              | Error error -> failtest $"The 4.0.0 request: {error}"

              Expect.equal run.ExitCode 0 "exit code"

          testCase "The guest process generates the same files as the built-in backend for the same checked-in IR"
          <| fun _ ->
              let cases =
                  [ "3.0.0", []
                    "4.0.0", []
                    "4.0.0", [ "optimize", "false" ] ]

              let run =
                  withGuest (offerOf [ generateTuple "4.0.0"; generateTuple "3.0.0" ]) (fun host cancellation ->
                      task {
                          let! _ = initialize host cancellation
                          let results = ResizeArray()

                          for version, options in cases do
                              let request =
                                  requestOf (generateTuple version) (File.ReadAllBytes(fixtureFor version)) options

                              let! reply = generate host request cancellation
                              results.Add reply

                          return List.ofSeq results
                      })

              for (version, options), reply in List.zip cases run.Value do
                  let label = $"IR {version} with options {options}"

                  let guest =
                      match reply with
                      | Ok element ->
                          let result = read element
                          Expect.isTrue result.Success $"{label}: the guest succeeds: {result}"
                          filesOf result
                      | Error error -> failtest $"{label}: {error}"

                  let expected = builtIn (fixtureFor version) options
                  Expect.isNonEmpty expected $"{label}: the built-in generates files"
                  Expect.equal (guest |> Map.keys |> List.ofSeq) (expected |> Map.keys |> List.ofSeq) $"{label}: the same paths"

                  for KeyValue(location, text) in expected do
                      Expect.equal guest[location] text $"{label}: {location} has the same text"

              Expect.equal run.ExitCode 0 "exit code" ]
