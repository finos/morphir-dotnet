// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using Morphir.Mep;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// The Extism adapter (task 3.1): the WASM implementation of <see cref="ISingleMessageChannel"/>, which a
/// <see cref="HostSession"/> or the worker (task 4.1) drives. One <see cref="SessionStore"/>, so one guest instance
/// and one kernel instance, for the life of the channel. Each request is one <c>handle</c> call: the message is the
/// Extism input, and the reply is the Extism output, or null when there is none. The channel does not parse the
/// reply; the host session checks it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Failures end the instance.</b> The contract's <c>handle</c> results decide: an Extism error set, a result that
/// is not 0, and no output for a request are failures, as is every failed call of the store (a bound, a trap or a
/// host function that stops the call). The channel then discards its instance and records the failure in
/// <see cref="Failure"/>. A call that the contract makes a failure throws <see cref="WasmChannelException"/>, except
/// no output for a request, which returns null so that the host session fails the session for the missing reply. A
/// cancelled call throws <see cref="OperationCanceledException"/> once the call has stopped. After a failure no
/// later call runs the guest: it throws the same failure.
/// </para>
/// <para>
/// <b>Frame limits.</b> A request frame over <see cref="SessionLimits.RequestFrameBytes"/> is refused before the guest
/// is called (<c>extension.request_limit</c>), and a response frame over <see cref="SessionLimits.ResponseFrameBytes"/>
/// is not passed on (<c>extension.incomplete_session</c>). Both count the whole frame as a stream carries it, the
/// <c>Content-Length</c> header included, as the contract's <c>hostSession.frameLimits.counted</c> says.
/// </para>
/// <para>
/// <b>Notifications.</b> A guest receives a notification only after its <c>morphir.initialize</c> result advertised
/// that it accepts them. The member that advertises it is not fixed in the pinned contract (design NTFY-1), so
/// <see cref="AdvertisesNotifications"/> reads nothing and every notification is withheld: it returns null without
/// a call. Task 3.2 reads the advertisement there; a guest that advertised then gets each notification as one
/// <c>handle</c> call through the same path as a request.
/// </para>
/// <para>
/// <b><c>morphir.exit</c></b> ends the instance: the store is disposed and no later call runs. A guest that
/// advertised nothing gets no call for it; a guest that advertised gets it as one call first.
/// </para>
/// <para>
/// <b>Where the call runs.</b> <c>handle</c> is synchronous and can run for seconds. Each call runs on a new thread
/// that the channel starts for it and that ends with the call, not on a thread-pool thread: a guest that spins holds
/// its thread, and pool threads held that way starve the pool, so other work, timers among them, waits for a thread
/// the pool will not add while every processor is busy. A dedicated thread per call costs one thread start per
/// message, which is small next to a call, and no thread exists between calls. Everything that follows the call
/// (reading the result, the limits, discarding the instance) runs on that thread too, and the returned task
/// completes from it, so the channel never waits for the pool. Calls on one channel never overlap: a call made while
/// another runs is refused with <see cref="InvalidOperationException"/>; the host session and the worker send one
/// message at a time.
/// </para>
/// </remarks>
public sealed class WasmChannel : ISingleMessageChannel, IDisposable
{
    readonly object gate = new();
    readonly SessionStore store;
    int handleCalls;
    bool calling;
    bool disposeAfterCall;
    bool storeDisposed;
    bool deliversNotifications;
    ChannelState state = ChannelState.Open;

    enum ChannelState
    {
        Open,
        Failed,
        Exited,
        Disposed,
    }

    WasmChannel(SessionStore store)
    {
        this.store = store;
        Module = store.Module;
        Limits = store.Limits;
    }

