// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.PackageTests

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Extensions.Installation
open Morphir.Toolchain
open Morphir.Mep
open Morphir.TestSupport
open IoTestSupport

let private utf8 (text: string) = Encoding.UTF8.GetBytes text

let private sample () =
    use cases =
        JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "ParentContracts", "package-cases.json")
            )
        )

    let first =
        cases.RootElement.GetProperty("cases").EnumerateArray()
        |> Seq.head

    let document =
        first.GetProperty("schemaDocuments").EnumerateArray()
        |> Seq.head

    nonNull (JsonNode.Parse(document.GetProperty("document").GetRawText()))

let private read (node: JsonNode) =
    PackagePlan.Read(utf8 (node.ToJsonString()), InstallationPlatform.Create("linux-x64"))

let private changed edit =
    let node = sample () in
    edit node
    read node

let private artifact (node: JsonNode) =
    nonNull ((nonNull node["artifacts"])[0])

[<Tests>]
let tests =
    testLabel "PlatformSpecific" <| testList "Packages" [
        test "published package plan retains distinct archive and tree identities" {
            let plan =
                read (sample ())
                |> get

            Expect.equal
                plan.ArchiveDigest.Value
                "e7843fe0cb40b99db07130143467fa7a36c00bfef5d680ed0b42cf0e2d1fd042"
                "archive"

            Expect.equal
                plan.TreeDigest.Value
                "b4b57fdb392b9a799f373d1cba33a6ccd8facdbf17e6a48afad9949bb352bf98"
                "tree"

            Expect.equal plan.EntryPoint.Text "run" "entry point"
        }
        test "package must be critical" {
            changed (fun node ->
                node.AsObject().Remove "critical"
                |> ignore
            )
            |> refused "package.not_critical"
        }
        test "archive extension must agree with format" {
            changed (fun node -> (artifact node)["filename"] <- JsonValue.Create "guest.tgz")
            |> refused "package.archive_format"
        }
        for filename, format in
            [ "guest.ZIP", "zip"; "guest.TAR.GZ", "tar.gz"; "guest.TGZ", "tar.gz" ] do
            testCase
                $"archive suffix is ASCII case insensitive: {filename}"
                (fun () ->
                    changed (fun node ->
                        let item = artifact node
                        item["filename"] <- JsonValue.Create filename
                        (nonNull item["package"])["format"] <- JsonValue.Create format
                    )
                    |> get
                    |> ignore
                )
        test "package cannot be wasm" {
            changed (fun node ->
                let a = artifact node in
                a["runtime"] <- JsonValue.Create "wasm"

                a.AsObject().Remove "platform"
                |> ignore
            )
            |> refused "package.runtime"
        }
        test "canonical manifest preserves a space and final LF" {
            let bytes =
                utf8
                    "morphir-package-tree 0.1.0-preview.1\nf 0 e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 docs/read me.txt\n"

            let value =
                PackageManifest.Read(bytes, digest bytes)
                |> get

            Expect.equal value.Files.Count 1 "one file"
            Expect.equal value.Files[0].Path.Text "docs/read me.txt" "split only three spaces"
            Expect.sequenceEqual (value.ToBytes()) bytes "exact canonical bytes"
        }
        test "manifest refuses a digest mismatch" {
            PackageManifest.Read(
                utf8 "morphir-package-tree 0.1.0-preview.1\n",
                Sha256Digest.Create(String.replicate 64 "0")
            )
            |> refused "package.tree_digest_mismatch"
        }
        test "manifest refuses a regular file used as a parent directory" {
            let sha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"

            let bytes =
                utf8 $"morphir-package-tree 0.1.0-preview.1\nf 0 {sha} a\nf 0 {sha} a/b\n"

            PackageManifest.Read(bytes, digest bytes)
            |> refused "package.tree_changed"
        }
        for content in
            [
                "morphir-package-tree 0.1.0-preview.1\r\n"
                "morphir-package-tree 0.1.0-preview.1"
                "morphir-package-tree 0.1.0-preview.1\nf 00 e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 a\n"
                "morphir-package-tree 0.1.0-preview.1\nf 0 e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 ../x\n"
            ] do
            testCase
                ($"manifest rejects noncanonical input {content.Length}:{content.GetHashCode()}")
                (fun () ->
                    let bytes = utf8 content

                    PackageManifest.Read(bytes, digest bytes)
                    |> refused "package.tree_changed"
                )
    ]

