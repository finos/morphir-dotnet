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

    /// <summary>
    /// Reads a digest written as <see cref="Hex"/> writes it: 64 lowercase hexadecimal digits, with no prefix. This
    /// is the form of a declared digest, such as the configuration key <c>extensions.&lt;name&gt;.sha256</c>.
    /// </summary>
    /// <returns>True when <paramref name="text"/> is a digest; otherwise <paramref name="digest"/> is the default.</returns>
    public static bool TryParse(string? text, out ModuleDigest digest)
    {
        var valid = text is { Length: 64 } && text.All(static c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
        digest = valid ? new ModuleDigest(text!) : default;
        return valid;
    }

    /// <inheritdoc />
    public override string ToString() => Hex;
}

/// <summary>
/// A module the engine has validated and compiled. The engine keeps it in memory for the life of the process,
/// keyed by <see cref="Digest"/>, and every session of the module instantiates it in its own store.
/// </summary>
public sealed class CompiledModule
{
    internal CompiledModule(WasmEngine engine, ModuleDigest digest, long byteLength, Module module, Module? runModule, string? guestMemoryName)
    {
        Engine = engine;
        Digest = digest;
        ByteLength = byteLength;
        Module = module;
        RunModule = runModule ?? module;
        GuestMemoryName = guestMemoryName;
    }

    /// <summary>The engine that compiled the module.</summary>
    public WasmEngine Engine { get; }

    /// <summary>The SHA-256 of the module's bytes.</summary>
    public ModuleDigest Digest { get; }

    /// <summary>The size of the module's bytes.</summary>
    public long ByteLength { get; }

    /// <summary>The compiled module, with the imports and exports the engine validated.</summary>
    public Module Module { get; }

    /// <summary>
    /// Whether the host runs a copy of the module that also exports its memory, because the module defines a
    /// memory and does not export it (see <c>GuestMemoryExport</c>). The digest and classification are always
    /// the original module's.
    /// </summary>
    public bool ExportsGuestMemoryForTheHost => !ReferenceEquals(RunModule, Module);

    /// <summary>The module a session instantiates: <see cref="Module"/>, or its copy that exports the memory.</summary>
    internal Module RunModule { get; }

    /// <summary>The export name of the guest's memory in <see cref="RunModule"/>, or null when it defines none.</summary>
    internal string? GuestMemoryName { get; }
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
