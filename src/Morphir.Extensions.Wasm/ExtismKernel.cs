// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// The embedded Extism kernel module of <c>moonrockz/extism</c> (Apache-2.0). An Extism guest imports the kernel
/// functions from <see cref="ImportModule"/>; the host links those imports to the exports of a kernel instance in
/// the same store. <c>Kernel/PROVENANCE.md</c> says where the bytes come from.
/// </summary>
public static class ExtismKernel
{
    /// <summary>The import module under which an Extism guest imports the kernel functions.</summary>
    public const string ImportModule = "extism:host/env";

    /// <summary>The <c>moonrockz/extism</c> version the kernel comes from: the version finos/morphir-moonbit pins.</summary>
    public const string PackageVersion = "0.3.0";

    /// <summary>The SHA-256 of the kernel module, as <c>moonrockz/extism</c> records it.</summary>
    public const string Sha256 = "3d23b39dc4cb508c32d5f8911ceeb625070dbe1335288d871e17feb03a7fba17";

    const string ResourceName = "Morphir.Extensions.Wasm.Kernel.extism-kernel.wasm";

    /// <summary>The 20 functions the kernel exports besides <c>memory</c>, with their upstream Extism names.</summary>
    public static IReadOnlyList<string> FunctionNames { get; } =
    [
        "alloc", "free", "length", "length_unsafe", "load_u8", "load_u64", "input_load_u8", "input_load_u64",
        "store_u8", "store_u64", "input_set", "output_set", "input_length", "input_offset", "output_length",
        "output_offset", "reset", "error_set", "error_get", "memory_bytes",
    ];

    static readonly Lazy<byte[]> Embedded = new(Load);

    /// <summary>The kernel module bytes, checked against <see cref="Sha256"/>.</summary>
    public static ReadOnlyMemory<byte> Bytes => Embedded.Value;

    static byte[] Load()
    {
        using var stream = typeof(ExtismKernel).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The assembly has no resource {ResourceName}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return digest == Sha256
            ? bytes
            : throw new InvalidOperationException($"The embedded kernel has SHA-256 {digest}, not {Sha256}.");
    }
}
