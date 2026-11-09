// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Contract conformance of the guest as a separate process (task 8.3): the frontend messages of the pinned
/// 0.2.0-draft.1 transcripts and the frontend cases of model-transport.cases.json, with F# documents and JSON
/// tuples. Every reply is read with the codecs that the pinned contract test checks against mep.ir.json.
module Morphir.Frontend.FSharp.Mep.Tests.ProcessReplayTests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Frontend.FSharp.Mep
open Morphir.Frontend.FSharp.Mep.Tests.GuestSupport
open Morphir.Mep

/// The guest executable, started with explicit arguments and no shell.
type private GuestProcess() =
    let host =
        match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
        | null -> "dotnet"
        | path -> path

    let start =
        let info = ProcessStartInfo(host)
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-frontend.dll"))
        info.RedirectStandardInput <- true
        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        info.UseShellExecute <- false
        info

    let child = Process.Start start |> nonNull
    let errors = child.StandardError.ReadToEndAsync()
    let input = child.StandardInput.BaseStream
    let output = child.StandardOutput.BaseStream

    member _.Send(message: string) =
        ContentLengthFraming.WriteAsync(input, ReadOnlyMemory(Encoding.UTF8.GetBytes message), CancellationToken.None).Wait()

    member _.Receive() : JsonNode =
        use timeout = new CancellationTokenSource(TimeSpan.FromMinutes 2.0)

        match ContentLengthFraming.ReadAsync(output, timeout.Token).GetAwaiter().GetResult() with
        | FrameRead.Message body -> JsonNode.Parse(ReadOnlySpan body) |> nonNull
        | other -> failtest $"Expected a reply frame: {other}"

    /// Ends the session: the process must exit after morphir.exit.
    member _.WaitForExit() =
        input.Close()

        if not (child.WaitForExit(TimeSpan.FromSeconds 30.0)) then
            child.Kill true
            failtest "The guest did not exit after morphir.exit"

        child.ExitCode, errors.Result

    interface IDisposable with
        member _.Dispose() =
            if not child.HasExited then
                child.Kill true
                child.WaitForExit()

            child.Dispose()

type private Direction =
    | HostToExtension
    | ExtensionToHost

/// The messages of a pinned transcript, in order.
let private transcript name =
    let text = File.ReadAllText(fixture name).ReplaceLineEndings "\n"

    [ for item in Regex.Matches(text, "```jsonrpc (host -> extension|extension -> host)\n(.*?)\n```", RegexOptions.Singleline) ->
          (if item.Groups[1].Value.StartsWith "host" then HostToExtension else ExtensionToHost), item.Groups[2].Value ]

/// The methods of the 0.2.0-draft.1 frontend subset (MEP-1). Other messages are not replayed against a
/// frontend-only guest.
let private frontendSubset =
    set
        [ "morphir.extension.describe"
          "morphir.initialize"
          "morphir.initialized"
          "morphir.extension.info"
          "morphir.extension.capabilities"
          "morphir.frontend.compileModel"
          "morphir.shutdown"
          "morphir.exit"
          "morphir.ping"
          "$/cancelRequest" ]

let private methodOf (node: JsonNode) =
    match node["method"] with
    | null -> None
    | value -> Some(value.GetValue<string>())

/// The message with JSON tuples, MEP 0.2.0-draft.1 and F# documents in place of the pinned Ion tuples, the legacy
/// version and the MoonBit or Elm documents.
let private toFSharp (node: JsonNode) =
    let message = JsonNode.Parse(node.ToJsonString().Replace("\"0.1\"", "\"0.2.0-draft.1\"")) |> nonNull

    match methodOf message with
    | Some "morphir.initialize" ->
        let parameters = message["params"] |> nonNull

        parameters["protocolVersions"] <- JsonNode.Parse """["0.2.0-draft.1"]"""

        match parameters["modelTransport"] with
        | null -> ()
        | transport -> transport["tuples"] <- JsonNode.Parse $"""[{jsonTuple "4.0.0"},{jsonTuple "3.0.0"}]"""

        match parameters["host"] with
        | null -> ()
        | hostInfo when isNull hostInfo["kind"] -> hostInfo["kind"] <- JsonValue.Create "cli"
        | _ -> ()

        message
    | Some "morphir.frontend.compileModel" ->
        let version =
            match (message["params"] |> nonNull)["options"] with
            | null -> "4.0.0"
            | options -> (options["irVersion"] |> nonNull).GetValue<string>()

        let request = compileRequest version [ "Pricing.fs", pricing ] None
        message["params"] <- JsonSerializer.SerializeToNode(request, MepJson.Default.CompileModelRequest)
        message
    | _ -> message

