// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// How a guest call or a session start failed (parent decisions BND-1 and BND-2). Each kind but
/// <see cref="Cancelled"/> is a fault kind of the pinned contract; the contract gives its host code or host
/// outcome. <see cref="Cancelled"/> is the .NET host's outcome for a cancelled command, which the contract does
/// not list.
/// </summary>
public enum GuestFailureKind
{
    /// <summary>The guest trapped for a reason that is not a bound: contract fault <c>trap</c>, <c>extension.guest_trap</c>.</summary>
    GuestTrap,

    /// <summary>A memory rule applies: contract fault <c>memory-limit</c>, <c>extension.memory_limit</c>.</summary>
    MemoryLimit,

    /// <summary>The call ran past its instruction budget: contract fault <c>budget-exhausted</c>, <c>extension.budget_exhausted</c>.</summary>
    BudgetExhausted,

    /// <summary>The call did not return before its deadline: contract fault <c>call-deadline</c>, outcome <c>deadline</c>.</summary>
    CallDeadline,

    /// <summary>Any other end the guest caused, such as a host function that stops the call or a <c>proc_exit</c>
    /// with a code that is not 0: contract fault <c>failure</c>, <c>extension.incomplete_session</c>.</summary>
    IncompleteSession,

    /// <summary>The host cancelled the call: outcome <c>cancelled</c> (the .NET host's spec, "Cancellation stops the guest").</summary>
    Cancelled,
}

/// <summary>
/// The memory of a session when a call ended: the guest's own linear memory, the Extism kernel's memory and the
/// ceiling that the store limit gives each of them.
/// </summary>
/// <param name="GuestBytes">The size of the guest's memory, or null when the guest defines no memory.</param>
/// <param name="KernelBytes">The size of the kernel's memory, where the guest's input, output and allocations live.</param>
/// <param name="CeilingBytes">The ceiling of each linear memory of the session.</param>
public sealed record MemoryUse(long? GuestBytes, long KernelBytes, long CeilingBytes)
{
    /// <summary>The size of a WASM page, the unit a memory grows by.</summary>
    public const long PageBytes = 65536;

    /// <summary>
    /// Whether the guest's memory is at its ceiling: it cannot grow by one more page without passing the ceiling.
    /// A ceiling that is not a whole number of pages is reached one partial page below it. A guest without a
    /// memory is never at the ceiling.
    /// </summary>
    public bool GuestAtCeiling => GuestBytes is { } guest && Full(guest);

    /// <summary>
    /// Whether the Extism kernel's memory, where the guest's input, output and allocations live, is at its ceiling:
    /// it cannot grow by one more page. The kernel's ceiling is the same store limit as the guest's.
    /// </summary>
    public bool KernelAtCeiling => Full(KernelBytes);

    /// <summary>Whether the guest's memory or the kernel's is at its ceiling, which the contract's <c>at-ceiling</c>
    /// memory rule asks of a call that ends in a trap or a failure.</summary>
    public bool AtCeiling => GuestAtCeiling || KernelAtCeiling;

    /// <summary>Which memory is at its ceiling, as a diagnostic states it, or null when neither is.</summary>
    public string? AtCeilingText => (GuestAtCeiling, KernelAtCeiling) switch
    {
        (true, true) => "the guest's memory and the kernel's memory at their ceiling",
        (true, false) => "the guest's memory at its ceiling",
        (false, true) => "the kernel's memory at its ceiling",
        _ => null,
    };

    bool Full(long bytes) => bytes > CeilingBytes - PageBytes;

    /// <summary>The sizes as a diagnostic states them, for example
    /// <c>guest memory 100 MiB (104857600 bytes), kernel memory 1 MiB (1048576 bytes), ceiling 256 MiB (268435456 bytes)</c>.</summary>
    public override string ToString()
    {
        var guest = GuestBytes is { } bytes ? $"guest memory {Size(bytes)}" : "the guest has no memory of its own";
        return $"{guest}, kernel memory {Size(KernelBytes)}, ceiling {Size(CeilingBytes)}";
    }

    static string Size(long bytes)
    {
        var mebibytes = bytes / (1024.0 * 1024.0);
        var shown = mebibytes == Math.Floor(mebibytes)
            ? mebibytes.ToString("0", CultureInfo.InvariantCulture)
            : mebibytes.ToString("0.###", CultureInfo.InvariantCulture);
        return $"{shown} MiB ({bytes.ToString(CultureInfo.InvariantCulture)} bytes)";
    }
}

