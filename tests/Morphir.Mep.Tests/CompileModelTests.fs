// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The frontend parts of the MEP core (add-fsharp-compile group 7): the compileModel request and result, the
/// reserved options, the context digest, frontend-sized frames and the negotiation of compileModel tuples.
module Morphir.Mep.Tests.CompileModelTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Mep
open Morphir.Mep.Tests.Transcripts
open Morphir.Mep.Tests.ModelTransportTests

let private cases =
    JsonNode.Parse(File.ReadAllText(fixture "model-transport.cases.json")) |> nonNull

let private allCases = (cases["cases"] |> nonNull).AsArray() |> Seq.map nonNull |> List.ofSeq

let private case (id: string) =
    allCases |> List.find (fun item -> textAt [ "id" ] item = id)

let private element (node: JsonNode) =
    use document = JsonDocument.Parse(node.ToJsonString())
    document.RootElement.Clone()

let private same (left: JsonNode) (right: JsonNode) = JsonNode.DeepEquals(left, right)

let private nodeOf (json: string) = JsonNode.Parse json |> nonNull

let private requestOf (item: JsonNode) =
    decode MepJson.Default.CompileModelRequest (at [ "request"; "params" ] item)

let private agreedOf (item: JsonNode) =
    let support (node: JsonNode) =
        decode MepJson.Default.ModelTransportSupport (at [ "modelTransport" ] node)

    Some(ModelTransport.select (support (at [ "hostOffer" ] item)) (support (at [ "guestSupport" ] item)))

let private compileRequests =
    allCases
    |> List.filter (fun item ->
        match item["request"] with
        | null -> false
        | request -> methodOf request = Some "morphir.frontend.compileModel")

let private jsonTuple (version: string) =
    { Operation = ModelOperation.CompileModel
      Profile = "morphir-ir"
      Codec = ModelCodec.MorphirJson
      EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.1"
      IrVersion = SemanticVersion.Create version }

let private limits =
    { MaxEncodedBytes = 1000
      MaxDecodedBytes = 1000
      MaxDepth = 64
      MaxNodes = 1000 }

let private supportOf (tuples: ModelTransportTuple list) =
    { Tuples = Array.ofList tuples
      Limits = limits
      Guarantees = [||] }

let private sample () =
    let request = requestOf (case "model-0028")

    { request with
        LanguageId = "fsharp"
        Sources =
            { Root = Some "src"
              Documents =
                [| { Uri = "Rates.fs"
                     LanguageId = "fsharp"
                     Version = 1UL
                     Text = "module Rates\nlet daily = 40" }
                   { Uri = "Pricing.fs"
                     LanguageId = "fsharp"
                     Version = 3UL
                     Text = "module Pricing\nlet total days = days * Rates.daily" } |] }
        Package =
            { Name = "acme/demo"
              ExposedModules = Some [| "Pricing" |] }
        Output = jsonTuple "4.0.0" }

/// The documented context object, written here independently of the implementation.
let private documentedContext (request: CompileModelRequest) =
    let sha (text: string) =
        Convert.ToHexStringLower(Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes text))

    let context = JsonObject()
    context["languageId"] <- JsonValue.Create request.LanguageId
    let sourceRoot: JsonNode | null =
        match request.Sources.Root with
        | Some root -> JsonValue.Create root
        | None -> null

    context["sourceRoot"] <- sourceRoot

    let documents = JsonArray()

    for document in request.Sources.Documents do
        let item = JsonObject()
        item["uri"] <- JsonValue.Create document.Uri
        item["languageId"] <- JsonValue.Create document.LanguageId
        item["textSha256"] <- JsonValue.Create(sha document.Text)
        documents.Add item

    context["documents"] <- documents
    context["package"] <- JsonValue.Create request.Package.Name

    context["exposedModules"] <-
        match request.Package.ExposedModules with
        | Some modules ->
            let array = JsonArray()

            for name in modules do
                array.Add(JsonValue.Create name)

            array :> JsonNode | null
        | None -> null

    context["dependencies"] <- JsonArray()
    context["options"] <- JsonSerializer.SerializeToNode(request.Options, MepJson.Default.CompileOptions)
    context["output"] <- JsonSerializer.SerializeToNode(request.Output, MepJson.Default.ModelTransportTuple)

    let canonical = CompileModel.canonicalJson (element context)
    Sha256Digest.Compute(Encoding.UTF8.GetBytes canonical)

