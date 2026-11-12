// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Product-path controls for explicit local-developer setup. Synthetic unit inputs
/// are not the separately owned full-archive executable journey or release qualification.
module Morphir.Toolchain.Tests.LocalCandidateTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Toolchain
open Morphir.TestSupport

let private parsed =
    function
    | Ok value -> value
    | Error error -> failtestf "Bad unit input: %A" error

let private digest =
    Sha256.tryParse (String('a', 64))
    |> parsed

let private manifestDigest =
    Sha256.tryParse (String('b', 64))
    |> parsed

let private token value =
    Token.tryParse value
    |> parsed

let private candidate () : LocalHostCandidate = {
    ManifestSha256 = manifestDigest
    Host = {
        Implementation = token "dotnet"
        Deployment = token "self-contained"
        Version =
            SemVer.tryParse "1.2.3"
            |> parsed
    }
    Platform = Platform.WinX64
    SourceCommit =
        NonBlankText.tryParse (String('c', 40))
        |> parsed
    BuildIdentity =
        NonBlankText.tryParse "synthetic-unit-only"
        |> parsed
    ArchiveSha256 = digest
    ArchiveBytes = 1024UL
    EntryPoint = {
        Target =
            TargetPath.tryParse "morphir.exe"
            |> parsed
        Sha256 = digest
    }
    Files = [
        {
            Artifact = {
                Target =
                    TargetPath.tryParse "morphir.exe"
                    |> parsed
                Sha256 = digest
            }
            Bytes = 32UL
        }
    ]
}

[<Tests>]
let planningTests =
    testList "morphirup local candidate planning" [
        testCase
            "local consent is required even when the exact archive digest is supplied"
            (fun () ->
                Expect.equal
                    (LocalCandidatePlanning.plan Platform.WinX64 None 4096UL (candidate ()))
                    (Error LocalCandidateRefusal.ExplicitConsentRequired)
                    "a checksum never implies publisher authentication or developer consent"
            )

        testCase
            "explicit local provenance retains distinct manifest and archive identities"
            (fun () ->
                match
                    LocalCandidatePlanning.plan
                        Platform.WinX64
                        (Some LocalConsent.LocalDeveloper)
                        4096UL
                        (candidate ())
                with
                | Ok plan ->
                    Expect.equal
                        plan.Origin
                        LocalConsent.LocalDeveloper
                        "local-developer remains explicit"

                    Expect.equal
                        plan.Candidate.ManifestSha256
                        manifestDigest
                        "generation identity uses the original manifest"

                    Expect.equal
                        plan.Candidate.ArchiveSha256
                        digest
                        "archive identity stays separate"

                    Expect.notEqual
                        plan.Candidate.ManifestSha256
                        plan.Candidate.ArchiveSha256
                        "neither identity substitutes for the other"
                | Error error -> failtestf "Explicit local plan refused: %A" error
            )

        testCase
            "platform mismatch refuses rather than installing another architecture"
            (fun () ->
                Expect.equal
                    (LocalCandidatePlanning.plan
                        Platform.WinArm64
                        (Some LocalConsent.LocalDeveloper)
                        4096UL
                        (candidate ()))
                    (Error LocalCandidateRefusal.PlatformMismatch)
                    "no architecture fallback"
            )

        testCase
            "this candidate path refuses an offering other than runtime-bundled managed Morphir"
            (fun () ->
                let original = candidate ()

                for deployment in [ "native-aot"; "framework-dependent"; "dotnet-tool" ] do
                    let other =
                        { original with
                            Host =
                                { original.Host with
                                    Deployment = token deployment
                                }
                        }

                    Expect.equal
                        (LocalCandidatePlanning.plan
                            Platform.WinX64
                            (Some LocalConsent.LocalDeveloper)
                            4096UL
                            other)
                        (Error LocalCandidateRefusal.UnsupportedOffering)
                        "the local milestone does not silently substitute a deployment"
            )

        testCase
            "another implementation cannot be labeled the official managed Morphir candidate"
            (fun () ->
                let original = candidate ()

                let other =
                    { original with
                        Host =
                            { original.Host with
                                Implementation = token "elm"
                            }
                    }

                Expect.equal
                    (LocalCandidatePlanning.plan
                        Platform.WinX64
                        (Some LocalConsent.LocalDeveloper)
                        4096UL
                        other)
                    (Error LocalCandidateRefusal.UnsupportedOffering)
                    "no executable-name inference"
            )

        testCase
            "empty and past-bound archives are refused before staging"
            (fun () ->
                let original = candidate ()

                for length in [ 0UL; 4097UL; UInt64.MaxValue ] do
                    Expect.equal
                        (LocalCandidatePlanning.plan
                            Platform.WinX64
                            (Some LocalConsent.LocalDeveloper)
                            4096UL
                            { original with ArchiveBytes = length })
                        (Error LocalCandidateRefusal.ArchiveBound)
                        "the recorded caller bound precedes allocation and copy"
            )

        testCase
            "a nested official entry point retains its portable path"
            (fun () ->
                let entry = {
                    Target =
                        TargetPath.tryParse "official-folder/morphir.exe"
                        |> parsed
                    Sha256 = digest
                }

                let value =
                    { candidate () with
                        EntryPoint = entry
                        Files = [ { Artifact = entry; Bytes = 32UL } ]
                    }

                match
                    LocalCandidatePlanning.plan
                        Platform.WinX64
                        (Some LocalConsent.LocalDeveloper)
                        4096UL
                        value
                with
                | Ok plan ->
                    Expect.equal
                        plan.Candidate.EntryPoint
                        value.EntryPoint
                        "the complete folder root is not flattened"
                | Error error -> failtestf "Portable entry point refused: %A" error
            )

        testCase
            "an entry point outside the declared complete closure is refused"
            (fun () ->
                Expect.equal
                    (LocalCandidatePlanning.plan
                        Platform.WinX64
                        (Some LocalConsent.LocalDeveloper)
                        4096UL
                        { candidate () with Files = [] })
                    (Error LocalCandidateRefusal.ClosureMismatch)
                    "an executable digest alone is not a full-folder candidate"
            )

        testCase
            "duplicate and case-aliased declared files cannot become an installed Windows closure"
            (fun () ->
                let original = candidate ()

                let alias =
                    { original.Files.Head with
                        Artifact =
                            { original.EntryPoint with
                                Target =
                                    TargetPath.tryParse "MORPHIR.EXE"
                                    |> parsed
                            }
                    }

                for files in
                    [
                        original.Files
                        @ original.Files
                        original.Files
                        @ [ alias ]
                    ] do
                    Expect.equal
                        (LocalCandidatePlanning.plan
                            Platform.WinX64
                            (Some LocalConsent.LocalDeveloper)
                            4096UL
                            { original with Files = files })
                        (Error LocalCandidateRefusal.ClosureMismatch)
                        "no duplicate target admission"
            )
    ]

