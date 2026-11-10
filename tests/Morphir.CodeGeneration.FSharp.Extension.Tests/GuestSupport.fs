// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Helpers that drive the F# backend guest through its dispatcher with JSON-RPC text, as a host would, and that
/// build generateModel requests from the checked-in IR fixtures.
module Morphir.CodeGeneration.FSharp.Extension.Tests.GuestSupport

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.CodeGeneration.FSharp.Extension
open Morphir.Mep

/// A checked-in IR fixture under tests/Fixtures/MorphirIR.
let irFixture name =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "MorphirIR", name)

/// The fixture for each claimed IR version: classic formatVersion 3 for 3.0.0, and formatVersion 4 for 4.0.0.
let fixtureFor (irVersion: string) =
    match irVersion with
    | "3.0.0" -> irFixture "daily-rate-v3.json"
    | "4.0.0" -> irFixture "daily-rate-v4.json"
    | other -> failtest $"No fixture for IR {other}"

/// Sends one message to the guest and returns its reply, or None for a notification.
let dispatch (guest: Guest) (message: string) : JsonNode option =
    guest.Dispatcher.DispatchAsync(ReadOnlyMemory(Encoding.UTF8.GetBytes message), CancellationToken.None).Result
    |> Option.ofObj
    |> Option.map (fun bytes -> JsonNode.Parse(bytes) |> nonNull)

let hostLimits =
    """{"maxEncodedBytes":67108864,"maxDecodedBytes":50331648,"maxDepth":4096,"maxNodes":50000000}"""

/// A model-transport tuple as JSON text.
let tupleText (operation: string) (profile: string) (codec: string) (version: string) =
    $$"""{"operation":"{{operation}}","profile":"{{profile}}","codec":"{{codec}}","envelopeVersion":"0.1.0-draft.1","irVersion":"{{version}}"}"""

/// A JSON generateModel tuple as JSON text.
let jsonTuple (version: string) =
    tupleText "morphir.backend.generateModel" "morphir-ir" "morphir-json" version

/// An Ion generateModel tuple as JSON text. The guest does not claim it.
let ionTuple (version: string) =
    tupleText "morphir.backend.generateModel" "morphir-model" "ion-binary" version

/// A JSON compileModel tuple as JSON text. The guest does not claim it.
let compileTuple (version: string) =
    tupleText "morphir.frontend.compileModel" "morphir-ir" "morphir-json" version

/// An initialize request that offers the given tuples, in that order.
let initialize (tuples: string list) =
    let offered = String.Join(",", tuples)

    """{"jsonrpc":"2.0","id":0,"method":"morphir.initialize","params":{"protocolVersions":["0.2.0-draft.1"],"host":{"kind":"cli","name":"morphir-dotnet-tests","version":"1.0.0"},"modelTransport":{"tuples":["""
    + offered
    + """],"limits":"""
    + hostLimits
    + "}}}"

/// A guest after initialize and initialized with JSON generateModel tuples for 4.0.0 and 3.0.0.
let initialized () =
    let guest = FSharpGuest.registration ()
    let reply = dispatch guest (initialize [ jsonTuple "4.0.0"; jsonTuple "3.0.0" ]) |> Option.get
    Expect.isNull reply["error"] $"initialize succeeds: {reply}"
    dispatch guest """{"jsonrpc":"2.0","method":"morphir.initialized","params":{}}""" |> ignore
    guest

/// A JSON generateModel tuple.
let generateTuple (version: string) : ModelTransportTuple =
    { Operation = ModelOperation.GenerateModel
      Profile = "morphir-ir"
      Codec = ModelCodec.MorphirJson
      EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.1"
      IrVersion = SemanticVersion.Create version }

/// An envelope of bytes in a tuple.
let envelope (tuple: ModelTransportTuple) (bytes: byte array) =
    match ModelTransport.createDerivedEnvelope tuple "car/rental" (ReadOnlyMemory bytes) with
    | Ok model -> model
    | Error failure -> failtest $"Envelope creation failed: {failure}"

