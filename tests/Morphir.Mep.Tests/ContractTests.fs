// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.ContractTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization.Metadata
open Expecto
open Morphir.Mep

let private pin =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mep", "mep.ir.json")

/// One member of a pinned record constructor.
type private PinnedField = { Name: string; Optional: bool }

let private camel (name: string) =
    let words = name.Split '-'

    words[0]
    + (words
       |> Array.skip 1
       |> Array.map (fun word -> string (Char.ToUpperInvariant word[0]) + word.Substring(1))
       |> String.concat "")

/// The wire members of a record type of the pin: camelCase, or snake_case when the contract says so.
let private pinnedFields (root: JsonElement) (typeName: string) (snakeCase: bool) =
    root
        .GetProperty("distribution")
        .GetProperty("Library")
        .GetProperty("def")
        .GetProperty("modules")
        .GetProperty("mep")
        .GetProperty("Public")
        .GetProperty("types")
        .GetProperty(typeName)
        .GetProperty("Public")
        .GetProperty("CustomTypeDefinition")
        .GetProperty("constructors")
        .GetProperty(typeName)
        .EnumerateArray()
    |> Seq.map (fun argument ->
        let name = argument[0].GetString() |> nonNull

        let reference =
            match argument[1].TryGetProperty "Reference" with
            | true, value -> value.GetProperty("fqname").GetString()
            | false, _ -> null

        { Name = (if snakeCase then name.Replace('-', '_') else camel name)
          Optional = reference = "morphir/SDK:maybe#maybe" })
    |> List.ofSeq

/// A record type of the pin and its generated codec.
type private Checked =
    {
        TypeName: string
        SnakeCase: bool
        /// Reads a JSON object and writes it again.
        RoundTrip: string -> string
        /// A complete sample: every optional member present and every collection nonempty.
        Sample: string
        /// Members that the contract text lets a reader default when absent.
        Defaulted: string list
        /// Contract members that are not wire members (flattened extension data).
        Flattened: string list
    }

let private codec<'T> (info: JsonTypeInfo<'T>) =
    fun (json: string) ->
        match JsonSerializer.Deserialize(json, info :> JsonTypeInfo) with
        | null -> failtest "The reader returned null"
        | value -> JsonSerializer.Serialize(value, info :> JsonTypeInfo)

let private position = """{"line":1,"character":2}"""
let private range = $$"""{"start":{{position}},"end":{{position}}}"""
let private location = $$"""{"uri":"file:///a.elm","range":{{range}}}"""
let private digest = String('a', 64)

let private payload =
    $$"""{"codec":"ion-binary","layout":"single-document","decodedByteLength":0,"sha256":"{{digest}}","content":""}"""

let private tuple =
    """{"operation":"morphir.frontend.compileModel","profile":"morphir-model","codec":"ion-binary","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"}"""

let private limits =
    """{"maxEncodedBytes":8,"maxDecodedBytes":4,"maxDepth":2,"maxNodes":3}"""

let private support =
    $$"""{"tuples":[{{tuple}}],"limits":{{limits}},"guarantees":["urn:g"]}"""

let private extensionInfo =
    """{"id":"x","name":"X","version":"1.0.0","description":"d","types":["validator"],"author":"a","homepage":"h","license":"l","min_sdk_version":"0.4.0"}"""

let private frontend =
    """{"languages":[{"id":"elm","fileExtensions":[".elm"]}],"irVersions":["3"],"compile":true,"incremental":false,"fragments":false,"multiDocument":true}"""

let private backend = """{"targets":["t"],"irVersions":["3"],"generate":true}"""

let private workspace = """{"protocolVersions":["0.1.0"],"discover":true}"""

let private capabilities =
    $$"""{"frontend":{{frontend}},"backend":{{backend}},"workspace":{{workspace}},"modelTransport":{{support}},"streaming":true,"incremental":true,"cancellation":true,"progress":true}"""

let private evidence =
    $$"""{"guarantee":"urn:g","modelId":"m","modelRevision":"r","contextDigest":"{{digest}}","scope":"s","producer":"p","evidence":{{payload}}}"""

let private morphirModel =
    $$"""{"envelopeVersion":"0.1.0-draft.1","modelId":"m","revision":"r","irVersion":"4.0.0","profile":"p","payload":{{payload}}}"""

let private sourceDocument =
    """{"uri":"Pricing.fs","languageId":"fsharp","version":1,"text":"module Pricing"}"""

let private sourceSet = $$"""{"root":"src","documents":[{{sourceDocument}}]}"""

let private compilePackage = """{"name":"acme/demo","exposedModules":["Pricing"]}"""

let private compileOptions = """{"typesOnly":false,"irVersion":"4.0.0"}"""

