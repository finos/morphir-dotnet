// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.RpcTests

open System
open System.Text
open System.Text.Json.Nodes
open Expecto
open Morphir.Mep

let private read (json: string) =
    JsonRpc.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes json))

let private refusal json =
    match read json with
    | Error refusal -> refusal
    | Ok message -> failtest $"Expected a refusal, got {message}"

let private text (bytes: byte array) = Encoding.UTF8.GetString bytes

[<Tests>]
let tests =
    testList
        "JSON-RPC messages"
        [ testCase "members decide the message kind"
          <| fun _ ->
              match read """{"jsonrpc":"2.0","id":1,"method":"morphir.ping"}""" with
              | Ok(JsonRpcMessage.Request(RequestId.Number 1UL, "morphir.ping", None)) -> ()
              | other -> failtest $"Request expected: {other}"

              match read """{"jsonrpc":"2.0","method":"morphir.initialized"}""" with
              | Ok(JsonRpcMessage.Notification("morphir.initialized", None)) -> ()
              | other -> failtest $"Notification expected: {other}"

              match read """{"jsonrpc":"2.0","id":"a","result":{"ok":true}}""" with
              | Ok(JsonRpcMessage.Response(RequestId.Text "a", result)) ->
                  Expect.equal (result.GetRawText()) """{"ok":true}""" "Raw result"
              | other -> failtest $"Response expected: {other}"

              match read """{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"Parse error"}}""" with
              | Ok(JsonRpcMessage.ErrorResponse(None, error)) -> Expect.equal error.Code -32700 "Code"
              | other -> failtest $"Error response expected: {other}"

          testCase "params stay raw and keep the whole object"
          <| fun _ ->
              match read """{"jsonrpc":"2.0","id":7,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}""" with
              | Ok(JsonRpcMessage.Request(_, _, Some parameters)) ->
                  Expect.equal
                      (parameters.GetRawText())
                      """{"protocolVersions":["0.2.0-draft.1"]}"""
                      "The one-member object is not unwrapped"
              | other -> failtest $"Request expected: {other}"

          testCase "request ids echo exactly"
          <| fun _ ->
              for id in [ "\"req-abc\""; "\"\""; "42"; "9223372036854775808"; "18446744073709551615" ] do
                  match read $$"""{"jsonrpc":"2.0","id":{{id}},"method":"morphir.ping"}""" with
                  | Ok(JsonRpcMessage.Request(requestId, _, _)) ->
                      let reply =
                          JsonRpc.Write(JsonRpcMessage.Response(requestId, JsonRpc.Constant """{"ok":true}"""))

                      Expect.equal (text reply) ("""{"jsonrpc":"2.0","id":""" + id + ""","result":{"ok":true}}""") "Same token"
                  | other -> failtest $"Request expected: {other}"

          testCase "bad ids get -32600 with a null id"
          <| fun _ ->
              for id in [ "-1"; "1.5"; "18446744073709551616"; "true"; "{}"; "null" ] do
                  let refused = refusal $$"""{"jsonrpc":"2.0","id":{{id}},"method":"morphir.ping"}"""
                  Expect.equal refused.Error.Code (int ErrorCode.InvalidRequest) $"Refused id {id}"
                  Expect.isNone refused.Id "No id to echo"

                  Expect.equal
                      (text (JsonRpc.Write(JsonRpc.Reply refused)))
                      """{"jsonrpc":"2.0","id":null,"error":{"code":-32600,"message":"Invalid Request"}}"""
                      "The reply has the id null"

          testCase "bad JSON gets -32700 with a null id"
          <| fun _ ->
              for json in [ "{"; ""; "not json"; "{\"jsonrpc\":\"2.0\",}" ] do
                  let refused = refusal json
                  Expect.equal refused.Error.Code (int ErrorCode.ParseError) $"Parse error for {json}"
                  Expect.isNone refused.Id "No id"
                  Expect.isTrue refused.Answer "A parse error gets a reply"

          testCase "JSON that is not a JSON-RPC message gets -32600"
          <| fun _ ->
              for json, id in
                  [ "[]", None
                    "1", None
                    """{"id":1,"method":"morphir.ping"}""", Some(RequestId.Number 1UL)
                    """{"jsonrpc":"1.0","id":1,"method":"morphir.ping"}""", Some(RequestId.Number 1UL)
                    """{"jsonrpc":"2.0","id":1,"method":3}""", Some(RequestId.Number 1UL)
                    """{"jsonrpc":"2.0","id":1,"method":"morphir.ping","params":3}""", Some(RequestId.Number 1UL)
                    """{"jsonrpc":"2.0","id":1}""", Some(RequestId.Number 1UL)
                    """{"jsonrpc":"2.0","id":1,"result":{},"error":{"code":1,"message":"m"}}""",
                    Some(RequestId.Number 1UL)
                    """{"jsonrpc":"2.0","id":1,"method":"morphir.ping","result":{}}""", Some(RequestId.Number 1UL) ] do
                  let refused = refusal json
                  Expect.equal refused.Error.Code (int ErrorCode.InvalidRequest) $"Invalid request: {json}"
                  Expect.equal refused.Id id $"Id of {json}"

          testCase "a refused response is not answered"
          <| fun _ ->
              let refused = refusal """{"jsonrpc":"2.0","id":-1,"result":{}}"""
              Expect.isFalse refused.Answer "No reply to a response"
              let error = refusal """{"jsonrpc":"2.0","id":1,"error":{"code":"x"}}"""
              Expect.isFalse error.Answer "No reply to an error response"
              let notification = refusal """{"jsonrpc":"2.0","method":"x","params":1}"""
              Expect.isFalse notification.Answer "No reply to a notification"

          testCase "messages write the JSON-RPC member order"
          <| fun _ ->
              let parameters = JsonRpc.Constant """{"id":"x"}"""

              Expect.equal
                  (text (JsonRpc.Write(JsonRpcMessage.Notification("$/cancelRequest", Some parameters))))
                  """{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":"x"}}"""
                  "Notification"

              Expect.equal
                  (text (JsonRpc.Write(JsonRpcMessage.Request(RequestId.Number 0UL, "morphir.shutdown", None))))
                  """{"jsonrpc":"2.0","id":0,"method":"morphir.shutdown"}"""
                  "Request without params"

              let error =
                  RpcErrors.create ErrorCode.NotInitialized

              Expect.equal
                  (text (JsonRpc.Write(JsonRpcMessage.ErrorResponse(Some(RequestId.Number 6UL), error))))
                  """{"jsonrpc":"2.0","id":6,"error":{"code":-32014,"message":"Request is not allowed before initialization or after shutdown"}}"""
                  "Error reply with the transcript text"

          testCase "every transcript message reads and writes the same JSON"
          <| fun _ ->
              for name in Transcripts.all do
                  for entry in Transcripts.read name do
                      match JsonRpc.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes entry.Text)) with
                      | Ok message ->
                          let written = JsonNode.Parse(JsonRpc.Write message)

                          Expect.isTrue
                              (JsonNode.DeepEquals(written, entry.Node))
                              $"{name} entry {entry.Entry} keeps its members"
                      | Error refusal -> failtest $"{name} entry {entry.Entry} was refused: {refusal}" ]
