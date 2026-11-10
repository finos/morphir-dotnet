// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>What an executable file is, from its bytes.</summary>
public enum ExecutableKind
{
    /// <summary>No bundle signature, and the Native AOT runtime's <c>DotNetRuntimeDebugHeader</c> export name.</summary>
    NativeAot,

    /// <summary>A .NET single-file bundle: the bundle signature with a non-zero bundle header offset before it.</summary>
    SingleFileBundle,

    /// <summary>A .NET apphost that runs an application beside it: the bundle signature with a zero offset.</summary>
    AppHost,

    /// <summary>None of the above.</summary>
    Other,
}

/// <summary>
/// Tells a Native AOT executable from a .NET single-file bundle and a plain apphost. Every apphost, and so every
/// bundle, carries the 32-byte bundle signature of the .NET host, preceded by the 8-byte offset of the bundle
/// header (zero when nothing is bundled). A Native AOT executable runs no .NET host and has no signature; it exports
/// <c>DotNetRuntimeDebugHeader</c> for debuggers. Verified on win-arm64 PE files from .NET 10 (a Native AOT
/// <c>morphir.exe</c>, a single-file bundle and two apphosts); the same names are expected in ELF and Mach-O files
/// but are not verified here.
/// </summary>
public static class ExecutableStructure
{
    /// <summary>The bundle signature: the SHA-256 of ".net core bundle".</summary>
    public static readonly byte[] BundleSignature =
    [
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
    ];

    private static ReadOnlySpan<byte> DebugHeaderExport => "DotNetRuntimeDebugHeader"u8;

    public static ExecutableKind Of(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var signature = bytes.AsSpan().IndexOf(BundleSignature);
        if (signature >= 8)
            return BitConverter.ToInt64(bytes, signature - 8) != 0 ? ExecutableKind.SingleFileBundle : ExecutableKind.AppHost;
        if (signature >= 0) return ExecutableKind.Other;
        return bytes.AsSpan().IndexOf(DebugHeaderExport) >= 0 ? ExecutableKind.NativeAot : ExecutableKind.Other;
    }
}
