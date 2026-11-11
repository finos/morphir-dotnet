// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The external corpus mode: <c>MORPHIR_TOOLCHAIN_CONTRACT_ROOT</c> names a finos/morphir checkout whose current
/// contract files the corpus tests read in place. A root that is not a corpus stops the run before any test, and an
/// external run prints one marker line first, so a caller can tell that the source it built honoured the variable.
/// </summary>
module Morphir.Toolchain.Tests.CorpusModeTests

open System
open System.Diagnostics
open System.IO
open Expecto
open Morphir.TestSupport

/// A finos/morphir-shaped tree holding a copy of the pin, laid out as the pin script reads a checkout.
let private parentLayoutOfPin (into: string) =
    let copy (from: string) (segments: string list) =
        let target = Path.Combine(Array.ofList (into :: segments))

        for file in Directory.GetFiles(from, "*", SearchOption.AllDirectories) do
            let destination = Path.Combine(target, Path.GetRelativePath(from, file))
            Directory.CreateDirectory(nonNull (Path.GetDirectoryName destination)) |> ignore
            File.Copy(file, destination)

    copy (Path.Combine(Fixtures.pinRoot, "fixtures", "0.1.0-draft.1")) Fixtures.Layout.unsigned
    copy (Path.Combine(Fixtures.pinRoot, "pages")) Fixtures.Layout.pages
    copy (Path.Combine(Fixtures.pinRoot, "schemas")) Fixtures.Layout.schemas
    copy Fixtures.pinnedWorkflow Fixtures.Layout.workflow
    into

type private Child = { Exit: int; Stdout: string; Stderr: string }

/// Runs this test assembly as a child process with the variable set (or removed) and the given arguments.
let private runChild (value: string option) (args: string list) =
    let info = ProcessStartInfo("dotnet", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true)
    info.ArgumentList.Add "exec"
    info.ArgumentList.Add(typeof<Child>.Assembly.Location)

    for arg in args do
        info.ArgumentList.Add arg

    match value with
    | Some value -> info.Environment[Fixtures.externalVariable] <- value
    | None -> info.Environment.Remove Fixtures.externalVariable |> ignore

    use child = new Process(StartInfo = info)
    child.Start() |> ignore
    let stdout = child.StandardOutput.ReadToEndAsync()
    let stderr = child.StandardError.ReadToEndAsync()

    if not (child.WaitForExit(TimeSpan.FromMinutes 3.0)) then
        child.Kill true
        failtest "the child test run did not finish in three minutes"

    { Exit = child.ExitCode; Stdout = stdout.Result; Stderr = stderr.Result }

/// A quick corpus test, run alone by name in the child.
let private oneCorpusTest = "the platform order is the page's platform table"

[<Tests>]
let tests =
    testList "External toolchain corpus mode" [
        testCase "an empty, relative or missing root is refused with the reason" (fun () ->
            use sandbox = TestSandbox.Create "corpus-"
            let refused (value: string) (reason: string) =
                match Fixtures.validateExternal value with
                | Error message -> Expect.stringContains message reason $"'{value}'"
                | Ok root -> failtestf "'%s' was accepted as %s" value root

            refused "" "is set but empty"
            refused "  " "is set but empty"
            refused "relative/morphir" "must be an absolute path"
            refused (Path.Combine(sandbox.Root, "absent")) "is not a directory")

        testCase "a directory that is not a contract corpus is refused, naming what is missing" (fun () ->
            use sandbox = TestSandbox.Create "corpus-"

            match Fixtures.validateExternal sandbox.Work with
            | Error message ->
                Expect.stringContains message "is not a toolchain contract corpus" "the reason"
                Expect.stringContains message "docs/spec/toolchain/fixtures/0.1.0-draft.1/cases.json" "the first missing file"
                Expect.stringContains message "website/static/schemas/toolset-recipe-0.1.0-draft.1.schema.json" "a missing schema"
            | Ok root -> failtestf "accepted %s" root)

        testCase "a checkout with the corpus is accepted as its full path, without a trailing separator" (fun () ->
            use sandbox = TestSandbox.Create "corpus-"
            let parent = parentLayoutOfPin (Path.Combine(sandbox.Work, "morphir"))
            Expect.equal (Fixtures.validateExternal (parent + string Path.DirectorySeparatorChar)) (Ok parent) "the root")

        testCase "a refused root stops the run with exit 2 and no marker" (fun () ->
            use sandbox = TestSandbox.Create "corpus-"
            let child = runChild (Some sandbox.Work) [ "--filter-test-case"; oneCorpusTest ]
            Expect.equal child.Exit 2 $"exit code; stderr: {child.Stderr}"
            Expect.stringContains child.Stderr "is not a toolchain contract corpus" "the reason on stderr"
            Expect.isFalse (child.Stdout.Contains "External toolchain corpus:") "no marker")

        testCase "an external run prints the marker first and runs the corpus tests" (fun () ->
            use sandbox = TestSandbox.Create "corpus-"
            let parent = parentLayoutOfPin (Path.Combine(sandbox.Work, "morphir"))
            let child = runChild (Some parent) [ "--filter-test-case"; oneCorpusTest; "--summary" ]
            Expect.equal child.Exit 0 $"exit code; stdout: {child.Stdout}; stderr: {child.Stderr}"
            let first = child.Stdout.Split('\n').[0].TrimEnd '\r'
            Expect.equal first $"External toolchain corpus: {parent}" "the marker is the first line"
            // Expecto colours its summary, so escape sequences are removed before it is read.
            let plain = Text.RegularExpressions.Regex.Replace(child.Stdout + child.Stderr, "\u001b\\[[0-9;?]*[A-Za-z]", "")
            Expect.isTrue (Text.RegularExpressions.Regex.IsMatch(plain, @"Passed:\s+1\s")) "the one corpus test ran and passed")

        testCase "a pinned run prints no marker" (fun () ->
            let child = runChild None [ "--filter-test-case"; oneCorpusTest ]
            Expect.equal child.Exit 0 $"exit code; stderr: {child.Stderr}"
            Expect.isFalse (child.Stdout.Contains "External toolchain corpus:") "no marker")
    ]