let private runLocalPlatform platform (sandbox: TestSandbox) args = task {
    use output = new MemoryStream()
    use errors = new StringWriter()
    use input = new MemoryStream()

    let io: Morphirup.Cli.Io = {
        Out = output
        Error = errors
        CurrentDirectory = sandbox.Work
        Platform = Some platform
    }

    let! exit = Morphirup.Cli.runAsync io input args CancellationToken.None
    return exit, Text.Encoding.UTF8.GetString(output.ToArray()), errors.ToString()
}

let private runLocal sandbox args =
    runLocalPlatform Platform.WinX64 sandbox args

let private candidateJson files =
    let file path =
        Value.Object [
            "path", Value.String path
            "sha256", Value.String digest.Text
            "bytes", Value.Number "32"
        ]

    Value.Object [
        "$schema", Value.String(LocalCandidateDocument.schema "candidate")
        "origin", Value.String "local-developer"
        "implementation", Value.String "dotnet"
        "deployment", Value.String "self-contained"
        "platform", Value.String "win-x64"
        "version", Value.String "1.2.3"
        "sourceRepository", Value.String "https://github.com/finos/morphir-dotnet"
        "sourceCommit", Value.String(String('c', 40))
        "buildIdentity", Value.String "synthetic-unit-only"
        "archive",
        Value.Object [
            "format", Value.String "zip"
            "sha256", Value.String digest.Text
            "bytes", Value.Number "1024"
        ]
        "entryPoint",
        Value.Object [ "path", Value.String "morphir.exe"; "sha256", Value.String digest.Text ]
        "files", Value.Array(List.map file files)
    ]
    |> JsonText.toBytes

[<Tests>]
let readerTests =
    testList "morphirup local candidate reader controls" [
        testCase
            "raw bytes are the candidate identity and metadata does not become publisher authority"
            (fun () ->
                let raw = candidateJson [ "morphir.exe" ]

                let candidate =
                    LocalCandidateDocument.readCandidate raw
                    |> parsed

                Expect.equal
                    candidate.ManifestSha256
                    (Sha256.ofBytes raw)
                    "no re-encoding for identity"
            )
        testCase
            "empty full file inventory is refused by the reader"
            (fun () ->
                match
                    candidateJson []
                    |> LocalCandidateDocument.readCandidate
                with
                | Error refusal ->
                    Expect.equal refusal.Code ReaderCode.InvalidValue "typed count refusal"
                | Ok _ -> failtest "Empty whole-folder inventory was accepted"
            )
        testCase
            "case aliased inventory is refused by the reader"
            (fun () ->
                match
                    candidateJson [ "morphir.exe"; "MORPHIR.EXE" ]
                    |> LocalCandidateDocument.readCandidate
                with
                | Error refusal ->
                    Expect.equal
                        refusal.Code
                        ReaderCode.InconsistentDocument
                        "typed duplicate refusal"
                | Ok _ -> failtest "A Windows case alias was accepted"
            )
        testCase
            "local ASCII path restriction applies to every inventory file"
            (fun () ->
                match
                    candidateJson [ "morphir.exe"; "_not-portable-by-this-schema.dll" ]
                    |> LocalCandidateDocument.readCandidate
                with
                | Error refusal ->
                    Expect.equal
                        refusal.Code
                        ReaderCode.InvalidValue
                        "same lexical rule as the root schema"
                | Ok _ -> failtest "A path forbidden by the local schema was accepted"
            )
        testCase
            "historical family cannot be relabeled as a local candidate"
            (fun () ->
                let text = Text.Encoding.UTF8.GetString(candidateJson [ "morphir.exe" ])

                let raw =
                    Text.Encoding.UTF8.GetBytes(
                        text.Replace(
                            LocalCandidateDocument.schema "candidate",
                            "https://morphir.finos.org/schemas/toolchains/preview1/candidate.schema.json"
                        )
                    )

                match LocalCandidateDocument.readCandidate raw with
                | Error refusal ->
                    Expect.equal
                        refusal.Code
                        ReaderCode.UnsupportedContractVersion
                        "no family conversion"
                | Ok _ -> failtest "Another family was accepted"
            )
    ]

