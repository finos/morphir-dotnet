// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Envelope creation and admission (task 4.4): every pinned model-transport case, each bound on its own,
/// the check order and opaque retention.
module Morphir.Mep.Tests.AdmissionTests

open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open Expecto
open Morphir.Mep
open Morphir.Mep.Tests.Transcripts
open Morphir.Mep.Tests.ModelTransportTests

let private cases =
    JsonNode.Parse(File.ReadAllText(fixture "model-transport.cases.json")) |> nonNull

let private asset (path: string) =
    (cases["assets"] |> nonNull).AsArray()
    |> Seq.map nonNull
    |> Seq.find (fun item -> textAt [ "path" ] item = path)

let private assetBytes path =
    let content = (asset path)["content"] |> nonNull
    Convert.FromBase64String(content.GetValue<string>())

let private richBytes = assetBytes "fixtures/rich-model.ionb"

/// The case codes that name one failure with two spellings. The pin uses decoded_limit in model-0035
/// and decoded_limit_exceeded in model-0012 for the same check.
let private canonicalCode (code: string) =
    match code with
    | "decoded_limit" -> "decoded_limit_exceeded"
    | other -> other

/// What the runner decided for one case.
type Verdict =
    | Accepted of AdmittedModel option
    | Rejected of code: string * diagnostic: string option

let private reject (failure: AdmissionFailure) = Rejected(failure.Code, None)

let private (>>=) (result: Result<'T, AdmissionFailure>) (next: 'T -> Verdict) =
    match result with
    | Ok value -> next value
    | Error failure -> reject failure

let private text (node: JsonNode | null) =
    (node |> nonNull).GetValue<string>()

let private trustAll = Func<ModelGuaranteeEvidence, bool>(fun _ -> true)

/// The outer JSON member of a compile result, as the wire has it.
let private modelMember (result: JsonNode) =
    let mutable model: JsonNode | null = null

    match result.AsObject().TryGetPropertyValue("model", &model), model with
    | false, _ -> ModelMember.Absent
    | true, Null -> ModelMember.Null
    | true, NonNull model -> ModelMember.Present(decode MepJson.Default.MorphirModel model)

/// Runs one pinned case through negotiation and admission, as a host or guest would.
let run (case: JsonNode) =
    let hostOffer = case["hostOffer"] |> nonNull
    let guest = case["guestSupport"] |> nonNull
    let transport (node: JsonNode) =
        node["modelTransport"] |> Option.ofObj |> Option.map (decode MepJson.Default.ModelTransportSupport)

    let request = case["request"] |> nonNull
    let parameters = request["params"] |> nonNull
    let guestProtocol = ProtocolVersion.Create(text guest["protocolVersion"])

    let hostProtocols =
        (hostOffer["protocolVersions"] |> nonNull).AsArray() |> Seq.map (text >> ProtocolVersion.Create) |> List.ofSeq

    match Method.Find(text request["method"]) |> Option.bind ModelTransport.operationOf with
    | None ->
        // A legacy method: model transport does not apply, and the legacy request is unchanged.
        Expect.isTrue (List.contains guestProtocol hostProtocols) "The legacy version was offered"
        Accepted None
    | Some operation ->
        ModelTransport.requireModelProtocol guestProtocol
        >>= fun () ->
            let agreed =
                match transport hostOffer, transport guest with
                | Some offer, Some guestSupport ->
                    match ModelTransport.verifySelection offer (ModelTransport.select offer guestSupport) with
                    | Ok agreed -> Some agreed
                    | Error failure -> failtestf "The guest's own selection is refused: %O" failure
                | _ -> None

            let limits () = (Option.get agreed).Limits

            match operation with
            | ModelOperation.GenerateModel ->
                // The typed request and the shared guest check: the input tuple, then admission, then evidence.
                let request = decode MepJson.Default.GenerateModelRequest parameters

                GenerateModel.admit agreed trustAll request
                >>= fun admitted -> Accepted(Some admitted.Model)
            | ModelOperation.CompileModel ->
                let output = decode MepJson.Default.ModelTransportTuple (parameters["output"] |> nonNull)
                let irVersion = SemanticVersion.Create(textAt [ "options"; "irVersion" ] parameters)

                ModelTransport.checkCompileOutput agreed output irVersion
                >>= fun output ->
                    match case["result"], case["projectionSource"] with
                    | NonNull result, _ ->
                        ModelTransport.checkCompileOutcome ((at [ "success" ] result).GetValue<bool>()) (modelMember result)
                        >>= (function
                        | Some model -> ModelTransport.admit output (limits ()) model >>= (Some >> Accepted)
                        | None -> Accepted None)
                    | Null, NonNull source ->
                        let model = decode MepJson.Default.MorphirModel source

                        ModelTransport.admit (ModelTransport.tupleOf operation model) (limits ()) model
                        >>= fun admitted ->
                            match ModelTransport.planConversion agreed operation admitted false with
                            | Ok _ -> Accepted(Some admitted)
                            | Error refusal ->
                                Rejected(refusal.Code, refusal.ToDiagnostic().Code)
                    | Null, Null -> Accepted None

