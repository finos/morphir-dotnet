// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;

namespace Morphir.Build.Helpers;

/// <summary>
/// The release assets of the morphirup manager. They follow the <c>morphir</c> assets: the bare executable
/// <c>morphirup-{rid}[.exe]</c>, and <c>morphirup-{rid}-v{version}.tar.gz</c> with the executable under <c>{rid}/</c>.
/// <c>morphirup-v{version}.sha256</c> lists the SHA-256 of every morphirup asset in the format that <c>sha256sum -c</c>
/// reads. The names never start with <c>morphir-</c>, so the patterns that pick the <c>morphir</c> assets do not pick these.
/// </summary>
public static class MorphirupRelease
{
    private const string Prefix = "morphirup-";

    /// <summary>The platforms that releases build, in the order of the release workflow's matrix.</summary>
    public static ImmutableArray<string> Platforms { get; } = ["linux-x64", "linux-arm64", "win-x64", "win-arm64", "osx-arm64"];

    /// <summary>The executable's own name on the platform: <c>morphirup</c> or <c>morphirup.exe</c>.</summary>
    public static string LaunchName(string rid) => IsWindows(rid) ? "morphirup.exe" : "morphirup";

    /// <summary>The bare executable asset, for example <c>morphirup-win-x64.exe</c>.</summary>
    public static string ExecutableName(string rid) => IsWindows(rid) ? $"{Prefix}{rid}.exe" : $"{Prefix}{rid}";

    /// <summary>The tarball asset, for example <c>morphirup-osx-arm64-v1.2.3.tar.gz</c>.</summary>
    public static string ArchiveName(string rid, string version) => $"{Prefix}{rid}-v{version}.tar.gz";

    /// <summary>The checksum file of a release, for example <c>morphirup-v1.2.3.sha256</c>.</summary>
    public static string ChecksumFileName(string version) => $"{Prefix}v{version}.sha256";

    /// <summary>
    /// The release's third-party notices of the executables, for example
    /// <c>morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt</c>: the bare executables have nothing beside them, so the release
    /// carries the text that each archive holds as <see cref="NoticesEntry"/>.
    /// </summary>
    public static string NoticesFileName(string version) => $"{Prefix}v{version}-THIRD-PARTY-NOTICES.txt";

    /// <summary>The name of the third-party notices beside the executable in an archive.</summary>
    public const string NoticesEntry = "THIRD-PARTY-NOTICES.txt";

    /// <summary>
    /// Every entry of the archive, sorted: the executable, the repository's <c>LICENSE</c> and <c>NOTICE</c>, and the
    /// third-party notices, all under <c>{rid}/</c>.
    /// </summary>
    public static IReadOnlyList<string> ArchiveEntries(string rid) =>
        [.. new[] { LaunchName(rid), "LICENSE", "NOTICE", NoticesEntry }.Select(name => $"{rid}/{name}").Order(StringComparer.Ordinal)];

