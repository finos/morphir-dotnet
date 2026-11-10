// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.IO
open System.Text
open Morphir.Conformance

/// A file that the generator writes, by its repository-relative path, with LF line endings.
type GeneratedFile = { Path: string; Text: string }

/// Generates the suite's features and id map from the pinned corpus, and checks the committed
/// ones. While finos/morphir publishes the corpus as JSON, the pinned JSON is the source and the
/// features are generated from it; no expectation is written in a feature by hand.
module Generation =
    /// The repository-relative directory of the suite.
    [<Literal>]
    let SuiteDirectory = "conformance/suites/wasm-host"

    let private utf8 = UTF8Encoding(false)

    /// The features and the id map that the corpus generates, by path in ordinal order. previous
    /// is the committed id map, whose numbers the generation keeps.
    let generate
        (corpus: WasmHostCorpus)
        (previous: CaseIdMap option)
        : Result<GeneratedFile list, CorpusIssue list> =
        corpus.Cases
        |> List.map (fun case -> case.CaseId)
        |> CaseIds.assign previous corpus.Pin
        |> Result.bind (fun map ->
            let numbered =
                corpus.Cases
                |> List.choose (fun case ->
                    CaseIds.numberOf map case.CaseId
                    |> Option.map (fun number -> number, case)
                )

            let features =
                numbered
                |> List.groupBy (fun (_, case) -> FeatureWriter.group case.CaseId)
                |> List.sortWith (fun (left, _) (right, _) ->
                    StringComparer.Ordinal.Compare(left, right)
                )
                |> List.map (fun (group, cases) ->
                    FeatureWriter.feature corpus.Pin corpus.Kit group cases
                    |> Result.map (fun text -> {
                        Path =
                            SuiteDirectory
                            + "/"
                            + FeatureWriter.fileName group
                        Text = text
                    })
                )

            match
                features
                |> List.collect (
                    function
                    | Error issues -> issues
                    | Ok _ -> []
                )
            with
            | [] ->
                let files =
                    (features
                     |> List.choose Result.toOption)
                    @ [
                        {
                            Path =
                                SuiteDirectory
                                + "/"
                                + Suite.IdMapFile
                            Text = CaseIds.write map
                        }
                    ]

                Ok(
                    files
                    |> List.sortWith (fun left right ->
                        StringComparer.Ordinal.Compare(left.Path, right.Path)
                    )
                )
            | issues -> Error issues
        )

    let private fileName (path: string) =
        match path.LastIndexOf '/' with
        | -1 -> path
        | index -> path.Substring(index + 1)

    let private lines (text: string) = text.Split '\n'

    /// The 1-based number of the first line where two texts differ.
    let private firstDifference (expected: string) (actual: string) =
        let expectedLines, actualLines = lines expected, lines actual

        let rec find index =
            if
                index
                >= expectedLines.Length
                || index
                   >= actualLines.Length
            then
                index + 1
            elif
                expectedLines[index]
                <> actualLines[index]
            then
                index + 1
            else
                find (index + 1)

        find 0

    /// The scenario blocks of a feature, by scenario name, and the text before the first one.
    let private blocks (text: string) =
        let all = lines text

        let starts =
            all
            |> Array.indexed
            |> Array.choose (fun (index, line) ->
                if line.TrimStart().StartsWith("Scenario:", StringComparison.Ordinal) then
                    let start =
                        if
                            index > 0
                            && all[index - 1].TrimStart().StartsWith('@')
                        then
                            index - 1
                        else
                            index

                    Some(start, line.TrimStart().Substring("Scenario:".Length).Trim())
                else
                    None
            )

        let header =
            match Array.tryHead starts with
            | Some(start, _) -> String.Join("\n", all[.. start - 1])
            | None -> text

        let named =
            starts
            |> Array.mapi (fun position (start, name) ->
                let finish =
                    if
                        position
                        + 1 < starts.Length
                    then
                        fst
                            starts[position
                                   + 1]
                        - 1
                    else
                        all.Length
                        - 1

                name, String.Join("\n", all[start..finish])
            )
            |> Array.toList
            |> List.groupBy fst
            |> List.map (fun (name, parts) ->
                name,
                parts
                |> List.map snd
                |> String.concat "\n"
            )

        header, named

    /// The scenarios that differ between two texts of a feature, and whether the text before the
    /// first scenario differs.
    let private differingScenarios (expected: string) (actual: string) =
        let expectedHeader, expectedBlocks = blocks expected
        let actualHeader, actualBlocks = blocks actual
        let expectedMap, actualMap = Map.ofList expectedBlocks, Map.ofList actualBlocks

        let names =
            (expectedBlocks
             |> List.map fst)
            @ (actualBlocks
               |> List.map fst
               |> List.filter (fun name -> not (Map.containsKey name expectedMap)))

        let differing =
            names
            |> List.filter (fun name ->
                Map.tryFind name expectedMap
                <> Map.tryFind name actualMap
            )

        expectedHeader
        <> actualHeader,
        differing

    let private regenerate =
        $"The suite is generated from the pinned corpus; never edit it by hand. Run: {Suite.RegenerateCommand}"

    /// Compares the generated files with the committed ones, by file name within the suite
    /// directory. Each issue names the file, the first line that differs and, for a feature, each
    /// case whose scenarios differ. No issue means the committed suite is what the corpus generates.
    let check (expected: GeneratedFile list) (committed: Artifact list) : CorpusIssue list =
        let committedByName =
            committed
            |> List.map (fun artifact -> fileName artifact.Path, artifact)
            |> Map.ofList

        let expectedNames =
            expected
            |> List.map (fun file -> fileName file.Path)
            |> Set.ofList

        let stale path line message = {
            Code = "mck.stale_suite"
            Path = path
            Line = line
            Message = message
        }

        [
            for file in expected do
                match Map.tryFind (fileName file.Path) committedByName with
                | None -> yield stale file.Path 0 $"The file is missing. {regenerate}"
                | Some artifact ->
                    let bytes = utf8.GetBytes file.Text

                    if
                        artifact.Bytes
                        <> bytes
                    then
                        let actual = Encoding.UTF8.GetString artifact.Bytes
                        let line = firstDifference file.Text actual

                        if file.Path.EndsWith(".feature", StringComparison.Ordinal) then
                            let header, scenarios = differingScenarios file.Text actual

                            let parts = [
                                if header then
                                    yield "the header"

                                yield! scenarios
                            ]

                            let named =
                                match parts with
                                | [] -> "its bytes"
                                | parts -> String.concat ", " parts

                            yield
                                stale
                                    artifact.Path
                                    line
                                    $"The feature differs from what the pinned corpus generates, at line {line}, in {named}. {regenerate}"
                        else
                            yield
                                stale
                                    artifact.Path
                                    line
                                    $"The id map differs from what the pinned corpus generates, at line {line}. {regenerate}"

            for artifact in committed do
                if not (Set.contains (fileName artifact.Path) expectedNames) then
                    yield
                        stale
                            artifact.Path
                            0
                            $"The pinned corpus generates no such file. {regenerate}"
        ]

    let private suiteRoot (repositoryRoot: string) =
        Path.Combine(Array.append [| repositoryRoot |] (SuiteDirectory.Split '/'))

    /// The committed files of the suite in a repository checkout, by repository-relative path.
    let committed (repositoryRoot: string) : Artifact list =
        let directory = suiteRoot repositoryRoot

        if Directory.Exists directory then
            Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            |> Array.map (fun file -> {
                Path = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/')
                Bytes = File.ReadAllBytes file
            })
            |> Array.sortWith (fun left right ->
                StringComparer.Ordinal.Compare(left.Path, right.Path)
            )
            |> Array.toList
        else
            []

    let private previousMap (committed: Artifact list) =
        match
            committed
            |> List.tryFind (fun artifact -> fileName artifact.Path = Suite.IdMapFile)
        with
        | None -> Ok None
        | Some artifact ->
            CaseIds.parse artifact.Path artifact.Bytes
            |> Result.map Some

    /// The files that the pinned corpus of a checkout generates, keeping the committed id map.
    let expected (repositoryRoot: string) : Result<GeneratedFile list, CorpusIssue list> =
        CorpusJson.load repositoryRoot
        |> Result.bind (fun corpus ->
            previousMap (committed repositoryRoot)
            |> Result.bind (generate corpus)
        )

    /// Checks the committed suite of a repository checkout against its pin: the stale check.
    let checkRepository (repositoryRoot: string) : CorpusIssue list =
        match expected repositoryRoot with
        | Error issues -> issues
        | Ok files -> check files (committed repositoryRoot)

    /// Generates the suite of a repository checkout again and writes it, removing any file of the
    /// suite directory that the corpus no longer generates.
    let writeRepository (repositoryRoot: string) : Result<GeneratedFile list, CorpusIssue list> =
        expected repositoryRoot
        |> Result.map (fun files ->
            let directory = suiteRoot repositoryRoot

            Directory.CreateDirectory directory
            |> ignore

            let names =
                files
                |> List.map (fun file -> fileName file.Path)
                |> Set.ofList

            for existing in Directory.GetFiles directory do
                if not (Set.contains (Path.GetFileName existing) names) then
                    File.Delete existing

            for file in files do
                File.WriteAllText(Path.Combine(repositoryRoot, file.Path), file.Text, utf8)

            files
        )