let private compileModelDependency =
    $$"""{"packageName":"acme/base","model":{{morphirModel}}}"""

let private generateTuple =
    """{"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"}"""

let private artifact = """{"path":"Car/Rental/Inventory.fs","content":"module X","binary":false}"""

let private generateOptions = """{"optimize":false}"""

let private record typeName info sample =
    { TypeName = typeName
      SnakeCase = false
      RoundTrip = codec info
      Sample = sample
      Defaulted = []
      Flattened = [] }

let private checkedTypes =
    [ record "describe-params" MepJson.Default.DescribeParams """{"protocolVersions":["0.2.0-draft.1"]}"""
      { record "extension-info" MepJson.Default.ExtensionInfo extensionInfo with
          SnakeCase = true }
      { record "peer-info" MepJson.Default.PeerInfo """{"kind":"cli","name":"n","version":"1"}""" with
          Defaulted = [ "kind" ] }
      { record "claims-requirements" MepJson.Default.ClaimsRequirements """{"host":[">=0.4.0"]}""" with
          Defaulted = [ "host" ] }
      { record
            "capability-claim-set"
            MepJson.Default.CapabilityClaimSet
            $$"""{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.1"],"extension":{{extensionInfo}},"capabilities":{"validator":{ } },"requires":{"host":[">=0.4.0"]},"critical":["requires.host"]}""" with
          Defaulted = [ "critical" ] }
      record
          "initialize-params"
          MepJson.Default.InitializeParams
          $$"""{"protocolVersions":["0.2.0-draft.1"],"host":{"kind":"cli","name":"n","version":"1"},"modelTransport":{{support}}}"""
      record
          "initialize-result"
          MepJson.Default.InitializeResult
          $$"""{"protocolVersion":"0.2.0-draft.1","extension":{{extensionInfo}},"capabilities":{{capabilities}}}"""
      { record "extension-capabilities" MepJson.Default.ExtensionCapabilities capabilities with
          Defaulted = [ "streaming"; "incremental"; "cancellation"; "progress" ]
          Flattened = [ "extra" ] }
      { record "frontend-capability" MepJson.Default.FrontendCapability frontend with
          Defaulted = [ "multiDocument" ] }
      record "backend-capability" MepJson.Default.BackendCapability backend
      record "workspace-capability" MepJson.Default.WorkspaceCapability workspace
      record "language-capability" MepJson.Default.LanguageCapability """{"id":"elm","fileExtensions":[".elm"]}"""
      record "cancel-params" MepJson.Default.CancelParams """{"id":"a"}"""
      record
          "progress-params"
          MepJson.Default.ProgressParams
          """{"requestId":1,"kind":"report","message":"m","percentage":50}"""
      record "rpc-error" MepJson.Default.RpcError """{"code":-32000,"message":"m","data":{"a":1}}"""
      { record
            "diagnostic"
            MepJson.Default.Diagnostic
            $$"""{"severity":"error","code":"c","message":"m","location":{{location}},"related":[{"location":{{location}},"message":"r"}],"data":[1]}""" with
          Defaulted = [ "related" ] }
      record "source-location" MepJson.Default.SourceLocation location
      record "source-range" MepJson.Default.SourceRange range
      record "source-position" MepJson.Default.SourcePosition position
      record
          "related-information"
          MepJson.Default.RelatedInformation
          $$"""{"location":{{location}},"message":"r"}"""
      record "model-transport-tuple" MepJson.Default.ModelTransportTuple tuple
      record "model-transport-limits" MepJson.Default.ModelTransportLimits limits
      { record "model-transport-support" MepJson.Default.ModelTransportSupport support with
          Defaulted = [ "guarantees" ] }
      { record
            "morphir-model"
            MepJson.Default.MorphirModel
            $$"""{"envelopeVersion":"0.1.0-draft.1","modelId":"m","revision":"r","irVersion":"4.0.0","profile":"p","payload":{{payload}},"evidence":[{{evidence}}],"critical":["payload"]}""" with
          Defaulted = [ "evidence"; "critical" ] }
      record "model-payload" MepJson.Default.ModelPayload payload
      record "model-guarantee-evidence" MepJson.Default.ModelGuaranteeEvidence evidence
      record
          "model-guarantee-requirement"
          MepJson.Default.ModelGuaranteeRequirement
          """{"guarantee":"urn:g","scope":"s"}"""
      record "source-document" MepJson.Default.SourceDocument sourceDocument
      record "source-set" MepJson.Default.SourceSet sourceSet
      record "compile-package" MepJson.Default.CompilePackage compilePackage
      { record "compile-options" MepJson.Default.CompileOptions compileOptions with
          Flattened = [ "extra" ] }
      record "compile-model-dependency" MepJson.Default.CompileModelDependency compileModelDependency
      { record
            "compile-model-request"
            MepJson.Default.CompileModelRequest
            $$"""{"languageId":"fsharp","sources":{{sourceSet}},"package":{{compilePackage}},"dependencies":[{{compileModelDependency}}],"options":{{compileOptions}},"contextDigest":"{{digest}}","output":{{tuple}}}""" with
          Defaulted = [ "dependencies" ] }
      { record
            "generate-model-request"
            MepJson.Default.GenerateModelRequest
            $$"""{"model":{{morphirModel}},"input":{{generateTuple}},"target":"fsharp","contextDigest":"{{digest}}","requiredGuarantees":[{"guarantee":"urn:g","scope":"s"}],"options":{{generateOptions}}}""" with
          Defaulted = [ "requiredGuarantees"; "options" ] }
      { record
            "generate-result"
            MepJson.Default.GenerateResult
            $$"""{"success":true,"artifacts":[{{artifact}}],"diagnostics":[{"severity":"warning","message":"m"}]}""" with
          Defaulted = [ "artifacts"; "diagnostics" ] }
      { record "artifact" MepJson.Default.Artifact artifact with
          Defaulted = [ "binary" ] } ]

