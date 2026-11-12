// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// An ABI profile of the WASM execution profile contract: how a module exchanges MEP messages with the host.
/// The contract defines exactly two; <see cref="WasmProfileContract"/> checks that the pinned contract defines
/// these two and no other.
/// </summary>
public abstract record WasmAbi
{
    private WasmAbi(string id) => Id = id;

    /// <summary>The profile's identifier in the contract, such as <c>extism-kernel-v1</c>.</summary>
    public string Id { get; }

    /// <summary><c>extism-kernel-v1</c>: an Extism module that moves its bytes through the Extism kernel.
    /// Every host runs it.</summary>
    public sealed record ExtismKernelV1() : WasmAbi("extism-kernel-v1");

    /// <summary><c>morphir-bytes-v1</c>: a module that pulls and pushes message bytes through three byte
    /// callbacks. Transitional; this host classifies it but does not run it.</summary>
    public sealed record MorphirBytesV1() : WasmAbi("morphir-bytes-v1");

    /// <summary>The <c>extism-kernel-v1</c> profile.</summary>
    public static WasmAbi ExtismKernel { get; } = new ExtismKernelV1();

    /// <summary>The <c>morphir-bytes-v1</c> profile.</summary>
    public static WasmAbi MorphirBytes { get; } = new MorphirBytesV1();

    /// <summary>Both profiles of the contract.</summary>
    public static IReadOnlyList<WasmAbi> All { get; } = [ExtismKernel, MorphirBytes];

    /// <summary>Reads a profile identifier, such as the value of a declared ABI.</summary>
    /// <returns>True when <paramref name="text"/> is the identifier of a contract profile.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out WasmAbi? abi)
    {
        abi = All.FirstOrDefault(profile => string.Equals(profile.Id, text, StringComparison.Ordinal));
        return abi is not null;
    }

    /// <summary>Calls the function for this profile.</summary>
    public TResult Match<TResult>(Func<TResult> extismKernel, Func<TResult> morphirBytes) => this switch
    {
        ExtismKernelV1 => extismKernel(),
        MorphirBytesV1 => morphirBytes(),
        _ => throw new UnreachableException(),
    };

    /// <summary>The profile's identifier.</summary>
    public sealed override string ToString() => Id;
}