let private codeOf (reply: JsonNode) =
    match reply["error"] with
    | null -> None
    | error -> Some((error["code"] |> nonNull).GetValue<int>())

let private read<'T when 'T: not null> (info: Serialization.Metadata.JsonTypeInfo<'T>) (node: JsonNode) =
    match JsonSerializer.Deserialize(node.ToJsonString(), info) with
    | null -> failtest "The reply does not read"
    | value -> value

/// Checks a successful reply with the codec of its method. The codecs are the ones that the pinned contract
/// test checks against mep.ir.json.
let private checkResult (agreed: ModelTransportSupport option ref) (method: string) (result: JsonNode) =
    match method with
    | "morphir.initialize" ->
        let initialized = read MepJson.Default.InitializeResult result
        Expect.isNone initialized.Capabilities.Backend "The guest offers no backend"
        Expect.isNone initialized.Capabilities.Workspace "The guest offers no workspace discovery"
        Expect.isFalse initialized.Capabilities.Streaming "No streaming"
        Expect.isFalse initialized.Capabilities.Cancellation "No cancellation"
        Expect.isFalse initialized.Capabilities.Progress "No progress"

        // A host without a modelTransport offer is legacy-only, and the guest agrees no tuple.
        match initialized.Capabilities.ModelTransport with
        | Some transport ->
            for tuple in transport.Tuples do
                Expect.contains FSharpGuest.support.Tuples tuple "Every agreed tuple is one the guest serves"

            agreed.Value <- Some transport
        | None -> agreed.Value <- None
    | "morphir.extension.info" -> read MepJson.Default.ExtensionInfo result |> ignore
    | "morphir.extension.capabilities" -> read MepJson.Default.ExtensionCapabilities result |> ignore
    | "morphir.frontend.compileModel" ->
        use document = JsonDocument.Parse(result.ToJsonString())

        match CompileModel.readResult document.RootElement with
        | Ok(CompileModelResult.Compiled(model, _, _)) ->
            let transport = agreed.Value |> Option.defaultWith (fun () -> failtest "compileModel after initialize")
            let tuple = ModelTransport.tupleOf ModelOperation.CompileModel model
            Expect.isOk (ModelTransport.admit tuple transport.Limits model) "The envelope is admitted"
        | Ok failed -> failtest $"The F# document compiles: {failed}"
        | Error failure -> failtest $"The result is refused: {failure}"
    | _ -> ()

/// Replays the frontend messages of a pinned transcript against a new guest process.
let private replay (name: string) =
    use guest = new GuestProcess()
    let agreed = ref None
    let messages = transcript name

    let replies =
        messages
        |> List.choose (function
            | ExtensionToHost, text -> Some(JsonNode.Parse text |> nonNull)
            | _ -> None)

    let mutable exited = false

    for direction, text in messages do
        let original = JsonNode.Parse text |> nonNull

        match direction, methodOf original with
        | HostToExtension, Some method when frontendSubset.Contains method ->
            let message = toFSharp original
            guest.Send(message.ToJsonString())

            match original["id"] with
            | null -> exited <- exited || method = "morphir.exit"
            | id ->
                let reply = guest.Receive()
                Expect.isTrue (JsonNode.DeepEquals(reply["id"], id)) $"{name} {method}: the same id"
                Expect.equal ((reply["jsonrpc"] |> nonNull).ToJsonString()) "\"2.0\"" "jsonrpc 2.0"

                let expected =
                    replies |> List.tryFind (fun candidate -> JsonNode.DeepEquals(candidate["id"], id))

                let expectedCode = expected |> Option.bind codeOf

                match codeOf reply, expectedCode with
                | Some actual, Some wanted -> Expect.equal actual wanted $"{name} {method}: the error code"
                | Some actual, None -> failtest $"{name} {method}: unexpected error {actual}: {reply}"
                | None, Some wanted -> failtest $"{name} {method}: expected error {wanted}: {reply}"
                | None, None ->
                    match reply["result"] with
                    | null -> failtest $"{name} {method}: a reply has a result or an error"
                    | result -> checkResult agreed method result
        | _ -> ()

    if exited then
        let code, _ = guest.WaitForExit()
        Expect.equal code 0 $"{name}: the guest exits with code 0 after morphir.exit"

let private cases =
    JsonNode.Parse(File.ReadAllText(fixture "model-transport.cases.json")) |> nonNull

[<Tests>]
let tests =
    testSequenced
    <| testList
        "F# MEP guest process"
        [ for name in [ "session-model-transport.md"; "lifecycle-violations.md"; "protocol-errors.md" ] do
              yield
                  testCase $"The frontend messages of {name} replay against the guest process"
                  <| fun _ -> replay name

          yield testCase "The request-side compileModel cases of model-transport.cases.json give their pinned verdicts"
          <| fun _ ->
              use guest = new GuestProcess()
              guest.Send(initialize [ jsonTuple "4.0.0"; jsonTuple "3.0.0" ])
              Expect.isNone (codeOf (guest.Receive())) "initialize"
              guest.Send """{"jsonrpc":"2.0","method":"morphir.initialized","params":{}}"""

              let mutable id = 10

              for item in (cases["cases"] |> nonNull).AsArray() |> Seq.map nonNull do
                  let request = item["request"]
                  let result = item["result"]

                  let code = ((item["expected"] |> nonNull)["code"] |> nonNull).GetValue<string>()

                  // Request-side cases whose verdict does not depend on the Ion profile. The compatibility cases
                  // check Ion-to-JSON losses, which JSON tuples cannot have.
                  match request with
                  | NonNull request when
                      methodOf request = Some "morphir.frontend.compileModel"
                      && isNull result
                      && (code = "accepted" || code = "ir_version_mismatch")
                      ->
                      // A request-side case: the guest admits or refuses the request itself.
                      let parameters = request["params"] |> nonNull
                      let options = (parameters["options"] |> nonNull)["irVersion"] |> nonNull
                      let output = (parameters["output"] |> nonNull)["irVersion"] |> nonNull
                      let fsharp = compileRequest (output.GetValue<string>()) [ "Pricing.fs", pricing ] None

                      let sent =
                          { fsharp with
                              Options =
                                  { fsharp.Options with
                                      IrVersion = SemanticVersion.Create(options.GetValue<string>()) } }

                      id <- id + 1
                      guest.Send(compileMessage id sent)
                      let reply = guest.Receive()
                      let caseId = (item["id"] |> nonNull).GetValue<string>()

                      match ((item["expected"] |> nonNull)["admission"] |> nonNull).GetValue<string>() with
                      | "accept" -> Expect.isNone (codeOf reply) $"{caseId} is accepted"
                      | _ -> Expect.equal (codeOf reply) (Some -32602) $"{caseId} is refused with -32602"
                  | _ -> ()

              guest.Send """{"jsonrpc":"2.0","id":99,"method":"morphir.shutdown"}"""
              Expect.isNone (codeOf (guest.Receive())) "shutdown"
              guest.Send """{"jsonrpc":"2.0","method":"morphir.exit"}"""
              Expect.equal (fst (guest.WaitForExit())) 0 "exit code"

          yield testCase "The guest writes only frames to stdout, also when the frontend runs"
          <| fun _ ->
              use guest = new GuestProcess()
              guest.Send(initialize [ jsonTuple "4.0.0" ])
              guest.Receive() |> ignore
              guest.Send(compileMessage 1 (compileRequest "4.0.0" [ "Pricing.fs", pricing ] None))
              let reply = guest.Receive()
              Expect.isNone (codeOf reply) "compileModel"
              guest.Send """{"jsonrpc":"2.0","id":2,"method":"morphir.shutdown"}"""
              guest.Receive() |> ignore
              guest.Send """{"jsonrpc":"2.0","method":"morphir.exit"}"""
              Expect.equal (fst (guest.WaitForExit())) 0 "Every reply was a frame, and the guest exited cleanly" ]