let private syntheticArchive (sandbox: TestSandbox) (files: (string * byte array) list) =
    let archive = Path.Combine(sandbox.Work, "local.zip")

    do
        use output = File.Create archive
        use zip = new Compression.ZipArchive(output, Compression.ZipArchiveMode.Create)

        for path, bytes in files do
            use entry = zip.CreateEntry(path).Open()
            entry.Write bytes

    let fileValues =
        files
        |> List.map (fun (path, bytes) ->
            Value.Object [
                "path", Value.String path
                "sha256", Value.String((Sha256.ofBytes bytes).Text)
                "bytes", Value.Number(string bytes.Length)
            ]
        )

    let original =
        candidateJson [ "morphir.exe" ]
        |> Syntax.parseJson
        |> parsed

    let properties =
        match original with
        | Value.Object properties -> properties
        | _ -> failtest "Invalid synthetic input"

    let archiveBytes = File.ReadAllBytes archive

    let values =
        properties
        |> List.map (fun (name, value) ->
            name,
            match name with
            | "archive" ->
                Value.Object [
                    "format", Value.String "zip"
                    "sha256", Value.String((Sha256.ofBytes archiveBytes).Text)
                    "bytes", Value.Number(string archiveBytes.Length)
                ]
            | "files" -> Value.Array fileValues
            | "entryPoint" ->
                Value.Object [
                    "path", Value.String "morphir.exe"
                    "sha256",
                    Value.String(
                        (files
                         |> List.find (fun (name, _) -> name = "morphir.exe")
                         |> snd
                         |> Sha256.ofBytes)
                            .Text
                    )
                ]
            | _ -> value
        )

    let manifest = Path.Combine(sandbox.Work, "candidate.json")
    File.WriteAllBytes(manifest, JsonText.toBytes (Value.Object values))
    manifest, archive

[<Tests>]
let archiveControls =
    testList "morphirup local candidate archive controls" [
        testCaseTask
            "checksum-valid declared win-x64 closure refuses a non-AMD64 executable before mutation"
            (fun () -> task {
                use sandbox = TestSandbox.Create()

                let bytes text =
                    Text.Encoding.UTF8.GetBytes(text: string)

                let names = [
                    "morphir.exe"
                    "morphir.dll"
                    "hostfxr.dll"
                    "hostpolicy.dll"
                    "coreclr.dll"
                    "System.Private.CoreLib.dll"
                    "FSharp.Core.dll"
                    "FSharp.Compiler.Service.dll"
                    "Morphir.CodeGeneration.FSharp.dll"
                ]

                let files =
                    (names
                     |> List.map (fun name -> name, Array.zeroCreate<byte> 64))
                    @ [
                        "morphir.runtimeconfig.json",
                        bytes
                            "{\"runtimeOptions\":{\"includedFrameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}]}}"
                        "morphir.deps.json",
                        bytes
                            "{\"runtimeTarget\":{\"name\":\".NETCoreApp,Version=v10.0/win-x64\"}}"
                    ]

                let manifest, archive = syntheticArchive sandbox files

                let! exit, output, _ =
                    runLocal sandbox [
                        "setup"
                        "--candidate"
                        manifest
                        "--local-archive"
                        archive
                        "--home"
                        sandbox.MorphirHome
                        "--local-developer"
                        "--dry-run"
                    ]

                Expect.equal exit 1 "integrity alone is not executable/platform validation"

                Expect.stringContains
                    output
                    "local.platform_mismatch"
                    "actual executable format/architecture refusal"

                Expect.isFalse
                    (Directory.Exists sandbox.MorphirHome)
                    "no candidate code, locks or state on refusal"
            })
    ]

