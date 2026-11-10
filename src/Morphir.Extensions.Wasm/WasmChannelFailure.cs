// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// Why a WASM channel ended its guest's instance (task 3.1). Each case is a failure of the pinned contract: a
/// bound or a trap that the store reports, or a <c>handle</c> result or a frame that the Extism profile and the host
/// session make a failure. Every case has the contract's code or outcome and the memory sizes.
/// </summary>
public abstract record WasmChannelFailure
{
    private WasmChannelFailure() { }

    /// <summary>The contract's fault kind of every failure that is not a store failure: <c>failure</c>.</summary>
    const string FailureKind = "failure";

    /// <summary>The memory sizes when the instance ended.</summary>
    public abstract MemoryUse Memory { get; }

    /// <summary>The contract's code, such as <c>extension.incomplete_session</c>, or null for an outcome.</summary>
    public string? Code => this switch
    {
        Guest guest => guest.Failure.Code,
        RequestOverLimit => WasmProfileContract.Pinned.RequestLimitCode,
        ResponseOverLimit => WasmProfileContract.Pinned.ResponseLimitCode,
        _ => WasmProfileContract.Pinned.Fault(FailureKind).HostCode,
    };

    /// <summary>The host outcome, <c>deadline</c> or <c>cancelled</c>, or null for a failure with a code.</summary>
    public string? Outcome => this is Guest guest ? guest.Failure.Outcome : null;

    /// <summary>The fault kind a worker reports, such as <c>trap</c> or <c>failure</c>, or null for a cancelled call.</summary>
    public string? FaultKind => this is Guest guest ? guest.Failure.FaultKind : FailureKind;

    /// <summary>The diagnostic: the code or outcome, what happened, and the memory sizes.</summary>
    public string Message => this switch
    {
        Guest guest => guest.Failure.Message,
        _ => $"{Code}: {Describe()}; {Memory}",
    };

    string Describe() => this switch
    {
        ExtismError error => $"the guest set the Extism error \"{error.Text}\" (handle returned {error.ReturnCode})",
        NonZeroResult result => $"handle returned {result.ReturnCode} with {result.OutputBytes} bytes of output and no error set",
        NoOutput => "handle returned 0 with no output for a request",
        AnsweredNotification answered => $"the guest gave {answered.OutputBytes} bytes of output for a notification",
        RequestOverLimit over => $"the request frame has {Bytes(over.FrameBytes)} bytes, over the request limit of {Bytes(over.LimitBytes)} bytes; the guest was not called",
        ResponseOverLimit over => $"the response frame has {Bytes(over.FrameBytes)} bytes, over the response limit of {Bytes(over.LimitBytes)} bytes; no part of it is passed on",
        _ => throw new UnreachableException(),
    };

