// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module internal Morphirup.LocalInstallation

open System
open System.Collections.Generic
open System.IO
open System.IO.Compression
open System.Formats.Tar
open System.Reflection.PortableExecutable
open System.Text.Json
open System.Threading
open Morphir.Toolchain
open Morphir.Extensions.Installation
open LocalCandidate

let rec private captureStageEntries (created: Dictionary<string,StageIdentity>) (directory: SafeDirectory) =
    for name in directory.Entries() do
        let path = Path.Combine(directory.Path,name)
        let attributes = File.GetAttributes path
        if attributes.HasFlag FileAttributes.ReparsePoint then
            refuse "local.path_escape" "A private setup stage contains a link; cleanup was refused."
        if attributes.HasFlag FileAttributes.Directory then
            use child = directory.Child(name,false)
            created[path] <- child.Identity
            captureStageEntries created child
        else
            use input = directory.File(name,false,false)
            created[path] <- directory.FileIdentity input

// Only this live invocation owns the stage. Published objects have already moved out before cleanup.
let withPrivateStageAsync (root: SafeDirectory) (stage: SafeDirectory) (owner: FileStream) (errors: TextWriter) operation =
    let name = Path.GetFileName stage.Path
    let identity = stage.Identity
    let ownerIdentity = stage.FileIdentity owner
    let parentIdentity =
        use parent = root.Child("tmp",false)
        if stage.Path <> Path.Combine(parent.Path,name) || not(name.StartsWith("local-toolchain-",StringComparison.Ordinal)) then
            refuse "local.path_escape" "Cleanup is restricted to this invocation's private setup stage."
        parent.Identity
    let cleanup = {
        new IAsyncDisposable with
            member _.DisposeAsync() = System.Threading.Tasks.ValueTask(task {
                owner.Dispose()
                (stage :> IDisposable).Dispose()
                use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 5.)
                let! acquired = task {
                    try
                        let! lease = FileSafety.mutationLeaseAsync root deadline.Token
                        return Some lease
                    with :? OperationCanceledException when deadline.IsCancellationRequested ->
                        return None
                }
                match acquired with
                | None ->
                    let folder = stage.Path.Replace("\r","\\r").Replace("\n","\\n")
                    try
                        errors.WriteLine($"morphirup: cleanup deferred; staging folder retained because the mutation lease is busy: '{folder}'")
                    with
                    | :? IOException | :? ObjectDisposedException -> ()
                | Some lease ->
                    use lease = lease
                    use parent = root.Child("tmp",false)
                    if parent.Identity <> parentIdentity then refuse "local.path_escape" "The private staging parent changed identity."
                    let created = Dictionary<string,StageIdentity>(StringComparer.Ordinal)
                    do
                        use current = parent.Child(name,false)
                        if current.Identity <> identity || not(current.HasFileIdentity(".owner",ownerIdentity)) then
                            refuse "local.path_escape" "The private setup stage changed identity; cleanup was refused."
                        captureStageEntries created current
                    PackageTreeIO.removeStage parent name identity created
            })
    }
    Morphir.Toolchain.IO.AsyncOwnership.runAsync cleanup operation

let checkExecutablePaths platform home (paths: string seq) =
    match platform with
    | Platform.WinX64 | Platform.WinArm64 ->
        for path in paths do
            // CreateProcessW's application path includes a terminating null within MAX_PATH.
            if path.Length >= 260 then
                refuse "local.executable_path_too_long"
                    $"Home '{home}' produces an installed executable path of {path.Length} characters: '{path}'. The Windows launch limit is 259 characters, excluding the terminating null. Choose a shorter --home."
    | _ -> ()

let maximumArchive = 1UL <<< 30
let documentBound = 8L <<< 20

let private parts (path: string) =
    path.Split('/')
    |> Array.toList

let private relative (root: string) (path: string) =
    Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))

