// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

#nowarn "9" // Linux glibc x64/arm64 layouts and pinned buffers.

open System
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.FSharp.NativeInterop
open Microsoft.Win32.SafeHandles

module private LinuxNative =
    [<Literal>]
    let LibSystem = "libc.so.6"
    [<Literal>]
    let Interrupted = 4
    [<Literal>]
    let OperationNotPermitted = 1
    [<Literal>]
    let NoSuchProcess = 3
    [<Literal>]
    let NoChild = 10
    [<Literal>]
    let WouldBlock = 11

    // Linux siginfo_t on x64 and arm64: 128 bytes, aligned to 8.
    [<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
    type WaitInfo =
        [<FieldOffset(0)>]
        val mutable Signal: int
        [<FieldOffset(8)>]
        val mutable Code: int
        [<FieldOffset(16)>]
        val mutable Pid: int
        [<FieldOffset(24)>]
        val mutable private Alignment: nativeint

    [<DllImport(LibSystem)>]
    extern nativeint gnu_get_libc_version()

    [<DllImport(LibSystem, SetLastError = true)>]
    extern int pipe2(int[] ends, int flags)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern int close(int fd)

    // Linux x64 and arm64 pass the integer fcntl argument in the third register.
    [<DllImport(LibSystem, EntryPoint = "fcntl", SetLastError = true)>]
    extern int fcntlWithInt(int fd, int command, int argument)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern nativeint read(SafeFileHandle fd, nativeint buffer, unativeint count)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern nativeint write(SafeFileHandle fd, nativeint buffer, unativeint count)

    [<DllImport(LibSystem)>]
    extern int posix_spawnattr_init(nativeint attr)

    [<DllImport(LibSystem)>]
    extern int posix_spawnattr_destroy(nativeint attr)

    [<DllImport(LibSystem)>]
    extern int posix_spawnattr_setflags(nativeint attr, int16 flags)

    [<DllImport(LibSystem)>]
    extern int posix_spawnattr_setpgroup(nativeint attr, int pgid)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_init(nativeint actions)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_destroy(nativeint actions)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_adddup2(nativeint actions, int source, int target)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_addchdir_np(nativeint actions, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string directory)

    [<DllImport(LibSystem)>]
    extern int posix_spawn(int& pid, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path,
                           nativeint actions, nativeint attr, nativeint argv, nativeint envp)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern int waitid(int idType, uint32 id, WaitInfo& info, int options)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern int waitpid(int pid, int& status, int options)

    [<DllImport(LibSystem)>]
    extern int getpgrp()

    [<DllImport(LibSystem, SetLastError = true)>]
    extern int getpgid(int pid)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern int killpg(int group, int signal)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_addclosefrom_np(nativeint actions, int from)

    let errno () = Marshal.GetLastPInvokeError()
    let error operation code = Win32Exception(code, $"{operation} failed (Linux errno {code}).")
    let check operation code = if code <> 0 then raise (error operation code)

    let fcntl fd command value =
        let result = fcntlWithInt(fd, command, value)
        if result = -1 then raise (error $"fcntl({command})" (errno ()))
        result

    let observeExit pid =
        let rec observe () =
            let mutable info = Unchecked.defaultof<WaitInfo>
            // P_PID, WEXITED | WNOHANG | WNOWAIT. The root stays reserved for group signals.
            if waitid(1, uint32 pid, &info, 0x1000005) = 0 then
                match info.Pid with
                | 0 -> false
                | found when found = pid && info.Signal = 17 ->
                    // SIGCHLD can also describe a stopped or traced child on Linux.
                    match info.Code with
                    | 1 | 2 | 3 -> true // CLD_EXITED, CLD_KILLED, CLD_DUMPED
                    | 4 | 5 | 6 -> false // CLD_TRAPPED, CLD_STOPPED, CLD_CONTINUED
                    | _ -> raise (IOException $"waitid returned an unknown SIGCHLD event for PID {pid}.")
                | _ -> raise (IOException $"waitid returned unexpected exit information for PID {pid}.")
            else
                match errno () with
                | Interrupted -> observe ()
                | NoChild -> raise (error $"waitid: lost wait ownership of guest PID {pid}" NoChild)
                | code -> raise (error $"waitid({pid})" code)
        observe ()

    let reap pid =
        let rec collect () =
            let mutable status = 0
            let result = waitpid(pid, &status, 1) // WNOHANG
            if result = -1 then
                match errno () with
                | Interrupted -> collect ()
                | NoChild -> raise (error $"waitpid: lost wait ownership of guest PID {pid}" NoChild)
                | code -> raise (error $"waitpid({pid})" code)
            elif result <> pid then
                raise (IOException $"PID {pid} was no longer ready to reap.")
        collect ()

    // comm may contain spaces and ')'; fields after the last ')' start at field 3.
    let processState pid =
        try
            let text = File.ReadAllText($"/proc/{pid}/stat")
            let fields = text.Substring(text.LastIndexOf(')') + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            Some(fields[0], Int32.Parse fields[2]) // state, pgrp
        with
        | :? FileNotFoundException | :? DirectoryNotFoundException -> None

    let requireUnfilteredProcfs () =
        let mounts =
            File.ReadLines("/proc/self/mounts")
            |> Seq.map (fun line -> line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            |> Seq.filter (fun fields -> fields.Length >= 4 && fields[1] = "/proc" && fields[2] = "proc")
            |> Seq.toArray

        let hidesProcesses (fields: string array) =
            fields[3].Split(',')
            |> Array.exists (fun option ->
                option.StartsWith("hidepid=", StringComparison.Ordinal)
                && option <> "hidepid=0" && option <> "hidepid=off")

        if mounts.Length = 0 || Array.exists hidesProcesses mounts then
            raise (PlatformNotSupportedException "Linux process-group ownership requires unfiltered procfs at /proc.")

    let hasLiveMember pgid rootExited =
        Directory.EnumerateDirectories("/proc")
        |> Seq.exists (fun path ->
            match Int32.TryParse(Path.GetFileName path) with
            | true, pid when pid > 0 && not (pid = pgid && rootExited) && getpgid pid = pgid ->
                match processState pid with
                | Some(state, group) -> group = pgid && state <> "Z" && state <> "X"
                | None -> false
            | _ -> false)

/// Keep the host ends nonblocking so teardown can close them even if an
/// inherited end is held outside the group or a guest stopped reading stdin.
type private LinuxPipe(handle: SafeFileHandle, readable: bool) =
    inherit Stream()

    let retry (ct: CancellationToken) code =
        match code with
        | LinuxNative.Interrupted -> Task.CompletedTask
        | LinuxNative.WouldBlock -> Task.Delay(5, ct)
        | _ ->
            let error = LinuxNative.error "guest pipe I/O" code
            raise (IOException(error.Message, error))

    let readOnce (buffer: Memory<byte>) =
        use pin = buffer.Pin()
        let pointer = pin.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
        let result = LinuxNative.read(handle, pointer, unativeint buffer.Length)
        if result < 0n then Error(LinuxNative.errno ()) else Ok(int result)

    let writeOnce (buffer: ReadOnlyMemory<byte>) =
        use pin = buffer.Pin()
        let pointer = pin.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
        let result = LinuxNative.write(handle, pointer, unativeint buffer.Length)
        if result < 0n then Error(LinuxNative.errno ()) else Ok(int result)

    override _.CanRead = readable && not handle.IsClosed
    override _.CanWrite = not readable && not handle.IsClosed
    override _.CanSeek = false
    override _.Length = raise (NotSupportedException())
    override _.Position
        with get () = raise (NotSupportedException())
        and set _ = raise (NotSupportedException())
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength(_) = raise (NotSupportedException())
    override _.Flush() = ()
    override _.FlushAsync(ct) =
        if ct.IsCancellationRequested then Task.FromCanceled ct else Task.CompletedTask

    override _.ReadAsync(buffer: Memory<byte>, ct: CancellationToken) =
        ValueTask<int>(task {
            if not readable then raise (NotSupportedException())
            ct.ThrowIfCancellationRequested()
            let mutable count = if buffer.IsEmpty then 0 else -1
            while count < 0 do
                ct.ThrowIfCancellationRequested()
                match readOnce buffer with
                | Ok received -> count <- received
                | Error code -> do! retry ct code
            return count
        })

    override _.WriteAsync(buffer: ReadOnlyMemory<byte>, ct: CancellationToken) =
        ValueTask(task {
            if readable then raise (NotSupportedException())
            ct.ThrowIfCancellationRequested()
            let mutable offset = 0
            while offset < buffer.Length do
                ct.ThrowIfCancellationRequested()
                match writeOnce (buffer.Slice offset) with
                | Ok sent when sent > 0 -> offset <- offset + sent
                | Ok _ -> raise (IOException "Zero-byte write to a Linux guest pipe.")
                | Error code -> do! retry ct code
        })

    override this.ReadAsync(buffer, offset, count, ct) =
        this.ReadAsync(Memory(buffer, offset, count), ct).AsTask()

    override this.WriteAsync(buffer, offset, count, ct) =
        this.WriteAsync(ReadOnlyMemory(buffer, offset, count), ct).AsTask()

    override this.Read(buffer, offset, count) =
        this.ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult()

    override this.Write(buffer, offset, count) =
        this.WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult()

    override _.Dispose(disposing) =
        if disposing then handle.Dispose()
        base.Dispose(disposing)

module private LinuxLaunch =
    let validateText (text: string) =
        if text.Contains '\000' then invalidArg "start" "A native launch value contains NUL."
        text

    let withVector (values: string array) (action: nativeint -> 'T) =
        let strings = Array.zeroCreate<nativeint> values.Length
        let argv = Marshal.AllocHGlobal((values.Length + 1) * IntPtr.Size)
        try
            for i in 0 .. values.Length - 1 do
                strings[i] <- Marshal.StringToCoTaskMemUTF8(validateText values[i])
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i])
            Marshal.WriteIntPtr(argv, values.Length * IntPtr.Size, 0n)
            action argv
        finally
            for ptr in strings do Marshal.FreeCoTaskMem ptr
            Marshal.FreeHGlobal argv

    let pipe () =
        let ends = Array.zeroCreate<int> 2
        if LinuxNative.pipe2(ends, 0x80000) = -1 then raise (LinuxNative.error "pipe" (LinuxNative.errno ()))
        try
            for i in 0 .. 1 do
                let fd = ends[i]
                if fd < 3 then
                    // A dup2 action onto itself could retain CLOEXEC on stdio.
                    ends[i] <- LinuxNative.fcntl fd 1030 3 // F_DUPFD_CLOEXEC
                    LinuxNative.close fd |> ignore
            new SafeFileHandle(nativeint ends[0], true), new SafeFileHandle(nativeint ends[1], true)
        with _ ->
            for fd in ends do LinuxNative.close fd |> ignore
            reraise ()

    let spawn (start: ProcessStartInfo) (childEnds: SafeFileHandle array) =
        let executable = validateText start.FileName
        let args = Array.append [| executable |] (Seq.toArray start.ArgumentList)
        let environment =
            start.Environment
            |> Seq.choose (fun entry ->
                match entry.Value with
                | null -> None
                | value -> Some(entry.Key + "=" + value))
            |> Seq.toArray
        withVector args (fun argv ->
            withVector environment (fun envp ->
                let check = LinuxNative.check
                let attr = Marshal.AllocHGlobal 336
                let mutable attrInitialized = false
                try
                    check "posix_spawnattr_init" (LinuxNative.posix_spawnattr_init attr)
                    attrInitialized <- true
                    let actions = Marshal.AllocHGlobal 80
                    let mutable actionsInitialized = false
                    try
                        check "posix_spawn_file_actions_init" (LinuxNative.posix_spawn_file_actions_init actions)
                        actionsInitialized <- true
                        // POSIX_SPAWN_SETPGROUP establishes ownership before exec.
                        check "posix_spawnattr_setflags" (LinuxNative.posix_spawnattr_setflags(attr, 0x2s))
                        check "posix_spawnattr_setpgroup" (LinuxNative.posix_spawnattr_setpgroup(attr, 0))
                        for i in 0 .. childEnds.Length - 1 do
                            let fd = int (childEnds[i].DangerousGetHandle())
                            check "posix_spawn_file_actions_adddup2" (LinuxNative.posix_spawn_file_actions_adddup2(actions, fd, i))
                        // glibc >= 2.34. Close every non-stdio descriptor after dup2.
                        check "posix_spawn_file_actions_addclosefrom_np"
                            (LinuxNative.posix_spawn_file_actions_addclosefrom_np(actions, 3))
                        if start.WorkingDirectory.Length > 0 then
                            check "posix_spawn_file_actions_addchdir_np"
                                (LinuxNative.posix_spawn_file_actions_addchdir_np(actions, validateText start.WorkingDirectory))
                        let mutable pid = 0
                        check "posix_spawn" (LinuxNative.posix_spawn(&pid, executable, actions, attr, argv, envp))
                        pid
                    finally
                        if actionsInitialized then LinuxNative.posix_spawn_file_actions_destroy actions |> ignore
                        Marshal.FreeHGlobal actions
                finally
                    if attrInitialized then LinuxNative.posix_spawnattr_destroy attr |> ignore
                    Marshal.FreeHGlobal attr))

/// Exclusively waits on a Linux glibc x64/arm64 child spawned as its own group leader.
[<Sealed>]
type internal LinuxChild private (input: LinuxPipe, output: LinuxPipe, errors: LinuxPipe, bound: TimeSpan) =
    let mutable pid = 0
    let gate = obj ()
    let mutable reaped = false
    let mutable releasing = false
    let utf8 = UTF8Encoding(false)
    let stdin = new StreamWriter(input, utf8, 4096, false)
    let stdout = new StreamReader(output, utf8, true, 4096, false)
    let stderr = new StreamReader(errors, utf8, true, 4096, false)

    let rootExited () = LinuxNative.observeExit pid

    let verifyGroup exited =
        if pid <= 1 || pid = LinuxNative.getpgrp() then
            raise (IOException $"Unsafe guest process group {pid}.")
        if not exited then
            let actual = LinuxNative.getpgid pid
            if actual = -1 then
                let code = LinuxNative.errno ()
                // A root may exit between waitid and getpgid.
                if code <> LinuxNative.NoSuchProcess || not (rootExited ()) then
                    raise (LinuxNative.error $"getpgid({pid})" code)
            elif actual <> pid then
                raise (IOException $"Guest PID {pid} changed its process group to {actual}.")

    let stopAndObserve () =
        lock gate (fun () ->
            let exited = rootExited ()
            verifyGroup exited
            if exited && not (LinuxNative.hasLiveMember pid true) then
                // Detect a foreign reap during enumeration as an error.
                rootExited ()
            else
                // Revalidate immediately before every signal. Our waitable
                // child pins the PID, and therefore the creation-time PGID.
                rootExited () |> ignore
                if LinuxNative.killpg(pid, 9) = 0 then false
                else
                    let code = LinuxNative.errno ()
                    // The last live member can exit between the scan and signal.
                    if (code = LinuxNative.NoSuchProcess || code = LinuxNative.OperationNotPermitted)
                       && rootExited () && not (LinuxNative.hasLiveMember pid true) then
                        rootExited ()
                    else
                        raise (LinuxNative.error $"killpg({pid}, SIGKILL)" code))

    let settlement = lazy (task {
        let timer = Stopwatch.StartNew()
        let mutable stopped = stopAndObserve ()
        while not stopped && timer.Elapsed < bound do
            do! Task.Delay 10
            stopped <- stopAndObserve ()
        if not stopped then raise (TimeoutException $"Linux guest group {pid} did not settle within {bound}.")
    })

    let releaseRoot () =
        lock gate (fun () ->
            // Once this owner begins release or loses wait ownership, there
            // is no path that can start another signal attempt.
            releasing <- true
            if rootExited () then
                LinuxNative.reap pid
                reaped <- true
            else
                raise (IOException $"Linux guest PID {pid} was still running at release."))

    let closePipes () =
        input.Dispose()
        output.Dispose()
        errors.Dispose()

    let release = lazy (task {
        // If settlement fails, surface it and close our descriptors. Reap
        // only our own waitable child and never signal after this point.
        let owner =
            { new IAsyncDisposable with
                member _.DisposeAsync() =
                    ValueTask(task {
                        closePipes ()
                        releaseRoot ()
                    }) }
        do! AsyncOwnership.runAsync owner (fun () -> settlement.Value)
    })

    member private _.SetPid(value) = pid <- value
    member _.StandardInput = stdin
    member _.StandardOutput = stdout
    member _.StandardError = stderr
    member _.SettlementBound = bound
    member _.SettleAsync() = settlement.Value
    member _.ClosePipes() = closePipes ()

    member _.HasExited =
        lock gate (fun () ->
            if reaped then true
            elif releasing then raise (IOException $"Wait ownership of Linux guest PID {pid} has ended.")
            else rootExited ())

    member this.WaitForExitAsync(ct: CancellationToken) : Task =
        task {
            ct.ThrowIfCancellationRequested()
            while not this.HasExited do
                do! Task.Delay(10, ct)
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() = ValueTask(release.Value)

    static member IsSupported =
        OperatingSystem.IsLinux() && (RuntimeInformation.ProcessArchitecture = Architecture.Arm64 || RuntimeInformation.ProcessArchitecture = Architecture.X64)

    static member Start(start: ProcessStartInfo, deadline: TimeSpan, ct: CancellationToken) =
        if not LinuxChild.IsSupported || Environment.ProcessId <= 1 then
            raise (PlatformNotSupportedException "Exclusive child waits require a non-init Linux x64 or arm64 glibc host.")
        if start.UseShellExecute || not (Path.IsPathFullyQualified start.FileName)
           || start.Arguments.Length > 0 || not start.RedirectStandardInput
           || not start.RedirectStandardOutput || not start.RedirectStandardError then
            invalidArg "start" "An absolute shell-free guest with an argument vector and three redirected streams is required."

        // Opaque glibc structs are 336 and 80 bytes on both supported architectures.
        // Another libc must never use these layouts or silently fall back after a failed launch.
        let version =
            try Marshal.PtrToStringUTF8(LinuxNative.gnu_get_libc_version()) |> nonNull |> Version.Parse
            with error -> raise (PlatformNotSupportedException("Linux ownership requires glibc >= 2.34 and readable procfs.", error))
        if version < Version(2, 34) || not (Directory.Exists "/proc/self/fd") then
            raise (PlatformNotSupportedException "Linux ownership requires glibc >= 2.34 and readable procfs.")
        LinuxNative.requireUnfilteredProcfs ()

        let bound =
            if deadline > TimeSpan.Zero then min deadline (TimeSpan.FromSeconds 5.)
            else TimeSpan.FromSeconds 5.

        ct.ThrowIfCancellationRequested()
        let resources = ResizeArray<SafeFileHandle>()
        let pipe () =
            let read, write = LinuxLaunch.pipe ()
            resources.Add read
            resources.Add write
            read, write

        try
            let childIn, hostIn = pipe ()
            let hostOut, childOut = pipe ()
            let hostErr, childErr = pipe ()
            use childIn = childIn
            use childOut = childOut
            use childErr = childErr
            let hostEnds = [| hostIn; hostOut; hostErr |]
            for fd in hostEnds do
                LinuxNative.fcntl (int (fd.DangerousGetHandle())) 4 2048 |> ignore // F_SETFL, O_NONBLOCK
            let input = new LinuxPipe(hostIn, false)
            let output = new LinuxPipe(hostOut, true)
            let errors = new LinuxPipe(hostErr, true)
            ct.ThrowIfCancellationRequested()
            // Allocate the owner and its stream wrappers before spawning.
            let owner = new LinuxChild(input, output, errors, bound)
            owner.SetPid(LinuxLaunch.spawn start [| childIn; childOut; childErr |])
            owner
        with _ ->
            for fd in resources do fd.Dispose()
            reraise ()
