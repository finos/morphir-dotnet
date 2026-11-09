// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.ContractTests

open System
open System.IO
open System.Text.Json
open Expecto
open Morphir.Mep

let private pin =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mep", "mep.ir.json")

let private describeFields (root: JsonElement) =
    root
        .GetProperty("distribution")
        .GetProperty("Library")
        .GetProperty("def")
        .GetProperty("modules")
        .GetProperty("mep")
        .GetProperty("Public")
        .GetProperty("types")
        .GetProperty("describe-params")
        .GetProperty("Public")
        .GetProperty("CustomTypeDefinition")
        .GetProperty("constructors")
        .GetProperty("describe-params")
        .EnumerateArray()
    |> Seq.map (fun argument ->
        let words = argument[0].GetString() |> nonNull |> (fun name -> name.Split '-')

        let name =
            words[0]
            + (words
               |> Array.skip 1
               |> Array.map (fun word -> string (Char.ToUpperInvariant word[0]) + word.Substring(1))
               |> String.concat "")

        let reference =
            argument[1].GetProperty("Reference").GetProperty("fqname").GetString()

        name, reference <> "morphir/SDK:maybe#maybe")
    |> List.ofSeq

let private sample =
    { ProtocolVersions = [| ProtocolVersion.Create "0.2.0-draft.1" |] }

let private matchesDescribeNames (root: JsonElement) =
    use serialized =
        JsonDocument.Parse(JsonSerializer.Serialize(sample, MepJson.Default.DescribeParams))

    let written =
        serialized.RootElement.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq

    let expected = describeFields root |> List.map fst |> Set.ofList
    written = expected

[<Tests>]
let tests =
    testList
        "pinned MEP contract"
        [ testCase "describe members and requiredness agree with the pinned constructor"
          <| fun _ ->
              use contract = JsonDocument.Parse(File.ReadAllText pin)
              Expect.isTrue (matchesDescribeNames contract.RootElement) "Generated wire names match the pin"

              for name, required in describeFields contract.RootElement do
                  Expect.isTrue required $"The pin requires {name}"

                  Expect.throwsT<JsonException>
                      (fun () -> JsonSerializer.Deserialize("{}", MepJson.Default.DescribeParams) |> ignore)
                      $"Omitting the contract's required member {name} is refused"
          testCase "renaming a member in a copy of the pin is detected"
          <| fun _ ->
              let changed =
                  File.ReadAllText(pin).Replace("\"protocol-versions\"", "\"renamed-versions\"")

              use contract = JsonDocument.Parse changed
              Expect.isFalse (matchesDescribeNames contract.RootElement) "A pin drift cannot pass the contract check"
          testCaseAsync "a framed describe payload reaches the generated codec"
          <| async {
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