let fixtureWithDirectory (sandbox: TestSandbox) name directory =
    let payload = utf8 "guest"
    use memory = new MemoryStream()

    do
        use zip = new ZipArchive(memory, ZipArchiveMode.Create, true)
        let entry = zip.CreateEntry name

        do
            use output = entry.Open()
            output.Write payload

        match directory with
        | Some path ->
            zip.CreateEntry(path)
            |> ignore
        | None -> ()

    let bytes = memory.ToArray()
    let node = sample ()
    let a = artifact node
    let p = nonNull a["package"]

    p.AsObject().Remove "root"
    |> ignore

    p.AsObject().Remove "executables"
    |> ignore

    let manifest =
        utf8 $"morphir-package-tree 0.1.0-preview.1\nx 5 {Digest.ofBytes payload} run\n"

    a["sha256"] <- JsonValue.Create(Digest.ofBytes bytes)
    p["treeSha256"] <- JsonValue.Create(Digest.ofBytes manifest)

    let plan =
        read node
        |> get

    plan, staged sandbox bytes, payload

let fixture sandbox name = fixtureWithDirectory sandbox name None

let fixtureForPlatform (platform: InstallationPlatform) (sandbox: TestSandbox) =
    let original, _, payload = fixture sandbox "run"

    let entryPoint =
        if platform.RuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal) then
            "run.exe"
        else
            "run"

    use memory = new MemoryStream()

    do
        use zip = new ZipArchive(memory, ZipArchiveMode.Create, true)
        use output = zip.CreateEntry(entryPoint).Open()
        output.Write payload

    let bytes = memory.ToArray()

    let node =
        nonNull (JsonNode.Parse(Encoding.UTF8.GetString(JsonText.toBytes original.Descriptor.Json)))

    let a = artifact node
    a["platform"] <- JsonValue.Create platform.DescriptorTriple
    a["sha256"] <- JsonValue.Create(Digest.ofBytes bytes)
    let p = nonNull a["package"]
    p["entryPoint"] <- JsonValue.Create entryPoint

    let manifest =
        utf8 $"morphir-package-tree 0.1.0-preview.1\nx 5 {Digest.ofBytes payload} {entryPoint}\n"

    p["treeSha256"] <- JsonValue.Create(Digest.ofBytes manifest)

    let plan =
        PackagePlan.Read(utf8 (node.ToJsonString()), platform)
        |> get

    plan, staged sandbox bytes, payload

let fixtureForHost sandbox =
    fixtureForPlatform
        (ActivationPlatforms.current ()
         |> StateOwnership.unwrap)
        sandbox

let private editPlan platform (plan: PackagePlan) change =
    let node =
        nonNull (JsonNode.Parse(Encoding.UTF8.GetString(JsonText.toBytes plan.Descriptor.Json)))

    change node

    PackagePlan.Read(utf8 (node.ToJsonString()), platform)
    |> get

let private executablePair sandbox platform =
    let original, _, payload = fixtureForPlatform platform sandbox
    use memory = new MemoryStream()

    do
        use zip = new ZipArchive(memory, ZipArchiveMode.Create, true)

        for name in [ "run.exe"; "helper" ] do
            use output = zip.CreateEntry(name).Open()
            output.Write payload

    let bytes = memory.ToArray()
    let fileDigest = Digest.ofBytes payload

    let manifest =
        utf8
            $"morphir-package-tree 0.1.0-preview.1\nx 5 {fileDigest} helper\nx 5 {fileDigest} run.exe\n"

    let plan =
        editPlan
            platform
            original
            (fun node ->
                let item = artifact node
                item["sha256"] <- JsonValue.Create(Digest.ofBytes bytes)
                let package = nonNull item["package"]
                package["entryPoint"] <- JsonValue.Create "run.exe"
                package["executables"] <- nonNull (JsonNode.Parse("[\"helper\"]"))
                package["treeSha256"] <- JsonValue.Create(Digest.ofBytes manifest)
            )

    plan, staged sandbox bytes

let private expectPackageRefusal code operation root plan = task {
    let! accepted =
        if operation = "acquire" then
            task {
                let! result = PackageInstaller.AcquireAsync(root, plan, ct)

                return
                    result.Match(
                        Func<PackageActivation, bool>(fun activation ->
                            (activation :> IDisposable).Dispose()
                            true
                        ),
                        Func<InstallationFailure, bool>(fun failure ->
                            Expect.equal failure.Code code failure.Message
                            false
                        )
                    )
            }
        else
            task {
                let! result = PackageInstaller.VerifyAsync(root, plan, ct)

                return
                    result.Match(
                        Func<InstalledPackage, bool>(fun _ -> true),
                        Func<InstallationFailure, bool>(fun failure ->
                            Expect.equal failure.Code code failure.Message
                            false
                        )
                    )
            }

    Expect.isFalse accepted $"{operation} must reject the invalid declaration"
}