let private checkedPlan platform candidate =
    match
        LocalCandidatePlanning.plan
            platform
            (Some LocalConsent.LocalDeveloper)
            maximumArchive
            candidate
    with
    | Ok plan -> plan
    | Error error -> refuse "local.candidate_refused" (string error)

let private copyChecked
    (source: Stream)
    (output: Stream option)
    expectedLength
    (expected: Sha256)
    (ct: CancellationToken)
    =
    let digest = Digest.Incremental()
    let buffer = Array.zeroCreate<byte> 65536
    let mutable total = 0UL
    let mutable reading = true

    while reading do
        ct.ThrowIfCancellationRequested()

        let count =
            source.Read(
                buffer,
                0,
                int (
                    min
                        (uint64 buffer.Length)
                        (expectedLength
                         - total
                         + 1UL)
                )
            )

        if count = 0 then
            reading <- false
        else
            if
                uint64 count > expectedLength
                               - total
            then
                refuse "local.length_mismatch" "A stream exceeded its declared length."

            digest.Append(buffer, 0, count)

            output
            |> Option.iter (fun target -> target.Write(buffer, 0, count))

            total <-
                total
                + uint64 count

    if
        total
        <> expectedLength
    then
        refuse "local.length_mismatch" "A stream ended before its declared length."

    if
        digest.Finish()
        <> expected.Text
    then
        refuse "local.digest_mismatch" "Exact streamed SHA-256 differs from the candidate."

// The managed entry point and the native runtime files of one platform's runtime-bundled folder.
let private launchNames platform =
    match platform with
    | Platform.WinX64
    | Platform.WinArm64 -> "morphir.exe", [ "hostfxr.dll"; "hostpolicy.dll"; "coreclr.dll" ]
    | Platform.OsxArm64 -> "morphir", [ "libhostfxr.dylib"; "libhostpolicy.dylib"; "libcoreclr.dylib" ]
    | _ -> refuse "local.platform_mismatch" "Only the win-x64, win-arm64 and osx-arm64 executable platforms are supported."

let private windowsExecutable (candidate: LocalHostCandidate) (image: byte array) =
    let expectedMachine = if candidate.Platform = Platform.WinX64 then Machine.Amd64 else Machine.Arm64
    try
        use stream = new MemoryStream(image, false)
        use pe = new PEReader(stream)

        if
            pe.PEHeaders.CoffHeader.Machine
            <> expectedMachine
            || not (pe.PEHeaders.CoffHeader.Characteristics.HasFlag Characteristics.ExecutableImage)
            || pe.PEHeaders.CoffHeader.Characteristics.HasFlag Characteristics.Dll
        then
            let architecture = if candidate.Platform=Platform.WinX64 then "AMD64" else "ARM64"
            refuse "local.platform_mismatch" $"The actual entry point is not an {architecture} executable."
    with :? BadImageFormatException ->
        refuse "local.platform_mismatch" "The actual entry point is not a Windows executable."

// A thin 64-bit Mach-O header: MH_MAGIC_64, CPU_TYPE_ARM64 and MH_EXECUTE, little-endian. A universal image is refused.
let private macExecutable (image: byte array) =
    let word offset = BitConverter.ToUInt32(image, offset)
    if
        image.Length < 32
        || word 0 <> 0xFEEDFACFu
        || word 4 <> 0x0100000Cu
        || word 12 <> 2u
    then
        refuse "local.platform_mismatch" "The actual entry point is not an ARM64 Mach-O executable."