[<Tests>]
let commandRefusalTests =
    testList "morphirup local candidate product refusals" [
        testCaseTask
            "shared sibling documents cannot provide selection authority"
            (fun () -> task {
                use sandbox = TestSandbox.Create()
                let opaque = Path.Combine(sandbox.MorphirHome, "catalog", "toolchains")

                Directory.CreateDirectory opaque
                |> ignore

                let fake = Path.Combine(opaque, "default.json")
                File.WriteAllText(fake, "not selected authority")
                let! exit, output, _ = runLocal sandbox [ "show"; "--home"; sandbox.MorphirHome ]

                Expect.equal
                    exit
                    1
                    "a shared catalog cannot substitute for the manager-owned pointer"

                Expect.stringContains
                    output
                    "local.io_refused"
                    "only the absent owned selection was read"

                Expect.equal
                    (File.ReadAllText fake)
                    "not selected authority"
                    "opaque sibling stays untouched"

                Expect.isFalse
                    (Directory.Exists(Path.Combine(sandbox.MorphirHome, "toolchains")))
                    "read-only refusal creates no owned state"
            })
        testCaseTask
            "native ARM64 local execution is refused at the actual command boundary"
            (fun () -> task {
                use sandbox = TestSandbox.Create()

                let! exit, output, _ =
                    runLocalPlatform Platform.WinArm64 sandbox [
                        "select"
                        "--generation"
                        manifestDigest.Text
                        "--home"
                        sandbox.MorphirHome
                    ]

                Expect.equal exit 1 "no implicit cross-target policy"

                Expect.stringContains
                    output
                    "local.platform_mismatch"
                    "platform refusal precedes state I/O"

                Expect.isFalse
                    (Directory.Exists sandbox.MorphirHome)
                    "no state on platform refusal"
            })
        testCaseTask
            "show on an empty explicit isolated Home cannot fabricate an installed selection"
            (fun () -> task {
                use sandbox = TestSandbox.Create()
                let! exit, _, _ = runLocal sandbox [ "show"; "--home"; sandbox.MorphirHome ]

                Expect.equal
                    exit
                    1
                    "missing installed selection is a product refusal, not a successful candidate receipt"

                Expect.isFalse
                    (Directory.Exists sandbox.MorphirHome)
                    "read-only show never provisions Home"
            })

        testCaseTask
            "a dry-run selecting an absent generation creates no lock or Home"
            (fun () -> task {
                use sandbox = TestSandbox.Create()

                let! exit, _, _ =
                    runLocal sandbox [
                        "use"
                        "--generation"
                        manifestDigest.Text
                        "--home"
                        sandbox.MorphirHome
                        "--dry-run"
                    ]

                Expect.equal exit 1 "dry-run uses real installed-generation validation"

                Expect.isFalse
                    (Directory.Exists sandbox.MorphirHome)
                    "no selection, staging, journal or lease creation"
            })
    ]

[<Tests>]
let executionPlatformTests =
    testList "morphirup local candidate execution platform" [
        testCase
            "Windows ARM64 OS and x64 process execute the declared win-x64 local candidate"
            (fun () ->
                Expect.equal
                    (Morphirup.Cli.executionPlatform
                        Runtime.InteropServices.OSPlatform.Windows
                        Runtime.InteropServices.Architecture.X64
                        (Some Platform.WinArm64)
                        [ "setup"; "--candidate"; "candidate.json" ])
                    (Some Platform.WinX64)
                    "actual process architecture, not a candidate fallback"
            )
        testCase
            "native ARM64 manager retains platform mismatch for a win-x64 candidate"
            (fun () ->
                Expect.equal
                    (Morphirup.Cli.executionPlatform
                        Runtime.InteropServices.OSPlatform.Windows
                        Runtime.InteropServices.Architecture.Arm64
                        (Some Platform.WinArm64)
                        [ "select" ])
                    (Some Platform.WinArm64)
                    "no blanket Windows override"
            )
        testCase
            "historical recipe setup and doctor retain their supplied platform"
            (fun () ->
                for args in [ [ "setup"; "--recipe"; "r.json" ]; [ "doctor" ] ] do
                    Expect.equal
                        (Morphirup.Cli.executionPlatform
                            Runtime.InteropServices.OSPlatform.Windows
                            Runtime.InteropServices.Architecture.X64
                            (Some Platform.WinArm64)
                            args)
                        (Some Platform.WinArm64)
                        "historical OS platform unchanged"
            )
    ]

let private installSelected (sandbox: TestSandbox) (candidatePath: string) (archivePath: string) = task {
    let candidate =
        File.ReadAllBytes candidatePath
        |> LocalCandidateDocument.readCandidate
        |> parsed

    let! installed, output, error =
        runLocal sandbox [
            "setup"
            "--candidate"
            candidatePath
            "--local-archive"
            archivePath
            "--home"
            sandbox.MorphirHome
            "--local-developer"
            "--yes"
        ]

    Expect.equal
        installed
        0
        (output
         + error)

    let! selected, output, error =
        runLocal sandbox [
            "select"
            "--generation"
            candidate.ManifestSha256.Text
            "--home"
            sandbox.MorphirHome
        ]

    Expect.equal
        selected
        0
        (output
         + error)

    return candidate
}

