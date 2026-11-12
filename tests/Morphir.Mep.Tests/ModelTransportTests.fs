// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Tuple negotiation (task 4.3): host order, bilateral limits, host refusal and empty agreements.
module Morphir.Mep.Tests.ModelTransportTests

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization.Metadata
open Expecto
open Morphir.Mep
open Morphir.Mep.Tests.Transcripts

/// Reads a JSON node with a generated reader.
let decode<'T when 'T: not null> (info: JsonTypeInfo<'T>) (node: JsonNode) : 'T =
    match JsonSerializer.Deserialize(node.ToJsonString(), info :> JsonTypeInfo) with
    | null -> failtest "The reader returned null"
    | value -> unbox<'T> value

/// The member at a path of names. The test fails when a member is absent or null.
let at (names: string list) (node: JsonNode) : JsonNode =
    names |> List.fold (fun (current: JsonNode) (name: string) -> current[name] |> nonNull) node

/// The string at a path of names.
let textAt (names: string list) (node: JsonNode) = (at names node).GetValue<string>()

/// The method of a transcript message, if it has one.
let methodOf (node: JsonNode) =
    node["method"] |> Option.ofObj |> Option.map (fun value -> value.GetValue<string>())

let private v = SemanticVersion.Create

/// A tuple for the tests.
let tuple operation profile codec irVersion =
    { Operation = operation
      Profile = profile
      Codec = codec
      EnvelopeVersion = v "0.1.0-draft.1"
      IrVersion = v irVersion }

let limits encoded decoded depth nodes =
    { MaxEncodedBytes = encoded
      MaxDecodedBytes = decoded
      MaxDepth = depth
      MaxNodes = nodes }

let support (tuples: ModelTransportTuple list) limits (guarantees: string list) =
    { Tuples = Array.ofList tuples
      Limits = limits
      Guarantees = Array.ofList guarantees }

let private compileIon = tuple ModelOperation.CompileModel "morphir-model" ModelCodec.IonBinary "4.0.0"
let private compileJson = tuple ModelOperation.CompileModel "morphir-ir" ModelCodec.MorphirJson "4.0.0"
let private generateIon = tuple ModelOperation.GenerateModel "morphir-model" ModelCodec.IonBinary "4.0.0"
let private generateJson4 = tuple ModelOperation.GenerateModel "morphir-ir" ModelCodec.MorphirJson "4.0.0"
let private generateJson3 = tuple ModelOperation.GenerateModel "morphir-ir" ModelCodec.MorphirJson "3.0.0"

let private wide = limits 8192 4096 64 256

let private expectError result message =
    match result with
    | Ok value -> failtestf "%s: expected a refusal, got %O" message (box value)
    | Error error -> error

let private expectOk result message =
    match result with
    | Ok value -> value
    | Error error -> failtestf "%s: %O" message (box error)

let private tuplesOf (agreed: ModelTransportSupport) = List.ofSeq agreed.Tuples

let private limitCases =
    [ "maxEncodedBytes", (fun (value: int) -> limits value 4096 64 256), (fun (l: ModelTransportLimits) -> l.MaxEncodedBytes)
      "maxDecodedBytes", (fun value -> limits 8192 value 64 256), (fun l -> l.MaxDecodedBytes)
      "maxDepth", (fun value -> limits 8192 4096 value 256), (fun l -> l.MaxDepth)
      "maxNodes", (fun value -> limits 8192 4096 64 value), (fun l -> l.MaxNodes) ]

[<Tests>]
let tests =
    testList
        "MEP model-transport negotiation"
        [ testCase "the guest keeps host order"
          <| fun _ ->
              let offer = support [ generateJson4; generateJson3 ] wide []
              let guest = support [ generateJson3; generateJson4 ] wide []
              let selection = ModelTransport.select offer guest
              Expect.equal (tuplesOf selection) [ generateJson4; generateJson3 ] "Host order, not guest order"

              let agreed = expectOk (ModelTransport.verifySelection offer selection) "The host accepts it"
              Expect.equal (tuplesOf agreed) [ generateJson4; generateJson3 ] "Host order kept"

              Expect.equal
                  (ModelTransport.firstTuple (Some agreed) ModelOperation.GenerateModel)
                  (Ok generateJson4)
                  "The first agreed tuple is the host's first choice"

          testCase "the guest selects by exact tuple equality"
          <| fun _ ->
              let offer = support [ generateIon; generateJson4 ] wide []
              // Same codecs and profiles, crossed: no tuple is equal.
              let crossed =
                  [ { generateIon with Codec = ModelCodec.MorphirJson }
                    { generateJson4 with Codec = ModelCodec.IonBinary } ]

              let selection = ModelTransport.select offer (support crossed wide [])
              Expect.isEmpty selection.Tuples "Independent codec agreement makes no tuple"

          for name, make, get in limitCases do
              testCase $"{name} is the smaller of the two values"
              <| fun _ ->
                  let offer = support [ generateIon ] (make 100) []
                  let lower = ModelTransport.select offer (support [ generateIon ] (make 40) [])
                  Expect.equal (get lower.Limits) 40 "The guest's lower value"
                  let higher = ModelTransport.select offer (support [ generateIon ] (make 400) [])
                  Expect.equal (get higher.Limits) 100 "The host's lower value"
                  Expect.equal higher.Limits (make 100) "The other limits do not change"

              testCase $"the host refuses a {name} above its own"
              <| fun _ ->
                  let offer = support [ generateIon ] (make 100) []
                  let selection = support [ generateIon ] (make 101) []
                  let failure = expectError (ModelTransport.verifySelection offer selection) "Above the offer"

                  match failure with
                  | ModelTransportFailure.LimitAboveOffer(limit, selected, offered) ->
                      Expect.equal (limit, selected, offered) (name, 101, 100) "Names the limit"
                  | other -> failtestf "Unexpected failure %O" other

                  Expect.stringContains (failure.ToString()) name "The text names the limit"

              testCase $"the host refuses a {name} that is not positive"
              <| fun _ ->
                  let offer = support [ generateIon ] (make 100) []
                  let selection = support [ generateIon ] (make 0) []

                  match expectError (ModelTransport.verifySelection offer selection) "Zero" with
                  | ModelTransportFailure.InvalidLimit(limit, 0) -> Expect.equal limit name "Names the limit"
                  | other -> failtestf "Unexpected failure %O" other

          testCase "the host accepts a limit equal to its own"
          <| fun _ ->
              let offer = support [ generateIon ] wide []
              let agreed = expectOk (ModelTransport.verifySelection offer (ModelTransport.select offer offer)) "Equal"
              Expect.equal agreed.Limits wide "Same limits"

          testCase "the host refuses a tuple that it did not offer, and names it"
          <| fun _ ->
              let offer = support [ generateIon ] wide []
              let notOffered = { generateIon with Codec = ModelCodec.MorphirJson }
              let selection = support [ generateIon; notOffered ] wide []
              let failure = expectError (ModelTransport.verifySelection offer selection) "Not offered"
              Expect.equal failure (ModelTransportFailure.TupleNotOffered notOffered) "The refused tuple"
              Expect.stringContains (failure.ToString()) (notOffered.ToString()) "The text names the tuple"

              let diagnostic = failure.ToDiagnostic "org.finos.morphir.example"
              Expect.equal diagnostic.Severity DiagnosticSeverity.Error "An error"
              Expect.stringContains diagnostic.Message "org.finos.morphir.example" "Names the extension"
              Expect.stringContains diagnostic.Message "morphir-json" "Names the tuple"
              Expect.isNone diagnostic.Code "No code that the pin does not have"

          testCase "the host refuses a selection that is not in host order"
          <| fun _ ->
              let offer = support [ generateJson4; generateJson3 ] wide []
              let selection = support [ generateJson3; generateJson4 ] wide []
              let failure = expectError (ModelTransport.verifySelection offer selection) "Reordered"
              Expect.equal failure (ModelTransportFailure.TupleOutOfOrder generateJson4) "The first tuple out of order"
              Expect.stringContains (failure.ToString()) (generateJson4.ToString()) "The text names the tuple"

          testCase "the host accepts a subset of its offer in host order"
          <| fun _ ->
              let offer = support [ generateIon; generateJson4; generateJson3 ] wide []
              let selection = support [ generateIon; generateJson3 ] wide []
              let agreed = expectOk (ModelTransport.verifySelection offer selection) "A subset"
              Expect.equal (tuplesOf agreed) [ generateIon; generateJson3 ] "Host order"

          testCase "the host refuses a duplicate tuple"
          <| fun _ ->
              let offer = support [ generateIon ] wide []
              let selection = support [ generateIon; generateIon ] wide []

              Expect.equal
                  (ModelTransport.verifySelection offer selection)
                  (Error(ModelTransportFailure.DuplicateTuple generateIon))
                  "Tuples are unique"

          testCase "guarantees are those the guest can interpret, in host order"
          <| fun _ ->
              let offer = support [ generateIon ] wide [ "urn:a"; "urn:b"; "urn:c" ]
              let guest = support [ generateIon ] wide [ "urn:c"; "urn:x"; "urn:a" ]
              let selection = ModelTransport.select offer guest
              Expect.equal (List.ofSeq selection.Guarantees) [ "urn:a"; "urn:c" ] "Host order, interpretable only"
              let agreed = expectOk (ModelTransport.verifySelection offer guest) "Guarantees are not refused"
              Expect.equal (List.ofSeq agreed.Guarantees) [ "urn:a"; "urn:c" ] "The host keeps the common ids"

          testCase "an Ion-only offer to a JSON-only guest makes compileModel unavailable"
          <| fun _ ->
              let offer = support [ compileIon ] wide []
              let guest = support [ compileJson ] wide []
              let selection = ModelTransport.select offer guest
              Expect.isEmpty selection.Tuples "No agreed tuple"
              let agreed = expectOk (ModelTransport.verifySelection offer selection) "An empty selection is valid"

              let unavailable =
                  expectError (ModelTransport.firstTuple (Some agreed) ModelOperation.CompileModel) "Unavailable"

              Expect.equal unavailable.Operation ModelOperation.CompileModel "The operation"
              Expect.equal unavailable.Code "extension.tuple_unavailable" "The pinned code"
              let diagnostic = unavailable.ToDiagnostic "org.finos.morphir.fsharp"
              Expect.equal diagnostic.Code (Some "extension.tuple_unavailable") "Diagnostic code"
              Expect.stringContains diagnostic.Message "org.finos.morphir.fsharp" "Names the extension"
              Expect.stringContains diagnostic.Message "morphir.frontend.compileModel" "Names the operation"

          testCase "an absent offer or selection supports no model operation"
          <| fun _ ->
              for operation in [ ModelOperation.CompileModel; ModelOperation.GenerateModel ] do
                  Expect.isError (ModelTransport.firstTuple None operation) "Legacy only"
                  Expect.isEmpty (ModelTransport.agreedTuples None operation) "No tuples"

          testCase "compile support does not authorize generateModel"
          <| fun _ ->
              let agreed = support [ compileIon ] wide []
              Expect.isError (ModelTransport.firstTuple (Some agreed) ModelOperation.GenerateModel) "Other operation"

              Expect.equal
                  (List.ofSeq (ModelTransport.agreedTuples (Some agreed) ModelOperation.CompileModel))
                  [ compileIon ]
                  "Only the compile tuple"

          testCase "only the exact draft 0.2.0-draft.1 enables model operations"
          <| fun _ ->
              Expect.equal ModelTransport.modelProtocol.Value "0.2.0-draft.1" "The pinned draft"
              Expect.isOk (ModelTransport.requireModelProtocol (ProtocolVersion.Create "0.2.0-draft.1")) "Exact"

              for version in [ "0.1"; "0.2.0-draft.2"; "0.2.0" ] do
                  match ModelTransport.requireModelProtocol (ProtocolVersion.Create version) with
                  | Error(AdmissionFailure.UnsupportedProtocol found) -> Expect.equal found.Value version "Names it"
                  | other -> failtestf "%s must not enable model operations: %O" version other

          testCase "model operations map from their methods only"
          <| fun _ ->
              Expect.equal (ModelTransport.operationOf Method.CompileModel) (Some ModelOperation.CompileModel) "compile"
              Expect.equal (ModelTransport.operationOf Method.GenerateModel) (Some ModelOperation.GenerateModel) "generate"

              for method in [ Method.Compile; Method.Generate; Method.Validate; Method.Initialize ] do
                  Expect.isNone (ModelTransport.operationOf method) $"{method} is not a model operation"

          testCase "the session handshake of session-model-transport.md negotiates both tuples"
          <| fun _ ->
              let entries = read "session-model-transport.md"
              let initialize = entries[0].Node
              let result = entries[1].Node
              Expect.equal (methodOf initialize) (Some "morphir.initialize") "The initialize request"

              let parameters = decode MepJson.Default.InitializeParams (at [ "params" ] initialize)
              let initialized = decode MepJson.Default.InitializeResult (at [ "result" ] result)
              let offer = parameters.ModelTransport |> Option.get
              let advertised = initialized.Capabilities.ModelTransport |> Option.get

              // The guest's support is the advertised selection: selecting again gives the same value.
              let selection = ModelTransport.select offer advertised
              Expect.equal (tuplesOf selection) (List.ofSeq advertised.Tuples) "The guest's selection is the result's modelTransport"
              Expect.equal selection.Limits advertised.Limits "Same limits"
              Expect.equal (List.ofSeq selection.Guarantees) (List.ofSeq advertised.Guarantees) "Same guarantees"

              let agreed = expectOk (ModelTransport.verifySelection offer advertised) "The host accepts the selection"
              Expect.equal (List.ofSeq agreed.Tuples) (List.ofSeq offer.Tuples) "Both tuples, in host order"
              Expect.equal agreed.Limits offer.Limits "Equal limits"
              Expect.equal (List.ofSeq agreed.Guarantees) [ "urn:morphir:mep:fixture:checked" ] "One guarantee"

              Expect.equal
                  (ModelTransport.firstTuple (Some agreed) ModelOperation.CompileModel)
                  (Ok compileIon)
                  "compileModel tuple"

              Expect.equal
                  (ModelTransport.firstTuple (Some agreed) ModelOperation.GenerateModel)
                  (Ok generateIon)
                  "generateModel tuple"

              // The later requests use only agreed tuples, and their models are admitted.
              let compile = entries |> List.find (fun entry -> methodOf entry.Node = Some "morphir.frontend.compileModel")
              let output = decode MepJson.Default.ModelTransportTuple (at [ "params"; "output" ] compile.Node)
              let irVersion = SemanticVersion.Create(textAt [ "params"; "options"; "irVersion" ] compile.Node)
              let output = expectOk (ModelTransport.checkCompileOutput (Some agreed) output irVersion) "Agreed output"
              let compiled = at [ "result"; "model" ] entries[compile.Entry + 1].Node
              let model = decode MepJson.Default.MorphirModel compiled
              expectOk (ModelTransport.admit output agreed.Limits model) "The compiled model is admitted" |> ignore

              let generate = entries |> List.find (fun entry -> methodOf entry.Node = Some "morphir.backend.generateModel")
              let input = decode MepJson.Default.ModelTransportTuple (at [ "params"; "input" ] generate.Node)
              let input = expectOk (ModelTransport.checkTuple (Some agreed) input) "Agreed input"
              let model = decode MepJson.Default.MorphirModel (at [ "params"; "model" ] generate.Node)
              expectOk (ModelTransport.admit input agreed.Limits model) "The retained model is admitted" |> ignore
          |> testLabel "PlatformSpecific" ]
