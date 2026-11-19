// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Compilation.Mep

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Runtime.ExceptionServices
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Mep
open Morphir.Toolchain.IO

// Absolute installed executables on Windows, Darwin arm64 and Linux glibc x64/arm64 have creation-time ownership.
type private GuestChild =
    | OwnedWindows of WindowsChild * Stream * StreamReader
    | OwnedLinux of LinuxChild
    | OwnedDarwin of DarwinChild
    | Legacy of Process

    static member LegacyCleanupBound = TimeSpan.FromSeconds 2.

    member this.Input =
        match this with
        | OwnedWindows(_, input, _) -> input
        | OwnedLinux child -> child.StandardInput.BaseStream
        | OwnedDarwin child -> child.StandardInput.BaseStream
        | Legacy child -> child.StandardInput.BaseStream

    member this.Output =
        match this with
        | OwnedWindows(child, _, _) -> child.StandardOutput
        | OwnedLinux child -> child.StandardOutput.BaseStream
        | OwnedDarwin child -> child.StandardOutput.BaseStream
        | Legacy child -> child.StandardOutput.BaseStream

    member this.StandardError =
        match this with
        | OwnedWindows(_, _, errors) -> errors
        | OwnedLinux child -> child.StandardError
        | OwnedDarwin child -> child.StandardError
        | Legacy child -> child.StandardError

    member this.CloseInput() =
        match this with
        | Legacy child -> child.StandardInput.Close()
        | _ -> this.Input.Dispose()

    member this.HasExited =
        match this with
        | OwnedWindows(child, _, _) -> child.WaitForExitAsync().IsCompleted
        | OwnedLinux child -> child.HasExited
        | OwnedDarwin child -> child.HasExited
        | Legacy child -> child.HasExited

    member this.WaitForExitAsync(ct: CancellationToken) : Task =
        match this with
        | OwnedWindows(child, _, _) -> child.WaitForExitAsync().WaitAsync(ct) :> Task
        | OwnedLinux child -> child.WaitForExitAsync(ct)
        | OwnedDarwin child -> child.WaitForExitAsync(ct)
        | Legacy child -> child.WaitForExitAsync(ct)

    member this.GracefulDeadline(configured: TimeSpan) =
        match this with
        | OwnedLinux child when configured = Timeout.InfiniteTimeSpan -> child.SettlementBound
        | OwnedDarwin child when configured = Timeout.InfiniteTimeSpan -> child.SettlementBound
        | _ -> configured

    member this.SettleAsync(terminate: bool, ?legacyBound: TimeSpan) = task {
        match this with
        | OwnedWindows(child, _, _) ->
            if terminate then child.RequestTermination()
            do! child.SettleAsync()
        | OwnedLinux child -> do! child.SettleAsync()
        | OwnedDarwin child -> do! child.SettleAsync()
        | Legacy child when terminate && not child.HasExited -> child.Kill(true)
        | Legacy _ -> ()
        let exited = this.WaitForExitAsync(CancellationToken.None)
        match this, legacyBound with
        | Legacy _, Some bound ->
            try do! exited.WaitAsync(bound)
            with :? TimeoutException as error ->
                return raise (IOException("The legacy guest did not exit within its cleanup bound.", error))
        | _ -> do! exited
    }

    member this.DrainAsync(operations: Task) : Task =
        match this with
        | OwnedDarwin child -> task {
            try do! operations.WaitAsync(child.SettlementBound)
            with :? TimeoutException as error when not operations.IsCompleted ->
                child.ClosePipes()
                try do! operations.WaitAsync(child.SettlementBound) with _ -> ()
                return raise error
          }
        | OwnedLinux child -> task {
            try do! operations.WaitAsync(child.SettlementBound)
            with :? TimeoutException as error when not operations.IsCompleted ->
                child.ClosePipes()
                try do! operations.WaitAsync(child.SettlementBound) with _ -> ()
                return raise error
          }
        | _ -> operations

    member this.DrainModelAccessAsync(operations: Task, stopIo: CancellationTokenSource, deadline: CancellationToken) : Task =
        match this with
        | Legacy child -> task {
            // Process has no independent ownership of descendants once the root exits.
            // A surviving writer must not turn a bounded request into an endless EOF wait.
            let grace = GuestChild.LegacyCleanupBound
            try
                do! operations.WaitAsync(grace, deadline)
            with
            | :? TimeoutException
            | :? OperationCanceledException ->
                let cancelled = stopIo.CancelAsync()
                // Close base streams directly. The stdin writer must not flush to a stopped peer.
                let close (stream: Stream) =
                    try stream.Dispose() with :? IOException -> ()
                close child.StandardInput.BaseStream
                close child.StandardOutput.BaseStream
                close child.StandardError.BaseStream
                try do! Task.WhenAll(operations, cancelled).WaitAsync(grace) with _ -> ()
                return raise (IOException "The legacy guest's redirected streams did not settle before the deadline.")
          }
        | _ -> this.DrainAsync(operations)

    member this.DrainAfterShutdownAsync(pump: Task, connection: Task) : Task =
        match this with
        | OwnedLinux _ -> this.DrainAsync(Task.WhenAll(pump, connection))
        | OwnedDarwin _ -> this.DrainAsync(Task.WhenAll(pump, connection))
        | _ -> pump

    member this.DisposeAsync() = task {
        match this with
        | OwnedWindows(child,_,errors) ->
            do! AsyncOwnership.runAsync (child :> IAsyncDisposable) (fun () -> task {
                errors.Dispose()
            })
        | OwnedLinux child -> do! (child :> IAsyncDisposable).DisposeAsync()
        | OwnedDarwin child -> do! (child :> IAsyncDisposable).DisposeAsync()
        | Legacy child -> child.Dispose()
    }
    interface IAsyncDisposable with
        member this.DisposeAsync() = ValueTask(this.DisposeAsync())
    static member StartAsync(start: ProcessStartInfo, deadline: TimeSpan, ct: CancellationToken) = task {
        if OperatingSystem.IsWindows() && Path.IsPathFullyQualified start.FileName then
            let! child = WindowsChild.StartAsync(start,ct)
            try
                // MEP writes frames directly. A StreamWriter would flush the dead pipe during forced disposal.
                let input = child.StandardInput |> Option.defaultWith(fun () -> invalidOp "Missing owned guest stdin")
                let errors = new StreamReader(child.StandardError,Text.UTF8Encoding(false),true,4096,true)
                return OwnedWindows(child,input,errors)
            with error ->
                do! (child :> IAsyncDisposable).DisposeAsync().AsTask()
                return raise error
        elif LinuxChild.IsSupported && Path.IsPathFullyQualified start.FileName then
            return OwnedLinux(LinuxChild.Start(start, deadline, ct))
        elif DarwinChild.IsSupported && Path.IsPathFullyQualified start.FileName then
            return OwnedDarwin(DarwinChild.Start(start, deadline, ct))
        else
            // Relative commands and other native architectures retain the explicit legacy path.
            // A supported native launch that fails its prerequisites never falls back here.
            match Process.Start(start) with
            | null -> return raise (IOException "Guest did not start")
            | child -> return Legacy child
    }

