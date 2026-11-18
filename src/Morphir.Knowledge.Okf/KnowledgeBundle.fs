// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Okf

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Runtime.InteropServices
open System.Threading
open Microsoft.Extensions.FileProviders
open Microsoft.Win32.SafeHandles
open Morphir.Files

/// The reason a logical path cannot address a bundle member.
[<RequireQualifiedAccess>]
type BundlePathError =
    /// No path was supplied.
    | Empty
    /// The path is rooted or contains an invalid segment.
    | Invalid

    override this.ToString() =
        match this with
        | BundlePathError.Empty -> "A bundle member path is required."
        | BundlePathError.Invalid -> "A bundle member path must have nonempty relative segments separated by '/'."

/// An ordinally compared path below the knowledge bundle root.
[<CustomEquality; CustomComparison>]
type BundlePath =
    private
    | BundlePath of string

    /// The original relative path, with '/' separators.
    member this.Value = let (BundlePath path) = this in path

    /// The member's last path segment.
    member this.Name = this.Value.Substring(this.Value.LastIndexOf('/') + 1)

    /// True for a Markdown file under the OKF filename rules.
    member this.IsMarkdown = this.Name.EndsWith(".md", StringComparison.Ordinal)

    /// True for a path with a hidden component under the OKF v0.3 rules.
    member internal this.IsHidden =
        this.Value.Split('/') |> Array.exists (fun segment -> segment.StartsWith('.'))

    /// Parses a decoded bundle path for F# callers.
    static member Parse(value: string | null) : Result<BundlePath, BundlePathError> =
        match value with
        | null
        | "" -> Result.Error BundlePathError.Empty
        | path ->
            if path.IndexOfAny([| '\\'; '\000' |]) >= 0
               || (path.Split('/') |> Array.exists (fun segment -> segment = "" || segment = "." || segment = ".."))
               || (path.Length > 1 && Char.IsAsciiLetter(path[0]) && path[1] = ':') then
                Result.Error BundlePathError.Invalid
            else
                Ok(BundlePath path)

    /// Parses a decoded bundle path for C# callers.
    static member TryParse
        (
            value: string | null,
            [<Out; MaybeNullWhen(false)>] path: byref<BundlePath>,
            [<Out; MaybeNullWhen(true)>] failure: byref<BundlePathError>
        ) =
        match BundlePath.Parse value with
        | Ok parsed ->
            path <- parsed
            failure <- Unchecked.defaultof<_>
            true
        | Result.Error error ->
            path <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Creates a literal path or raises an argument error.
    static member Create(literal: string) =
        match BundlePath.Parse literal with
        | Ok path -> path
        | Result.Error error -> invalidArg (nameof literal) (error.ToString())

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? BundlePath as otherPath -> String.Equals(this.Value, otherPath.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode(this.Value)

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? BundlePath as otherPath -> String.CompareOrdinal(this.Value, otherPath.Value)
            | _ -> invalidArg (nameof other) "Expected a bundle path."

/// The retained source of a non-Markdown bundle entry.
[<Sealed>]
type BundleAsset internal (file: VFile) =
    /// Opens an independent asset stream, owned by the caller.
    member _.TryOpenRead
        (
            [<Out; MaybeNullWhen(false)>] stream: byref<Stream>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ContentError>
        ) =
        file.TryOpenRead(&stream, &failure)

    override _.ToString() = "Bundle asset"

/// An OKF Markdown parse outcome or another file offered by the bundle.
[<RequireQualifiedAccess>]
type BundleEntry =
    /// Includes both successfully read and unreadable Markdown members.
    | Document of OkfParseResult
    /// Retains the source of a non-Markdown file without opening it.
    | Asset of BundleAsset

    /// For C# callers: handles either kind of bundle entry.
    member this.Match<'T>(document: Func<OkfParseResult, 'T>, asset: Func<BundleAsset, 'T>) =
        match this with
        | BundleEntry.Document value -> document.Invoke value
        | BundleEntry.Asset value -> asset.Invoke value

    override this.ToString() =
        match this with
        | BundleEntry.Document _ -> "Markdown document"
        | BundleEntry.Asset _ -> "Bundle asset"

/// A member address and its retained parse outcome or asset.
[<Sealed>]
type BundleMember internal (path: BundlePath, read: CancellationToken -> BundleEntry) =
    let mutable cached: BundleEntry option = None

    /// The path relative to the bundle root.
    member _.Path = path

    member internal _.ReadEntry(cancellation: CancellationToken) =
        cancellation.ThrowIfCancellationRequested()

        match Volatile.Read(&cached) with
        | Some entry -> entry
        | None ->
            let entry = read cancellation
            cancellation.ThrowIfCancellationRequested()

            match Interlocked.CompareExchange(&cached, Some entry, None) with
            | Some published -> published
            | None -> entry

    /// The member. A Markdown member is captured on first access.
    member this.Entry = this.ReadEntry(CancellationToken.None)

    override _.ToString() = path.Value

type internal BundleLoadIssue = {
    Path: string
    IsExcluded: bool
    Message: string
}

/// Why a knowledge bundle could not be loaded at its root.
[<Sealed>]
type BundleLoadError internal (message: string) =
    /// A human-readable failure message.
    member _.Message = message

    override _.ToString() = message

// Linux FileAttributes does not distinguish FIFOs and sockets from regular files.
module private LinuxBundleFiles =
    // The Linux statx ABI keeps stx_mode at offset 28 across architectures.
    [<Struct; StructLayout(LayoutKind.Sequential, Size = 256)>]
    type Statx =
        val mutable Mask: uint32
        val mutable BlockSize: uint32
        val mutable Attributes: uint64
        val mutable LinkCount: uint32
        val mutable User: uint32
        val mutable Group: uint32
        val mutable Mode: uint16

    [<DllImport("libc", EntryPoint = "statx", SetLastError = true)>]
    extern int statx(int descriptor, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int flags, uint32 mask, Statx& info)

    [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
    extern int openFile([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int flags)

    let private systemError path =
        IOException($"Could not open or inspect '{path}': {ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()).Message}")

    let private regularFile descriptor path flags =
        let mutable info = Unchecked.defaultof<Statx>
        let statxType = 0x1u

        if statx(descriptor, path, flags, statxType, &info) <> 0 then
            raise (systemError path)

        (info.Mask &&& statxType) <> 0u && (info.Mode &&& 0xF000us) = 0x8000us

    let isRegular path =
        let currentDirectory = -100
        let atSymlinkNoFollow = 0x100
        regularFile currentDirectory path atSymlinkNoFollow

    let openRegular path =
        let noFollow = if RuntimeInformation.ProcessArchitecture = Architecture.Arm64 then 0x8000 else 0x20000
        let closeOnExec = 0x80000
        let nonBlocking = 0x800
        let descriptor = openFile(path, noFollow ||| closeOnExec ||| nonBlocking)

        if descriptor < 0 then raise (systemError path)

        let handle = new SafeFileHandle(nativeint descriptor, true)

        try
            let atEmptyPath = 0x1000

            if not (regularFile descriptor "" atEmptyPath) then
                raise (IOException($"The bundle member is no longer a regular file: '{path}'."))

            new FileStream(handle, FileAccess.Read) :> Stream
        with _ ->
            handle.Dispose()
            reraise ()

module private BundleLoading =
    let isReadError (error: exn) =
        match error with
        | :? IOException
        | :? UnauthorizedAccessException
        | :? Security.SecurityException
        | :? ArgumentException
        | :? NotSupportedException -> true
        | _ -> false

    let private unreadable (message: string) =
        BundleEntry.Document(
            OkfParseResult.UnreadableSource {
                Code = "okf.unreadable-member"
                Severity = DiagnosticSeverity.Error
                Message = message
                Span = ValueNone
            }
        )

    let memberOf (path: BundlePath) (file: VFile) =
        let getEntry (cancellation: CancellationToken) =
            if not path.IsMarkdown then
                BundleEntry.Asset(BundleAsset file)
            else
                try
                    match file.Content with
                    | Some(VFileContent.Text text) -> BundleEntry.Document(OkfDocument.Parse text)
                    | Some(VFileContent.Bytes bytes) -> BundleEntry.Document(OkfDocument.Parse bytes)
                    | _ ->
                        let mutable input = Unchecked.defaultof<Stream>
                        let mutable error = Unchecked.defaultof<ContentError>

                        if file.TryOpenRead(&input, &error) then
                            use stream = input
                            use buffer = new MemoryStream()
                            stream.CopyToAsync(buffer, cancellation).GetAwaiter().GetResult()
                            BundleEntry.Document(OkfDocument.Parse(ReadOnlyMemory(buffer.ToArray())))
                        else
                            unreadable (ContentError.describe error)
                with error when isReadError error ->
                    unreadable error.Message

        path, BundleMember(path, getEntry)

    let join parent child =
        if parent = "" then child else parent + "/" + child

    let virtualFiles (tree: VFileTree) =
        let rec directories prefix (directory: VFileTree) =
            seq {
                for node in directory.Content do
                    match node with
                    | TreeNode.File _ -> ()
                    | TreeNode.Directory child ->
                        let location = join prefix (child.Path.Replace('\\', '/'))
                        yield location
                        yield! directories location child
            }

        VFileTree.toFileMap tree, Seq.toList (directories "" tree)

    let physicalFiles (root: string) (cancellation: CancellationToken) =
        let issues = ResizeArray<BundleLoadIssue>()
        let directories = ResizeArray<string>()
        let files = ResizeArray<string * VFile>()

        let unsupported entry =
            (entry &&& (FileAttributes.ReparsePoint ||| FileAttributes.Device)) <> enum 0

        let ensureOrdinary (entry: FileSystemInfo) =
            if unsupported (File.GetAttributes entry.FullName) then
                raise (IOException($"Links and special files are excluded: {entry.FullName}"))

        let rec checkParents (ancestors: DirectoryInfo list) =
            match ancestors with
            | [] -> ()
            | directory :: parents ->
                checkParents parents
                ensureOrdinary directory

        let report relative excluded message =
            issues.Add { Path = relative; IsExcluded = excluded; Message = message }

        let rec visit (relative: string) (parents: DirectoryInfo list) (directory: DirectoryInfo) =
            cancellation.ThrowIfCancellationRequested()
            ensureOrdinary directory
            let ancestors = directory :: parents

            for entry in directory.GetFileSystemInfos() do
                cancellation.ThrowIfCancellationRequested()
                let location = join relative entry.Name

                try
                    let attributes = File.GetAttributes entry.FullName

                    if unsupported attributes then
                        report location true "Links and special files are excluded from physical bundle input."
                    elif attributes.HasFlag(FileAttributes.Directory) then
                        directories.Add location
                        visit location ancestors (DirectoryInfo entry.FullName)
                    elif OperatingSystem.IsLinux() && not (LinuxBundleFiles.isRegular entry.FullName) then
                        report location true "Links and special files are excluded from physical bundle input."
                    else
                        let source =
                            StreamSource(fun () ->
                                checkParents ancestors
                                ensureOrdinary entry

                                if OperatingSystem.IsLinux() then LinuxBundleFiles.openRegular entry.FullName
                                else File.OpenRead entry.FullName :> Stream)

                        files.Add(location, VFile.ofStream entry.Name source)
                with error when isReadError error ->
                    report location false error.Message

        try
            cancellation.ThrowIfCancellationRequested()
            let directory = DirectoryInfo(Path.GetFullPath root)
            visit "" [] directory
            Ok(Map.ofSeq files, List.ofSeq directories, List.ofSeq issues)
        with error when isReadError error ->
            Result.Error(BundleLoadError error.Message)

/// An addressed collection of Markdown members and assets rooted at one source.
[<Sealed>]
type KnowledgeBundle private
    (
        members: Map<BundlePath, BundleMember>,
        directories: Set<BundlePath>,
        loadIssues: BundleLoadIssue list
    ) =
    /// Members in ordinal path order.
    member _.Members =
        members |> Map.toSeq |> Seq.map snd |> ImmutableArray.CreateRange

    /// Finds a member by its exact bundle path for F# callers.
    member _.TryFind(path: BundlePath) = members.TryFind path

    /// Finds a member by its exact bundle path for C# callers.
    member this.FindOrNull(path: BundlePath) : BundleMember | null =
        match this.TryFind path with
        | Some found -> found
        | None -> null

    /// True for an addressed directory of the bundle.
    member internal _.HasDirectory(path: BundlePath) = directories.Contains path

    /// Failures encountered while enumerating the input.
    member internal _.LoadIssues = loadIssues

    /// Builds a bundle from a virtual tree.
    static member FromTree(tree: VFileTree) =
        let files, directories = BundleLoading.virtualFiles tree
        KnowledgeBundle.Create(files, directories, [])

    /// Builds a bundle from a provider rooted at the bundle directory.
    static member FromFileProvider(provider: IFileProvider) =
        provider |> VFileProviders.ofFileProvider |> KnowledgeBundle.FromTree

    /// Enumerates a directory and excludes links and special files.
    static member LoadDirectory(path: string, cancellation: CancellationToken) =
        BundleLoading.physicalFiles path cancellation
        |> Result.map (fun (files, directories, issues) ->
            KnowledgeBundle.Create(files, directories, issues))

    static member private Create
        (files: Map<string, VFile>, directories: string list, issues: BundleLoadIssue list) =
        let loadIssues = ResizeArray<BundleLoadIssue>(issues)

        let tryPath (path: string) =
            match BundlePath.Parse path with
            | Ok parsed -> Some parsed
            | Result.Error failure ->
                loadIssues.Add { Path = path; IsExcluded = false; Message = failure.ToString() }
                None

        let members =
            files
            |> Map.toSeq
            |> Seq.choose (fun (path, file) -> tryPath path |> Option.map (fun valid -> BundleLoading.memberOf valid file))
            |> Map.ofSeq

        let directoryPaths =
            let parents (path: BundlePath) =
                seq {
                    let mutable boundary = path.Value.LastIndexOf('/')

                    while boundary > 0 do
                        yield BundlePath.Create(path.Value.Substring(0, boundary))
                        boundary <- path.Value.LastIndexOf('/', boundary - 1)
                }

            Seq.append (Seq.choose tryPath directories) (members |> Map.toSeq |> Seq.collect (fst >> parents))
            |> Set.ofSeq

        KnowledgeBundle(members, directoryPaths, List.ofSeq loadIssues)

    override _.ToString() = $"Knowledge bundle ({members.Count} members)"
