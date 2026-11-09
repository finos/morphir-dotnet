// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The F# guest (add-fsharp-compile group 8): its claims, compile outcomes and envelopes.
module Morphir.Frontend.FSharp.Mep.Tests.GuestTests

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.Frontend.FSharp.Mep
open Morphir.Frontend.FSharp.Mep.Tests.GuestSupport
open Morphir.Mep

let private text (node: JsonNode | null) = (node |> nonNull).ToJsonString()

let private payloadBytes (model: MorphirModel) =
    Convert.FromBase64String model.Payload.Content

[<Tests>]
let tests =
    testList
        "F# MEP guest"
        [ testCase "The claim set has only 0.2.0-draft.1, F# with .fs, and JSON compile tuples for 3.0.0 and 4.0.0"
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

              let frontend = claims["capabilities"] |> nonNull |> fun node -> node["frontend"] |> nonNull
              Expect.equal (text frontend["languages"]) """[{"id":"fsharp","fileExtensions":[".fs"]}]""" "language"
              Expect.equal (text frontend["multiDocument"]) "true" "multiDocument"
              Expect.equal (text frontend["compile"]) "false" "legacy compile"
              Expect.equal (text frontend["incremental"]) "false" "incremental"

              let tuples = (claims["capabilities"] |> nonNull)["modelTransport"] |> nonNull |> fun node -> node["tuples"]

              Expect.isTrue
                  (JsonNode.DeepEquals(tuples, JsonNode.Parse $"""[{jsonTuple "4.0.0"},{jsonTuple "3.0.0"}]"""))
                  $"JSON compileModel tuples only: {text tuples}"

              let result = (dispatch guest (initialize [ jsonTuple "4.0.0" ]) |> Option.get)["result"] |> nonNull
              Expect.equal (text result["protocolVersion"]) "\"0.2.0-draft.1\"" "agreed version"

          testCase "A legacy-only host gets -32011, and a later initialize with 0.2.0-draft.1 succeeds"
          <| fun _ ->
              let guest = FSharpGuest.registration ()

              let legacy =
                  dispatch
                      guest
                      """{"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersions":["0.1"],"host":{"kind":"cli","name":"h","version":"1"}}}"""
                  |> Option.get

              Expect.equal (text ((legacy["error"] |> nonNull)["code"])) "-32011" "no common version"
              let current = dispatch guest (initialize [ jsonTuple "4.0.0" ]) |> Option.get
              Expect.isNull current["error"] "The second initialize succeeds"

          testCase "Each claimed IR version compiles, and the model reads back through that version's reader"
          <| fun _ ->
              let guest = initialized ()

              for version in [ "4.0.0"; "3.0.0" ] do
                  let request = compileRequest version [ "Pricing.fs", pricing ] (Some [ "Pricing" ])
                  let reply = dispatch guest (compileMessage 1 request) |> Option.get

                  match resultOf reply with
                  | CompileModelResult.Compiled(model, diagnostics, modules) ->
                      Expect.equal model.IrVersion (SemanticVersion.Create version) "The tuple IR version"
                      Expect.equal model.ModelId "acme/demo" "modelId is the package name"
                      Expect.equal model.Revision (FSharpGuest.revision request.ContextDigest) "deterministic revision"
                      Expect.notEqual model.Revision ("sha256:" + model.Payload.Sha256.Value) "not the payload digest"
                      Expect.isFalse (diagnostics |> Seq.exists (fun d -> d.Severity = DiagnosticSeverity.Error)) "no error"
                      Expect.sequenceEqual modules [ "Pricing" ] "modules"

                      match ModelTransport.admit (FSharpGuest.tuple version) FSharpGuest.limits model with
                      | Error failure -> failtest $"The envelope is admitted: {failure}"
                      | Ok _ -> ()

                      use document = JsonDocument.Parse(payloadBytes model)

                      let read =
                          if version = "4.0.0" then
                              Morphir.Json.V4.V4Json.readDistribution document.RootElement
                              |> Result.map ignore
                              |> Result.mapError string
                          else
                              Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement
                              |> Result.map ignore
                              |> Result.mapError string

                      Expect.isOk read $"IR {version} reads back"
                  | failed -> failtest $"IR {version} compiles: {failed}"

          testCase "Warnings travel with a successful model"
          <| fun _ ->
              let guest = initialized ()

              let source =
                  """namespace Acme.Demo

module Pricing =
    let first (prices: int list) : int =
        match prices with
        | price :: _ -> price
"""

              let reply = dispatch guest (compileMessage 2 (compileRequest "4.0.0" [ "Pricing.fs", source ] None)) |> Option.get

              match resultOf reply with
              | CompileModelResult.Compiled(_, diagnostics, _) ->
                  Expect.exists
                      diagnostics
                      (fun diagnostic ->
                          diagnostic.Severity = DiagnosticSeverity.Warning
                          && diagnostic.Code = Some "fsharp.compiler")
                      "The incomplete-match warning is in the result"
              | failed -> failtest $"A warning does not fail the compilation: {failed}"

          testCase "An unsupported construct is a failed result with no model member"
          <| fun _ ->
              let guest = initialized ()

              let source =
                  """namespace Acme.Demo

module Pricing =
    let mutable total = 0
"""

              let reply = dispatch guest (compileMessage 3 (compileRequest "4.0.0" [ "Pricing.fs", source ] None)) |> Option.get
              let result = reply["result"] |> nonNull
              Expect.equal (text result["success"]) "false" "success is false"
              Expect.isFalse (result.AsObject().ContainsKey "model") "No model member, not even null"

              match resultOf reply with
              | CompileModelResult.Failed diagnostics ->
                  Expect.exists
                      diagnostics
                      (fun diagnostic -> diagnostic.Code = Some "fsharp.unsupported_mutable" && diagnostic.Location.IsSome)
                      "The catalog diagnostic with its location"
              | compiled -> failtest $"Expected a failure: {compiled}"

          testCase "A payload whose SHA-256 does not match is refused with -32602 before the IR is decoded, and the session stays usable"
          <| fun _ ->
              let guest = initialized ()
              let request = compileRequest "4.0.0" [ "Pricing.fs", pricing ] None
              let reply = dispatch guest (compileMessage 4 request) |> Option.get

              match resultOf reply with
              | CompileModelResult.Compiled(model, _, _) ->
                  let tampered =
                      { model with
                          Payload =
                              { model.Payload with
                                  Sha256 = Sha256Digest.Create(String('0', 64)) } }

                  match ModelTransport.admit (FSharpGuest.tuple "4.0.0") FSharpGuest.limits tampered with
                  | Error(AdmissionFailure.DigestMismatch _ as failure) ->
                      Expect.equal failure.ErrorCode ErrorCode.InvalidParams "The refusal is -32602"
                  | other -> failtest $"Expected digest_mismatch: {other}"
              | failed -> failtest $"Expected a model: {failed}"

              match resultOf (dispatch guest (compileMessage 5 request) |> Option.get) with
              | CompileModelResult.Compiled(model, _, _) ->
                  match ModelTransport.admit (FSharpGuest.tuple "4.0.0") FSharpGuest.limits model with
                  | Ok _ -> ()
                  | Error failure -> failtest $"The next model in the session is admitted: {failure}"
              | failed -> failtest $"A valid compileModel after the refusal succeeds in the same session: {failed}"

          testCase "Requests that the session does not admit are -32602 or lifecycle errors, and the session stays usable"
          <| fun _ ->
              let fresh = FSharpGuest.registration ()
              let early = dispatch fresh (compileMessage 5 (compileRequest "4.0.0" [ "Pricing.fs", pricing ] None)) |> Option.get
              Expect.equal (text ((early["error"] |> nonNull)["code"])) "-32014" "compileModel before initialize"

              let guest = initialized ()
              let request = compileRequest "4.0.0" [ "Pricing.fs", pricing ] None

              let mismatch =
                  { request with
                      Options = { request.Options with IrVersion = SemanticVersion.Create "3.0.0" } }

              let refused = dispatch guest (compileMessage 6 mismatch) |> Option.get
              Expect.equal (text ((refused["error"] |> nonNull)["code"])) "-32602" "options IR version differs"

              let ion =
                  { request with
                      Output =
                          { request.Output with
                              Profile = "morphir-model"
                              Codec = ModelCodec.IonBinary } }

              let notAgreed = dispatch guest (compileMessage 7 ion) |> Option.get
              Expect.equal (text ((notAgreed["error"] |> nonNull)["code"])) "-32602" "a tuple outside the agreement"

              let unknown = dispatch guest """{"jsonrpc":"2.0","id":8,"method":"morphir.unknown"}""" |> Option.get
              Expect.equal (text ((unknown["error"] |> nonNull)["code"])) "-32601" "unknown method"

              match resultOf (dispatch guest (compileMessage 9 request) |> Option.get) with
              | CompileModelResult.Compiled _ -> ()
              | failed -> failtest $"The session stays usable: {failed}"

          testCase "A types-only request is refused, because the guest does not support it yet"
          <| fun _ ->
              let guest = initialized ()
              let request = compileRequest "4.0.0" [ "Pricing.fs", pricing ] None

              let typesOnly =
                  { request with
                      Options = { request.Options with TypesOnly = true } }

              let reply = dispatch guest (compileMessage 1 typesOnly) |> Option.get
              Expect.equal (text ((reply["error"] |> nonNull)["code"])) "-32602" "typesOnly is refused"
              Expect.stringContains (text ((reply["error"] |> nonNull)["message"])) "typesOnly" "The message names it"

          testCase "A model larger than the agreed limits is not returned as a success"
          <| fun _ ->
              let guest = FSharpGuest.registration ()

              let small =
                  (initialize [ jsonTuple "4.0.0" ])
                      .Replace(hostLimits, """{"maxEncodedBytes":64,"maxDecodedBytes":48,"maxDepth":4096,"maxNodes":50000000}""")

              let initialized = dispatch guest small |> Option.get
              Expect.isNull initialized["error"] "initialize"
              let reply = dispatch guest (compileMessage 1 (compileRequest "4.0.0" [ "Pricing.fs", pricing ] None)) |> Option.get
              Expect.isNull reply["result"] "No result"
              Expect.equal (text ((reply["error"] |> nonNull)["code"])) "-32603" "The guest reports that the model does not fit"
              Expect.stringContains (text ((reply["error"] |> nonNull)["message"])) "limit" "The message names the limit"

          testCase "A host that agrees only 4.0.0 cannot get 3.0.0 from the guest"
          <| fun _ ->
              let guest = FSharpGuest.registration ()
              dispatch guest (initialize [ jsonTuple "4.0.0" ]) |> ignore
              let refused = dispatch guest (compileMessage 1 (compileRequest "3.0.0" [ "Pricing.fs", pricing ] None)) |> Option.get
              Expect.equal (text ((refused["error"] |> nonNull)["code"])) "-32602" "3.0.0 is not agreed" ]
