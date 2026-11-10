// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// EXPERIMENT, not a product change: see ManagedCrypto.targets beside this file. Compiled only into the publish that
// passes that file with MorphirExperimentManagedCrypto=library-random; it shadows
// System.Security.Cryptography.RandomNumberGenerator in Morphir.Extensions.Wasm with the kernel's generator, read from
// /dev/urandom, which needs no OpenSSL. Linux only, as the measurement is.
namespace Morphir.Extensions.Wasm;

/// <summary>EXPERIMENT: random bytes from /dev/urandom, without the platform's cryptography library.</summary>
public static class RandomNumberGenerator
{
    /// <summary>Fills <paramref name="data"/> with random bytes.</summary>
    public static void Fill(Span<byte> data)
    {
        using var urandom = new FileStream("/dev/urandom", FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0);
        urandom.ReadExactly(data);
    }

    /// <summary><paramref name="count"/> random bytes.</summary>
    public static byte[] GetBytes(int count)
    {
        var bytes = new byte[count];
        Fill(bytes);
        return bytes;
    }
}
