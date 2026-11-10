using System.Diagnostics;
using System.Security.Cryptography;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// The published Python extension that this host's sessions run (design MEP-1): the one pin of it in the test
/// project, and the one way the tests get its bytes. The module is not committed. It is read from a git-ignored
/// cache in the repository, <c>artifacts/wasm-guests/</c>, or downloaded over HTTPS from its release and checked
/// against the pinned SHA-256 before use.
/// </summary>
internal static class PublishedGuest
{
    /// <summary>The finos/morphir commit whose <c>.config/published-extension-bundles.toml</c> gives this pin
    /// (<c>[bundles.python]</c>).</summary>
    public const string ParentCommit = MepWasmFixtures.PinnedCommit;

    /// <summary>The repository that releases the bundle.</summary>
    public const string Repository = "finos/morphir-python";

    /// <summary>The release tag.</summary>
    public const string Tag = "extension/python/v0.6.0";

    /// <summary>The release asset: the guest module.</summary>
    public const string FileName = "morphir-python-binding-0.6.0.wasm";

    /// <summary>The SHA-256 of the module, as the bundle pin records it.</summary>
    public const string Sha256 = "ce0cba07f0395ff8179b42eb021ade05c68197f69b8e9c0c8bc369d2a035727c";

    /// <summary>Where the release serves the module.</summary>
    public static Uri DownloadUrl => new($"https://github.com/{Repository}/releases/download/{Tag}/{FileName}");

    static readonly Lazy<Task<Fetch>> Fetched = new(FetchAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The bytes of the module. A cached or downloaded file whose digest is not <see cref="Sha256"/> fails the
    /// test, in every environment. When the module cannot be downloaded, the test is skipped unless the
    /// environment variable <c>CI</c> is set, and fails when it is.
    /// </summary>
    public static async Task<byte[]> BytesAsync()
    {
        var fetch = await Fetched.Value;
        if (fetch.Bytes is { } bytes)
        {
            return bytes;
        }

        var reason = $"The published Python guest {FileName} ({Repository} {Tag}) could not be downloaded from {DownloadUrl}: {fetch.Unavailable}";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            throw new InvalidOperationException($"{reason}. CI is set, so a test that needs the guest fails.");
        }

        Skip.Test($"{reason}. Set CI to make this a failure.");
        throw new UnreachableException();
    }

    /// <summary>The cache directory: <c>artifacts/wasm-guests/</c> at the repository root, which <c>.gitignore</c>
    /// excludes (<c>artifacts/</c>).</summary>
    public static string CacheDirectory => Path.Combine(RepositoryRoot(), "artifacts", "wasm-guests");

    sealed record Fetch(byte[]? Bytes, string? Unavailable);

    static async Task<Fetch> FetchAsync()
    {
        var cached = Path.Combine(CacheDirectory, FileName);
        if (File.Exists(cached))
        {
            return new Fetch(Checked(await File.ReadAllBytesAsync(cached), cached), null);
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
            return new Fetch(null, failure.Message);
        }

        Checked(downloaded, DownloadUrl.ToString());
        Directory.CreateDirectory(CacheDirectory);
        var partial = Path.Combine(CacheDirectory, $"{FileName}.{Guid.NewGuid():N}.partial");
        await File.WriteAllBytesAsync(partial, downloaded);
        File.Move(partial, cached, overwrite: true);
        return new Fetch(downloaded, null);
    }

    /// <summary>A wrong digest is a hard failure: the bytes are not the pinned guest.</summary>
    static byte[] Checked(byte[] bytes, string source)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return digest == Sha256
            ? bytes
            : throw new InvalidDataException(
                $"{source} has SHA-256 {digest}, not the pinned {Sha256} of {FileName}. Delete the file if it is a stale cache entry.");
    }

    static string RepositoryRoot()
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
