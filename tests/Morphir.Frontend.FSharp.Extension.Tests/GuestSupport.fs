// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Helpers that drive the F# guest through its dispatcher with JSON-RPC text, as a host would.
module Morphir.Frontend.FSharp.Extension.Tests.GuestSupport

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Frontend.FSharp.Extension
open Morphir.Mep

let fixture name =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mep", name)

/// Sends one message to the guest and returns its reply, or None for a notification.
let dispatch (guest: Guest) (message: string) : JsonNode option =
    guest.Dispatcher.DispatchAsync(ReadOnlyMemory(Encoding.UTF8.GetBytes message), CancellationToken.None).Result
    |> Option.ofObj
    |> Option.map (fun bytes -> JsonNode.Parse(bytes) |> nonNull)

let hostLimits =
    """{"maxEncodedBytes":67108864,"maxDecodedBytes":50331648,"maxDepth":4096,"maxNodes":50000000}"""

let jsonTuple (version: string) =
    $$"""{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"{{version}}"}"""

/// An initialize request that offers the given tuples, in that order.
let initialize (tuples: string list) =
    let offered = String.Join(",", tuples)

    """{"jsonrpc":"2.0","id":0,"method":"morphir.initialize","params":{"protocolVersions":["0.2.0-draft.1"],"host":{"kind":"cli","name":"morphir-dotnet-tests","version":"1.0.0"},"modelTransport":{"tuples":["""
    + offered
    + """],"limits":"""
    + hostLimits
    + "}}}"

/// A guest after initialize and initialized with JSON tuples for 4.0.0 and 3.0.0.
let initialized () =
    let guest = FSharpGuest.registration ()
    let reply = dispatch guest (initialize [ jsonTuple "4.0.0"; jsonTuple "3.0.0" ]) |> Option.get
    Expect.isNull reply["error"] $"initialize succeeds: {reply}"
    dispatch guest """{"jsonrpc":"2.0","method":"morphir.initialized","params":{}}""" |> ignore
    guest

/// A compileModel request for F# documents, with the documented context digest.
let compileRequest (version: string) (documents: (string * string) list) (exposed: string list option) =
    let request: CompileModelRequest =
        { LanguageId = "fsharp"
          Sources =
            { Root = None
              Documents =
                [| for uri, text in documents ->
                       { Uri = uri
                         LanguageId = "fsharp"
                         Version = 1UL
                         Text = text } |] }
          Package =
            { Name = "acme/demo"
              ExposedModules = exposed |> Option.map Array.ofList |> Option.map (fun items -> items :> Collections.Generic.IReadOnlyList<string>) }
          Dependencies = [||]
          Options =
            { TypesOnly = false
              IrVersion = SemanticVersion.Create version
              Extra = Collections.Generic.Dictionary() }
          ContextDigest = Sha256Digest.Create(String('0', 64))
          Output = FSharpGuest.tuple version }

    { request with
        ContextDigest = CompileModel.contextDigest request }

/// The JSON-RPC text of a compileModel request.
let compileMessage (id: int) (request: CompileModelRequest) =
    let parameters = JsonSerializer.Serialize(request, MepJson.Default.CompileModelRequest)
    $$"""{"jsonrpc":"2.0","id":{{id}},"method":"morphir.frontend.compileModel","params":{{parameters}}}"""

let element (node: JsonNode) =
    use document = JsonDocument.Parse(node.ToJsonString())
    document.RootElement.Clone()

/// The result of a compileModel reply, read as the result sum.
let resultOf (reply: JsonNode) =
    match reply["result"] with
    | null -> failtest $"Expected a result: {reply}"
    | result ->
        match CompileModel.readResult (element result) with
        | Ok value -> value
        | Error failure -> failtest $"The result is refused: {failure}"

let pricing =
    """namespace Acme.Demo

module Pricing =
    type Size =
        | Small
        | Large of seats: int

    let seats (size: Size) : int =
        match size with
        | Small -> 2
        | Large n -> n
"""