/// A generateModel request for bytes in a tuple, with options.
let requestOf (tuple: ModelTransportTuple) (bytes: byte array) (options: (string * string) list) : GenerateModelRequest =
    let values = Dictionary<string, JsonElement>()

    for key, json in options do
        use document = JsonDocument.Parse json
        values[key] <- document.RootElement.Clone()

    { Model = envelope tuple bytes
      Input = tuple
      Target = "fsharp"
      ContextDigest = Sha256Digest.Create(String('1', 64))
      RequiredGuarantees = [||]
      Options = values }

/// A generateModel request for the fixture of an IR version, in its JSON tuple.
let generateRequest (version: string) =
    requestOf (generateTuple version) (File.ReadAllBytes(fixtureFor version)) []

/// The JSON-RPC text of a generateModel request.
let generateMessage (id: int) (request: GenerateModelRequest) =
    let parameters = JsonSerializer.Serialize(request, MepJson.Default.GenerateModelRequest)
    $$"""{"jsonrpc":"2.0","id":{{id}},"method":"morphir.backend.generateModel","params":{{parameters}}}"""

/// The error code of a reply, or None for a result.
let codeOf (reply: JsonNode) =
    match reply["error"] with
    | null -> None
    | error -> Some((error["code"] |> nonNull).GetValue<int>())

/// The error message of a reply.
let messageOf (reply: JsonNode) =
    ((reply["error"] |> nonNull)["message"] |> nonNull).GetValue<string>()

/// The result of a generateModel reply, read with the generated codec.
let resultOf (reply: JsonNode) : GenerateResult =
    match reply["result"] with
    | null -> failtest $"Expected a result: {reply}"
    | result ->
        match JsonSerializer.Deserialize(result.ToJsonString(), MepJson.Default.GenerateResult) with
        | null -> failtest "The result is null"
        | value -> value

/// The artifacts of a result as a map from relative path to text. Every artifact is text.
let filesOf (result: GenerateResult) =
    for artifact in result.Artifacts do
        Expect.isFalse artifact.Binary $"{artifact.Path} is text"

    result.Artifacts |> Seq.map (fun artifact -> artifact.Path, artifact.Content) |> Map.ofSeq

let private v4Model (values: (string * string) list) =
    let members =
        [ for name, body in values ->
              JsonSerializer.Serialize name
              + """:{"access":"Public","ExpressionBody":{"inputTypes":{},"outputType":"morphir/SDK:basics#int","body":"""
              + body
              + "}}" ]

    let text =
        """{"formatVersion":4,"distribution":{"Library":{"packageName":"car/rental","dependencies":{},"def":{"modules":{"inventory":{"access":"Public","types":{},"values":{"""
        + String.Join(",", members)
        + "}}}}}}}"

    Encoding.UTF8.GetBytes text

let private literal (value: int) =
    """{"Literal":{"WholeNumberLiteral":""" + string value + "}}"

/// A name word of letters only for a number (1 is a, 27 is aa): the generator refuses words that start with a digit.
let private letters (number: int) =
    let rec go (n: int) (acc: string) =
        if n <= 0 then acc else go ((n - 1) / 26) (string (char (int 'a' + (n - 1) % 26)) + acc)

    go number ""

/// basics.add of two IR v4 values.
let private add (left: string) (right: string) =
    """{"Apply":{"function":{"Apply":{"function":{"Reference":"morphir/SDK:basics#add"},"argument":"""
    + left
    + """}},"argument":"""
    + right
    + "}}"

/// An IR v4 library with one module of <c>count</c> values, each a sum of five literals: generation takes long.
let largeModel (count: int) =
    v4Model
        [ for index in 1..count ->
              $"rate-{letters index}", List.fold (fun body step -> add body (literal (index + step))) (literal index) [ 1..4 ] ]

/// An IR v4 library with one value nested <c>levels</c> additions deep: about two JSON levels for each addition.
let deepModel (levels: int) =
    v4Model [ "deep", List.fold (fun body level -> add (literal level) body) (literal 0) [ 1..levels ] ]

/// The deepest JSON nesting of a document, counting the root as 1.
let jsonDepth (bytes: byte array) =
    let mutable reader = Utf8JsonReader(ReadOnlySpan bytes, JsonReaderOptions(MaxDepth = Int32.MaxValue))
    let mutable deepest = 0

    while reader.Read() do
        deepest <- max deepest (reader.CurrentDepth + 1)

    deepest
