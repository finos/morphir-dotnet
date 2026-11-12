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

    static readonly PinnedGuestModule Module = new($"The published Python guest {FileName} ({Repository} {Tag})", DownloadUrl, FileName, Sha256);

    /// <summary>
    /// The bytes of the module. A cached or downloaded file whose digest is not <see cref="Sha256"/> fails the
    /// test, in every environment. When the module cannot be downloaded, the test is skipped unless the
    /// environment variable <c>CI</c> is set, and fails when it is.
    /// </summary>
    public static Task<byte[]> BytesAsync() => Module.BytesAsync();

    /// <summary>The cache directory: <c>artifacts/wasm-guests/</c> at the repository root, which <c>.gitignore</c>
    /// excludes (<c>artifacts/</c>).</summary>
    public static string CacheDirectory => PinnedGuestModule.CacheDirectory;
}

/// <summary>
/// The SDK fixture guest that the generic guest kit's README names (task 3.5): a test fixture of finos/morphir-moonbit,
/// built in Rust with <c>morphir-extension-sdk</c>, committed there with a build receipt. It is not committed here; it
/// is fetched at a pinned commit and checked against the SHA-256 of its build receipt, as
/// <see cref="PublishedGuest"/> fetches the Python guest.
/// </summary>
internal static class SdkFixtureGuest
{
    /// <summary>The repository that holds the fixture.</summary>
    public const string Repository = "finos/morphir-moonbit";

    /// <summary>The commit: <c>origin/main</c> on 9 November 2026, the commit the MepWasm pin cites for the MoonBit
    /// worker.</summary>
    public const string Commit = "89f892468649bc6fef6acc40e5bfb2e9800c16da";

    /// <summary>The path of the module in that repository.</summary>
    public const string RepositoryPath = "apps/morphir/fixtures/sdk-guest/morphir-sdk-fixture-guest.wasm";

    /// <summary>The cache file name; it carries the commit, so another pin is another file.</summary>
    public const string FileName = "morphir-sdk-fixture-guest-89f89246.wasm";

    /// <summary>The SHA-256 of the module, as <c>build-receipt.json</c> beside it records it.</summary>
    public const string Sha256 = "f5e1c9cd7a3472b6261c2c3ebfe2c39aeae14da5287dac6cd6ca87ce854901f1";

    /// <summary>Where GitHub serves the file at the pinned commit.</summary>
    public static Uri DownloadUrl => new($"https://raw.githubusercontent.com/{Repository}/{Commit}/{RepositoryPath}");

    static readonly PinnedGuestModule Module = new($"The SDK fixture guest ({Repository} {Commit[..7]} {RepositoryPath})", DownloadUrl, FileName, Sha256);

    /// <summary>The bytes of the module, with the rules of <see cref="PublishedGuest.BytesAsync"/>.</summary>
    public static Task<byte[]> BytesAsync() => Module.BytesAsync();
}

/// <summary>A guest module the tests download once, check against a pinned SHA-256 and cache under
/// <c>artifacts/wasm-guests/</c>.</summary>
internal sealed class PinnedGuestModule(string description, Uri downloadUrl, string fileName, string sha256)
{
    readonly Lazy<Task<Fetch>> fetched = new(() => FetchAsync(downloadUrl, fileName, sha256), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The bytes of the module. A cached or downloaded file whose digest is not the pinned one fails the test, in
    /// every environment. When the module cannot be downloaded, the test is skipped unless the environment variable
    /// <c>CI</c> is set, and fails when it is.
    /// </summary>
    public async Task<byte[]> BytesAsync()
    {
        var fetch = await fetched.Value;
        if (fetch.Bytes is { } bytes)
        {
            return bytes;
        }

        var reason = $"{description} could not be downloaded from {downloadUrl}: {fetch.Unavailable}";
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

    static async Task<Fetch> FetchAsync(Uri downloadUrl, string fileName, string sha256)
    {
        var cached = Path.Combine(CacheDirectory, fileName);
        if (File.Exists(cached))
        {
            return new Fetch(Checked(await File.ReadAllBytesAsync(cached), cached, fileName, sha256), null);
        }

        byte[] downloaded;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("morphir-dotnet-tests");
            downloaded = await client.GetByteArrayAsync(downloadUrl);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException)
        {
            return new Fetch(null, failure.Message);
        }

        Checked(downloaded, downloadUrl.ToString(), fileName, sha256);
        Directory.CreateDirectory(CacheDirectory);
        var partial = Path.Combine(CacheDirectory, $"{fileName}.{Guid.NewGuid():N}.partial");
        await File.WriteAllBytesAsync(partial, downloaded);
        File.Move(partial, cached, overwrite: true);
        return new Fetch(downloaded, null);
    }

    /// <summary>A wrong digest is a hard failure: the bytes are not the pinned guest.</summary>
    static byte[] Checked(byte[] bytes, string source, string fileName, string sha256)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return digest == sha256
            ? bytes
            : throw new InvalidDataException(
                $"{source} has SHA-256 {digest}, not the pinned {sha256} of {fileName}. Delete the file if it is a stale cache entry.");
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