// Explicit opt-in external candidate integration. The separately owned native executable journey remains mandatory.
[<Tests>]
let suppliedCandidateTests =
    match
        Environment.GetEnvironmentVariable "MORPHIR_LOCAL_TEST_CANDIDATE",
        Environment.GetEnvironmentVariable "MORPHIR_LOCAL_TEST_ARCHIVE"
    with
    | null, null -> testList "morphirup supplied local candidate integration (not configured)" []
    | candidatePath, archivePath ->
        testList "morphirup supplied local candidate integration" [
            for cancel in [ true; false ] do
                testCaseTask
                    (sprintf
                        "interrupted prepared selection preserves usable prior state cancel=%b"
                        cancel)
                    (fun () -> task {
                        let candidatePath =
                            candidatePath
                            |> Option.ofObj
                            |> Option.get

                        let archivePath =
                            archivePath
                            |> Option.ofObj
                            |> Option.get

                        use sandbox = TestSandbox.Create()
                        let! candidate = installSelected sandbox candidatePath archivePath

                        let pointer =
                            Path.Combine(sandbox.MorphirHome, "toolchains", "default.json")

                        let before = File.ReadAllBytes pointer
                        use cancellation = new CancellationTokenSource()
                        let injected = IOException "selection preparation failed"
                        let mutable prepared = false
                        let mutable observed: exn option = None

                        try
                            let! _ =
                                Morphirup.LocalInstallation.selectWithPreparedAsync
                                    sandbox.MorphirHome
                                    candidate.ManifestSha256
                                    false
                                    (fun () ->
                                        prepared <- true
                                        if cancel then cancellation.Cancel() else raise injected
                                    )
                                    cancellation.Token

                            failtest "Interrupted selection unexpectedly committed"
                        with error ->
                            observed <- Some error

                        Expect.isTrue
                            prepared
                            "the actual selection bytes were prepared before interruption"

                        match observed with
                        | Some(:? OperationCanceledException) when cancel -> ()
                        | Some error when
                            not cancel
                            && obj.ReferenceEquals(error, injected)
                            ->
                            ()
                        | _ -> failtestf "Wrong primary interruption: %A" observed

                        Expect.equal
                            (File.ReadAllBytes pointer)
                            before
                            "prior pointer stays intact"

                        for command in
                            [
                                [ "show"; "--home"; sandbox.MorphirHome ]
                                [
                                    "select"
                                    "--generation"
                                    candidate.ManifestSha256.Text
                                    "--home"
                                    sandbox.MorphirHome
                                    "--dry-run"
                                ]
                                [ "run"; "--home"; sandbox.MorphirHome; "--"; "--version" ]
                                [
                                    "select"
                                    "--generation"
                                    candidate.ManifestSha256.Text
                                    "--home"
                                    sandbox.MorphirHome
                                ]
                            ] do
                            let! exit, output, error = runLocal sandbox command

                            Expect.equal
                                exit
                                0
                                ("Prepared private state must not poison "
                                 + command.Head
                                 + ": "
                                 + output
                                 + error)

                        Expect.equal
                            (File.ReadAllBytes pointer)
                            before
                            "later successful selection keeps exact identity"
                    })
            testCaseTask
                "later commands preserve host locks and ignore shared Home siblings without adopting them"
                (fun () -> task {
                    let candidatePath =
                        candidatePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Candidate path required")

                    let archivePath =
                        archivePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Archive path required")

                    use sandbox = TestSandbox.Create()
                    let! candidate = installSelected sandbox candidatePath archivePath
                    let pointer = Path.Combine(sandbox.MorphirHome, "toolchains", "default.json")

                    let receipt =
                        Path.Combine(
                            sandbox.MorphirHome,
                            (LocalCandidateDocument.receiptPath candidate.ManifestSha256)
                                .Replace('/', Path.DirectorySeparatorChar)
                        )

                    let pointerBefore, receiptBefore =
                        File.ReadAllBytes pointer, File.ReadAllBytes receipt

                    let siblings = [
                        "locks/install/0123456789abcdef0123456789abcdef.lock", ""
                        "morphir.toml", "# shared global configuration\n"
                        "catalog/other-owner.txt", "opaque catalog sibling"
                        "store/extensions/other-owner.txt", "opaque shared store sibling"
                        "tmp/host-job/other-owner.txt", "opaque other temporary namespace"
                    ]

                    for relative, bytes in siblings do
                        let path =
                            Path.Combine(
                                sandbox.MorphirHome,
                                relative.Replace('/', Path.DirectorySeparatorChar)
                            )

                        Directory.CreateDirectory(
                            Path.GetDirectoryName path
                            |> Option.ofObj
                            |> Option.defaultWith (fun () -> failtest "Parent required")
                        )
                        |> ignore

                        File.WriteAllText(path, bytes)

                    let outside = Path.Combine(sandbox.Work, "other-owner")

                    Directory.CreateDirectory outside
                    |> ignore

                    File.WriteAllText(
                        Path.Combine(outside, "invalid-owned-state.txt"),
                        "do not interpret or traverse"
                    )

                    for relative in
                        [ "other-owner-link"; "store/other-owner-link"; "tmp/other-owner-link" ] do
                        Directory.CreateSymbolicLink(
                            Path.Combine(
                                sandbox.MorphirHome,
                                relative.Replace('/', Path.DirectorySeparatorChar)
                            ),
                            outside
                        )
                        |> ignore

                    for args in
                        [
                            [ "show"; "--home"; sandbox.MorphirHome; "--explain"; "--json" ]
                            [
                                "select"
                                "--generation"
                                candidate.ManifestSha256.Text
                                "--home"
                                sandbox.MorphirHome
                                "--dry-run"
                            ]
                            [
                                "select"
                                "--generation"
                                candidate.ManifestSha256.Text
                                "--home"
                                sandbox.MorphirHome
                            ]
                            [ "run"; "--home"; sandbox.MorphirHome; "--"; "--version" ]
                        ] do
                        let! exit, output, error = runLocal sandbox args

                        Expect.equal
                            exit
                            0
                            ("Shared Home must coexist for "
                             + args.Head
                             + ": "
                             + output
                             + error)

                    Expect.equal
                        (File.ReadAllBytes pointer)
                        pointerBefore
                        "shared siblings never replace selected authority"

                    Expect.equal
                        (File.ReadAllBytes receipt)
                        receiptBefore
                        "receipt bytes remain immutable"

                    for relative, bytes in siblings do
                        Expect.equal
                            (File.ReadAllText(
                                Path.Combine(
                                    sandbox.MorphirHome,
                                    relative.Replace('/', Path.DirectorySeparatorChar)
                                )
                            ))
                            bytes
                            "unknown siblings are not modified"

                    Expect.equal
                        (File.ReadAllText(Path.Combine(outside, "invalid-owned-state.txt")))
                        "do not interpret or traverse"
                        "unknown reparse target stays opaque"

                    let stagesBefore =
                        Directory.GetDirectories(
                            Path.Combine(sandbox.MorphirHome, "tmp"),
                            "local-toolchain-*"
                        )
                        |> Array.sort

                    for mode in [ "--dry-run"; "--yes" ] do
                        let! setupRefusal, output, _ =
                            runLocal sandbox [
                                "setup"
                                "--candidate"
                                candidatePath
                                "--local-archive"
                                archivePath
                                "--home"
                                sandbox.MorphirHome
                                "--local-developer"
                                mode
                            ]

                        Expect.equal
                            setupRefusal
                            1
                            "setup admission stays strict even after installation"

                        Expect.stringContains
                            output
                            "local.foreign_home"
                            "phase distinction does not weaken admission"

                        Expect.equal
                            (File.ReadAllBytes pointer)
                            pointerBefore
                            "refused setup preserves default"

                        Expect.equal
                            (File.ReadAllBytes receipt)
                            receiptBefore
                            "refused setup preserves immutable receipt"

                        Expect.equal
                            (Directory.GetDirectories(
                                Path.Combine(sandbox.MorphirHome, "tmp"),
                                "local-toolchain-*"
                             )
                             |> Array.sort)
                            stagesBefore
                            "refused setup creates no private stage"
                })
            testCaseTask
                "later commands still refuse invalid foreign and reparse entries inside manager-owned partitions"
                (fun () -> task {
                    let candidatePath =
                        candidatePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Candidate path required")

                    let archivePath =
                        archivePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Archive path required")

                    use sandbox = TestSandbox.Create()
                    let! candidate = installSelected sandbox candidatePath archivePath
                    let pointer = Path.Combine(sandbox.MorphirHome, "toolchains", "default.json")
                    let before = File.ReadAllBytes pointer

                    let commands = [
                        [ "show"; "--home"; sandbox.MorphirHome ]
                        [
                            "select"
                            "--generation"
                            candidate.ManifestSha256.Text
                            "--home"
                            sandbox.MorphirHome
                            "--dry-run"
                        ]
                        [ "run"; "--home"; sandbox.MorphirHome; "--"; "--version" ]
                    ]

                    let refused expected = task {
                        for args in commands do
                            let! exit, output, error = runLocal sandbox args

                            Expect.equal
                                exit
                                1
                                ("Invalid owned state refused for "
                                 + args.Head)

                            Expect.stringContains
                                (output
                                 + error)
                                expected
                                "the intended owned-state check executes"

                            Expect.equal
                                (File.ReadAllBytes pointer)
                                before
                                "refusal cannot select or alter default"
                    }

                    for relative in
                        [
                            "store/tools/alien"
                            "toolchains/alien"
                            "tmp/local-toolchain-negative/alien"
                        ] do
                        let path =
                            Path.Combine(
                                sandbox.MorphirHome,
                                relative.Replace('/', Path.DirectorySeparatorChar)
                            )

                        Directory.CreateDirectory(
                            Path.GetDirectoryName path
                            |> Option.ofObj
                            |> Option.defaultWith (fun () -> failtest "Parent required")
                        )
                        |> ignore

                        File.WriteAllText(path, "foreign bytes")
                        do! refused "local.foreign_home"

                        Expect.equal
                            (File.ReadAllText path)
                            "foreign bytes"
                            "foreign bytes are preserved"

                        File.Delete path

                    for relative in
                        [
                            "store/tools/sha256/not-a-digest"
                            "toolchains/generations/not-a-digest"
                        ] do
                        let path =
                            Path.Combine(
                                sandbox.MorphirHome,
                                relative.Replace('/', Path.DirectorySeparatorChar)
                            )

                        Directory.CreateDirectory path
                        |> ignore

                        do! refused "local.foreign_home"
                        Directory.Delete path

                    let outside = Path.Combine(sandbox.Work, "external-reparse-target")

                    Directory.CreateDirectory outside
                    |> ignore

                    File.WriteAllText(Path.Combine(outside, "marker"), "unchanged")

                    for relative in
                        [
                            "store/tools/sha256/"
                            + String('d', 64)
                            "toolchains/generations/"
                            + String('e', 64)
                            "tmp/local-toolchain-link"
                        ] do
                        let path =
                            Path.Combine(
                                sandbox.MorphirHome,
                                relative.Replace('/', Path.DirectorySeparatorChar)
                            )

                        Directory.CreateSymbolicLink(path, outside)
                        |> ignore

                        do! refused "path_escape"
                        Directory.Delete path

                    Expect.equal
                        (File.ReadAllText(Path.Combine(outside, "marker")))
                        "unchanged"
                        "owned reparse targets are not touched"

                    let! restored, output, error =
                        runLocal sandbox [ "show"; "--home"; sandbox.MorphirHome ]

                    Expect.equal
                        restored
                        0
                        ("positive control after removing only test-created foreign entries: "
                         + output
                         + error)
                })
            testCaseTask
                "real full-folder setup selects compiles and generates with retained host locks"
                (fun () -> task {
                    let candidatePath =
                        candidatePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Candidate path required")

                    let archivePath =
                        archivePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Archive path required")

                    use sandbox = TestSandbox.Create()
                    let raw = File.ReadAllBytes candidatePath

                    let candidate =
                        LocalCandidateDocument.readCandidate raw
                        |> parsed

                    let setup mode = [
                        "setup"
                        "--candidate"
                        candidatePath
                        "--local-archive"
                        archivePath
                        "--home"
                        sandbox.MorphirHome
                        "--local-developer"
                        mode
                    ]

                    let! noConsent, _, _ =
                        runLocal
                            sandbox
                            (setup "--yes"
                             |> List.filter ((<>) "--local-developer"))

                    Expect.equal noConsent 1 "digest validity does not confer developer consent"

                    Expect.isFalse
                        (Directory.Exists sandbox.MorphirHome)
                        "consent refusal makes no state"

                    let! conflictingFlags, _, _ =
                        runLocal
                            sandbox
                            (setup "--yes"
                             @ [ "--dry-run" ])

                    Expect.equal conflictingFlags 2 "dry-run and yes cannot be combined"

                    Expect.isFalse
                        (Directory.Exists sandbox.MorphirHome)
                        "usage refusal makes no state"

                    let! planned, _, _ = runLocal sandbox (setup "--dry-run")
                    Expect.equal planned 0 "complete official archive validates"
                    Expect.isFalse (Directory.Exists sandbox.MorphirHome) "dry-run creates no Home"
                    let! installed, output, error = runLocal sandbox (setup "--yes")

                    Expect.equal
                        installed
                        0
                        ("actual installed --version probe must succeed: "
                         + output
                         + error)

                    let pointer = Path.Combine(sandbox.MorphirHome, "toolchains", "default.json")
                    Expect.isFalse (File.Exists pointer) "setup does not select implicitly"

                    let candidateFile =
                        Path.Combine(
                            sandbox.MorphirHome,
                            (LocalCandidateDocument.generationPath candidate.ManifestSha256)
                                .Replace('/', Path.DirectorySeparatorChar),
                            "candidate.json"
                        )

                    Expect.equal
                        (File.ReadAllBytes candidateFile)
                        raw
                        "exact raw candidate is retained"

                    let select = [
                        "select"
                        "--generation"
                        candidate.ManifestSha256.Text
                        "--home"
                        sandbox.MorphirHome
                    ]

                    let! selectionPlan, _, _ =
                        runLocal
                            sandbox
                            (select
                             @ [ "--dry-run" ])

                    Expect.equal selectionPlan 0 "selection validates the installed closure"
                    Expect.isFalse (File.Exists pointer) "selection dry-run publishes no pointer"
                    let! selected, output, error = runLocal sandbox select

                    Expect.equal
                        selected
                        0
                        (output
                         + error)

                    let before = File.ReadAllBytes pointer

                    let! shown, output, error =
                        runLocal sandbox [
                            "show"
                            "--home"
                            sandbox.MorphirHome
                            "--explain"
                            "--json"
                        ]

                    Expect.equal
                        shown
                        0
                        (output
                         + error)

                    let! exit, output, error =
                        runLocal sandbox [
                            "run"
                            "--home"
                            sandbox.MorphirHome
                            "--"
                            "--version"
                        ]

                    Expect.equal exit 0 error

                    Expect.equal
                        (output.TrimEnd('\r', '\n'))
                        candidate.Host.Version.Text
                        "real child version, no manager JSON mixed in"

                    Expect.equal (File.ReadAllBytes pointer) before "run preserves selection bytes"
                    // Exercise the real product sequence that --version alone did not cover.
                    // Compile retains its existing target install lock in the shared Morphir Home.
                    let fixture =
                        Path.GetFullPath(
                            Path.Combine(
                                __SOURCE_DIRECTORY__,
                                "..",
                                "Fixtures",
                                "Toolchain",
                                "workflows",
                                "fsharp-compile-generate"
                            )
                        )

                    File.Copy(
                        Path.Combine(fixture, "source", "Pricing.fs"),
                        Path.Combine(sandbox.Work, "Pricing.fs")
                    )

                    let host args =
                        runLocal
                            sandbox
                            ([ "run"; "--home"; sandbox.MorphirHome; "--" ]
                             @ args)

                    let! compiled, output, error =
                        host [
                            "compile"
                            "--language"
                            "fsharp"
                            "--input"
                            "Pricing.fs"
                            "--package-name"
                            "acme/demo"
                            "--ir-version"
                            "4"
                            "--output"
                            "compiled"
                            "--out-dir"
                            "out"
                            "--json"
                        ]

                    Expect.equal
                        compiled
                        0
                        ("actual compile: "
                         + output
                         + error)

                    let actualIr =
                        File.ReadAllText(Path.Combine(sandbox.Work, "compiled", "morphir-ir.json"))
                        |> Text.Json.Nodes.JsonNode.Parse

                    let fixedIr =
                        File.ReadAllText(Path.Combine(fixture, "expected", "ir-4.0.0.json"))
                        |> Text.Json.Nodes.JsonNode.Parse

                    Expect.isTrue
                        (Text.Json.Nodes.JsonNode.DeepEquals(actualIr, fixedIr))
                        "fixed IR is unchanged"

                    let locks =
                        Directory.GetFiles(
                            Path.Combine(sandbox.MorphirHome, "locks", "install"),
                            "*.lock"
                        )

                    Expect.isGreaterThan
                        locks.Length
                        0
                        "actual compile retained its stable target lock"

                    for path in locks do
                        Expect.equal (FileInfo(path).Length) 0L "existing host lock is empty"

                    let! generated, output, error =
                        host [
                            "generate"
                            "--target"
                            "fsharp"
                            "--input"
                            "compiled/morphir-ir.json"
                            "--output"
                            "generated"
                            "--option"
                            "format=false"
                            "--json"
                        ]

                    Expect.equal
                        generated
                        0
                        ("generate after successful compile: "
                         + output
                         + error)

                    let generatedBytes =
                        File.ReadAllBytes(Path.Combine(sandbox.Work, "generated", "Pricing.fs"))

                    Expect.equal
                        generatedBytes
                        (File.ReadAllBytes(
                            Path.Combine(fixture, "expected", "generated", "Pricing.fs")
                        ))
                        "fixed generated F# is unchanged"

                    Expect.equal generatedBytes.Length 112 "fixed generated byte count"

                    Expect.equal
                        (File.ReadAllBytes pointer)
                        before
                        "compile and generate preserve selected identity"
                    // An existing object is reusable only while its complete inventory still matches.
                    let folder =
                        Path.Combine(
                            sandbox.MorphirHome,
                            (LocalCandidateDocument.installationPath candidate)
                                .Replace('/', Path.DirectorySeparatorChar)
                        )

                    let victim =
                        candidate.Files
                        |> List.find (fun file ->
                            file.Artifact.Target
                            <> candidate.EntryPoint.Target
                            && file.Bytes > 0UL
                        )

                    let victimPath =
                        Path.Combine(
                            folder,
                            victim.Artifact.Target.Text.Replace('/', Path.DirectorySeparatorChar)
                        )

                    let corrupted = File.ReadAllBytes victimPath

                    corrupted[0] <-
                        corrupted[0]
                        ^^^ 1uy

                    File.WriteAllBytes(victimPath, corrupted)
                    let! refused, _, _ = runLocal sandbox select

                    Expect.equal
                        refused
                        1
                        "selection checks every file digest, including files other than the executable"

                    Expect.equal
                        (File.ReadAllBytes pointer)
                        before
                        "refused selection never changes the prior pointer"
                })
            testCaseTask
                "wrong archive digest and declared length refuse before Home or receipt publication"
                (fun () -> task {
                    let candidatePath =
                        candidatePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Candidate path required")

                    let archivePath =
                        archivePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Archive path required")

                    use sandbox = TestSandbox.Create()
                    let original = File.ReadAllText candidatePath

                    let candidate =
                        File.ReadAllBytes candidatePath
                        |> LocalCandidateDocument.readCandidate
                        |> parsed

                    let variants = [
                        "digest",
                        original.Replace(candidate.ArchiveSha256.Text, String('a', 64)),
                        "local.digest_mismatch"
                        "length",
                        original.Replace(
                            string candidate.ArchiveBytes,
                            string (
                                candidate.ArchiveBytes
                                - 1UL
                            )
                        ),
                        "local.length_mismatch"
                    ]

                    for name, text, expected in variants do
                        let file =
                            Path.Combine(
                                sandbox.Work,
                                name
                                + ".json"
                            )

                        File.WriteAllText(file, text, Text.UTF8Encoding(false))

                        let! exit, output, _ =
                            runLocal sandbox [
                                "setup"
                                "--candidate"
                                file
                                "--local-archive"
                                archivePath
                                "--home"
                                sandbox.MorphirHome
                                "--local-developer"
                                "--yes"
                            ]

                        Expect.equal exit 1 "exact integrity refusal"

                        Expect.stringContains
                            output
                            expected
                            "the refusal occurred in the intended mechanism"

                        Expect.isFalse
                            (Directory.Exists sandbox.MorphirHome)
                            "no stage, lease, receipt or object on integrity refusal"
                })
            testCaseTask
                "foreign files below reserved Home directories are refused without adoption"
                (fun () -> task {
                    let candidatePath =
                        candidatePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Candidate path required")

                    let archivePath =
                        archivePath
                        |> Option.ofObj
                        |> Option.defaultWith (fun () -> failtest "Archive path required")

                    use sandbox = TestSandbox.Create()

                    Directory.CreateDirectory(Path.Combine(sandbox.MorphirHome, "store"))
                    |> ignore

                    let foreign = Path.Combine(sandbox.MorphirHome, "store", "alien")
                    File.WriteAllText(foreign, "foreign bytes")

                    let! exit, output, _ =
                        runLocal sandbox [
                            "setup"
                            "--candidate"
                            candidatePath
                            "--local-archive"
                            archivePath
                            "--home"
                            sandbox.MorphirHome
                            "--local-developer"
                            "--dry-run"
                        ]

                    Expect.equal exit 1 "reserved directory names do not imply ownership"
                    Expect.stringContains output "local.foreign_home" "foreign state refusal"

                    Expect.equal
                        (File.ReadAllText foreign)
                        "foreign bytes"
                        "foreign state is preserved"

                    Expect.isFalse
                        (Directory.Exists(Path.Combine(sandbox.MorphirHome, "toolchains")))
                        "no generation or pointer on refusal"
                })
        ]
