// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;

namespace Morphir.TestSupport;

/// <summary>
/// macOS reaches its temporary directory through <c>/var</c> → <c>/private/var</c> (and <c>/tmp</c> →
/// <c>/private/tmp</c>). Bounded capture and configuration transactions refuse a linked ancestor, so a test
/// that passes <see cref="Path.GetTempPath"/> straight through fails on that platform. Point the process
/// temp variables at the physical directory before any test builds a path.
/// </summary>
internal static class PhysicalTempDirectory
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Test processes must rewrite the temp directory before any test builds a path, whichever host launches them.")]
    internal static void UsePhysicalTempDirectory()
    {
        var current = Path.GetTempPath();
        var physical = Resolve(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(Trim(current), physical, PathComparison)) return;

        var directory = physical + Path.DirectorySeparatorChar;
        Environment.SetEnvironmentVariable("TMPDIR", directory);
        Environment.SetEnvironmentVariable("TEMP", directory);
        Environment.SetEnvironmentVariable("TMP", directory);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string Resolve(string full, int depth = 0)
    {
        if (depth > 32) throw new IOException($"Too many links while resolving {full}.");
        var root = Path.GetPathRoot(full) ?? "";
        var current = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        var parts = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < parts.Length; index++)
        {
            var next = Path.Combine(current, parts[index]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (!info.Exists) return Path.Combine([current, .. parts[index..]]);

            if (info.LinkTarget is not null)
            {
                var target = Path.GetFullPath(info.LinkTarget, current);
                return Resolve(Path.Combine([target, .. parts[(index + 1)..]]), depth + 1);
            }

            current = next;
        }

        return current;
    }
}
