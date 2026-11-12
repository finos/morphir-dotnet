// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The backend parts of the MEP core: the pinned generateModel request, the generate result and its artifacts, and
/// the guest check that admits a request before generation.
module Morphir.Mep.Tests.GenerateModelTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.Mep
open Morphir.Mep.Tests.Transcripts
open Morphir.Mep.Tests.ModelTransportTests

let private cases =
    JsonNode.Parse(File.ReadAllText(fixture "model-transport.cases.json")) |> nonNull

let private generateRequests =
    [ for item in (cases["cases"] |> nonNull).AsArray() |> Seq.map nonNull do
          match item["request"] with
          | NonNull request when textAt [ "method" ] request = "morphir.backend.generateModel" ->
              yield textAt [ "id" ] item, at [ "params" ] request
          | _ -> () ]

let private jsonTuple = tuple ModelOperation.GenerateModel "morphir-ir" ModelCodec.MorphirJson "4.0.0"
let private compileTuple = tuple ModelOperation.CompileModel "morphir-ir" ModelCodec.MorphirJson "4.0.0"
let private wide = limits 8192 4096 64 256

let private plain = """{"formatVersion":"4.0.0","distribution":{"Library":{"packageName":"a/b","dependencies":{},"def":{"modules":{}}}}}"""

let private envelope (tuple: ModelTransportTuple) =
    match ModelTransport.createEnvelope tuple "urn:m" "r1" (ReadOnlyMemory(Text.Encoding.UTF8.GetBytes plain)) with
    | Ok model -> model
    | Error failure -> failtestf "Envelope creation failed: %O" failure

let private request (input: ModelTransportTuple) : GenerateModelRequest =
    { Model = envelope { input with Operation = ModelOperation.GenerateModel }
      Input = input
      Target = "fsharp"
      ContextDigest = Sha256Digest.Create(String('1', 64))
      RequiredGuarantees = [||]
      Options = Collections.Generic.Dictionary() }

let private trustAll = Func<ModelGuaranteeEvidence, bool>(fun _ -> true)

[<Tests>]
let tests =
    testList
        "generateModel"
        [ testCase "Every pinned generateModel request reads with the typed codec, and writes the same members and values"
          <| fun _ ->
              Expect.isNonEmpty generateRequests "The pin has generateModel requests"

              for id, parameters in generateRequests do
                  let typed = decode MepJson.Default.GenerateModelRequest parameters
                  let written = JsonNode.Parse(JsonSerializer.Serialize(typed, MepJson.Default.GenerateModelRequest))

                  // The model member is the envelope as the existing MorphirModel codec writes it (it leaves out an empty
                  // critical list and unknown payload members). Every other member is written as the pin has it.
                  let expected = parameters.DeepClone()
                  let envelope = decode MepJson.Default.MorphirModel (at [ "model" ] parameters)
                  expected["model"] <- JsonNode.Parse(JsonSerializer.Serialize(envelope, MepJson.Default.MorphirModel))

                  Expect.isTrue (JsonNode.DeepEquals(written, expected)) $"{id} round-trips: {written}"
          |> testLabel "PlatformSpecific"

          testCase "Absent requiredGuarantees and options read as empty, as the wire rules say"
          <| fun _ ->
              let json =
                  JsonSerializer.Serialize(request jsonTuple, MepJson.Default.GenerateModelRequest)
                  |> JsonNode.Parse
                  |> nonNull

              json.AsObject().Remove "requiredGuarantees" |> ignore
              json.AsObject().Remove "options" |> ignore
              let read = decode MepJson.Default.GenerateModelRequest json
              Expect.isEmpty read.RequiredGuarantees "requiredGuarantees"
              Expect.isEmpty read.Options "options"

          testCase "An agreed request is admitted, and its model is the request model"
          <| fun _ ->
              let agreed = Some(support [ jsonTuple ] wide [])
              let sent = request jsonTuple

              match GenerateModel.admit agreed trustAll sent with
              | Ok admitted ->
                  Expect.equal admitted.Model.Envelope sent.Model "The admitted envelope"
                  Expect.equal admitted.Model.Tuple jsonTuple "The input tuple"
                  Expect.isEmpty admitted.Guarantees "No guarantee is required"
              | Error failure -> failtest $"Expected admission: {failure}"

          testCase "An input that is not a generateModel tuple is unsupported_tuple, also when the session agrees it for compileModel"
          <| fun _ ->
              let agreed = Some(support [ compileTuple; jsonTuple ] wide [])

              match GenerateModel.admit agreed trustAll (request compileTuple) with
              | Error(AdmissionFailure.UnsupportedTuple refused as failure) ->
                  Expect.equal refused compileTuple "The refused tuple"
                  Expect.equal failure.ErrorCode ErrorCode.InvalidParams "-32602"
              | other -> failtest $"Expected unsupported_tuple: {other}"

          testCase "An input outside the agreement is unsupported_tuple, before the payload is read"
          <| fun _ ->
              let agreed = Some(support [ jsonTuple ] wide [])
              let three = { jsonTuple with IrVersion = SemanticVersion.Create "3.0.0" }

              let corrupt =
                  { request three with
                      Model =
                          { (request three).Model with
                              Payload = { (request three).Model.Payload with Content = "!" } } }

              match GenerateModel.admit agreed trustAll corrupt with
              | Error(AdmissionFailure.UnsupportedTuple _) -> ()
              | other -> failtest $"Expected unsupported_tuple first: {other}"

              match GenerateModel.admit None trustAll (request jsonTuple) with
              | Error(AdmissionFailure.UnsupportedTuple _) -> ()
              | other -> failtest $"A legacy-only session agrees no tuple: {other}"

          testCase "A required guarantee that the session cannot interpret is unsupported_guarantee"
          <| fun _ ->
              let agreed = Some(support [ jsonTuple ] wide [])

              let required =
                  { request jsonTuple with
                      RequiredGuarantees = [| { Guarantee = "urn:g"; Scope = "s" } |] }

              match GenerateModel.admit agreed trustAll required with
              | Error(AdmissionFailure.UnsupportedGuarantee "urn:g") -> ()
              | other -> failtest $"Expected unsupported_guarantee: {other}"

          testCase "A generate result always writes success, artifacts, diagnostics and binary"
          <| fun _ ->
              let empty: GenerateResult =
                  { Success = false
                    Artifacts = [||]
                    Diagnostics = [||] }

              Expect.equal
                  (JsonSerializer.Serialize(empty, MepJson.Default.GenerateResult))
                  """{"success":false,"artifacts":[],"diagnostics":[]}"""
                  "Every member is written"

              let file: Artifact =
                  { Path = "A/B.fs"
                    Content = "module B"
                    Binary = false }

              Expect.equal
                  (JsonSerializer.Serialize(file, MepJson.Default.Artifact))
                  """{"path":"A/B.fs","content":"module B","binary":false}"""
                  "binary is written when false"

              let read = decode MepJson.Default.Artifact (JsonNode.Parse """{"path":"A/B.fs","content":"x"}""" |> nonNull)
              Expect.isFalse read.Binary "An absent binary reads as false" ]
