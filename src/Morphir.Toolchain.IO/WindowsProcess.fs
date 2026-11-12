// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

open System
open System.ComponentModel
open System.Runtime.InteropServices
open System.Runtime.Versioning
open Microsoft.Win32.SafeHandles

// Extracted async ownership boundary. Tests exercise final-disposal precedence
// without inventing native failures or supplying a mutable process hook.
[<RequireQualifiedAccess>]
module internal AsyncOwnership =
    let runAsync (owner: IAsyncDisposable) (operation: unit -> System.Threading.Tasks.Task<'T>) = task {
        let! result = task {
            try
                let! value = operation ()
                return Ok value
            with primary ->
                return Error primary
        }

        let! disposal = task {
            try
                do! owner.DisposeAsync()
                return None
            with error ->
                return Some error
        }

        match result, disposal with
        | Ok value, None -> return value
        | Ok _, Some error -> return raise error
        | Error primary, cleanup ->
            cleanup
            |> Option.iter (fun error -> primary.Data["Morphir.Launch.DisposalFailure"] <- error)

            return raise primary
    }

    /// The same awaited ownership rule for internal BCL/C# consumers.
    let RunAsync(owner: IAsyncDisposable, operation: Func<System.Threading.Tasks.Task<'T>>) =
        runAsync owner (fun () -> operation.Invoke())

// Shared Win32 job declarations. The host's lifetime job and the manager's
// per-launch job use the same creation, limits and handle ownership rules.
module private JobNative =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type BasicLimits =
        val mutable ProcessTime: int64
        val mutable JobTime: int64
        val mutable Flags: uint32
        val mutable MinimumWorkingSet: unativeint
        val mutable MaximumWorkingSet: unativeint
        val mutable ActiveLimit: uint32
        val mutable Affinity: unativeint
        val mutable Priority: uint32
        val mutable Scheduling: uint32

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type IoCounters =
        val mutable ReadOperations: uint64
        val mutable WriteOperations: uint64
        val mutable OtherOperations: uint64
        val mutable ReadBytes: uint64
        val mutable WriteBytes: uint64
        val mutable OtherBytes: uint64

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type ExtendedLimits =
        val mutable Basic: BasicLimits
        val mutable Io: IoCounters
        val mutable ProcessMemory: unativeint
        val mutable JobMemory: unativeint
        val mutable PeakProcessMemory: unativeint
        val mutable PeakJobMemory: unativeint

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type Accounting =
        val mutable UserTime: int64
        val mutable KernelTime: int64
        val mutable PeriodUserTime: int64
        val mutable PeriodKernelTime: int64
        val mutable PageFaults: uint32
        val mutable TotalProcesses: uint32
        val mutable ActiveProcesses: uint32
        val mutable TerminatedProcesses: uint32

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern nativeint CreateJobObjectW(nativeint attributes, nativeint name)

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool SetInformationJobObject(
        nativeint job,
        int informationClass,
        ExtendedLimits& limits,
        uint32 length
    )

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool AssignProcessToJobObject(nativeint job, nativeint child)

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool QueryInformationJobObject(
        nativeint job,
        int informationClass,
        Accounting& accounting,
        uint32 length,
        nativeint returned
    )

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool TerminateJobObject(nativeint job, uint32 exitCode)

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool CloseHandle(nativeint handle)

    let error operation =
        Win32Exception(Marshal.GetLastPInvokeError(), operation)

type private JobHandle(value: nativeint) =
    inherit SafeHandleZeroOrMinusOneIsInvalid(true)
    do base.SetHandle value
    override this.ReleaseHandle() = JobNative.CloseHandle this.handle

/// An unnamed non-inheritable job. No breakaway flags or child-visible handle.
type private OwnedJob(handle: JobHandle) =
    member _.Handle = handle.DangerousGetHandle()

    static member Create() =
        let raw = JobNative.CreateJobObjectW(0n, 0n)

        if raw = 0n then
            raise (JobNative.error "CreateJobObjectW")

        let handle = new JobHandle(raw)
        let mutable limits = Unchecked.defaultof<JobNative.ExtendedLimits>
        let mutable basic = Unchecked.defaultof<JobNative.BasicLimits>
        basic.Flags <- 0x2000u // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; no breakaway.
        limits.Basic <- basic

        if
            not (
                JobNative.SetInformationJobObject(
                    raw,
                    9,
                    &limits,
                    uint32 (Marshal.SizeOf<JobNative.ExtendedLimits>())
                )
            )
        then
            let error = JobNative.error "SetInformationJobObject"
            handle.Dispose()
            raise error

        new OwnedJob(handle)

    member this.AssignCurrentProcess() =
        if not (JobNative.AssignProcessToJobObject(this.Handle, -1n)) then
            raise (JobNative.error "AssignProcessToJobObject")

    member this.ActiveProcesses =
        let mutable accounting = Unchecked.defaultof<JobNative.Accounting>

        if
            not (
                JobNative.QueryInformationJobObject(
                    this.Handle,
                    1,
                    &accounting,
                    uint32 (Marshal.SizeOf<JobNative.Accounting>()),
                    0n
                )
            )
        then
            raise (JobNative.error "QueryInformationJobObject")

        accounting.ActiveProcesses

    member this.RequestTermination() =
        if JobNative.TerminateJobObject(this.Handle, 1u) then
            None
        else
            Some(JobNative.error "TerminateJobObject" :> exn)

    interface IDisposable with
        member _.Dispose() = handle.Dispose()

/// Shared process-lifetime behavior of the existing host job wrapper.
/// Per-launch ownership below never assigns the manager itself to its child job.
[<AbstractClass; Sealed; SupportedOSPlatform("windows")>]
type internal WindowsProcessLifetime private () =
    static let gate = obj ()
    static let mutable owner: OwnedJob option = None

    static member JoinCurrentProcess() =
        if not (OperatingSystem.IsWindows()) then
            raise (PlatformNotSupportedException("Windows job ownership is required."))

        lock
            gate
            (fun () ->
                match owner with
                | Some _ -> ()
                | None ->
                    let job = OwnedJob.Create()

                    try
                        job.AssignCurrentProcess()
                        owner <- Some job
                    with error ->
                        (job :> IDisposable).Dispose()
                        raise error
            )


module private ChildNative =
    open System.Text

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type StartupInfo =
        val mutable Size: int
        val mutable Reserved: nativeint
        val mutable Desktop: nativeint
        val mutable Title: nativeint
        val mutable X: int
        val mutable Y: int
        val mutable XSize: int
        val mutable YSize: int
        val mutable XChars: int
        val mutable YChars: int
        val mutable Fill: int
        val mutable Flags: int
        val mutable Show: int16
        val mutable ReservedBytes: int16
        val mutable ReservedPointer: nativeint
        val mutable Input: nativeint
        val mutable Output: nativeint
        val mutable Error: nativeint

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type StartupInfoEx =
        val mutable Startup: StartupInfo
        val mutable Attributes: nativeint

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type ProcessInfo =
        val mutable Process: nativeint
        val mutable Thread: nativeint
        val mutable Pid: int
        val mutable Tid: int

    [<DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)>]
    extern bool CreateProcessW(
        string application,
        StringBuilder command,
        nativeint processSecurity,
        nativeint threadSecurity,
        bool inheritHandles,
        uint32 flags,
        nativeint environment,
        string directory,
        StartupInfoEx& startup,
        ProcessInfo& result
    )

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool InitializeProcThreadAttributeList(
        nativeint list,
        int count,
        int flags,
        unativeint& size
    )

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool UpdateProcThreadAttribute(
        nativeint list,
        uint32 flags,
        nativeint attribute,
        nativeint value,
        unativeint size,
        nativeint previous,
        nativeint returned
    )

    [<DllImport("kernel32.dll", ExactSpelling = true)>]
    extern void DeleteProcThreadAttributeList(nativeint list)

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool DuplicateHandle(
        nativeint sourceProcess,
        nativeint source,
        nativeint targetProcess,
        nativeint& target,
        uint32 access,
        bool inheritHandle,
        uint32 options
    )

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern nativeint GetStdHandle(int kind)

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern uint32 WaitForSingleObject(nativeint handle, uint32 milliseconds)

    [<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
    extern bool GetExitCodeProcess(nativeint handle, uint32& exitCode)

    let quote (value: string) =
        if value.Contains('\000') then
            invalidArg "value" "Arguments cannot contain NUL."

        let result = StringBuilder("\"")
        let mutable slashes = 0

        for character in value do
            if character = '\\' then
                slashes <-
                    slashes
                    + 1
            else
                if character = '"' then
                    result
                        .Append(
                            '\\',
                            2
                            * slashes
                            + 1
                        )
                        .Append('"')
                    |> ignore
                else
                    result.Append('\\', slashes).Append(character)
                    |> ignore

                slashes <- 0

        result
            .Append(
                '\\',
                2
                * slashes
            )
            .Append('"')
            .ToString()

type private NativeMemory(length: int) =
    let pointer = Marshal.AllocHGlobal length
    member _.Pointer = pointer

    interface IDisposable with
        member _.Dispose() = Marshal.FreeHGlobal pointer

type private Attributes() =
    let mutable bytes = 0un
    let mutable initialized = false

    do
        ChildNative.InitializeProcThreadAttributeList(0n, 2, 0, &bytes)
        |> ignore

        if bytes = 0un then
            raise (JobNative.error "InitializeProcThreadAttributeList size")

    let memory = new NativeMemory(int bytes)

    do
        if ChildNative.InitializeProcThreadAttributeList(memory.Pointer, 2, 0, &bytes) then
            initialized <- true
        else
            let error = JobNative.error "InitializeProcThreadAttributeList"
            (memory :> IDisposable).Dispose()
            raise error

    member _.Pointer = memory.Pointer

    member _.Set(attribute, value, length) =
        if
            not (
                ChildNative.UpdateProcThreadAttribute(
                    memory.Pointer,
                    0u,
                    attribute,
                    value,
                    length,
                    0n,
                    0n
                )
            )
        then
            raise (JobNative.error "UpdateProcThreadAttribute")

    interface IDisposable with
        member _.Dispose() =
            if initialized then
                ChildNative.DeleteProcThreadAttributeList memory.Pointer
                initialized <- false

            (memory :> IDisposable).Dispose()

module private ChildOwnership =
    open System.Threading.Tasks

    // Completion messages/job-handle signaling are not used as a general closure oracle.
    // This is an owned settlement operation, not a detached timeout/replacement worker.
    let settle (job: OwnedJob) = task {
        let mutable firstError: exn option = None
        let mutable empty = false

        try
            empty <- job.ActiveProcesses = 0u
        with error ->
            firstError <- Some error

        if not empty then
            match job.RequestTermination() with
            | Some error when firstError.IsNone -> firstError <- Some error
            | _ -> ()

        while not empty do
            try
                empty <- job.ActiveProcesses = 0u
            with error ->
                if firstError.IsNone then
                    firstError <- Some error

            if not empty then
                do! Task.Delay 20

        return firstError
    }

    // Keep the constrained generic operation outside resumable task code.
    let keep (resources: ResizeArray<IDisposable>) (value: #IDisposable) =
        resources.Add(value :> IDisposable)
        value

    let disposeAll (resources: ResizeArray<IDisposable>) =
        let errors = ResizeArray<exn>()

        for index = resources.Count
                    - 1 downto 0 do
            try
                resources[index].Dispose()
            with error ->
                errors.Add error

        resources.Clear()

        if errors.Count > 0 then
            raise (AggregateException("Launch resource disposal failed.", errors))

/// A creation-time job-owned Windows child. Its actual exit, job closure and streams
/// remain owned until asynchronous disposal completes. No job handle reaches the child.
[<Sealed; SupportedOSPlatform("windows")>]
type internal WindowsChild
    private
    (
        job: OwnedJob,
        resources: ResizeArray<IDisposable>,
        root: JobHandle,
        input: System.IO.Stream option,
        output: System.IO.Stream,
        errors: System.IO.Stream,
        pid: int
    ) =
    let gate = obj ()
    let mutable closing: System.Threading.Tasks.Task option = None
    let mutable terminationError: exn option = None
    // The waiter owns an additional handle reference before it is scheduled.
    let mutable referenceAdded = false
    do root.DangerousAddRef(&referenceAdded)

    let scheduleWaiter () =
        System.Threading.Tasks.Task.Factory.StartNew(
            Func<int>(fun () ->
                try
                    let raw = root.DangerousGetHandle()

                    if
                        ChildNative.WaitForSingleObject(raw, UInt32.MaxValue)
                        <> 0u
                    then
                        raise (JobNative.error "WaitForSingleObject process")

                    let mutable code = 0u

                    if not (ChildNative.GetExitCodeProcess(raw, &code)) then
                        raise (JobNative.error "GetExitCodeProcess")

                    int code
                finally
                    if referenceAdded then
                        root.DangerousRelease()
            ),
            System.Threading.CancellationToken.None,
            System.Threading.Tasks.TaskCreationOptions.LongRunning,
            System.Threading.Tasks.TaskScheduler.Default
        )

    let exitTask =
        try
            scheduleWaiter ()
        with error ->
            if referenceAdded then
                root.DangerousRelease()

            raise error

    member _.Id = pid
    member _.StandardInput = input
    member _.StandardOutput = output
    member _.StandardError = errors
    member _.WaitForExitAsync() = exitTask

    member _.RequestTermination() =
        match job.RequestTermination() with
        | Some error ->
            System.Threading.Interlocked.CompareExchange(&terminationError, Some error, None)
            |> ignore
        | None -> ()

    member _.SettleAsync() =
        lock
            gate
            (fun () ->
                match closing with
                | Some existing -> existing
                | None ->
                    let operation =
                        System.Threading.Tasks.Task.Run(
                            Func<System.Threading.Tasks.Task>(fun () -> task {
                                let! failure = ChildOwnership.settle job
                                let! _ = exitTask

                                match failure with
                                | Some error -> return raise error
                                | None ->
                                    match terminationError with
                                    | None -> ()
                                    | Some error -> return raise error
                            })
                        )

                    closing <- Some operation
                    operation
            )

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            System.Threading.Tasks.ValueTask(
                task {
                    // Even an error is reported only after job membership reaches zero.
                    let! failure = task {
                        try
                            do! this.SettleAsync()
                            return None
                        with error ->
                            return Some error
                    }

                    ChildOwnership.disposeAll resources

                    match failure with
                    | Some error -> return raise error
                    | None -> ()
                }
            )

    static member StartAsync
        (
            start: System.Diagnostics.ProcessStartInfo,
            ct: System.Threading.CancellationToken
        ) =
        task {
            if not (OperatingSystem.IsWindows()) then
                raise (
                    PlatformNotSupportedException(
                        "This local candidate launcher requires Windows job ownership."
                    )
                )

            if
                start.UseShellExecute
                || not start.RedirectStandardOutput
                || not start.RedirectStandardError
            then
                invalidArg "start" "A shell-free captured launch is required."

            if not (System.IO.Path.IsPathFullyQualified start.FileName) then
                invalidArg "start" "An absolute executable is required."

            if start.Arguments.Length > 0 then
                invalidArg "start" "Use an argument vector."

            let resources = ResizeArray<IDisposable>()

            let mutable ownedJob: OwnedJob option = None

            try
                ct.ThrowIfCancellationRequested()
                let job = ChildOwnership.keep resources (OwnedJob.Create())
                ownedJob <- Some job

                let output =
                    ChildOwnership.keep resources (
                        new System.IO.Pipes.AnonymousPipeServerStream(
                            System.IO.Pipes.PipeDirection.In,
                            System.IO.HandleInheritability.Inheritable
                        )
                    )

                let errors =
                    ChildOwnership.keep resources (
                        new System.IO.Pipes.AnonymousPipeServerStream(
                            System.IO.Pipes.PipeDirection.In,
                            System.IO.HandleInheritability.Inheritable
                        )
                    )

                let input =
                    if start.RedirectStandardInput then
                        Some(
                            ChildOwnership.keep resources (
                                new System.IO.Pipes.AnonymousPipeServerStream(
                                    System.IO.Pipes.PipeDirection.Out,
                                    System.IO.HandleInheritability.Inheritable
                                )
                            )
                        )
                    else
                        None

                let inheritedInput =
                    if start.RedirectStandardInput then
                        None
                    else
                        let source = ChildNative.GetStdHandle(-10)

                        if
                            source = 0n
                            || source = -1n
                        then
                            let file = ChildOwnership.keep resources (System.IO.File.OpenRead("NUL"))
                            let mutable duplicate = 0n

                            if
                                not (
                                    ChildNative.DuplicateHandle(
                                        -1n,
                                        file.SafeFileHandle.DangerousGetHandle(),
                                        -1n,
                                        &duplicate,
                                        0u,
                                        true,
                                        2u
                                    )
                                )
                            then
                                raise (JobNative.error "DuplicateHandle input")

                            Some(ChildOwnership.keep resources (new JobHandle(duplicate)))
                        else
                            let mutable duplicate = 0n

                            if
                                not (
                                    ChildNative.DuplicateHandle(
                                        -1n,
                                        source,
                                        -1n,
                                        &duplicate,
                                        0u,
                                        true,
                                        2u
                                    )
                                )
                            then
                                raise (JobNative.error "DuplicateHandle input")

                            Some(ChildOwnership.keep resources (new JobHandle(duplicate)))

                let inputHandle =
                    match input, inheritedInput with
                    | Some pipe, _ -> pipe.ClientSafePipeHandle.DangerousGetHandle()
                    | None, Some handle -> handle.DangerousGetHandle()
                    | _ -> invalidOp "No owned standard input."

                use attributes = new Attributes()

                use standardHandles =
                    new NativeMemory(
                        3
                        * IntPtr.Size
                    )

                for index, handle in
                    [
                        0, inputHandle
                        1, output.ClientSafePipeHandle.DangerousGetHandle()
                        2, errors.ClientSafePipeHandle.DangerousGetHandle()
                    ] do
                    Marshal.WriteIntPtr(
                        standardHandles.Pointer,
                        index
                        * IntPtr.Size,
                        handle
                    )

                attributes.Set(
                    0x20002n,
                    standardHandles.Pointer,
                    unativeint (
                        3
                        * IntPtr.Size
                    )
                )

                use jobs = new NativeMemory(IntPtr.Size)
                Marshal.WriteIntPtr(jobs.Pointer, job.Handle)
                attributes.Set(0x2000Dn, jobs.Pointer, unativeint IntPtr.Size)

                let environment =
                    start.Environment
                    |> Seq.choose (fun entry ->
                        entry.Value
                        |> Option.ofObj
                        |> Option.map (fun value -> entry.Key, value)
                    )
                    |> Seq.sortWith (fun (left, _) (right, _) ->
                        StringComparer.OrdinalIgnoreCase.Compare(left, right)
                    )
                    |> Seq.map (fun (name, value) ->
                        name
                        + "="
                        + value
                    )
                    |> String.concat "\000"
                    |> fun block ->
                        System.Text.Encoding.Unicode.GetBytes(
                            block
                            + "\000\000"
                        )

                use environmentMemory = new NativeMemory(environment.Length)
                Marshal.Copy(environment, 0, environmentMemory.Pointer, environment.Length)

                let command =
                    seq {
                        yield start.FileName
                        yield! start.ArgumentList
                    }
                    |> Seq.map ChildNative.quote
                    |> String.concat " "
                    |> System.Text.StringBuilder

                let mutable startup = Unchecked.defaultof<ChildNative.StartupInfoEx>
                let mutable basic = Unchecked.defaultof<ChildNative.StartupInfo>
                basic.Size <- Marshal.SizeOf<ChildNative.StartupInfoEx>()
                basic.Flags <- 0x100
                basic.Input <- inputHandle
                basic.Output <- output.ClientSafePipeHandle.DangerousGetHandle()
                basic.Error <- errors.ClientSafePipeHandle.DangerousGetHandle()
                startup.Startup <- basic
                startup.Attributes <- attributes.Pointer
                let mutable result = Unchecked.defaultof<ChildNative.ProcessInfo>
                ct.ThrowIfCancellationRequested()

                if
                    not (
                        ChildNative.CreateProcessW(
                            start.FileName,
                            command,
                            0n,
                            0n,
                            true,
                            0x08080400u,
                            environmentMemory.Pointer,
                            start.WorkingDirectory,
                            &startup,
                            &result
                        )
                    )
                then
                    raise (JobNative.error "CreateProcessW")

                let root = ChildOwnership.keep resources (new JobHandle(result.Process))
                use thread = new JobHandle(result.Thread)
                output.DisposeLocalCopyOfClientHandle()
                errors.DisposeLocalCopyOfClientHandle()

                input
                |> Option.iter (fun pipe -> pipe.DisposeLocalCopyOfClientHandle())

                inheritedInput
                |> Option.iter (fun handle -> handle.Dispose())

                ct.ThrowIfCancellationRequested()

                return
                    new WindowsChild(
                        job,
                        resources,
                        root,
                        input
                        |> Option.map (fun pipe -> pipe :> System.IO.Stream),
                        output,
                        errors,
                        result.Pid
                    )
            with primary ->
                match ownedJob with
                | Some job ->
                    let! failure = ChildOwnership.settle job

                    failure
                    |> Option.iter (fun error ->
                        primary.Data["Morphir.Launch.CleanupFailure"] <- error
                    )
                | None -> ()

                try
                    ChildOwnership.disposeAll resources
                with cleanupError ->
                    primary.Data["Morphir.Launch.DisposalFailure"] <- cleanupError

                return raise primary
        }
