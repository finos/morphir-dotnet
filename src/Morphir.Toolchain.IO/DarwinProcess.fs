// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

#nowarn "9" // Native layouts and pinned buffers below are specific to Darwin arm64.

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

module private DarwinNative =
    [<Literal>]
    let LibSystem = "/usr/lib/libSystem.B.dylib"
    [<Literal>]
    let LibProc = "/usr/lib/libproc.dylib"
    [<Literal>]
    let Interrupted = 4
    [<Literal>]
    let OperationNotPermitted = 1
    [<Literal>]
    let NoSuchProcess = 3
    [<Literal>]
    let NoChild = 10
    [<Literal>]
    let WouldBlock = 35

    // macOS arm64 SDK: siginfo_t is 104 bytes, aligned to 8.
    [<Struct; StructLayout(LayoutKind.Explicit, Size = 104)>]
    type WaitInfo =
        [<FieldOffset(0)>]
        val mutable Signal: int
        [<FieldOffset(8)>]
        val mutable Code: int
        [<FieldOffset(12)>]
        val mutable Pid: int
        [<FieldOffset(32)>]
        val mutable private Alignment: nativeint

    // sys/proc_info.h: PROC_PIDTBSDINFO, sizeof(struct proc_bsdinfo) = 136.
    [<Struct; StructLayout(LayoutKind.Explicit, Size = 136)>]
    type ProcessInfo =
        [<FieldOffset(4)>]
        val mutable Status: uint32
        [<FieldOffset(12)>]
        val mutable Pid: uint32
        [<FieldOffset(100)>]
        val mutable Group: uint32
        [<FieldOffset(120)>]
        val mutable private Alignment: uint64

    [<DllImport(LibSystem, SetLastError = true)>]
    extern int pipe(int[] ends)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern int close(int fd)

    // On Apple arm64 the first variadic argument goes in the stack slot,
    // after the eight integer argument registers in this fixed signature.
    [<DllImport(LibSystem, EntryPoint = "fcntl", SetLastError = true)>]
    extern int fcntlWithInt(int fd, int command, nativeint x2, nativeint x3, nativeint x4,
                            nativeint x5, nativeint x6, nativeint x7, nativeint argument)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern nativeint read(SafeFileHandle fd, nativeint buffer, unativeint count)

    [<DllImport(LibSystem, SetLastError = true)>]
    extern nativeint write(SafeFileHandle fd, nativeint buffer, unativeint count)

    [<DllImport(LibSystem)>]
    extern int posix_spawnattr_init(nativeint& attr)

    [<DllImport(LibSystem)>]
    extern int posix_spawnattr_destroy(nativeint& attr)

    [<DllImport(LibSystem)>]
    extern int posix_spawnattr_setflags(nativeint& attr, int16 flags)

    [<DllImport(LibSystem)>]
    extern int posix_spawnattr_setpgroup(nativeint& attr, int pgid)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_init(nativeint& actions)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_destroy(nativeint& actions)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_adddup2(nativeint& actions, int source, int target)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_addclose(nativeint& actions, int fd)

    [<DllImport(LibSystem)>]
    extern int posix_spawn_file_actions_addchdir_np(nativeint& actions, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string directory)

    [<DllImport(LibSystem)>]
    extern int posix_spawn(int& pid, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path,
                           nativeint& actions, nativeint& attr, nativeint argv, nativeint envp)

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

    [<DllImport(LibProc, SetLastError = true)>]
    extern int proc_listpids(uint32 kind, uint32 selector, [<Out>] int[] pids, int size)

    [<DllImport(LibProc, SetLastError = true)>]
    extern int proc_pidinfo(int pid, int flavor, uint64 arg, ProcessInfo& info, int size)

    let errno () = Marshal.GetLastPInvokeError()
    let error operation code = Win32Exception(code, $"{operation} failed (Darwin errno {code}).")
    let check operation code = if code <> 0 then raise (error operation code)

    let fcntl fd command value =
        let result = fcntlWithInt(fd, command, 0n, 0n, 0n, 0n, 0n, 0n, nativeint value)
        if result = -1 then raise (error $"fcntl({command})" (errno ()))
        result

    let observeExit pid =
        let rec observe () =
            let mutable info = Unchecked.defaultof<WaitInfo>
            // P_PID, WEXITED | WNOHANG | WNOWAIT. The root stays reserved for group signals.
            if waitid(1, uint32 pid, &info, 0x25) = 0 then
                match info.Pid with
                | 0 -> false
                | found when found = pid && info.Signal = 20 ->
                    // SIGCHLD can also describe a stopped or traced child on Darwin.
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

    let hasLiveMember pgid rootExited =
        let rec query capacity =
            let pids = Array.zeroCreate<int> capacity
            let size = capacity * sizeof<int>
            let bytes = proc_listpids(2u, uint32 pgid, pids, size) // PROC_PGRP_ONLY
            let code = errno ()
            if bytes < 0 || (bytes = 0 && code <> 0) then
                raise (error $"proc_listpids({pgid})" code)
            if bytes > size || bytes % sizeof<int> <> 0 then
                raise (IOException $"Invalid process list for Darwin group {pgid}.")
            if bytes = size then
                if capacity >= 65536 then raise (IOException $"Process list for Darwin group {pgid} is too large.")
                query (capacity * 2)
            else
                pids
                |> Seq.take (bytes / sizeof<int>)
                |> Seq.exists (fun pid ->
                    if pid <= 0 || (pid = pgid && rootExited) then false
                    else
                        let mutable info = Unchecked.defaultof<ProcessInfo>
                        let returned = proc_pidinfo(pid, 3, 0UL, &info, 136) // PROC_PIDTBSDINFO
                        let code = errno ()
                        if returned = 136 && info.Pid = uint32 pid then
                            info.Group = uint32 pgid && info.Status <> 5u // SZOMB
                        elif returned <= 0 && code = NoSuchProcess then false
                        else raise (error $"proc_pidinfo({pid}) returned {returned} bytes" code))
        query 64

