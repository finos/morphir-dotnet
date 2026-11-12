// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Security.Cryptography;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// The published Python extension <c>0.6.0</c> as a file for a worker's <c>--module</c>, with the pin and the rules of
/// <c>PublishedGuest</c> in <c>tests/Morphir.Extensions.Wasm.Tests</c>: it is read from the git-ignored cache
/// <c>artifacts/wasm-guests/</c>, or downloaded over HTTPS from its release into that cache, and its SHA-256 is checked
/// before use. The module is not committed.
/// </summary>
internal static class PublishedPythonGuest
{
    /// <summary>The release asset: the guest module.</summary>
    public const string FileName = "morphir-python-binding-0.6.0.wasm";

    /// <summary>The SHA-256 of the module, as the bundle pin of finos/morphir records it.</summary>
    public const string Sha256 = "ce0cba07f0395ff8179b42eb021ade05c68197f69b8e9c0c8bc369d2a035727c";

    /// <summary>Where the release serves the module.</summary>
    public static Uri DownloadUrl => new($"https://github.com/finos/morphir-python/releases/download/extension/python/v0.6.0/{FileName}");

    /// <summary>The cache directory: <c>artifacts/wasm-guests/</c> at the repository root.</summary>
    public static string CacheDirectory => Path.Combine(RepositoryRoot(), "artifacts", "wasm-guests");

    static readonly Lazy<Task<(string? Path, string? Unavailable)>> Fetched =
        new(FetchAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The path of the checked module. A cached or downloaded file whose digest is not <see cref="Sha256"/> fails the
    /// test. When the module cannot be downloaded, the test is skipped unless the environment variable <c>CI</c> is
    /// set, and fails when it is.
    /// </summary>
    public static async Task<string> PathAsync()
    {
        var (path, unavailable) = await Fetched.Value;
        if (path is not null)
        {
            return path;
        }

        var reason = $"The published Python guest {FileName} could not be downloaded from {DownloadUrl}: {unavailable}";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            throw new InvalidOperationException($"{reason}. CI is set, so a test that needs the guest fails.");
        }

        Skip.Test($"{reason}. Set CI to make this a failure.");
        throw new UnreachableException();
    }

    static async Task<(string? Path, string? Unavailable)> FetchAsync()
    {
        var cached = Path.Combine(CacheDirectory, FileName);
        if (File.Exists(cached))
        {
            Checked(await File.ReadAllBytesAsync(cached), cached);
            return (cached, null);
        }

        byte[] downloaded;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("morphir-dotnet-tests");
            downloaded = await client.GetByteArrayAsync(DownloadUrl);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException)
        {
            return (null, failure.Message);
        }

        Checked(downloaded, DownloadUrl.ToString());
        Directory.CreateDirectory(CacheDirectory);
        var partial = Path.Combine(CacheDirectory, $"{FileName}.{Guid.NewGuid():N}.partial");
        await File.WriteAllBytesAsync(partial, downloaded);
        File.Move(partial, cached, overwrite: true);
        return (cached, null);
    }

    /// <summary>A wrong digest is a hard failure: the bytes are not the pinned guest.</summary>
    static void Checked(byte[] bytes, string source)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (digest != Sha256)
        {
            throw new InvalidDataException(
                $"{source} has SHA-256 {digest}, not the pinned {Sha256} of {FileName}. Delete the file if it is a stale cache entry.");
        }
    }

    /// <summary>The repository root: the directory above the test output that holds <c>Morphir.slnx</c>.</summary>
    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Morphir.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No Morphir.slnx above {AppContext.BaseDirectory}: the test does not run inside the repository.");
    }
}
