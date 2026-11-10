// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Wasm;

/// <summary>
/// The bounds a session applies (parent decision BND-1). The memory ceiling is the store limit of every linear
/// memory in the store, the kernel's and the guest's. The instruction budget is the fuel and the call deadline the
/// time that each call starts with. The frame limits are the host session's request and response limits, which
/// the channel applies (<see cref="WasmChannel"/>). Every value comes from the pinned contract; a test or a worker
/// argument that needs another value passes it explicitly with <c>with</c>.
/// </summary>
/// <param name="MemoryCeilingBytes">The ceiling of each linear memory, in bytes.</param>
/// <param name="InstructionBudget">The fuel each call starts with, in Wasmtime fuel units.</param>
/// <param name="CallDeadline">The time each call may run before the host stops it.</param>
public sealed record SessionLimits(long MemoryCeilingBytes, ulong InstructionBudget, TimeSpan CallDeadline)
{
    static readonly Lazy<SessionLimits> Defaults = new(() =>
    {
        var contract = WasmProfileContract.Pinned;
        return new SessionLimits(contract.MemoryCeilingBytes, contract.InstructionBudget, contract.Profile(WasmAbi.ExtismKernel).CallDeadline);
    });

    /// <summary>
    /// The contract's bounds for an <c>extism-kernel-v1</c> session: the 256 MiB memory ceiling, the instruction
    /// budget of MEP <c>0.2.0-draft.1</c> (5,000,000,000 at the pin), the profile's call deadline of 30 seconds, and
    /// the host session's frame limits of 6 MiB for a request and 16 MiB for a response. This host speaks MEP
    /// <c>0.2.0-draft.1</c> only (design MEP-1), so it has one instruction budget.
    /// </summary>
    public static SessionLimits Default => Defaults.Value;

    /// <summary>The largest request frame the channel passes to the guest, in bytes, counted over the whole frame,
    /// its <c>Content-Length</c> header included. The contract's limit unless set.</summary>
    public long RequestFrameBytes { get; init; } = WasmProfileContract.Pinned.RequestFrameLimitBytes;

    /// <summary>The largest response frame the channel accepts from the guest, in bytes, counted over the whole
    /// frame. The contract's limit unless set.</summary>
    public long ResponseFrameBytes { get; init; } = WasmProfileContract.Pinned.ResponseFrameLimitBytes;
}