/// <summary>How a host starts a MEP guest process: a frontend, a backend or another extension.</summary>
type MepGuestOptions =
    {
        /// <summary>The guest executable. It starts with explicit arguments, no shell and no window.</summary>
        Executable: string
        /// <summary>The arguments, one item for each argument.</summary>
        Arguments: IReadOnlyList<string>
        /// <summary>The working directory of the guest, or null for the current directory.</summary>
        WorkingDirectory: string | null
        /// <summary>The deadline for initialize, for each request, and for the end of the session.</summary>
        Deadline: TimeSpan
        /// <summary>Where the guest's stderr goes, line by line.</summary>
        Stderr: TextWriter
        /// <summary>The host identity that the session sends.</summary>
        Host: PeerInfo
        /// <summary>
        /// The whole environment of the guest, or null for the host's environment. When it is given, the guest gets
        /// exactly these variables and inherits none.
        /// </summary>
        Environment: IReadOnlyDictionary<string, string> | null
    }

    /// <summary>For C# callers: the options, with the host kind cli and the given host name and version.</summary>
    static member Create
        (
            executable: string,
            arguments: IEnumerable<string>,
            workingDirectory: string | null,
            deadline: TimeSpan,
            stderr: TextWriter,
            hostName: string,
            hostVersion: string
        ) : MepGuestOptions =
        { Executable = executable
          Arguments = Array.ofSeq arguments
          WorkingDirectory = workingDirectory
          Deadline = deadline
          Stderr = stderr
          Host =
            { Kind = PeerKind.Cli
              Name = hostName
              Version = hostVersion }
          Environment = null }

    /// <summary>
    /// For C# callers: the options, with the host kind cli, the given host name and version, and the guest's whole
    /// environment (null for the host's).
    /// </summary>
    static member Create
        (
            executable: string,
            arguments: IEnumerable<string>,
            workingDirectory: string | null,
            deadline: TimeSpan,
            stderr: TextWriter,
            hostName: string,
            hostVersion: string,
            environment: IReadOnlyDictionary<string, string> | null
        ) : MepGuestOptions =
        { MepGuestOptions.Create(executable, arguments, workingDirectory, deadline, stderr, hostName, hostVersion) with
            Environment = environment }

    /// <summary>The executable.</summary>
    override this.ToString() = $"Extension {this.Executable}"

