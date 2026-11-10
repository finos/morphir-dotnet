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

    /// <summary>
    /// The Microsoft.NETCore.App version the host chose: the version folder of the first <c>Chose FX version [..]</c>
    /// line, or null. Earlier lines can name other versions that the host only inspected.
    /// </summary>
    public static string? ChosenFramework(string trace) =>
        FrameworkPattern().Match(trace) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>
    /// True when <paramref name="path"/> is <paramref name="directory"/> or below it. Both are compared in canonical
    /// form (<see cref="CanonicalPath"/>): links resolved, so <c>/var/folders</c> and <c>/private/var/folders</c> agree on
    /// macOS, and Windows short names expanded.
    /// </summary>
    public static bool IsUnder(string path, string directory)
    {
        var full = CanonicalPath.Of(path);
        var root = Path.TrimEndingDirectorySeparator(CanonicalPath.Of(directory)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return full.StartsWith(root, comparison) || string.Equals(full + Path.DirectorySeparatorChar, root, comparison);
    }

    /// <summary>
    /// The default install location the apphost used: from "Using global install location [..]" when it started, or
    /// from the "Default location:" line of its failure message. Null when it never looked there.
    /// </summary>
    public static string? DefaultLocation(string trace)
    {
        if (GlobalLocationPattern().Match(trace) is { Success: true } used) return used.Groups[1].Value;
        return DefaultLocationPattern().Match(trace) is { Success: true } listed ? listed.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// True when <paramref name="output"/> holds .NET host tracing. The host writes its trace to standard error when it
    /// cannot open the trace file, so a missing trace file alone does not prove that no host ran.
    /// </summary>
    public static bool IsTracingOutput(string output) => TracingPattern().IsMatch(output);

    /// <summary>
    /// What contradicts a bundled runtime in <paramref name="trace"/>: the host was not in self-contained mode, it
    /// loaded nothing, or it loaded hostfxr, CoreCLR or a library from outside <paramref name="installationDirectory"/>
    /// or from one of <paramref name="machineRoots"/>. Empty when the start used only the installation.
    /// </summary>
    public static IReadOnlyList<string> BundledRuntimeViolations(string trace, string installationDirectory, IEnumerable<string> machineRoots)
    {
        var problems = new List<string>();
        if (!trace.Contains("Using app-local location", StringComparison.Ordinal))
            problems.Add("the host did not use an app-local runtime location (no \"Using app-local location\" line)");
        var loaded = LoadedRuntimePaths(trace);
        if (loaded.Count == 0)
            problems.Add("the trace names no hostfxr, CoreCLR or loaded library");
        foreach (var path in loaded.Where(path => !IsUnder(path, installationDirectory)))
            problems.Add($"{path} is outside the installation {installationDirectory}");
        foreach (var root in machineRoots)
            foreach (var path in loaded.Where(path => IsUnder(path, root)))
                problems.Add($"{path} is under the machine runtime {root}");
        return problems.Distinct(StringComparer.Ordinal).ToList();
    }

    [GeneratedRegex(@"Using global install location \[(.+?)\] as runtime location")]
    private static partial Regex GlobalLocationPattern();

    [GeneratedRegex(@"Default location:\s*\r?\n\s*(\S.*)")]
    private static partial Regex DefaultLocationPattern();

    [GeneratedRegex(@"^(Tracing enabled @ |--- Invoked (apphost|dotnet|hostfxr|hostpolicy))", RegexOptions.Multiline)]
    private static partial Regex TracingPattern();
    [GeneratedRegex(@"Resolved fxr \[(.+?)\]")]
    private static partial Regex ResolvedFxrPattern();

    [GeneratedRegex(@"CoreCLR path exists=\[(.+?)\]")]
    private static partial Regex CoreClrPattern();

    [GeneratedRegex(@"Loaded library from (.+)")]
    private static partial Regex LoadedLibraryPattern();

    [GeneratedRegex(@"Chose FX version \[[^\]]*?[\\/]shared[\\/]Microsoft\.NETCore\.App[\\/](\d+\.\d+\.\d+[^\\/\]\s]*)[\\/]?\]")]
    private static partial Regex FrameworkPattern();
}
