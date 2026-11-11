// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Microsoft.Win32.SafeHandles

/// An operation result. C# callers handle both cases through Match, without F# union tags.
[<Sealed>]
type InstallationResult<'T> internal (result: Result<'T, InstallationFailure>) =
    /// Handles success or refusal with C# delegates.
    member _.Match<'R>(success: Func<'T, 'R>, failure: Func<InstallationFailure, 'R>) =
        match result with
        | Ok value -> success.Invoke value
        | Error error -> failure.Invoke error

    /// Stable text without reflection over the result value.
    override _.ToString() =
        match result with
        | Ok _ -> "Installation operation succeeded."
        | Error error -> error.ToString()

/// Bounds recorded in the approved extension-installation store and archive specifications.
[<AbstractClass; Sealed>]
type InstallationLimits private () =
    /// Maximum single artifact or archive entry, 256 MiB.
    static member MaxArtifactBytes =
        256L
        * 1024L
        * 1024L

    /// Maximum compressed archive and aggregate expanded content, 1 GiB.
    static member MaxArchiveBytes =
        1024L
        * 1024L
        * 1024L

    /// Maximum entries in one archive.
    static member MaxArchiveEntries = 1024

    /// Maximum PAX or GNU extension header payload, 1 MiB.
    static member MaxExtensionHeaderBytes =
        1024L
        * 1024L

    /// Maximum expansion after the first 1 MiB.
    static member MaxExpansionRatio = 200L

exception internal InstallationException of InstallationFailure

module internal Operations =
    let fail code message =
        raise (InstallationException(InstallationFailure(code, message)))

    let requiredPath (value: string | null) =
        match value with
        | null -> fail "extension.invalid_path" "A path component is missing."
        | value -> value

    let run (ct: CancellationToken) work =
        let result =
            try
                ct.ThrowIfCancellationRequested()
                Ok(work ())
            with
            | InstallationException error -> Error error
            | :? OperationCanceledException ->
                Error(InstallationFailure("extension.cancelled", "The operation was cancelled."))
            | :? IOException as error ->
                Error(InstallationFailure("extension.io_error", error.Message))
            | :? UnauthorizedAccessException as error ->
                Error(InstallationFailure("extension.io_error", error.Message))
            | :? ArgumentException as error ->
                Error(InstallationFailure("extension.invalid_path", error.Message))

        Task.FromResult(InstallationResult result)

// Windows directories are pinned without write/delete sharing while their paths are used. Unix operations are
// relative to open directory descriptors with O_NOFOLLOW; publication uses linkat, which cannot replace a winner.
// API references: CreateFileW / FILE_FLAG_OPEN_REPARSE_POINT (Microsoft Learn); openat(2), linkat(2), flock(2).
module private NativeFiles =
    [<DllImport("kernel32.dll",
                EntryPoint = "CreateFileW",
                CharSet = CharSet.Unicode,
                SetLastError = true)>]
    extern SafeFileHandle createFile(
        string path,
        uint32 access,
        uint32 share,
        nativeint security,
        uint32 disposition,
        uint32 flags,
        nativeint template
    )

    [<DllImport("kernel32.dll",
                EntryPoint = "MoveFileExW",
                CharSet = CharSet.Unicode,
                SetLastError = true)>]
    extern bool moveFile(string source, string target, uint32 flags)

    [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
    extern int unixOpen([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int flags, uint32 mode)

    [<DllImport("libc", EntryPoint = "openat", SetLastError = true)>]
    extern int openAt(
        int directory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path,
        int flags,
        uint32 mode
    )

    [<DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)>]
    extern int mkdirAt(
        int directory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path,
        uint32 mode
    )

    [<DllImport("libc", EntryPoint = "linkat", SetLastError = true)>]
    extern int linkAt(
        int sourceDirectory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string source,
        int targetDirectory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string target,
        int flags
    )

    [<DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)>]
    extern int unlinkAt(int directory, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int flags)

    [<DllImport("libc", EntryPoint = "flock", SetLastError = true)>]
    extern int flock(int descriptor, int operation)

    let directoryFlags = if OperatingSystem.IsMacOS() then 0x100000 else 0x10000
    let noFollow = if OperatingSystem.IsMacOS() then 0x100 else 0x20000
    let closeOnExec = if OperatingSystem.IsMacOS() then 0x1000000 else 0x80000
    let nonBlock = if OperatingSystem.IsMacOS() then 0x4 else 0x800
    let create = if OperatingSystem.IsMacOS() then 0x200 else 0x40
    let exclusive = if OperatingSystem.IsMacOS() then 0x800 else 0x80
    let fd (handle: SafeFileHandle) = handle.DangerousGetHandle().ToInt32()

    let windowsPath (path: string) =
        if path.StartsWith("\\\\?\\", StringComparison.Ordinal) then
            path
        elif path.StartsWith("\\\\", StringComparison.Ordinal) then
            "\\\\?\\UNC\\"
            + path.Substring(2)
        else
            "\\\\?\\"
            + path

    let error (path: string) code =
        if
            code = 2
            || (OperatingSystem.IsWindows()
                && code = 3)
        then
            raise (FileNotFoundException("Entry does not exist.", path))
        elif
            (OperatingSystem.IsWindows()
             && code = 1920)
            || (not (OperatingSystem.IsWindows())
                && (code = 20
                    || code = 40
                    || code = 62))
        then
            Operations.fail
                "extension.path_escape"
                $"A link or unsafe directory was refused: {path}"
        else
            raise (IOException($"Cannot access '{path}': native error {code}."))

    let unixHandle path value =
        if value < 0 then
            error path (Marshal.GetLastPInvokeError())

        new SafeFileHandle(nativeint value, true)

    let windowsHandle path (handle: SafeFileHandle) =
        if handle.IsInvalid then
            let code = Marshal.GetLastPInvokeError()
            handle.Dispose()
            error path code

        handle

    let attributes path (handle: SafeFileHandle) directory =
        let attributes = File.GetAttributes handle

        if
            attributes.HasFlag FileAttributes.ReparsePoint
            || attributes.HasFlag FileAttributes.Device
        then
            Operations.fail "extension.path_escape" $"A reparse point or device was refused: {path}"

        if
            attributes.HasFlag FileAttributes.Directory
            <> directory
        then
            let kind = if directory then "directory" else "file"
            Operations.fail "extension.path_escape" $"Not a regular {kind}: {path}"