let private names (json: string) =
    match JsonNode.Parse json with
    | :? JsonObject as value -> value |> Seq.map _.Key |> Set.ofSeq
    | _ -> failtest "Expected an object"

let private without (name: string) (json: string) =
    match JsonNode.Parse json with
    | :? JsonObject as value ->
        value.Remove name |> ignore
        value.ToJsonString()
    | _ -> failtest "Expected an object"

/// True when the generated codec writes exactly the pinned member names.
let private matchesNames (root: JsonElement) (item: Checked) =
    let expected =
        pinnedFields root item.TypeName item.SnakeCase
        |> List.map _.Name
        |> List.filter (fun name -> not (List.contains name item.Flattened))
        |> Set.ofList

    names (item.RoundTrip item.Sample) = expected

[<Tests>]
let tests =
    testList
        "pinned MEP contract"
        [ for item in checkedTypes do
              testCase $"{item.TypeName} members and requiredness agree with the pin"
              <| fun _ ->
                  use contract = JsonDocument.Parse(File.ReadAllText pin)
                  let written = item.RoundTrip item.Sample
                  Expect.isTrue (matchesNames contract.RootElement item) $"Wire names of {item.TypeName}: {written}"

                  for field in pinnedFields contract.RootElement item.TypeName item.SnakeCase do
                      if not (List.contains field.Name item.Flattened) then
                          let reduced = without field.Name item.Sample

                          if field.Optional || List.contains field.Name item.Defaulted then
                              let again = item.RoundTrip reduced

                              if field.Optional then
                                  Expect.isFalse
                                      ((names again).Contains field.Name)
                                      $"An absent optional {field.Name} is not written"
                          else
                              Expect.throwsT<JsonException>
                                  (fun () -> item.RoundTrip reduced |> ignore)
                                  $"Omitting the required member {field.Name} is refused"

          testCase "renaming a member in a copy of the pin is detected"
          <| fun _ ->
              for original, renamed in
                  [ "\"protocol-versions\"", "\"renamed-versions\""
                    "\"min-sdk-version\"", "\"min-sdk\""
                    "\"decoded-byte-length\"", "\"decoded-length\"" ] do
                  let changed = File.ReadAllText(pin).Replace(original, renamed)
                  use contract = JsonDocument.Parse changed

                  Expect.isFalse
                      (checkedTypes |> List.forall (matchesNames contract.RootElement))
                      $"A pin drift of {original} cannot pass the contract check"

          testCaseAsync "a framed describe payload reaches the generated codec"
          <| async {
              let sample = { ProtocolVersions = [| ProtocolVersion.Create "0.2.0-draft.1" |] }
              let json = JsonSerializer.Serialize(sample, MepJson.Default.DescribeParams)
              let body = System.Text.Encoding.UTF8.GetBytes json
              use stream = new MemoryStream()

              do!
                  ContentLengthFraming.WriteAsync(stream, body.AsMemory(), Threading.CancellationToken.None)
                  |> Async.AwaitTask

              stream.Position <- 0L

              let! frame =
                  ContentLengthFraming.ReadAsync(stream, Threading.CancellationToken.None)
                  |> Async.AwaitTask

              match frame with
              | FrameRead.Message bytes ->
                  let actual =
                      JsonSerializer.Deserialize(bytes.AsSpan(), MepJson.Default.DescribeParams)

                  Expect.equal actual sample "The framing boundary preserves the protocol params object"
              | _ -> failtest "Expected the describe frame"
          } ]
