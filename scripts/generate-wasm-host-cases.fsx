// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

// Generates the wasm-host suite of the compatibility kit from the pinned WASM host corpus:
// the Gherkin features and the id map in conformance/suites/wasm-host/, from
// tests/Fixtures/MepWasm/wasm-host.cases.json and the commit in its PROVENANCE.md.
//
//   dotnet fsi scripts/generate-wasm-host-cases.fsx          # write the features and the id map
//   dotnet fsi scripts/generate-wasm-host-cases.fsx --check  # exit 1 if a committed file differs
//
// Run it when the pin moves. Never edit a generated feature by hand: an expectation changes
// only when the corpus in finos/morphir changes and the pin moves. The script loads the
// generator's source from src/Morphir.Conformance, so it needs only the .NET SDK;
// wasm-host/Artifacts.fs gives it the two library types it uses.

#load "wasm-host/Artifacts.fs"
#load "../src/Morphir.Conformance/WasmHost/Model.fs"
#load "../src/Morphir.Conformance/WasmHost/CorpusJson.fs"
#load "../src/Morphir.Conformance/WasmHost/CaseIds.fs"
#load "../src/Morphir.Conformance/WasmHost/FeatureWriter.fs"
#load "../src/Morphir.Conformance/WasmHost/Generation.fs"

open System
open System.IO
open Morphir.Conformance
open Morphir.Conformance.WasmHost

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let check = fsi.CommandLineArgs |> Array.contains "--check"
let onGitHubActions = Environment.GetEnvironmentVariable "GITHUB_ACTIONS" = "true"

let report (issue: CorpusIssue) =
    let location =
        if issue.Line > 0 then
            $"{issue.Path}:{issue.Line}"
        else
            issue.Path

    eprintfn $"error: {location}: {issue.Message} ({issue.Code})"
    // A GitHub Actions annotation puts the finding on the file in the pull request.
    if onGitHubActions then
        let line = if issue.Line > 0 then $",line={issue.Line}" else ""
        printfn $"::error file={issue.Path}{line},title=Stale wasm-host features::{issue.Message}"

if check then
    match Generation.checkRepository root with
    | [] -> eprintfn $"ok       {Generation.SuiteDirectory} is what the pinned corpus generates"
    | issues ->
        issues |> List.iter report
        eprintfn $"Run: {Suite.RegenerateCommand}"
        exit 1
else
    match Generation.writeRepository root with
    | Ok files ->
        for file in files do
            eprintfn $"wrote    {file.Path}"
    | Error issues ->
        issues |> List.iter report
        exit 1
