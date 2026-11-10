// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics.CodeAnalysis;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// The bounds a session store applies (parent decision BND-1). The memory ceiling is the store limit of every
/// linear memory in the store, the kernel's and the guest's. The instruction budget is the fuel and the call
/// deadline the time that each call starts with. The contract's values come from the pinned contract; a test
/// that needs a short deadline or a small budget passes it explicitly with <c>with</c>.
/// </summary>
/// <param name="MemoryCeilingBytes">The ceiling of each linear memory, in bytes.</param>
/// <param name="InstructionBudget">The fuel each call starts with, in Wasmtime fuel units.</param>
/// <param name="CallDeadline">The time each call may run before the host stops it.</param>
public sealed record SessionLimits(long MemoryCeilingBytes, ulong InstructionBudget, TimeSpan CallDeadline)
{
    /// <summary>The MEP version whose instruction budget <see cref="Default"/> uses.</summary>
    public const string DefaultBudgetProtocol = "0.2.0-draft.1";

    /// <summary>The key of the contract's budget for a probe, which is not a MEP version.</summary>
    const string ProbeBudgetKey = "probe";

    static readonly Lazy<SessionLimits> Defaults = new(() => ForProtocol(DefaultBudgetProtocol));

    /// <summary>
    /// The contract's bounds for an <c>extism-kernel-v1</c> session on MEP <c>0.2.0-draft.1</c>: the 256 MiB
    /// memory ceiling, the instruction budget of that version and the profile's call deadline.
    /// </summary>
    public static SessionLimits Default => Defaults.Value;

    /// <summary>
    /// The contract's bounds for an <c>extism-kernel-v1</c> session on MEP <paramref name="protocolVersion"/>:
    /// the memory ceiling, the instruction budget the contract gives that version (5,000,000,000 on
    /// <c>0.2.0-draft.1</c> and 1,000,000,000 on <c>0.1</c> at the pin) and the profile's call deadline. The
    /// session layer chooses it once the protocol version is negotiated.
    /// </summary>
    /// <exception cref="ArgumentException">The contract gives no instruction budget for that version.</exception>
    public static SessionLimits ForProtocol(string protocolVersion) =>
        TryForProtocol(protocolVersion, out var limits)
            ? limits
            : throw new ArgumentException($"The pinned contract gives no instruction budget for MEP {protocolVersion}.", nameof(protocolVersion));

    /// <summary>The bounds of <see cref="ForProtocol"/>, or false when the contract gives no instruction budget
    /// for <paramref name="protocolVersion"/>. The contract's <c>probe</c> budget is not a MEP version.</summary>
    public static bool TryForProtocol(string protocolVersion, [NotNullWhen(true)] out SessionLimits? limits)
    {
        ArgumentNullException.ThrowIfNull(protocolVersion);
        var contract = WasmProfileContract.Pinned;
        if (protocolVersion == ProbeBudgetKey || !contract.TryGetInstructionBudget(protocolVersion, out var budget))
        {
            limits = null;
            return false;
        }

        limits = new SessionLimits(contract.MemoryCeilingBytes, budget, contract.Profile(WasmAbi.ExtismKernel).CallDeadline);
        return true;
    }
}