/// <summary>Why a guest session failed. Each message is a sentence that names the extension.</summary>
[<RequireQualifiedAccess>]
type MepGuestFailure =
    /// <summary>The guest did not start, or did not initialize.</summary>
    | Unavailable of message: string
    /// <summary>The guest broke the protocol, or the connection ended.</summary>
    | ProtocolError of message: string
    /// <summary>The guest did not answer before the deadline.</summary>
    | Timeout of message: string

    /// <summary>The message.</summary>
    member this.Message =
        match this with
        | Unavailable message
        | ProtocolError message
        | Timeout message -> message

    /// <summary>The diagnostic code: extension.unavailable, extension.protocol_error or extension.timeout.</summary>
    member this.Code =
        match this with
        | Unavailable _ -> "extension.unavailable"
        | ProtocolError _ -> "extension.protocol_error"
        | Timeout _ -> "extension.timeout"

    /// <summary>For C# callers: one function for each case.</summary>
    member this.Match<'R>(unavailable: Func<string, 'R>, protocolError: Func<string, 'R>, timeout: Func<string, 'R>) : 'R =
        match this with
        | Unavailable message -> unavailable.Invoke message
        | ProtocolError message -> protocolError.Invoke message
        | Timeout message -> timeout.Invoke message

    /// <summary>The code and the message.</summary>
    override this.ToString() = $"{this.Code}: {this.Message}"

/// <summary>The answer of a guest to one request.</summary>
[<RequireQualifiedAccess>]
type MepGuestReply =
    /// <summary>The guest answered with a result.</summary>
    | Result of result: JsonElement
    /// <summary>The guest answered with a JSON-RPC error.</summary>
    | Error of error: RpcError
    /// <summary>No answer: the connection ended or the deadline passed.</summary>
    | Failed of failure: MepGuestFailure

    /// <summary>For C# callers: one function for each case.</summary>
    member this.Match<'R>(result: Func<JsonElement, 'R>, error: Func<RpcError, 'R>, failed: Func<MepGuestFailure, 'R>) : 'R =
        match this with
        | Result value -> result.Invoke value
        | Error value -> error.Invoke value
        | Failed value -> failed.Invoke value

    /// <summary>Stable text for diagnostics.</summary>
    override this.ToString() =
        match this with
        | Result _ -> "Result"
        | Error error -> error.ToString()
        | Failed failure -> failure.ToString()

/// <summary>An initialized guest session whose owner settles requests and closes its transport.</summary>
type IMepGuestSession =
    inherit IAsyncDisposable
    /// <summary>The negotiated identity and capabilities.</summary>
    abstract Session: NegotiatedSession
    /// <summary>Whether the owned session has ended. This is not a process exit receipt.</summary>
    abstract IsClosed: bool
    /// <summary>Sends a typed request over the existing session.</summary>
    abstract SendAsync: Method * JsonElement * CancellationToken -> Task<MepGuestReply>
    /// <summary>Reads the guest's claims over the existing session.</summary>
    abstract DescribeAsync: CancellationToken -> Task<Result<CapabilityClaimSet, MepGuestFailure>>

