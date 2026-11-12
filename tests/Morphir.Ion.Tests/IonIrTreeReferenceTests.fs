/// Trees written by the pinned reference CLI, read beside the single files for the same distributions. Every
/// file under `trees/` comes from the CLI, so a bug in this reader cannot change what the tests expect. Git does
/// not keep empty directories, so a tree here is the list of its files and nothing else.
module Morphir.Ion.Tests.IonIrTreeReferenceTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Morphir.IR.Pipeline
open Morphir.Ion
open Morphir.Ion.Tests

let private limits = IonReadLimits.defaults

/// The names of the vendored trees: the directories directly under `trees/`, except the generator inputs.
let private treeNames =
    Fixtures.names
    |> Array.filter (fun name ->
        name.StartsWith "trees/"
        && name.Split('/').Length
           >= 3
    )
    |> Array.map (fun name -> name.Split('/')[1])
    |> Array.filter (fun name ->
        name
        <> "inputs"
    )
    |> Array.distinct
    |> List.ofArray

/// The files of one tree, by their location in it.
let private filesOf (name: string) =
    let prefix = $"trees/{name}/"

    Fixtures.names
    |> Array.filter (fun file -> file.StartsWith prefix)
    |> Array.map (fun file -> file.Substring prefix.Length)
    |> List.ofArray

/// The tree as an in-memory VFileTree, with each file's bytes.
let private treeOf (name: string) : VFileTree =
    filesOf name
    |> List.map (fun location ->
        location,
        VFile.ofBytes location (ReadOnlyMemory(Fixtures.bytes $"trees/{name}/{location}"))
    )
    |> Map.ofList
    |> VFileTree.fromFileMap

/// The single file the same distribution was written to. The pairs hold most of them.
let private singleFile (name: string) =
    if
        Fixtures.names
        |> Array.contains $"pairs/{name}.ion"
    then
        Fixtures.bytes $"pairs/{name}.ion"
    else
        Fixtures.bytes $"trees/{name}.ion"

let private readSingle (bytes: byte array) =
    match IonIr.read limits (ReadOnlyMemory bytes) with
    | Ok document -> document
    | Result.Error error -> failtestf "The single file was refused: %A" error

let private expectSame (tree: IrDocument) (single: IrDocument) =
    match tree, single with
    | ClassicIr(treeVersion, a), ClassicIr(singleVersion, b) ->
        Expect.equal treeVersion singleVersion "The format version"

        Expect.equal
            (ClassicComparison.comparable a)
            (ClassicComparison.comparable b)
            "One classic value"
    | V4Ir(treeVersion, a), V4Ir(singleVersion, b) ->
        Expect.equal treeVersion singleVersion "The format version"
        Expect.equal a b "One v4 value"
    | a, b -> failtestf "The families differ: %A and %A" a b

/// SHA-256 of the lines `<file hash>  <location>` in ordinal order, each ending in a line feed. This is what
/// `sha256sum` prints for the tree's files, so PROVENANCE.md records one digest for a tree.
let private treeDigest (name: string) =
    let lines =
        filesOf name
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))
        |> List.map (fun location ->
            let hash =
                Fixtures.sha256 (
                    Fixtures.bytes (
                        "trees/"
                        + name
                        + "/"
                        + location
                    )
                )

            hash
            + "  "
            + location
            + "\n"
        )
        |> String.concat ""

    Fixtures.sha256 (Text.Encoding.UTF8.GetBytes lines)

