// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Contract conformance of the guest as a separate process (task 8.3): the frontend messages of the pinned
/// 0.2.0-draft.1 transcripts and the frontend cases of model-transport.cases.json, with F# documents and JSON
/// tuples. Every reply is read with the codecs that the pinned contract test checks against mep.ir.json.
module Morphir.Frontend.FSharp.Mep.Tests.ProcessReplayTests

open System
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
open Morphir.TestSupport

/// Runs the guest as a separate process through the shared test harness (the process gate, a deadline and
/// process-tree cleanup) inside a test sandbox. The host messages go to stdin as Content-Length frames, the guest
/// reads them in order, and stdin closes after the last one. The result is the reply frames in order and the exit
/// code.
let internal exchange (messages: string list) : JsonNode list * int =
    let host =
        match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
        | null -> "dotnet"
        | path -> path

    use sandbox = TestSandbox.Create "morphir-guest-"
    let start = ChildProcess.StartInfo(host, Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-frontend.dll"))
    start.WorkingDirectory <- sandbox.Work
    start.StandardInputEncoding <- UTF8Encoding false
    start.StandardOutputEncoding <- UTF8Encoding false
    sandbox.Apply start

    let input =
        messages
        |> List.map (fun message -> $"Content-Length: {Encoding.UTF8.GetByteCount message}\r\n\r\n{message}")
        |> String.concat ""

    let result =
        ChildProcess.RunAsync(start, TimeSpan.FromMinutes 3.0, CancellationToken.None, null, input).GetAwaiter().GetResult()

    use output = new MemoryStream(Encoding.UTF8.GetBytes result.StandardOutput)

    let rec frames acc =
        match ContentLengthFraming.ReadAsync(output, CancellationToken.None).GetAwaiter().GetResult() with
        | FrameRead.Message body -> frames ((JsonNode.Parse(ReadOnlySpan body) |> nonNull) :: acc)
        | FrameRead.EndOfStream -> List.rev acc
        | FrameRead.Refused failure -> failtest $"The guest wrote output that is not a frame ({failure}): {result.CombinedOutput}"

    frames [], result.ExitCode

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
    let agreed = ref None
    let messages = transcript name

    let expectedReplies =
        messages
        |> List.choose (function
            | ExtensionToHost, text -> Some(JsonNode.Parse text |> nonNull)
            | _ -> None)

    let sent =
        messages
        |> List.choose (fun (direction, text) ->
            let original = JsonNode.Parse text |> nonNull

            match direction, methodOf original with
            | HostToExtension, Some method when frontendSubset.Contains method -> Some(method, original)
            | _ -> None)

    // The guest ends the session at morphir.exit and reads nothing after it.
    let upToExit =
        match sent |> List.tryFindIndex (fun (method, _) -> method = "morphir.exit") with
        | Some index -> List.take (index + 1) sent
        | None -> sent

    let replies, exitCode =
        exchange [ for _, original in upToExit -> (toFSharp original).ToJsonString() ]

    let requests = upToExit |> List.filter (fun (_, original) -> not (isNull original["id"]))
    Expect.equal replies.Length requests.Length $"{name}: one reply for each request"

    // The guest can answer requests in another order than it got them, so replies are matched by id.
    for method, original in requests do
        let id = original["id"]

        let reply =
            replies
            |> List.tryFind (fun candidate -> JsonNode.DeepEquals(candidate["id"], id))
            |> Option.defaultWith (fun () -> failtest $"{name} {method}: no reply with the id {id}")

        Expect.equal ((reply["jsonrpc"] |> nonNull).ToJsonString()) "\"2.0\"" "jsonrpc 2.0"

        let expectedCode =
            expectedReplies
            |> List.tryFind (fun candidate -> JsonNode.DeepEquals(candidate["id"], id))
            |> Option.bind codeOf

        match codeOf reply, expectedCode with
        | Some actual, Some wanted -> Expect.equal actual wanted $"{name} {method}: the error code"
        | Some actual, None -> failtest $"{name} {method}: unexpected error {actual}: {reply}"
        | None, Some wanted -> failtest $"{name} {method}: expected error {wanted}: {reply}"
        | None, None ->
            match reply["result"] with
            | null -> failtest $"{name} {method}: a reply has a result or an error"
            | result -> checkResult agreed method result

    if upToExit |> List.exists (fun (method, _) -> method = "morphir.exit") then
        Expect.equal exitCode 0 $"{name}: the guest exits with code 0 after morphir.exit"

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
              let selected =
                  [ for item in (cases["cases"] |> nonNull).AsArray() |> Seq.map nonNull do
                        let code = ((item["expected"] |> nonNull)["code"] |> nonNull).GetValue<string>()

                        // Request-side cases whose verdict does not depend on the Ion profile. The compatibility
                        // cases check Ion-to-JSON losses, which JSON tuples cannot have.
                        match item["request"] with
                        | NonNull request when
                            methodOf request = Some "morphir.frontend.compileModel"
                            && isNull item["result"]
                            && (code = "accepted" || code = "ir_version_mismatch")
                            ->
                            yield item, request
                        | _ -> () ]

              Expect.isNonEmpty selected "The pin has request-side compileModel cases"

              let compiles =
                  [ for index, (_, request) in List.indexed selected ->
                        let parameters = request["params"] |> nonNull
                        let options = (parameters["options"] |> nonNull)["irVersion"] |> nonNull
                        let output = (parameters["output"] |> nonNull)["irVersion"] |> nonNull
                        let fsharp = compileRequest (output.GetValue<string>()) [ "Pricing.fs", pricing ] None

                        let sent =
                            { fsharp with
                                Options =
                                    { fsharp.Options with
                                        IrVersion = SemanticVersion.Create(options.GetValue<string>()) } }

                        compileMessage (10 + index) sent ]

              let replies, exitCode =
                  exchange (
                      [ initialize [ jsonTuple "4.0.0"; jsonTuple "3.0.0" ]
                        """{"jsonrpc":"2.0","method":"morphir.initialized","params":{}}""" ]
                      @ compiles
                      @ [ """{"jsonrpc":"2.0","id":99,"method":"morphir.shutdown"}"""
                          """{"jsonrpc":"2.0","method":"morphir.exit"}""" ]
                  )

              Expect.equal replies.Length (selected.Length + 2) "initialize, each compile and shutdown"

              let byId (id: int) =
                  replies
                  |> List.find (fun reply -> (reply["id"] |> nonNull).GetValue<int>() = id)

              Expect.isNone (codeOf (byId 0)) "initialize"

              for index, (item, _) in List.indexed selected do
                  let reply = byId (10 + index)
                  let caseId = (item["id"] |> nonNull).GetValue<string>()

                  match ((item["expected"] |> nonNull)["admission"] |> nonNull).GetValue<string>() with
                  | "accept" -> Expect.isNone (codeOf reply) $"{caseId} is accepted"
                  | _ -> Expect.equal (codeOf reply) (Some -32602) $"{caseId} is refused with -32602"

              Expect.isNone (codeOf (byId 99)) "shutdown"
              Expect.equal exitCode 0 "exit code"

          yield testCase "The guest writes only frames to stdout, also when the frontend runs"
          <| fun _ ->
              let replies, exitCode =
                  exchange
                      [ initialize [ jsonTuple "4.0.0" ]
                        compileMessage 1 (compileRequest "4.0.0" [ "Pricing.fs", pricing ] None)
                        """{"jsonrpc":"2.0","id":2,"method":"morphir.shutdown"}"""
                        """{"jsonrpc":"2.0","method":"morphir.exit"}""" ]

              Expect.equal replies.Length 3 "Every reply is a frame"

              let compile =
                  replies |> List.find (fun reply -> (reply["id"] |> nonNull).GetValue<int>() = 1)

              Expect.isNone (codeOf compile) "compileModel"
              Expect.equal exitCode 0 "The guest exited cleanly" ]