    /// <summary>
    /// Writes the assets of <paramref name="executable"/> into <paramref name="assets"/> and returns their names: a copy
    /// named <see cref="ExecutableName"/>; the tarball, written by <see cref="ReleaseArchive"/>, with the executable
    /// (mode 0755), <paramref name="license"/>, <paramref name="notice"/> and the third-party notices under
    /// <c>{rid}/</c> (see <see cref="ArchiveEntries"/>); and the third-party notices as <see cref="NoticesFileName"/>.
    /// </summary>
    public static IReadOnlyList<string> WriteAssets(string executable, string rid, string version, string assets, string license, string notice, string thirdPartyNotices)
    {
        Directory.CreateDirectory(assets);
        var bare = Path.Combine(assets, ExecutableName(rid));
        File.Copy(executable, bare, overwrite: true);
        File.WriteAllText(Path.Combine(assets, NoticesFileName(version)), thirdPartyNotices, new UTF8Encoding(false));

        var staging = Path.Combine(assets, $".staging-{rid}");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(staging, rid)).FullName;
            File.Copy(executable, Path.Combine(folder, LaunchName(rid)));
            File.Copy(license, Path.Combine(folder, "LICENSE"));
            File.Copy(notice, Path.Combine(folder, "NOTICE"));
            File.Copy(Path.Combine(assets, NoticesFileName(version)), Path.Combine(folder, NoticesEntry));
            // tar.gz on every platform, Windows included, as for the morphir tarballs.
            ReleaseArchive.Create(staging, Path.Combine(assets, ArchiveName(rid, version)), ArchiveFormat.TarGz,
                ReleaseArchive.ExecutablesFor($"{rid}/{LaunchName(rid)}"));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }

        return [ExecutableName(rid), ArchiveName(rid, version), NoticesFileName(version)];
    }

    /// <summary>
    /// Writes <see cref="ChecksumFileName"/> in <paramref name="assets"/>, listing every morphirup asset there (every file
    /// whose name starts with <c>morphirup-</c>, apart from checksum files) sorted by name, and returns its path.
    /// </summary>
    public static string WriteChecksums(string assets, string version)
    {
        var path = Path.Combine(assets, ChecksumFileName(version));
        var text = new StringBuilder();
        foreach (var name in Assets(assets))
            text.Append(ReleaseArchive.Sha256(Path.Combine(assets, name))).Append("  ").Append(name).Append('\n');
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// What differs between <paramref name="checksums"/> and the morphirup assets in <paramref name="assets"/>: a listed
    /// asset that is missing or has another SHA-256, and an asset that is not listed. Empty when they agree.
    /// </summary>
    public static IReadOnlyList<string> VerifyChecksums(string assets, string checksums)
    {
        var problems = new List<string>();
        var listed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(checksums).Where(line => line.Length > 0))
        {
            var separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator != 64)
            {
                problems.Add($"{Path.GetFileName(checksums)} has a line that is not '<sha256>  <name>': {line}");
                continue;
            }

            listed[line[(separator + 2)..]] = line[..separator];
        }

        foreach (var (name, digest) in listed.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(assets, name);
            if (!File.Exists(path))
                problems.Add($"{name} is listed but missing");
            else if (ReleaseArchive.Sha256(path) != digest)
                problems.Add($"{name} has SHA-256 {ReleaseArchive.Sha256(path)}, not the listed {digest}");
        }

        foreach (var name in Assets(assets).Where(name => !listed.ContainsKey(name)))
            problems.Add($"{name} is a morphirup asset that is not listed");

        return problems;
    }

    /// <summary>The environment of an artifact check; see <see cref="NativeCheck.CheckEnvironment"/>.</summary>
    public static IReadOnlyDictionary<string, string> CheckEnvironment(IReadOnlyDictionary<string, string?> inherited, bool windows) =>
        NativeCheck.CheckEnvironment(inherited, windows);

    /// <summary>The highest glibc the Linux executables may need; see <see cref="NativeCheck.GlibcFloor"/>.</summary>
    public static Version GlibcFloor => NativeCheck.GlibcFloor;

    /// <summary>The pinned image the Linux executables are published in; see <see cref="NativeCheck.LinuxBuildImage"/>.</summary>
    public const string LinuxBuildImage = NativeCheck.LinuxBuildImage;

    /// <summary>The highest glibc version an executable needs; see <see cref="NativeCheck.HighestGlibc"/>.</summary>
    public static (Version Version, string File)? HighestGlibc(string readelf) => NativeCheck.HighestGlibc(readelf);

    /// <summary>Whether a needed glibc version is above the floor; see <see cref="NativeCheck.IsAboveFloor"/>.</summary>
    public static bool IsAboveFloor(Version needed) => NativeCheck.IsAboveFloor(needed);

    /// <summary>The minimum macOS version an executable declares; see <see cref="NativeCheck.MacOsMinimum"/>.</summary>
    public static string? MacOsMinimum(string otool) => NativeCheck.MacOsMinimum(otool);

    private static IEnumerable<string> Assets(string assets) =>
        Directory.GetFiles(assets)
            .Select(path => Path.GetFileName(path))
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && !name.EndsWith(".sha256", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static bool IsWindows(string rid) => rid.StartsWith("win-", StringComparison.Ordinal);
}