let private structure (candidate: LocalHostCandidate) (read: string -> byte array) =
    let entryName, nativeNames = launchNames candidate.Platform
    match candidate.Platform with
    | Platform.OsxArm64 -> macExecutable (read candidate.EntryPoint.Target.Text)
    | _ -> windowsExecutable candidate (read candidate.EntryPoint.Target.Text)

    let prefix =
        let p = candidate.EntryPoint.Target.Text
        let slash = p.LastIndexOf '/'
        if slash < 0 then "" else p.Substring(0, slash + 1)

    if
        Path.GetFileName candidate.EntryPoint.Target.Text
        <> entryName
    then
        refuse "local.launch_structure" $"The official managed entry point must be {entryName}."

    let names =
        HashSet<string>(
            candidate.Files
            |> Seq.map (fun f -> f.Artifact.Target.Text),
            StringComparer.Ordinal
        )

    for name in
        [
            "morphir.dll"
            "morphir.deps.json"
            "morphir.runtimeconfig.json"
            yield! nativeNames
            "System.Private.CoreLib.dll"
            "FSharp.Core.dll"
            "FSharp.Compiler.Service.dll"
            "Morphir.CodeGeneration.FSharp.dll"
        ] do
        if
            not (
                names.Contains(
                    prefix
                    + name
                )
            )
        then
            refuse
                "local.launch_structure"
                ("Incomplete runtime-bundled folder: "
                 + name)

    use runtime =
        JsonDocument.Parse(
            read (
                prefix
                + "morphir.runtimeconfig.json"
            )
        )

    let options = runtime.RootElement.GetProperty "runtimeOptions"
    let mutable found = Unchecked.defaultof<JsonElement>

    if
        options.TryGetProperty("framework", &found)
        || options.TryGetProperty("frameworks", &found)
        || not (options.TryGetProperty("includedFrameworks", &found))
    then
        refuse
            "local.launch_structure"
            "The candidate requires a system framework rather than an app-local runtime."

    if
        not (
            found.EnumerateArray()
            |> Seq.exists (fun item ->
                item.GetProperty("name").GetString() = "Microsoft.NETCore.App"
            )
        )
    then
        refuse "local.launch_structure" "No bundled Microsoft.NETCore.App was declared."

    use deps =
        JsonDocument.Parse(
            read (
                prefix
                + "morphir.deps.json"
            )
        )

    let target =
        deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()
        |> Option.ofObj
        |> Option.defaultValue ""

    if not (target.EndsWith("/" + Platform.name candidate.Platform, StringComparison.Ordinal)) then
        refuse "local.platform_mismatch" $"The managed dependency closure is not {Platform.name candidate.Platform}."

let private pinnedRead (root: SafeDirectory) path maximum =
    use file = FileSafety.openRelative root (relative root.Path path)

    if file.Length > maximum then
        refuse "local.document_too_large" "Installed document exceeds its bound."

    use output = new MemoryStream()
    let buffer = Array.zeroCreate<byte> 65536
    let mutable reading = true

    while reading do
        let count = file.Read(buffer, 0, buffer.Length)

        if count = 0 then
            reading <- false
        else
            if
                output.Length
                + int64 count > maximum
            then
                refuse "local.document_too_large" "Installed document grew past its bound."

            output.Write(buffer, 0, count)

    output.ToArray()

let checkFolder (root: SafeDirectory) candidate hashFiles (ct: CancellationToken) =
    let expected = Dictionary<string, LocalHostFile>(StringComparer.Ordinal)

    for file in candidate.Files do
        expected.Add(file.Artifact.Target.Text, file)

    let seen = HashSet<string>(StringComparer.Ordinal)

    let rec walk (directory: SafeDirectory) prefix =
        for name in directory.Entries() do
            ct.ThrowIfCancellationRequested()

            let path =
                prefix
                + name

            let attributes = File.GetAttributes(Path.Combine(directory.Path, name))

            if attributes.HasFlag FileAttributes.ReparsePoint then
                refuse "local.path_escape" "Installed links are refused."

            if attributes.HasFlag FileAttributes.Directory then
                if
                    not (
                        expected.Keys
                        |> Seq.exists (fun item ->
                            item.StartsWith(path + "/", StringComparison.Ordinal)
                        )
                    )
                then
                    refuse
                        "local.closure_mismatch"
                        "An undeclared directory exists in the installed closure."

                use child = directory.Child(name, false)
                walk child (path + "/")
            else
                match expected.TryGetValue path with
                | false, _ ->
                    refuse
                        "local.closure_mismatch"
                        "An undeclared file exists in the installed closure."
                | true, file ->
                    use input = directory.File(name, false, false)

                    if
                        uint64 input.Length
                        <> file.Bytes
                    then
                        refuse "local.length_mismatch" "An installed file has the wrong length."

                    // The Unix entry point runs only with its executable mode, which the archive gave it.
                    if
                        not (OperatingSystem.IsWindows())
                        && path = candidate.EntryPoint.Target.Text
                        && not ((File.GetUnixFileMode input.SafeFileHandle).HasFlag UnixFileMode.UserExecute)
                    then
                        refuse "local.launch_structure" "The installed entry point is not executable."

                    if hashFiles then
                        copyChecked input None file.Bytes file.Artifact.Sha256 ct

                    if not (seen.Add path) then
                        refuse "local.closure_mismatch" "Duplicate installed file."

    walk root ""

    if
        seen.Count
        <> expected.Count
    then
        refuse "local.closure_mismatch" "The installed closure is incomplete."

    structure candidate (fun name -> pinnedRead root name documentBound)

