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

    let runAsync (ct: CancellationToken) (work: unit -> Task<'T>) : Task<InstallationResult<'T>> =
        Task.Run<InstallationResult<'T>>(
            Func<Task<InstallationResult<'T>>>(fun () -> task {
                try
                    ct.ThrowIfCancellationRequested()
                    let! value = work ()
                    return InstallationResult(Ok value)
                with
                | InstallationException error -> return InstallationResult(Error error)
                | :? OperationCanceledException ->
                    return
                        InstallationResult(
                            Error(
                                InstallationFailure(
                                    "extension.cancelled",
                                    "The operation was cancelled."
                                )
                            )
                        )
                | :? IOException as error ->
                    return
                        InstallationResult(
                            Error(InstallationFailure("extension.io_error", error.Message))
                        )
                | :? UnauthorizedAccessException as error ->
                    return
                        InstallationResult(
                            Error(InstallationFailure("extension.io_error", error.Message))
                        )
                | :? ArgumentException as error ->
                    return
                        InstallationResult(
                            Error(InstallationFailure("extension.invalid_path", error.Message))
                        )
            })
        )

    let run (ct: CancellationToken) work =
        runAsync ct (fun () -> Task.FromResult(work ()))

// Windows directories deny deletion while permitting child writes. Unix operations are
// relative to open directory descriptors with O_NOFOLLOW. Descriptor identity is preserved through publication.
// Directory ancestry checks detect rename-out, but advisory coordination is not same-UID OS isolation.
// API references: CreateFileW / FILE_FLAG_OPEN_REPARSE_POINT (Microsoft Learn); openat(2), linkat(2), flock(2).
module private NativeFiles =
    [<DllImport("kernel32.dll",
                EntryPoint = "GetFinalPathNameByHandleW",
                CharSet = CharSet.Unicode,
                SetLastError = true)>]
    extern uint32 finalPath(SafeFileHandle handle, StringBuilder text, uint32 length, uint32 flags)

    [<DllImport("libc", EntryPoint = "realpath", SetLastError = true)>]
    extern nativeint realPath([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, nativeint resolved)

    [<DllImport("libc", EntryPoint = "free")>]
    extern void free(nativeint memory)

    [<DllImport("kernel32.dll", EntryPoint = "LockFileEx", SetLastError = true)>]
    extern bool lockFile(
        SafeFileHandle file,
        uint32 flags,
        uint32 reserved,
        uint32 low,
        uint32 high,
        nativeint overlapped
    )

    [<DllImport("kernel32.dll", EntryPoint = "CancelIoEx", SetLastError = true)>]
    extern bool cancelIo(SafeFileHandle file, nativeint overlapped)

    [<DllImport("kernel32.dll", EntryPoint = "GetOverlappedResult", SetLastError = true)>]
    extern bool overlappedResult(
        SafeFileHandle file,
        nativeint overlapped,
        uint32& transferred,
        bool wait
    )

    [<DllImport("kernel32.dll",
                EntryPoint = "MoveFileExW",
                CharSet = CharSet.Unicode,
                SetLastError = true)>]
    extern bool moveDirectory(string source, string target, uint32 flags)

    [<DllImport("libc", EntryPoint = "renameat2", SetLastError = true)>]
    extern int renameAtNoReplace(
        int sourceDirectory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string source,
        int targetDirectory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string target,
        uint32 flags
    )

    [<DllImport("libc", EntryPoint = "renameatx_np", SetLastError = true)>]
    extern int renameAtDarwin(
        int sourceDirectory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string source,
        int targetDirectory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string target,
        uint32 flags
    )

    [<DllImport("libc", EntryPoint = "renameat", SetLastError = true)>]
    extern int renameAt(
        int sourceDirectory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string source,
        int targetDirectory,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string target
    )

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

    [<DllImport("libc", EntryPoint = "fcntl", SetLastError = true)>]
    extern int fcntlFixed(int descriptor, int command, int argument)

    // fcntl is variadic like open: on Darwin arm64 the argument goes in the first stack slot.
    [<DllImport("libc", EntryPoint = "fcntl", SetLastError = true)>]
    extern int fcntlDarwinArm64(
        int descriptor,
        int command,
        nativeint padding2,
        nativeint padding3,
        nativeint padding4,
        nativeint padding5,
        nativeint padding6,
        nativeint padding7,
        int argument
    )

    let fcntl (descriptor, command, argument) =
        match RuntimeInformation.ProcessArchitecture with
        | Architecture.Arm64 when OperatingSystem.IsMacOS() ->
            fcntlDarwinArm64 (descriptor, command, 0n, 0n, 0n, 0n, 0n, 0n, argument)
        | Architecture.X64 when
            OperatingSystem.IsMacOS()
            || OperatingSystem.IsLinux()
            ->
            fcntlFixed (descriptor, command, argument)
        | Architecture.Arm64 when OperatingSystem.IsLinux() -> fcntlFixed (descriptor, command, argument)
        | _ -> Operations.fail "extension.io_error" "Unsupported native fcntl ABI."

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

    // A flock belongs to the open file description, not to the descriptor. A child process that another
    // thread is starting holds a copy of every descriptor until its exec closes the O_CLOEXEC ones, so close()
    // alone can leave the lock held after the owner returned. LOCK_UN releases it on the shared description.
    let unlock (handle: SafeFileHandle) =
        if
            not (OperatingSystem.IsWindows())
            && not handle.IsClosed
            && not handle.IsInvalid
        then
            flock (fd handle, 8 (* LOCK_UN *))
            |> ignore

    let release (handle: SafeFileHandle) =
        unlock handle
        handle.Dispose()

    // O_NONBLOCK lets the open of a planted FIFO return instead of blocking. A regular file needs no non-blocking
    // I/O, and from .NET 11 SafeFileHandle.IsAsync reads O_NONBLOCK from the descriptor: FileStream then reads and
    // writes at the descriptor offset and ignores its Position. Clear the flag before a FileStream wraps the handle,
    // and before anything reads IsAsync, which .NET caches.
    let synchronous path (handle: SafeFileHandle) =
        let flags = fcntl (fd handle, 3 (* F_GETFL *), 0)

        if flags < 0 then
            error path (Marshal.GetLastPInvokeError())

        if
            flags &&& nonBlock <> 0
            && fcntl (fd handle, 4 (* F_SETFL *), flags &&& ~~~nonBlock) < 0
        then
            error path (Marshal.GetLastPInvokeError())

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

    let renameHandleMode (source: SafeFileHandle) name replace =
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

            if replace then
                Marshal.WriteByte(data, 0, 1uy)
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

    let renameHandle source name = renameHandleMode source name false

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

/// The stream of a lease. Its disposal releases the lock before it closes the descriptor (NativeFiles.unlock),
/// so the next owner can take the lock as soon as disposal returns.
[<Sealed>]
type internal LeaseStream(handle: SafeFileHandle, access: FileAccess, bufferSize: int, isAsync: bool) =
    inherit FileStream(handle, access, bufferSize, isAsync)

    override this.Dispose(disposing: bool) =
        if
            disposing
            && not handle.IsClosed
        then
            // Unlock only after buffered writes reach the file, so no write lands outside the lock. When the
            // flush fails, the base disposal reports it and close() releases the lock.
            let flushed =
                try
                    if this.CanWrite then
                        this.Flush()

                    true
                with _ ->
                    false

            if flushed then
                NativeFiles.unlock handle

        base.Dispose(disposing)

    // FileStream.DisposeAsync does not call Dispose(bool); route it through the unlocking disposal.
    override this.DisposeAsync() =
        try
            this.Dispose()
            ValueTask.CompletedTask
        with error ->
            ValueTask(Task.FromException error)

/// One trusted directory. Internal so callers cannot manufacture trusted filesystem state.
type internal SafeDirectory private (path: string, handle: SafeFileHandle, ancestry: unit -> unit) =
    let identity = NativeFiles.identity handle
    member _.Path = path
    member _.Handle = handle
    member _.Identity = identity

    member this.CanonicalPath =
        this.ValidateLocation()

        let canonical =
            if OperatingSystem.IsWindows() then
                let buffer = StringBuilder(32768)
                let length = NativeFiles.finalPath (handle, buffer, uint32 buffer.Capacity, 0u)

                if
                    length = 0u
                    || length
                       >= uint32 buffer.Capacity
                then
                    NativeFiles.error path (Marshal.GetLastPInvokeError())

                let value = buffer.ToString()

                let ordinary =
                    if value.StartsWith("\\\\?\\UNC\\", StringComparison.Ordinal) then
                        "\\\\"
                        + value.Substring(8)
                    elif value.StartsWith("\\\\?\\", StringComparison.Ordinal) then
                        value.Substring(4)
                    else
                        value

                if
                    ordinary.Length > 1
                    && ordinary[1] = ':'
                then
                    ordinary.Substring(0, 1).ToUpperInvariant()
                    + ordinary.Substring(1)
                else
                    ordinary
            else
                let result = NativeFiles.realPath (path, 0n)

                if result = 0n then
                    NativeFiles.error path (Marshal.GetLastPInvokeError())

                try
                    Marshal.PtrToStringUTF8(result)
                    |> Operations.requiredPath
                finally
                    NativeFiles.free result

        let root =
            Path.GetPathRoot canonical
            |> Operations.requiredPath

        let canonical =
            if canonical.Length = root.Length then
                canonical
            else
                canonical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

        this.ValidateLocation()
        canonical

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

            if not (OperatingSystem.IsWindows()) then
                NativeFiles.synchronous full file

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

            // Unix descriptors are synchronous here (O_NONBLOCK is cleared above), so
            // IsAsync=false. .NET still implements their WriteAsync/FlushAsync through
            // awaited I/O; the constructor flag must describe the handle, not our caller's API.
            // Windows alone opened an OVERLAPPED handle above when requested.
            let asynchronousHandle =
                asynchronous
                && OperatingSystem.IsWindows()

            let access =
                if
                    createNew
                    || lease
                then
                    FileAccess.ReadWrite
                else
                    FileAccess.Read

            let stream =
                if lease then
                    new LeaseStream(file, access, 64 * 1024, asynchronousHandle) :> FileStream
                else
                    new FileStream(file, access, 64 * 1024, asynchronousHandle)

            if not stream.CanSeek then
                stream.Dispose()
                Operations.fail "extension.path_escape" $"Not a regular seekable file: {full}"

            stream
        with _ ->
            NativeFiles.release file
            reraise ()

    member this.File(name: string, createNew: bool, lease: bool) =
        this.File(name, createNew, lease, false)

    // Only state transactions call replacement, after validated admission under the shared lock.
    member this.ReplaceStateFile(name: string, bytes: byte array) =
        this.ValidateLocation()

        if
            name.Length = 0
            || name = "."
            || name = ".."
            || name.IndexOfAny([| '/'; '\\' |])
               >= 0
        then
            Operations.fail "extension.path_escape" "State filename is not a plain component."

        if
            this.Entries()
            |> Array.contains name
        then
            use previous = this.File(name, false, false)

            previous.Length
            |> ignore

        let stageName =
            ".stage-"
            + Guid.NewGuid().ToString("N")

        use stage = this.File(stageName, true, false)
        let identity = this.FileIdentity stage
        let mutable committed = false

        try
            stage.Write bytes
            stage.Flush true
            this.ValidateLocation()

            if not (this.HasFileIdentity(stageName, identity)) then
                Operations.fail "extension.path_escape" "State staging identity changed."

            let success =
                if OperatingSystem.IsWindows() then
                    NativeFiles.renameHandleMode
                        stage.SafeFileHandle
                        (NativeFiles.windowsPath (Path.Combine(path, name)))
                        true
                else
                    NativeFiles.renameAt (
                        NativeFiles.fd handle,
                        stageName,
                        NativeFiles.fd handle,
                        name
                    ) = 0

            if not success then
                NativeFiles.error path (Marshal.GetLastPInvokeError())

            committed <- true
            this.ValidateLocation()

            if not (this.HasFileIdentity(name, identity)) then
                Operations.fail "extension.path_escape" "Published state identity changed."
        finally
            stage.Dispose()

            if not committed then
                this.DeleteIfIdentity(stageName, identity)

    // Contract locks permit a second open: exclusion belongs to the kernel lock,
    // never FileShare.None. Closing this open description releases its lock.
    member this.NamedLease(name: string, exclusive: bool, wait: bool) =
        this.ValidateLocation()

        if
            name.Length = 0
            || name = "."
            || name = ".."
            || name.IndexOfAny([| '/'; '\\' |])
               >= 0
        then
            Operations.fail "extension.path_escape" "Lease name is not a plain component."

        let full = Path.Combine(path, name)

        let file =
            if OperatingSystem.IsWindows() then
                NativeFiles.createFile (
                    NativeFiles.windowsPath full,
                    0xC0000000u,
                    7u,
                    0n,
                    4u,
                    0x00200080u,
                    0n
                )
                |> NativeFiles.windowsHandle full
            else
                NativeFiles.openAt (
                    NativeFiles.fd handle,
                    name,
                    2
                    ||| NativeFiles.create
                    ||| NativeFiles.noFollow
                    ||| NativeFiles.closeOnExec,
                    0o644u
                )
                |> NativeFiles.unixHandle full

        try
            NativeFiles.attributes full file false

            let locked =
                if OperatingSystem.IsWindows() then
                    let overlapped = Marshal.AllocHGlobal(32)

                    try
                        Marshal.Copy(Array.zeroCreate<byte> 32, 0, overlapped, 32)

                        NativeFiles.lockFile (
                            file,
                            (if exclusive then 2u else 0u)
                            ||| (if wait then 0u else 1u),
                            0u,
                            UInt32.MaxValue,
                            UInt32.MaxValue,
                            overlapped
                        )
                    finally
                        Marshal.FreeHGlobal overlapped
                else
                    NativeFiles.flock (
                        NativeFiles.fd file,
                        (if exclusive then 2 else 1)
                        ||| (if wait then 0 else 4)
                    ) = 0

            if not locked then
                let error = Marshal.GetLastPInvokeError()

                if
                    (OperatingSystem.IsWindows()
                     && error = 33)
                    || (not (OperatingSystem.IsWindows())
                        && (error = 11
                            || error = 35))
                then
                    raise (FileLeaseBusyException $"Lease is busy: {full}")

                NativeFiles.error full error

            if not (this.HasFileIdentity(name, NativeFiles.identity file)) then
                Operations.fail
                    "extension.path_escape"
                    "Lock file was replaced while acquiring ownership."

            this.ValidateLocation()
            new LeaseStream(file, FileAccess.ReadWrite, 1, false) :> FileStream
        with _ ->
            NativeFiles.release file
            reraise ()

    // Windows cancellation cancels the exact pending OVERLAPPED and waits for its
    // completion before releasing its memory/handle. Unix never parks a kernel
    // waiter: each nonblocking flock attempt is followed by an awaited timer.
    member this.NamedLeaseObservedAsync
        (
            name: string,
            exclusive: bool,
            wait: bool,
            ct: CancellationToken,
            completed: Action<int>
        ) : Task<FileStream> =
        Task.Run<FileStream>(
            Func<Task<FileStream>>(fun () -> task {
                ct.ThrowIfCancellationRequested()
                this.ValidateLocation()

                if
                    name.Length = 0
                    || name = "."
                    || name = ".."
                    || name.IndexOfAny([| '/'; '\\' |])
                       >= 0
                then
                    Operations.fail "extension.path_escape" "Lease name is not a plain component."

                let full = Path.Combine(path, name)

                let file =
                    if OperatingSystem.IsWindows() then
                        NativeFiles.createFile (
                            NativeFiles.windowsPath full,
                            0xC0000000u,
                            7u,
                            0n,
                            4u,
                            0x40200080u,
                            0n
                        )
                        |> NativeFiles.windowsHandle full
                    else
                        NativeFiles.openAt (
                            NativeFiles.fd handle,
                            name,
                            2
                            ||| NativeFiles.create
                            ||| NativeFiles.noFollow
                            ||| NativeFiles.closeOnExec,
                            0o644u
                        )
                        |> NativeFiles.unixHandle full

                try
                    NativeFiles.attributes full file false

                    if OperatingSystem.IsWindows() then
                        use signal = new EventWaitHandle(false, EventResetMode.ManualReset)
                        let size = if IntPtr.Size = 8 then 32 else 20
                        let eventOffset = if IntPtr.Size = 8 then 24 else 16
                        let overlapped = Marshal.AllocHGlobal(size)

                        try
                            Marshal.Copy(Array.zeroCreate<byte> size, 0, overlapped, size)

                            Marshal.WriteIntPtr(
                                overlapped,
                                eventOffset,
                                signal.SafeWaitHandle.DangerousGetHandle()
                            )
                            // All managed wait/cancellation setup precedes the syscall. A setup
                            // failure therefore cannot leave the kernel owning OVERLAPPED memory.
                            let settled =
                                TaskCompletionSource<unit>(
                                    TaskCreationOptions.RunContinuationsAsynchronously
                                )

                            let registered =
                                ThreadPool.RegisterWaitForSingleObject(
                                    signal,
                                    WaitOrTimerCallback(fun _ _ ->
                                        settled.TrySetResult()
                                        |> ignore
                                    ),
                                    null,
                                    Timeout.Infinite,
                                    true
                                )

                            try
                                let issueGate = obj ()
                                let mutable issued = false
                                let mutable cancelFailure = 0

                                let requestCancellation () =
                                    lock
                                        issueGate
                                        (fun () ->
                                            if
                                                issued
                                                && not (NativeFiles.cancelIo (file, overlapped))
                                            then
                                                let error = Marshal.GetLastPInvokeError()
                                                // Completion may win the race with cancellation.
                                                if
                                                    error
                                                    <> 1168
                                                then
                                                    cancelFailure <- error
                                        )

                                let cancellation = ct.Register(Action(requestCancellation))

                                use cancellationLifetime =
                                    { new IAsyncDisposable with
                                        member _.DisposeAsync() = cancellation.DisposeAsync()
                                    }

                                let immediate, initial =
                                    lock
                                        issueGate
                                        (fun () ->
                                            ct.ThrowIfCancellationRequested()

                                            let immediate =
                                                NativeFiles.lockFile (
                                                    file,
                                                    (if exclusive then 2u else 0u)
                                                    ||| (if wait then 0u else 1u),
                                                    0u,
                                                    UInt32.MaxValue,
                                                    UInt32.MaxValue,
                                                    overlapped
                                                )

                                            let initial =
                                                if immediate then
                                                    0
                                                else
                                                    Marshal.GetLastPInvokeError()

                                            issued <-
                                                not immediate
                                                && initial = 997

                                            immediate, initial
                                        )

                                if
                                    not immediate
                                    && initial = 997
                                then
                                    // A cancellation racing with admission either ran after issued
                                    // became true, or is observed explicitly here.
                                    if ct.IsCancellationRequested then
                                        requestCancellation ()

                                    let mutable observerFailure: exn option = None

                                    try
                                        completed.Invoke 997
                                    with error ->
                                        observerFailure <- Some error
                                        requestCancellation ()
                                    // Cancellation requests do not complete this task; only the kernel event does.
                                    do! settled.Task
                                    let mutable transferred = 0u

                                    let acquired =
                                        NativeFiles.overlappedResult (
                                            file,
                                            overlapped,
                                            &transferred,
                                            false
                                        )

                                    let error =
                                        if acquired then 0 else Marshal.GetLastPInvokeError()

                                    lock issueGate (fun () -> issued <- false)
                                    completed.Invoke error

                                    match observerFailure with
                                    | Some failure -> raise failure
                                    | None -> ()

                                    if
                                        error = 995
                                        && ct.IsCancellationRequested
                                    then
                                        ct.ThrowIfCancellationRequested()

                                    if not acquired then
                                        NativeFiles.error full error

                                    if
                                        cancelFailure
                                        <> 0
                                    then
                                        NativeFiles.error full cancelFailure
                                elif not immediate then
                                    completed.Invoke initial

                                    if initial = 33 then
                                        raise (FileLeaseBusyException $"Lease is busy: {full}")

                                    NativeFiles.error full initial
                                else
                                    completed.Invoke 0
                            finally
                                registered.Unregister(null)
                                |> ignore
                        finally
                            Marshal.FreeHGlobal overlapped
                    else
                        let mutable acquired = false

                        while not acquired do
                            ct.ThrowIfCancellationRequested()

                            if
                                NativeFiles.flock (
                                    NativeFiles.fd file,
                                    (if exclusive then 2 else 1)
                                    ||| 4
                                ) = 0
                            then
                                acquired <- true
                            else
                                let error = Marshal.GetLastPInvokeError()

                                if
                                    error = 11
                                    || error = 35
                                then
                                    if not wait then
                                        raise (FileLeaseBusyException $"Lease is busy: {full}")

                                    do! Task.Delay(25, ct)
                                elif error <> 4 then
                                    NativeFiles.error full error

                        completed.Invoke 0

                    ct.ThrowIfCancellationRequested()

                    if not (this.HasFileIdentity(name, NativeFiles.identity file)) then
                        Operations.fail
                            "extension.path_escape"
                            "Lock file changed identity while acquiring ownership."

                    this.ValidateLocation()

                    return
                        new LeaseStream(file, FileAccess.ReadWrite, 1, OperatingSystem.IsWindows())
                        :> FileStream
                with error ->
                    NativeFiles.release file
                    return raise error
            })
        )

    member this.NamedLeaseAsync(name: string, exclusive: bool, wait: bool, ct: CancellationToken) =
        this.NamedLeaseObservedAsync(name, exclusive, wait, ct, Action<int>(ignore))

    member this.PublishDirectory
        (
            sourceDirectory: SafeDirectory,
            sourceName: string,
            targetName: string
        ) =
        this.ValidateLocation()
        sourceDirectory.ValidateLocation()

        for name in [ sourceName; targetName ] do
            if
                name.Length = 0
                || name = "."
                || name = ".."
                || name.IndexOfAny([| '/'; '\\' |])
                   >= 0
            then
                Operations.fail "extension.path_escape" "Publication requires plain sibling names."

        let published =
            if OperatingSystem.IsWindows() then
                NativeFiles.moveDirectory (
                    Path.Combine(sourceDirectory.Path, sourceName),
                    Path.Combine(path, targetName),
                    0u
                )
            elif OperatingSystem.IsLinux() then
                NativeFiles.renameAtNoReplace (
                    NativeFiles.fd sourceDirectory.Handle,
                    sourceName,
                    NativeFiles.fd handle,
                    targetName,
                    1u
                ) = 0
            elif OperatingSystem.IsMacOS() then
                NativeFiles.renameAtDarwin (
                    NativeFiles.fd sourceDirectory.Handle,
                    sourceName,
                    NativeFiles.fd handle,
                    targetName,
                    4u
                ) = 0
            else
                Operations.fail "extension.io_error" "Unsupported directory publication platform."

        if not published then
            let error = Marshal.GetLastPInvokeError()

            if
                (OperatingSystem.IsWindows()
                 && (error = 80
                     || error = 183))
                || (not (OperatingSystem.IsWindows())
                    && (error = 17
                        || error = 66
                        || error = 39))
            then
                false
            elif
                (OperatingSystem.IsWindows()
                 && error = 17)
                || (not (OperatingSystem.IsWindows())
                    && error = 18)
            then
                Operations.fail
                    "package.store_not_one_volume"
                    "Package stage and store must share one volume."
            else
                NativeFiles.error path error
        else
            this.ValidateLocation()
            true

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
