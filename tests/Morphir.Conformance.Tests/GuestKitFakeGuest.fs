// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// A scripted single-message guest for the generic guest kit's model tier (task 3.5), and the pinned kit.
module Morphir.Conformance.Tests.GuestKitFakeGuest

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Mep
open Morphir.Conformance.WasmHost
open Morphir.Conformance.GuestKit

/// The pinned kit directory in the checkout.
let kitDirectory =
    Path.Combine(Repository.root, "tests", "Fixtures", "MepGuestKit")

/// The bytes of the pinned kit file.
let kitBytes () =
    File.ReadAllBytes(Path.Combine(kitDirectory, "guest-generic-json.json"))

/// The pinned kit, read by the runner's reader.
let kit =
    lazy
        (match GuestKit.Parse(kitBytes ()) with
         | Ok kit -> kit
         | Error issues ->
             failtest (
                 issues
                 |> Seq.map string
                 |> String.concat "\n"
             ))

let private parse (text: string) =
    match Json.parse text with
    | Ok value -> value
    | Error message -> failtest message

/// The pinned kit file as a JSON value.
let kitJson () =
    parse (Encoding.UTF8.GetString(kitBytes ()))

let str (text: string) = JsonValue.String text
let num (value: int) = JsonValue.Number(string value)
let arr (items: JsonValue list) = JsonValue.Array items
let obj (members: (string * JsonValue) list) = JsonValue.Object members

/// A member of an object, or None.
let memberOf (name: string) (value: JsonValue) =
    match value with
    | JsonValue.Object members ->
        members
        |> List.tryFind (fun (key, _) -> key = name)
        |> Option.map snd
    | _ -> None

/// The value at a path of member names.
let at (names: string list) (value: JsonValue) =
    names
    |> List.fold (fun found name -> found |> Option.bind (memberOf name)) (Some value)
    |> Option.defaultWith (fun () -> failtest $"""no member at {String.concat "/" names}""")

/// The value with the member at a path of member names replaced.
let rec replaceAt (names: string list) (replacement: JsonValue) (value: JsonValue) =
    match names, value with
    | [], _ -> replacement
    | name :: rest, JsonValue.Object members ->
        JsonValue.Object(
            members
            |> List.map (fun (key, item) ->
                if key = name then
                    key, replaceAt rest replacement item
                else
                    key, item
            )
        )
    | _ -> failtest $"""cannot replace {String.concat "/" names}"""

/// The value with every object's members in reverse order, at every depth.
let rec reverseMembers (value: JsonValue) =
    match value with
    | JsonValue.Object members ->
        JsonValue.Object(
            members
            |> List.rev
            |> List.map (fun (key, item) -> key, reverseMembers item)
        )
    | JsonValue.Array items -> JsonValue.Array(List.map reverseMembers items)
    | other -> other

/// The kit's expected IR for a version.
let expectedIr (version: string) =
    kitJson ()
    |> at [ "expectedIrVersions"; version ]

/// The kit's Python source document.
let pythonSource () =
    match
        kitJson ()
        |> at [ "languages"; "python"; "documents" ]
    with
    | JsonValue.Array(document :: _) -> document |> at [ "text" ]
    | _ -> failtest "no python document"

/// What the scripted guest answers to a request: a result, or a JSON-RPC error code and message.
type Reply = Result<JsonValue, int * string>

