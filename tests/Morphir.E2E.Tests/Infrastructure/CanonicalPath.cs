// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// The canonical form of a path: absolute, every existing link resolved (so <c>/var</c> on macOS becomes
/// <c>/private/var</c>), and on Windows every existing component spelled as the file system stores it (so a short
/// name such as <c>RUNNER~1</c> becomes the long name). A tail that does not exist is kept as given.
/// </summary>
public static class CanonicalPath
{
    public static string Of(string path) => Resolve(Path.GetFullPath(path), depth: 0);

    private static string Resolve(string full, int depth)
    {
        if (depth > 32) throw new IOException($"Too many links while resolving {full}.");
        var root = Path.GetPathRoot(full) ?? "";
        var current = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        var parts = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < parts.Length; index++)
        {
            var next = Path.Combine(current, parts[index]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (!info.Exists)
                return Path.Combine([current, .. parts[index..]]);

            if (info.LinkTarget is not null)
            {
                // A link: continue from its target, then append the rest of the path.
                var target = Path.GetFullPath(info.LinkTarget, current);
                return Resolve(Path.Combine([target, .. parts[(index + 1)..]]), depth + 1);
            }

            if (OperatingSystem.IsWindows())
            {
                // The entry as stored: a short name or another case resolves to the long name.
                var stored = new DirectoryInfo(current).EnumerateFileSystemInfos(parts[index]).FirstOrDefault();
                if (stored is not null) next = Path.Combine(current, stored.Name);
            }

            current = next;
        }

        return current;
    }
}