type Installed = {
    Candidate: LocalHostCandidate
    Receipt: byte array
}

let private loadGeneration (root: SafeDirectory) identity hashFiles ct =
    let generation = LocalCandidateDocument.generationPath identity

    let bytes =
        pinnedRead
            root
            (generation
             + "/candidate.json")
            documentBound

    if
        Sha256.ofBytes bytes
        <> identity
    then
        refuse
            "local.candidate_mismatch"
            "Stored candidate bytes differ from the generation identity."

    let candidate =
        LocalCandidateDocument.readCandidate bytes
        |> reader

    checkedPlan Platform.WinX64 candidate
    |> ignore

    let receipt =
        pinnedRead
            root
            (generation
             + "/receipt.json")
            documentBound

    LocalCandidateDocument.receiptMatches candidate receipt
    |> reader

    FileSafety.withDirectories
        root
        (parts (LocalCandidateDocument.installationPath candidate))
        false
        (fun folder -> checkFolder folder candidate hashFiles ct)

    {
        Candidate = candidate
        Receipt = receipt
    }

let private loadSelection (root: SafeDirectory) hashFiles ct =
    let pointer =
        pinnedRead root LocalCandidateDocument.selectionPath documentBound
        |> LocalCandidateDocument.readSelection
        |> reader

    let installed = loadGeneration root pointer.CandidateSha256 hashFiles ct

    if
        Sha256.ofBytes installed.Receipt
        <> pointer.ReceiptSha256
    then
        refuse "local.receipt_mismatch" "Selected receipt bytes changed."

    installed

let private foreignHome () =
    refuse
        "local.foreign_home"
        "Unsupported entries were found in setup admission or a manager-owned namespace."

let private only (directory: SafeDirectory) names =
    for name in directory.Entries() do
        if not (List.contains name names) then
            foreignHome ()

let private optionalChild (parent: SafeDirectory) name action =
    if
        parent.Entries()
        |> Array.contains name
    then
        use child = parent.Child(name, false)
        action child

