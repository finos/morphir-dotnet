module Morphir.Frontend.FSharp.Tests.FixtureProvenanceTests

open System
open System.IO
open System.Security.Cryptography
open System.Text.RegularExpressions
open Expecto

let private verify directory =
    let root =
        Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", directory))

    let provenance = File.ReadAllText(Path.Combine(root, "PROVENANCE.md"))

    let entries =
        Regex.Matches(provenance, @"(?m)^\| `([^`]+)` \| `[^`]+` \| `([0-9a-f]{64})` \|$")
        |> Seq.map (fun entry -> entry.Groups[1].Value, entry.Groups[2].Value)
        |> Map.ofSeq

    Expect.isGreaterThan entries.Count 0 "The provenance table must name the imported files"

    let files =
        Directory.GetFiles(root)
        |> Array.map Path.GetFileName
        |> Array.filter ((<>) "PROVENANCE.md")
        |> Set.ofArray

    Expect.equal
        (entries
         |> Map.keys
         |> Set.ofSeq)
        files
        "Every fixture must have exactly one provenance entry"

    for KeyValue(file, expected) in entries do
        let actual =
            File.ReadAllBytes(Path.Combine(root, file))
            |> SHA256.HashData
            |> Convert.ToHexStringLower

        Expect.equal actual expected $"Pinned bytes must not change: {directory}/{file}"

[<Tests>]
let tests =
    testList "Pinned frontend inputs" [
        testCase "MEP bytes match their upstream provenance" (fun () -> verify "Mep")
        testCase "Compile records match their upstream provenance" (fun () -> verify "MorphirOut")
    ]
