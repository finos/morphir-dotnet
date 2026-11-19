// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Rdf.Tests.CanonicalizationTests

open System
open System.Text
open System.Text.Json
open Expecto
open Morphir.Knowledge.Rdf
open Morphir.TestSupport.Fixtures

module CanonicalizationMother =
    /// Two blank nodes that point at each other, written in two orders and with two labelings.
    let cycle = "_:a <urn:p> _:b .\n_:b <urn:p> _:a .\n_:a <urn:q> \"start\" <urn:g> .\n"
    let cycleReordered = "_:y <urn:q> \"start\" <urn:g> .\n_:x <urn:p> _:y .\n_:y <urn:p> _:x .\n"

    /// A ring of blank nodes with no distinguishing statement, which needs n-degree hashing.
    let ring size =
        [ for index in 0 .. size - 1 -> $"_:n{index} <urn:next> _:n{(index + 1) % size} .\n" ] |> String.concat ""

let private readNQuads (bytes: byte array) =
    match NQuads.Read(ReadOnlyMemory bytes) with
    | Ok dataset -> dataset
    | Error error -> failtest (error.ToString())

let private canonicalize options dataset =
    match RdfCanonicalization.Canonicalize(dataset, options) with
    | Ok canonical -> canonical
    | Error error -> failtest (error.ToString())

let private canonicalNQuads (canonical: CanonicalDataset) =
    match NQuads.Write canonical with
    | Ok output -> output.Bytes
    | Error error -> failtest (error.ToString())

let private optionsFor (test: JsonElement) =
    match test.GetProperty("hashAlgorithm").GetString() with
    | "SHA384" -> CanonicalizationOptions.Default.WithHashAlgorithm CanonicalHashAlgorithm.Sha384
    | _ -> CanonicalizationOptions.Default

[<Tests>]
let w3cTests =
    use document = JsonDocument.Parse(ReadOnlyMemory(FixtureCatalog.ReadBytes FixtureCatalog.RdfCanon.Corpus))

    testList "W3C RDFC-1.0" [
        for test in document.RootElement.GetProperty("tests").EnumerateArray() do
            let name = $"""{test.GetProperty("name").GetString()} {test.GetProperty("label").GetString()}"""
            let input = test.GetProperty("actionUtf8Base64").GetBytesFromBase64()
            let options = optionsFor test

            match test.GetProperty("type").GetString() with
            | "eval" ->
                let expected = test.GetProperty("resultUtf8Base64").GetBytesFromBase64()
                testCase name <| fun () ->
                    let actual = readNQuads input |> canonicalize options |> canonicalNQuads
                    Expect.equal (Encoding.UTF8.GetString actual) (Encoding.UTF8.GetString expected) "the canonical N-Quads match"
            | "map" ->
                let expected = test.GetProperty("resultUtf8Base64").GetBytesFromBase64()
                testCase name <| fun () ->
                    let dataset, labels =
                        match NQuadsReading.readLabelled true (ReadOnlyMemory input) with
                        | Ok result -> result
                        | Error error -> failtest (error.ToString())

                    let canonical = canonicalize options dataset
                    use map = JsonDocument.Parse(ReadOnlyMemory expected)
                    let expectedMap =
                        [ for property in map.RootElement.EnumerateObject() -> property.Name, property.Value.GetString() ] |> Map.ofList
                    let actualMap =
                        [ for pair in labels -> pair.Key, canonical.LabelOf pair.Value ] |> Map.ofList
                    Expect.equal actualMap expectedMap "each input label is issued its canonical identifier"
            | _ ->
                testCase name <| fun () ->
                    match RdfCanonicalization.Canonicalize(readNQuads input, options) with
                    | Error(CanonicalizationError.WorkLimitExceeded _) -> ()
                    | Ok _ -> failtest "the poison graph must stop at the work limit"
    ]

[<Tests>]
let tests =
    testList "RDF canonicalization" [
        testCase "isomorphic inputs with fresh blank nodes write identical bytes" <| fun () ->
            let write text =
                Encoding.UTF8.GetBytes(text: string) |> readNQuads |> canonicalize CanonicalizationOptions.Default |> canonicalNQuads

            let first = write CanonicalizationMother.cycle
            Expect.sequenceEqual (write CanonicalizationMother.cycle) first "two reads of one input agree"
            Expect.sequenceEqual (write CanonicalizationMother.cycleReordered) first "another order and labeling agree"

        testCase "canonical N-Triples is byte-identical for two reads of one input" <| fun () ->
            let text = "_:x <urn:p> _:y .\n_:y <urn:p> _:x .\n_:x <urn:label> \"tab\\there\" .\n"
            let write () =
                let dataset =
                    match NTriples.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes text)) with
                    | Ok dataset -> dataset
                    | Error error -> failtest (error.ToString())

                match NTriples.Write(canonicalize CanonicalizationOptions.Default dataset, NamedGraphHandling.Reject) with
                | Ok output -> output.Bytes.ToArray()
                | Error error -> failtest (error.ToString())

            let first = write ()
            Expect.sequenceEqual (write ()) first "the bytes do not depend on blank-node identities"
            Expect.stringContains (Encoding.UTF8.GetString first) "\"tab\\there\"" "canonical escaping writes a tab as \\t"

        testCase "the default N-Quads output is unchanged by canonicalization support" <| fun () ->
            let dataset = readNQuads (Encoding.UTF8.GetBytes "<urn:s> <urn:p> \"a\\tb\" .\n")
            match NQuads.Write dataset with
            | Ok output -> Expect.equal (Encoding.UTF8.GetString output.Bytes) "<urn:s> <urn:p> \"a\\u0009b\" .\n" "the default writer keeps its escaping"
            | Error error -> failtest (error.ToString())

        testCase "the canonical form keeps the dataset and labels every blank node" <| fun () ->
            let dataset = readNQuads (Encoding.UTF8.GetBytes CanonicalizationMother.cycle)
            let canonical = canonicalize CanonicalizationOptions.Default dataset
            Expect.equal canonical.Dataset.Quads dataset.Quads "the identities are unchanged"
            Expect.equal (canonical.Labels.Values |> Seq.sort |> List.ofSeq) [ "c14n0"; "c14n1" ] "each node has a c14n label"

        testCase "work beyond the caller's limit is a typed failure" <| fun () ->
            let dataset = readNQuads (Encoding.UTF8.GetBytes(CanonicalizationMother.ring 6))
            let options = CanonicalizationOptions.Default.WithWorkLimit 3
            match RdfCanonicalization.Canonicalize(dataset, options) with
            | Error(CanonicalizationError.WorkLimitExceeded limit) -> Expect.equal limit 3 "the failure names the limit"
            | Ok _ -> failtest "a ring of six needs more than three units of work"
            Expect.isOk (RdfCanonicalization.Canonicalize(dataset, CanonicalizationOptions.Default)) "the default limit admits it"

        testCase "a work limit must be positive" <| fun () ->
            Expect.throwsT<ArgumentOutOfRangeException> (fun () -> CanonicalizationOptions.Default.WithWorkLimit 0 |> ignore) "zero work cannot finish"
    ]