    /// <summary>
    /// Opens a channel on <paramref name="module"/> with <paramref name="limits"/>: a new store, with the kernel and
    /// the guest instantiated in it. No guest export runs. When the store cannot open, the failure is returned as a
    /// value: a guest whose initial memory is above the ceiling (the contract's <c>initial-above-ceiling</c> rule).
    /// <paramref name="workspaceRoot"/> is the absolute directory below which the session's private workspace is
    /// created, or null for the system's temporary directory (<see cref="SessionStore.Open"/>).
    /// </summary>
    public static WasmChannelOpening Open(ExtismModule module, SessionLimits limits, string? workspaceRoot = null) =>
        SessionStore.Open(module, limits, workspaceRoot).Match<WasmChannelOpening>(
            store => new WasmChannelOpening.Opened(new WasmChannel(store)),
            failure => new WasmChannelOpening.Failed(failure));

    /// <summary>The module this channel runs.</summary>
    public ExtismModule Module { get; }

    /// <summary>The bounds and frame limits of this channel.</summary>
    public SessionLimits Limits { get; }

    /// <summary>The guest's output: what it wrote to WASI stdout and stderr and logged, and the <c>http_request</c>
    /// calls the host refused, bounded (<see cref="SessionStore.GuestOutput"/>). It is never part of a reply. The
    /// worker (task 4.1) drains it after each call for its <c>output</c> events; it stays readable after the instance
    /// ends.</summary>
    public GuestOutputTail GuestOutput => store.GuestOutput;

    /// <summary>The <c>http_request</c> calls the host refused, with the method and URL each asked for
    /// (<see cref="SessionStore.RefusedHttpRequests"/>).</summary>
    public IReadOnlyList<RefusedHttpRequest> RefusedHttpRequests => store.RefusedHttpRequests;

    /// <summary>The WASI functions the guest was refused, with how many times each
    /// (<see cref="SessionStore.RefusedWasiCalls"/>).</summary>
    public IReadOnlyDictionary<string, long> RefusedWasiCalls => store.RefusedWasiCalls;

    /// <summary>The session's private workspace, or null when the guest has not asked for it
    /// (<see cref="SessionStore.WorkspaceDirectory"/>). It is removed when the instance ends, at <c>morphir.exit</c>,
    /// at a failure or when the channel is disposed.</summary>
    public string? WorkspaceDirectory => store.WorkspaceDirectory;

    /// <summary>How many <c>handle</c> calls the channel has made.</summary>
    public int HandleCalls => Volatile.Read(ref handleCalls);

    /// <summary>Whether the guest receives notifications: false until its <c>morphir.initialize</c> result advertises
    /// that it accepts them, which task 3.2 reads (design NTFY-1).</summary>
    public bool DeliversNotifications
    {
        get
        {
            lock (gate)
            {
                return deliversNotifications;
            }
        }
    }

    /// <summary>The failure that ended the instance, or null while it runs or after it ended at <c>morphir.exit</c>.</summary>
    public WasmChannelFailure? Failure { get; private set; }

    /// <summary>Whether the guest's instance has ended: at <c>morphir.exit</c>, at a failure or when the channel was
    /// disposed. An ended instance runs no more guest code.</summary>
    public bool InstanceEnded
    {
        get
        {
            lock (gate)
            {
                return state != ChannelState.Open;
            }
        }
    }