/// Keep the host ends nonblocking so teardown can close them even if an
/// inherited end is held outside the group or a guest stopped reading stdin.
type private DarwinPipe(handle: SafeFileHandle, readable: bool) =
    inherit Stream()

    let retry (ct: CancellationToken) code =
        match code with
        | DarwinNative.Interrupted -> Task.CompletedTask
        | DarwinNative.WouldBlock -> Task.Delay(5, ct)
        | _ -> raise (DarwinNative.error "guest pipe I/O" code)

    let readOnce (buffer: Memory<byte>) =
        use pin = buffer.Pin()
        let pointer = pin.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
        let result = DarwinNative.read(handle, pointer, unativeint buffer.Length)
        if result < 0n then Error(DarwinNative.errno ()) else Ok(int result)

    let writeOnce (buffer: ReadOnlyMemory<byte>) =
        use pin = buffer.Pin()
        let pointer = pin.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
        let result = DarwinNative.write(handle, pointer, unativeint buffer.Length)
        if result < 0n then Error(DarwinNative.errno ()) else Ok(int result)

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
                | Ok _ -> raise (IOException "Zero-byte write to a Darwin guest pipe.")
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

module private DarwinLaunch =
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
        if DarwinNative.pipe ends = -1 then raise (DarwinNative.error "pipe" (DarwinNative.errno ()))
        try
            for i in 0 .. 1 do
                let fd = ends[i]
                if fd < 3 then
                    // A dup2 action onto itself could retain CLOEXEC on stdio.
                    ends[i] <- DarwinNative.fcntl fd 67 3 // F_DUPFD_CLOEXEC
                    DarwinNative.close fd |> ignore
                else
                    DarwinNative.fcntl fd 2 1 |> ignore // F_SETFD, FD_CLOEXEC
            new SafeFileHandle(nativeint ends[0], true), new SafeFileHandle(nativeint ends[1], true)
        with _ ->
            for fd in ends do DarwinNative.close fd |> ignore
            reraise ()

    let spawn (start: ProcessStartInfo) (childEnds: SafeFileHandle array) (hostEnds: SafeFileHandle array) =
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
                let check = DarwinNative.check
                let mutable attr = 0n
                check "posix_spawnattr_init" (DarwinNative.posix_spawnattr_init &attr)
                try
                    let mutable actions = 0n
                    check "posix_spawn_file_actions_init" (DarwinNative.posix_spawn_file_actions_init &actions)
                    try
                        // POSIX_SPAWN_SETPGROUP | POSIX_SPAWN_CLOEXEC_DEFAULT.
                        check "posix_spawnattr_setflags" (DarwinNative.posix_spawnattr_setflags(&attr, 0x4002s))
                        check "posix_spawnattr_setpgroup" (DarwinNative.posix_spawnattr_setpgroup(&attr, 0))
                        for i in 0 .. childEnds.Length - 1 do
                            let fd = int (childEnds[i].DangerousGetHandle())
                            check "posix_spawn_file_actions_adddup2" (DarwinNative.posix_spawn_file_actions_adddup2(&actions, fd, i))
                        for fd in Seq.append hostEnds childEnds do
                            check "posix_spawn_file_actions_addclose"
                                (DarwinNative.posix_spawn_file_actions_addclose(&actions, int (fd.DangerousGetHandle())))
                        if start.WorkingDirectory.Length > 0 then
                            check "posix_spawn_file_actions_addchdir_np"
                                (DarwinNative.posix_spawn_file_actions_addchdir_np(&actions, validateText start.WorkingDirectory))
                        let mutable pid = 0
                        check "posix_spawn" (DarwinNative.posix_spawn(&pid, executable, &actions, &attr, argv, envp))
                        pid
                    finally
                        DarwinNative.posix_spawn_file_actions_destroy &actions |> ignore
                finally
                    DarwinNative.posix_spawnattr_destroy &attr |> ignore))