let private allCases =
    (cases["cases"] |> nonNull).AsArray() |> Seq.map nonNull |> List.ofSeq

let private model (tuple: ModelTransportTuple) (bytes: byte array) =
    match ModelTransport.createEnvelope tuple "urn:morphir:mep:fixture:model" "fixture-revision-1" (ReadOnlyMemory bytes) with
    | Ok envelope -> envelope
    | Error failure -> failtestf "Envelope creation failed: %O" failure

let private ionTuple = tuple ModelOperation.GenerateModel "morphir-model" ModelCodec.IonBinary "4.0.0"
let private jsonTuple = tuple ModelOperation.GenerateModel "morphir-ir" ModelCodec.MorphirJson "4.0.0"
let private wideLimits = limits 8192 4096 64 256
let private unbounded = limits Int32.MaxValue Int32.MaxValue Int32.MaxValue Int32.MaxValue

let private code (result: Result<AdmittedModel, AdmissionFailure>) =
    match result with
    | Ok _ -> "accepted"
    | Error failure -> failure.Code

let private withPayload (envelope: MorphirModel) (change: ModelPayload -> ModelPayload) =
    { envelope with Payload = change envelope.Payload }

let private evidenceFor (envelope: MorphirModel) =
    let bytes = Encoding.UTF8.GetBytes "{\"fixture\":\"checked\"}\n"

    { Guarantee = "urn:morphir:mep:fixture:checked"
      ModelId = envelope.ModelId
      ModelRevision = envelope.Revision
      ContextDigest = Sha256Digest.Create(String('1', 64))
      Scope = "urn:morphir:mep:fixture:package"
      Producer = "urn:morphir:mep:fixture:producer"
      Evidence = (model jsonTuple bytes).Payload }

/// The bound values of the pinned rich payload: 668 base64 bytes, 499 decoded bytes, depth 6, 25 nodes.
let private boundCases =
    [ "encoded", (fun value -> limits value Int32.MaxValue Int32.MaxValue Int32.MaxValue), 668, "encoded_limit_exceeded"
      "decoded", (fun value -> limits Int32.MaxValue value Int32.MaxValue Int32.MaxValue), 499, "decoded_limit_exceeded"
      "depth", (fun value -> limits Int32.MaxValue Int32.MaxValue value Int32.MaxValue), 6, "depth_limit_exceeded"
      "nodes", (fun value -> limits Int32.MaxValue Int32.MaxValue Int32.MaxValue value), 25, "node_limit_exceeded" ]