/// Later commands validate only local manager authority. Shared Home siblings are opaque and are never traversed.
let validateOwnedState (root: SafeDirectory) =

    if
        root.Entries()
        |> Array.contains ".morphir-dotnet-io.lock"
    then
        let attributes =
            File.GetAttributes(Path.Combine(root.Path, ".morphir-dotnet-io.lock"))

        if
            attributes.HasFlag FileAttributes.ReparsePoint
            || attributes.HasFlag FileAttributes.Directory
            || attributes.HasFlag FileAttributes.Device
        then
            foreignHome ()

    optionalChild
        root
        "store"
        (fun store ->
            optionalChild
                store
                "tools"
                (fun tools ->
                    only tools [ "sha256" ]

                    optionalChild
                        tools
                        "sha256"
                        (fun objects ->
                            for name in objects.Entries() do
                                if
                                    Sha256.tryParse name
                                    |> Result.isError
                                then
                                    foreignHome ()

                                use folder = objects.Child(name, false)
                                folder.ValidateLocation()
                        )
                )
        )

    optionalChild
        root
        "toolchains"
        (fun toolchains ->
            only toolchains [ "generations"; "default.json" ]

            optionalChild
                toolchains
                "generations"
                (fun generations ->
                    for name in generations.Entries() do
                        let identity =
                            Sha256.tryParse name
                            |> function
                                | Ok identity -> identity
                                | Error _ -> foreignHome ()

                        use folder = generations.Child(name, false)
                        if folder.Entries() |> Array.contains "generation.json" then
                            GenerationBinding.load root folder.Path |> ignore
                        else
                            only folder [ "candidate.json"; "receipt.json" ]
                            loadGeneration root identity false CancellationToken.None |> ignore
                )
        )

    optionalChild
        root
        "tmp"
        (fun temporary ->
            for name in temporary.Entries() do
                if name.StartsWith("local-toolchain-", StringComparison.Ordinal) then
                    use folder = temporary.Child(name, false)
                    only folder [ ".owner"; "host"; "generation"; "downloads"; "repositories"; "work" ]
        )

/// Setup never adopts foreign or legacy Home state. This admission happens before staging, probing or publication.
let private validateSetupAdmission (root: SafeDirectory) =
    only root [ "store"; "toolchains"; "tmp"; ".morphir-dotnet-io.lock" ]
    optionalChild root "store" (fun store -> only store [ "tools" ])

    optionalChild
        root
        "tmp"
        (fun temporary ->
            for name in temporary.Entries() do
                if not (name.StartsWith("local-toolchain-", StringComparison.Ordinal)) then
                    foreignHome ()
        )

    validateOwnedState root

let private existingHome home ct =
    if Directory.Exists home then
        use root = SafeDirectory.Open(home, false)
        validateSetupAdmission root

        if File.Exists(relative home LocalCandidateDocument.selectionPath) then
            loadSelection root true ct
            |> ignore

/// One entry of a host archive: its name, whether it is a directory, its length, whether its mode is executable, and
/// its content. The archive inspection already refused links, devices and unsafe names.
type private HostEntry = { Name: string; Directory: bool; Length: int64; Executable: bool; Open: unit -> Stream }

// Visits the entries of a ZIP or a tar.gz host archive in archive order, from the start of the source.
let private forEachEntry (source: Stream) (candidate: LocalHostCandidate) (visit: HostEntry -> unit) =
    source.Position <- 0L
    match LocalCandidateDocument.archiveFormat candidate.Platform with
    | "zip" ->
        use zip = new ZipArchive(source, ZipArchiveMode.Read, true)
        for entry in zip.Entries do
            visit {
                Name = entry.FullName
                Directory = entry.FullName.EndsWith("/", StringComparison.Ordinal)
                Length = entry.Length
                Executable = ((entry.ExternalAttributes >>> 16) &&& 0o100) <> 0
                Open = fun () -> entry.Open()
            }
    | _ ->
        use gzip = new GZipStream(source, CompressionMode.Decompress, true)
        use tar = new TarReader(gzip, true)
        let mutable reading = true
        while reading do
            match tar.GetNextEntry(false) with
            | null -> reading <- false
            | entry when entry.EntryType = TarEntryType.GlobalExtendedAttributes -> ()
            | entry ->
                let directory = entry.EntryType = TarEntryType.Directory
                if not directory && entry.EntryType <> TarEntryType.RegularFile && entry.EntryType <> TarEntryType.V7RegularFile then
                    refuse "local.archive_format" "The archive contains an entry that is not a regular file or a directory."
                visit {
                    Name = if directory && not (entry.Name.EndsWith "/") then entry.Name + "/" else entry.Name
                    Directory = directory
                    Length = entry.Length
                    Executable = entry.Mode.HasFlag UnixFileMode.UserExecute
                    Open = fun () -> match entry.DataStream with null -> Stream.Null | stream -> stream
                }

