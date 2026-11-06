// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;

namespace Morphir.TestSupport;

/// <summary>
/// Detects tests that escape their sandbox. Capture the real machine's Morphir locations before a test run
/// and call <see cref="AssertUnchanged"/> after it: anything created, modified or removed under those
/// locations during the run is reported by path.
/// </summary>
public sealed class MachineTripwire
{
    private readonly IReadOnlyDictionary<string, Snapshot> _before;

    private MachineTripwire(IReadOnlyList<string> roots)
    {
        Roots = roots;
        _before = roots.ToDictionary(root => root, Take, PathComparer);
    }

    public IReadOnlyList<string> Roots { get; }

    /// <summary>Snapshots <paramref name="roots"/>, or the real machine's Morphir locations when omitted.</summary>
    public static MachineTripwire Capture(IEnumerable<string>? roots = null) =>
        new((roots ?? DefaultRoots()).Distinct(PathComparer).ToArray());

    /// <summary>
    /// Every location the product reads or writes on a real machine: the Morphir home, the platform
    /// configuration and cache directories, and the system configuration directory. Resolved from the
    /// real environment, so capture this before anything could redirect it.
    /// </summary>
    public static IReadOnlyList<string> DefaultRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string> { Path.Combine(home, ".morphir") };
        if (Environment.GetEnvironmentVariable("MORPHIR_HOME") is { Length: > 0 } morphirHome && Path.IsPathRooted(morphirHome))
            roots.Add(morphirHome);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Morphir"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Morphir"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "morphir"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            roots.Add(Path.Combine(home, "Library", "Application Support", "morphir"));
            roots.Add(Path.Combine(home, "Library", "Caches", "morphir"));
        }
        else
        {
            roots.Add(Path.Combine(Variable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"), "morphir"));
            roots.Add(Path.Combine(Variable("XDG_CACHE_HOME") ?? Path.Combine(home, ".cache"), "morphir"));
            roots.Add("/etc/morphir");
        }

        return roots.Distinct(PathComparer).ToArray();
    }

    /// <summary>One line per created, modified or removed entry, each naming its full path.</summary>
    public IReadOnlyList<string> Changes()
    {
        var changes = new List<string>();
        foreach (var root in Roots)
        {
            var before = _before[root];
            var after = Take(root);
            if (before.Exists != after.Exists)
            {
                changes.Add($"{(after.Exists ? "created" : "removed")} {root}");
                if (!after.Exists) continue;
            }

            foreach (var (path, entry) in after.Entries)
            {
                if (!before.Entries.TryGetValue(path, out var previous)) changes.Add($"created {path}");
                else if (previous != entry) changes.Add($"modified {path}");
            }

            changes.AddRange(before.Entries.Keys.Where(path => !after.Entries.ContainsKey(path)).Select(path => $"removed {path}"));
        }

        return changes;
    }

    /// <summary>Throws, listing every change, when the run touched a watched location.</summary>
    public void AssertUnchanged()
    {
        var changes = Changes();
        if (changes.Count > 0)
        {
            throw new InvalidOperationException(
                "Tests changed the real machine's Morphir locations; a test escaped its sandbox:" +
                Environment.NewLine + string.Join(Environment.NewLine, changes.Select(change => "  " + change)));
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string? Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && Path.IsPathRooted(value) ? value : null;

    private static Snapshot Take(string root)
    {
        if (!Directory.Exists(root)) return new Snapshot(false, new Dictionary<string, Entry>(PathComparer));

        var entries = new Dictionary<string, Entry>(PathComparer);
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            }))
            {
                var info = new FileInfo(path);
                entries[path] = info.Attributes.HasFlag(FileAttributes.Directory)
                    ? new Entry(IsDirectory: true, Length: 0, LastWriteUtc: default)
                    : new Entry(IsDirectory: false, info.Length, info.LastWriteTimeUtc);
            }
        }
        catch (IOException)
        {
            // An entry vanished while enumerating; compare what was seen.
        }

        return new Snapshot(true, entries);
    }

    private sealed record Snapshot(bool Exists, IReadOnlyDictionary<string, Entry> Entries);

    private sealed record Entry(bool IsDirectory, long Length, DateTime LastWriteUtc);
}
