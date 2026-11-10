// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The repository checkout, the pinned corpus and sandbox copies of both.
module Morphir.Conformance.Tests.Repository

open System.IO
open System.Text
open System.Text.Json.Nodes
open Expecto
open Morphir.Conformance
open Morphir.Conformance.WasmHost
open Morphir.TestSupport

/// The root of the repository checkout.
let root =
    let rec find (directory: DirectoryInfo) =
        match directory with
        | null -> failwith "No directory above the tests holds Morphir.slnx"
        | found when File.Exists(Path.Combine(found.FullName, "Morphir.slnx")) -> found.FullName
        | found -> find found.Parent

    find (DirectoryInfo __SOURCE_DIRECTORY__)

/// The number of cases at the pinned commit 7b049c223.
[<Literal>]
let PinnedCaseCount = 56

let requireOk label (result: Result<'T, CorpusIssue list>) =
    match result with
    | Ok value -> value
    | Error issues ->
        let lines =
            issues
            |> List.map (fun issue -> $"{issue.Code} {issue.Path}:{issue.Line} {issue.Message}")

        failtest $"""{label}: {String.concat "\n" lines}"""

let requireError label (result: Result<'T, CorpusIssue list>) =
    match result with
    | Ok _ -> failtest $"{label}: expected an error"
    | Error issues -> issues

/// The pinned corpus, read from the checkout.
let corpus =
    lazy
        (CorpusJson.load root
         |> requireOk "The pinned corpus")

/// The committed files of the suite.
let committed () = Generation.committed root

/// The number of elements of the corpus's cases array, counted without the reader.
let corpusCaseCount () =
    let node =
        JsonNode.Parse(File.ReadAllText(Path.Combine(root, CorpusJson.DefaultCorpusPath)))

    node["cases"].AsArray().Count

/// A sandbox that holds a copy of the pinned corpus, its provenance and the committed suite at
/// their repository paths. edit changes the corpus copy; the pinned file is never touched.
let sandboxWith (edit: JsonObject -> unit) =
    let sandbox = TestSandbox.Create "morphir-wasm-host-"

    let copy (relative: string) =
        let target = sandbox.PathOf(relative.Split '/')

        Directory.CreateDirectory(Path.GetDirectoryName target)
        |> ignore

        File.Copy(Path.Combine(root, relative), target)

    copy CorpusJson.DefaultProvenancePath

    let corpusNode =
        JsonNode
            .Parse(File.ReadAllText(Path.Combine(root, CorpusJson.DefaultCorpusPath)))
            .AsObject()

    edit corpusNode
    let target = sandbox.PathOf(CorpusJson.DefaultCorpusPath.Split '/')

    Directory.CreateDirectory(Path.GetDirectoryName target)
    |> ignore

    File.WriteAllText(target, corpusNode.ToJsonString(), UTF8Encoding false)

    for artifact in committed () do
        let target = sandbox.PathOf(artifact.Path.Split '/')

        Directory.CreateDirectory(Path.GetDirectoryName target)
        |> ignore

        File.WriteAllBytes(target, artifact.Bytes)

    sandbox

/// The case object with this id in a corpus copy.
let caseNode (caseId: string) (corpusNode: JsonObject) =
    corpusNode["cases"].AsArray()
    |> Seq.find (fun case -> case["caseId"].GetValue<string>() = caseId)
    |> fun case -> case.AsObject()

/// The text of a committed feature.
let featureText (name: string) =
    committed ()
    |> List.find (fun artifact ->
        artifact.Path = Generation.SuiteDirectory
                        + "/"
                        + name
    )
    |> fun artifact -> Encoding.UTF8.GetString artifact.Bytes

/// The committed suite, with one file's text replaced.
let committedWith (name: string) (text: string) =
    committed ()
    |> List.map (fun artifact ->
        if
            artifact.Path = Generation.SuiteDirectory
                            + "/"
                            + name
        then
            { artifact with
                Bytes = Encoding.UTF8.GetBytes text
            }
        else
            artifact
    )

/// The bytes of the committed id map.
let committedMap () =
    committed ()
    |> List.find (fun artifact ->
        artifact.Path = Generation.SuiteDirectory
                        + "/"
                        + Suite.IdMapFile
    )
    |> fun artifact -> artifact.Bytes

/// The expectation object of a case's first run in a corpus copy.
let expectNode (caseId: string) (corpusNode: JsonObject) =
    let case = caseNode caseId corpusNode
    case["runs"].[0].["expect"].AsObject()

/// One event matcher of a case's first run in a corpus copy.
let eventNode (caseId: string) (index: int) (corpusNode: JsonObject) =
    let expect = expectNode caseId corpusNode
    expect["events"].[index].AsObject()