// The launch documents that the structure check reads from an archive before anything is written.
let private launchDocuments (candidate: LocalHostCandidate) =
    let p = candidate.EntryPoint.Target.Text
    let prefix = match p.LastIndexOf '/' with -1 -> "" | slash -> p.Substring(0, slash + 1)
    set [ p; prefix + "morphir.runtimeconfig.json"; prefix + "morphir.deps.json" ]

/// Same archive and state validation for dry-run and apply. No Home creation or process launch occurs here.
let validateArchive (source: Stream) candidate validateHome ct =
    if
        uint64 source.Length
        <> candidate.ArchiveBytes
    then
        refuse "local.length_mismatch" "Archive length differs from the candidate."

    copyChecked source None candidate.ArchiveBytes candidate.ArchiveSha256 ct
    source.Position <- 0L
    let inspection = ArchiveChecks.inspect source ct

    if
        inspection.Format
        <> LocalCandidateDocument.archiveFormat candidate.Platform
    then
        refuse "local.archive_format" "The archive is not in the declared archive format of its platform."

    let expected =
        candidate.Files
        |> Seq.map (fun file -> file.Artifact.Target.Text, file)
        |> dict

    let seen = HashSet<string>(StringComparer.Ordinal)
    let launch = launchDocuments candidate
    let documents = Dictionary<string, byte array>(StringComparer.Ordinal)

    forEachEntry source candidate (fun entry ->
        if entry.Directory then
            let prefix = entry.Name

            if
                not (
                    expected.Keys
                    |> Seq.exists (fun path -> path.StartsWith(prefix, StringComparison.Ordinal))
                )
            then
                refuse "local.closure_mismatch" "Undeclared archive directory."
        else
            match expected.TryGetValue entry.Name with
            | false, _ ->
                refuse
                    "local.closure_mismatch"
                    "Archive contains a file absent from the complete candidate inventory."
            | true, file ->
                if not (seen.Add entry.Name) then
                    refuse "local.closure_mismatch" "Duplicate archive entry."

                if
                    uint64 entry.Length
                    <> file.Bytes
                then
                    refuse "local.length_mismatch" "Archive file length differs from the inventory."

                use input = entry.Open()

                if launch.Contains entry.Name then
                    if entry.Length > documentBound then
                        refuse "local.document_too_large" "Launch document exceeds its bound."

                    use output = new MemoryStream()
                    copyChecked input (Some(output :> Stream)) file.Bytes file.Artifact.Sha256 ct
                    documents[entry.Name] <- output.ToArray()
                else
                    copyChecked input None file.Bytes file.Artifact.Sha256 ct)

    if
        seen.Count
        <> expected.Count
    then
        refuse "local.closure_mismatch" "Archive does not contain the complete candidate inventory."

    structure
        candidate
        (fun name ->
            match documents.TryGetValue name with
            | true, bytes -> bytes
            | false, _ -> refuse "local.closure_mismatch" "Missing archive entry.")

    validateHome ()

/// Writes the checked archive's complete inventory into an empty private folder, then checks the folder.
/// Off Windows, a file gets mode 0755 when its archive mode is executable and 0644 otherwise, so the modes that the
/// producer packed survive. The bytes are copied unchanged, which keeps a macOS code signature valid.
let extractHost (source: Stream) (candidate: LocalHostCandidate) (folder: SafeDirectory) ct =
    let expected =
        candidate.Files
        |> Seq.map (fun file -> file.Artifact.Target.Text, file)
        |> dict

    let written = HashSet<string>(StringComparer.Ordinal)

    forEachEntry source candidate (fun entry ->
        if not entry.Directory then
            let file =
                match expected.TryGetValue entry.Name with
                | true, file when written.Add entry.Name -> file
                | _ -> refuse "local.closure_mismatch" "The archive changed after its inspection."

            let names = parts file.Artifact.Target.Text

            FileSafety.withDirectories
                folder
                (names
                 |> List.take (
                     names.Length
                     - 1
                 ))
                true
                (fun directory ->
                    use output = directory.File(List.last names, true, false)
                    use input = entry.Open()
                    copyChecked input (Some(output :> Stream)) file.Bytes file.Artifact.Sha256 ct

                    if not (OperatingSystem.IsWindows()) then
                        File.SetUnixFileMode(
                            output.SafeFileHandle,
                            if entry.Executable then enum<UnixFileMode> 0o755 else enum<UnixFileMode> 0o644
                        )

                    output.Flush true
                ))

    if written.Count <> expected.Count then
        refuse "local.closure_mismatch" "The archive changed after its inspection."

    checkFolder folder candidate true ct

