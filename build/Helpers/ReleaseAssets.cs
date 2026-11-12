// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Morphir.Build.Helpers;

/// <summary>
/// The release-assets folder that the release job attaches, assembled from the outputs that each platform's job
/// uploaded. Each output is downloaded into a folder of its own and flattened once into the assets folder: a name that
/// two outputs share must hold the same bytes, so nothing is overwritten unseen, and a per-platform
/// <see cref="WindowsToolchainChecksumFileName"/> is never copied. One checksum file is then written over the Windows
/// toolchain outputs of both Windows platforms.
/// </summary>
public static class ReleaseAssets
{
    /// <summary>The checksum file of the Windows toolchain outputs, per platform and for the release.</summary>
    public const string WindowsToolchainChecksumFileName = "SHA256SUMS";

    /// <summary>The platforms whose Windows toolchain outputs a release needs.</summary>
    public static ImmutableArray<string> WindowsToolchainPlatforms { get; } = ["win-x64", "win-arm64"];

    /// <summary>
    /// The files that a platform's Windows toolchain output must hold for the release checksum file to be written: its
    /// bundle and its manager.
    /// </summary>
    public static IReadOnlyList<string> WindowsToolchainRequiredFiles(string rid) =>
        [$"morphir-toolchain-{rid}.json", MorphirupRelease.ExecutableName(rid)];

    /// <summary>What <see cref="Flatten"/> copied or found already there, by name in ordinal order, and what it refused.</summary>
    public sealed record FlattenResult(IReadOnlyList<string> Files, IReadOnlyList<string> Problems);

    /// <summary>What <see cref="WriteWindowsToolchainChecksums"/> listed, and why it wrote nothing when it did not.</summary>
    public sealed record ChecksumResult(string? Path, IReadOnlyList<string> Problems);

    /// <summary>
    /// Copies the files of each folder in <paramref name="sources"/> into <paramref name="destination"/>. A name that is
    /// already there with the same bytes is accepted; one with other bytes is a problem and is not copied. A
    /// <see cref="WindowsToolchainChecksumFileName"/> in a source is a per-platform file and is skipped. A folder inside a
    /// source is a problem: uploaded outputs hold files only.
    /// </summary>
    public static FlattenResult Flatten(IEnumerable<string> sources, string destination)
    {
        Directory.CreateDirectory(destination);
        var files = new SortedSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var source in sources)
        {
            foreach (var folder in Directory.GetDirectories(source).Order(StringComparer.Ordinal))
                problems.Add($"{source} holds the folder {Path.GetFileName(folder)}; an uploaded output holds files only.");

            foreach (var path in Directory.GetFiles(source).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                if (name == WindowsToolchainChecksumFileName) continue;
                var target = Path.Combine(destination, name);
                if (File.Exists(target))
                {
                    if (ReleaseArchive.Sha256(target) != ReleaseArchive.Sha256(path))
                    {
                        problems.Add($"{name} from {source} differs from the {name} already in {destination}; it is not copied.");
                        continue;
                    }
                }
                else
                {
                    File.Copy(path, target);
                }

                files.Add(name);
            }
        }