    /// <summary>Passes one request to the guest as one <c>handle</c> call and returns the Extism output, or null when
    /// the guest gave none (which ends the instance as a failure).</summary>
    /// <exception cref="WasmChannelException">The call failed, the request frame is over its limit, the response
    /// frame is over its limit, or an earlier call had failed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled; the call has stopped and the instance ended.</exception>
    /// <exception cref="ObjectDisposedException">The instance ended at <c>morphir.exit</c> or the channel was disposed.</exception>
    public Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Call(method, message, isRequest: true, cancellation);
    }

    /// <summary>Withholds the notification from a guest that has not advertised that it accepts notifications, and
    /// returns null without a call. <c>morphir.exit</c> then ends the instance.</summary>
    /// <exception cref="WasmChannelException">An earlier call had failed.</exception>
    /// <exception cref="ObjectDisposedException">The instance ended at <c>morphir.exit</c> or the channel was disposed.</exception>
    public Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(method);
        var exit = method == Method.Exit.WireValue;
        lock (gate)
        {
            if (Refusal(method) is { } refused)
            {
                return Task.FromException<byte[]?>(refused);
            }

            if (!deliversNotifications)
            {
                if (exit)
                {
                    End(ChannelState.Exited);
                }

                return Task.FromResult<byte[]?>(null);
            }
        }

        return Call(method, message, isRequest: false, cancellation);
    }

    /// <summary>
    /// Whether a <c>morphir.initialize</c> result advertises that the guest accepts notifications. The member is not
    /// fixed in the pinned contract (design NTFY-1, the contract's <c>notificationAdvertisement</c> is
    /// <c>provisional</c>), so no result advertises it: every published bundle advertises nothing, and the contract
    /// delivers nothing to such a guest. Task 3.2 reads the fixed member here.
    /// </summary>
    static bool AdvertisesNotifications(ReadOnlySpan<byte> initializeResult) => false;

    /// <summary>The exception for a message after the instance ended, or null while it runs. Holds the gate.</summary>
    Exception? Refusal(string method) => state switch
    {
        ChannelState.Open => null,
        ChannelState.Failed => new WasmChannelException(method, Failure!),
        ChannelState.Exited => new ObjectDisposedException(nameof(WasmChannel), $"The guest's instance ended at {Method.Exit.WireValue}; {method} was not passed to it."),
        _ => new ObjectDisposedException(nameof(WasmChannel), $"The channel is disposed; {method} was not passed to the guest."),
    };

    Task<byte[]?> Call(string method, ReadOnlyMemory<byte> message, bool isRequest, CancellationToken cancellation)
    {
        lock (gate)
        {
            if (Refusal(method) is { } refused)
            {
                return Task.FromException<byte[]?>(refused);
            }

            if (calling)
            {
                return Task.FromException<byte[]?>(new InvalidOperationException(
                    $"A call is inside the guest; {method} was not passed. Calls on one WASM channel never overlap."));
            }

            var frameBytes = FrameBytes(message.Length);
            if (frameBytes > Limits.RequestFrameBytes)
            {
                var failure = new WasmChannelFailure.RequestOverLimit(frameBytes, Limits.RequestFrameBytes, store.Memory);
                return Task.FromException<byte[]?>(Fail(method, failure));
            }

            calling = true;
        }

        var done = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunCall(method, message, isRequest, cancellation, done))
        {
            IsBackground = true,
            Name = "morphir-wasm-call",
        };
        thread.Start();
        return done.Task;
    }

    /// <summary>One call, on its own thread: the <c>handle</c> call, then what its result means. Completes
    /// <paramref name="done"/> last, after the instance has been discarded when the call ended it.</summary>
    void RunCall(string method, ReadOnlyMemory<byte> message, bool isRequest, CancellationToken cancellation, TaskCompletionSource<byte[]?> done)
    {
        try
        {
            Interlocked.Increment(ref handleCalls);
            var outcome = store.CallHandle(message.Span, cancellation);
            var exit = method == Method.Exit.WireValue;
            lock (gate)
            {
                calling = false;
                var (reply, error) = Interpret(method, outcome, isRequest, cancellation);
                if (error is null && exit)
                {
                    End(ChannelState.Exited);
                }

                if (disposeAfterCall)
                {
                    End(ChannelState.Disposed);
                }

                Complete(done, reply, error);
            }
        }
        catch (Exception unexpected)
        {
            lock (gate)
            {
                calling = false;
                End(ChannelState.Disposed);
            }

            done.TrySetException(unexpected);
        }
    }

    static void Complete(TaskCompletionSource<byte[]?> done, byte[]? reply, Exception? error)
    {
        switch (error)
        {
            case null:
                done.TrySetResult(reply);
                break;
            case OperationCanceledException cancelled:
                done.TrySetCanceled(cancelled.CancellationToken);
                break;
            default:
                done.TrySetException(error);
                break;
        }
    }

    /// <summary>What a call's outcome means, as the contract's <c>handle</c> results say. Holds the gate.</summary>
    (byte[]? Reply, Exception? Error) Interpret(string method, HandleOutcome outcome, bool isRequest, CancellationToken cancellation)
    {
        switch (outcome)
        {
            case HandleOutcome.Failed { Failure: { Kind: GuestFailureKind.Cancelled } cancelled }:
                Fail(method, new WasmChannelFailure.Guest(cancelled));
                return (null, new OperationCanceledException($"{method} was cancelled while the guest handled it; the call has stopped.", cancellation));
            case HandleOutcome.Failed failed:
                return (null, Fail(method, new WasmChannelFailure.Guest(failed.Failure)));
            case HandleOutcome.NotRun notRun:
                return (null, Fail(method, new WasmChannelFailure.Guest(notRun.EndedBy)));
            case HandleOutcome.Completed completed:
                return Completed(method, completed, isRequest);
            default:
                throw new InvalidOperationException($"The store gave an outcome this channel does not know: {outcome}.");
        }
    }

    (byte[]? Reply, Exception? Error) Completed(string method, HandleOutcome.Completed completed, bool isRequest)
    {
        var result = completed.Result;
        if (result.ErrorHandle != 0)
        {
            var text = Encoding.UTF8.GetString(result.Error);
            return (null, Fail(method, new WasmChannelFailure.ExtismError(text, result.ReturnCode, completed.Memory)));
        }

        if (result.ReturnCode != 0)
        {
            return (null, Fail(method, new WasmChannelFailure.NonZeroResult(result.ReturnCode, result.Output.Length, completed.Memory)));
        }

        if (result.Output.Length == 0)
        {
            if (isRequest)
            {
                // The host session fails the session for the missing reply; the instance ends here.
                Fail(method, new WasmChannelFailure.NoOutput(completed.Memory));
            }

            return (null, null);
        }

        var frameBytes = FrameBytes(result.Output.Length);
        if (frameBytes > Limits.ResponseFrameBytes)
        {
            return (null, Fail(method, new WasmChannelFailure.ResponseOverLimit(frameBytes, Limits.ResponseFrameBytes, completed.Memory)));
        }

        if (!isRequest)
        {
            // Output for a delivered notification: the host session fails the session for it.
            Fail(method, new WasmChannelFailure.AnsweredNotification(result.Output.Length, completed.Memory));
            return (result.Output, null);
        }

        if (method == Method.Initialize.WireValue)
        {
            deliversNotifications = AdvertisesNotifications(result.Output);
        }

        return (result.Output, null);
    }

    /// <summary>Records the failure, discards the instance and gives the exception to throw. Holds the gate.</summary>
    WasmChannelException Fail(string method, WasmChannelFailure failure)
    {
        Failure ??= failure;
        End(ChannelState.Failed);
        return new WasmChannelException(method, Failure);
    }

    /// <summary>Ends the instance once: the store is disposed, unless a call is inside it, in which case the call's
    /// thread disposes it when the call returns. Holds the gate.</summary>
    void End(ChannelState ended)
    {
        if (state == ChannelState.Open)
        {
            state = ended;
        }

        if (calling)
        {
            disposeAfterCall = true;
            return;
        }

        if (!storeDisposed)
        {
            storeDisposed = true;
            store.Dispose();
        }
    }

    /// <summary>The size of a message's frame on a stream: its <c>Content-Length</c> header and its body.</summary>
    static long FrameBytes(int bodyBytes) =>
        Encoding.ASCII.GetByteCount($"Content-Length: {bodyBytes}\r\n\r\n") + (long)bodyBytes;

    /// <summary>Ends the instance and disposes the store. A call that is running stops at its own deadline or
    /// cancellation; the store is disposed when it returns.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            End(ChannelState.Disposed);
        }
    }
}
