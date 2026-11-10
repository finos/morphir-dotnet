// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Compilation.Mep

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Mep

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
              Version = hostVersion } }

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
        child: Process,
        session: HostSession,
        connection: Task<ConnectionEnd>,
        negotiated: NegotiatedSession,
        pump: Task
    ) =

    let name = negotiated.Extension.Id
    let mutable ended = false
    let mutable exited = false

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

    /// <summary>
    /// Starts the guest, initializes a session with the model-transport offer, and returns the process, or the
    /// failure: Unavailable when it does not start or initialize, ProtocolError when it ends the connection first.
    /// </summary>
    static member StartAsync(options: MepGuestOptions, offer: ModelTransportSupport, cancellation: CancellationToken) : Task<Result<MepGuestProcess, MepGuestFailure>> =
        task {
            let start = ProcessStartInfo(options.Executable)

            for argument in options.Arguments do
                start.ArgumentList.Add argument

            match options.WorkingDirectory with
            | null -> ()
            | directory -> start.WorkingDirectory <- directory

            start.UseShellExecute <- false
            start.CreateNoWindow <- true
            start.RedirectStandardInput <- true
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true

            let started =
                try
                    match Process.Start start with
                    | null -> Error $"Extension {options.Executable} did not start."
                    | child -> Ok child
                with error ->
                    Error $"Extension {options.Executable} did not start: {error.Message}"

            match started with
            | Error message -> return Error(MepGuestFailure.Unavailable message)
            | Ok child ->
                let pump =
                    task {
                        let mutable reading = true

                        while reading do
                            match! child.StandardError.ReadLineAsync() with
                            | null -> reading <- false
                            | line -> options.Stderr.WriteLine line
                    }
                    :> Task

                let session =
                    HostSession(child.StandardOutput.BaseStream, child.StandardInput.BaseStream, options.Host, offer)

                let connection = session.RunAsync CancellationToken.None

                // A start that fails stops the process tree, waits for the stderr pump, and disposes the child, so
                // no process, pipe or pump outlives the failure.
                let stop () =
                    task {
                        try
                            if not child.HasExited then
                                child.Kill true

                            do! child.WaitForExitAsync()
                        with _ ->
                            ()

                        try
                            do! pump
                        with _ ->
                            ()

                        child.Dispose()
                    }

                use deadline = CancellationTokenSource.CreateLinkedTokenSource cancellation
                deadline.CancelAfter options.Deadline

                let initialize = session.InitializeAsync deadline.Token
                let! waited = wait (initialize :> Task) connection deadline.Token

                if waited = Waited.ConnectionEnded then
                    do! stop ()

                    return
                        Error(
                            MepGuestFailure.ProtocolError
                                $"Extension {options.Executable} ended the connection before initialize: {connection.Result}"
                        )
                elif waited = Waited.DeadlinePassed then
                    do! stop ()
                    cancellation.ThrowIfCancellationRequested()
                    return Error(MepGuestFailure.Unavailable $"Extension {options.Executable} did not initialize within {options.Deadline}.")
                else
                    let! initialized =
                        task {
                            try
                                let! result = initialize
                                return result |> Result.mapError _.ToString()
                            with :? OperationCanceledException ->
                                return Error $"Extension {options.Executable} did not initialize within {options.Deadline}."
                        }

                    match initialized with
                    | Error message ->
                        do! stop ()
                        return Error(MepGuestFailure.Unavailable message)
                    | Ok negotiated -> return Ok(new MepGuestProcess(options, child, session, connection, negotiated, pump))
        }

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

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            ValueTask(
                task {
                    if not ended then
                        ended <- true

                        try
                            use deadline = new CancellationTokenSource(options.Deadline)

                            // Neither shutdown nor exit completes when the guest does not read them, for example
                            // when a handler blocks its read loop: a cancelled token only sends $/cancelRequest. So
                            // both wait for the deadline too, and after it the process tree is stopped.
                            if not connection.IsCompleted then
                                let shutdown = session.ShutdownAsync deadline.Token
                                let! answered = wait (shutdown :> Task) connection deadline.Token

                                if answered = Waited.Reply then
                                    let! _ = wait (session.ExitAsync()) connection deadline.Token
                                    ()

                            // The end of its input also ends a guest whose connection already failed.
                            try
                                child.StandardInput.Close()
                            with _ ->
                                ()

                            let! stopped =
                                task {
                                    try
                                        do! child.WaitForExitAsync deadline.Token
                                        return true
                                    with :? OperationCanceledException ->
                                        return false
                                }

                            if not stopped then
                                child.Kill true
                                do! child.WaitForExitAsync()
                        with _ ->
                            try
                                if not child.HasExited then
                                    child.Kill true
                                    child.WaitForExit()
                            with _ ->
                                ()

                        do! pump
                        exited <- true
                        child.Dispose()
                }
            )

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