[<Tests>]
let tests =
    testList "Reference trees" [
        testList "Reference tree" [
            for name in treeNames do
                testCase $"{name} reads as its single file"
                <| fun () ->
                    let single = readSingle (singleFile name)

                    match IonIrTree.read limits (treeOf name) with
                    | Ok tree -> expectSame tree single
                    | Result.Error error -> failtestf "The tree was refused: %A" error
        ]

        testCase "The reference trees cover v3, v4, specs, applications and dependencies"
        <| fun () ->
            for name in
                [
                    "v3-daily-rate"
                    "v3-library-nodes"
                    "v3-specs"
                    "v4-library-nodes"
                    "v4-specs"
                    "v4-application"
                    "v4-empty-library"
                    "v4-escaped-names"
                    "books-and-records-example"
                ] do
                Expect.contains treeNames name "The tree is vendored"

            let kinds =
                treeNames
                |> List.map (fun name ->
                    match IonIr.read limits (ReadOnlyMemory(singleFile name)) with
                    | Ok(ClassicIr(version, _)) -> $"classic {version}"
                    | Ok(V4Ir(version, _)) -> $"v4 {version}"
                    | Result.Error error -> failtestf "%A" error
                )
                |> List.distinct
                |> List.sort

            Expect.equal
                kinds
                [ "classic 3.0.0"; "classic 3.1.0"; "v4 4.0.0" ]
                "Every format version"

            let hasFile (suffix: string) =
                treeNames
                |> List.exists (fun name ->
                    filesOf name
                    |> List.exists (fun file -> file.EndsWith suffix)
                )

            Expect.isTrue (hasFile "/module.ion") "Module files"

            Expect.isTrue
                (filesOf "v4-application"
                 |> List.exists (fun file -> file.StartsWith "deps/"))
                "Linked dependencies"

            Expect.isTrue
                (filesOf "v3-specs"
                 |> List.exists (fun file -> file.StartsWith "deps/"))
                "Specification dependencies"
        |> testLabel "PlatformSpecific"

        testCase "The escaped names tree has the names the draft's table lists"
        <| fun () ->
            let files = filesOf "v4-escaped-names"

            for expected in
                [
                    "pkg/example/manifest/module.ion"
                    "pkg/example/manifest/manifest.type.ion"
                    "pkg/example/manifest/module.type.ion"
                    "pkg/example/manifest/con_.type.ion"
                    "pkg/example/manifest/module/module.ion"
                    "pkg/example/con_/module.ion"
                    "pkg/example/_id/module.ion"
                    "pkg/example/manifest/value-in-_usd.type.ion"
                    "pkg/example/manifest/user-_id.type.ion"
                    "deps/morphir/_sdk/@/basics/module.ion"
                    "deps/acme/_xml/@/manifest/module.ion"
                ] do
                Expect.contains files expected "The reference CLI wrote the file"
        |> testLabel "PlatformSpecific"

        testCase "A tree read from disk through lazy streams reads the same"
        <| fun () ->
            let directory =
                Path.Combine(
                    Path.GetTempPath(),
                    "morphir-ion-tree-"
                    + Guid.NewGuid().ToString "N"
                )

            try
                for location in filesOf "v4-escaped-names" do
                    let target = Path.Combine(directory, location)

                    Directory.CreateDirectory(Path.GetDirectoryName target)
                    |> ignore

                    File.WriteAllBytes(target, Fixtures.bytes $"trees/v4-escaped-names/{location}")

                match VFileTree.loadFromDisk directory with
                | Result.Error error -> failtestf "%A" error
                | Ok loaded ->
                    match IonIrTree.read limits loaded with
                    | Ok tree -> expectSame tree (readSingle (singleFile "v4-escaped-names"))
                    | Result.Error error -> failtestf "The tree was refused: %A" error
            finally
                if Directory.Exists directory then
                    Directory.Delete(directory, true)
        |> testLabel "PlatformSpecific"

        testList "A disk-loaded tree runs through a pipeline with stream sources" [
            for name, classic in
                [
                    "v3-library-nodes", true
                    "v4-library-nodes", false
                ] do
                testCase name
                <| fun () ->
                    let directory =
                        Path.Combine(
                            Path.GetTempPath(),
                            "morphir-ion-pipeline-"
                            + Guid.NewGuid().ToString "N"
                        )

                    try
                        for location in filesOf name do
                            let target = Path.Combine(directory, location)

                            Directory.CreateDirectory(Path.GetDirectoryName target)
                            |> ignore

                            File.WriteAllBytes(target, Fixtures.bytes $"trees/{name}/{location}")

                        match VFileTree.loadFromDisk directory with
                        | Result.Error error -> failtestf "%A" error
                        | Ok loaded ->
                            for file in VFileTree.allFiles loaded do
                                match file.Content with
                                | Some(VFileContent.Stream _) -> ()
                                | other -> failtestf "Expected a stream source, got %A" other

                            let model = ref None

                            let capture =
                                Plugin.create
                                    "capture"
                                    (fun (found: IrDocument) file ->
                                        model.Value <- Some found
                                        Some found, file
                                    )

                            let processor =
                                MorphirProcessor.empty<IrDocument>
                                |> MorphirProcessor.parse IonIrParser.defaultParser
                                |> MorphirProcessor.plugin capture

                            let result = MorphirProcessor.run (Directory loaded) processor

                            let errors =
                                VFileTree.collectMessages result
                                |> List.filter (fun message ->
                                    message.Severity = MessageSeverity.Error
                                    || message.Severity = MessageSeverity.Fatal
                                )

                            Expect.isEmpty errors "The run reports no error"

                            match model.Value with
                            | Some(ClassicIr _ as document) when classic ->
                                expectSame document (readSingle (singleFile name))
                            | Some(V4Ir _ as document) when not classic ->
                                expectSame document (readSingle (singleFile name))
                            | other -> failtestf "The pipeline produced the wrong model: %A" other
                    finally
                        if Directory.Exists directory then
                            Directory.Delete(directory, true)
        ]
        |> testLabel "PlatformSpecific"

        testCase "The vendored trees match their recorded hashes"
        <| fun () ->
            let rows =
                Regex.Matches(
                    Fixtures.text "PROVENANCE.md",
                    @"(?m)^\| `(trees/[^`]+)` \| `([0-9a-f]{64})` \|"
                )
                |> Seq.map (fun row -> row.Groups[1].Value, row.Groups[2].Value)
                |> Map.ofSeq

            Expect.isGreaterThan treeNames.Length 7 "The trees are found"

            for name in treeNames do
                match rows.TryFind $"trees/{name}/" with
                | Some expected ->
                    Expect.equal (treeDigest name) expected $"The digest of trees/{name}/"
                | None -> failtestf "trees/%s/ has no row in PROVENANCE.md" name

            let loose =
                Fixtures.names
                |> Array.filter (fun file ->
                    (file.StartsWith "trees/"
                     && not (file.Substring("trees/".Length).Contains '/'))
                    || file.StartsWith "trees/inputs/"
                )

            Expect.isGreaterThan loose.Length 1 "The single file and the generator input are found"

            for file in loose do
                match rows.TryFind file with
                | Some expected ->
                    Expect.equal (Fixtures.sha256 (Fixtures.bytes file)) expected file
                | None -> failtestf "%s has no row in PROVENANCE.md" file

            for key in
                rows
                |> Map.toList
                |> List.map fst do
                let exists =
                    if key.EndsWith "/" then
                        treeNames
                        |> List.contains (key.Substring("trees/".Length).TrimEnd '/')
                    else
                        Fixtures.names
                        |> Array.contains key

                Expect.isTrue exists $"A row names something that exists: {key}"
        |> testLabel "PlatformSpecific"
    ]
