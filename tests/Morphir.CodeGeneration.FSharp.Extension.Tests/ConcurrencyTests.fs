// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Generation runs off the connection's read loop: while a generateModel runs, the guest still reads and answers
/// other frames, and a $/cancelRequest reaches the request. The generator here is gated by the test, so each result
/// is decided by what the guest read, not by timing.
module Morphir.CodeGeneration.FSharp.Extension.Tests.ConcurrencyTests

open System
open System.IO.Pipelines
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.CodeGeneration.FSharp.Extension
open Morphir.CodeGeneration.FSharp.Extension.Tests.GuestSupport
open Morphir.Mep

let private wait = TimeSpan.FromSeconds 10.0

let private offer: ModelTransportSupport =
    { Tuples = [| generateTuple "4.0.0" |]
      Limits = FSharpGuest.limits
      Guarantees = [||] }

let private succeeded: GenerateResult =
    { Success = true
      Artifacts = [||]
      Diagnostics = [||] }

/// Runs a guest and a host session over in-memory pipes. The conversation gets the host session; afterwards the
/// session shuts down, exits and both connections end.
let private converse (guest: Guest) (conversation: HostSession -> unit) =
    let toGuest = Pipe()
    let toHost = Pipe()
    let guestRun = guest.RunAsync(toGuest.Reader.AsStream(), toHost.Writer.AsStream(), CancellationToken.None)

    let host =
        HostSession(
            toHost.Reader.AsStream(),
            toGuest.Writer.AsStream(),
            { Kind = PeerKind.Cli
              Name = "morphir-dotnet-tests"
              Version = "1.0.0" },
            offer
        )

    let hostRun = host.RunAsync CancellationToken.None

    match host.InitializeAsync(CancellationToken.None).Result with
    | Ok _ -> ()
    | Error failure -> failtest $"initialize: {failure}"

    try
        conversation host
    finally
        host.ShutdownAsync(CancellationToken.None).Wait wait |> ignore
        host.ExitAsync().Wait wait |> ignore
        guestRun.Wait wait |> ignore
        toHost.Writer.Complete()
        hostRun.Wait wait |> ignore

let private parameters () =
    JsonSerializer.SerializeToElement(generateRequest "4.0.0", MepJson.Default.GenerateModelRequest)