/// A single-message channel that answers each request from a script and records what it was sent.
type ScriptedChannel(answer: string -> JsonValue -> Reply) =
    let requests = List<string * JsonValue>()
    let notifications = List<string>()
    let mutable shutDown = false

    /// The requests the guest received, as method and params.
    member _.Requests = List.ofSeq requests

    /// The notifications the host passed to the channel. None reaches the guest.
    member _.Notifications = List.ofSeq notifications

    interface ISingleMessageChannel with
        member _.RequestAsync(method, message, _cancellation) =
            let parsed = parse (Encoding.UTF8.GetString message.Span)
            let id = parsed |> at [ "id" ]

            let parameters =
                memberOf "params" parsed
                |> Option.defaultValue JsonValue.Null

            requests.Add((method, parameters))

            let body =
                let reply = if shutDown then Error(-32014, "The scripted peer is shut down.") else answer method parameters
                if method = "morphir.shutdown" && Result.isOk reply then shutDown <- true
                match reply with
                | Ok result -> obj [ "jsonrpc", str "2.0"; "id", id; "result", result ]
                | Error(code, text) ->
                    obj [
                        "jsonrpc", str "2.0"
                        "id", id
                        "error", obj [ "code", num code; "message", str text ]
                    ]

            Task.FromResult<byte array>(Encoding.UTF8.GetBytes(Json.compact body))

        member _.NotifyAsync(method, _message, _cancellation) =
            notifications.Add method
            Task.FromResult<byte array>(null)

/// How the scripted generic guest behaves.
type FakeGuest = {
    /// The IR versions its frontend and backend claim, as written: "3", "4".
    IrVersions: string list
    /// The IR versions it claims JSON compileModel and generateModel tuples for: "3.0.0", "4.0.0".
    ModelVersions: string list
    /// The protocol versions it claims in describe.
    ProtocolVersions: string list
    /// The IR it compiles, from the IR version and the kit's expected IR.
    Compile: string -> JsonValue -> JsonValue
    /// Whether it answers a payload with a wrong SHA-256 with a result instead of -32602.
    AcceptBadDigest: bool
    /// The extension version it gives in initialize; describe always gives 0.1.0.
    InitializeVersion: string
}

/// A guest that compiles the kit's expected IR for both versions.
let faithful = {
    IrVersions = [ "3"; "4" ]
    ModelVersions = [ "3.0.0"; "4.0.0" ]
    ProtocolVersions = [ "0.2.0-draft.1"; "0.1" ]
    Compile = fun _ expected -> expected
    AcceptBadDigest = false
    InitializeVersion = "0.1.0"
}

let private limits =
    obj [
        "maxEncodedBytes", num 1000000
        "maxDecodedBytes", num 750000
        "maxDepth", num 128
        "maxNodes", num 100000
    ]

let private tuple (operation: string) (version: string) =
    obj [
        "operation", str operation
        "profile", str "morphir-ir"
        "codec", str "morphir-json"
        "envelopeVersion", str "0.1.0-draft.1"
        "irVersion", str version
    ]

let private extension (version: string) =
    obj [
        "id", str "morphir-fake"
        "name", str "Fake guest"
        "version", str version
        "types", arr [ str "frontend"; str "backend" ]
    ]

let private capabilities (guest: FakeGuest) (transport: JsonValue) =
    obj [
        "frontend",
        obj [
            "languages", arr [ obj [ "id", str "python"; "fileExtensions", arr [ str ".py" ] ] ]
            "irVersions", arr (List.map str guest.IrVersions)
            "compile", JsonValue.Bool true
            "incremental", JsonValue.Bool false
            "fragments", JsonValue.Bool false
        ]
        "backend",
        obj [
            "targets", arr [ str "python" ]
            "irVersions", arr (List.map str guest.IrVersions)
            "generate", JsonValue.Bool true
        ]
        "modelTransport", transport
    ]

let private claimed (guest: FakeGuest) =
    obj [
        "tuples",
        arr [
            for version in guest.ModelVersions do
                tuple "morphir.frontend.compileModel" version
                tuple "morphir.backend.generateModel" version
        ]
        "limits", limits
        "guarantees", arr []
    ]

let private payload (value: JsonValue) =
    let bytes = Encoding.UTF8.GetBytes(Json.compact value)

    obj [
        "codec", str "morphir-json"
        "layout", str "single-document"
        "decodedByteLength", num bytes.Length
        "sha256", str (Convert.ToHexStringLower(SHA256.HashData bytes))
        "content", str (Convert.ToBase64String bytes)
    ]

