// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.ConformanceTests

open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Conformance

let private options = {
    Suite = "frontend-fsharp"
    VocabularyVersion = "1.0"
    IrVersions = [ "1"; "2"; "3"; "4" ]
    InputKinds = [ "files"; "directory"; "project file" ]
}

let private featureText =
    """@suite:frontend-fsharp @refusal @basis:hand-authored
Feature: Refuse mutable bindings
  Scenario Outline: frontend-fsharp-0001 Mutable bindings are refused
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      '''fsharp
      namespace Acme.Demo
      module Pricing =
          let mutable total = 0
      '''
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      '''json
      [{"severity":"error","code":"fsharp.unsupported_mutable","location":{"uri":"Pricing.fs","range":{"start":{"line":2,"character":8},"end":{"line":2,"character":15}}}}]
      '''
    Examples:
      | input        | version |
      | files        | 1       |
      | files        | 2       |
      | files        | 3       |
      | files        | 4       |
      | directory    | 1       |
      | directory    | 2       |
      | directory    | 3       |
      | directory    | 4       |
      | project file | 1       |
      | project file | 2       |
      | project file | 3       |
      | project file | 4       |
"""

let private feature = featureText.Replace("'''", String.replicate 3 "\"")

let private artifact path text = {
    Path = path
    Bytes = Encoding.UTF8.GetBytes(text: string)
}

let private read source =
    Corpus.read options [ artifact "suites/frontend-fsharp/mutable.feature" source ]

let private requireOk =
    function
    | Ok value -> value
    | Error issues -> failtestf "Expected a valid case set: %A" issues

let private excluding reason =
    feature.Replace("      | project file | 4       |", "")
    + "\n    @excluded\n    Examples:\n      | input | version | reason |\n      | project file | 4 | "
    + reason
    + " |\n"

