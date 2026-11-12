// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Text
open System.Collections.Generic
open System.Globalization
open System.IO.Compression
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain
open Morphir.Mep

/// A release-bound package plan. Archive and canonical tree identities remain separate.
[<Sealed>]
type PackagePlan
    private
    (
        descriptor: ReleaseDescriptor option,
        artifact: ReleaseArtifact,
        format: string,
        root: string option,
        entryPoint: TargetPath,
        executables: TargetPath array,
        tree: Sha256Digest
    ) =
    /// The digest of the delivered archive bytes.
    member _.ArchiveDigest = artifact.Sha256
    /// The digest of the canonical package manifest.
    member _.TreeDigest = tree
    /// The portable regular-file path of the process entry point.
    member _.EntryPoint = entryPoint
    /// The declared archive format, zip or tar.gz. Reading a plan does not imply extraction support.
    member _.Format = format
    /// Read-only declared executable paths. The entry point is also executable when omitted here.
    member _.Executables: IReadOnlyList<TargetPath> = Array.AsReadOnly executables
    member internal _.Root = root
    member internal _.Artifact = artifact

    member internal _.Descriptor =
        match descriptor with
        | Some descriptor -> descriptor
        | None ->
            Operations.fail
                "extension.invalid_descriptor"
                "A retained plan has no release descriptor to republish."

    /// A diagnostic summary of the two independently bound identities.
    override _.ToString() =
        $"Package tree {tree.Value}, archive {artifact.Sha256.Value}."

    static member internal FromState(entry: StateEntry, locked: StateLock) =
        let value =
            match entry.Package with
            | Some value -> value
            | None -> Operations.fail "extension.not_package" "Selected entry is not a package."

        let items = SharedJson.members "package" value

        let text name =
            SharedJson.required "package" name items
            |> SharedJson.text name

        let root =
            SharedJson.optional "root" items
            |> Option.map (SharedJson.text "package.root")

        let executables =
            SharedJson.optional "executables" items
            |> Option.map (SharedJson.strings "package.executables")
            |> Option.defaultValue []
            |> List.map (PackagePaths.read "package.path")
            |> List.toArray

        let platform =
            match entry.Platform with
            | Some value -> SharedJson.members "platform" value
            | None -> Operations.fail "extension.invalid_document" "Package platform is absent."

        let os =
            SharedJson.required "platform" "os" platform
            |> SharedJson.text "platform.os"

        let arch =
            SharedJson.required "platform" "arch" platform
            |> SharedJson.text "platform.arch"

        let selected =
            InstallationPlatform.FromIndexPlatform(os, arch)
            |> StateOwnership.unwrap

        let lockItems = SharedJson.members "lock" locked.Json

        let source =
            SharedJson.required "lock" "source" lockItems
            |> SharedJson.members "source"

        let filename =
            SharedJson.required "source" "path" source
            |> SharedJson.text "source.path"
            |> fun path ->
                (path.Split '/')[path.Split('/').Length
                                 - 1]

        let filename =
            DistributionIdentifiers.ParseArtifactFileName filename
            |> StateOwnership.unwrap

        let artifact = {
            Runtime = entry.Runtime
            Platform = Some selected.DescriptorTriple
            Filename = filename
            Sha256 = entry.Digest
            Claims = entry.Claims
            Package = Some value
        }

        PackagePlan(
            None,
            artifact,
            text "format",
            root,
            PackagePaths.read "package.path" (text "entryPoint"),
            executables,
            Sha256Digest.Create(text "treeSha256")
        )

    /// Reads the exact published release family and selects one process artifact by target triple.
    static member Read(bytes: byte array, platform: InstallationPlatform) =
        let result =
            Descriptors.read bytes
            |> Result.bind (fun descriptor ->
                SharedJson.protect (fun () ->
                    let items = SharedJson.members "release.json" descriptor.Json

                    let selected =
                        descriptor.Artifacts
                        |> List.filter (fun artifact ->
                            artifact.Platform = Some platform.DescriptorTriple
                        )

                    let artifact =
                        match selected with
                        | [ artifact ] -> artifact
                        | _ ->
                            SharedJson.fail
                                "extension.no_matching_artifact"
                                "artifacts"
                                "Expected exactly one matching process artifact."

                    if
                        artifact.Runtime
                        <> ArtifactRuntime.Process
                    then
                        SharedJson.fail
                            "package.runtime"
                            "artifacts"
                            "Packages require process runtime."

                    let declaration =
                        match artifact.Package with
                        | Some value -> SharedJson.members "package" value
                        | None ->
                            SharedJson.fail
                                "package.archive_without_package"
                                "package"
                                "A package declaration is required."

                    let critical =
                        SharedJson.optional "critical" items
                        |> Option.map (SharedJson.strings "critical")
                        |> Option.defaultValue []

                    if not (List.contains "artifacts.package" critical) then
                        SharedJson.fail
                            "package.not_critical"
                            "critical"
                            "artifacts.package must be critical."

                    let format =
                        SharedJson.required "package" "format" declaration
                        |> SharedJson.text "package.format"

                    let filename = artifact.Filename.Text

                    if
                        not (
                            (format = "zip"
                             && filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            || (format = "tar.gz"
                                && (filename.EndsWith(
                                        ".tar.gz",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                    || filename.EndsWith(
                                        ".tgz",
                                        StringComparison.OrdinalIgnoreCase
                                    )))
                        )
                    then
                        SharedJson.fail
                            "package.archive_format"
                            "package.format"
                            "Filename and archive format must agree."

                    let path name =
                        let value =
                            SharedJson.required "package" name declaration
                            |> SharedJson.text $"package.{name}"

                        match
                            PackagePaths.result (fun () -> PackagePaths.read "package.path" value)
                        with
                        | Ok path -> path
                        | Error error -> SharedJson.fail error.Code name error.Message

                    let entryPoint = path "entryPoint"

                    let root =
                        SharedJson.optional "root" declaration
                        |> Option.map (fun value ->
                            let text = SharedJson.text "package.root" value

                            match
                                PackagePaths.result (fun () ->
                                    PackagePaths.read "package.path" text
                                )
                            with
                            | Ok _ -> text
                            | Error error ->
                                SharedJson.fail error.Code "package.root" error.Message
                        )

                    let executables =
                        SharedJson.optional "executables" declaration
                        |> Option.map (SharedJson.strings "package.executables")
                        |> Option.defaultValue []

                    let executables =
                        executables
                        |> List.map (fun value ->
                            match
                                PackagePaths.result (fun () ->
                                    PackagePaths.read "package.path" value
                                )
                            with
                            | Ok path -> path
                            | Error error ->
                                SharedJson.fail error.Code "package.executables" error.Message
                        )
                        |> List.toArray

                    if
                        executables
                        |> Array.map (fun p -> p.Text)
                        |> Array.distinct
                        |> Array.length
                        <> executables.Length
                    then
                        SharedJson.invalid "package.executables" "Repeated executable."

                    let tree =
                        SharedJson.required "package" "treeSha256" declaration
                        |> SharedJson.parsed "package.treeSha256" Sha256Digest.Parse

                    PackagePlan(
                        Some descriptor,
                        artifact,
                        format,
                        root,
                        entryPoint,
                        executables,
                        tree
                    )
                )
            )

        InstallationResult result

/// One canonical regular-file entry. Executability comes from the package declaration.
[<Sealed>]
type PackageFile internal (path: TargetPath, length: int64, digest: Sha256Digest, executable: bool)
    =
    /// The validated portable path relative to the package tree.
    member _.Path = path
    /// The exact regular-file length in bytes.
    member _.Length = length
    /// The digest of the complete regular-file bytes.
    member _.Digest = digest
    /// Whether the package declaration requires executable mode for this file.
    member _.Executable = executable

    /// Diagnostic text without reflective formatting.
    override _.ToString() =
        $"Package file {path.Text}, {length} bytes, SHA-256 {digest.Value}."

    /// Creates one bounded producer observation. This does not read a file or establish provenance.
    static member Create(path: TargetPath, length: int64, digest: Sha256Digest, executable: bool) =
        InstallationResult(
            PackagePaths.result (fun () ->
                let path = PackagePaths.read "package.tree_changed" path.Text

                if
                    length < 0L
                    || length > InstallationLimits.MaxArtifactBytes
                then
                    Operations.fail "package.tree_changed" "File length exceeds package bounds."

                PackageFile(path, length, digest, executable)
            )
        )

/// The exact ASCII canonical manifest bound by package.treeSha256.
[<Sealed>]
type PackageManifest private (bytes: byte array, files: PackageFile array) =
    /// Read-only canonical regular-file entries in ordinal path order.
    member _.Files: IReadOnlyList<PackageFile> = Array.AsReadOnly files
    /// A defensive copy of the exact canonical manifest bytes, for F# and C# callers.
    member _.ToBytes() = Array.copy bytes

    /// Diagnostic text without exposing the retained manifest buffer.
    override _.ToString() =
        $"Package manifest with {files.Length} regular files."

    static member internal Create(files: PackageFile array) =
        let sorted =
            files
            |> Array.sortWith (fun a b -> StringComparer.Ordinal.Compare(a.Path.Text, b.Path.Text))

        let content = StringBuilder("morphir-package-tree 0.1.0-preview.1\n")

        for file in sorted do
            content
                .Append(if file.Executable then 'x' else 'f')
                .Append(' ')
                .Append(file.Length.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(file.Digest.Value)
                .Append(' ')
                .Append(file.Path.Text)
                .Append('\n')
            |> ignore

        PackageManifest(Encoding.ASCII.GetBytes(content.ToString()), sorted)

    /// Refuses noncanonical spellings before accepting the recorded tree digest.
    static member Read(bytes: byte array, expected: Sha256Digest) =
        let result =
            PackagePaths.result (fun () ->
                let bad () =
                    Operations.fail
                        "package.tree_changed"
                        "Manifest is not a canonical package tree."

                if
                    bytes.Length > 2
                                   * 1024
                                   * 1024
                    || bytes
                       |> Array.exists (fun b ->
                           b > 126uy
                           || (b < 32uy
                               && b <> 10uy)
                       )
                then
                    bad ()

                let text = Encoding.ASCII.GetString bytes
                let lines = text.Split '\n'

                if
                    lines.Length < 2
                    || lines[0]
                       <> "morphir-package-tree 0.1.0-preview.1"
                    || lines[lines.Length
                             - 1]
                       <> ""
                then
                    bad ()

                if
                    lines.Length
                    - 2 > 4096
                then
                    bad ()

                let seen = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                let regularFiles = HashSet<string>(StringComparer.Ordinal)
                let mutable previous = ""
                let mutable total = 0L

                let files = [|
                    for line in
                        lines[1 .. lines.Length
                                   - 2] do
                        let parts = line.Split([| ' ' |], 4, StringSplitOptions.None)

                        if
                            parts.Length
                            <> 4
                            || (parts[0]
                                <> "f"
                                && parts[0]
                                   <> "x")
                        then
                            bad ()

                        let mutable length = 0L

                        if
                            not (
                                Int64.TryParse(
                                    parts[1],
                                    NumberStyles.None,
                                    CultureInfo.InvariantCulture,
                                    &length
                                )
                            )
                            || length.ToString(CultureInfo.InvariantCulture)
                               <> parts[1]
                            || length > InstallationLimits.MaxArtifactBytes
                        then
                            bad ()

                        total <-
                            total
                            + length

                        if total > InstallationLimits.MaxArchiveBytes then
                            bad ()

                        let path = PackagePaths.read "package.tree_changed" parts[3]

                        if
                            StringComparer.Ordinal.Compare(previous, path.Text)
                            >= 0
                        then
                            bad ()

                        previous <- path.Text
                        let components = path.Text.Split '/'

                        for i in 1 .. components.Length do
                            let prefix = String.Join('/', components[0 .. i - 1])

                            if regularFiles.Contains prefix then
                                bad ()

                            match seen.TryGetValue prefix with
                            | true, spelling when
                                spelling
                                <> prefix
                                || i = components.Length
                                ->
                                bad ()
                            | _ -> seen[prefix] <- prefix

                        let digest =
                            match Sha256Digest.Parse parts[2] with
                            | Ok value -> value
                            | Error _ ->
                                bad ()
                                Unchecked.defaultof<Sha256Digest>

                        regularFiles.Add path.Text
                        |> ignore

                        yield PackageFile(path, length, digest, parts[0] = "x")
                |]

                if
                    Digest.ofBytes bytes
                    <> expected.Value
                then
                    Operations.fail
                        "package.tree_digest_mismatch"
                        "Canonical manifest digest differs from package.treeSha256."

                PackageManifest(Array.copy bytes, files)
            )

        InstallationResult result

    /// Writes through the existing canonical writer, then validates the complete owned file inventory.
    /// C# callers supply a read-only collection; the returned manifest retains its own copy.
    static member FromFiles(files: IReadOnlyList<PackageFile>) =
        let result =
            PackagePaths.result (fun () ->
                if files.Count > 4096 then
                    Operations.fail "package.tree_changed" "Too many regular package files."

                let manifest =
                    PackageManifest.Create(
                        files
                        |> Seq.toArray
                    )

                let bytes = manifest.ToBytes()

                PackageManifest
                    .Read(bytes, Sha256Digest.Create(Digest.ofBytes bytes))
                    .Match(
                        Func<PackageManifest, PackageManifest>(id),
                        Func<InstallationFailure, PackageManifest>(fun failure ->
                            raise (InstallationException failure)
                        )
                    )
            )

        InstallationResult result

/// An installed immutable tree, without a claim that catalog registration or readiness probing occurred.
[<Sealed>]
type InstalledPackage internal (path: string, plan: PackagePlan, manifest: PackageManifest) =
    /// The absolute immutable store-object directory. This metadata does not retain an activation lease.
    member _.DirectoryPath = path

    /// The absolute entry-point spelling verified in this store object.
    member _.EntryPointPath =
        Path.Combine(path, "tree", plan.EntryPoint.Text.Replace('/', Path.DirectorySeparatorChar))

    /// The canonical manifest identity of the store object.
    member _.TreeDigest = plan.TreeDigest
    /// The identity of the archive from which the object was installed.
    member _.ArchiveDigest = plan.ArchiveDigest
    /// Immutable manifest observations of the verified object.
    member _.Manifest = manifest

    /// Diagnostic text identifying the store object and its path.
    override _.ToString() =
        $"Installed package {plan.TreeDigest.Value} at {path}."

/// Owned shared package lease. The connector must retain this until its whole child tree and pumps settle.
[<Sealed>]
type PackageActivation internal (installed: InstalledPackage, resources: IDisposable array) =
    let mutable disposed = 0
    /// Verified package metadata. Retain this activation until its child tree and I/O pumps settle.
    member _.Package = installed
    /// The verified entry-point path. Do not launch through this lifetime after disposal.
    member _.EntryPointPath = installed.EntryPointPath

    /// Diagnostic text identifying the package whose shared lease this object owns.
    override _.ToString() =
        $"Package lease for {installed.TreeDigest.Value}."

    member private _.Close() =
        if Interlocked.Exchange(&disposed, 1) = 0 then
            for resource in Array.rev resources do
                resource.Dispose()

    interface IDisposable with
        member this.Dispose() = this.Close()

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            this.Close()
            ValueTask.CompletedTask

module internal PackageTreeIO =
    let unwrap (result: InstallationResult<'T>) =
        result.Match(
            Func<'T, 'T>(id),
            Func<InstallationFailure, 'T>(fun failure -> raise (InstallationException failure))
        )

    let directories (owned: ResizeArray<IDisposable>) (root: SafeDirectory) parts create =
        let mutable current = root

        for name in parts do
            let child = current.Child(name, create)
            owned.Add child
            current <- child

        current

    let close (owned: ResizeArray<IDisposable>) =
        for resource in Seq.rev owned do
            resource.Dispose()

    let stageDirectories
        (owned: ResizeArray<IDisposable>)
        (created: Dictionary<string, StageIdentity>)
        (root: SafeDirectory)
        parts
        =
        let mutable current = root

        for name in parts do
            let path = Path.Combine(current.Path, name)

            if
                not (created.ContainsKey path)
                && current.Entries()
                   |> Array.contains name
            then
                Operations.fail
                    "extension.path_escape"
                    "An unowned directory appeared in the private stage."

            let child = current.Child(name, true)
            owned.Add child

            match created.TryGetValue path with
            | true, identity when
                identity
                <> child.Identity
                ->
                Operations.fail "extension.path_escape" "Private stage directory identity changed."
            | _ -> created[path] <- child.Identity

            current <- child

        current

    let mode (path: string) executable =
        if OperatingSystem.IsWindows() then
            File.SetAttributes(
                path,
                File.GetAttributes(path)
                ||| FileAttributes.ReadOnly
            )
        else
            File.SetUnixFileMode(
                path,
                if executable then
                    enum<UnixFileMode> 0o555
                else
                    enum<UnixFileMode> 0o444
            )

    let verifyMode (path: string) executable =
        if OperatingSystem.IsWindows() then
            if
                File.GetAttributes(path)
                &&& FileAttributes.ReadOnly = enum<FileAttributes> 0
            then
                Operations.fail "package.tree_changed" "Package file is writable."
        elif
            File.GetUnixFileMode(path)
            <> (if executable then
                    enum<UnixFileMode> 0o555
                else
                    enum<UnixFileMode> 0o444)
        then
            Operations.fail
                "package.tree_changed"
                "Package file mode differs from the canonical executable declaration."

    let verifyExecutableDeclaration (plan: PackagePlan) isRegularFile =
        if
            plan.Artifact.Platform
            |> Option.exists (fun platform -> platform.Contains "windows")
        then
            if not (plan.EntryPoint.Text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) then
                Operations.fail
                    "package.entry_point_not_exe"
                    "Windows entry point must end in .exe."

        for executable in plan.Executables do
            if not (isRegularFile executable.Text) then
                Operations.fail "package.executable_missing" "An additional executable is absent."

    let verify (directory: SafeDirectory) (plan: PackagePlan) fullHash (ct: CancellationToken) =
        let fail message =
            Operations.fail "package.tree_changed" message

        let top =
            directory.Entries()
            |> Array.sort

        if
            top
            <> [| "manifest"; "tree" |]
        then
            fail "Package root has missing or foreign entries."

        use manifestInput = directory.File("manifest", false, false)

        if
            manifestInput.Length > 2L
                                   * 1024L
                                   * 1024L
        then
            fail "Manifest exceeds canonical bounds."

        let bytes = Array.zeroCreate<byte> (int manifestInput.Length)
        manifestInput.ReadExactly bytes

        let manifest =
            PackageManifest.Read(bytes, plan.TreeDigest)
            |> unwrap

        verifyMode (Path.Combine(directory.Path, "manifest")) false
        use tree = directory.Child("tree", false)
        let expected = Dictionary<string, PackageFile>(StringComparer.Ordinal)
        let expectedDirectories = HashSet<string>(StringComparer.Ordinal)

        for file in manifest.Files do
            expected.Add(file.Path.Text, file)
            let parts = file.Path.Text.Split '/'

            for i in
                1 .. parts.Length
                     - 1 do
                expectedDirectories.Add(String.Join('/', parts[0 .. i - 1]))
                |> ignore

        let seen = HashSet<string>(StringComparer.Ordinal)

        let rec walk (current: SafeDirectory) prefix =
            ct.ThrowIfCancellationRequested()

            for name in current.Entries() do
                let relative =
                    if prefix = "" then
                        name
                    else
                        prefix
                        + "/"
                        + name

                if expectedDirectories.Contains relative then
                    use child = current.Child(name, false)

                    if
                        not (OperatingSystem.IsWindows())
                        && File.GetUnixFileMode(child.Path)
                           <> enum<UnixFileMode> 0o555
                    then
                        fail "Package directory mode changed."

                    walk child relative
                else
                    match expected.TryGetValue relative with
                    | false, _ -> fail $"Unexpected package entry: {relative}"
                    | true, file ->
                        use input = current.File(name, false, false)
                        let identity = current.FileIdentity input

                        if
                            input.Length
                            <> file.Length
                        then
                            fail $"Package file size changed: {relative}"

                        verifyMode (Path.Combine(current.Path, name)) file.Executable

                        if fullHash then
                            let digest, count =
                                Copying.hashCopy
                                    input
                                    Stream.Null
                                    InstallationLimits.MaxArtifactBytes
                                    "package.file_too_large"
                                    ct

                            if
                                digest
                                <> file.Digest.Value
                                || count
                                   <> file.Length
                            then
                                fail $"Package file bytes changed: {relative}"

                        if not (current.HasFileIdentity(name, identity)) then
                            fail "Package file identity changed during verification."

                        seen.Add relative
                        |> ignore

            current.ValidateLocation()

        walk tree ""

        if
            seen.Count
            <> expected.Count
        then
            fail "Package files are missing."

        if not (expected.ContainsKey plan.EntryPoint.Text) then
            fail "Declared entry point is absent."

        verifyExecutableDeclaration plan expected.ContainsKey

        let executable =
            HashSet<string>(
                plan.Executables
                |> Seq.map (fun p -> p.Text),
                StringComparer.Ordinal
            )

        executable.Add plan.EntryPoint.Text
        |> ignore

        for file in manifest.Files do
            if
                file.Executable
                <> executable.Contains file.Path.Text
            then
                fail "Manifest modes do not match package declaration."

        InstalledPackage(directory.Path, plan, manifest)

    let crcTable =
        Array.init
            256
            (fun index ->
                let mutable value = uint32 index

                for _ in 1..8 do
                    value <-
                        if
                            value
                            &&& 1u
                            <> 0u
                        then
                            0xEDB88320u
                            ^^^ (value
                                 >>> 1)
                        else
                            value
                            >>> 1

                value
            )

    let extractZip
        (source: FileStream)
        (tree: SafeDirectory)
        (plan: PackagePlan)
        (created: Dictionary<string, StageIdentity>)
        (ct: CancellationToken)
        =
        let records = ArchiveChecks.zipRecordsWithPolicy source true
        let names = Dictionary<string, bool>(StringComparer.Ordinal)
        let spellings = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        let normalized = ResizeArray<string option>()

        let root =
            plan.Root
            |> Option.map (fun value ->
                value
                + "/"
            )

        for record in records do
            let original = record.Name

            if original.Length > 240 then
                Operations.fail "package.path" "Archive path exceeds 240 ASCII bytes."

            let stripped =
                if original.StartsWith("./", StringComparison.Ordinal) then
                    original.Substring 2
                else
                    original

            let stripped =
                if
                    record.Directory
                    && stripped.EndsWith '/'
                then
                    stripped.Substring(
                        0,
                        stripped.Length
                        - 1
                    )
                else
                    stripped

            if
                stripped = ""
                && original = "./"
            then
                normalized.Add None
            else
                PackagePaths.read "package.path" stripped
                |> ignore

                let relative =
                    match root with
                    | Some prefix when
                        stripped
                        + "/" = prefix
                        && record.Directory
                        ->
                        None
                    | Some prefix when stripped.StartsWith(prefix, StringComparison.Ordinal) ->
                        Some(stripped.Substring prefix.Length)
                    | Some _ ->
                        Operations.fail "package.root" "Entry is outside the declared package root."
                    | None -> Some stripped

                match relative with
                | None -> normalized.Add None
                | Some relative ->
                    PackagePaths.read "package.path" relative
                    |> ignore

                    if names.ContainsKey relative then
                        Operations.fail "package.duplicate_path" "Duplicate package entry."

                    let parts = relative.Split '/'

                    for i in 1 .. parts.Length do
                        let prefix = String.Join('/', parts[0 .. i - 1])

                        match spellings.TryGetValue prefix with
                        | true, existing when
                            existing
                            <> prefix
                            ->
                            Operations.fail
                                "package.case_collision"
                                "ASCII case collision in package path."
                        | _ -> spellings[prefix] <- prefix

                        match names.TryGetValue prefix with
                        | true, false when i < parts.Length ->
                            Operations.fail
                                "package.path_conflict"
                                "A file is also a parent directory."
                        | _ -> ()

                    if
                        not record.Directory
                        && names.Keys
                           |> Seq.exists (fun name ->
                               name.StartsWith(
                                   relative
                                   + "/",
                                   StringComparison.Ordinal
                               )
                           )
                    then
                        Operations.fail
                            "package.path_conflict"
                            "A file replaces a parent directory."

                    names.Add(relative, record.Directory)
                    normalized.Add(Some relative)

        if not (names.ContainsKey plan.EntryPoint.Text) then
            Operations.fail "package.entry_point_missing" "Entry point is absent."

        if names[plan.EntryPoint.Text] then
            Operations.fail "package.entry_point_missing" "Entry point is a directory."

        verifyExecutableDeclaration
            plan
            (fun path ->
                match names.TryGetValue path with
                | true, false -> true
                | _ -> false
            )

        source.Position <- 0L
        use zip = new ZipArchive(source, ZipArchiveMode.Read, true)

        if
            zip.Entries.Count
            <> records.Length
        then
            Operations.fail
                "package.zip_header_mismatch"
                "Archive reader and validated records differ."

        let files = ResizeArray<PackageFile>()
        let mutable total = 0L

        for i in
            0 .. records.Length
                 - 1 do
            ct.ThrowIfCancellationRequested()

            match normalized[i] with
            | None -> ()
            | Some relative ->
                let record = records[i]
                let entry = zip.Entries[i]

                if
                    entry.FullName
                    <> record.Name
                then
                    Operations.fail "package.zip_header_mismatch" "ZIP effective name differs."

                let owned = ResizeArray<IDisposable>()

                try
                    let parts = relative.Split '/'

                    // Directory headers are validated above. Materialize directories
                    // only when a regular file requires them, never from empty entries.
                    if not record.Directory then
                        let parent =
                            stageDirectories
                                owned
                                created
                                tree
                                (parts[0 .. parts.Length
                                            - 2]
                                 |> Array.toList)

                        let name =
                            parts[parts.Length
                                  - 1]

                        use output = parent.File(name, true, false)
                        created[Path.Combine(parent.Path, name)] <- parent.FileIdentity output
                        use input = entry.Open()
                        let hash = Digest.Incremental()
                        let buffer = Array.zeroCreate<byte> 65536
                        let mutable length = 0L
                        let mutable crc = UInt32.MaxValue
                        let mutable reading = true

                        while reading do
                            ct.ThrowIfCancellationRequested()
                            let count = input.Read(buffer, 0, buffer.Length)

                            if count = 0 then
                                reading <- false
                            else
                                length <-
                                    length
                                    + int64 count

                                total <-
                                    total
                                    + int64 count

                                if length > InstallationLimits.MaxArtifactBytes then
                                    Operations.fail
                                        "package.file_too_large"
                                        "Package file exceeds 256 MiB."

                                if total > InstallationLimits.MaxArchiveBytes then
                                    Operations.fail
                                        "package.expanded_too_large"
                                        "Package expansion exceeds 1 GiB."

                                hash.Append(buffer, 0, count)

                                for j in 0 .. count - 1 do
                                    crc <-
                                        crcTable[int (
                                                     (crc
                                                      ^^^ uint32 buffer[j])
                                                     &&& 255u
                                                 )]
                                        ^^^ (crc >>> 8)

                                output.Write(buffer, 0, count)

                        if
                            length
                            <> record.Length
                            || (crc
                                ^^^ UInt32.MaxValue)
                               <> record.Crc
                        then
                            Operations.fail
                                "package.zip_header_mismatch"
                                "ZIP actual file size or CRC differs."

                        output.Flush true

                        let executable =
                            relative = plan.EntryPoint.Text
                            || plan.Executables
                               |> Seq.exists (fun path -> path.Text = relative)

                        mode (Path.Combine(parent.Path, name)) executable

                        files.Add(
                            PackageFile(
                                PackagePaths.read "package.path" relative,
                                length,
                                Sha256Digest.Create(hash.Finish()),
                                executable
                            )
                        )
                finally
                    close owned

        let manifest = PackageManifest.Create(files.ToArray())

        if
            Digest.ofBytes (manifest.ToBytes())
            <> plan.TreeDigest.Value
        then
            Operations.fail
                "package.tree_digest_mismatch"
                "Extracted canonical tree differs from package.treeSha256."

        manifest

    let sealDirectories (tree: SafeDirectory) =
        let rec seal (directory: SafeDirectory) =
            for name in directory.Entries() do
                let path = Path.Combine(directory.Path, name)

                if
                    File.GetAttributes(path)
                    &&& FileAttributes.Directory
                    <> enum<FileAttributes> 0
                then
                    use child = directory.Child(name, false)
                    seal child

            directory.ValidateLocation()

            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(directory.Path, enum<UnixFileMode> 0o555)

        seal tree

    let removeStage
        (packages: SafeDirectory)
        name
        identity
        (created: Dictionary<string, StageIdentity>)
        =
        use stage = packages.Child(name, false)

        if
            stage.Identity
            <> identity
        then
            Operations.fail
                "extension.path_escape"
                "Private package stage identity changed; retained."

        let rec remove (directory: SafeDirectory) =
            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(directory.Path, enum<UnixFileMode> 0o755)

            for name in directory.Entries() do
                let path = Path.Combine(directory.Path, name)

                let expected =
                    match created.TryGetValue path with
                    | true, identity -> identity
                    | _ ->
                        Operations.fail
                            "extension.path_escape"
                            $"Unowned private-stage entry retained: {path}"

                let attributes = File.GetAttributes path

                if
                    attributes
                    &&& FileAttributes.ReparsePoint
                    <> enum<FileAttributes> 0
                then
                    Operations.fail
                        "extension.path_escape"
                        "Private stage contains a link; retained."
                elif
                    attributes
                    &&& FileAttributes.Directory
                    <> enum<FileAttributes> 0
                then
                    do
                        use child = directory.Child(name, false)

                        if
                            child.Identity
                            <> expected
                        then
                            Operations.fail
                                "extension.path_escape"
                                "Replaced private-stage directory retained."

                        remove child

                    directory.DeleteDirectory name
                else
                    use input = directory.File(name, false, false)
                    let fileIdentity = directory.FileIdentity input

                    if
                        fileIdentity
                        <> expected
                    then
                        Operations.fail
                            "extension.path_escape"
                            "Replaced private-stage file retained."

                    input.Dispose()
                    File.SetAttributes(path, FileAttributes.Normal)
                    directory.DeleteIfIdentity(name, fileIdentity)

        remove stage
        (stage :> IDisposable).Dispose()
        packages.DeleteDirectory name

/// Package tree operations. Catalog/lock registration and selected-record capture are separate, unfinished work.
[<AbstractClass; Sealed>]
type PackageInstaller private () =
    /// Validates, extracts and publishes a ZIP tree. This does not mark an extension ready or write selection metadata.
    static member InstallAsync
        (
            home: string,
            plan: PackagePlan,
            archivePath: string,
            ct: CancellationToken
        ) : Task<InstallationResult<InstalledPackage>> =
        Operations.runAsync
            ct
            (fun () -> task {
                use input = FileSafety.openInput archivePath

                if input.Length > InstallationLimits.MaxArtifactBytes then
                    Operations.fail "package.archive_too_large" "Package archive exceeds 256 MiB."

                let digest, _ =
                    Copying.hashCopy
                        input
                        Stream.Null
                        InstallationLimits.MaxArtifactBytes
                        "package.archive_too_large"
                        ct

                if
                    digest
                    <> plan.ArchiveDigest.Value
                then
                    Operations.fail
                        "package.archive_digest_mismatch"
                        "Archive bytes differ from the release descriptor."

                input.Position <- 0L
                let signature = Array.zeroCreate<byte> 4
                let count = input.Read(signature, 0, 4)
                input.Position <- 0L

                if
                    plan.Format
                    <> "zip"
                then
                    Operations.fail
                        "package.format_not_implemented"
                        "Package tar.gz extraction is not implemented yet."

                if not (ArchiveChecks.isZip signature count) then
                    Operations.fail
                        "package.archive_format"
                        "Archive bytes do not match declared ZIP format."

                use root = SafeDirectory.Open(home, true)
                do! StateOwnership.checkRepositoriesAsync root ct
                let owned = ResizeArray<IDisposable>()

                try
                    let packages =
                        PackageTreeIO.directories
                            owned
                            root
                            [ "store"; "extensions"; "packages" ]
                            true

                    let store = PackageTreeIO.directories owned packages [ "sha256" ] true

                    let name =
                        ".stage-"
                        + Guid.NewGuid().ToString("N")

                    use owner =
                        packages.NamedLease(
                            name
                            + ".owner",
                            true,
                            false
                        )

                    let mutable stageIdentity = None
                    let created = Dictionary<string, StageIdentity>(StringComparer.Ordinal)
                    let ownerIdentity = packages.FileIdentity owner
                    let mutable published = false
                    let mutable primary: exn option = None

                    try
                        try
                            do
                                // Unix cannot move a sealed directory to another parent
                                // without write permission to update its '..' entry.
                                // Stage beside the destination so publication stays sealed.
                                use stage = store.Child(name, true)
                                stageIdentity <- Some stage.Identity
                                use tree = stage.Child("tree", true)
                                created[tree.Path] <- tree.Identity

                                let manifest =
                                    try
                                        PackageTreeIO.extractZip input tree plan created ct
                                    with
                                    | :? InvalidDataException as error ->
                                        ArchiveChecks.unsafe error.Message
                                    | :? EndOfStreamException as error ->
                                        ArchiveChecks.unsafe error.Message
                                    | :? NotSupportedException as error ->
                                        ArchiveChecks.unsafe error.Message

                                use manifestOutput = stage.File("manifest", true, false)

                                created[Path.Combine(stage.Path, "manifest")] <-
                                    stage.FileIdentity manifestOutput

                                manifestOutput.Write(manifest.ToBytes())
                                manifestOutput.Flush true
                                PackageTreeIO.mode (Path.Combine(stage.Path, "manifest")) false
                                PackageTreeIO.sealDirectories stage

                            ct.ThrowIfCancellationRequested()
                            use locks = root.Child("locks", true)

                            use! state =
                                locks.NamedLeaseAsync("extensions.state.lock", true, true, ct)

                            ct.ThrowIfCancellationRequested()
                            StateOwnership.checkUnderLock root ct
                            // A stable sibling lease is created before publication and never removed.
                            use treeLease =
                                store.NamedLease(
                                    plan.TreeDigest.Value
                                    + ".lease",
                                    false,
                                    false
                                )

                            let exists =
                                store.Entries()
                                |> Array.contains plan.TreeDigest.Value

                            if not exists then
                                use stage = store.Child(name, false)

                                if
                                    Some stage.Identity
                                    <> stageIdentity
                                then
                                    Operations.fail
                                        "extension.path_escape"
                                        "Private stage was replaced."

                                PackageTreeIO.verify stage plan true ct
                                |> ignore

                                (stage :> IDisposable).Dispose()

                                published <-
                                    store.PublishDirectory(store, name, plan.TreeDigest.Value)

                            use final = store.Child(plan.TreeDigest.Value, false)
                            // Actual destination verification is required even when a winner already existed.
                            return PackageTreeIO.verify final plan true CancellationToken.None
                        with error ->
                            primary <- Some error
                            return raise error
                    finally
                        let mutable cleanupFailure: exn option = None

                        try
                            if not published then
                                match stageIdentity with
                                | Some identity ->
                                    PackageTreeIO.removeStage store name identity created
                                | None -> ()
                        with error ->
                            cleanupFailure <- Some error

                        owner.Dispose()

                        if cleanupFailure.IsNone then
                            packages.DeleteIfIdentity(
                                name
                                + ".owner",
                                ownerIdentity
                            )

                        match cleanupFailure with
                        | None -> ()
                        | Some cleanup ->
                            let code, message =
                                match primary with
                                | Some(InstallationException failure) ->
                                    failure.Code, failure.Message
                                | Some(:? OperationCanceledException) ->
                                    "extension.cancelled", "Package operation was cancelled."
                                | Some error -> "extension.io_error", error.Message
                                | None ->
                                    "extension.cleanup_deferred",
                                    "Package tree operation completed."

                            Operations.fail
                                code
                                $"{message} Private cleanup deferred at {Path.Combine(store.Path, name)}; ownership released: {cleanup.Message}"
                finally
                    PackageTreeIO.close owned
            })

    /// VERIFY-B structure validation and a retained shared lease, independent of global catalog lookup.
    /// This low-level API does not replace installer validation of retained catalog and lock records.
    static member private AcquireCore
        (
            home: string,
            plan: PackagePlan,
            fullHash: bool,
            ct: CancellationToken
        ) : Task<InstallationResult<PackageActivation>> =
        Operations.runAsync
            ct
            (fun () -> task {
                let owned = ResizeArray<IDisposable>()

                if not fullHash then
                    ActivationPlatforms.ensure plan.Artifact.Platform
                    |> StateOwnership.unwrap

                try
                    let root = SafeDirectory.Open(home, false)
                    owned.Add root
                    do! StateOwnership.checkRepositoriesAsync root ct

                    let store =
                        PackageTreeIO.directories
                            owned
                            root
                            [ "store"; "extensions"; "packages"; "sha256" ]
                            false

                    use locks = root.Child("locks", true)
                    use! state = locks.NamedLeaseAsync("extensions.state.lock", true, true, ct)
                    ct.ThrowIfCancellationRequested()
                    StateOwnership.checkUnderLock root ct

                    let lease =
                        store.NamedLease(
                            plan.TreeDigest.Value
                            + ".lease",
                            false,
                            false
                        )

                    owned.Add lease
                    let directory = store.Child(plan.TreeDigest.Value, false)
                    owned.Add directory
                    let installed = PackageTreeIO.verify directory plan fullHash ct
                    return new PackageActivation(installed, owned.ToArray())
                with error ->
                    PackageTreeIO.close owned
                    return raise error
            })

    /// Acquires a VERIFY-B shared lifetime; this deliberately does not rehash every content file.
    static member AcquireAsync(home: string, plan: PackagePlan, ct: CancellationToken) =
        PackageInstaller.AcquireCore(home, plan, false, ct)

    /// Explicitly rehashes every installed regular file while retaining a shared lease.
    static member VerifyAsync
        (
            home: string,
            plan: PackagePlan,
            ct: CancellationToken
        ) : Task<InstallationResult<InstalledPackage>> =
        task {
            let! result = PackageInstaller.AcquireCore(home, plan, true, ct)

            return
                result.Match(
                    Func<PackageActivation, InstallationResult<InstalledPackage>>(fun activation ->
                        try
                            InstallationResult(Ok activation.Package)
                        finally
                            (activation :> IDisposable).Dispose()
                    ),
                    Func<InstallationFailure, InstallationResult<InstalledPackage>>(fun error ->
                        InstallationResult(Error error)
                    )
                )
        }

/// Prepares the existing release format for a complete-folder producer archive.
/// This pure writer makes no installation, readiness or authenticated-provenance claim.
[<AbstractClass; Sealed>]
type PackageRelease private () =
    /// Binds an existing valid release descriptor to the actual archive and canonical file inventory.
    /// Whole-folder archives have no enclosing package root. Identity and unknown optional metadata are retained.
    static member PrepareWholeArchive
        (
            releaseBytes: byte array,
            platform: InstallationPlatform,
            archiveName: TargetPath,
            archiveDigest: Sha256Digest,
            manifest: PackageManifest,
            entryPoint: TargetPath
        ) =
        InstallationResult(
            PackagePaths.result (fun () ->
                let descriptor =
                    Descriptors.read releaseBytes
                    |> StateOwnership.unwrap

                SharedJson.protect (fun () ->
                    let replace name value items =
                        if
                            items
                            |> List.exists (fun (key, _) -> key = name)
                        then
                            items
                            |> List.map (fun (key, current) ->
                                key, (if key = name then value else current)
                            )
                        else
                            items
                            @ [ name, value ]

                    let matching =
                        descriptor.Artifacts
                        |> List.filter (fun artifact ->
                            artifact.Platform = Some platform.DescriptorTriple
                        )

                    match matching with
                    | [ artifact ] when artifact.Runtime = ArtifactRuntime.Process -> ()
                    | _ ->
                        SharedJson.fail
                            "extension.no_matching_artifact"
                            "artifacts"
                            "Expected one process artifact for this platform."

                    let archiveName =
                        DistributionIdentifiers.ParseArtifactFileName archiveName.Text
                        |> StateOwnership.unwrap

                    let format =
                        if
                            archiveName.Text.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        then
                            "zip"
                        elif
                            archiveName.Text.EndsWith(
                                ".tar.gz",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || archiveName.Text.EndsWith(
                                ".tgz",
                                StringComparison.OrdinalIgnoreCase
                            )
                        then
                            "tar.gz"
                        else
                            SharedJson.fail
                                "package.archive_format"
                                "filename"
                                "Expected a ZIP or tar.gz archive."

                    let entryPoint = PackagePaths.read "package.path" entryPoint.Text

                    let entry =
                        manifest.Files
                        |> Seq.tryFind (fun file -> file.Path = entryPoint)

                    match entry with
                    | None ->
                        Operations.fail
                            "package.entry_point_missing"
                            "Entry point is absent from the canonical manifest."
                    | Some file when not file.Executable ->
                        Operations.fail
                            "package.tree_changed"
                            "Entry point must be declared executable."
                    | _ -> ()

                    let additional =
                        manifest.Files
                        |> Seq.filter (fun file ->
                            file.Executable
                            && file.Path
                               <> entryPoint
                        )
                        |> Seq.map (fun file -> Value.String file.Path.Text)
                        |> Seq.toList

                    let root = SharedJson.members "release.json" descriptor.Json

                    let artifacts =
                        SharedJson.required "release.json" "artifacts" root
                        |> SharedJson.array "artifacts"
                        |> List.map (fun value ->
                            let items = SharedJson.members "artifact" value

                            if
                                SharedJson.optional "platform" items
                                <> Some(Value.String platform.DescriptorTriple)
                            then
                                value
                            else
                                let previousPackage =
                                    SharedJson.optional "package" items
                                    |> Option.map (SharedJson.members "package")
                                    |> Option.defaultValue []
                                    |> List.filter (fun (name, _) ->
                                        name
                                        <> "root"
                                    )

                                let declaration =
                                    previousPackage
                                    |> replace "format" (Value.String format)
                                    |> replace "entryPoint" (Value.String entryPoint.Text)
                                    |> replace "executables" (Value.Array additional)
                                    |> replace
                                        "treeSha256"
                                        (Value.String(Digest.ofBytes (manifest.ToBytes())))

                                items
                                |> replace "filename" (Value.String archiveName.Text)
                                |> replace "sha256" (Value.String archiveDigest.Value)
                                |> replace "package" (Value.Object declaration)
                                |> Value.Object
                        )

                    let critical =
                        SharedJson.optional "critical" root
                        |> Option.map (SharedJson.strings "critical")
                        |> Option.defaultValue []

                    let critical =
                        if List.contains "artifacts.package" critical then
                            critical
                        else
                            critical
                            @ [ "artifacts.package" ]

                    let updated =
                        root
                        |> replace "artifacts" (Value.Array artifacts)
                        |> replace "critical" (Value.Array(List.map Value.String critical))

                    let schema = SharedJson.required "release.json" "$schema" updated

                    let bytes =
                        Value.Object(
                            ("$schema", schema)
                            :: (updated
                                |> List.filter (fun (name, _) ->
                                    name
                                    <> "$schema"
                                ))
                        )
                        |> JsonText.toBytes

                    let plan =
                        PackagePlan.Read(bytes, platform)
                        |> PackageTreeIO.unwrap

                    PackageTreeIO.verifyExecutableDeclaration
                        plan
                        (fun path ->
                            manifest.Files
                            |> Seq.exists (fun file -> file.Path.Text = path)
                        )

                    bytes
                )
                |> StateOwnership.unwrap
            )
        )
