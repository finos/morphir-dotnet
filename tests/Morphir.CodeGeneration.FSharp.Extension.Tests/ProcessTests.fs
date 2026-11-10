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
let private builtIn (path: string) (options: (string * string) list) : (string * string) list =
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
        // The order of the published manifest and the generate report: the declared order, else by path.
        let tree = success.Value.Tree
        let files = Morphir.IR.Pipeline.VFileTree.toFileMap tree

        let order =
            Morphir.IR.Pipeline.VFileTree.tryGet Morphir.IR.Pipeline.VFileTree.declaredOrder tree
            |> Option.defaultWith (fun () -> files |> Map.keys |> List.ofSeq)

        [ for location in order ->
              match Morphir.IR.Pipeline.VFile.readText files[location] with
              | Ok text -> location, text
              | Error failure -> failtest $"{location} reads as text: {failure}" ]
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
              Expect.equal session.Extension.Id FSharpGuest.extension.Id "The guest identity"
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

          testCase "The guest process generates the same files, in the same order, as the managed built-in backend, with and without optimize where folding applies"
          <| fun _ ->
              let fixture name = irFixture name

              let cases =
                  [ "daily-rate-v3.json", "3.0.0", []
                    "daily-rate-v4.json", "4.0.0", []
                    "constant-folding-v3.json", "3.0.0", [ "optimize", "true" ]
                    "constant-folding-v3.json", "3.0.0", [ "optimize", "false" ]
                    "constant-folding-v4.json", "4.0.0", [ "optimize", "true" ]
                    "constant-folding-v4.json", "4.0.0", [ "optimize", "false" ] ]

              let run =
                  withGuest (offerOf [ generateTuple "4.0.0"; generateTuple "3.0.0" ]) (fun host cancellation ->
                      task {
                          let! _ = initialize host cancellation
                          let results = ResizeArray()

                          for name, version, options in cases do
                              let request = requestOf (generateTuple version) (File.ReadAllBytes(fixture name)) options
                              let! reply = generate host request cancellation
                              results.Add reply

                          return List.ofSeq results
                      })

              let outputs =
                  [ for (name, version, options), reply in List.zip cases run.Value ->
                        let label = $"{name} (IR {version}) with options {options}"

                        let guest =
                            match reply with
                            | Ok element ->
                                let result = read element
                                Expect.isTrue result.Success $"{label}: the guest succeeds: {result}"
                                [ for artifact in result.Artifacts -> artifact.Path, artifact.Content ]
                            | Error error -> failtest $"{label}: {error}"

                        let expected = builtIn (fixture name) options
                        Expect.isNonEmpty expected $"{label}: the built-in generates files"

                        // Over MEP the artifact order is the only order a host gets, so it must be the built-in's order.
                        Expect.sequenceEqual (List.map fst guest) (List.map fst expected) $"{label}: the same paths in the same order"
                        Expect.sequenceEqual guest expected $"{label}: the same text for each file"
                        (name, options), guest ]

              let text name optimize =
                  outputs
                  |> List.find (fun ((fixtureName, options), _) -> fixtureName = name && options = [ "optimize", optimize ])
                  |> snd
                  |> List.map snd
                  |> String.concat "\n"

              for name in [ "constant-folding-v3.json"; "constant-folding-v4.json" ] do
                  let folded = text name "true"
                  let plain = text name "false"
                  Expect.notEqual folded plain $"{name}: optimize reaches the generator"
                  Expect.stringContains folded "42" $"{name}: optimize=true folds 20 + 22"
                  Expect.stringContains plain "20" $"{name}: optimize=false keeps the addition"

                  let paths =
                      outputs |> List.find (fun ((fixtureName, _), _) -> fixtureName = name) |> snd |> List.map fst

                  Expect.isTrue
                      ((paths |> List.findIndex (fun path -> path.Contains "Zeta")) < (paths |> List.findIndex (fun path -> path.Contains "Alpha")))
                      $"{name}: dependency order, not path order: {paths}"

              Expect.equal run.ExitCode 0 "exit code"

          testCase "A ping sent right after a large generateModel is answered before the generation ends"
          <| fun _ ->
              // Before the reply to generateModel, the guest can answer only frames that it read during generation.
              // A guest that generates on its read loop reads the ping only afterwards, so its ping reply always
              // comes second; this test cannot pass by luck. The model is large, so generation takes much longer
              // than a ping.
              let request = requestOf (generateTuple "4.0.0") (largeModel 400) []

              let run =
                  withGuest (offerOf [ generateTuple "4.0.0" ]) (fun host cancellation ->
                      task {
                          let! _ = initialize host cancellation
                          let generation = generate host request cancellation
                          let ping = host.PingAsync cancellation
                          let! first = Task.WhenAny(generation :> Task, ping :> Task)
                          let pingFirst = Object.ReferenceEquals(first, ping)
                          let! generated = generation
                          let! pinged = ping
                          return pingFirst, generated, pinged
                      })

              let pingFirst, generated, pinged = run.Value
              Expect.isOk pinged "ping"

              match generated with
              | Ok element -> Expect.isTrue (read element).Success "The large model generates"
              | Error error -> failtest $"generateModel: {error}"

              Expect.isTrue pingFirst "The ping reply arrives before the generateModel reply"
              Expect.equal run.ExitCode 0 "exit code"

          testCase "A model nested just under the agreed maxDepth gets a reply and the guest keeps running; one just over is refused"
          <| fun _ ->
              // The agreed maxDepth is the smaller of the host's 4096 and the guest's 1024.
              let agreedDepth = FSharpGuest.limits.MaxDepth

              let rec levelsFor (levels: int) =
                  if jsonDepth (deepModel levels) >= agreedDepth - 8 then levels else levelsFor (levels + 1)

              let near = deepModel (levelsFor 400)
              let over = deepModel (levelsFor 400 + 20)
              Expect.isLessThanOrEqual (jsonDepth near) agreedDepth "The near model is within the limit"
              Expect.isGreaterThan (jsonDepth over) agreedDepth "The over model is beyond the limit"

              let run =
                  withGuest (offerOf [ generateTuple "4.0.0" ]) (fun host cancellation ->
                      task {
                          let! _ = initialize host cancellation
                          let! nearReply = generate host (requestOf (generateTuple "4.0.0") near []) cancellation
                          let! pingAfterNear = host.PingAsync cancellation
                          let! overReply = generate host (requestOf (generateTuple "4.0.0") over []) cancellation
                          return nearReply, pingAfterNear, overReply
                      })

              let nearReply, pingAfterNear, overReply = run.Value

              // A success or a protocol error is a reply. A crashed guest would give no ping reply and no clean exit.
              match nearReply with
              | Ok element -> Expect.isNotNull (box (read element)) "a result"
              | Error error -> Expect.notEqual error.Message "The connection closed." $"The guest did not end the connection: {error}"

              Expect.isOk pingAfterNear $"The guest still answers after the deep model: {run.StandardError}"

              match overReply with
              | Error error ->
                  Expect.equal error.Code -32602 "-32602"
                  Expect.equal error.Message "Model admission refused: depth_limit_exceeded" "The admission code"
              | Ok element -> failtest $"A model over the depth limit is not served: {element}"

              Expect.equal run.ExitCode 0 $"exit code: {run.StandardError}" ]