let packageCleanup (sandbox: TestSandbox) =
    { new IAsyncDisposable with
        member _.DisposeAsync() =
            let rec writable path =
                for item in Directory.GetFileSystemEntries path do
                    let attributes = File.GetAttributes item

                    if
                        attributes
                        &&& FileAttributes.ReparsePoint
                        <> enum<FileAttributes> 0
                    then
                        failtest "Cleanup refuses a linked test entry."
                    elif
                        attributes
                        &&& FileAttributes.Directory
                        <> enum<FileAttributes> 0
                    then
                        writable item
                    else
                        File.SetAttributes(item, FileAttributes.Normal)

                if not (OperatingSystem.IsWindows()) then
                    File.SetUnixFileMode(path, enum<UnixFileMode> 0o755)

            writable (home sandbox)
            System.Threading.Tasks.ValueTask.CompletedTask
    }

[<Tests>]
let mechanismTests =
    testLabel "PlatformSpecific" <| testList "PackageTrees" [
        for operation in [ "acquire"; "verify" ] do
            testTask $"{operation} refuses a declared executable absent from the installed manifest" {
                use sandbox = TestSandbox.Create()
                use cleanup = packageCleanup sandbox

                let platform =
                    ActivationPlatforms.current ()
                    |> StateOwnership.unwrap

                let plan, archive, payload = fixtureForPlatform platform sandbox
                let root = home sandbox
                let! installed = PackageInstaller.InstallAsync(root, plan, archive, ct)
                let installed: InstalledPackage = get installed

                let changed =
                    editPlan
                        platform
                        plan
                        (fun node ->
                            let item = artifact node
                            let package = nonNull item["package"]
                            package["executables"] <- nonNull (JsonNode.Parse("[\"ghost\"]"))
                        )

                do! expectPackageRefusal "package.executable_missing" operation root changed

                Expect.equal
                    (File.ReadAllBytes installed.EntryPointPath)
                    payload
                    "valid winner is preserved"
            }

            testTask
                $"{operation} refuses a Windows entry point without exe despite identical manifest modes" {
                if
                    operation = "acquire"
                    && not (OperatingSystem.IsWindows())
                then
                    skiptest
                        "Acquisition rejects foreign host platforms before structural verification."

                use sandbox = TestSandbox.Create()
                use cleanup = packageCleanup sandbox

                let platform =
                    if OperatingSystem.IsWindows() then
                        ActivationPlatforms.current ()
                        |> StateOwnership.unwrap
                    else
                        InstallationPlatform.Create "win-x64"

                let plan, archive = executablePair sandbox platform
                let root = home sandbox
                let! installed = PackageInstaller.InstallAsync(root, plan, archive, ct)

                get installed
                |> ignore

                let changed =
                    editPlan
                        platform
                        plan
                        (fun node ->
                            let item = artifact node
                            let package = nonNull item["package"]
                            package["entryPoint"] <- JsonValue.Create "helper"
                            package["executables"] <- nonNull (JsonNode.Parse("[\"run.exe\"]"))
                        )

                do! expectPackageRefusal "package.entry_point_not_exe" operation root changed
            }
        testTask "malformed Deflate is a typed archive refusal after a valid delivered digest" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, path, _ = fixture sandbox "run"
            let bytes = File.ReadAllBytes path

            let offset =
                use input = new MemoryStream(bytes)
                let records = ArchiveChecks.zipRecordsWithPolicy input true

                records[0].DataStart
                |> int

            bytes[offset] <- 7uy // reserved Deflate BTYPE=3

            let node =
                nonNull (
                    JsonNode.Parse(Encoding.UTF8.GetString(JsonText.toBytes plan.Descriptor.Json))
                )

            (artifact node)["sha256"] <- JsonValue.Create(Digest.ofBytes bytes)

            let changedPlan =
                read node
                |> get

            let changedPath = staged sandbox bytes
            let! result = PackageInstaller.InstallAsync(home sandbox, changedPlan, changedPath, ct)
            refused "extension.unsafe_archive" result
        }
        testTask "valid empty ZIP directory is validated without creating an empty tree directory" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive, payload = fixtureWithDirectory sandbox "run" (Some "empty/")
            let! result = PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)
            let installed: InstalledPackage = get result

            Expect.sequenceEqual
                (File.ReadAllBytes installed.EntryPointPath)
                payload
                "regular file preserved"

            Expect.isFalse
                (Directory.Exists(Path.Combine(installed.DirectoryPath, "tree", "empty")))
                "directory entry creates no empty directory"
        }
        testTask "installs and fully rehashes reusable package bytes" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive, payload = fixture sandbox "run"

            let! (first: InstallationResult<InstalledPackage>) =
                PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)

            let installed: InstalledPackage = get first

            Expect.sequenceEqual
                (File.ReadAllBytes installed.EntryPointPath)
                payload
                "actual stored bytes"

            let before = File.GetLastWriteTimeUtc installed.EntryPointPath

            let! (second: InstallationResult<InstalledPackage>) =
                PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)

            Expect.equal (get second).EntryPointPath installed.EntryPointPath "same content object"
            Expect.equal (File.GetLastWriteTimeUtc installed.EntryPointPath) before "no rewrite"
        }
        testTask "activation owns concurrent shared leases and excludes collection" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive, _ = fixtureForHost sandbox
            let root = home sandbox
            let! installed = PackageInstaller.InstallAsync(root, plan, archive, ct)

            get installed
            |> ignore

            let! (first: InstallationResult<PackageActivation>) =
                PackageInstaller.AcquireAsync(root, plan, ct)

            let first = get first

            try
                let! (second: InstallationResult<PackageActivation>) =
                    PackageInstaller.AcquireAsync(root, plan, ct)

                let second = get second

                try
                    let directory =
                        SafeDirectory.Open(
                            Path.Combine(root, "store", "extensions", "packages", "sha256"),
                            false
                        )

                    try
                        Expect.throwsT<FileLeaseBusyException>
                            (fun () ->
                                use lease =
                                    directory.NamedLease(
                                        plan.TreeDigest.Value
                                        + ".lease",
                                        true,
                                        false
                                    ) in

                                ()
                            )
                            "readers retain lifetime"
                    finally
                        (directory :> IDisposable).Dispose()
                finally
                    (second :> IDisposable).Dispose()
            finally
                (first :> IDisposable).Dispose()
        }
        testTask "reuse detects tampering and preserves the corrupt winner" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive, _ = fixture sandbox "run"

            let! (first: InstallationResult<InstalledPackage>) =
                PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)

            let installed: InstalledPackage = get first
            File.SetAttributes(installed.EntryPointPath, FileAttributes.Normal)

            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(
                    installed.EntryPointPath,
                    UnixFileMode.UserRead
                    ||| UnixFileMode.UserWrite
                )

            File.WriteAllText(installed.EntryPointPath, "wrong")

            let! (second: InstallationResult<InstalledPackage>) =
                PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)

            refused "package.tree_changed" second
            Expect.equal (File.ReadAllText installed.EntryPointPath) "wrong" "no silent repair"
        }
        testTask "traversal refuses without a published tree" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive, _ = fixture sandbox "../run"
            let! result = PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)
            refused "package.path" result

            Expect.isFalse
                (Directory.Exists(
                    Path.Combine(
                        home sandbox,
                        "store",
                        "extensions",
                        "packages",
                        "sha256",
                        plan.TreeDigest.Value
                    )
                ))
                "not published"
        }
        testTask "old catalog prevents publication without being rewritten" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive, _ = fixture sandbox "run"
            let root = home sandbox

            Directory.CreateDirectory(Path.Combine(root, "catalog"))
            |> ignore

            let old = "{\"version\":1,\"extensions\":[]}"
            File.WriteAllText(Path.Combine(root, "catalog", "extensions.json"), old)
            let! result = PackageInstaller.InstallAsync(root, plan, archive, ct)
            refused "extension.old_state" result

            Expect.equal
                (File.ReadAllText(Path.Combine(root, "catalog", "extensions.json")))
                old
                "untouched"
        }
        testTask "activation refuses an extra regular file" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive, _ = fixtureForHost sandbox

            let! (installed: InstallationResult<InstalledPackage>) =
                PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)

            let installed = get installed
            let tree = Path.Combine(installed.DirectoryPath, "tree")

            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(tree, enum<UnixFileMode> 0o755)

            File.WriteAllText(Path.Combine(tree, "foreign"), "foreign")

            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(tree, enum<UnixFileMode> 0o555)

            let! result = PackageInstaller.AcquireAsync(home sandbox, plan, ct)
            refused "package.tree_changed" result

            Expect.equal
                (File.ReadAllText(Path.Combine(tree, "foreign")))
                "foreign"
                "foreign object retained"
        }
        testTask
            "explicit verification hashes bytes while activation performs VERIFY-B structure checks" {
            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive, _ = fixtureForHost sandbox

            let! (installed: InstallationResult<InstalledPackage>) =
                PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)

            let path = (get installed).EntryPointPath
            File.SetAttributes(path, FileAttributes.Normal)

            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(path, enum<UnixFileMode> 0o755)

            File.WriteAllText(path, "wrong")

            if OperatingSystem.IsWindows() then
                File.SetAttributes(path, FileAttributes.ReadOnly)
            else
                File.SetUnixFileMode(path, enum<UnixFileMode> 0o555)

            let! (activation: InstallationResult<PackageActivation>) =
                PackageInstaller.AcquireAsync(home sandbox, plan, ct)

            (get activation :> IDisposable).Dispose()
            let! result = PackageInstaller.VerifyAsync(home sandbox, plan, ct)
            refused "package.tree_changed" result
        }
    ]