/// Exclusively waits on a Darwin arm64 child spawned as its own group leader.
[<Sealed>]
type internal DarwinChild private (input: DarwinPipe, output: DarwinPipe, errors: DarwinPipe, bound: TimeSpan) =
    let mutable pid = 0
    let gate = obj ()
    let mutable reaped = false
    let mutable releasing = false
    let utf8 = UTF8Encoding(false)
    let stdin = new StreamWriter(input, utf8, 4096, false)
    let stdout = new StreamReader(output, utf8, true, 4096, false)
    let stderr = new StreamReader(errors, utf8, true, 4096, false)

    let rootExited () = DarwinNative.observeExit pid

    let verifyGroup exited =
        if pid <= 1 || pid = DarwinNative.getpgrp() then
            raise (IOException $"Unsafe guest process group {pid}.")
        if not exited then
            let actual = DarwinNative.getpgid pid
            if actual = -1 then
                let code = DarwinNative.errno ()
                // A root may exit between waitid and getpgid.
                if code <> DarwinNative.NoSuchProcess || not (rootExited ()) then
                    raise (DarwinNative.error $"getpgid({pid})" code)
            elif actual <> pid then
                raise (IOException $"Guest PID {pid} changed its process group to {actual}.")

    let stopAndObserve () =
        lock gate (fun () ->
            let exited = rootExited ()
            verifyGroup exited
            if exited && not (DarwinNative.hasLiveMember pid true) then
                // Detect a foreign reap during enumeration as an error.
                rootExited ()
            else
                // Revalidate immediately before every signal. Our waitable
                // child pins the PID, and therefore the creation-time PGID.
                rootExited () |> ignore
                if DarwinNative.killpg(pid, 9) = 0 then false
                else
                    let code = DarwinNative.errno ()
                    // The last live member can exit between the scan and signal.
                    if (code = DarwinNative.NoSuchProcess || code = DarwinNative.OperationNotPermitted)
                       && rootExited () && not (DarwinNative.hasLiveMember pid true) then
                        rootExited ()
                    else
                        raise (DarwinNative.error $"killpg({pid}, SIGKILL)" code))

    let settlement = lazy (task {
        let timer = Stopwatch.StartNew()
        let mutable stopped = stopAndObserve ()
        while not stopped && timer.Elapsed < bound do
            do! Task.Delay 10
            stopped <- stopAndObserve ()
        if not stopped then raise (TimeoutException $"Darwin guest group {pid} did not settle within {bound}.")
    })

    let releaseRoot () =
        lock gate (fun () ->
            // Once this owner begins release or loses wait ownership, there
            // is no path that can start another signal attempt.
            releasing <- true
            if rootExited () then
                DarwinNative.reap pid
                reaped <- true
            else
                raise (IOException $"Darwin guest PID {pid} was still running at release."))

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
            elif releasing then raise (IOException $"Wait ownership of Darwin guest PID {pid} has ended.")
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
        OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture = Architecture.Arm64

    static member Start(start: ProcessStartInfo, deadline: TimeSpan, ct: CancellationToken) =
        if not DarwinChild.IsSupported || Environment.ProcessId <= 1 then
            raise (PlatformNotSupportedException "Exclusive child waits require a non-init Darwin arm64 host.")
        if start.UseShellExecute || not (Path.IsPathFullyQualified start.FileName)
           || start.Arguments.Length > 0 || not start.RedirectStandardInput
           || not start.RedirectStandardOutput || not start.RedirectStandardError then
            invalidArg "start" "An absolute shell-free guest with an argument vector and three redirected streams is required."

        let bound =
            if deadline > TimeSpan.Zero then min deadline (TimeSpan.FromSeconds 5.)
            else TimeSpan.FromSeconds 5.

        ct.ThrowIfCancellationRequested()
        let resources = ResizeArray<SafeFileHandle>()
        let pipe () =
            let read, write = DarwinLaunch.pipe ()
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
                DarwinNative.fcntl (int (fd.DangerousGetHandle())) 4 4 |> ignore // F_SETFL, O_NONBLOCK
            DarwinNative.fcntl (int (hostIn.DangerousGetHandle())) 73 1 |> ignore // F_SETNOSIGPIPE
            let input = new DarwinPipe(hostIn, false)
            let output = new DarwinPipe(hostOut, true)
            let errors = new DarwinPipe(hostErr, true)
            ct.ThrowIfCancellationRequested()
            // Allocate the owner and its stream wrappers before spawning.
            let owner = new DarwinChild(input, output, errors, bound)
            owner.SetPid(DarwinLaunch.spawn start [| childIn; childOut; childErr |] hostEnds)
            owner
        with _ ->
            for fd in resources do fd.Dispose()
            reraise ()