let writeFile (root: SafeDirectory) path bytes =
    let names = parts path

    FileSafety.withDirectories
        root
        (names
         |> List.take (
             names.Length
             - 1
         ))
        true
        (fun directory ->
            use output = directory.File(List.last names, true, false)
            output.Write(bytes: byte array)
            output.Flush true
        )

type private HostPreparation = | HostOnly | Composed

let private setupCoreAsync mode home raw (candidate: LocalHostCandidate) archive dryRun (ct: CancellationToken) = task {
    checkExecutablePaths candidate.Platform home [relative home (LocalCandidateDocument.installationPath candidate+"/"+candidate.EntryPoint.Target.Text)]
    let admission root = if mode = HostOnly then validateSetupAdmission root else validateOwnedState root
    use source = FileSafety.openInput archive
    validateArchive source candidate (fun () -> if mode = HostOnly then existingHome home ct elif Directory.Exists home then use root = SafeDirectory.Open(home,false) in validateOwnedState root) ct

    if not dryRun then
        use root = SafeDirectory.Open(home, true)
        admission root
        // Admission is brief. Decompression and verification happen in a privately owned folder outside the writer lease.
        let stageName =
            "local-toolchain-"
            + Guid.NewGuid().ToString("N")

        let stagePath =
            "tmp/"
            + stageName

        do! task {
            use! lease = FileSafety.mutationLeaseAsync root ct
            FileSafety.withDirectories root (parts stagePath) true ignore
        }

        use stage = SafeDirectory.Open(relative home stagePath, false)
        use owner = stage.File(".owner", true, true)

        do! withPrivateStageAsync root stage owner Console.Error (fun () -> task {
            FileSafety.withDirectories
                stage
                [ "host" ]
                true
                (fun folder -> extractHost source candidate folder ct)

            do! task {
                use! lease = FileSafety.mutationLeaseAsync root ct
                admission root

                FileSafety.withDirectories
                    root
                    [ "store"; "tools"; "sha256" ]
                    true
                    (fun parent ->
                        let destination =
                            relative home (LocalCandidateDocument.installationPath candidate)

                        if Directory.Exists destination then
                            use folder = parent.Child(candidate.ArchiveSha256.Text, false)
                            checkFolder folder candidate true ct
                        else
                            stage.ValidateLocation()
                            parent.ValidateLocation()

                            let identity =
                                use sourceFolder = stage.Child("host", false)
                                sourceFolder.Identity

                            Directory.Move(Path.Combine(stage.Path, "host"), destination)
                            use folder = parent.Child(candidate.ArchiveSha256.Text, false)

                            if
                                folder.Identity
                                <> identity
                            then
                                refuse "local.path_escape" "Published host folder identity changed."

                            checkFolder folder candidate true ct
                    )
            }

            let program =
                relative
                    home
                    (LocalCandidateDocument.installationPath candidate
                     + "/"
                     + candidate.EntryPoint.Target.Text)

            let directory =
                Path.GetDirectoryName program
                |> Option.ofObj
                |> Option.defaultWith (fun () ->
                    refuse "local.path_escape" "Missing executable directory."
                )

            do! LocalProcess.verifyVersionAsync program candidate.Host.Version.Text directory home ct
            if mode = HostOnly then
                let receipt = LocalCandidateDocument.writeReceipt candidate

                FileSafety.withDirectories
                    stage
                    [ "generation" ]
                    true
                    (fun generation ->
                        writeFile generation "candidate.json" raw
                        writeFile generation "receipt.json" receipt
                    )

                use! lease = FileSafety.mutationLeaseAsync root ct

                FileSafety.withDirectories
                    root
                    [ "toolchains"; "generations" ]
                    true
                    (fun parent ->
                        let destination = Path.Combine(parent.Path, candidate.ManifestSha256.Text)

                        if Directory.Exists destination then
                            let existing = loadGeneration root candidate.ManifestSha256 true ct

                            if
                                existing.Receipt
                                <> receipt
                            then
                                refuse "local.generation_conflict" "Existing immutable generation differs."
                        else
                            ct.ThrowIfCancellationRequested()
                            stage.ValidateLocation()
                            parent.ValidateLocation()

                            let identity =
                                use sourceFolder = stage.Child("generation", false)
                                sourceFolder.Identity

                            Directory.Move(Path.Combine(stage.Path, "generation"), destination)
                            use published = parent.Child(candidate.ManifestSha256.Text, false)

                            if
                                published.Identity
                                <> identity
                            then
                                refuse "local.path_escape" "Published generation identity changed."
                    )
        })
// An interrupted process may leave a private orphan; normal completion awaits cleanup.
}

