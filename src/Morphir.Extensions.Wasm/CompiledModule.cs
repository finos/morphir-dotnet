// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Security.Cryptography;
using Wasmtime;

namespace Morphir.Extensions.Wasm;

/// <summary>The SHA-256 of a module's bytes: the key under which the engine keeps the compiled module.</summary>
public readonly record struct ModuleDigest
{
    readonly string? hex;

    ModuleDigest(string hex) => this.hex = hex;

    /// <summary>The digest in lowercase hexadecimal.</summary>
    public string Hex => hex ?? string.Empty;

    /// <summary>The digest of <paramref name="bytes"/>.</summary>
    public static ModuleDigest Of(ReadOnlySpan<byte> bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));

    /// <inheritdoc />
    public override string ToString() => Hex;
}

/// <summary>
/// A module the engine has validated and compiled. The engine keeps it in memory for the life of the process,
/// keyed by <see cref="Digest"/>, and every session of the module instantiates it in its own store.
/// </summary>
public sealed class CompiledModule
{
    internal CompiledModule(WasmEngine engine, ModuleDigest digest, long byteLength, Module module)
    {
        Engine = engine;
        Digest = digest;
        ByteLength = byteLength;
        Module = module;
    }

    /// <summary>The engine that compiled the module.</summary>
    public WasmEngine Engine { get; }

    /// <summary>The SHA-256 of the module's bytes.</summary>
    public ModuleDigest Digest { get; }

    /// <summary>The size of the module's bytes.</summary>
    public long ByteLength { get; }

    /// <summary>The compiled module, with the imports and exports the engine validated.</summary>
    public Module Module { get; }
}

/// <summary>What the engine made of a module's bytes.</summary>
public abstract record ModuleLoad
{
    private ModuleLoad() { }

    /// <summary>The engine validated and compiled the module.</summary>
    public sealed record Compiled(CompiledModule Module) : ModuleLoad;

    /// <summary>The module needs a WASM feature the engine does not have (<c>extension.wasm_feature_unsupported</c>).</summary>
    public sealed record Refused(WasmRefusal Refusal) : ModuleLoad;

    /// <summary>
    /// The bytes are not a WASM module this engine can load, even with every feature enabled. The contract
    /// gives this no refusal code; task 2.6 reports it as a path that is not a WASM module.
    /// </summary>
    /// <param name="Reason">The engine's message.</param>
    public sealed record Invalid(string Reason) : ModuleLoad;

    /// <summary>Calls the function for this result.</summary>
    public TResult Match<TResult>(Func<CompiledModule, TResult> compiled, Func<WasmRefusal, TResult> refused, Func<string, TResult> invalid) => this switch
    {
        Compiled result => compiled(result.Module),
        Refused result => refused(result.Refusal),
        Invalid result => invalid(result.Reason),
        _ => throw new UnreachableException(),
    };
}