let private producerCanonical () =
    use document =
        JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "ParentContracts", "package-cases.json")
            )
        )

    let first =
        document.RootElement.GetProperty("cases").EnumerateArray()
        |> Seq.head

    utf8 (nonNull (first.GetProperty("expected").GetProperty("manifest").GetString()))

let private producerFact (path: string) (length: int64) (executable: bool) =
    PackageFile.Create(
        PackagePaths.read "package.tree_changed" path,
        length,
        digest [||],
        executable
    )
    |> get

let private producerRelease
    (template: JsonNode)
    (platform: InstallationPlatform)
    (manifest: PackageManifest)
    (entry: string)
    =
    let archiveName =
        DistributionIdentifiers.ParseArtifactFileName "provider.ZIP"
        |> function
            | Ok value -> value
            | Error error -> failtest (error.ToString())

    PackageRelease.PrepareWholeArchive(
        utf8 (template.ToJsonString()),
        platform,
        archiveName,
        digest (utf8 "archive"),
        manifest,
        PackagePaths.read "package.tree_changed" entry
    )

[<Tests>]
let producerTests =
    testLabel "PlatformSpecific" <| testList "PackageProducer" [
        test "public producer uses the existing canonical writer and owns its file list" {
            let expected = producerCanonical ()

            let original =
                PackageManifest.Read(expected, digest expected)
                |> get

            let source =
                original.Files
                |> Seq.rev
                |> Seq.map (fun file ->
                    PackageFile.Create(file.Path, file.Length, file.Digest, file.Executable)
                    |> get
                )
                |> Seq.toArray

            let actual =
                PackageManifest.FromFiles(Array.AsReadOnly source)
                |> get

            Array.Clear(source, 0, source.Length)
            Expect.sequenceEqual (actual.ToBytes()) expected "exact frozen canonical bytes"
            Expect.equal actual.Files.Count original.Files.Count "defensive ownership"
        }
        for length in
            [
                -1L
                InstallationLimits.MaxArtifactBytes
                + 1L
            ] do
            testCase
                $"public file facts refuse an invalid length {length}"
                (fun () ->
                    PackageFile.Create(
                        PackagePaths.read "package.tree_changed" "run",
                        length,
                        digest [||],
                        true
                    )
                    |> refused "package.tree_changed"
                )
        for paths in [ [ "a"; "A" ]; [ "a"; "a/b" ] ] do
            let label = String.concat "," paths

            testCase
                $"public manifest factory refuses conflicting paths {label}"
                (fun () ->
                    let files =
                        paths
                        |> List.map (fun path -> producerFact path 0L false)
                        |> List.toArray

                    PackageManifest.FromFiles(Array.AsReadOnly files)
                    |> refused "package.tree_changed"
                )
        test
            "release preparation binds actual archive and canonical manifest while preserving optional metadata" {
            let template = sample ()
            template["future"] <- JsonValue.Create "preserved"

            let manifest =
                PackageManifest.Read(producerCanonical (), digest (producerCanonical ()))
                |> get

            let bytes =
                producerRelease template (InstallationPlatform.Create "linux-x64") manifest "run"
                |> get

            let plan =
                PackagePlan.Read(bytes, InstallationPlatform.Create "linux-x64")
                |> get

            let parsed = nonNull (JsonNode.Parse(Encoding.UTF8.GetString bytes))

            Expect.equal
                ((nonNull parsed["future"]).GetValue<string>())
                "preserved"
                "unknown optional root value"

            Expect.equal plan.ArchiveDigest (digest (utf8 "archive")) "actual archive identity"
            Expect.equal plan.TreeDigest (digest (manifest.ToBytes())) "canonical manifest identity"
            Expect.isNone plan.Root "the official producer archive has no enclosing root"

            Expect.sequenceEqual
                (plan.Executables
                 |> Seq.map (fun path -> path.Text))
                [ "tools/helper" ]
                "only actual additional executables"

            use document = JsonDocument.Parse bytes

            Expect.equal
                ((document.RootElement.EnumerateObject()
                  |> Seq.head)
                    .Name)
                "$schema"
                "schema written first"
        }
        test "release preparation refuses an absent entry point" {
            let manifest =
                PackageManifest.FromFiles(Array.AsReadOnly [| producerFact "data" 0L false |])
                |> get

            producerRelease (sample ()) (InstallationPlatform.Create "linux-x64") manifest "missing"
            |> refused "package.entry_point_missing"
        }
        test "release preparation refuses an entry not marked executable" {
            let manifest =
                PackageManifest.FromFiles(Array.AsReadOnly [| producerFact "run" 0L false |])
                |> get

            producerRelease (sample ()) (InstallationPlatform.Create "linux-x64") manifest "run"
            |> refused "package.tree_changed"
        }
        test "release preparation reuses the Windows executable declaration guard" {
            let template = sample ()
            (artifact template)["platform"] <- JsonValue.Create "x86_64-pc-windows-msvc"

            let manifest =
                PackageManifest.FromFiles(Array.AsReadOnly [| producerFact "run" 0L true |])
                |> get

            producerRelease template (InstallationPlatform.Create "win-x64") manifest "run"
            |> refused "package.entry_point_not_exe"
        }
    ]