/// The script of a generic guest.
let answer (guest: FakeGuest) (method: string) (parameters: JsonValue) : Reply =
    match method with
    | "morphir.extension.describe" ->
        Ok(
            obj [
                "claimsVersion", str "0.1.0-draft.2"
                "protocolVersions", arr (List.map str guest.ProtocolVersions)
                "extension", extension "0.1.0"
                "capabilities", capabilities guest (claimed guest)
            ]
        )
    | "morphir.initialize" ->
        Ok(
            obj [
                "protocolVersion", str "0.2.0-draft.1"
                "extension", extension guest.InitializeVersion
                "capabilities", capabilities guest (parameters |> at [ "modelTransport" ])
            ]
        )
    | "morphir.frontend.compileModel" ->
        let version =
            match parameters |> at [ "options"; "irVersion" ] with
            | JsonValue.String version -> version
            | _ -> failtest "irVersion is not a string"

        let malformed = kitJson () |> at [ "languages"; "python"; "malformed" ]
        let isMalformed =
            match malformed, parameters |> at [ "sources"; "documents" ] with
            | JsonValue.Array expected, JsonValue.Array actual ->
                expected |> List.exists (fun source -> actual |> List.exists (fun document -> at [ "text" ] source = at [ "text" ] document))
            | _ -> false
        if isMalformed then
            Ok(obj [ "success", JsonValue.Bool false; "diagnostics", arr [ obj [ "severity", str "error"; "message", str "Malformed fixed source" ] ] ])
        else Ok(
            obj [
                "success", JsonValue.Bool true
                "diagnostics", arr []
                "model",
                obj [
                    "envelopeVersion", str "0.1.0-draft.1"
                    "modelId", str "urn:fake:generic"
                    "revision", str "r1"
                    "irVersion", str version
                    "profile", str "morphir-ir"
                    "payload", payload (guest.Compile version (expectedIr version))
                ]
            ]
        )
    | "morphir.backend.generateModel" ->
        let digest = parameters |> at [ "model"; "payload"; "sha256" ]

        if
            digest = str (String('0', 64))
            && not guest.AcceptBadDigest
        then
            Error(-32602, "The payload digest does not match.")
        else
            Ok(
                obj [
                    "success", JsonValue.Bool true
                    "diagnostics", arr []
                    "artifacts", arr [ obj [ "path", str "main.py"; "content", pythonSource () ] ]
                ]
            )
    | "morphir.shutdown" -> Ok JsonValue.Null
    | other -> Error(-32601, $"Method not found: {other}")

/// The host the sessions introduce.
let host = {
    Kind = PeerKind.Cli
    Name = "morphir-conformance-tests"
    Version = "0.0.0"
}

/// The sessions a run opened: what the runner asked for and the channel that served it.
type Opened = List<KitSessionRequest * ScriptedChannel>

/// A session factory that serves each session with a new scripted channel.
let factory (guest: FakeGuest) (opened: Opened) =
    Func<KitSessionRequest, CancellationToken, Task<HostSession>>(fun request _ ->
        let channel = ScriptedChannel(answer guest)
        opened.Add((request, channel))
        Task.FromResult(request.CreateSession(channel :> ISingleMessageChannel, host))
    )

/// Runs the model tier of the pinned kit on a scripted guest.
let run (guest: FakeGuest) =
    let opened = Opened()

    let result =
        ModelTierRunner
            .RunAsync(kit.Value, factory guest opened, CancellationToken.None)
            .GetAwaiter()
            .GetResult()

    result, List.ofSeq opened

/// The step with this id.
let step (id: string) (run: ModelTierRun) =
    run.Steps
    |> Seq.tryFind (fun step -> step.Id = id)
    |> Option.defaultWith (fun () ->
        failtest $"""no step {id}; steps: {run.Steps |> Seq.map _.Id |> String.concat ", "}"""
    )