[<Tests>]
let tests =
    testList
        "F# backend guest concurrency"
        [ testCase "A ping sent while a generateModel runs is answered before the generation ends"
          <| fun _ ->
              use started = new ManualResetEventSlim false
              use gate = new ManualResetEventSlim false

              // A generator that blocks its thread until the test opens the gate, as a long generation does.
              let guest =
                  FSharpGuest.create (fun _ _ _ ->
                      started.Set()
                      gate.Wait(TimeSpan.FromSeconds 30.0) |> ignore
                      Ok succeeded)

              converse guest (fun host ->
                  let generation = host.RequestAsync(Method.GenerateModel, Nullable(parameters ()), CancellationToken.None)

                  try
                      Expect.isTrue (started.Wait wait) "The generator started"
                      let ping = host.PingAsync CancellationToken.None
                      Expect.isTrue (ping.Wait wait) "The ping is answered while the generator still runs"
                      Expect.isOk ping.Result "ping"
                      Expect.isFalse generation.IsCompleted "The generateModel reply comes after the ping reply"
                  finally
                      gate.Set()

                  Expect.isTrue (generation.Wait wait) "The generateModel reply arrives after the gate opens"
                  Expect.isOk generation.Result "generateModel")

          testCase "Cancelling the guest's session, as Ctrl+C does, cancels the token of a running generateModel"
          <| fun _ ->
              use started = new ManualResetEventSlim false
              let observed = ref false

              // A generator that runs until its token is cancelled, or for at most 15 seconds.
              let guest =
                  FSharpGuest.create (fun _ _ cancellation ->
                      started.Set()
                      observed.Value <- cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds 15.0)
                      Ok succeeded)

              let toGuest = Pipe()
              let toHost = Pipe()
              use stop = new CancellationTokenSource()
              let guestRun = guest.RunAsync(toGuest.Reader.AsStream(), toHost.Writer.AsStream(), stop.Token)

              let host =
                  HostSession(
                      toHost.Reader.AsStream(),
                      toGuest.Writer.AsStream(),
                      { Kind = PeerKind.Cli
                        Name = "morphir-dotnet-tests"
                        Version = "1.0.0" },
                      offer
                  )

              let hostRun = host.RunAsync CancellationToken.None

              match host.InitializeAsync(CancellationToken.None).Result with
              | Ok _ -> ()
              | Error failure -> failtest $"initialize: {failure}"

              host.RequestAsync(Method.GenerateModel, Nullable(parameters ()), CancellationToken.None) |> ignore
              Expect.isTrue (started.Wait wait) "The generator started"
              stop.Cancel()

              // The session ends with the cancellation, after its running handlers finish.
              try
                  guestRun.Wait wait |> ignore
              with _ ->
                  ()

              Expect.isTrue observed.Value "The running generator saw its token cancelled"
              toHost.Writer.Complete()
              hostRun.Wait wait |> ignore

          testCase "A $/cancelRequest is not admitted, because the guest claims no cancellation; the request is served"
          <| fun _ ->
              use started = new ManualResetEventSlim false
              use gate = new ManualResetEventSlim false
              let cancelled = ref false

              let guest =
                  FSharpGuest.create (fun _ _ cancellation ->
                      started.Set()
                      gate.Wait(TimeSpan.FromSeconds 30.0) |> ignore
                      cancelled.Value <- cancellation.IsCancellationRequested
                      Ok succeeded)

              converse guest (fun host ->
                  use cancel = new CancellationTokenSource()
                  let generation = host.RequestAsync(Method.GenerateModel, Nullable(parameters ()), cancel.Token)

                  try
                      Expect.isTrue (started.Wait wait) "The generator started"
                      cancel.Cancel()
                      // The guest reads frames in order, so once the ping is answered the cancel has been read.
                      let ping = host.PingAsync CancellationToken.None
                      Expect.isTrue (ping.Wait wait) "The ping after the cancel is answered"
                  finally
                      gate.Set()

                  Expect.isTrue (generation.Wait wait) "The generateModel reply arrives"
                  Expect.isOk generation.Result "The request is served, as capabilities.cancellation is false"
                  Expect.isFalse cancelled.Value "The request's token was not cancelled")
          testCase "generate does not generate for a request whose token is already cancelled"
          <| fun _ ->
              use cancelled = new CancellationTokenSource()
              cancelled.Cancel()

              match FSharpGuest.generate (Some offer) (generateRequest "4.0.0") cancelled.Token with
              | Error error -> Expect.equal error.Code (int ErrorCode.RequestCancelled) "-32800"
              | Ok result -> failtest $"Expected a cancellation: {result}"

          testCase "A generateModel admitted before morphir.shutdown is served with the tuples agreed when it was admitted"
          <| fun _ ->
              use scheduled = new ManualResetEventSlim false
              use gate = new ManualResetEventSlim false

              // The real generator, scheduled so that the scheduled work starts only when the test opens the gate.
              let schedule (work: unit -> Result<GenerateResult, RpcError>) (cancellation: CancellationToken) =
                  Task.Run(
                      Func<_>(fun () ->
                          scheduled.Set()
                          gate.Wait(TimeSpan.FromSeconds 30.0) |> ignore
                          work ()),
                      cancellation
                  )

              let guest = FSharpGuest.createScheduled schedule FSharpGuest.generate

              converse guest (fun host ->
                  let generation = host.RequestAsync(Method.GenerateModel, Nullable(parameters ()), CancellationToken.None)

                  try
                      Expect.isTrue (scheduled.Wait wait) "The request is admitted and its work is scheduled"
                      // The read loop handles shutdown, which moves the session to ShutDown, before the work starts.
                      let shutdown = host.ShutdownAsync CancellationToken.None
                      Expect.isTrue (shutdown.Wait wait) "shutdown is answered"
                      Expect.isOk shutdown.Result "shutdown"
                  finally
                      gate.Set()

                  Expect.isTrue (generation.Wait wait) "The generateModel reply arrives"

                  match generation.Result with
                  | Ok element ->
                      match JsonSerializer.Deserialize(element, MepJson.Default.GenerateResult) with
                      | null -> failtest "The result is null"
                      | result ->
                          Expect.isTrue result.Success $"The admitted request generates: {result}"
                          Expect.isNonEmpty result.Artifacts "with its files"
                  | Error error -> failtest $"An admitted request is served after shutdown, not refused: {error}") ]