// A tar.gz package for this host from explicit archive entries. The manifest lists the regular files, with the
// entry point "run" and the additional executable "tools/helper" executable.
let private tarPackageOf (sandbox: TestSandbox) (entries: Formats.Tar.TarEntry list) (files: (string * byte array) list) =
    let platform =
        ActivationPlatforms.current ()
        |> StateOwnership.unwrap

    let original, _, _ = fixtureForPlatform platform sandbox
    use memory = new MemoryStream()

    do
        use gzip = new GZipStream(memory, CompressionLevel.Fastest, true)
        use tar = new Formats.Tar.TarWriter(gzip, Formats.Tar.TarEntryFormat.Pax, false)

        for entry in entries do
            tar.WriteEntry entry

    let bytes = memory.ToArray()
    let executable name = name = "run" || name = "tools/helper"

    let manifest =
        files
        |> List.sortWith (fun (a, _) (b, _) -> StringComparer.Ordinal.Compare(a, b))
        |> List.map (fun (name, content) ->
            $"""{if executable name then "x" else "f"} {content.Length} {Digest.ofBytes content} {name}""")
        |> String.concat "\n"
        |> fun lines -> utf8 $"morphir-package-tree 0.1.0-preview.1\n{lines}\n"

    let plan =
        editPlan
            platform
            original
            (fun node ->
                let item = artifact node
                item["filename"] <- JsonValue.Create "guest.tar.gz"
                item["sha256"] <- JsonValue.Create(Digest.ofBytes bytes)
                let package = nonNull item["package"]
                package["format"] <- JsonValue.Create "tar.gz"
                package["entryPoint"] <- JsonValue.Create "run"
                package["executables"] <- nonNull (JsonNode.Parse("[\"tools/helper\"]"))
                package["treeSha256"] <- JsonValue.Create(Digest.ofBytes manifest)
            )

    plan, staged sandbox bytes