[<Tests>]
let tests =
    testList
        "compileModel (frontend group 7)"
        [ testCase "Every pinned compileModel request reads and writes again without a change"
          <| fun _ ->
              Expect.isNonEmpty compileRequests "The pin has compileModel requests"

              for item in compileRequests do
                  let parameters = at [ "request"; "params" ] item
                  let written = JsonSerializer.SerializeToNode(requestOf item, MepJson.Default.CompileModelRequest)
                  let id = textAt [ "id" ] item
                  Expect.isTrue (same parameters (written |> nonNull)) $"{id}: {written}"

          testCase "Extra options stay beside the named members, and reserved names are refused"
          <| fun _ ->
              let options =
                  decode MepJson.Default.CompileOptions (nodeOf """{"typesOnly":false,"irVersion":"4.0.0","defines":["DEBUG"]}""")

              Expect.isTrue
                  (same
                      (JsonSerializer.SerializeToNode(options, MepJson.Default.CompileOptions) |> nonNull)
                      (nodeOf """{"typesOnly":false,"irVersion":"4.0.0","defines":["DEBUG"]}"""))
                  "The extra member is written beside the named members"

              Expect.equal (CompileModel.checkOptions options) (Ok options) "defines is not reserved"

              for name in [ "sourceRoot"; "sourceRootUri" ] do
                  let legacy =
                      decode
                          MepJson.Default.CompileOptions
                          (nodeOf $$"""{"typesOnly":false,"irVersion":"4.0.0","{{name}}":"src"}""")

                  Expect.equal
                      (CompileModel.checkOptions legacy |> Result.mapError _.Code)
                      (Error "reserved_option")
                      $"The legacy key {name} is refused; the source root belongs in SourceSet.root"

          testCase "A request needs an agreed compileModel tuple with the options IR version"
          <| fun _ ->
              let accepted = case "model-0028"

              Expect.equal
                  (CompileModel.checkRequest (agreedOf accepted) (requestOf accepted) |> Result.map _.ContextDigest)
                  (Ok (requestOf accepted).ContextDigest)
                  "model-0028 is accepted"

              let mismatch = case "model-0031"

              Expect.equal
                  (CompileModel.checkRequest (agreedOf mismatch) (requestOf mismatch) |> Result.mapError _.Code)
                  (Error(textAt [ "expected"; "code" ] mismatch))
                  "model-0031: the options name another IR version than the output tuple"

              Expect.equal
                  (CompileModel.checkRequest None (requestOf accepted) |> Result.mapError _.Code)
                  (Error "unsupported_tuple")
                  "A session without model transport admits no compileModel tuple"

          testCase "Pinned compile results read as the result sum, and a written failure has no model member"
          <| fun _ ->
              let success = at [ "result" ] (case "model-0028")

              match CompileModel.readResult (element success) with
              | Ok(CompileModelResult.Compiled(model, diagnostics, modules) as result) ->
                  Expect.equal model.IrVersion (SemanticVersion.Create "4.0.0") "The model"
                  // The MorphirModel codec leaves out an empty critical list, which the pin writes as [].
                  Expect.equal
                      (CompileModel.readResult (CompileModel.writeResult result))
                      (Ok result)
                      "The written result reads back as the same value"

                  ignore (diagnostics, modules)
              | other -> failtest $"model-0028 is a success: {other}"

              match CompileModel.readResult (element (at [ "result" ] (case "model-0029"))) with
              | Ok(CompileModelResult.Failed diagnostics as result) ->
                  Expect.equal diagnostics.Count 1 "The failure diagnostic"
                  let written = CompileModel.writeResult result
                  Expect.isFalse (fst (written.TryGetProperty "model")) "A failure has no model member"
                  Expect.equal (written.GetProperty("success").GetBoolean()) false "success is false"
              | other -> failtest $"model-0029 is a failure: {other}"

              for id in [ "model-0018"; "model-0019"; "model-0020" ] do
                  let item = case id

                  Expect.equal
                      (CompileModel.readResult (element (at [ "result" ] item)) |> Result.mapError _.Code)
                      (Error(textAt [ "expected"; "code" ] item))
                      id

              for malformed in [ """{"success":"true","diagnostics":[]}"""; """{"diagnostics":[]}"""; "[]" ] do
                  Expect.equal
                      (CompileModel.readResult (element (nodeOf malformed)) |> Result.mapError _.Code)
                      (Error "malformed_result")
                      malformed

              match CompileModel.readResult (element (nodeOf """{"success":false}""")) with
              | Ok(CompileModelResult.Failed diagnostics) -> Expect.isEmpty diagnostics "Absent diagnostics are empty"
              | other -> failtest $"An absent diagnostics member is read as empty: {other}"

          testCase "canonicalJson follows the RFC 8785 example"
          <| fun _ ->
              // RFC 8785, section 3.2.2: the sample input and its canonical form.
              let input =
                  "{\"numbers\": [333333333.33333329, 1E30, 4.50, 2e-3, 0.000000000000000000000000001], "
                  + "\"string\": \"\\u20ac$\\u000F\\u000aA'\\u0042\\u0022\\u005c\\\\\\\"\\/\", "
                  + "\"literals\": [null, true, false]}"

              let expected =
                  "{\"literals\":[null,true,false],\"numbers\":[333333333.3333333,1e+30,4.5,0.002,1e-27],"
                  + "\"string\":\"\u20ac$\\u000f\\nA'B\\\"\\\\\\\\\\\"/\"}"

              use document = JsonDocument.Parse input
              Expect.equal (CompileModel.canonicalJson document.RootElement) expected "RFC 8785 sample"

              for number, text in
                  [ "0", "0"
                    "-0", "0"
                    "1e21", "1e+21"
                    "1e20", "100000000000000000000"
                    "0.000001", "0.000001"
                    "1e-7", "1e-7"
                    "-12.5", "-12.5"
                    "9007199254740993", "9007199254740992" ] do
                  use value = JsonDocument.Parse number
                  Expect.equal (CompileModel.canonicalJson value.RootElement) text number

              use sorted = JsonDocument.Parse "{\"b\":1,\"\u20ac\":2,\"a\":3,\"\\r\":4}"
              Expect.equal
                  (CompileModel.canonicalJson sorted.RootElement)
                  "{\"\\r\":4,\"a\":3,\"b\":1,\"\u20ac\":2}"
                  "Members are sorted by UTF-16 code units"

          testCase "The context digest is the documented construction, and binds the context, not the versions"
          <| fun _ ->
              let request = sample ()
              let digest = CompileModel.contextDigest request
              Expect.isTrue (Text.RegularExpressions.Regex.IsMatch(digest.Value, "^[0-9a-f]{64}$")) "64 lowercase digits"
              Expect.equal digest (documentedContext request) "The documented construction"

              let changed text =
                  { request with
                      Sources =
                          { request.Sources with
                              Documents = [| { request.Sources.Documents[0] with Text = text }; request.Sources.Documents[1] |] } }

              Expect.notEqual (CompileModel.contextDigest (changed "module Rates\nlet daily = 41")) digest "A text change"

              let reordered =
                  { request with
                      Sources =
                          { request.Sources with
                              Documents = [| request.Sources.Documents[1]; request.Sources.Documents[0] |] } }

              Expect.notEqual (CompileModel.contextDigest reordered) digest "The document order"

              let otherVersion =
                  { request with
                      Sources =
                          { request.Sources with
                              Documents = [| { request.Sources.Documents[0] with Version = 9UL }; request.Sources.Documents[1] |] } }

              Expect.equal (CompileModel.contextDigest otherVersion) digest "Document versions do not take part"

              Expect.equal
                  (CompileModel.contextDigest { request with ContextDigest = Sha256Digest.Create(String('0', 64)) })
                  digest
                  "The contextDigest member does not take part"

              let classic =
                  { request with
                      Output = jsonTuple "3.0.0"
                      Options = { request.Options with IrVersion = SemanticVersion.Create "3.0.0" } }

              Expect.notEqual (CompileModel.contextDigest classic) digest "The output tuple"

              let unexposed =
                  { request with
                      Package = { request.Package with ExposedModules = None } }

              Expect.notEqual (CompileModel.contextDigest unexposed) digest "An absent list differs from a list"

          testCase "A compileModel frame round-trips at the 64 MiB body limit, and one byte more is refused"
          <| fun _ ->
              let frame (size: int) =
                  let prefix =
                      "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"morphir.frontend.compileModel\",\"params\":{\"languageId\":\"fsharp\",\"sources\":{\"documents\":[{\"uri\":\"Big.fs\",\"languageId\":\"fsharp\",\"version\":1,\"text\":\""

                  let suffix = "\"}]}}}"
                  let filler = size - Encoding.UTF8.GetByteCount prefix - Encoding.UTF8.GetByteCount suffix
                  Encoding.UTF8.GetBytes(prefix + String('a', filler) + suffix)

              let body = frame ContentLengthFraming.MaxBodyBytes
              Expect.equal body.Length ContentLengthFraming.MaxBodyBytes "The body is exactly at the limit"
              use stream = new MemoryStream()
              ContentLengthFraming.WriteAsync(stream, ReadOnlyMemory body, CancellationToken.None).Wait()
              stream.Position <- 0L

              match ContentLengthFraming.ReadAsync(stream, CancellationToken.None).Result with
              | FrameRead.Message read ->
                  Expect.isTrue (read.AsSpan().SequenceEqual(ReadOnlySpan body)) "The body reads back"
                  use document = JsonDocument.Parse(read)
                  Expect.equal (document.RootElement.GetProperty("method").GetString()) "morphir.frontend.compileModel" "JSON"
              | other -> failtest $"Expected a message: {other}"

              let over = ContentLengthFraming.MaxBodyBytes + 1

              Expect.throwsT<ArgumentException>
                  (fun () ->
                      ContentLengthFraming.WriteAsync(new MemoryStream(), ReadOnlyMemory(Array.zeroCreate<byte> over), CancellationToken.None).GetAwaiter().GetResult())
                  "The writer refuses a body over the limit"

              use oversized =
                  new MemoryStream(Encoding.ASCII.GetBytes $"Content-Length: {over}\r\n\r\n{{}}")

              match ContentLengthFraming.ReadAsync(oversized, CancellationToken.None).Result with
              | FrameRead.Refused FramingFailure.BodyTooLarge -> ()
              | other -> failtest $"Expected BodyTooLarge before the body is read: {other}"

          testCase "An Ion-only compileModel offer agrees no tuple, so the host sends no request"
          <| fun _ ->
              let ion =
                  { jsonTuple "4.0.0" with
                      Profile = "morphir-model"
                      Codec = ModelCodec.IonBinary }

              let guest = supportOf [ jsonTuple "3.0.0"; jsonTuple "4.0.0" ]
              let agreed = ModelTransport.select (supportOf [ ion ]) guest
              Expect.isEmpty agreed.Tuples "No common tuple"

              match ModelTransport.firstTuple (Some agreed) ModelOperation.CompileModel with
              | Error unavailable -> Expect.equal unavailable.Code "extension.tuple_unavailable" "The code"
              | Ok tuple -> failtest $"No tuple may be selected: {tuple}"

          testCase "The agreement keeps the host preference order for compileModel tuples"
          <| fun _ ->
              let offer = supportOf [ jsonTuple "4.0.0"; jsonTuple "3.0.0" ]
              let guest = supportOf [ jsonTuple "3.0.0"; jsonTuple "4.0.0" ]
              let agreed = ModelTransport.select offer guest

              Expect.sequenceEqual agreed.Tuples [ jsonTuple "4.0.0"; jsonTuple "3.0.0" ] "Host order"
              Expect.isOk (ModelTransport.verifySelection offer agreed) "The host verifies the selection"

              Expect.equal
                  (ModelTransport.firstTuple (Some agreed) ModelOperation.CompileModel)
                  (Ok(jsonTuple "4.0.0"))
                  "The host compiles at its first preference" ]
