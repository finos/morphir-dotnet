// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The F# backend guest through its dispatcher: its exact claims, its identity, negotiation, generation for each
/// claimed tuple, and the refusals of requests that the session does not admit.
module Morphir.CodeGeneration.FSharp.Extension.Tests.GuestTests

open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open Expecto
open Morphir.CodeGeneration.FSharp.Extension
open Morphir.CodeGeneration.FSharp.Extension.Tests.GuestSupport
open Morphir.Extensions
open Morphir.Mep
open Morphir.Tooling.Features.Extensions
open Morphir.Tooling.Features.Generate

let private text (node: JsonNode | null) = (node |> nonNull).ToJsonString()

let private same (expected: string) (node: JsonNode | null) =
    JsonNode.DeepEquals(node, JsonNode.Parse expected)

[<Tests>]
let tests =
    testList
        "F# backend MEP guest"
        [ testCase "The claim set is exactly MEP 0.2.0-draft.1, the target fsharp for IR 4.0.0 and 3.0.0, and JSON generateModel tuples for 4.0.0 and 3.0.0"
          <| fun _ ->
              let guest = FSharpGuest.registration ()

              let reply =
                  dispatch
                      guest
                      """{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}"""
                  |> Option.get

              let claims = reply["result"] |> nonNull
              Expect.equal (text claims["claimsVersion"]) "\"0.1.0-draft.2\"" "claims version"
              Expect.equal (text claims["protocolVersions"]) """["0.2.0-draft.1"]""" "protocol versions"
              Expect.isNull claims["requires"] "No host requirement"
              Expect.isNull claims["critical"] "No critical member"

              let extension = claims["extension"] |> nonNull
              Expect.equal (text extension["id"]) "\"org.finos.morphir.fsharp-backend\"" "extension id"
              Expect.equal (text extension["types"]) """["backend"]""" "extension types"

              let capabilities = (claims["capabilities"] |> nonNull).AsObject()

              Expect.equal
                  (capabilities |> Seq.map _.Key |> Set.ofSeq)
                  (set [ "backend"; "modelTransport" ])
                  "Only the backend and model-transport claims"

              let backend = capabilities["backend"]

              Expect.isTrue
                  (same """{"targets":["fsharp"],"irVersions":["4.0.0","3.0.0"],"generate":false}""" backend)
                  $"The backend claim: {text backend}"

              let transport = capabilities["modelTransport"] |> nonNull
              let tuples = transport["tuples"]

              Expect.isTrue
                  (same $"""[{jsonTuple "4.0.0"},{jsonTuple "3.0.0"}]""" tuples)
                  $"JSON generateModel tuples only: {text tuples}"

              Expect.equal (text transport["guarantees"]) "[]" "No guarantee is claimed"

          testCase "The session capabilities after initialize have the backend and no frontend, workspace, streaming, cancellation or progress"
          <| fun _ ->
              let guest = FSharpGuest.registration ()
              let reply = dispatch guest (initialize [ jsonTuple "4.0.0" ]) |> Option.get
              let result = reply["result"] |> nonNull
              Expect.equal (text result["protocolVersion"]) "\"0.2.0-draft.1\"" "agreed version"
              let capabilities = result["capabilities"] |> nonNull
              Expect.isNull capabilities["frontend"] "No frontend"
              Expect.isNull capabilities["workspace"] "No workspace discovery"
              Expect.isNotNull capabilities["backend"] "The backend"

              for flag in [ "streaming"; "incremental"; "cancellation"; "progress" ] do
                  Expect.equal (text capabilities[flag]) "false" flag

          testCase "The identity is its own: a valid extension id that is not the built-in morphir.fsharp or any built-in entry"
          <| fun _ ->
              let id = FSharpGuest.extension.Id
              Expect.isNotEmpty id "The guest id"

              match ExtensionId.Parse id with
              | Ok parsed -> Expect.equal parsed.Value id "The id is a registry id"
              | Error failure -> failtest $"The guest id is not a registry id: {failure}"

              let builtIn = FSharpGenerationBackend()
              Expect.equal builtIn.Descriptor.Id.Value "morphir.fsharp" "The built-in keeps its id"
              Expect.notEqual id builtIn.Descriptor.Id.Value "The guest does not impersonate the built-in"

              let host =
                  { Host = MorphirHost.DotnetTool
                    Version = ReleaseVersion.Create "1.0.0" }

              let entries =
                  BackendExtensionSource([ builtIn :> IGenerationBackend; CSharpGenerationBackend() ], host).Entries

              Expect.isFalse
                  (entries |> Seq.exists (fun entry -> entry.Identity.Id.Value = id))
                  "No built-in entry has the guest id"

          testCase "Negotiation agrees only the claimed tuples, in host order"
          <| fun _ ->
              let guest = FSharpGuest.registration ()

              let reply =
                  dispatch
                      guest
                      (initialize [ ionTuple "4.0.0"; jsonTuple "3.0.0"; compileTuple "4.0.0"; jsonTuple "4.0.0" ])
                  |> Option.get

              let transport = ((reply["result"] |> nonNull)["capabilities"] |> nonNull)["modelTransport"] |> nonNull
              let tuples = transport["tuples"]

              Expect.isTrue
                  (same $"""[{jsonTuple "3.0.0"},{jsonTuple "4.0.0"}]""" tuples)
                  $"The agreed tuples: {text tuples}"

          testCase "Each claimed tuple generates F# from its checked-in fixture"
          <| fun _ ->
              let guest = initialized ()

              for version in [ "4.0.0"; "3.0.0" ] do
                  let reply = dispatch guest (generateMessage 1 (generateRequest version)) |> Option.get
                  Expect.isNone (codeOf reply) $"IR {version} generates: {reply}"
                  let result = resultOf reply
                  Expect.isTrue result.Success $"IR {version} succeeds: {result}"
                  Expect.isNonEmpty result.Artifacts $"IR {version} has files"

                  Expect.isFalse
                      (result.Diagnostics |> Seq.exists (fun item -> item.Severity = DiagnosticSeverity.Error))
                      "No error diagnostic"

                  for artifact in result.Artifacts do
                      Expect.isTrue (artifact.Path.EndsWith ".fs") $"{artifact.Path} is an F# file"
                      Expect.isFalse (Path.IsPathRooted artifact.Path) $"{artifact.Path} is relative"
                      Expect.isFalse artifact.Binary $"{artifact.Path} is text"

          testCase "The Boolean option optimize is accepted"
          <| fun _ ->
              let guest = initialized ()

              for value in [ "true"; "false" ] do
                  let request =
                      requestOf (generateTuple "4.0.0") (File.ReadAllBytes(fixtureFor "4.0.0")) [ "optimize", value ]

                  let reply = dispatch guest (generateMessage 2 request) |> Option.get
                  Expect.isTrue (resultOf reply).Success $"optimize={value}"

          testCase "A request in a tuple that the session did not agree is refused with -32602 unsupported_tuple, is not served, and the session stays usable"
          <| fun _ ->
              let guest = FSharpGuest.registration ()
              dispatch guest (initialize [ jsonTuple "4.0.0" ]) |> ignore

              let refusals =
                  [ "3.0.0 is not agreed", generateRequest "3.0.0"
                    "Ion is not claimed",
                    requestOf
                        { generateTuple "4.0.0" with
                            Profile = "morphir-model"
                            Codec = ModelCodec.IonBinary }
                        (File.ReadAllBytes(fixtureFor "4.0.0"))
                        []
                    "a compileModel tuple is not a generateModel input",
                    { generateRequest "4.0.0" with
                        Input =
                            { generateTuple "4.0.0" with
                                Operation = ModelOperation.CompileModel } } ]

              for label, request in refusals do
                  let reply = dispatch guest (generateMessage 3 request) |> Option.get
                  Expect.equal (codeOf reply) (Some -32602) $"{label}: -32602"
                  Expect.equal (messageOf reply) "Model admission refused: unsupported_tuple" $"{label}: the admission code"
                  Expect.isNull reply["result"] $"{label}: no result"

              let served = dispatch guest (generateMessage 4 (generateRequest "4.0.0")) |> Option.get
              Expect.isTrue (resultOf served).Success "A valid request in the same session is served"

          testCase "Without an agreed generateModel tuple, generateModel is -32013; the legacy generate is -32013; before initialize it is -32014"
          <| fun _ ->
              let early = FSharpGuest.registration ()
              let before = dispatch early (generateMessage 1 (generateRequest "4.0.0")) |> Option.get
              Expect.equal (codeOf before) (Some -32014) "generateModel before initialize"

              let guest = FSharpGuest.registration ()
              dispatch guest (initialize [ compileTuple "4.0.0" ]) |> ignore
              let unavailable = dispatch guest (generateMessage 2 (generateRequest "4.0.0")) |> Option.get
              Expect.equal (codeOf unavailable) (Some -32013) "No agreed generateModel tuple"

              let legacy =
                  dispatch
                      guest
                      """{"jsonrpc":"2.0","id":3,"method":"morphir.backend.generate","params":{"ir":{},"target":"fsharp"}}"""
                  |> Option.get

              Expect.equal (codeOf legacy) (Some -32013) "The legacy generate is not claimed"

          testCase "A target other than fsharp, an unknown option, a non-Boolean optimize and a required guarantee are refused with -32602"
          <| fun _ ->
              let guest = initialized ()
              let request = generateRequest "4.0.0"
              let bytes = File.ReadAllBytes(fixtureFor "4.0.0")

              let refusals =
                  [ "target csharp", { request with Target = "csharp" }
                    "unknown option", requestOf (generateTuple "4.0.0") bytes [ "emit", "true" ]
                    "optimize is a string", requestOf (generateTuple "4.0.0") bytes [ "optimize", "\"no\"" ]
                    "required guarantee",
                    { request with
                        RequiredGuarantees = [| { Guarantee = "urn:morphir:checked"; Scope = "model" } |] } ]

              for label, refused in refusals do
                  let reply = dispatch guest (generateMessage 5 refused) |> Option.get
                  Expect.equal (codeOf reply) (Some -32602) $"{label}: -32602"
                  Expect.isNull reply["result"] $"{label}: no result"

              let guarantee =
                  dispatch
                      guest
                      (generateMessage
                          6
                          { request with
                              RequiredGuarantees = [| { Guarantee = "urn:morphir:checked"; Scope = "model" } |] })
                  |> Option.get

              Expect.equal (messageOf guarantee) "Model admission refused: unsupported_guarantee" "The guarantee code"

          testCase "A payload that is not IR of the input tuple, or whose digest does not match, is refused with -32602 before generation"
          <| fun _ ->
              let guest = initialized ()
              let classic = File.ReadAllBytes(fixtureFor "3.0.0")
              let mislabelled = dispatch guest (generateMessage 7 (requestOf (generateTuple "4.0.0") classic [])) |> Option.get
              Expect.equal (codeOf mislabelled) (Some -32602) "Classic IR in a 4.0.0 tuple"
              Expect.stringContains (messageOf mislabelled) "4.0.0" "The message names the input IR version"

              let malformed =
                  dispatch guest (generateMessage 8 (requestOf (generateTuple "4.0.0") (Encoding.UTF8.GetBytes "[1,2]") [])) |> Option.get

              Expect.equal (codeOf malformed) (Some -32602) "JSON that is not a Morphir IR document"

              let request = generateRequest "4.0.0"

              let tampered =
                  { request with
                      Model =
                          { request.Model with
                              Payload =
                                  { request.Model.Payload with
                                      Sha256 = Sha256Digest.Create(String('0', 64)) } } }

              let digest = dispatch guest (generateMessage 9 tampered) |> Option.get
              Expect.equal (codeOf digest) (Some -32602) "digest mismatch"
              Expect.equal (messageOf digest) "Model admission refused: digest_mismatch" "The admission code"

          testCase "A model that the generator rejects is a failed result with error diagnostics and no artifacts"
          <| fun _ ->
              let guest = initialized ()
              let bytes = File.ReadAllBytes(irFixture "daily-rate-mismatch-v3.json")
              let reply = dispatch guest (generateMessage 10 (requestOf (generateTuple "3.0.0") bytes [])) |> Option.get
              Expect.isNone (codeOf reply) $"A semantic failure is a result, not an error: {reply}"
              let result = resultOf reply
              Expect.isFalse result.Success "success is false"
              Expect.isEmpty result.Artifacts "No artifacts"

              Expect.exists
                  result.Diagnostics
                  (fun item -> item.Severity = DiagnosticSeverity.Error && item.Code.IsSome)
                  "An error diagnostic with its rule code"

          testCase "A Specs model in a claimed tuple is a failed result with unsupported-distribution and no artifacts"
          <| fun _ ->
              let guest = initialized ()
              let specs = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MorphirIon", "pairs", "v4-specs.json")
              let reply = dispatch guest (generateMessage 11 (requestOf (generateTuple "4.0.0") (File.ReadAllBytes specs) [])) |> Option.get
              let result = resultOf reply
              Expect.isFalse result.Success "success is false"
              Expect.isEmpty result.Artifacts "No artifacts"
              Expect.exists result.Diagnostics (fun item -> item.Code = Some "unsupported-distribution") "The distribution is named" ]