[<Tests>]
let tests =
    testList "Portable frontend conformance" [
        testCase
            "The official parser expands the declared version and input matrix"
            (fun () ->
                let suite =
                    read feature
                    |> requireOk

                Expect.equal suite.Cases.Length 12 "Every declared combination is retained"

                Expect.equal
                    suite.Cases[0].Input.Documents[0].Uri
                    "Pricing.fs"
                    "Sources remain portable documents"

                Expect.equal suite.Cases[0].Input.IrVersion "1" "Version spelling remains exact"
            )
        testCase
            "Unknown vocabulary steps are rejected with their source line"
            (fun () ->
                let unknown =
                    feature.Replace(
                        "compilation fails with diagnostics:",
                        "the output looks right"
                    )

                match read unknown with
                | Ok _ -> failtest "An unknown step must not become an executable case"
                | Error issues ->
                    Expect.isTrue
                        (issues
                         |> List.exists (fun issue ->
                             issue.Code = "mck.unknown_step"
                             && issue.Line > 0
                         ))
                        "The linter identifies the unknown step"
            )
        testCase
            "An excluded matrix row needs an explanation"
            (fun () ->
                match read (excluding "") with
                | Ok _ -> failtest "An unexplained exclusion must fail validation"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.exclusion_reason"
                        "A reason is required"
            )
        testCase
            "A missing matrix row fails until it has an explicit exclusion"
            (fun () ->
                let incomplete = feature.Replace("      | project file | 4       |", "")

                match read incomplete with
                | Ok _ -> failtest "An absent combination must not silently disappear"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.missing_combination"
                        "The matrix is complete or explicitly excluded"

                let explained =
                    read (excluding "Covered by a separate project-file case")
                    |> requireOk

                Expect.equal explained.Cases.Length 12 "Exclusions remain in the plan"

                Expect.equal
                    explained.Cases[11].Exclusion
                    (Some "Covered by a separate project-file case")
                    "The reason survives parsing"
            )
        testCase
            "An empty Examples matrix cannot disappear beside a complete scenario"
            (fun () ->
                let start = feature.IndexOf("  Scenario Outline:", System.StringComparison.Ordinal)

                let length =
                    feature.IndexOf("    Examples:", System.StringComparison.Ordinal)
                    - start

                let emptyScenario =
                    feature
                        .Substring(start, length)
                        .Replace("frontend-fsharp-0001", "frontend-fsharp-0002")
                    + "    Examples:\n      | input | version |\n"

                let corpus =
                    feature
                    + emptyScenario

                match read corpus with
                | Ok _ -> failtest "Every declared scenario must cover or exclude its matrix"
                | Error issues ->
                    let missing =
                        issues
                        |> List.filter (fun issue -> issue.Code = "mck.missing_combination")

                    Expect.equal
                        missing.Length
                        12
                        "The empty scenario is missing every combination"

                    Expect.all
                        missing
                        (fun issue ->
                            issue.Path = "suites/frontend-fsharp/mutable.feature"
                            && issue.Line > 1
                            && issue.Message.StartsWith(
                                "frontend-fsharp-0002",
                                System.StringComparison.Ordinal
                            )
                        )
                        "Every issue identifies the omitted scenario and its declaration"
            )
        testCase
            "Features cannot select an adapter"
            (fun () ->
                match
                    read (
                        "@adapter:cli\n"
                        + feature
                    )
                with
                | Ok _ -> failtest "Adapter selection belongs to the runner"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.implementation_in_feature"
                        "Cases are portable across adapters"
            )
        testCase
            "Library observations are compared against case data and reports are deterministic"
            (fun () ->
                let suite =
                    read feature
                    |> requireOk

                let identity = {
                    Name = "test-library"
                    Version = "1.0.0"
                    Sha256 = String.replicate 64 "a"
                }

                let adapter =
                    LibraryAdapter.create
                        identity
                        (fun _ _ ->
                            Task.FromResult(
                                Ok
                                    {
                                        Success = true
                                        ModelJson = None
                                        Diagnostics = []
                                    }
                            )
                        )

                let run () =
                    Runner.run suite adapter CancellationToken.None
                    |> fun task -> task.GetAwaiter().GetResult()

                let first, second = run (), run ()

                Expect.isFalse
                    first.Qualified
                    "An incorrect implementation cannot generate its own expectations"

                Expect.equal first.Results.Length 12 "Every planned combination has a verdict"

                Expect.equal
                    (Report.toUtf8 first)
                    (Report.toUtf8 second)
                    "Reports contain no run-dependent members"

                Expect.isMatch
                    first.CaseSetSha256
                    "^[0-9a-f]{64}$"
                    "The case set has a content digest"

                let json = Encoding.UTF8.GetString(Report.toUtf8 first)
                Expect.stringContains json "test-library" "The implementation is identified"

                Expect.stringContains
                    json
                    (typeof<RunReport>.Assembly.GetName().Version.ToString())
                    "The runner reports its real assembly version"

                Expect.stringContains json first.CaseSetSha256 "The report identifies its oracle"
            )
        testCase
            "Malformed Gherkin returns a located issue instead of escaping the reader"
            (fun () ->
                match read "Given something before a Feature\nScenario: bad" with
                | Ok _ -> failtest "Malformed Gherkin cannot form a case set"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.invalid_gherkin"
                        "The parser failure is a corpus error"
            )
        testCase
            "A vocabulary version the runner does not implement is refused"
            (fun () ->
                let future =
                    { options with
                        VocabularyVersion = "2.0"
                    }

                match
                    Corpus.read future [
                        artifact "suites/frontend-fsharp/mutable.feature" feature
                    ]
                with
                | Ok _ -> failtest "A vocabulary is a versioned contract"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.vocabulary_version"
                        "No case runs under unknown semantics"
            )
        testCase
            "A scenario without an expected observation is invalid"
            (fun () ->
                let missing =
                    System.Text.RegularExpressions.Regex.Replace(
                        feature,
                        "(?s)    Then compilation fails with diagnostics:.*?    Examples:",
                        "    Examples:"
                    )

                match read missing with
                | Ok _ -> failtest "No expected observation cannot mean successful compilation"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.invalid_case"
                        "Expectations are required"
            )
        testCase
            "IR observations use object-member equality while preserving array order"
            (fun () ->
                let success =
                    System.Text.RegularExpressions.Regex.Replace(
                        feature,
                        "(?s)    Then compilation fails with diagnostics:.*?    Examples:",
                        "    Then compilation succeeds with no error diagnostics\n    And the IR equals \"expected/constant-<version>.json\"\n\n    Examples:"
                    )

                let artifacts = [
                    artifact "suites/frontend-fsharp/constant.feature" success
                    for version in options.IrVersions do
                        artifact
                            ("suites/frontend-fsharp/expected/constant-"
                             + version
                             + ".json")
                            """{"a":1,"b":[1,2]}"""
                ]

                let suite =
                    Corpus.read options artifacts
                    |> requireOk

                let identity = {
                    Name = "json-control"
                    Version = "1.0.0"
                    Sha256 = String.replicate 64 "b"
                }

                let run model =
                    let adapter =
                        LibraryAdapter.create
                            identity
                            (fun _ _ ->
                                Task.FromResult(
                                    Ok
                                        {
                                            Success = true
                                            ModelJson = Some model
                                            Diagnostics = []
                                        }
                                )
                            )

                    Runner.run suite adapter CancellationToken.None
                    |> fun task -> task.GetAwaiter().GetResult()

                Expect.isTrue
                    (run """{"b":[1,2],"a":1}""").Qualified
                    "Object member order is immaterial"

                Expect.isFalse
                    (run """{"a":1,"b":[2,1]}""").Qualified
                    "Changing array order must fail"
            )
        testCase
            "Case ids and matrix rows must be unique"
            (fun () ->
                let duplicate =
                    feature.Replace(
                        "      | files        | 1       |",
                        "      | files        | 1       |\n      | files        | 1       |"
                    )

                match read duplicate with
                | Ok _ -> failtest "Duplicate rows would run the same case twice"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.duplicate_combination"
                        "Each matrix key is unique"
            )
        testCase
            "Malformed case data is refused before interpretation"
            (fun () ->
                let invalid = [
                    feature.Replace(
                        "Given the F# package \"acme/demo\" exposing \"Pricing\"",
                        "Given the source \"Other.fs\":"
                    )
                    feature.Replace("'''fsharp", "'''json").Replace("\"\"\"fsharp", "\"\"\"json")
                    feature.Replace("\"severity\":\"error\"", "\"notSeverity\":true")
                    feature.Replace("from <input>", "from cli")
                    feature.Replace("| input        | version |", "| wrong        | version |")
                ]

                for source in invalid do
                    match read source with
                    | Ok _ -> failtest "Malformed case data must not be silently interpreted"
                    | Error issues -> Expect.isNonEmpty issues "The corpus explains the refusal"
            )
        testCase
            "Artifact capture order does not change the digest or plan"
            (fun () ->
                let first = artifact "suites/frontend-fsharp/a.feature" feature

                let second =
                    artifact
                        "suites/frontend-fsharp/z.feature"
                        (feature.Replace("frontend-fsharp-0001", "frontend-fsharp-0002"))

                let forward =
                    Corpus.read options [ first; second ]
                    |> requireOk

                let backward =
                    Corpus.read options [ second; first ]
                    |> requireOk

                Expect.equal
                    forward.Sha256
                    backward.Sha256
                    "The same bytes identify the same oracle"

                Expect.equal forward.Cases backward.Cases "Plans use portable file order"

                let edited =
                    artifact
                        first.Path
                        (feature
                         + "\n# Independent corpus edit\n")

                let changed =
                    Corpus.read options [ edited; second ]
                    |> requireOk

                Expect.notEqual
                    forward.Sha256
                    changed.Sha256
                    "Every case artifact byte is part of the digest"
            )
        testCase
            "Captured artifacts must have unique root-relative portable paths"
            (fun () ->
                for path in
                    [
                        "/private/mutable.feature"
                        "../mutable.feature"
                        "suites/frontend-fsharp/../mutable.feature"
                    ] do
                    match Corpus.read options [ artifact path feature ] with
                    | Ok _ -> failtest "Machine paths cannot identify portable case artifacts"
                    | Error issues ->
                        Expect.contains
                            (issues
                             |> List.map (fun issue -> issue.Code))
                            "mck.artifact_path"
                            "The captured root is explicit"
            )
        testCase
            "Separate scenarios cannot reuse one stable case id"
            (fun () ->
                let remove versions source =
                    System.Text.RegularExpressions.Regex.Replace(
                        source,
                        "(?m)^.*\\| ["
                        + versions
                        + "] +\\|\\r?\\n",
                        ""
                    )

                let artifacts = [
                    artifact "suites/frontend-fsharp/first.feature" (remove "34" feature)
                    artifact "suites/frontend-fsharp/second.feature" (remove "12" feature)
                ]

                match Corpus.read options artifacts with
                | Ok _ -> failtest "Case identity must not be split across different scenarios"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.duplicate_id"
                        "Ids are unique across the captured case set"
            )
        testCase
            "The library adapter preserves exclusions and compares diagnostic ranges"
            (fun () ->
                let suite =
                    read (excluding "Covered by another case")
                    |> requireOk

                let identity = {
                    Name = "diagnostic-control"
                    Version = "1.0.0"
                    Sha256 = String.replicate 64 "c"
                }

                let diagnostic = {
                    Severity = "error"
                    Code = "fsharp.unsupported_mutable"
                    Uri = "Pricing.fs"
                    StartLine = 2
                    StartCharacter = 8
                    EndLine = 2
                    EndCharacter = 15
                }

                let observed = ResizeArray<CompileInput>()

                let run key =
                    observed.Clear()

                    let adapter =
                        LibraryAdapter.create
                            identity
                            (fun input _ ->
                                observed.Add input

                                Task.FromResult(
                                    Ok
                                        {
                                            Success = false
                                            ModelJson = None
                                            Diagnostics = [ key ]
                                        }
                                )
                            )

                    Runner.run suite adapter CancellationToken.None
                    |> fun task -> task.GetAwaiter().GetResult()

                let correct = run diagnostic
                Expect.isTrue correct.Qualified "Independent expected diagnostics match"
                Expect.equal observed.Count 11 "An excluded row never invokes the implementation"

                Expect.equal
                    correct.Results[11].Verdict
                    (Excluded "Covered by another case")
                    "Excluded cases keep their reason"

                use document =
                    System.Text.Json.JsonDocument.Parse(
                        Encoding.UTF8.GetString(Report.toUtf8 correct)
                    )

                let rows = document.RootElement.GetProperty("cases")
                let observation = rows[0].GetProperty("observation")
                let diagnostics = observation.GetProperty("diagnostics")
                let location = diagnostics[0].GetProperty("location")

                Expect.equal
                    (location.GetProperty("uri").GetString())
                    "Pricing.fs"
                    "Reports retain the diagnostic location"

                Expect.equal
                    (rows[11].GetProperty("reason").GetString())
                    "Covered by another case"
                    "The serialized report retains exclusions"

                Expect.isFalse
                    (run { diagnostic with StartCharacter = 9 }).Qualified
                    "An off-by-one UTF-16 location fails"
            )
        testCase
            "The shipped seed is accepted and a missing sidecar is refused"
            (fun () ->
                let root =
                    System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "conformance")
                    )

                let files = [
                    for path in
                        System.IO.Directory.GetFiles(
                            root,
                            "*",
                            System.IO.SearchOption.AllDirectories
                        ) do
                        {
                            Path =
                                System.IO.Path
                                    .GetRelativePath(root, path)
                                    .Replace(System.IO.Path.DirectorySeparatorChar, '/')
                            Bytes = System.IO.File.ReadAllBytes path
                        }
                ]

                let shipped =
                    Corpus.read options files
                    |> requireOk

                Expect.isTrue
                    (shipped.Cases
                     |> List.exists (fun case -> case.Id = "frontend-fsharp-0001"))
                    "The product data passes the same linter"

                let missing =
                    feature.Replace(
                        "    Examples:",
                        "    And the IR equals \"expected/missing-<version>.json\"\n    Examples:"
                    )

                match read missing with
                | Ok _ -> failtest "An expectation cannot silently lose its sidecar"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.missing_artifact"
                        "The missing oracle file is named"
            )
        testCase
            "Invalid implementation JSON stays a failed observation in a valid report"
            (fun () ->
                let suite =
                    read feature
                    |> requireOk

                let identity = {
                    Name = "broken-json"
                    Version = "1.0.0"
                    Sha256 = String.replicate 64 "d"
                }

                let adapter =
                    LibraryAdapter.create
                        identity
                        (fun _ _ ->
                            Task.FromResult(
                                Ok
                                    {
                                        Success = false
                                        ModelJson = Some "{broken"
                                        Diagnostics = []
                                    }
                            )
                        )

                let report =
                    Runner.run suite adapter CancellationToken.None
                    |> fun task -> task.GetAwaiter().GetResult()

                Expect.isFalse report.Qualified "A failed compile cannot return a model"

                use parsed =
                    System.Text.Json.JsonDocument.Parse(
                        Encoding.UTF8.GetString(Report.toUtf8 report)
                    )

                let cases = parsed.RootElement.GetProperty("cases")
                let observation = cases[0].GetProperty("observation")

                Expect.equal
                    (observation.GetProperty("modelJson").GetString())
                    "{broken"
                    "The complete invalid payload is retained as text"
            )
        testCase
            "Unknown eligibility metadata cannot silently become a required passing case"
            (fun () ->
                for tag in [ "@knownGap:compiler"; "@excluded"; "@implementation:other" ] do
                    match
                        read (
                            tag
                            + "\n"
                            + feature
                        )
                    with
                    | Ok _ -> failtest "Only implemented tag semantics can be admitted"
                    | Error issues -> Expect.isNonEmpty issues "The unsupported metadata is named"
            )
        testCase
            "An operational failure gets a verdict and later cases still run"
            (fun () ->
                let suite =
                    read feature
                    |> requireOk

                let identity = {
                    Name = "failing-control"
                    Version = "1.0.0"
                    Sha256 = String.replicate 64 "e"
                }

                let mutable called = 0

                let adapter =
                    LibraryAdapter.create
                        identity
                        (fun _ _ ->
                            called <- called + 1

                            Task.FromResult(
                                Error
                                    {
                                        Message = "simulated compiler failure"
                                        Partial = None
                                    }
                            )
                        )

                let report =
                    Runner.run suite adapter CancellationToken.None
                    |> fun task -> task.GetAwaiter().GetResult()

                Expect.equal called 12 "One operational failure does not truncate the plan"
                Expect.isFalse report.Qualified "An unavailable implementation never qualifies"

                use document =
                    System.Text.Json.JsonDocument.Parse(
                        Encoding.UTF8.GetString(Report.toUtf8 report)
                    )

                Expect.equal
                    (document.RootElement.GetProperty("cases").GetArrayLength())
                    12
                    "Every failure remains checkable"
            )
        testCase
            "Invalid successful observations cannot qualify"
            (fun () ->
                let success =
                    System.Text.RegularExpressions.Regex.Replace(
                        feature,
                        "(?s)    Then compilation fails with diagnostics:.*?    Examples:",
                        "    Then compilation succeeds with no error diagnostics\n\n    Examples:"
                    )

                let suite =
                    read success
                    |> requireOk

                let identity = {
                    Name = "invalid-success"
                    Version = "1.0.0"
                    Sha256 = String.replicate 64 "f"
                }

                let invalidSeverity = {
                    Severity = "fatal"
                    Code = ""
                    Uri = ""
                    StartLine = 0
                    StartCharacter = 0
                    EndLine = 0
                    EndCharacter = 0
                }

                for model, diagnostics in
                    [ None, []; Some "{broken", []; Some "{}", [ invalidSeverity ] ] do
                    let adapter =
                        LibraryAdapter.create
                            identity
                            (fun _ _ ->
                                Task.FromResult(
                                    Ok
                                        {
                                            Success = true
                                            ModelJson = model
                                            Diagnostics = diagnostics
                                        }
                                )
                            )

                    let report =
                        Runner.run suite adapter CancellationToken.None
                        |> fun task -> task.GetAwaiter().GetResult()

                    Expect.isFalse
                        report.Qualified
                        "Success needs valid JSON and recognized diagnostic severities"
            )
        testCase
            "Malformed expected IR is a corpus error rather than an implementation failure"
            (fun () ->
                let success =
                    System.Text.RegularExpressions.Regex.Replace(
                        feature,
                        "(?s)    Then compilation fails with diagnostics:.*?    Examples:",
                        "    Then compilation succeeds with no error diagnostics\n    And the IR equals \"expected/broken.json\"\n    Examples:"
                    )

                match
                    Corpus.read options [
                        artifact "suites/frontend-fsharp/constant.feature" success
                        artifact "suites/frontend-fsharp/expected/broken.json" "{broken"
                    ]
                with
                | Ok _ -> failtest "Invalid oracle JSON must fail before the implementation runs"
                | Error issues ->
                    Expect.contains
                        (issues
                         |> List.map (fun issue -> issue.Code))
                        "mck.invalid_artifact"
                        "The invalid expectation is named"
            )
        testCase
            "Operational failures retain observations already gathered"
            (fun () ->
                let suite =
                    read feature
                    |> requireOk

                let identity = {
                    Name = "partial-control"
                    Version = "1.0.0"
                    Sha256 = String.replicate 64 "1"
                }

                let warning = {
                    Severity = "warning"
                    Code = "fsharp.compiler"
                    Uri = "Pricing.fs"
                    StartLine = 2
                    StartCharacter = 8
                    EndLine = 2
                    EndCharacter = 15
                }

                let partial = {
                    Success = false
                    ModelJson = None
                    Diagnostics = [ warning ]
                }

                let adapter =
                    LibraryAdapter.create
                        identity
                        (fun _ _ ->
                            Task.FromResult(
                                Error
                                    {
                                        Message = "Compiler unavailable after analysis"
                                        Partial = Some partial
                                    }
                            )
                        )

                let report =
                    Runner.run suite adapter CancellationToken.None
                    |> fun task -> task.GetAwaiter().GetResult()

                Expect.isFalse
                    report.Qualified
                    "A partial observation does not turn operational failure into success"

                Expect.equal
                    report.Results[0].Observation
                    (Some partial)
                    "Earlier warnings are kept"

                Expect.stringContains
                    (Encoding.UTF8.GetString(Report.toUtf8 report))
                    "fsharp.compiler"
                    "The report retains the partial diagnostics"
            )
        testCase
            "Diagnostic observations preserve document and position order"
            (fun () ->
                let first = {
                    Severity = "error"
                    Code = "fsharp.unsupported_mutable"
                    Uri = "Pricing.fs"
                    StartLine = 2
                    StartCharacter = 8
                    EndLine = 2
                    EndCharacter = 15
                }

                let second =
                    { first with
                        StartLine = 3
                        EndLine = 3
                    }

                let secondJson =
                    """{"severity":"error","code":"fsharp.unsupported_mutable","location":{"uri":"Pricing.fs","range":{"start":{"line":3,"character":8},"end":{"line":3,"character":15}}}}"""

                let source =
                    feature
                        .Replace(
                            "let mutable total = 0",
                            "let mutable total = 0\n          let mutable other = 1"
                        )
                        .Replace(
                            "}}}}]",
                            "}}}},"
                            + secondJson
                            + "]"
                        )

                let suite =
                    read source
                    |> requireOk

                let identity = {
                    Name = "ordered-diagnostics"
                    Version = "1.0.0"
                    Sha256 = String.replicate 64 "2"
                }

                let adapter =
                    LibraryAdapter.create
                        identity
                        (fun _ _ ->
                            Task.FromResult(
                                Ok
                                    {
                                        Success = false
                                        ModelJson = None
                                        Diagnostics = [ second; first ]
                                    }
                            )
                        )

                let report =
                    Runner.run suite adapter CancellationToken.None
                    |> fun task -> task.GetAwaiter().GetResult()

                Expect.isFalse
                    report.Qualified
                    "Reordering diagnostics is not an unnamed normalization"
            )
        testCase
            "Ambiguous expectations and unused step arguments are not silently ignored"
            (fun () ->
                let argument =
                    feature.Replace(
                        "exposing \"Pricing\"\n",
                        "exposing \"Pricing\"\n      \"\"\"json\n      {}\n      \"\"\"\n"
                    )

                match read argument with
                | Ok _ -> failtest "A package step has no docstring argument"
                | Error issues -> Expect.isNonEmpty issues "Unexpected arguments fail linting"

                let success =
                    System.Text.RegularExpressions.Regex.Replace(
                        feature,
                        "(?s)    Then compilation fails with diagnostics:.*?    Examples:",
                        "    Then compilation succeeds with no error diagnostics\n    And the IR equals \"expected/a.json\"\n    And the IR equals \"expected/b.json\"\n    Examples:"
                    )

                match
                    Corpus.read options [
                        artifact "suites/frontend-fsharp/constant.feature" success
                        artifact "suites/frontend-fsharp/expected/a.json" "{\"value\":1}"
                        artifact "suites/frontend-fsharp/expected/b.json" "{\"value\":2}"
                    ]
                with
                | Ok _ -> failtest "A variant has one complete model expectation"
                | Error issues ->
                    Expect.isNonEmpty
                        issues
                        "Multiple incompatible model expectations fail linting"
            )
    ]
