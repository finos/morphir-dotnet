// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The scenarios of the mep-protocol delta of add-wasm-extension-host (task 2.3, design CHAN-1): a host session on a
/// channel to a guest that takes one message per call. The fake guest is a Guest's dispatcher behind
/// ISingleMessageChannel, as a WASM guest's handle call is.
module Morphir.Mep.Tests.ChannelTests

open System
open System.Collections.Concurrent
open System.IO
open System.IO.Pipelines
open System.Reflection
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Mep
open Morphir.TestSupport

let private host: PeerInfo =
    { Kind = PeerKind.Cli
      Name = "morphir"
      Version = "0.4.0" }

let private validate = Method.Validate.WireValue

/// A validator guest with one extension method, morphir.validator.validate.
let private guest () =
    let claims =
        JsonSerializer.Deserialize(
            """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.1"],"extension":{"id":"org.finos.morphir.fake","name":"Fake","version":"1.0.0","types":["validator"]},"capabilities":{}}""",
            MepJson.Default.CapabilityClaimSet
        )
        |> nonNull

    let capabilities =
        JsonSerializer.Deserialize("""{"progress":true}""", MepJson.Default.ExtensionCapabilities)
        |> nonNull

    Guest(
        { Claims = claims
          Capabilities = capabilities
          Methods =
            readOnlyDict
                [ validate, Handlers.raw (fun _ _ -> Task.FromResult(Ok(JsonRpc.Constant """{"valid":true}"""))) ] }
    )

/// A guest that takes one message per call. Each call passes the message to the guest's dispatcher, as one WASM
/// handle call does. answer may replace what the guest returned; enter runs inside the call, before the guest.
type private FakeGuest
    (
        guest: Guest,
        deliverNotifications: bool,
        answer: string -> byte array | null -> byte array | null,
        enter: string -> CancellationToken -> Task
    ) =
    let messages = ConcurrentQueue<string * byte array>()
    let active = ref 0
    let mutable overlapped = false

    let call (method: string) (message: ReadOnlyMemory<byte>) (cancellation: CancellationToken) =
        task {
            if Interlocked.Increment(&active.contents) > 1 then
                overlapped <- true

            try
                messages.Enqueue(method, message.ToArray())
                do! enter method cancellation
                let! reply = guest.Dispatcher.DispatchAsync(message, cancellation)
                return answer method reply
            finally
                Interlocked.Decrement(&active.contents) |> ignore
        }

    new(guest: Guest) = FakeGuest(guest, true, (fun _ reply -> reply), (fun _ _ -> Task.CompletedTask))

    /// The method and bytes of each message passed to the guest, in order.
    member _.Messages = messages.ToArray() |> List.ofArray

    /// The methods of the messages passed to the guest, in order.
    member this.Methods = this.Messages |> List.map fst

    /// True when two calls were inside the guest at the same time.
    member _.Overlapped = overlapped

    interface ISingleMessageChannel with
        member _.RequestAsync(method, message, cancellation) = call method message cancellation

        member _.NotifyAsync(method, message, cancellation) =
            if deliverNotifications then
                call method message cancellation
            else
                Task.FromResult<byte array | null> null

