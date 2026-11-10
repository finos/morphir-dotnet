// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// Existing <c>[toolchain.&lt;name&gt;]</c> task adapters in <c>morphir.toml</c> keep their meaning
/// (<c>src/Morphir.Configuration/Schema.fs</c>): the manager never reads them as a host selection and never writes
/// them. The request is the separate file <c>.morphir/toolchain.toml</c>.
/// </summary>
module Morphir.Toolchain.Tests.ConfigurationAdapterTests

open System.IO
open System.Text
open Expecto
open Morphir.Configuration
open Morphir.Configuration.IO
open Morphir.Toolchain
open Morphir.Toolchain.Tests.CliTests

/// A morphir.toml with a task adapter whose members look like a host selection.
let morphirToml =
    """[project]
name = "Acme.Loans"
source_directory = "src"
exposed_modules = ["Loans"]

[toolchain.morphir-elm]
version = "2.90.0"

[toolchain.morphir-elm.acquire]
backend = "npm"
package = "morphir-elm"
version = "2.90.0"

[toolchain.morphir-elm.tasks.make]
exec = "morphir-elm"
args = ["make"]
"""

[<Tests>]
let tests =
    testList "morphir.toml toolchain adapters" [
        testCase "the adapter declaration is valid Morphir configuration" (fun () ->
            let input = ConfigurationReader.ReadText("morphir.toml", morphirToml)

            match input.Content with
            | SourceContent.Available document ->
                let errors = document.Diagnostics |> List.filter (fun d -> d.Severity = DiagnosticSeverity.Error)
                Expect.isEmpty errors "the adapter table is configuration, not a toolchain request"
            | other -> failtestf "the configuration does not read: %A" other)

        testCase "the request reader never takes morphir.toml as a host selection" (fun () ->
            match RequestDocument.read (Encoding.UTF8.GetBytes morphirToml) with
            | Error refusal ->
                Expect.equal (refusal.Code, DocumentPath.render refusal.Path) (ReaderCode.InvalidDocument, "schemaVersion") "not a request"
            | Ok request -> failtestf "morphir.toml was read as a request: %A" request)

        testCase "setup and doctor read .morphir/toolchain.toml and leave morphir.toml unchanged" (fun () ->
            let directory = tempDirectory ()
            let morphir = Path.Combine(directory, "morphir.toml")
            File.WriteAllText(morphir, morphirToml)
            let before = File.ReadAllBytes morphir
            let written = File.GetLastWriteTimeUtc morphir
            let dot = Directory.CreateDirectory(Path.Combine(directory, ".morphir"))
            File.Copy(fixture "request/approved.toml", Path.Combine(dot.FullName, "toolchain.toml"))
            File.Copy(fixture "lock/two-platforms-bound.json", Path.Combine(dot.FullName, "toolchain.lock.json"))
            let sizes = sizesFile (tempDirectory ())

            let plan =
                runIn
                    directory
                    Platform.WinX64
                    [ "setup"
                      "--plan"
                      "--recipe"
                      fixture "recipe/fsharp-synthetic.json"
                      "--repository-id"
                      repositoryText
                      "--artifact-sizes"
                      sizes ]

            Expect.equal plan.Exit 0 $"setup --plan; stderr: {plan.Stderr}"

            match (readResult plan).Plan with
            | Some digested -> Expect.equal digested.Plan.Deployment.Text "native-aot" "the deployment of .morphir/toolchain.toml"
            | None -> failtest "no plan"

            let doctor = runIn directory Platform.WinX64 [ "doctor" ]
            Expect.equal doctor.Exit 0 $"doctor; stdout: {doctor.Stdout}"
            Expect.isFalse (plan.Stderr.Contains "morphir.toml" && not (plan.Stderr.Contains "toolchain.toml")) "morphir.toml is not read"
            Expect.sequenceEqual (File.ReadAllBytes morphir) before "morphir.toml bytes are unchanged"
            Expect.equal (File.GetLastWriteTimeUtc morphir) written "morphir.toml is not rewritten"

            Expect.equal
                (Directory.GetFileSystemEntries directory |> Array.map Path.GetFileName |> Array.sort)
                [| ".morphir"; "morphir.toml" |]
                "nothing else is written to the project")
    ]
