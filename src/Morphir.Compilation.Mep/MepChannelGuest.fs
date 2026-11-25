// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Compilation.Mep

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Mep
open Morphir.Toolchain.IO

/// <summary>The result of connecting an owned single-message guest.</summary>
[<RequireQualifiedAccess>]
type MepChannelStart =
    | Started of session: IMepGuestSession
    | Failed of failure: MepGuestFailure

    /// <summary>One function for each outcome, for C# callers.</summary>
    member this.Match<'R>
        (
            started: Func<IMepGuestSession, 'R>,
            failed: Func<MepGuestFailure, 'R>
        ) : 'R =
        match this with
        | Started session -> started.Invoke session
        | Failed failure -> failed.Invoke failure

    override this.ToString() =
        match this with
        | Started session -> session.Session.Extension.Id
        | Failed failure -> failure.ToString()

/// <summary>
/// Owns an ordinary HostSession on a bounded, disposable single-message adapter. The adapter must settle
/// its I/O when cancelled. No process is launched and no model or wire validation is duplicated.
/// </summary>
[<Sealed>]
type MepChannelGuest
    private
    (
        session: HostSession,
        negotiated: NegotiatedSession,
        owner: IDisposable,
        lifetime: CancellationTokenSource,
        running: Task<ConnectionEnd>,
        deadline: TimeSpan
    ) =

    static let settle (owner: IDisposable) (lifetime: CancellationTokenSource) (running: Task<ConnectionEnd> option) =
        task {
            use lifetime = lifetime
            let adapter =
                { new IAsyncDisposable with
                    member _.DisposeAsync() =
                        match owner with
                        | :? IAsyncDisposable as asyncOwner -> asyncOwner.DisposeAsync()
                        | _ ->
                            owner.Dispose()
                            ValueTask() }

            do! AsyncOwnership.runAsync adapter (fun () -> task {
                lifetime.Cancel()
                match running with
                | Some operation ->
                    try
                        let! _ = operation
                        ()
                    with :? OperationCanceledException ->
                        ()
                | None -> ()
            })
        }

    let mutable closed = false

    let finish =
        lazy
            (task {
                closed <- true
                do! settle owner lifetime (Some running)
            })

    let close =
        lazy
            (task {
                if not finish.IsValueCreated then
                    closed <- true
                    try
                        use stopping = new CancellationTokenSource(deadline)
                        let! _ = session.ShutdownAsync stopping.Token
                        do! session.ExitAsync()
                    with _ ->
                        ()
                do! finish.Value
            })

    static member private StartAsync
        (channel: ISingleMessageChannel, openSession: unit -> HostSession, deadline: TimeSpan, cancellation: CancellationToken)
        : Task<Result<MepChannelGuest, MepGuestFailure>> =
        task {
            match channel with
            | :? IDisposable as owner ->
                let lifetime = new CancellationTokenSource()
                let mutable running: Task<ConnectionEnd> option = None
                let cleanup = lazy (settle owner lifetime running)

                try
                    let session = openSession ()
                    let run = session.RunAsync lifetime.Token
                    running <- Some run
                    use timeout = CancellationTokenSource.CreateLinkedTokenSource cancellation
                    timeout.CancelAfter deadline
                    let! initialized = session.InitializeAsync timeout.Token

                    match initialized with
                    | Ok negotiated ->
                        return
                            Ok(
                                new MepChannelGuest(
                                    session,
                                    negotiated,
                                    owner,
                                    lifetime,
                                    run,
                                    deadline
                                )
                            )
                    | Error failure ->
                        do! cleanup.Value

                        return
                            Error(
                                MepGuestFailure.ProtocolError(failure.ToString())
                            )
                with
                | :? OperationCanceledException as error when cancellation.IsCancellationRequested ->
                    do! cleanup.Value
                    return raise error
                | error ->
                    do! cleanup.Value
                    cancellation.ThrowIfCancellationRequested()

                    return
                        Error(
                            MepGuestFailure.ProtocolError
                                $"The guest could not initialize: {error.Message}"
                        )
            | _ ->
                return
                    Error(
                        MepGuestFailure.Unavailable
                            "The single-message adapter must have an owned disposable lifetime."
                    )
        }

    /// <summary>
    /// Initializes and takes ownership of the adapter, closing it on failure. The existing host session
    /// enforces lifecycle, negotiation and response correlation with ordinary product request IDs.
    /// </summary>
    static member StartCheckedAsync
        (channel: ISingleMessageChannel, host: PeerInfo, offer: ModelTransportSupport, deadline: TimeSpan,
         cancellation: CancellationToken) : Task<MepChannelStart> =
        task {
            match! MepChannelGuest.StartAsync(channel, (fun () -> HostSession(channel, host, offer)), deadline, cancellation) with
            | Ok guest -> return MepChannelStart.Started guest
            | Error failure -> return MepChannelStart.Failed failure
        }

    /// Shared refusal message when a byte-call guest does not agree to bounded model access.
    static member internal ModelAccessNotAgreedMessage = "The extension did not agree to bounded model access."

    /// Initializes the byte-call session with an exact model-access offer.
    static member internal StartModelAccessAsync
        (channel: ISingleMessageChannel, host: PeerInfo, offer: ModelAccessSupport, deadline: TimeSpan,
         cancellation: CancellationToken) : Task<Result<MepChannelGuest, MepGuestFailure>> =
        task {
            let! initialized = MepChannelGuest.StartAsync(
                channel, (fun () -> HostSession.CreateWithModelAccess(channel, host, offer, null)), deadline, cancellation)
            match initialized with
            | Ok guest when (guest :> IMepGuestSession).Session.Capabilities.ModelAccess.IsNone ->
                do! guest.TerminateModelAccessAsync()
                return Error(MepGuestFailure.Unavailable MepChannelGuest.ModelAccessNotAgreedMessage)
            | _ -> return initialized
        }

    member internal _.HostSession = session

    member internal _.TerminateModelAccessAsync() : Task = finish.Value

    member internal _.CloseModelAccessAsync(stopping: CancellationToken) : Task<Result<unit, MepGuestFailure>> =
        task {
            if finish.IsValueCreated then
                do! finish.Value
                return Ok()
            else
                let! result = task {
                    try
                        stopping.ThrowIfCancellationRequested()
                        let! shutdown = session.ShutdownAsync stopping
                        stopping.ThrowIfCancellationRequested()
                        match shutdown with
                        | Error error ->
                            return Error(MepGuestFailure.ProtocolError $"Extension {negotiated.Extension.Id} refused shutdown: {error.Message}")
                        | Ok _ ->
                            let! exit = session.SendExitAsync stopping
                            stopping.ThrowIfCancellationRequested()
                            return exit |> Result.mapError (fun error -> MepGuestFailure.ProtocolError error.Message)
                    with
                    | :? OperationCanceledException when stopping.IsCancellationRequested ->
                        return Error(MepGuestFailure.Timeout $"Extension {negotiated.Extension.Id} did not exit within the negotiated deadline.")
                    | error ->
                        return Error(MepGuestFailure.ProtocolError error.Message)
                }
                do! finish.Value
                return result
        }

    interface IMepGuestSession with
        member _.Session = negotiated

        member _.IsClosed =
            closed
            || running.IsCompleted

        member _.SendAsync(method, parameters, cancellation) = task {
            use timeout = CancellationTokenSource.CreateLinkedTokenSource cancellation
            timeout.CancelAfter deadline

            try
                let! reply = session.RequestAsync(method, Nullable parameters, timeout.Token)
                cancellation.ThrowIfCancellationRequested()

                if timeout.IsCancellationRequested then
                    return
                        MepGuestReply.Failed(
                            MepGuestFailure.Timeout
                                $"The guest did not answer {method} within {deadline}."
                        )
                else
                    return
                        match reply with
                        | Ok value -> MepGuestReply.Result value
                        | Error error -> MepGuestReply.Error error
            with
            | :? OperationCanceledException when not cancellation.IsCancellationRequested ->
                return
                    MepGuestReply.Failed(
                        MepGuestFailure.Timeout
                            $"The guest did not answer {method} within {deadline}."
                    )
            | error when not (error :? OperationCanceledException) ->
                return MepGuestReply.Failed(MepGuestFailure.ProtocolError error.Message)
        }

        member _.DescribeAsync(cancellation) = task {
            use timeout = CancellationTokenSource.CreateLinkedTokenSource cancellation
            timeout.CancelAfter deadline

            try
                let! reply = session.DescribeAsync timeout.Token
                cancellation.ThrowIfCancellationRequested()

                if timeout.IsCancellationRequested then
                    return
                        Error(
                            MepGuestFailure.Timeout
                                $"The guest did not answer describe within {deadline}."
                        )
                else
                    return
                        Result.mapError
                            (fun (error: RpcError) ->
                                MepGuestFailure.ProtocolError(error.ToString())
                            )
                            reply
            with :? OperationCanceledException when not cancellation.IsCancellationRequested ->
                return
                    Error(
                        MepGuestFailure.Timeout
                            $"The guest did not answer describe within {deadline}."
                    )
        }

    override _.ToString() =
        $"Extension {negotiated.Extension.Id} (single-message channel)"

    interface IModelAccessGuest with
        member this.HostSession = this.HostSession
        member _.Transport = "wasm"
        member this.TerminateModelAccessAsync() = this.TerminateModelAccessAsync()
        member this.CloseModelAccessAsync(cancellation) = this.CloseModelAccessAsync(cancellation)

    interface IAsyncDisposable with
        member _.DisposeAsync() = ValueTask(close.Value)