[<Tests>]
let tests =
    testList
        "MEP model admission"
        [ testList
              "pinned model-transport cases"
              [ for case in allCases do
                    let id = text case["id"]
                    let kind = text case["kind"]
                    let description = text case["description"]

                    testCase $"{id} ({kind}) {description}"
                    <| fun _ ->
                        let expected = case["expected"] |> nonNull

                        match text expected["admission"], run case with
                        | "accept", Accepted _ -> ()
                        | "reject", Rejected(code, diagnostic) ->
                            Expect.equal code (canonicalCode (text expected["code"])) "The pinned failure"

                            match expected["diagnostic"] with
                            | NonNull pinned -> Expect.equal diagnostic (Some(pinned.GetValue<string>())) "Diagnostic code"
                            | Null -> ()
                        | admission, verdict -> failtestf "Expected %s, got %A" admission verdict ]

          testCase "the pin has 35 cases of seven kinds"
          <| fun _ ->
              Expect.equal allCases.Length 35 "Every pinned case runs"

              Expect.equal
                  (allCases |> List.map (fun case -> text case["kind"]) |> List.distinct |> List.sort)
                  [ "compatibility"; "evidence"; "integrity"; "limits"; "negotiation"; "outcome"; "preservation" ]
                  "Every case kind"

          testCase "no IR reader runs for a refused envelope"
          <| fun _ ->
              let mutable reads = 0

              let reader (admitted: AdmittedModel) =
                  reads <- reads + 1
                  admitted.Bytes.Length

              let accepted =
                  allCases
                  |> List.filter (fun case ->
                      match run case with
                      | Accepted(Some admitted) ->
                          reader admitted |> ignore
                          true
                      | Accepted None -> false
                      | Rejected _ -> false)

              Expect.equal reads accepted.Length "Only admitted models reach the reader"
              Expect.equal (accepted |> List.map (fun case -> text case["id"])) [ "model-0001"; "model-0024"; "model-0027"; "model-0028" ] "The admitted cases"

          testCase "model-0024 retains the complete bytes with their unknown facts"
          <| fun _ ->
              let case = allCases |> List.find (fun case -> text case["id"] = "model-0024")
              let expected = case["expected"] |> nonNull

              match run case with
              | Accepted(Some admitted) ->
                  let bytes = admitted.Bytes.ToArray()
                  Expect.equal bytes (assetBytes (text expected["retainedAsset"])) "Byte for byte"
                  Expect.equal (Sha256Digest.Compute(ReadOnlySpan bytes)).Value (text expected["retainedSha256"]) "Digest"

                  let contains (needle: byte array) =
                      bytes.AsSpan().IndexOf(ReadOnlySpan needle) >= 0

                  // The symbol table keeps the annotation names in order, and the owner is a string.
                  let symbols =
                      [ for name in (expected["annotations"] |> nonNull).AsArray() -> text name ]

                  Expect.equal symbols [ "source_name"; "target_name" ] "Ordered annotations"
                  let first = Encoding.UTF8.GetString(bytes).IndexOf "source_name"
                  let second = Encoding.UTF8.GetString(bytes).IndexOf "target_name"
                  Expect.isTrue (first > 0 && second > first) "Annotation symbols in order"
                  Expect.isTrue (contains (Encoding.UTF8.GetBytes(text expected["unknownEnrichmentOwner"]))) "Owner"
                  // 9007199254740993 as an Ion positive int of 7 bytes, and 123.4500 as an Ion decimal.
                  Expect.equal (text expected["exactInteger"]) "9007199254740993" "Pinned integer"
                  Expect.isTrue (contains [| 0x27uy; 0x20uy; 0uy; 0uy; 0uy; 0uy; 0uy; 1uy |]) "Exact integer bytes"
                  Expect.equal (text expected["decimal"]) "123.4500" "Pinned decimal"
                  Expect.isTrue (contains [| 0x54uy; 0xC4uy; 0x12uy; 0xD6uy; 0x44uy |]) "Exact decimal bytes"
                  Expect.isTrue admitted.Facts.HasAnnotations "The scan saw the annotations"
                  Expect.isTrue admitted.Facts.HasWideIntegers "The scan saw the wide integer"
                  Expect.isTrue admitted.Facts.HasDecimals "The scan saw the decimal"
              | other -> failtestf "Expected an admitted model, got %A" other

          testList
              "envelope creation"
              [ testCase "the pinned envelope is what creation makes from the asset bytes"
                <| fun _ ->
                    let created = model ionTuple richBytes
                    let pinned = asset "fixtures/rich-model.ionb"
                    Expect.equal created.Payload.Content (text pinned["content"]) "Canonical padded base64"
                    Expect.equal created.Payload.DecodedByteLength 499 "Exact decoded length"
                    Expect.equal created.Payload.Sha256.Value (text pinned["sha256"]) "Lowercase SHA-256"
                    Expect.equal created.Payload.Codec ModelCodec.IonBinary "Codec of the tuple"
                    Expect.equal created.Profile "morphir-model" "Profile of the tuple"
                    Expect.equal created.IrVersion (SemanticVersion.Create "4.0.0") "IR version of the tuple"
                    Expect.equal created.EnvelopeVersion (SemanticVersion.Create "0.1.0-draft.1") "Envelope draft"
                    Expect.isEmpty created.Evidence "No evidence"
                    Expect.isEmpty created.Critical "No critical paths"
                    Expect.equal (code (ModelTransport.admit ionTuple unbounded created)) "accepted" "Admitted"

                testCase "a derived revision is deterministic and comes from the digest"
                <| fun _ ->
                    let derive bytes =
                        match ModelTransport.createDerivedEnvelope ionTuple "urn:m" (ReadOnlyMemory bytes) with
                        | Ok envelope -> envelope
                        | Error failure -> failtestf "%O" failure

                    let first = derive richBytes
                    let again = derive (Array.copy richBytes)
                    Expect.equal first.Revision again.Revision "Same bytes, same revision"
                    Expect.equal first.Revision (ModelTransport.deriveRevision first.Payload.Sha256) "From the digest"
                    Expect.equal first.Revision ("sha256:" + first.Payload.Sha256.Value) "Spelling"
                    Expect.notEqual (derive [| 1uy |]).Revision first.Revision "Other bytes, other revision"

                testCase "creation refuses empty identities"
                <| fun _ ->
                    let create modelId revision tuple =
                        ModelTransport.createEnvelope tuple modelId revision (ReadOnlyMemory richBytes)

                    Expect.equal (create "" "r" ionTuple) (Error EnvelopeFailure.EmptyModelId) "Model id"
                    Expect.equal (create "m" "" ionTuple) (Error EnvelopeFailure.EmptyRevision) "Revision"
                    Expect.equal (create "m" "r" { ionTuple with Profile = "" }) (Error EnvelopeFailure.EmptyProfile) "Profile"

                testCase "an empty payload is a canonical empty document"
                <| fun _ ->
                    let created = model jsonTuple [||]
                    Expect.equal created.Payload.Content "" "No content"
                    Expect.equal created.Payload.DecodedByteLength 0 "Zero bytes"
                    // Empty bytes are not a JSON document.
                    Expect.equal (code (ModelTransport.admit jsonTuple unbounded created)) "malformed_payload" "Refused" ]

          testList
              "each bound on its own"
              [ for name, make, bound, failure in boundCases do
                    testCase $"{name}: {bound} passes and {bound - 1} fails"
                    <| fun _ ->
                        let envelope = model ionTuple richBytes
                        Expect.equal (code (ModelTransport.admit ionTuple (make bound) envelope)) "accepted" "At the bound"
                        Expect.equal (code (ModelTransport.admit ionTuple (make (bound - 1)) envelope)) failure "Below it"

                testCase "JSON depth and nodes count values, not member names"
                <| fun _ ->
                    // Root object (depth 1), member array (depth 2), two numbers (depth 3): 4 nodes.
                    let envelope = model jsonTuple (Encoding.UTF8.GetBytes """{"a":[1,2]}""")
                    let admit l = code (ModelTransport.admit jsonTuple l envelope)
                    Expect.equal (admit (limits 100 100 3 4)) "accepted" "At the bounds"
                    Expect.equal (admit (limits 100 100 2 4)) "depth_limit_exceeded" "Depth"
                    Expect.equal (admit (limits 100 100 3 3)) "node_limit_exceeded" "Nodes"

                testCase "decoded and node bounds sum across the model and its evidence"
                <| fun _ ->
                    let bare = model ionTuple richBytes
                    let envelope = { bare with Evidence = [| evidenceFor bare |] }
                    let admit l = code (ModelTransport.admit ionTuple l envelope)
                    Expect.equal (admit (limits 8192 (499 + 22) 64 256)) "accepted" "Both fit"
                    Expect.equal (admit (limits 8192 (499 + 21) 64 256)) "decoded_limit_exceeded" "Aggregate bytes"
                    // The evidence {"fixture":"checked"} has 2 nodes.
                    Expect.equal (admit (limits 8192 4096 64 27)) "accepted" "Both fit"
                    Expect.equal (admit (limits 8192 4096 64 26)) "node_limit_exceeded" "Aggregate nodes"
                    Expect.equal (admit (limits (668 + 32) 4096 64 256)) "accepted" "Both fit"
                    Expect.equal (admit (limits (668 + 31) 4096 64 256)) "encoded_limit_exceeded" "Aggregate encoded" ]

          testList
              "check order and canonical form"
              [ testCase "the encoded bound is checked before base64"
                <| fun _ ->
                    let envelope = withPayload (model ionTuple richBytes) (fun p -> { p with Content = "!" + p.Content })
                    Expect.equal (code (ModelTransport.admit ionTuple (limits 668 4096 64 256) envelope)) "encoded_limit_exceeded" "First"
                    Expect.equal (code (ModelTransport.admit ionTuple wideLimits envelope)) "invalid_base64" "Then form"

                testCase "the base64 bound is checked before the declared length"
                <| fun _ ->
                    let envelope = withPayload (model ionTuple richBytes) (fun p -> { p with DecodedByteLength = 1 })
                    Expect.equal (code (ModelTransport.admit ionTuple (limits 8192 498 64 256) envelope)) "decoded_limit_exceeded" "Bound"
                    Expect.equal (code (ModelTransport.admit ionTuple wideLimits envelope)) "length_mismatch" "Then length"

                testCase "the length is checked before the digest, and the digest before depth"
                <| fun _ ->
                    let wrongDigest = Sha256Digest.Create(String('0', 64))

                    let envelope =
                        withPayload (model ionTuple richBytes) (fun p -> { p with DecodedByteLength = 500; Sha256 = wrongDigest })

                    Expect.equal (code (ModelTransport.admit ionTuple wideLimits envelope)) "length_mismatch" "Length"
                    let envelope = withPayload envelope (fun p -> { p with DecodedByteLength = 499 })
                    Expect.equal (code (ModelTransport.admit ionTuple (limits 8192 4096 1 1) envelope)) "digest_mismatch" "Digest"

                testCase "depth and nodes are checked before the tuple"
                <| fun _ ->
                    let envelope = model ionTuple richBytes
                    let other = { ionTuple with Profile = "morphir-ir" }
                    Expect.equal (code (ModelTransport.admit other (limits 8192 4096 1 256) envelope)) "depth_limit_exceeded" "Depth"
                    Expect.equal (code (ModelTransport.admit other wideLimits envelope)) "profile_mismatch" "Then profile"

                for name, change, failure in
                    [ "profile", (fun (t: ModelTransportTuple) -> { t with Profile = "morphir-ir" }), "profile_mismatch"
                      "codec", (fun t -> { t with Codec = ModelCodec.MorphirJson }), "codec_mismatch"
                      "envelope version", (fun t -> { t with EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.2" }), "envelope_version_mismatch"
                      "IR version", (fun t -> { t with IrVersion = SemanticVersion.Create "3.0.0" }), "ir_version_mismatch" ] do
                    testCase $"the {name} must match the negotiated tuple"
                    <| fun _ ->
                        let envelope = model ionTuple richBytes
                        Expect.equal (code (ModelTransport.admit (change ionTuple) wideLimits envelope)) failure "Refused"

                for content, reason in
                    [ "QQ", "no padding"
                      "QR==", "non-zero trailing bits"
                      "QUI=QUI=", "padding inside"
                      "QUJD\nREVG", "a line break"
                      " QUJD", "a space"
                      "QUJD-_==", "the URL alphabet"
                      "Q===", "three padding characters"
                      "QUJD\tREVG", "a tab"
                      "QUJDREVG\n", "a trailing line break" ] do
                    testCase $"base64 with {reason} is refused"
                    <| fun _ ->
                        let envelope = withPayload (model jsonTuple [| 0x41uy |]) (fun p -> { p with Content = content })
                        Expect.equal (code (ModelTransport.admit jsonTuple wideLimits envelope)) "invalid_base64" reason

                testCase "malformed Ion binary is refused"
                <| fun _ ->
                    for bytes in
                        [ [| 0x41uy |] // no version marker
                          [| 0xE0uy; 1uy; 0uy; 0xEAuy; 0xD5uy; 0x81uy |] // a struct longer than the payload
                          [| 0xE0uy; 1uy; 0uy; 0xEAuy; 0xF0uy |] // a reserved type
                          [| 0xE0uy; 1uy; 0uy; 0xEAuy; 0x2Euy |] // a length that never ends
                          // A string length of Int32.MaxValue: the end offset must not wrap negative.
                          [| 0xE0uy; 1uy; 0uy; 0xEAuy; 0x8Euy; 0x07uy; 0x7Fuy; 0x7Fuy; 0x7Fuy; 0xFFuy |]
                          // An annotation wrapper length of Int32.MaxValue.
                          [| 0xE0uy; 1uy; 0uy; 0xEAuy; 0xEEuy; 0x07uy; 0x7Fuy; 0x7Fuy; 0x7Fuy; 0xFFuy; 0x81uy; 0x84uy; 0x20uy |] ] do
                        let envelope = model ionTuple bytes
                        Expect.equal (code (ModelTransport.admit ionTuple wideLimits envelope)) "malformed_payload" "Refused"

                testCase "malformed JSON is refused"
                <| fun _ ->
                    for json in [ "{"; "[1,]"; "1 2"; "{\"a\":1}//" ] do
                        let envelope = model jsonTuple (Encoding.UTF8.GetBytes json)
                        Expect.equal (code (ModelTransport.admit jsonTuple wideLimits envelope)) "malformed_payload" json

                testCase "deep JSON is refused without recursion"
                <| fun _ ->
                    let json = String('[', 100000) + String(']', 100000)
                    let envelope = model jsonTuple (Encoding.UTF8.GetBytes json)
                    Expect.equal (code (ModelTransport.admit jsonTuple unbounded envelope)) "accepted" "No stack limit"
                    Expect.equal (code (ModelTransport.admit jsonTuple (limits Int32.MaxValue Int32.MaxValue 64 Int32.MaxValue) envelope)) "depth_limit_exceeded" "Depth"

                testCase "deep Ion is scanned without recursion"
                <| fun _ ->
                    // 20000 nested one-element lists around a null, built from the inside out.
                    let depth = 20000

                    let header (length: int) =
                        if length < 14 then
                            [| 0xB0uy ||| byte length |]
                        else
                            let rec digits (value: int) (acc: byte list) =
                                if value < 128 then byte value :: acc else digits (value >>> 7) (byte (value &&& 127) :: acc)

                            let varUInt = digits length [] |> Array.ofList
                            varUInt[varUInt.Length - 1] <- varUInt[varUInt.Length - 1] ||| 0x80uy
                            Array.append [| 0xBEuy |] varUInt

                    // The fold starts at the null (1 byte) and ends with the outermost header first.
                    let headers =
                        ((1, []), [ 1..depth ])
                        ||> List.fold (fun (length, acc) _ ->
                            let next = header length
                            length + next.Length, next :: acc)
                        |> snd

                    let body = Array.concat (headers @ [ [| 0x0Fuy |] ])
                    let bytes = Array.append [| 0xE0uy; 1uy; 0uy; 0xEAuy |] body
                    let envelope = model ionTuple bytes
                    Expect.equal (code (ModelTransport.admit ionTuple unbounded envelope)) "accepted" "No stack limit"
                    Expect.equal (code (ModelTransport.admit ionTuple (limits Int32.MaxValue Int32.MaxValue depth Int32.MaxValue) envelope)) "depth_limit_exceeded" "Depth" ]

          testList
              "critical paths"
              [ testCase "known envelope paths are admitted"
                <| fun _ ->
                    let envelope = { model ionTuple richBytes with Critical = [| "payload"; "payload.sha256"; "evidence" |] }
                    Expect.equal (code (ModelTransport.admit ionTuple wideLimits envelope)) "accepted" "Known"

                testCase "a known parent does not admit an unknown child"
                <| fun _ ->
                    for path in [ "payload.futureMember"; "futureMember"; "Payload"; "payload." ] do
                        let envelope = { model ionTuple richBytes with Critical = [| "payload"; path |] }

                        match ModelTransport.admit ionTuple wideLimits envelope with
                        | Error(AdmissionFailure.UnknownCriticalPath found) -> Expect.equal found path "Names the path"
                        | other -> failtestf "%s must be refused: %A" path other ]

          testList
              "guarantee evidence"
              [ let required = [| { Guarantee = "urn:morphir:mep:fixture:checked"; Scope = "urn:morphir:mep:fixture:package" } |]
                let agreed = support [ ionTuple ] wideLimits [ "urn:morphir:mep:fixture:checked" ]
                let context = Sha256Digest.Create(String('1', 64))

                let admitted (evidence: ModelGuaranteeEvidence list) =
                    let bare = model ionTuple richBytes

                    match ModelTransport.admit ionTuple wideLimits { bare with Evidence = Array.ofList evidence } with
                    | Ok value -> value
                    | Error failure -> failtestf "%O" failure

                testCase "matching evidence establishes the required guarantee"
                <| fun _ ->
                    let model' = admitted []
                    let evidence = evidenceFor model'.Envelope
                    let model' = admitted [ evidence ]

                    match ModelTransport.establish agreed context required trustAll model' with
                    | Ok established ->
                        Expect.equal established.Count 1 "One guarantee"
                        Expect.equal established[0].Requirement required[0] "The requirement"
                        Expect.equal established[0].Evidence.Evidence evidence "The evidence"
                        Expect.equal established[0].Evidence.Bytes.Length 22 "Its verified bytes"
                    | Error failure -> failtestf "%O" failure

                testCase "untrusted evidence does not establish a guarantee"
                <| fun _ ->
                    let evidence = evidenceFor (admitted []).Envelope
                    let distrust = Func<ModelGuaranteeEvidence, bool>(fun _ -> false)

                    match ModelTransport.establish agreed context required distrust (admitted [ evidence ]) with
                    | Error(AdmissionFailure.MissingEvidence requirement) -> Expect.equal requirement required[0] "Names it"
                    | other -> failtestf "%A" other

                testCase "no requirement needs no evidence"
                <| fun _ ->
                    Expect.isOk (ModelTransport.establish agreed context [||] trustAll (admitted [])) "Nothing required"

                testCase "evidence bytes are verified like the model"
                <| fun _ ->
                    let evidence = evidenceFor (admitted []).Envelope
                    let broken = { evidence with Evidence = { evidence.Evidence with Sha256 = Sha256Digest.Create(String('0', 64)) } }
                    let bare = model ionTuple richBytes
                    Expect.equal (code (ModelTransport.admit ionTuple wideLimits { bare with Evidence = [| broken |] })) "digest_mismatch" "Refused" ]

          testList
              "compile outcome"
              [ let envelope = model ionTuple richBytes

                testCase "success needs a model, and failure must not have one"
                <| fun _ ->
                    Expect.equal (ModelTransport.checkCompileOutcome true (ModelMember.Present envelope)) (Ok(Some envelope)) "Success"
                    Expect.equal (ModelTransport.checkCompileOutcome false ModelMember.Absent) (Ok None) "Failure"
                    Expect.equal (ModelTransport.checkCompileOutcome true ModelMember.Absent) (Error AdmissionFailure.MissingModel) "Missing"
                    Expect.equal (ModelTransport.checkCompileOutcome true ModelMember.Null) (Error AdmissionFailure.MissingModel) "Null"
                    Expect.equal (ModelTransport.checkCompileOutcome false ModelMember.Null) (Error AdmissionFailure.ModelOnFailure) "Null on failure"
                    Expect.equal (ModelTransport.checkCompileOutcome false (ModelMember.Present envelope)) (Error AdmissionFailure.ModelOnFailure) "Model on failure" ]

          testList
              "conversion"
              [ let compileIon = tuple ModelOperation.CompileModel "morphir-model" ModelCodec.IonBinary "4.0.0"
                let compileJson = tuple ModelOperation.CompileModel "morphir-model" ModelCodec.MorphirJson "4.0.0"
                let compilePlain = tuple ModelOperation.CompileModel "morphir-ir" ModelCodec.MorphirJson "4.0.0"
                let compileJson3 = tuple ModelOperation.CompileModel "morphir-ir" ModelCodec.MorphirJson "3.0.0"

                let rich =
                    match ModelTransport.admit compileIon wideLimits (model compileIon richBytes) with
                    | Ok value -> value
                    | Error failure -> failtestf "%O" failure

                let agreed tuples = Some(support tuples wideLimits [])

                testCase "a model in an agreed tuple is sent unchanged"
                <| fun _ ->
                    Expect.equal
                        (ModelTransport.planConversion (agreed [ compilePlain; compileIon ]) ModelOperation.CompileModel rich false)
                        (Ok(ConversionPlan.Unchanged compileIon))
                        "No conversion"

                testCase "rich Ion to Morphir JSON names each loss"
                <| fun _ ->
                    let losses = ModelTransport.conversionLosses rich compileJson
                    Expect.equal (List.ofSeq losses) [ ConversionLoss.Annotations; ConversionLoss.IntegerPrecision; ConversionLoss.DecimalPrecision; ConversionLoss.IonOnlyValues ] "Every loss"
                    Expect.contains (List.ofSeq (ModelTransport.conversionLosses rich compilePlain)) ConversionLoss.ProfileContent "Profile change"

                testCase "a lossy conversion is refused with data.lossy_conversion, unless loss is allowed"
                <| fun _ ->
                    match ModelTransport.planConversion (agreed [ compileJson ]) ModelOperation.CompileModel rich false with
                    | Error(ConversionRefusal.LossyConversion(target, losses) as refusal) ->
                        Expect.equal target compileJson "The first reachable tuple"
                        Expect.isNonEmpty losses "With its losses"
                        Expect.equal refusal.Code "lossy_conversion" "Not tuple_unavailable"
                        Expect.equal (refusal.ToDiagnostic().Code) (Some "data.lossy_conversion") "Diagnostic"
                    | other -> failtestf "%A" other

                    match ModelTransport.planConversion (agreed [ compileJson ]) ModelOperation.CompileModel rich true with
                    | Ok(ConversionPlan.Convert(target, losses)) ->
                        Expect.equal target compileJson "Allowed"
                        Expect.isNonEmpty losses "The losses are reported"
                    | other -> failtestf "%A" other

                testCase "the first lossless agreed tuple wins over an earlier lossy one"
                <| fun _ ->
                    let plain = Encoding.UTF8.GetBytes """{"formatVersion":"4.0.0"}"""
                    let compileIr = tuple ModelOperation.CompileModel "morphir-ir" ModelCodec.IonBinary "4.0.0"

                    let source =
                        match ModelTransport.admit compileJson wideLimits (model compileJson plain) with
                        | Ok value -> value
                        | Error failure -> failtestf "%O" failure

                    Expect.equal
                        (ModelTransport.planConversion (agreed [ compileIr; compileIon ]) ModelOperation.CompileModel source false)
                        (Ok(ConversionPlan.Convert(compileIon, [||])))
                        "Lossless codec change in host order"

                testCase "no reachable agreed tuple is tuple_unavailable"
                <| fun _ ->
                    for tuples in [ []; [ compileJson3 ] ] do
                        match ModelTransport.planConversion (agreed tuples) ModelOperation.CompileModel rich false with
                        | Error(ConversionRefusal.TupleUnavailable unavailable as refusal) ->
                            Expect.equal unavailable.Operation ModelOperation.CompileModel "Operation"
                            Expect.equal refusal.Code "extension.tuple_unavailable" "Code"
                        | other -> failtestf "%A" other ]

          testCase "every failure maps to -32602 except a missing model protocol"
          <| fun _ ->
              let failure = AdmissionFailure.DigestMismatch(Sha256Digest.Create(String('0', 64)), Sha256Digest.Create(String('1', 64)))
              let error = failure.ToRpcError()
              Expect.equal error.Code -32602 "Invalid params"
              Expect.equal error.Message "Model admission refused: digest_mismatch" "The text of the upstream guests"
              Expect.isNone error.Data "No data member that the pin does not define"
              let protocol = AdmissionFailure.UnsupportedProtocol(ProtocolVersion.Create "0.1")
              Expect.equal (protocol.ToRpcError().Code) -32013 "Capability unavailable" ]