let setupAsync home raw candidate archive dryRun ct = setupCoreAsync HostOnly home raw candidate archive dryRun ct

let prepareComposedHostAsync home raw candidate archive dryRun ct = setupCoreAsync Composed home raw candidate archive dryRun ct

let generation home identity ct =
    use root = SafeDirectory.Open(home, false)
    validateOwnedState root

    if File.Exists(relative home LocalCandidateDocument.selectionPath) then
        loadSelection root true ct
        |> ignore

    loadGeneration root identity true ct

let selected home ct =
    use root = SafeDirectory.Open(home, false)
    validateOwnedState root
    loadSelection root false ct

let internal selectWithPreparedAsync home identity dryRun (prepared: unit -> unit) ct = task {
    let installed = generation home identity ct

    if not dryRun then
        use root = SafeDirectory.Open(home, false)
        use! lease = FileSafety.mutationLeaseAsync root ct
        validateOwnedState root
        let current = loadGeneration root identity true ct

        if File.Exists(relative home LocalCandidateDocument.selectionPath) then
            loadSelection root true ct
            |> ignore

        let bytes = LocalCandidateDocument.writeSelection current.Candidate current.Receipt

        // Prepared pointer bytes have no authority. An interrupted write stays in
        // the already-supported private stage namespace, never beside default.json.
        let stageName =
            "local-toolchain-"
            + Guid.NewGuid().ToString("N")

        FileSafety.withDirectories
            root
            [ "tmp"; stageName ]
            true
            (fun stage ->
                use owner = stage.File(".owner", true, true)

                FileSafety.withDirectories
                    stage
                    [ "generation" ]
                    true
                    (fun pending ->
                        writeFile pending "selection.json" bytes
                        prepared ()
                        ct.ThrowIfCancellationRequested()
                        pending.ValidateLocation()

                        FileSafety.withDirectories
                            root
                            [ "toolchains" ]
                            false
                            (fun directory ->
                                directory.ValidateLocation()

                                File.Move(
                                    Path.Combine(pending.Path, "selection.json"),
                                    Path.Combine(directory.Path, "default.json"),
                                    true
                                )
                            )
                    )
            )

    return installed
}

let selectAsync home identity dryRun ct =
    selectWithPreparedAsync home identity dryRun ignore ct

let runAsync home args directory input output errors ct = task {
    use root = SafeDirectory.Open(home, false)
    use! lease = FileSafety.mutationLeaseAsync root ct
    validateOwnedState root
    let installed = loadSelection root false ct

    let program =
        relative
            home
            (LocalCandidateDocument.installationPath installed.Candidate
             + "/"
             + installed.Candidate.EntryPoint.Target.Text)

    return! LocalProcess.runAsync program args directory home input output errors ct
}