/// <summary>
/// A failed guest call, or a session that failed before any request, with what the contract makes of it and the
/// memory sizes that every failure states (parent decision BND-2).
/// </summary>
/// <param name="Kind">The failure, after the memory rule: a trap or a failure becomes <see cref="GuestFailureKind.MemoryLimit"/> when the guest's memory or the kernel's is at its ceiling.</param>
/// <param name="Signal">What the engine or the host observed before the memory rule; equal to <paramref name="Kind"/> when the rule did not apply.</param>
/// <param name="Detail">What the engine or the host reported, such as the trap message.</param>
/// <param name="Memory">The memory sizes when the call ended.</param>
/// <param name="FuelUsed">The fuel the call used: its budget minus what remained.</param>
/// <param name="Elapsed">How long the call ran.</param>
public sealed record GuestFailure(
    GuestFailureKind Kind,
    GuestFailureKind Signal,
    string Detail,
    MemoryUse Memory,
    ulong FuelUsed,
    TimeSpan Elapsed)
{
    /// <summary>The outcome of a cancelled call, from the .NET host's spec; the contract does not list it.</summary>
    public const string CancelledOutcome = "cancelled";

    /// <summary>The contract's fault kind, as a worker reports it in <c>fault.kind</c>, or null for a cancelled call.</summary>
    public string? FaultKind => ContractFault?.Kind;

    /// <summary>The contract's host code, such as <c>extension.budget_exhausted</c>, or null for an outcome.</summary>
    public string? Code => ContractFault?.HostCode;

    /// <summary>The host outcome, <c>deadline</c> or <c>cancelled</c>, or null for a failure with a code.</summary>
    public string? Outcome => Kind == GuestFailureKind.Cancelled ? CancelledOutcome : ContractFault?.HostOutcome;

    /// <summary>The diagnostic: the code or outcome, what was observed, and the memory sizes.</summary>
    public string Message
    {
        get
        {
            var name = Code ?? $"outcome {Outcome}";
            var rule = Kind != Signal ? $" ({Describe(Signal)}, with {Memory.AtCeilingText})" : string.Empty;
            return $"{name}: {Describe(Kind)}{rule}: {Detail}; {Memory}";
        }
    }

    ContractFault? ContractFault => FaultKindOf(Kind) is { } kind ? WasmProfileContract.Pinned.Fault(kind) : null;

    /// <summary>The contract's fault kind of <paramref name="kind"/>, or null for <see cref="GuestFailureKind.Cancelled"/>.</summary>
    public static string? FaultKindOf(GuestFailureKind kind) => kind switch
    {
        GuestFailureKind.GuestTrap => "trap",
        GuestFailureKind.MemoryLimit => "memory-limit",
        GuestFailureKind.BudgetExhausted => "budget-exhausted",
        GuestFailureKind.CallDeadline => "call-deadline",
        GuestFailureKind.IncompleteSession => "failure",
        GuestFailureKind.Cancelled => null,
        _ => throw new UnreachableException(),
    };

    static string Describe(GuestFailureKind kind) => kind switch
    {
        GuestFailureKind.GuestTrap => "the guest trapped",
        GuestFailureKind.MemoryLimit => "the guest failed at the memory ceiling",
        GuestFailureKind.BudgetExhausted => "the call ran past its instruction budget",
        GuestFailureKind.CallDeadline => "the call did not return before its deadline",
        GuestFailureKind.IncompleteSession => "the call ended the session",
        GuestFailureKind.Cancelled => "the call was cancelled",
        _ => throw new UnreachableException(),
    };
}

/// <summary>What one <c>handle</c> call gave.</summary>
public abstract record HandleOutcome
{
    private HandleOutcome() { }

    /// <summary>
    /// <c>handle</c> returned. What its result, output and error mean for the session (an Extism error, a non-zero
    /// result, no output) the Extism adapter, <see cref="WasmChannel"/>, decides.
    /// </summary>
    /// <param name="Result">The i32 result and what the kernel holds.</param>
    /// <param name="FuelUsed">The fuel the call used: its budget minus what remained.</param>
    /// <param name="Elapsed">How long the call ran.</param>
    /// <param name="Memory">The memory sizes after the call.</param>
    public sealed record Completed(HandleCallResult Result, ulong FuelUsed, TimeSpan Elapsed, MemoryUse Memory) : HandleOutcome;

    /// <summary>The call failed. The session is finished: no later call runs the guest.</summary>
    public sealed record Failed(GuestFailure Failure) : HandleOutcome;

    /// <summary>The session had already failed, so the guest was not called. <paramref name="EndedBy"/> is the
    /// failure that finished the session, the same value its call returned.</summary>
    public sealed record NotRun(GuestFailure EndedBy) : HandleOutcome;

    /// <summary>Calls the function for this outcome.</summary>
    public TResult Match<TResult>(Func<Completed, TResult> completed, Func<GuestFailure, TResult> failed, Func<GuestFailure, TResult> notRun) => this switch
    {
        Completed outcome => completed(outcome),
        Failed outcome => failed(outcome.Failure),
        NotRun outcome => notRun(outcome.EndedBy),
        _ => throw new UnreachableException(),
    };
}

/// <summary>What opening a session gave: a session, or a failure before any request.</summary>
public abstract record SessionOpening
{
    private SessionOpening() { }

    /// <summary>The session is open; no guest export has run.</summary>
    public sealed record Opened(SessionStore Session) : SessionOpening;

    /// <summary>
    /// The session failed before any request. The one such failure is the contract's
    /// <c>initial-above-ceiling</c> memory rule: a fault, not a refusal.
    /// </summary>
    public sealed record Failed(GuestFailure Failure) : SessionOpening;

    /// <summary>Calls the function for this result.</summary>
    public TResult Match<TResult>(Func<SessionStore, TResult> opened, Func<GuestFailure, TResult> failed) => this switch
    {
        Opened result => opened(result.Session),
        Failed result => failed(result.Failure),
        _ => throw new UnreachableException(),
    };
}