let private regular (name: string) (content: byte array) (mode: int) =
    let entry = Formats.Tar.PaxTarEntry(Formats.Tar.TarEntryType.RegularFile, name)
    entry.Mode <- enum<UnixFileMode> mode
    entry.DataStream <- new MemoryStream(content)
    entry :> Formats.Tar.TarEntry

let private folder (name: string) =
    let entry = Formats.Tar.PaxTarEntry(Formats.Tar.TarEntryType.Directory, name)
    entry.Mode <- enum<UnixFileMode> 0o755
    entry :> Formats.Tar.TarEntry

let private packageFiles = [ "lib/data.json", utf8 "{}"; "run", utf8 "guest"; "tools/helper", utf8 "helper" ]

// A tar.gz package for this host: its entry point, one additional executable and one data file at the archive root.
let private tarPackage (sandbox: TestSandbox) (extra: Formats.Tar.TarEntry list) =
    let entries =
        packageFiles
        |> List.map (fun (name, content) -> regular name content (if name = "lib/data.json" then 0o644 else 0o755))

    tarPackageOf sandbox (entries @ extra) packageFiles

let private installTar (sandbox: TestSandbox) plan archive = task {
    let root = home sandbox
    let! installed = PackageInstaller.InstallAsync(root, plan, archive, ct)
    return root, (get installed: InstalledPackage)
}