/// The common model-access operations of an owned process or byte-call session.
type internal IModelAccessGuest =
    inherit IMepGuestSession
    abstract HostSession: HostSession
    abstract Transport: string
    abstract TerminateModelAccessAsync: unit -> Task
    abstract CloseModelAccessAsync: CancellationToken -> Task<Result<unit, MepGuestFailure>>

/// What ended a wait for a reply: the reply, the end of the connection, or the deadline.
[<RequireQualifiedAccess>]
type internal Waited =
    | Reply
    | ConnectionEnded
    | DeadlinePassed

/// <summary>
/// A MEP 0.2.0-draft.1 guest process with an initialized host session. <c>StartAsync</c> starts the process with
/// no shell and no window, passes its stderr on line by line, and initializes the session within the deadline.
/// <c>SendAsync</c> sends one request and waits for its reply, the deadline or the end of the connection. Dispose
/// it to end the session with shutdown and exit, or to stop the whole process tree after the deadline.
/// </summary>
[<Sealed>]
type MepGuestProcess
    private
    (
        options: MepGuestOptions,
        child: GuestChild,
        session: HostSession,
        connection: Task<ConnectionEnd>,
        negotiated: NegotiatedSession,
        pump: Task,
        stopIo: CancellationTokenSource,
        bounded: bool
    ) =

    let name = negotiated.Extension.Id
    let mutable ended = false
    let mutable exited = false

    let observe (operation: Task) =
        task {
            try do! operation with _ -> ()
        }

    /// Waits for a request to complete. A cancelled request token alone does not complete a request: the host
    /// sends $/cancelRequest, and a guest that claims no cancellation ignores it and may never answer. So the
    /// deadline is a wait of its own.
    static let wait (request: Task) (connection: Task<ConnectionEnd>) (deadline: CancellationToken) =
        task {
            let expired = Task.Delay(Timeout.Infinite, deadline)
            let! first = Task.WhenAny(request, connection :> Task, expired)

            if request.IsCompleted then return Waited.Reply
            elif Object.ReferenceEquals(first, connection) then return Waited.ConnectionEnded
            else return Waited.DeadlinePassed
        }

    /// <summary>The agreed session: the guest's identity, protocol version and capabilities.</summary>
    member _.Session = negotiated

    /// <summary>The extension id that the guest gave.</summary>
    member _.Name = name

    /// <summary>The options that started the guest.</summary>
    member _.Options = options

    /// <summary>True when the guest process has exited.</summary>
    member _.HasExited = exited || child.HasExited

    /// The initialized host session of an owned bounded-generation process.
    member internal _.HostSession = session

    static member private StartCoreAsync
        (options: MepGuestOptions, openSession: Func<Stream, Stream, HostSession>, bounded: bool, cancellation: CancellationToken)
        : Task<Result<MepGuestProcess, MepGuestFailure>> =
        task {
            let start = ProcessStartInfo(options.Executable)

            for argument in options.Arguments do
                start.ArgumentList.Add argument

            match options.WorkingDirectory with
            | null -> ()
            | directory -> start.WorkingDirectory <- directory

            match options.Environment with
            | null -> ()
            | environment ->
                start.Environment.Clear()

                for KeyValue(key, value) in environment do
                    start.Environment[key] <- value

            start.UseShellExecute <- false
            start.CreateNoWindow <- true
            start.RedirectStandardInput <- true
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true

            let! started = task {
                try
                    let! child = GuestChild.StartAsync(start, options.Deadline, cancellation)
                    return Ok child
                with
                | :? OperationCanceledException as error -> return raise error
                | error -> return Error $"Extension {options.Executable} did not start: {error.Message}"
            }

            match started with
            | Error message -> return Error(MepGuestFailure.Unavailable message)
            | Ok child ->
                let stopIo = new CancellationTokenSource()
                let pump =
                    task {
                        let mutable reading = true

                        while reading do
                            match! child.StandardError.ReadLineAsync(stopIo.Token) with
                            | null -> reading <- false
                            | line -> options.Stderr.WriteLine line
                    }
                    :> Task

                // A start that fails stops the process tree, waits for the stderr pump, and disposes the child, so
                // no process, pipe or pump outlives the failure.
                let mutable startedConnection: Task<ConnectionEnd> option = None
                let stop () = task {
                    use _io = stopIo
                    do! AsyncOwnership.runAsync (child :> IAsyncDisposable) (fun () -> task {
                        let legacyBound = if bounded then Some GuestChild.LegacyCleanupBound else None
                        do! child.SettleAsync(true, ?legacyBound = legacyBound)
                        let draining = task {
                            match startedConnection with
                            | Some connection ->
                                try do! (connection :> Task) with _ -> ()
                            | None -> ()
                            try do! pump with _ -> ()
                        }
                        if bounded then
                            do! child.DrainModelAccessAsync(draining, stopIo, CancellationToken.None)
                        else
                            do! child.DrainAsync(draining)
                    })
                }
                use deadline = CancellationTokenSource.CreateLinkedTokenSource cancellation
                deadline.CancelAfter options.Deadline

                let timedOut () =
                    MepGuestFailure.Unavailable $"Extension {options.Executable} did not initialize within {options.Deadline}."

                let! initialized = task {
                    try
                        let session = openSession.Invoke(child.Output, child.Input)
                        let connection = session.RunAsync stopIo.Token
                        startedConnection <- Some connection
                        let initialize = session.InitializeAsync deadline.Token

                        match! wait (initialize :> Task) connection deadline.Token with
                        | Waited.ConnectionEnded ->
                            let! ended = connection
                            return Error(MepGuestFailure.ProtocolError $"Extension {options.Executable} ended the connection before initialize: {ended}")
                        | Waited.DeadlinePassed -> return Error(timedOut ())
                        | Waited.Reply ->
                            match! initialize with
                            | Ok negotiated ->
                                return Ok(new MepGuestProcess(options, child, session, connection, negotiated, pump, stopIo, bounded))
                            | Error refusal -> return Error(MepGuestFailure.Unavailable(refusal.ToString()))
                    with
                    | :? OperationCanceledException -> return Error(timedOut ())
                    | error ->
                        return Error(MepGuestFailure.ProtocolError $"Extension {options.Executable} could not initialize: {error.Message}")
                }

                match initialized with
                | Ok guest -> return Ok guest
                | Error failure ->
                    do! stop ()
                    cancellation.ThrowIfCancellationRequested()
                    return Error failure
        }

    static member internal StartUsingAsync
        (options: MepGuestOptions, openSession: Func<Stream, Stream, HostSession>, cancellation: CancellationToken) =
        MepGuestProcess.StartCoreAsync(options, openSession, false, cancellation)

    /// <summary>
    /// Starts the guest, initializes a session with the model-transport offer, and returns the process, or the
    /// failure: Unavailable when it does not start or initialize, ProtocolError when it ends the connection first.
    /// </summary>
    static member StartAsync(options: MepGuestOptions, offer: ModelTransportSupport, cancellation: CancellationToken) =
        MepGuestProcess.StartUsingAsync(options, (fun input output -> HostSession(input, output, options.Host, offer)), cancellation)

    /// Starts the same owned process with the exact bounded model-access offer.
    static member internal StartModelAccessAsync
        (options: MepGuestOptions, offer: ModelAccessSupport, cancellation: CancellationToken) =
        MepGuestProcess.StartCoreAsync(
            options,
            (fun input output -> HostSession.CreateWithModelAccess(input, output, options.Host, offer, null)),
            true,
            cancellation
        )

    /// <summary>
    /// Starts the guest and initializes a session, for C# callers: a passed result holds the process, a failed one
    /// the failure. Dispose the process to end the session.
    /// </summary>
    static member StartCheckedAsync
        (
            options: MepGuestOptions,
            offer: ModelTransportSupport,
            cancellation: CancellationToken
        ) : Task<MepGuestStart> =
        task {
            match! MepGuestProcess.StartAsync(options, offer, cancellation) with
            | Ok started -> return MepGuestStart.Started started
            | Error failure -> return MepGuestStart.Failed failure
        }

    /// <summary>
    /// Sends one request and waits for its reply, the deadline or the end of the connection. A reply after the
    /// connection ended is a ProtocolError; no reply before the deadline is a Timeout.
    /// </summary>
    member _.SendAsync(method: Method, parameters: JsonElement, cancellation: CancellationToken) : Task<MepGuestReply> =
        task {
            use deadline = CancellationTokenSource.CreateLinkedTokenSource cancellation
            deadline.CancelAfter options.Deadline
            let request = session.RequestAsync(method, Nullable parameters, deadline.Token)

            match! wait (request :> Task) connection deadline.Token with
            | Waited.ConnectionEnded ->
                return MepGuestReply.Failed(MepGuestFailure.ProtocolError $"Extension {name}: the connection ended: {connection.Result}")
            | Waited.DeadlinePassed ->
                cancellation.ThrowIfCancellationRequested()
                return MepGuestReply.Failed(MepGuestFailure.Timeout $"Extension {name} did not answer {method} within {options.Deadline}.")
            | Waited.Reply ->
                try
                    match! request with
                    | Ok result -> return MepGuestReply.Result result
                    | Error error -> return MepGuestReply.Error error
                with
                | :? OperationCanceledException when not cancellation.IsCancellationRequested ->
                    return MepGuestReply.Failed(MepGuestFailure.Timeout $"Extension {name} did not answer {method} within {options.Deadline}.")
                // A request that faults for another reason, for example a message larger than one frame, is a
                // protocol failure of this request. A user cancellation is not caught.
                | error when not (error :? OperationCanceledException) ->
                    return MepGuestReply.Failed(MepGuestFailure.ProtocolError $"Extension {name}: the {method} request could not be sent: {error.Message}")
        }

    /// <summary>
    /// Asks for the guest's claims (morphir.extension.describe) within the deadline: the claim set, or why there is
    /// none. C# callers use the Match extension of Morphir.Mep.
    /// </summary>
    member _.DescribeAsync(cancellation: CancellationToken) : Task<Result<CapabilityClaimSet, MepGuestFailure>> =
        task {
            use deadline = CancellationTokenSource.CreateLinkedTokenSource cancellation
            deadline.CancelAfter options.Deadline

            try
                let describe = session.DescribeAsync deadline.Token

                match! wait (describe :> Task) connection deadline.Token with
                | Waited.ConnectionEnded ->
                    return Error(MepGuestFailure.ProtocolError $"Extension {name}: the connection ended: {connection.Result}")
                | Waited.DeadlinePassed ->
                    cancellation.ThrowIfCancellationRequested()
                    return Error(MepGuestFailure.Timeout $"Extension {name} did not answer describe within {options.Deadline}.")
                | Waited.Reply ->
                    match! describe with
                    | Ok claims -> return Ok claims
                    | Error error -> return Error(MepGuestFailure.ProtocolError $"Extension {name}: describe failed with {error}")
            with :? OperationCanceledException when not cancellation.IsCancellationRequested ->
                return Error(MepGuestFailure.Timeout $"Extension {name} did not answer describe within {options.Deadline}.")
        }
    /// <summary>The extension and the executable.</summary>
    override _.ToString() = $"Extension {name} ({options.Executable})"

    member private _.FinishModelAccessAsync(force: bool, pendingClose: Task option, deadline: CancellationToken) : Task =
        task {
            if not ended then
                ended <- true
                use _io = stopIo
                do! AsyncOwnership.runAsync (child :> IAsyncDisposable) (fun () -> task {
                    let! settlementError = task {
                        try
                            do! child.SettleAsync(force, legacyBound = GuestChild.LegacyCleanupBound)
                            return None
                        with error ->
                            return Some(ExceptionDispatchInfo.Capture error)
                    }
                    let draining = task {
                        do! observe connection
                        do! observe pump
                        match pendingClose with
                        | Some operation -> do! observe operation
                        | None -> ()
                    }
                    let drain = child.DrainModelAccessAsync(draining, stopIo, deadline)
                    match settlementError with
                    | None -> do! drain
                    | Some error ->
                        do! observe drain
                        error.Throw()
                    exited <- true
                })
        }

    /// Stops the process and joins its streams after an unanswered or broken bounded exchange.
    member internal this.TerminateModelAccessAsync() : Task =
        this.FinishModelAccessAsync(true, None, CancellationToken.None)

    /// Uses the remaining negotiated grace for shutdown and exit, then settles the owned child before returning.
    member internal this.CloseModelAccessAsync(deadline: CancellationToken) : Task<Result<unit, MepGuestFailure>> =
        task {
            if ended then
                return Ok()
            else
                let mutable pendingClose: Task option = None
                let! closed = task {
                    try
                        deadline.ThrowIfCancellationRequested()
                        let shutdown = session.ShutdownAsync deadline
                        pendingClose <- Some shutdown
                        match! shutdown.WaitAsync deadline with
                        | Error error ->
                            return Error(MepGuestFailure.ProtocolError $"Extension {name} refused shutdown: {error.Message}")
                        | Ok _ ->
                            let exit = session.ExitAsync()
                            pendingClose <- Some exit
                            do! exit.WaitAsync deadline
                            child.CloseInput()
                            do! child.WaitForExitAsync(deadline)
                            deadline.ThrowIfCancellationRequested()
                            return Ok()
                    with
                    | :? OperationCanceledException when deadline.IsCancellationRequested ->
                        return Error(MepGuestFailure.Timeout $"Extension {name} did not exit within the negotiated deadline.")
                    | :? IOException as error ->
                        return Error(MepGuestFailure.ProtocolError $"Extension {name} could not close: {error.Message}")
                }
                do! this.FinishModelAccessAsync(Result.isError closed, pendingClose, deadline)
                return closed
        }

    interface IMepGuestSession with
        member this.Session = this.Session
        member this.IsClosed = this.HasExited
        member this.SendAsync(method, parameters, cancellation) = this.SendAsync(method, parameters, cancellation)
        member this.DescribeAsync(cancellation) = this.DescribeAsync(cancellation)

    interface IModelAccessGuest with
        member this.HostSession = this.HostSession
        member _.Transport = "mep"
        member this.TerminateModelAccessAsync() = this.TerminateModelAccessAsync()
        member this.CloseModelAccessAsync(cancellation) = this.CloseModelAccessAsync(cancellation)

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            ValueTask(task {
                if not ended then
                    ended <- true
                    use _io = stopIo
                    do! AsyncOwnership.runAsync (child :> IAsyncDisposable) (fun () -> task {
                        use deadline = new CancellationTokenSource(child.GracefulDeadline(options.Deadline))
                        let mutable terminate = false
                        try
                            if not child.HasExited && not connection.IsCompleted then
                                let shutdown = session.ShutdownAsync deadline.Token
                                let! answered = wait (shutdown :> Task) connection deadline.Token
                                if answered = Waited.Reply then
                                    let! _ = wait (session.ExitAsync()) connection deadline.Token
                                    ()
                            child.CloseInput()
                            do! child.WaitForExitAsync(deadline.Token)
                        with _ -> terminate <- true
                        // Settlement errors escape, after the IO owner has joined the actual job.
                        // Disposal still joins/owns every handle through AsyncOwnership.
                        if bounded then
                            do! child.SettleAsync(terminate, legacyBound = GuestChild.LegacyCleanupBound)
                            do! child.DrainModelAccessAsync(Task.WhenAll(pump, connection), stopIo, deadline.Token)
                        else
                            do! child.SettleAsync(terminate)
                            do! child.DrainAfterShutdownAsync(pump, connection)
                        exited <- true
                    })
            })
/// <summary>The outcome of starting a guest, for C# callers.</summary>
and [<RequireQualifiedAccess>] MepGuestStart =
    /// <summary>The guest runs and its session is initialized.</summary>
    | Started of guest: MepGuestProcess
    /// <summary>The guest did not start or initialize.</summary>
    | Failed of failure: MepGuestFailure

    /// <summary>One function for each case.</summary>
    member this.Match<'R>(started: Func<MepGuestProcess, 'R>, failed: Func<MepGuestFailure, 'R>) : 'R =
        match this with
        | Started guest -> started.Invoke guest
        | Failed failure -> failed.Invoke failure

    /// <summary>Stable text for diagnostics.</summary>
    override this.ToString() =
        match this with
        | Started guest -> $"Started {guest}"
        | Failed failure -> $"Failed: {failure}"