    static string Bytes(long count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>The diagnostic.</summary>
    public override string ToString() => Message;

    /// <summary>A bound, a trap or another failure that the session store reports (<see cref="GuestFailure"/>),
    /// cancellation among them.</summary>
    /// <param name="Failure">The store's failure.</param>
    public sealed record Guest(GuestFailure Failure) : WasmChannelFailure
    {
        /// <inheritdoc />
        public override MemoryUse Memory => Failure.Memory;
    }

    /// <summary><c>handle</c> returned with an Extism error set: a failure whatever it returned.</summary>
    /// <param name="Text">The error text the guest set, as UTF-8.</param>
    /// <param name="ReturnCode">What <c>handle</c> returned.</param>
    /// <param name="Memory">The memory sizes after the call.</param>
    public sealed record ExtismError(string Text, int ReturnCode, MemoryUse Memory) : WasmChannelFailure
    {
        /// <inheritdoc />
        public override MemoryUse Memory { get; } = Memory;
    }

    /// <summary><c>handle</c> returned a result that is not 0 with no error set: a failure, also when the guest gave output.</summary>
    /// <param name="ReturnCode">What <c>handle</c> returned.</param>
    /// <param name="OutputBytes">The bytes of output the kernel held, which are not passed on.</param>
    /// <param name="Memory">The memory sizes after the call.</param>
    public sealed record NonZeroResult(int ReturnCode, int OutputBytes, MemoryUse Memory) : WasmChannelFailure
    {
        /// <inheritdoc />
        public override MemoryUse Memory { get; } = Memory;
    }

    /// <summary><c>handle</c> returned 0 with no output for a request. The channel returns no reply, and the
    /// host session fails the session for it.</summary>
    /// <param name="Memory">The memory sizes after the call.</param>
    public sealed record NoOutput(MemoryUse Memory) : WasmChannelFailure
    {
        /// <inheritdoc />
        public override MemoryUse Memory { get; } = Memory;
    }

    /// <summary>The guest gave output for a delivered notification. The channel returns it, and the host session
    /// fails the session for it. No notification is delivered before task 3.2.</summary>
    /// <param name="OutputBytes">The bytes of output.</param>
    /// <param name="Memory">The memory sizes after the call.</param>
    public sealed record AnsweredNotification(int OutputBytes, MemoryUse Memory) : WasmChannelFailure
    {
        /// <inheritdoc />
        public override MemoryUse Memory { get; } = Memory;
    }

    /// <summary>A request frame over the request limit. The guest was not called.</summary>
    /// <param name="FrameBytes">The size of the whole frame, its <c>Content-Length</c> header included.</param>
    /// <param name="LimitBytes">The request limit.</param>
    /// <param name="Memory">The memory sizes when the request was refused.</param>
    public sealed record RequestOverLimit(long FrameBytes, long LimitBytes, MemoryUse Memory) : WasmChannelFailure
    {
        /// <inheritdoc />
        public override MemoryUse Memory { get; } = Memory;
    }

    /// <summary>A response frame over the response limit. No part of it is passed on.</summary>
    /// <param name="FrameBytes">The size of the whole frame, its <c>Content-Length</c> header included.</param>
    /// <param name="LimitBytes">The response limit.</param>
    /// <param name="Memory">The memory sizes after the call.</param>
    public sealed record ResponseOverLimit(long FrameBytes, long LimitBytes, MemoryUse Memory) : WasmChannelFailure
    {
        /// <inheritdoc />
        public override MemoryUse Memory { get; } = Memory;
    }

    /// <summary>Calls the function for this failure.</summary>
    public TResult Match<TResult>(
        Func<GuestFailure, TResult> guest,
        Func<ExtismError, TResult> extismError,
        Func<NonZeroResult, TResult> nonZeroResult,
        Func<NoOutput, TResult> noOutput,
        Func<AnsweredNotification, TResult> answeredNotification,
        Func<RequestOverLimit, TResult> requestOverLimit,
        Func<ResponseOverLimit, TResult> responseOverLimit) => this switch
        {
            Guest failure => guest(failure.Failure),
            ExtismError failure => extismError(failure),
            NonZeroResult failure => nonZeroResult(failure),
            NoOutput failure => noOutput(failure),
            AnsweredNotification failure => answeredNotification(failure),
            RequestOverLimit failure => requestOverLimit(failure),
            ResponseOverLimit failure => responseOverLimit(failure),
            _ => throw new UnreachableException(),
        };
}

/// <summary>
/// How a WASM channel signals a failed call to the host session (<c>ISingleMessageChannel</c> signals failure by
/// throwing). <c>SessionFailure.ChannelFailed</c> holds it, so a caller can report <see cref="Code"/> or
/// <see cref="Outcome"/>, the message and the memory sizes. A cancelled call throws
/// <see cref="OperationCanceledException"/> instead.
/// </summary>
public sealed class WasmChannelException : Exception
{
    /// <summary>A failure during the call of <paramref name="method"/>.</summary>
    public WasmChannelException(string method, WasmChannelFailure failure)
        : base(failure.Message)
    {
        Method = method;
        Failure = failure;
    }

    /// <summary>The method of the message whose call failed, or that was refused because the instance had failed.</summary>
    public string Method { get; }

    /// <summary>The failure that ended the instance.</summary>
    public WasmChannelFailure Failure { get; }

    /// <summary>The contract's code, or null for an outcome.</summary>
    public string? Code => Failure.Code;

    /// <summary>The host outcome, <c>deadline</c> or <c>cancelled</c>, or null.</summary>
    public string? Outcome => Failure.Outcome;

    /// <summary>The memory sizes when the instance ended.</summary>
    public MemoryUse Memory => Failure.Memory;
}

/// <summary>What opening a channel gave: a channel, or a failure before any request.</summary>
public abstract record WasmChannelOpening
{
    private WasmChannelOpening() { }

    /// <summary>The channel is open; no guest export has run.</summary>
    public sealed record Opened(WasmChannel Channel) : WasmChannelOpening;

    /// <summary>The store did not open: the contract's <c>initial-above-ceiling</c> memory rule.</summary>
    public sealed record Failed(GuestFailure Failure) : WasmChannelOpening;

    /// <summary>Calls the function for this result.</summary>
    public TResult Match<TResult>(Func<WasmChannel, TResult> opened, Func<GuestFailure, TResult> failed) => this switch
    {
        Opened result => opened(result.Channel),
        Failed result => failed(result.Failure),
        _ => throw new UnreachableException(),
    };
}