[<Tests>]
let tarPackageTests =
    testLabel "PlatformSpecific" <| testList "PackageTarTrees" [
        testTask "a tar.gz package installs with its canonical modes and verifies" {
            if OperatingSystem.IsWindows() then
                skiptest "A Windows entry point is a .exe in a ZIP package."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, archive = tarPackage sandbox []
            Expect.equal plan.Format "tar.gz" "the declared format"
            let root = home sandbox
            let! installed = PackageInstaller.InstallAsync(root, plan, archive, ct)
            let installed: InstalledPackage = get installed
            Expect.equal (File.ReadAllBytes installed.EntryPointPath) (utf8 "guest") "entry point bytes are unchanged"
            let tree = Path.GetDirectoryName installed.EntryPointPath |> nonNull
            Expect.equal (File.GetUnixFileMode installed.EntryPointPath) (enum<UnixFileMode> 0o555) "entry point is executable"
            Expect.equal (File.GetUnixFileMode(Path.Combine(tree, "tools", "helper"))) (enum<UnixFileMode> 0o555) "declared executable"
            Expect.equal (File.GetUnixFileMode(Path.Combine(tree, "lib", "data.json"))) (enum<UnixFileMode> 0o444) "data file"
            let! verified = PackageInstaller.VerifyAsync(root, plan, ct)
            get verified |> ignore
        }
        testTask "a tar.gz package with a symbolic link is refused before publication" {
            if OperatingSystem.IsWindows() then
                skiptest "A Windows entry point is a .exe in a ZIP package."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let link = Formats.Tar.UstarTarEntry(Formats.Tar.TarEntryType.SymbolicLink, "tools/link")
            link.LinkName <- "../run"
            let plan, archive = tarPackage sandbox [ link ]
            let root = home sandbox
            let! result = PackageInstaller.InstallAsync(root, plan, archive, ct)
            refused "extension.unsafe_archive" result
            Expect.isFalse (Directory.Exists(Path.Combine(root, "store", "extensions", "packages", "sha256", plan.TreeDigest.Value))) "nothing is published"
        }
        testTask "a tar.gz package whose bytes are a ZIP is refused" {
            if OperatingSystem.IsWindows() then
                skiptest "A Windows entry point is a .exe in a ZIP package."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let plan, _ = tarPackage sandbox []
            let zipPlan, zipArchive, _ = fixtureForHost sandbox
            let mismatched =
                editPlan
                    (ActivationPlatforms.current () |> StateOwnership.unwrap)
                    plan
                    (fun node -> (artifact node)["sha256"] <- JsonValue.Create zipPlan.ArchiveDigest.Value)
            let! result = PackageInstaller.InstallAsync(home sandbox, mismatched, zipArchive, ct)
            refused "package.archive_format" result
        }
        testTask "a package made with tar -C dir -czf package.tar.gz . installs" {
            if OperatingSystem.IsWindows() then
                skiptest "A Windows entry point is a .exe in a ZIP package."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let entries =
                [ folder "./"; folder "./lib/"; regular "./lib/data.json" (utf8 "{}") 0o644
                  regular "./run" (utf8 "guest") 0o755; folder "./tools/"; regular "./tools/helper" (utf8 "helper") 0o755 ]
            let plan, archive = tarPackageOf sandbox entries packageFiles
            let! (root: string), (installed: InstalledPackage) = installTar sandbox plan archive
            Expect.equal (File.ReadAllBytes installed.EntryPointPath) (utf8 "guest") "the ./ prefix is removed"
            let! verified = PackageInstaller.VerifyAsync(root, plan, ct)
            get verified |> ignore
        }
        testTask "a package from the system tar of a folder installs" {
            if OperatingSystem.IsWindows() || not (File.Exists "/usr/bin/tar") then
                skiptest "Needs the system tar of a Unix machine."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let source = Directory.CreateDirectory(Path.Combine(sandbox.Work, "package-source")).FullName
            for name, content in packageFiles do
                let path = Path.Combine(source, name)
                Directory.CreateDirectory(nonNull (Path.GetDirectoryName path)) |> ignore
                File.WriteAllBytes(path, content)
            File.SetUnixFileMode(Path.Combine(source, "run"), enum<UnixFileMode> 0o755)
            File.SetUnixFileMode(Path.Combine(source, "tools", "helper"), enum<UnixFileMode> 0o755)
            let built = Path.Combine(sandbox.Work, "built.tar.gz")
            let start = ChildProcess.StartInfo("/usr/bin/tar", "-C", source, "-czf", built, ".")
            // macOS tar adds AppleDouble files for extended attributes unless COPYFILE_DISABLE is set.
            start.Environment["COPYFILE_DISABLE"] <- "1"
            let! (result: ChildProcessResult) = ChildProcess.RunAsync(start, TimeSpan.FromSeconds 30.)
            Expect.equal result.ExitCode 0 result.CombinedOutput
            let reference, _ = tarPackage sandbox []
            let bytes = File.ReadAllBytes built
            let plan =
                editPlan
                    (ActivationPlatforms.current () |> StateOwnership.unwrap)
                    reference
                    (fun node -> (artifact node)["sha256"] <- JsonValue.Create(Digest.ofBytes bytes))
            let! (root: string), (installed: InstalledPackage) = installTar sandbox plan (staged sandbox bytes)
            Expect.equal (File.ReadAllBytes installed.EntryPointPath) (utf8 "guest") "the entry point"
            let! verified = PackageInstaller.VerifyAsync(root, plan, ct)
            get verified |> ignore
        }
        testTask "a tar.gz package with more than 1024 and fewer than 4096 entries installs, as a ZIP package does" {
            if OperatingSystem.IsWindows() then
                skiptest "A Windows entry point is a .exe in a ZIP package."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let many = [ for index in 1..1100 -> $"lib/file{index:D4}.txt", utf8 (string index) ]
            let files = packageFiles @ many
            let entries = files |> List.map (fun (name, content) -> regular name content (if name = "run" || name = "tools/helper" then 0o755 else 0o644))
            let plan, archive = tarPackageOf sandbox entries files
            let! _, (installed: InstalledPackage) = installTar sandbox plan archive
            Expect.equal installed.Manifest.Files.Count files.Length "every file"
        }
        testTask "a tar.gz package with more than 4096 entries is refused" {
            if OperatingSystem.IsWindows() then
                skiptest "A Windows entry point is a .exe in a ZIP package."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let many = [ for index in 1..4100 -> $"lib/file{index:D4}.txt", utf8 "x" ]
            let files = packageFiles @ many
            let entries = files |> List.map (fun (name, content) -> regular name content 0o644)
            let plan, archive = tarPackageOf sandbox entries files
            let! result = PackageInstaller.InstallAsync(home sandbox, plan, archive, ct)
            refused "extension.archive_limit" result
        }
        testTask "a gzip resource in a tar.gz package is a file, not a nested archive" {
            if OperatingSystem.IsWindows() then
                skiptest "A Windows entry point is a .exe in a ZIP package."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let resource =
                use memory = new MemoryStream()
                do
                    use gzip = new GZipStream(memory, CompressionLevel.Fastest, true)
                    gzip.Write(utf8 "not a tar archive")
                memory.ToArray()
            let files = packageFiles @ [ "lib/resource.gz", resource ]
            let entries = files |> List.map (fun (name, content) -> regular name content (if name = "run" || name = "tools/helper" then 0o755 else 0o644))
            let plan, archive = tarPackageOf sandbox entries files
            let! _, (installed: InstalledPackage) = installTar sandbox plan archive
            let tree = nonNull (Path.GetDirectoryName installed.EntryPointPath)
            Expect.equal (File.ReadAllBytes(Path.Combine(tree, "lib", "resource.gz"))) resource "the resource bytes"
        }
        testTask "setuid, setgid and world-writable package entry modes are installed as the canonical modes" {
            if OperatingSystem.IsWindows() then
                skiptest "Unix file modes."

            use sandbox = TestSandbox.Create()
            use cleanup = packageCleanup sandbox
            let modes = Map.ofList [ "run", 0o4777; "tools/helper", 0o2755; "lib/data.json", 0o666 ]
            let entries = packageFiles |> List.map (fun (name, content) -> regular name content modes[name])
            let plan, archive = tarPackageOf sandbox entries packageFiles
            let! _, (installed: InstalledPackage) = installTar sandbox plan archive
            let tree = nonNull (Path.GetDirectoryName installed.EntryPointPath)
            Expect.equal (File.GetUnixFileMode installed.EntryPointPath) (enum<UnixFileMode> 0o555) "no setuid or write"
            Expect.equal (File.GetUnixFileMode(Path.Combine(tree, "tools", "helper"))) (enum<UnixFileMode> 0o555) "no setgid"
            Expect.equal (File.GetUnixFileMode(Path.Combine(tree, "lib", "data.json"))) (enum<UnixFileMode> 0o444) "no write"
        }
    ]