/// The guest's own check of artifact paths, and malformed params of generateModel.
[<Tests>]
let edgeTests =
    testList
        "F# backend MEP guest edges"
        [ testCase "An artifact path must be relative, /-separated, with no empty, . or .. segment, and no \\ or :"
          <| fun _ ->
              for path in [ "Inventory.fs"; "Car/Rental/Inventory.fs" ] do
                  Expect.isOk (FSharpGuest.checkArtifactPath path) $"{path} is a safe path"

              for path in
                  [ ""
                    "/Inventory.fs"
                    "Car//Inventory.fs"
                    "Car/Inventory.fs/"
                    "./Inventory.fs"
                    "Car/./Inventory.fs"
                    ".."
                    "Car/../../Inventory.fs"
                    @"Car\Inventory.fs"
                    "C:/Inventory.fs"
                    "C:Inventory.fs" ] do
                  Expect.isError (FSharpGuest.checkArtifactPath path) $"'{path}' is refused"

          testCase "A generated tree with an unsafe path is -32603, not artifacts"
          <| fun _ ->
              let tree =
                  Morphir.IR.Pipeline.VFileTree.fromFileMap (
                      Map.ofList [ "Car:Inventory.fs", Morphir.IR.Pipeline.VFile.ofText "Car:Inventory.fs" "module X" ]
                  )

              match FSharpGuest.artifacts tree with
              | Error error -> Expect.equal error.Code -32603 "-32603"
              | Ok artifacts -> failtest $"An unsafe path is not an artifact: {List.ofSeq artifacts}"

          testCase "Malformed generateModel params are -32602, and the session stays usable"
          <| fun _ ->
              let guest = initialized ()
              let valid = JsonNode.Parse(generateMessage 1 (generateRequest "4.0.0")) |> nonNull

              let withParams (change: JsonObject -> unit) =
                  let message = valid.DeepClone().AsObject()
                  change ((message["params"] |> nonNull).AsObject())
                  message.ToJsonString()

              let replace (value: string) =
                  let message = valid.DeepClone().AsObject()
                  message["params"] <- JsonNode.Parse value
                  message.ToJsonString()

              let malformed =
                  [ "no target", withParams (fun parameters -> parameters.Remove "target" |> ignore)
                    "no model", withParams (fun parameters -> parameters.Remove "model" |> ignore)
                    "a bad contextDigest", withParams (fun parameters -> parameters["contextDigest"] <- JsonValue.Create "xyz")
                    "params an array", replace "[1,2]"
                    "no params", (let message = valid.DeepClone().AsObject() in message.Remove "params" |> ignore; message.ToJsonString()) ]

              for label, message in malformed do
                  let reply = dispatch guest message |> Option.get
                  Expect.equal (codeOf reply) (Some -32602) $"{label}: -32602"
                  Expect.isNull reply["result"] $"{label}: no result"

              // JSON-RPC 2.0: params is an object or an array, so a string makes the request itself invalid.
              let invalid = dispatch guest (replace "\"fsharp\"") |> Option.get
              Expect.equal (codeOf invalid) (Some -32600) "params a string: -32600"

              let served = dispatch guest (generateMessage 2 (generateRequest "4.0.0")) |> Option.get
              Expect.isTrue (resultOf served).Success "A valid request after the malformed ones is served" ]
