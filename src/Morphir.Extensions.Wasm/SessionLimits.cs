// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Wasm;

/// <summary>
/// The bounds a session store applies. The memory ceiling is the store limit of every linear memory in the
/// store, the kernel's and the guest's. The instruction budget is the fuel and the call deadline the epoch
/// deadline that each call starts with. How an exhausted budget, a passed deadline, cancellation and the
/// memory ceiling become failure codes is task 2.5.
/// </summary>
/// <param name="MemoryCeilingBytes">The ceiling of each linear memory, in bytes.</param>
/// <param name="InstructionBudget">The fuel each call starts with, in Wasmtime fuel units.</param>
/// <param name="CallDeadline">The time each call may run before the engine interrupts it.</param>
public sealed record SessionLimits(long MemoryCeilingBytes, ulong InstructionBudget, TimeSpan CallDeadline)
{
    /// <summary>The MEP version whose instruction budget <see cref="Default"/> uses.</summary>
    public const string DefaultBudgetProtocol = "0.2.0-draft.1";

    static readonly Lazy<SessionLimits> Defaults = new(FromContract);

    /// <summary>
    /// The contract's bounds for an <c>extism-kernel-v1</c> session: the 256 MiB memory ceiling, the instruction
    /// budget of MEP <c>0.2.0-draft.1</c> and the profile's call deadline, all read from the pinned contract.
    /// </summary>
    public static SessionLimits Default => Defaults.Value;

    static SessionLimits FromContract()
    {
        var contract = WasmProfileContract.Pinned;
        if (!contract.TryGetInstructionBudget(DefaultBudgetProtocol, out var budget))
        {
            throw new InvalidOperationException($"The pinned contract gives no instruction budget for MEP {DefaultBudgetProtocol}.");
        }

        return new SessionLimits(contract.MemoryCeilingBytes, budget, contract.Profile(WasmAbi.ExtismKernel).CallDeadline);
    }
}