        return new FlattenResult([.. files], problems);
    }

    /// <summary>
    /// Writes <see cref="WindowsToolchainChecksumFileName"/> in <paramref name="assets"/>, listing every file of the
    /// Windows toolchain outputs in <paramref name="sources"/> (apart from their own checksum files) as it is in
    /// <paramref name="assets"/>, after <see cref="Flatten"/>. Writes nothing when a platform of
    /// <see cref="WindowsToolchainPlatforms"/> lacks a file of <see cref="WindowsToolchainRequiredFiles"/>, or a listed
    /// file is not in <paramref name="assets"/> with the bytes of its output.
    /// </summary>
    public static ChecksumResult WriteWindowsToolchainChecksums(IEnumerable<string> sources, string assets)
    {
        var problems = new List<string>();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var path in Directory.GetFiles(source))
            {
                var name = Path.GetFileName(path);
                if (name == WindowsToolchainChecksumFileName) continue;
                var flattened = Path.Combine(assets, name);
                if (!File.Exists(flattened) || ReleaseArchive.Sha256(flattened) != ReleaseArchive.Sha256(path))
                    problems.Add($"{name} from {source} is not in {assets} with the same bytes.");
                names.Add(name);
            }
        }

        foreach (var rid in WindowsToolchainPlatforms)
        {
            foreach (var required in WindowsToolchainRequiredFiles(rid).Where(required => !names.Contains(required)))
                problems.Add($"The Windows toolchain output of {rid} has no {required}.");
            var bundle = WindowsToolchainRequiredFiles(rid)[0];
            if (names.Contains(bundle))
                problems.AddRange(BundleProblems(rid, bundle, assets, names));
        }

        if (problems.Count > 0) return new ChecksumResult(null, problems);
        var checksums = Path.Combine(assets, WindowsToolchainChecksumFileName);
        File.WriteAllText(checksums, Sha256Lines(assets, names), new UTF8Encoding(false));
        return new ChecksumResult(checksums, []);
    }

    /// <summary>
    /// What a bundle says that the outputs contradict. The release SHA256SUMS lists the bundle and every asset it
    /// names with the bundle's digests (finos/morphir local-toolchains <c>0.1.0-preview.3</c>, rule
    /// <c>release-bindings</c>), so each named asset must be among the outputs with the bundle's bytes and SHA-256, and
    /// the bundle's <c>platform</c> must be its RID. Only those members are read; the bundle's own schema and the
    /// other rules are the reader's.
    /// </summary>
    private static IEnumerable<string> BundleProblems(string rid, string bundle, string assets, IReadOnlySet<string> names)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(assets, bundle)));
        }
        catch (JsonException error)
        {
            return [$"{bundle} is not JSON: {error.Message}"];
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("platform", out var platform) || platform.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("assets", out var listed) || listed.ValueKind != JsonValueKind.Array)
                return [$"{bundle} has no platform string or assets array."];

            var problems = new List<string>();
            if (platform.GetString() != rid)
                problems.Add($"{bundle} names the platform {platform.GetString()}, not {rid}.");
            foreach (var asset in listed.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object
                    || !asset.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String
                    || !asset.TryGetProperty("bytes", out var bytesElement) || !bytesElement.TryGetInt64(out var bytes)
                    || !asset.TryGetProperty("sha256", out var shaElement) || shaElement.ValueKind != JsonValueKind.String)
                {
                    problems.Add($"{bundle} has an asset without a name, bytes and sha256.");
                    continue;
                }

                var name = nameElement.GetString()!;
                var path = Path.Combine(assets, name);
                if (!names.Contains(name) || !File.Exists(path))
                    problems.Add($"{name}, named by {bundle}, is missing from the Windows toolchain outputs.");
                else if (new FileInfo(path).Length != bytes || ReleaseArchive.Sha256(path) != shaElement.GetString())
                    problems.Add($"{name} has {new FileInfo(path).Length} bytes and SHA-256 {ReleaseArchive.Sha256(path)}; {bundle} says {bytes} bytes and {shaElement.GetString()}.");
            }

            return problems;
        }
    }

    /// <summary>
    /// The lines of a checksum file in the format that <c>sha256sum -c</c> reads: the lowercase SHA-256 of each named
    /// file in <paramref name="folder"/>, two spaces and the name, one line each, sorted by name with ordinal comparison.
    /// </summary>
    public static string Sha256Lines(string folder, IEnumerable<string> names)
    {
        var text = new StringBuilder();
        foreach (var name in names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            text.Append(ReleaseArchive.Sha256(Path.Combine(folder, name))).Append("  ").Append(name).Append('\n');
        return text.ToString();
    }
}
