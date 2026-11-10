using System.Security.Cryptography;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// The pinned finos/morphir WASM host inputs under <c>tests/Fixtures/MepWasm/</c>, which the build
/// copies beside the test assembly. <c>PROVENANCE.md</c> in that directory records their origin.
/// </summary>
internal static class MepWasmFixtures
{
    /// <summary>The finos/morphir commit every pinned file is copied from.</summary>
    public const string PinnedCommit = "938f798da71d97d5b06018e7ec2b3fe92cf1fe2b";

    public static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "MepWasm");

    public static string Provenance => Path.Combine(Root, "PROVENANCE.md");

    public static string Corpus => Path.Combine(Root, "wasm-host.cases.json");

    public static string Contract => Path.Combine(Root, "wasm-execution-profiles.json");

    /// <summary>A path relative to <see cref="Root"/>, written with forward slashes.</summary>
    public static string RelativeName(string path) =>
        Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');

    public static string FullPath(string relativeName) =>
        Path.GetFullPath(Path.Combine(Root, relativeName.Replace('/', Path.DirectorySeparatorChar)));

    public static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
