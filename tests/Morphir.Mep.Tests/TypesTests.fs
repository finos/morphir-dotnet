// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.TypesTests

open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization.Metadata
open Expecto
open Morphir.Mep
open Morphir.Mep.Tests.Transcripts

/// Reads with the generated reader. The untyped overload keeps F# nullness exact for generic code.
let private read'<'T when 'T: not null> (info: JsonTypeInfo<'T>) (json: string) : 'T =
    match JsonSerializer.Deserialize(json, info :> JsonTypeInfo) with
    | null -> failtest "The reader returned null"
    | value -> unbox<'T> value

/// Reads, writes and reads again. The second write must equal the first; the written JSON is returned.
let private roundTrip<'T when 'T: not null> (info: JsonTypeInfo<'T>) (node: JsonNode) =
    let first = read' info (node.ToJsonString())
    let written = JsonSerializer.Serialize(first, info)
    let again = JsonSerializer.Serialize(read' info written, info)
    Expect.equal again written $"Writing {typeof<'T>.Name} is idempotent"
    JsonNode.Parse written |> nonNull

/// The original JSON without the empty critical lists that a writer leaves out.
let rec private withoutEmptyCritical (node: JsonNode) : JsonNode =
    match node with
    | :? JsonObject as value ->
        let copy = JsonObject()

        for KeyValue(name, item) in value do
            match name, item with
            | "critical", (:? JsonArray as list) when list.Count = 0 -> ()
            | _, null -> copy[name] <- null
            | _, item -> copy[name] <- withoutEmptyCritical item

        copy
    | :? JsonArray as value -> JsonArray(value |> Seq.map (fun item -> item |> Option.ofObj |> Option.map withoutEmptyCritical |> Option.toObj) |> Array.ofSeq)
    | value -> value.DeepClone()

let private exact<'T when 'T: not null> (info: JsonTypeInfo<'T>) (node: JsonNode) =
    let written = roundTrip info node

    Expect.isTrue
        (JsonNode.DeepEquals(written, withoutEmptyCritical node))
        $"{typeof<'T>.Name} keeps every member: {node.ToJsonString()} became {written.ToJsonString()}"

let private member' (name: string) (node: JsonNode) = node[name] |> Option.ofObj

let private methodOf (node: JsonNode) =
    member' "method" node |> Option.map (fun value -> value.GetValue<string>())

/// Round-trips every typed part of one transcript message.
let private checkMessage (initializeIds: Set<string>) (entry: Entry) =
    let node = entry.Node

    member' "id" node
    |> Option.iter (fun id -> exact MepJson.Default.RequestId id)

    let parameters = member' "params" node

    match methodOf node, parameters with
    | Some "morphir.initialize", Some value -> roundTrip MepJson.Default.InitializeParams value |> ignore
    | Some "morphir.progress", Some value -> exact MepJson.Default.ProgressParams value
    | Some "$/cancelRequest", Some value -> exact MepJson.Default.CancelParams value
    | Some "morphir.frontend.compileModel", Some value ->
        exact MepJson.Default.ModelTransportTuple (value["output"] |> nonNull)
    | Some "morphir.backend.generateModel", Some value ->
        exact MepJson.Default.MorphirModel (value["model"] |> nonNull)
        exact MepJson.Default.ModelTransportTuple (value["input"] |> nonNull)
    | _ -> ()

    member' "error" node
    |> Option.iter (fun error -> exact MepJson.Default.RpcError error)

    match member' "id" node, member' "result" node with
    | Some id, Some result when initializeIds.Contains(id.ToJsonString()) ->
        roundTrip MepJson.Default.InitializeResult result |> ignore
    | _, Some result ->
        member' "model" result
        |> Option.iter (fun model -> exact MepJson.Default.MorphirModel model)
    | _ -> ()

let private initializeIds (entries: Entry list) =
    entries
    |> List.filter (fun entry -> methodOf entry.Node = Some "morphir.initialize")
    |> List.map (fun entry -> (entry.Node["id"] |> nonNull).ToJsonString())
    |> Set.ofList

let private extension =
    { Id = "org.finos.morphir.example"
      Name = "Example"
      Version = "1.0.0"
      Description = None
      Types = [| ExtensionType.Validator |]
      Author = None
      Homepage = None
      License = None
      MinSdkVersion = None }

let private writtenNames<'T when 'T: not null> (info: JsonTypeInfo<'T>) (value: 'T) =
    use document = JsonDocument.Parse(JsonSerializer.Serialize(value, info))
    document.RootElement.EnumerateObject() |> Seq.map _.Name |> List.ofSeq

[<Tests>]
let tests =
    testList
        "MEP session types"
        [ for name in all do
              testCase $"every message of {name} round-trips"
              <| fun _ ->
                  let entries = read name
                  Expect.isNonEmpty entries "The transcript has messages"
                  let ids = initializeIds entries
                  entries |> List.iter (checkMessage ids)
              |> testLabel "PlatformSpecific"

          testCase "the session initialize messages keep every member"
          <| fun _ ->
              let entries = read "session-model-transport.md"
              exact MepJson.Default.InitializeParams (entries[0].Node["params"] |> nonNull)
              exact MepJson.Default.InitializeResult (entries[1].Node["result"] |> nonNull)
          |> testLabel "PlatformSpecific"

          testCase "an absent peer kind reads as unspecified and an unknown kind is unspecified"
          <| fun _ ->
              let absent = read' MepJson.Default.PeerInfo """{"name":"a","version":"1"}"""
              Expect.equal absent.Kind PeerKind.Unspecified "Absent kind"
              let unknown = read' MepJson.Default.PeerInfo """{"kind":"ide","name":"a","version":"1"}"""
              Expect.equal unknown.Kind PeerKind.Unspecified "Unknown kind"

          testCase "the extension info uses snake_case wire names and leaves out absent members"
          <| fun _ ->
              Expect.equal
                  (writtenNames MepJson.Default.ExtensionInfo extension)
                  [ "id"; "name"; "version"; "types" ]
                  "Absent optional members are not written"

              let withSdk =
                  { extension with
                      MinSdkVersion = Some "0.4.0" }

              Expect.contains
                  (writtenNames MepJson.Default.ExtensionInfo withSdk)
                  "min_sdk_version"
                  "The contract's wire name"

          testCase "absent optional members are not written"
          <| fun _ ->
              let progress: ProgressParams =
                  { RequestId = RequestId.Number 1UL
                    Kind = ProgressKind.Begin
                    Message = "Starting"
                    Percentage = None }

              Expect.equal
                  (JsonSerializer.Serialize(progress, MepJson.Default.ProgressParams))
                  """{"requestId":1,"kind":"begin","message":"Starting"}"""
                  "No percentage"

              let error: RpcError = { Code = -32601; Message = "Method not found"; Data = None }

              Expect.equal
                  (JsonSerializer.Serialize(error, MepJson.Default.RpcError))
                  """{"code":-32601,"message":"Method not found"}"""
                  "No data"

              let diagnostic: Diagnostic =
                  { Severity = DiagnosticSeverity.Warning
                    Code = None
                    Message = "m"
                    Location = None
                    Related = [||]
                    Data = None }

              Expect.equal
                  (JsonSerializer.Serialize(diagnostic, MepJson.Default.Diagnostic))
                  """{"severity":"warning","message":"m"}"""
                  "No code, location, related or data"

          testCase "a null for a required member fails"
          <| fun _ ->
              let cases: (string * (string -> unit)) list =
                  [ """{"protocolVersions":["0.2.0-draft.1"],"host":null}""",
                    read' MepJson.Default.InitializeParams >> ignore
                    """{"code":1,"message":null}""", read' MepJson.Default.RpcError >> ignore
                    """{"severity":null,"message":"m"}""", read' MepJson.Default.Diagnostic >> ignore
                    """{"requestId":null,"kind":"begin","message":"m"}""",
                    read' MepJson.Default.ProgressParams >> ignore
                    """{"id":null}""", read' MepJson.Default.CancelParams >> ignore ]

              for json, reader in cases do
                  Expect.throwsT<JsonException> (fun () -> reader json) $"Refused: {json}"

          testCase "request ids keep their token kind"
          <| fun _ ->
              for json in [ "\"req-abc\""; "\"\""; "42"; "9223372036854775808"; "18446744073709551615" ] do
                  let id = read' MepJson.Default.RequestId json
                  Expect.equal (JsonSerializer.Serialize(id, MepJson.Default.RequestId)) json "Same token"

              for json in [ "-1"; "1.5"; "18446744073709551616"; "true"; "{}" ] do
                  Expect.throwsT<JsonException>
                      (fun () -> read' MepJson.Default.RequestId json |> ignore)
                      $"Refused: {json}"

          testCase "versions and digests are parsed at the reader"
          <| fun _ ->
              let tuple version =
                  $$"""{"operation":"morphir.frontend.compileModel","profile":"p","codec":"ion-binary","envelopeVersion":"{{version}}","irVersion":"4.0.0"}"""

              Expect.equal
                  (read' MepJson.Default.ModelTransportTuple (tuple "0.1.0-draft.1")).EnvelopeVersion
                  (SemanticVersion.Create "0.1.0-draft.1")
                  "Exact draft"

              Expect.throwsT<JsonException>
                  (fun () -> read' MepJson.Default.ModelTransportTuple (tuple "0.1") |> ignore)
                  "Not full SemVer"

              let payload digest =
                  $$"""{"codec":"ion-binary","layout":"single-document","decodedByteLength":0,"sha256":"{{digest}}","content":""}"""

              let digest = Sha256Digest.Compute(System.ReadOnlySpan<byte>.Empty)
              Expect.equal (read' MepJson.Default.ModelPayload (payload digest.Value)).Sha256 digest "Digest"

              Expect.throwsT<JsonException>
                  (fun () -> read' MepJson.Default.ModelPayload (payload (digest.Value.ToUpperInvariant())) |> ignore)
                  "Uppercase digits are refused"

          testCase "a draft.1 claim set reads as draft.2 and writes draft.2"
          <| fun _ ->
              let json =
                  """{"statementVersion":"0.1.0-draft.1","protocolVersions":["0.2.0-draft.1"],"extension":{"id":"x","name":"X","version":"1.0.0","types":["validator"]},"capabilities":{"validator":{"x":1}}}"""

              let claims = read' MepJson.Default.CapabilityClaimSet json
              Expect.equal claims.ClaimsVersion (SemanticVersion.Create "0.1.0-draft.2") "Read as draft.2"
              let written = JsonSerializer.Serialize(claims, MepJson.Default.CapabilityClaimSet)
              Expect.stringContains written "\"claimsVersion\":\"0.1.0-draft.2\"" "Written as draft.2"
              Expect.stringContains written "\"validator\":{\"x\":1}" "Open capabilities are kept"

              let critical =
                  json.Replace("}}}", "}},\"critical\":[\"claimsVersion\"]}")

              Expect.throwsT<JsonException>
                  (fun () -> read' MepJson.Default.CapabilityClaimSet critical |> ignore)
                  "A draft.1 critical list that names claimsVersion is refused"

          testCase "unknown capability members are kept beside the named members"
          <| fun _ ->
              let json =
                  """{"streaming":false,"incremental":false,"cancellation":true,"progress":false,"x-future":{"a":[1]}}"""

              let capabilities = read' MepJson.Default.ExtensionCapabilities json
              Expect.isTrue capabilities.Cancellation "Named member"
              Expect.isTrue (capabilities.Extra.ContainsKey "x-future") "Extra member"

              Expect.isTrue
                  (JsonNode.DeepEquals(
                      JsonNode.Parse(JsonSerializer.Serialize(capabilities, MepJson.Default.ExtensionCapabilities)),
                      JsonNode.Parse json
                  ))
                  "Written back" ]
