// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open System.Text
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

type internal FileLeaseBusyException(message: string) =
    inherit IOException(message)

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

// Windows directories deny deletion while permitting child writes. Unix operations are
// relative to open directory descriptors with O_NOFOLLOW. Descriptor identity is preserved through publication.
// Directory ancestry checks detect rename-out, but advisory coordination is not same-UID OS isolation.
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

    [<DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)>]
    extern bool setFileInformation(SafeFileHandle file, int kind, nativeint data, uint32 size)

    [<DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)>]
    extern bool getFileInformation(SafeFileHandle file, int kind, nativeint data, uint32 size)

    [<DllImport("libc", EntryPoint = "fstat", SetLastError = true)>]
    extern int fstat(int file, nativeint data)

    [<DllImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)>]
    extern int fstatDarwinX64(int file, nativeint data)

    [<DllImport("libc", EntryPoint = "fclonefileat", SetLastError = true)>]
    extern int cloneAt(
        int file,
        int directory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string name,
        int flags
    )

    [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
    extern int unixOpenFixed(
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path,
        int flags,
        uint32 mode
    )

    [<DllImport("libc", EntryPoint = "openat", SetLastError = true)>]
    extern int openAtFixed(
        int directory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path,
        int flags,
        uint32 mode
    )

    // Darwin arm64 places variadic arguments on the stack. Fill the eight integer
    // argument registers so mode occupies the first stack slot, for both entrypoints.
    // open/openat read mode only with O_CREAT; the same ABI is used for all calls.
    // Apple: Writing ARM64 code for Apple platforms; xnu libsyscall/wrappers/open-base.c.
    [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
    extern int unixOpenDarwinArm64(
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path,
        int flags,
        nativeint padding2,
        nativeint padding3,
        nativeint padding4,
        nativeint padding5,
        nativeint padding6,
        nativeint padding7,
        uint32 mode
    )

    [<DllImport("libc", EntryPoint = "openat", SetLastError = true)>]
    extern int openAtDarwinArm64(
        int directory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path,
        int flags,
        nativeint padding3,
        nativeint padding4,
        nativeint padding5,
        nativeint padding6,
        nativeint padding7,
        uint32 mode
    )

    let unixOpen (path, flags, mode) =
        match RuntimeInformation.ProcessArchitecture with
        | Architecture.Arm64 when OperatingSystem.IsMacOS() ->
            unixOpenDarwinArm64 (path, flags, 0n, 0n, 0n, 0n, 0n, 0n, mode)
        | Architecture.X64 when
            OperatingSystem.IsMacOS()
            || OperatingSystem.IsLinux()
            ->
            unixOpenFixed (path, flags, mode)
        | Architecture.Arm64 when OperatingSystem.IsLinux() -> unixOpenFixed (path, flags, mode)
        | _ -> Operations.fail "extension.io_error" "Unsupported native open ABI."

    let openAt (directory, path, flags, mode) =
        match RuntimeInformation.ProcessArchitecture with
        | Architecture.Arm64 when OperatingSystem.IsMacOS() ->
            openAtDarwinArm64 (directory, path, flags, 0n, 0n, 0n, 0n, 0n, mode)
        | Architecture.X64 when
            OperatingSystem.IsMacOS()
            || OperatingSystem.IsLinux()
            ->
            openAtFixed (directory, path, flags, mode)
        | Architecture.Arm64 when OperatingSystem.IsLinux() ->
            openAtFixed (directory, path, flags, mode)
        | _ -> Operations.fail "extension.io_error" "Unsupported native openat ABI."

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

    // Identity only: the first device/inode fields of the supported 64-bit Linux and Darwin stat ABIs.
    // Darwin x64 uses the INODE64 symbol; Darwin arm64's ordinary fstat already uses that layout.
    let identity (handle: SafeFileHandle) =
        let data = Marshal.AllocHGlobal 256

        try
            if OperatingSystem.IsWindows() then
                if not (getFileInformation (handle, 18, data, 24u)) then
                    error "file identity" (Marshal.GetLastPInvokeError())

                struct (uint64 (Marshal.ReadInt64(data, 0)),
                        uint64 (Marshal.ReadInt64(data, 8)),
                        uint64 (Marshal.ReadInt64(data, 16)))
            else
                if
                    RuntimeInformation.ProcessArchitecture
                    <> Architecture.X64
                    && RuntimeInformation.ProcessArchitecture
                       <> Architecture.Arm64
                then
                    Operations.fail
                        "extension.io_error"
                        "Filesystem identity is supported only on x64 and arm64."

                let result =
                    if
                        OperatingSystem.IsMacOS()
                        && RuntimeInformation.ProcessArchitecture = Architecture.X64
                    then
                        fstatDarwinX64 (fd handle, data)
                    else
                        fstat (fd handle, data)

                if
                    result
                    <> 0
                then
                    error "file identity" (Marshal.GetLastPInvokeError())

                let device =
                    if OperatingSystem.IsMacOS() then
                        uint64 (uint32 (Marshal.ReadInt32(data, 0)))
                    else
                        uint64 (Marshal.ReadInt64(data, 0))

                struct (device, uint64 (Marshal.ReadInt64(data, 8)), 0UL)
        finally
            Marshal.FreeHGlobal data

    let renameHandle (source: SafeFileHandle) name =
        let bytes = Encoding.Unicode.GetBytes(name: string)
        let rootOffset = if IntPtr.Size = 8 then 8 else 4

        let lengthOffset =
            rootOffset
            + IntPtr.Size

        let nameOffset =
            lengthOffset
            + 4

        let size =
            nameOffset
            + bytes.Length
            + 2

        let data = Marshal.AllocHGlobal size

        try
            Marshal.Copy(Array.zeroCreate<byte> size, 0, data, size)
            // ReplaceIfExists remains FALSE. The source is the verified handle, never a reopened pathname.
            // RootDirectory stays NULL; the full destination path is protected by pinned data-access directory handles.
            Marshal.WriteInt32(data, lengthOffset, bytes.Length)

            Marshal.Copy(
                bytes,
                0,
                data
                + nativeint nameOffset,
                bytes.Length
            )

            setFileInformation (source, 3, data, uint32 size)
        finally
            Marshal.FreeHGlobal data

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
type internal SafeDirectory private (path: string, handle: SafeFileHandle, ancestry: unit -> unit) =
    let identity = NativeFiles.identity handle
    member _.Path = path
    member _.Handle = handle
    member _.Identity = identity

    member _.ValidateLocation() =
        ancestry ()
        // Reopen the expected location only for comparison. All actual I/O remains on the pinned descriptor.
        let current =
            try
                if OperatingSystem.IsWindows() then
                    NativeFiles.createFile (
                        NativeFiles.windowsPath path,
                        0x80000080u,
                        3u,
                        0n,
                        3u,
                        0x02200000u,
                        0n
                    )
                    |> NativeFiles.windowsHandle path
                else
                    NativeFiles.unixOpen (
                        path,
                        NativeFiles.directoryFlags
                        ||| NativeFiles.noFollow
                        ||| NativeFiles.closeOnExec,
                        0u
                    )
                    |> NativeFiles.unixHandle path
            with :? FileNotFoundException ->
                Operations.fail "extension.path_escape" $"Directory moved or disappeared: {path}"

        use current = current
        NativeFiles.attributes path current true

        if
            NativeFiles.identity current
            <> identity
        then
            Operations.fail "extension.path_escape" $"Directory identity changed: {path}"

    member this.Entries() =
        this.ValidateLocation()

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
                    0x80000080u,
                    3u,
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
            new SafeDirectory(full, handle, ignore)
        with _ ->
            handle.Dispose()
            reraise ()

    member this.Child(name: string, create: bool) =
        this.ValidateLocation()

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
                    0x80000080u,
                    3u,
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
            new SafeDirectory(full, child, this.ValidateLocation)
        with _ ->
            child.Dispose()
            reraise ()

    member this.File(name: string, createNew: bool, lease: bool, asynchronous: bool) =
        this.ValidateLocation()

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
                let opened =
                    NativeFiles.createFile (
                        NativeFiles.windowsPath full,
                        (if
                             createNew
                             || lease
                         then
                             (if createNew then 0xC0010000u else 0xC0000000u)
                         else
                             0x80000000u),
                        (if lease then 0u else 1u),
                        0n,
                        (if createNew then 1u
                         elif lease then 4u
                         else 3u),
                        (0x00200080u
                         ||| (if asynchronous then 0x40000000u else 0u)),
                        0n
                    )

                if
                    lease
                    && opened.IsInvalid
                then
                    let error = Marshal.GetLastPInvokeError()

                    if
                        error = 32
                        || error = 33
                    then
                        opened.Dispose()
                        raise (FileLeaseBusyException $"Private I/O lease is busy: {full}")

                NativeFiles.windowsHandle full opened
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
                let error = Marshal.GetLastPInvokeError()

                if
                    error = 11
                    || error = 35
                then
                    raise (FileLeaseBusyException $"Private I/O lease is busy: {full}")

                NativeFiles.error full error

            // Raw Unix SafeFileHandle wrappers have IsAsync=false. .NET still
            // implements their WriteAsync/FlushAsync through awaited I/O; the
            // constructor flag must describe the handle, not our caller's API.
            // Windows alone opened an OVERLAPPED handle above when requested.
            let asynchronousHandle =
                asynchronous
                && OperatingSystem.IsWindows()

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
                    asynchronousHandle
                )

            if not stream.CanSeek then
                stream.Dispose()
                Operations.fail "extension.path_escape" $"Not a regular seekable file: {full}"

            stream
        with _ ->
            file.Dispose()
            reraise ()

    member this.File(name: string, createNew: bool, lease: bool) =
        this.File(name, createNew, lease, false)

    member this.Publish(source: SafeFileHandle, name: string, committed: Action) =
        this.ValidateLocation()

        let published =
            if OperatingSystem.IsWindows() then
                NativeFiles.renameHandle source (NativeFiles.windowsPath (Path.Combine(path, name)))
            elif OperatingSystem.IsLinux() then
                // /proc/self/fd is the kernel descriptor link, not the mutable staging name.
                NativeFiles.linkAt (
                    -100,
                    $"/proc/self/fd/{NativeFiles.fd source}",
                    NativeFiles.fd handle,
                    name,
                    0x400
                ) = 0
            elif OperatingSystem.IsMacOS() then
                NativeFiles.cloneAt (NativeFiles.fd source, NativeFiles.fd handle, name, 0) = 0
            else
                Operations.fail
                    "extension.io_error"
                    "Descriptor-bound publication is not supported on this platform."

        if published then
            // Native visibility precedes validation, which can itself fail.
            // Record that fact before any managed postcommit operation.
            committed.Invoke()
            this.ValidateLocation()
            true
        else
            let code = Marshal.GetLastPInvokeError()

            if
                (OperatingSystem.IsWindows()
                 && (code = 80
                     || code = 183))
                || (not (OperatingSystem.IsWindows())
                    && code = 17)
            then
                false
            else
                NativeFiles.error path code

    member this.Publish(source: SafeFileHandle, name: string) =
        this.Publish(source, name, Action(ignore))

    member _.FileIdentity(file: FileStream) =
        NativeFiles.identity file.SafeFileHandle

    member this.HasFileIdentity(name: string, expected) =
        this.ValidateLocation()

        try
            let full = Path.Combine(path, name)

            use current =
                if OperatingSystem.IsWindows() then
                    NativeFiles.createFile (
                        NativeFiles.windowsPath full,
                        0x80u,
                        7u,
                        0n,
                        3u,
                        0x00200080u,
                        0n
                    )
                    |> NativeFiles.windowsHandle full
                else
                    NativeFiles.openAt (
                        NativeFiles.fd handle,
                        name,
                        NativeFiles.noFollow
                        ||| NativeFiles.closeOnExec
                        ||| NativeFiles.nonBlock,
                        0u
                    )
                    |> NativeFiles.unixHandle full

            NativeFiles.attributes full current false
            NativeFiles.identity current = expected
        with :? FileNotFoundException ->
            false

    member this.DeleteIfIdentity(name: string, expected) =
        this.ValidateLocation()

        if this.HasFileIdentity(name, expected) then
            this.DeleteFile name

    member this.DeleteFile(name: string) =
        this.ValidateLocation()

        if OperatingSystem.IsWindows() then
            File.Delete(Path.Combine(path, name))
        elif
            NativeFiles.unlinkAt (NativeFiles.fd handle, name, 0)
            <> 0
            && Marshal.GetLastPInvokeError()
               <> 2
        then
            NativeFiles.error (Path.Combine(path, name)) (Marshal.GetLastPInvokeError())

    member this.DeleteDirectory(name: string) =
        this.ValidateLocation()

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
    // Private .NET coordination, not the shared state lock or an installed-state document.
    // Every .NET store/run mutation uses this nonwaiting interprocess lease. It is advisory on Unix.
    let mutationLease (root: SafeDirectory) =
        let lease = root.File(".morphir-dotnet-io.lock", false, true)

        try
            if not (root.HasFileIdentity(".morphir-dotnet-io.lock", root.FileIdentity lease)) then
                Operations.fail
                    "extension.path_escape"
                    "The .NET I/O coordination lease changed identity."

            lease
        with _ ->
            lease.Dispose()
            reraise ()

    let tryMutationLease root =
        try
            Some(mutationLease root)
        with :? FileLeaseBusyException ->
            None

    // Retry only actual interprocess contention. Permission/identity failures are refusals.
    let mutationLeaseAsync root (ct: CancellationToken) = task {
        // One state machine owns the wait, including prolonged contention.
        // Recursive task expressions would retain a parent task per retry.
        let mutable acquired: FileStream option = None

        while acquired.IsNone do
            ct.ThrowIfCancellationRequested()

            match tryMutationLease root with
            | Some lease ->
                if ct.IsCancellationRequested then
                    lease.Dispose()
                    ct.ThrowIfCancellationRequested()

                acquired <- Some lease
            | None -> do! Task.Delay(25, ct)

        return acquired.Value
    }

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

    let tryDeleteIdentity (directory: SafeDirectory) name expected =
        try
            directory.DeleteIfIdentity(name, expected)
        with
        | :? IOException
        | :? UnauthorizedAccessException
        | InstallationException _ -> ()
