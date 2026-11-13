// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The refusals of morphirup setup, select and show are local-toolchains 0.1.0-preview.5 results, validated against
/// the published schema pinned in tests/Fixtures/LocalToolchains, with the origin of the route or of the generation
/// the command acted on.
/// </summary>
module Morphir.Toolchain.Tests.RefusalResultTests

open System
open System.IO
open System.Text.Json
open System.Threading
open Expecto
open Json.Schema
open Morphir.Toolchain
open Morphir.TestSupport

let private pinRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "LocalToolchains"))
let private schemaFile = Path.Combine(pinRoot, "0.1.0-preview.5", "result.schema.json")
let private resultSchema = "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.5/result.schema.json"

let private schema =
    lazy (JsonSchema.FromText(SchemaTests.endAnchored (File.ReadAllText schemaFile), BuildOptions(SchemaRegistry = SchemaRegistry())))

/// Validates one result document against the pinned schema and returns its root.
let private validResult (stdout: string) =
    let document = JsonDocument.Parse stdout
    let results = schema.Value.Evaluate(document.RootElement, EvaluationOptions(OutputFormat = OutputFormat.List))
    let failure =
        (match results.Details with null -> [] | details -> List.ofSeq details)
        |> List.tryPick (fun detail ->
            match detail.Errors with
            | NonNull errors when not detail.IsValid && errors.Count > 0 -> Some $"{detail.InstanceLocation}: {Seq.head errors.Values}"
            | _ -> None)
    Expect.isTrue results.IsValid $"the refusal validates against the preview.5 result schema: {defaultArg failure String.Empty}\n{stdout}"
    document.RootElement

let private run platform (sandbox: TestSandbox) args = task {
    use output = new MemoryStream()
    use errors = new StringWriter()
    use input = new MemoryStream()
    let io: Morphirup.Cli.Io = { Out = output; Error = errors; CurrentDirectory = sandbox.Work; Platform = Some platform }
    let! exit = Morphirup.Cli.runAsync io input args CancellationToken.None
    return exit, Text.Encoding.UTF8.GetString(output.ToArray())
}

let private expectRefusal (command: string) (origin: string) (exit: int, stdout: string) =
    Expect.equal exit 1 $"a refusal exits with 1: {stdout}"
    let root = validResult stdout
    Expect.equal (root.GetProperty("$schema").GetString()) resultSchema "the current result family"
    Expect.equal (root.GetProperty("command").GetString()) command "the command"
    Expect.equal (root.GetProperty("status").GetString()) "refused" "refused"
    Expect.equal (root.GetProperty("origin").GetString()) origin "the origin of the route"
    Expect.isNonEmpty (root.GetProperty("diagnostic").GetProperty("code").GetString()) "a diagnostic code"
    root.GetProperty("diagnostic").GetProperty("code").GetString()

let private generation = String('a', 64)

let private pointer origin =
    $"""{{"$schema":"https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.5/selection.schema.json","origin":"{origin}","generationSha256":"{generation}","receiptSha256":"{String('b', 64)}","receiptPath":"toolchains/generations/{generation}/receipt.json"}}"""

[<Tests>]
let tests =
    testList "RefusalResult" [
        test "the pinned result schema has the bytes of finos/morphir" {
            Expect.equal (Sha256.ofBytes(File.ReadAllBytes schemaFile)).Text "6a49c582de9fc4609767356993e5d0276272a06a00584366433dfd2e1b99839f" "PROVENANCE.md"
        }
        testCaseTask "show with no selection refuses with the local-developer origin" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let! result = run Platform.WinX64 sandbox [ "show"; "--home"; Path.Combine(sandbox.Work, "home") ]
            expectRefusal "show" "local-developer" result |> ignore
        })
        testCaseTask "show of a release-checksum selection that cannot be read refuses with its origin" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let home = Path.Combine(sandbox.Work, "home")
            Directory.CreateDirectory(Path.Combine(home, "toolchains")) |> ignore
            File.WriteAllText(Path.Combine(home, "toolchains", "default.json"), pointer "release-checksum")
            let! result = run Platform.WinArm64 sandbox [ "show"; "--home"; home ]
            expectRefusal "show" "release-checksum" result |> ignore
        })
        testCaseTask "select of a release-checksum generation that is not complete refuses with its origin" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let home = Path.Combine(sandbox.Work, "home")
            let folder = Path.Combine(home, "toolchains", "generations", generation)
            Directory.CreateDirectory folder |> ignore
            File.WriteAllBytes(Path.Combine(folder, "candidate.json"), LocalCandidateTests.preview5Candidate "release-checksum")
            let! result = run Platform.WinX64 sandbox [ "select"; "--generation"; generation; "--home"; home ]
            expectRefusal "select" "release-checksum" result |> ignore
        })
        testCaseTask "select of a generation that is not there refuses with the local-developer origin" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let! result = run Platform.WinArm64 sandbox [ "select"; "--generation"; generation; "--home"; Path.Combine(sandbox.Work, "home") ]
            expectRefusal "select" "local-developer" result |> ignore
        })
        testCaseTask "historical local setup refuses with the local-developer origin" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let! result =
                run Platform.WinX64 sandbox
                    [ "setup"; "--candidate"; Path.Combine(sandbox.Work, "missing.json"); "--local-archive"; Path.Combine(sandbox.Work, "missing.zip")
                      "--local-developer"; "--dry-run"; "--home"; Path.Combine(sandbox.Work, "home") ]
            expectRefusal "setup" "local-developer" result |> ignore
        })
        testCaseTask "composed local setup refuses with the local-developer origin" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let candidate = Path.Combine(sandbox.Work, "candidate.json")
            File.WriteAllBytes(candidate, LocalCandidateTests.preview5Candidate "release-checksum")
            let! result =
                run Platform.WinArm64 sandbox
                    [ "setup"; "--candidate"; candidate; "--local-archive"; Path.Combine(sandbox.Work, "host.zip")
                      "--local-bundle"; Path.Combine(sandbox.Work, "bundle.json"); "--local-developer"; "--dry-run"; "--home"; Path.Combine(sandbox.Work, "home") ]
            Expect.equal (expectRefusal "setup" "local-developer" result) "local.origin_mismatch" "a release-checksum candidate is refused on the local route"
        })
        testCaseTask "local setup without consent refuses before it reads anything" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let! result = run Platform.WinX64 sandbox [ "setup"; "--candidate"; "c.json"; "--local-archive"; "a.zip"; "--dry-run"; "--home"; Path.Combine(sandbox.Work, "home") ]
            Expect.equal (expectRefusal "setup" "local-developer" result) "local.consent_required" "consent"
        })
    ]
    |> testLabel "PlatformSpecific"