/// One trusted directory. Internal so callers cannot manufacture trusted filesystem state.
type internal SafeDirectory private (path: string, handle: SafeFileHandle) =
    member _.Path = path
    member _.Handle = handle

    member _.Entries() =
        Directory.GetFileSystemEntries(path)
        |> Array.map (fun entry ->
            Path.GetFileName entry
            |> Operations.requiredPath
        )

    static member Open(path: string, create: bool) =
        let full = Path.GetFullPath path

        if create then
            Directory.CreateDirectory full
            |> ignore

        let handle =
            if OperatingSystem.IsWindows() then
                NativeFiles.createFile (
                    NativeFiles.windowsPath full,
                    0x80u,
                    1u,
                    0n,
                    3u,
                    0x02200000u,
                    0n
                )
                |> NativeFiles.windowsHandle full
            else
                NativeFiles.unixOpen (
                    full,
                    NativeFiles.directoryFlags
                    ||| NativeFiles.noFollow
                    ||| NativeFiles.closeOnExec,
                    0u
                )
                |> NativeFiles.unixHandle full

        try
            NativeFiles.attributes full handle true
            new SafeDirectory(full, handle)
        with _ ->
            handle.Dispose()
            reraise ()

    member this.Child(name: string, create: bool) =
        if
            name.Length = 0
            || name = "."
            || name = ".."
            || name.IndexOfAny([| '/'; '\\' |])
               >= 0
        then
            Operations.fail "extension.path_escape" "A directory component is not a plain name."

        let full = Path.Combine(path, name)

        let child =
            if OperatingSystem.IsWindows() then
                if create then
                    Directory.CreateDirectory full
                    |> ignore

                NativeFiles.createFile (
                    NativeFiles.windowsPath full,
                    0x80u,
                    1u,
                    0n,
                    3u,
                    0x02200000u,
                    0n
                )
                |> NativeFiles.windowsHandle full
            else
                if
                    create
                    && NativeFiles.mkdirAt (NativeFiles.fd handle, name, 0o755u) < 0
                    && Marshal.GetLastPInvokeError()
                       <> 17
                then
                    NativeFiles.error full (Marshal.GetLastPInvokeError())

                NativeFiles.openAt (
                    NativeFiles.fd handle,
                    name,
                    NativeFiles.directoryFlags
                    ||| NativeFiles.noFollow
                    ||| NativeFiles.closeOnExec,
                    0u
                )
                |> NativeFiles.unixHandle full

        try
            NativeFiles.attributes full child true
            new SafeDirectory(full, child)
        with _ ->
            child.Dispose()
            reraise ()

    member _.File(name: string, createNew: bool, lease: bool) =
        if
            name.Length = 0
            || name = "."
            || name = ".."
            || name.IndexOfAny([| '/'; '\\' |])
               >= 0
        then
            Operations.fail "extension.path_escape" "A file component is not a plain name."

        let full = Path.Combine(path, name)

        let file =
            if OperatingSystem.IsWindows() then
                NativeFiles.createFile (
                    NativeFiles.windowsPath full,
                    (if
                         createNew
                         || lease
                     then
                         0xC0000000u
                     else
                         0x80000000u),
                    (if lease then 0u else 1u),
                    0n,
                    (if createNew then 1u
                     elif lease then 4u
                     else 3u),
                    0x00200080u,
                    0n
                )
                |> NativeFiles.windowsHandle full
            else
                let flags =
                    NativeFiles.noFollow
                    ||| NativeFiles.closeOnExec
                    ||| NativeFiles.nonBlock

                let access =
                    if createNew then
                        2
                        ||| NativeFiles.create
                        ||| NativeFiles.exclusive
                    elif lease then
                        2
                        ||| NativeFiles.create
                    else
                        0

                NativeFiles.openAt (
                    NativeFiles.fd handle,
                    name,
                    flags
                    ||| access,
                    0o600u
                )
                |> NativeFiles.unixHandle full

        try
            NativeFiles.attributes full file false

            if
                lease
                && not (OperatingSystem.IsWindows())
                && NativeFiles.flock (NativeFiles.fd file, 2 ||| 4)
                   <> 0
            then
                raise (IOException $"Run directory is in use: {path}")

            let stream =
                new FileStream(
                    file,
                    (if
                         createNew
                         || lease
                     then
                         FileAccess.ReadWrite
                     else
                         FileAccess.Read),
                    64 * 1024,
                    false
                )

            if not stream.CanSeek then
                stream.Dispose()
                Operations.fail "extension.path_escape" $"Not a regular seekable file: {full}"

            stream
        with _ ->
            file.Dispose()
            reraise ()

    member _.Publish(stage: string, name: string) =
        if OperatingSystem.IsWindows() then
            if
                NativeFiles.moveFile (
                    NativeFiles.windowsPath (Path.Combine(path, stage)),
                    NativeFiles.windowsPath (Path.Combine(path, name)),
                    0u
                )
            then
                true
            else
                let code = Marshal.GetLastPInvokeError()

                if
                    code = 80
                    || code = 183
                then
                    false
                else
                    NativeFiles.error path code
        else if
            NativeFiles.linkAt (NativeFiles.fd handle, stage, NativeFiles.fd handle, name, 0) = 0
        then
            true
        else
            let code = Marshal.GetLastPInvokeError()
            if code = 17 then false else NativeFiles.error path code

    member _.DeleteFile(name: string) =
        if OperatingSystem.IsWindows() then
            File.Delete(Path.Combine(path, name))
        elif
            NativeFiles.unlinkAt (NativeFiles.fd handle, name, 0)
            <> 0
            && Marshal.GetLastPInvokeError()
               <> 2
        then
            NativeFiles.error (Path.Combine(path, name)) (Marshal.GetLastPInvokeError())

    member _.DeleteDirectory(name: string) =
        if OperatingSystem.IsWindows() then
            Directory.Delete(Path.Combine(path, name), false)
        elif
            NativeFiles.unlinkAt (
                NativeFiles.fd handle,
                name,
                (if OperatingSystem.IsMacOS() then 0x80 else 0x200)
            )
            <> 0
        then
            NativeFiles.error (Path.Combine(path, name)) (Marshal.GetLastPInvokeError())

    interface IDisposable with
        member _.Dispose() = handle.Dispose()

