// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Compilation.Mep

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Mep

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

    let mutable closed = false

    let close =
        lazy
            (task {
                closed <- true

                try
                    use stopping = new CancellationTokenSource(deadline)
                    let! _ = session.ShutdownAsync stopping.Token
                    do! session.ExitAsync()
                with _ ->
                    ()

                lifetime.Cancel()

                try
                    let! _ = running
                    ()
                with :? OperationCanceledException ->
                    ()

                owner.Dispose()
                lifetime.Dispose()
            })

    /// <summary>
    /// Initializes and takes ownership of the adapter, closing it on failure. The existing host session
    /// enforces lifecycle, negotiation and response correlation with ordinary product request IDs.
    /// </summary>
    static member StartCheckedAsync
        (
            channel: ISingleMessageChannel,
            host: PeerInfo,
            offer: ModelTransportSupport,
            deadline: TimeSpan,
            cancellation: CancellationToken
        ) : Task<MepChannelStart> =
        task {
            match channel with
            | :? IDisposable as owner ->
                let lifetime = new CancellationTokenSource()
                let session = HostSession(channel, host, offer)
                let running = session.RunAsync lifetime.Token

                let cleanup () = task {
                    lifetime.Cancel()

                    try
                        let! _ = running
                        ()
                    with :? OperationCanceledException ->
                        ()

                    owner.Dispose()
                    lifetime.Dispose()
                }

                try
                    use timeout = CancellationTokenSource.CreateLinkedTokenSource cancellation
                    timeout.CancelAfter deadline
                    let! initialized = session.InitializeAsync timeout.Token

                    match initialized with
                    | Ok negotiated ->
                        return
                            MepChannelStart.Started(
                                new MepChannelGuest(
                                    session,
                                    negotiated,
                                    owner,
                                    lifetime,
                                    running,
                                    deadline
                                )
                            )
                    | Error failure ->
                        do! cleanup ()

                        return
                            MepChannelStart.Failed(
                                MepGuestFailure.ProtocolError(failure.ToString())
                            )
                with
                | :? OperationCanceledException as error when cancellation.IsCancellationRequested ->
                    do! cleanup ()
                    return raise error
                | error ->
                    do! cleanup ()
                    cancellation.ThrowIfCancellationRequested()

                    return
                        MepChannelStart.Failed(
                            MepGuestFailure.ProtocolError
                                $"The guest could not initialize: {error.Message}"
                        )
            | _ ->
                return
                    MepChannelStart.Failed(
                        MepGuestFailure.Unavailable
                            "The single-message adapter must have an owned disposable lifetime."
                    )
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

                return
                    Result.mapError
                        (fun (error: RpcError) -> MepGuestFailure.ProtocolError(error.ToString()))
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

    interface IAsyncDisposable with
        member _.DisposeAsync() = ValueTask(close.Value)
