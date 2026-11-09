// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.ProposalTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization.Metadata
open System.Text.RegularExpressions
open Expecto
open Morphir.Mep
open Morphir.Mep.Tests.Proposal

let private proposal =
    Path.Combine(
        __SOURCE_DIRECTORY__,
        "..",
        "..",
        "docs",
        "content",
        "contributing",
        "design",
        "proposals",
        "mep-validate-model.md"
    )

/// The jsonrpc messages of the proposal's transcripts.
let private messages () =
    Regex.Matches(File.ReadAllText proposal, "```jsonrpc (?:host -> extension|extension -> host)\n(.*?)\n```", RegexOptions.Singleline)
    |> Seq.map (fun m -> JsonNode.Parse(m.Groups[1].Value) |> nonNull)
    |> List.ofSeq

let private read<'T when 'T: not null> (info: JsonTypeInfo<'T>) (node: JsonNode) : 'T =
    match JsonSerializer.Deserialize(node.ToJsonString(), info :> JsonTypeInfo) with
    | :? 'T as value -> value
    | _ -> failtest $"Not a {typeof<'T>.Name}"

/// Reads and writes with a codec, and checks that the written JSON has the same members, apart from the members
/// that the contract reads as empty when absent.
let private check<'T when 'T: not null> (info: JsonTypeInfo<'T>) (node: JsonNode) =
    let written = JsonNode.Parse(JsonSerializer.Serialize(read info node, info)) |> nonNull

    let rec normal (value: JsonNode | null) : JsonNode | null =
        match value with
        | :? JsonObject as item ->
            let copy = JsonObject()

            for KeyValue(name, child) in item do
                match name, child with
                | ("options" | "evidence" | "critical" | "guarantees" | "related"), (:? JsonObject as empty) when empty.Count = 0 -> ()
                | ("options" | "evidence" | "critical" | "guarantees" | "related"), (:? JsonArray as empty) when empty.Count = 0 -> ()
                | _ -> copy[name] <- normal child

            copy
        | :? JsonArray as items -> JsonArray(items |> Seq.map normal |> Array.ofSeq)
        | null -> null
        | other -> other.DeepClone()

    Expect.isTrue
        (JsonNode.DeepEquals(normal written, normal node))
        $"{typeof<'T>.Name}: {node.ToJsonString()} became {written.ToJsonString()}"

/// Every member name of the record constructors of the pinned contract, in wire spelling.
let private contractMembers () =
    use pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mep", "mep.ir.json")))

    let types =
        pin.RootElement
            .GetProperty("distribution")
            .GetProperty("Library")
            .GetProperty("def")
            .GetProperty("modules")
            .GetProperty("mep")
            .GetProperty("Public")
            .GetProperty("types")

    [ for t in types.EnumerateObject() do
          match t.Value.GetProperty("Public").TryGetProperty "CustomTypeDefinition" with
          | true, definition ->
              for constructor in definition.GetProperty("constructors").EnumerateObject() do
                  for argument in constructor.Value.EnumerateArray() do
                      let name = argument[0].GetString() |> nonNull
                      let words = name.Split '-'
                      yield name.Replace('-', '_')

                      yield
                          words[0]
                          + (words
                             |> Array.skip 1
                             |> Array.map (fun w -> string (Char.ToUpperInvariant w[0]) + w.Substring 1)
                             |> String.concat "")
          | false, _ -> () ]
    |> Set.ofList

let rec private memberNames (node: JsonNode | null) : string list =
    match node with
    | :? JsonObject as item ->
        [ for KeyValue(name, child) in item do
              yield name
              yield! memberNames child ]
    | :? JsonArray as items -> items |> Seq.collect memberNames |> List.ofSeq
    | _ -> []

[<Tests>]
let tests =
    testList
        "validateModel proposal"
        [ testCase "the proposal transcript messages round-trip through the extended codecs"
          <| fun _ ->
              let all = messages ()
              Expect.isGreaterThanOrEqual all.Length 10 "Both transcripts"
              let mutable requests = Map.empty

              for message in all do
                  match Option.ofObj message["method"] with
                  | Some method ->
                      let name = method.GetValue<string>()
                      let parameters = message["params"] |> Option.ofObj

                      message["id"]
                      |> Option.ofObj
                      |> Option.iter (fun id -> requests <- requests.Add(id.ToJsonString(), name))

                      match name, parameters with
                      | "morphir.initialize", Some parameters -> check ProposalJson.Default.ProposalInitializeParams parameters
                      | "morphir.extension.describe", Some parameters -> check MepJson.Default.DescribeParams parameters
                      | "morphir.validator.validateModel", Some request ->
                          check ProposalJson.Default.ValidateModelRequest request
                          check MepJson.Default.MorphirModel (request["model"] |> nonNull)
                      | _ -> ()
                  | None ->
                      let request = requests[(message["id"] |> nonNull).ToJsonString()]

                      match request, Option.ofObj message["result"], Option.ofObj message["error"] with
                      | "morphir.initialize", Some result, _ -> check ProposalJson.Default.ProposalInitializeResult result
                      | "morphir.extension.describe", Some result, _ -> check MepJson.Default.CapabilityClaimSet result
                      | "morphir.validator.validateModel", Some result, _ ->
                          check ProposalJson.Default.ValidateModelResult result

                          for diagnostic in (result["diagnostics"] |> nonNull).AsArray() do
                              check MepJson.Default.Diagnostic (nonNull diagnostic)
                      | _, _, Some error -> check MepJson.Default.RpcError error
                      | _ -> ()

          testCase "the proposal adds no member outside the validator operation"
          <| fun _ ->
              let known =
                  contractMembers () + set [ "jsonrpc"; "id"; "method"; "params"; "result"; "error" ]

              let added =
                  messages () |> List.collect memberNames |> Set.ofList |> fun names -> names - known

              Expect.equal added (set [ "validator" ]) "Only ExtensionCapabilities.validator is new" ]