module internal FileSafety =
    let rec withDirectories (root: SafeDirectory) (parts: string list) create work =
        match parts with
        | [] -> work root
        | first :: rest ->
            use child = root.Child(first, create)
            withDirectories child rest create work

    let openRelative (root: SafeDirectory) (fullPath: string) =
        let relative =
            Path.GetRelativePath(root.Path, Path.GetFullPath fullPath).Replace('\\', '/')

        if
            Path.IsPathRooted relative
            || relative = ".."
            || relative.StartsWith("../", StringComparison.Ordinal)
        then
            Operations.fail
                "extension.path_escape"
                $"Staging must stay inside Morphir Home: {fullPath}"

        let parts =
            relative.Split('/')
            |> Array.toList

        withDirectories
            root
            (parts
             |> List.take (
                 parts.Length
                 - 1
             ))
            false
            (fun directory -> directory.File(List.last parts, false, false))

    let openInput fullPath =
        let path = Path.GetFullPath fullPath

        use parent =
            SafeDirectory.Open(
                Path.GetDirectoryName path
                |> Operations.requiredPath,
                false
            )

        parent.File(
            Path.GetFileName path
            |> Operations.requiredPath,
            false,
            false
        )

    let tryDelete (directory: SafeDirectory) name =
        try
            directory.DeleteFile name
        with
        | :? IOException
        | :? UnauthorizedAccessException -> ()
