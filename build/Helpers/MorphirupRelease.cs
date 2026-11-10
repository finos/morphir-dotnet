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
    public static ImmutableArray<string> Platforms { get; } = ["linux-x64", "linux-arm64", "win-x64", "osx-arm64"];

    /// <summary>The executable's own name on the platform: <c>morphirup</c> or <c>morphirup.exe</c>.</summary>
    public static string LaunchName(string rid) => IsWindows(rid) ? "morphirup.exe" : "morphirup";

    /// <summary>The bare executable asset, for example <c>morphirup-win-x64.exe</c>.</summary>
    public static string ExecutableName(string rid) => IsWindows(rid) ? $"{Prefix}{rid}.exe" : $"{Prefix}{rid}";

    /// <summary>The tarball asset, for example <c>morphirup-osx-arm64-v1.2.3.tar.gz</c>.</summary>
    public static string ArchiveName(string rid, string version) => $"{Prefix}{rid}-v{version}.tar.gz";

    /// <summary>The checksum file of a release, for example <c>morphirup-v1.2.3.sha256</c>.</summary>
    public static string ChecksumFileName(string version) => $"{Prefix}v{version}.sha256";

    /// <summary>
    /// Writes the two assets of <paramref name="executable"/> into <paramref name="assets"/> and returns their names: a
    /// copy named <see cref="ExecutableName"/>, and the tarball, written by <see cref="ReleaseArchive"/> with the
    /// executable at <c>{rid}/{name}</c> and mode 0755.
    /// </summary>
    public static IReadOnlyList<string> WriteAssets(string executable, string rid, string version, string assets)
    {
        Directory.CreateDirectory(assets);
        var bare = Path.Combine(assets, ExecutableName(rid));
        File.Copy(executable, bare, overwrite: true);

        var staging = Path.Combine(assets, $".staging-{rid}");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        try
        {
            var entry = $"{rid}/{LaunchName(rid)}";
            var staged = Path.Combine(staging, rid, LaunchName(rid));
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(executable, staged);
            // tar.gz on every platform, Windows included, as for the morphir tarballs.
            ReleaseArchive.Create(staging, Path.Combine(assets, ArchiveName(rid, version)), ArchiveFormat.TarGz, ReleaseArchive.ExecutablesFor(entry));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }

        return [ExecutableName(rid), ArchiveName(rid, version)];
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

    /// <summary>
    /// The environment of an artifact check: <c>PATH</c> reduced to the system folders, no <c>DOTNET_*</c> variable, and
    /// only the variables that any process needs (on Windows <c>SystemRoot</c>, <c>windir</c>, <c>TEMP</c> and
    /// <c>TMP</c>; elsewhere <c>HOME</c>, <c>TMPDIR</c> and <c>LANG</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> CheckEnvironment(IReadOnlyDictionary<string, string?> inherited, bool windows)
    {
        string[] kept = windows ? ["SystemRoot", "windir", "TEMP", "TMP", "SystemDrive", "ComSpec"] : ["HOME", "TMPDIR", "LANG"];
        var comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var environment = new Dictionary<string, string>(comparer);
        foreach (var (key, value) in inherited)
            if (value is not null && kept.Contains(key, comparer))
                environment[key] = value;

        if (windows)
        {
            var systemRoot = environment.TryGetValue("SystemRoot", out var root) ? root : @"C:\Windows";
            environment["PATH"] = $@"{systemRoot}\System32;{systemRoot}";
        }
        else
        {
            environment["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin";
        }

        return environment;
    }

    private static IEnumerable<string> Assets(string assets) =>
        Directory.GetFiles(assets)
            .Select(path => Path.GetFileName(path))
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && !name.EndsWith(".sha256", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static bool IsWindows(string rid) => rid.StartsWith("win-", StringComparison.Ordinal);
}
