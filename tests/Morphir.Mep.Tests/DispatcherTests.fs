// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.DispatcherTests

open System
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Mep

let private versions = ResizeArray<string>()

let private dispatcher () =
    Dispatcher(
        readOnlyDict
            [ Method.Describe.WireValue,
              Handlers.request MepJson.Default.DescribeParams MepJson.Default.DescribeParams (fun parameters _ ->
                  versions.Add(string parameters.ProtocolVersions.Count)
                  Task.FromResult(Ok parameters))
              Method.Ping.WireValue, Handlers.constant """{"ok":true}"""
              "test.fail", Handlers.raw (fun _ _ -> failwith "Handler failure")
              "test.slow",
              Handlers.raw (fun _ token ->
                  task {
                      do! Task.Delay(Timeout.Infinite, token)
                      return Ok(JsonRpc.Constant "{}")
                  })
              Method.Initialized.WireValue, Handlers.notification (fun _ -> failwith "Notification failure") ]
    )

let private dispatch (target: Dispatcher) (json: string) =
    target.DispatchAsync(ReadOnlyMemory(Encoding.UTF8.GetBytes json), CancellationToken.None).Result
    |> Option.map Encoding.UTF8.GetString

[<Tests>]
let tests =
    testList
        "MEP dispatcher"
        [ testCase "a one-member params object binds as the whole object"
          <| fun _ ->
              let reply =
                  dispatch
                      (dispatcher ())
                      """{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}"""

              Expect.equal
                  reply
                  (Some """{"jsonrpc":"2.0","id":1,"result":{"protocolVersions":["0.2.0-draft.1"]}}""")
                  "Bound and echoed"

          testCase "an unknown method gives -32601 and the next request succeeds"
          <| fun _ ->
              let target = dispatcher ()

              Expect.equal
                  (dispatch target """{"jsonrpc":"2.0","id":1,"method":"morphir.frontend.snippet","params":{}}""")
                  (Some """{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"Method not found"}}""")
                  "Method not found"

              Expect.equal
                  (dispatch target """{"jsonrpc":"2.0","id":2,"method":"morphir.ping"}""")
                  (Some """{"jsonrpc":"2.0","id":2,"result":{"ok":true}}""")
                  "Next request"

          testCase "params that do not match the type give -32602"
          <| fun _ ->
              for parameters in [ """{"protocolVersions":3}"""; "{}"; """{"protocolVersions":["0.2"]}"""; "[1]" ] do
                  let reply =
                      dispatch
                          (dispatcher ())
                          $$"""{"jsonrpc":"2.0","id":"p","method":"morphir.extension.describe","params":{{parameters}}}"""

                  Expect.equal
                      reply
                      (Some """{"jsonrpc":"2.0","id":"p","error":{"code":-32602,"message":"Invalid params"}}""")
                      $"Invalid params {parameters}"

              let absent =
                  dispatch (dispatcher ()) """{"jsonrpc":"2.0","id":"p","method":"morphir.extension.describe"}"""

              Expect.stringContains (absent |> Option.defaultValue "") "-32602" "Absent params"

          testCase "a notification gives no bytes, also when its handler fails"
          <| fun _ ->
              Expect.isNone (dispatch (dispatcher ()) """{"jsonrpc":"2.0","method":"morphir.initialized"}""") "Failing"
              Expect.isNone (dispatch (dispatcher ()) """{"jsonrpc":"2.0","method":"x.unknown"}""") "Unknown"
              Expect.isNone (dispatch (dispatcher ()) """{"jsonrpc":"2.0","method":"x","params":1}""") "Invalid"

          testCase "a response gives no bytes"
          <| fun _ ->
              Expect.isNone (dispatch (dispatcher ()) """{"jsonrpc":"2.0","id":1,"result":{}}""") "Response"

          testCase "a handler exception gives -32603 and the next request succeeds"
          <| fun _ ->
              let target = dispatcher ()

              Expect.equal
                  (dispatch target """{"jsonrpc":"2.0","id":1,"method":"test.fail"}""")
                  (Some """{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"Internal error"}}""")
                  "Internal error, with no exception text"

              Expect.isSome (dispatch target """{"jsonrpc":"2.0","id":2,"method":"morphir.ping"}""") "Next request"

          testCase "bad JSON gets -32700 with id null"
          <| fun _ ->
              Expect.equal
                  (dispatch (dispatcher ()) "{")
                  (Some """{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"Parse error"}}""")
                  "Parse error"

          testCase "a cancelled dispatch gives -32800"
          <| fun _ ->
              use source = new CancellationTokenSource()
              let target = dispatcher ()

              let pending =
                  target.DispatchAsync(
                      ReadOnlyMemory(
                          Encoding.UTF8.GetBytes """{"jsonrpc":"2.0","id":18446744073709551615,"method":"test.slow"}"""
                      ),
                      source.Token
                  )

              source.Cancel()

              Expect.equal
                  (pending.Result |> Option.map Encoding.UTF8.GetString)
                  (Some """{"jsonrpc":"2.0","id":18446744073709551615,"error":{"code":-32800,"message":"Request cancelled"}}""")
                  "Cancelled"

          testCase "a gate refuses a request with its error and drops a notification"
          <| fun _ ->
              let gate =
                  fun (method: string) (_: MethodKind) ->
                      if method = Method.Ping.WireValue then
                          Admission.Refuse(RpcErrors.create ErrorCode.NotInitialized)
                      else
                          Admission.Proceed

              let target =
                  Dispatcher(readOnlyDict [ Method.Ping.WireValue, Handlers.constant """{"ok":true}""" ], gate)

              Expect.equal
                  (dispatch target """{"jsonrpc":"2.0","id":6,"method":"morphir.ping"}""")
                  (Some
                      """{"jsonrpc":"2.0","id":6,"error":{"code":-32014,"message":"Request is not allowed before initialization or after shutdown"}}""")
                  "Refused" ]
