// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// Reads a .NET host trace (<c>COREHOST_TRACE=1</c>, <c>COREHOST_TRACEFILE</c>): which hostfxr and CoreCLR the apphost
/// loaded and which shared framework version it chose. A Native AOT executable runs no host and writes no trace.
/// </summary>
public static partial class HostTrace
{
    /// <summary>The hostfxr path the host resolved, or null.</summary>
    public static string? ResolvedFxr(string trace) => ResolvedFxrPattern().Match(trace) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>Every hostfxr, CoreCLR and library path the trace says the host resolved or loaded.</summary>
    public static IReadOnlyList<string> LoadedRuntimePaths(string trace) =>
        [.. ResolvedFxrPattern().Matches(trace).Select(match => match.Groups[1].Value)
            .Concat(CoreClrPattern().Matches(trace).Select(match => match.Groups[1].Value))
            .Concat(LoadedLibraryPattern().Matches(trace).Select(match => match.Groups[1].Value.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The Microsoft.NETCore.App version the host chose, or null.</summary>
    public static string? ChosenFramework(string trace) =>
        FrameworkPattern().Match(trace) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>True when <paramref name="path"/> is <paramref name="directory"/> or below it.</summary>
    public static bool IsUnder(string path, string directory)
    {
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return full.StartsWith(root, comparison) || string.Equals(full + Path.DirectorySeparatorChar, root, comparison);
    }

    [GeneratedRegex(@"Resolved fxr \[(.+?)\]")]
    private static partial Regex ResolvedFxrPattern();

    [GeneratedRegex(@"CoreCLR path exists=\[(.+?)\]")]
    private static partial Regex CoreClrPattern();

    [GeneratedRegex(@"Loaded library from (.+)")]
    private static partial Regex LoadedLibraryPattern();

    [GeneratedRegex(@"shared[\\/]Microsoft\.NETCore\.App[\\/](\d+\.\d+\.\d+[^\\/\]\s]*)")]
    private static partial Regex FrameworkPattern();
}