let private wait (task: Task<'T>) =
    if not (task.Wait(TimeSpan.FromSeconds 10.)) then
        failtest "The task did not complete within 10 seconds"

    task.Result

let private waitTask (task: Task) =
    if not (task.Wait(TimeSpan.FromSeconds 10.)) then
        failtest "The task did not complete within 10 seconds"

let private bytes (json: string) : byte array | null = Encoding.UTF8.GetBytes json

let private node (bytes: byte array) = JsonNode.Parse(ReadOnlySpan bytes) |> nonNull

/// A session on the channel, initialized.
let private initialized (fake: FakeGuest) =
    let session = HostSession(fake :> ISingleMessageChannel, host)
    Expect.isOk (wait (session.InitializeAsync CancellationToken.None)) "initialize"
    session

let private request (session: HostSession) =
    session.RequestAsync(Method.Validate, Nullable(JsonRpc.Constant "{}"), CancellationToken.None)

let private failure (session: HostSession) =
    session.Failure |> Option.defaultWith (fun () -> failtest "The session did not fail")

/// The host refuses a later request without passing a message to the guest.
let private refusesWithoutCall (session: HostSession) (fake: FakeGuest) =
    let before = fake.Messages.Length

    match wait (session.PingAsync CancellationToken.None) with
    | Ok _ -> failtest "A request after the session ended must be refused"
    | Error error -> Expect.equal error.Code -32603 "The ended session refuses the request"

    Expect.equal fake.Messages.Length before "No message reaches the guest after the session ended"

/// The negotiated session of the same guest on a framed stream.
let private streamSession () =
    let guest = guest ()
    let toGuest = Pipe()
    let toHost = Pipe()
    let guestRun = guest.RunAsync(toGuest.Reader.AsStream(), toHost.Writer.AsStream(), CancellationToken.None)
    let session = HostSession(toHost.Reader.AsStream(), toGuest.Writer.AsStream(), host)
    let hostRun = session.RunAsync CancellationToken.None
    let negotiated = wait (session.InitializeAsync CancellationToken.None)
    Expect.isOk (wait (session.ShutdownAsync CancellationToken.None)) "stream shutdown"
    waitTask (session.ExitAsync())
    wait guestRun |> ignore
    toHost.Writer.Complete()
    wait hostRun |> ignore

    match negotiated with
    | Ok negotiated -> negotiated
    | Error failure -> failtest $"The stream session did not initialize: {failure}"

let private text (session: NegotiatedSession) =
    String.Join(
        "\n",
        session.ToString(),
        JsonSerializer.Serialize(session.Extension, MepJson.Default.ExtensionInfo),
        JsonSerializer.Serialize(session.Capabilities, MepJson.Default.ExtensionCapabilities),
        JsonSerializer.Serialize(session.Host, MepJson.Default.PeerInfo)
    )

[<Tests>]
let tests =
    Transcripts.ensureThreads.Force()

    testList
        "MEP single-message channel"
        [ testList
              "A host session applies one set of rules on every channel"
              [ testCase "a fake single-message guest completes initialize, a request and shutdown"
                <| fun _ ->
                    let guest = guest ()
                    let fake = FakeGuest guest
                    let session = HostSession(fake :> ISingleMessageChannel, host)

                    let negotiated =
                        match wait (session.InitializeAsync CancellationToken.None) with
                        | Ok negotiated -> negotiated
                        | Error failure -> failtest $"initialize: {failure}"

                    match wait (request session) with
                    | Ok result -> Expect.equal (result.GetRawText()) """{"valid":true}""" "The request gets its response"
                    | Error error -> failtest $"validate: {error}"

                    match wait (session.ShutdownAsync CancellationToken.None) with
                    | Ok result -> Expect.equal (result.GetRawText()) "{}" "shutdown gets its response"
                    | Error error -> failtest $"shutdown: {error}"

                    waitTask (session.ExitAsync())

                    Expect.equal
                        fake.Methods
                        [ "morphir.initialize"; "morphir.initialized"; validate; "morphir.shutdown"; "morphir.exit" ]
                        "One call for each message, in lifecycle order"

                    for method, message in fake.Messages do
                        Expect.equal message[0] (byte '{') $"{method} is one JSON object with no Content-Length header"
                        let parsed = node message
                        Expect.equal ((parsed["method"] |> nonNull).GetValue<string>()) method "The call names the method of its message"

                    Expect.equal session.State SessionState.Exited "The host session exited"
                    Expect.equal guest.State SessionState.Exited "The guest received exit"
                    Expect.isNone session.Failure "The session did not fail"
                    Expect.equal (text negotiated) (text (streamSession ())) "The same negotiated session as on a framed stream"

                testCase "the same refusal on both channels"
                <| fun _ ->
                    use input = new MemoryStream()
                    use output = new MemoryStream()
                    let stream = HostSession(input, output, host)
                    let fake = FakeGuest(guest ())
                    let channel = HostSession(fake :> ISingleMessageChannel, host)

                    let refusal (session: HostSession) =
                        match wait (session.CapabilitiesAsync CancellationToken.None) with
                        | Ok _ -> failtest "capabilities before initialize must be refused"
                        | Error error -> error

                    let onStream = refusal stream
                    let onChannel = refusal channel
                    Expect.equal onStream.Code -32014 "The stream session refuses with -32014"
                    Expect.equal onChannel onStream "The same error on both channels"
                    Expect.equal output.Length 0L "The stream guest receives nothing"
                    Expect.isEmpty fake.Messages "The single-message guest receives nothing" ]

          testList
              "One call carries one message and at most one reply"
              [ testCase "request with no reply"
                <| fun _ ->
                    let fake =
                        FakeGuest(
                            guest (),
                            true,
                            (fun method reply -> if method = validate then null else reply),
                            fun _ _ -> Task.CompletedTask
                        )

                    let session = initialized fake

                    match wait (request session) with
                    | Ok _ -> failtest "A request with no reply must fail"
                    | Error error -> Expect.equal error.Code -32603 "The request fails"

                    Expect.equal (failure session) (SessionFailure.NoReply validate) "The session fails"
                    refusesWithoutCall session fake

                testCase "reply for another request"
                <| fun _ ->
                    let fake =
                        FakeGuest(
                            guest (),
                            true,
                            (fun method reply ->
                                match reply with
                                | null -> null
                                | reply when method = validate ->
                                    let answer = node reply
                                    answer["id"] <- JsonValue.Create 999
                                    bytes (answer.ToJsonString())
                                | reply -> reply),
                            fun _ _ -> Task.CompletedTask
                        )

                    let session = initialized fake

                    match wait (request session) with
                    | Ok _ -> failtest "The response for another id must not reach the caller"
                    | Error error -> Expect.equal error.Code -32603 "The request fails"

                    let sent =
                        fake.Messages |> List.find (fun (method, _) -> method = validate) |> snd |> node

                    let id = RequestId.Number((sent["id"] |> nonNull).GetValue<uint64>())

                    Expect.equal
                        (failure session)
                        (SessionFailure.ReplyForAnotherRequest(validate, id, Some(RequestId.Number 999UL)))
                        "The session fails"

                    refusesWithoutCall session fake

                testCase "guest returns a notification"
                <| fun _ ->
                    let progress =
                        """{"jsonrpc":"2.0","method":"morphir.progress","params":{"requestId":1,"kind":"report","message":"m"}}"""

                    let fake =
                        FakeGuest(
                            guest (),
                            true,
                            (fun method reply -> if method = validate then bytes progress else reply),
                            fun _ _ -> Task.CompletedTask
                        )

                    let session = initialized fake

                    match wait (request session) with
                    | Ok _ -> failtest "A notification is not a response"
                    | Error error -> Expect.equal error.Code -32603 "The request fails"

                    Expect.equal
                        (failure session)
                        (SessionFailure.NotAResponse(validate, "morphir.progress"))
                        "The session fails"

                    Expect.isEmpty session.Progress "The notification is not given to the host"
                    refusesWithoutCall session fake

                testCase "a reply over the message limit fails the session"
                <| fun _ ->
                    let size = ContentLengthFraming.MaxBodyBytes + 1

                    let fake =
                        FakeGuest(
                            guest (),
                            true,
                            (fun method reply -> if method = validate then Array.zeroCreate size else reply),
                            fun _ _ -> Task.CompletedTask
                        )

                    let session = initialized fake

                    match wait (request session) with
                    | Ok _ -> failtest "A reply over the limit must fail"
                    | Error error -> Expect.equal error.Code -32603 "The request fails"

                    Expect.equal (failure session) (SessionFailure.ReplyTooLarge(validate, size)) "The session fails"
                    refusesWithoutCall session fake

                testCase "a channel that throws fails the session with its exception"
                <| fun _ ->
                    let trap = InvalidOperationException "wasm trap: unreachable"

                    let fake =
                        FakeGuest(
                            guest (),
                            true,
                            (fun _ reply -> reply),
                            fun method _ -> if method = validate then raise trap else Task.CompletedTask
                        )

                    let session = initialized fake

                    match wait (request session) with
                    | Ok _ -> failtest "A request whose channel throws must fail"
                    | Error error ->
                        Expect.equal error.Code -32603 "The request fails with -32603"

                        Expect.stringContains
                            error.Message
                            $"The channel failed during {validate}: {trap.Message}"
                            "The error names the failure"

                    match failure session with
                    | SessionFailure.ChannelFailed(method, error) ->
                        Expect.equal method validate "The failure names the method"
                        Expect.isTrue (obj.ReferenceEquals(error, trap)) "The failure holds the exception that the channel threw"
                    | other -> failtest $"The session must fail with ChannelFailed, not {other}"

                    refusesWithoutCall session fake

                testCase "a reply that is not a JSON-RPC message fails the session"
                <| fun _ ->
                    for returned, code in [ "not json", -32700; """[{"jsonrpc":"2.0","id":1,"result":{}}]""", -32600 ] do
                        let fake =
                            FakeGuest(
                                guest (),
                                true,
                                (fun method reply -> if method = validate then bytes returned else reply),
                                fun _ _ -> Task.CompletedTask
                            )

                        let session = initialized fake

                        match wait (request session) with
                        | Ok _ -> failtest $"The reply {returned} must not reach the caller"
                        | Error error -> Expect.equal error.Code -32603 $"The request fails for {returned}"

                        match failure session with
                        | SessionFailure.InvalidReply(method, refusal) ->
                            Expect.equal method validate "The failure names the method"
                            Expect.equal refusal.Error.Code code $"The refusal of {returned} has the reader's code"
                            Expect.isNone refusal.Id $"The refusal of {returned} has no id"
                        | other -> failtest $"The session must fail with InvalidReply for {returned}, not {other}"

                        refusesWithoutCall session fake

                testCase "a single-message guest gets one call at a time"
                <| fun _ ->
                    let fake =
                        FakeGuest(
                            guest (),
                            true,
                            (fun _ reply -> reply),
                            fun method _ -> if method = validate then Task.Delay 50 else Task.CompletedTask
                        )

                    let session = initialized fake
                    let replies = [ for _ in 1..4 -> request session ]

                    for reply in replies do
                        Expect.isOk (wait reply) "Each request gets its response"

                    Expect.isFalse fake.Overlapped "No call starts while another is inside the guest" ]

          testList
              "A notification to a single-message guest returns nothing"
              [ testCase "guest answers a notification"
                <| fun _ ->
                    let fake =
                        FakeGuest(
                            guest (),
                            true,
                            (fun method reply ->
                                if method = "morphir.initialized" then
                                    bytes """{"jsonrpc":"2.0","id":0,"result":{}}"""
                                else
                                    reply),
                            fun _ _ -> Task.CompletedTask
                        )

                    let session = HostSession(fake :> ISingleMessageChannel, host)
                    Expect.isError (wait (session.InitializeAsync CancellationToken.None)) "initialize fails"

                    Expect.equal
                        (failure session)
                        (SessionFailure.AnsweredNotification "morphir.initialized")
                        "The session fails"

                    refusesWithoutCall session fake

                testCase "runtime withholds a notification"
                <| fun _ ->
                    let guest = guest ()
                    let fake = FakeGuest(guest, false, (fun _ reply -> reply), (fun _ _ -> Task.CompletedTask))
                    let session = initialized fake
                    Expect.equal fake.Methods [ "morphir.initialize" ] "The guest is not called for morphir.initialized"

                    match session.State with
                    | SessionState.Initialized _ -> ()
                    | other -> failtest $"The session continues as initialized, not {other}"

                    Expect.isOk (wait (request session)) "The session continues"
                    Expect.isNone session.Failure "The session did not fail" ]

          testList
              "Cancelling a call to a single-message guest ends the session"
              [ testCase "cancel during a call"
                <| fun _ ->
                    let entered = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

                    let fake =
                        FakeGuest(
                            guest (),
                            true,
                            (fun _ reply -> reply),
                            fun method cancellation ->
                                if method = validate then
                                    entered.TrySetResult() |> ignore
                                    // The runtime stops the call when the token is cancelled.
                                    Task.Delay(Timeout.Infinite, cancellation)
                                else
                                    Task.CompletedTask
                        )

                    let session = initialized fake
                    use cancellation = new CancellationTokenSource()

                    let pending =
                        session.RequestAsync(Method.Validate, Nullable(JsonRpc.Constant "{}"), cancellation.Token)

                    waitTask entered.Task
                    cancellation.Cancel()

                    match wait pending with
                    | Ok _ -> failtest "The request must end as cancelled"
                    | Error error -> Expect.equal error.Code -32800 "The request ends as cancelled"

                    Expect.isFalse
                        (fake.Methods |> List.contains Method.CancelRequest.WireValue)
                        "No $/cancelRequest is passed to the guest"

                    Expect.equal (failure session) (SessionFailure.Cancelled validate) "The session ends"
                    refusesWithoutCall session fake
                    waitTask (session.ExitAsync())
                    Expect.isFalse (fake.Methods |> List.contains "morphir.exit") "Not even exit reaches the guest" ]

          testCase "C# reads the channel's nullness"
          <| fun _ ->
              use sandbox = TestSandbox.Create("mep-channel-interop-")

              let project =
                  Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../src/Morphir.Mep/Morphir.Mep.fsproj"))

              File.WriteAllText(
                  Path.Combine(sandbox.Work, "Consumer.csproj"),
                  $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><ProjectReference Include="{project}" /></ItemGroup>
</Project>"""
              )

              File.WriteAllText(
                  Path.Combine(sandbox.Work, "BadReply.cs"),
                  """using System; using System.Threading; using System.Threading.Tasks; using Morphir.Mep;
static class BadReply { static async Task Check(ISingleMessageChannel channel) {
    var reply = await channel.RequestAsync("morphir.ping", ReadOnlyMemory<byte>.Empty, CancellationToken.None);
    Console.WriteLine(reply.Length);
} }"""
              )

              File.WriteAllText(
                  Path.Combine(sandbox.Work, "BadChannel.cs"),
                  """using Morphir.Mep;
static class BadChannel { static void Check(PeerInfo host) { new HostSession((ISingleMessageChannel)null, host); } }"""
              )

              File.WriteAllText(
                  Path.Combine(sandbox.Work, "BadFailure.cs"),
                  """using System; using Morphir.Mep;
static class BadFailure { static void Check(HostSession session) { Console.WriteLine(session.FailureOrNull().Method); } }"""
              )

              let configuration =
                  Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()
                  |> nonNull
                  |> _.Configuration

              let start =
                  ChildProcess.StartInfo(
                      "dotnet",
                      "build",
                      "Consumer.csproj",
                      "-c",
                      configuration,
                      "-p:BuildProjectReferences=false",
                      "--nologo"
                  )

              start.WorkingDirectory <- sandbox.Work
              sandbox.Apply start
              Transcripts.consumerBuild.Wait()

              let result =
                  try
                      wait (ChildProcess.RunAsync(start, TimeSpan.FromMinutes 2.0))
                  finally
                      Transcripts.consumerBuild.Release() |> ignore

              Expect.notEqual result.ExitCode 0 "Unsafe C# use must not compile"

              for file, diagnostic in [ "BadReply", "CS8602"; "BadChannel", "CS8625"; "BadFailure", "CS8602" ] do
                  Expect.isTrue
                      (Regex.IsMatch(result.CombinedOutput, file + @"\.cs\([^\r\n]*" + diagnostic))
                      $"The compiler reports {diagnostic} for {file}: {result.CombinedOutput}"

          testCase "C# implements the channel and runs a session on it"
          <| fun _ ->
              let output = wait (Morphir.Mep.AotProbe.MepConsumer.RunAsync())

              for line in
                  [ "single-initialize Session 0.2.0-draft.1 of org.finos.morphir.probe 1.0.0 with morphir-probe 0.0.0 (cli)"
                    "single-ping {\"ok\":true}"
                    "single-shutdown {}"
                    "single-state exited"
                    "single-calls morphir.initialize morphir.initialized morphir.ping morphir.shutdown morphir.exit"
                    "single-failure none"
                    "single-no-reply no-reply morphir.ping -32603"
                    "single-ended stopped morphir.initialize morphir.initialized morphir.ping" ] do
                  Expect.stringContains output line "C# single-message line" ]
