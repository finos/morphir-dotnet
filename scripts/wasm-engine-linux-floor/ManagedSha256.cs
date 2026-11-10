// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// EXPERIMENT, not a product change: see ManagedSha256.targets beside this file. Compiled only into the publish that
// passes that file; it shadows System.Security.Cryptography.SHA256 in one namespace with BouncyCastle's managed digest,
// which needs no OpenSSL.
using Org.BouncyCastle.Crypto.Digests;

#if MORPHIR_EXPERIMENT_MANAGED_SHA256_LIBRARY
namespace Morphir.Extensions.Wasm;

/// <summary>EXPERIMENT: SHA-256 without the platform's cryptography library.</summary>
public
#else
namespace Morphir.Extensions.Wasm.AotProbe;

/// <summary>EXPERIMENT: SHA-256 without the platform's cryptography library.</summary>
internal
#endif
static class SHA256
{
    /// <summary>The SHA-256 of <paramref name="source"/>.</summary>
    public static byte[] HashData(ReadOnlySpan<byte> source)
    {
        var digest = new Sha256Digest();
        digest.BlockUpdate(source);
        var result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return result;
    }

    /// <summary>The SHA-256 of <paramref name="source"/>.</summary>
    public static byte[] HashData(byte[] source) => HashData((ReadOnlySpan<byte>)source);
}
