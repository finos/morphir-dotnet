// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace Morphir.Build.Helpers;

/// <summary>
/// The checks of the Native AOT <c>morphir</c> after its publish. The publish itself is the gate for trim and AOT
/// warnings (the WarningsAsErrors of src/Morphir/Morphir.csproj). These checks add the closure and the size:
/// no assembly that only an IDE loads is an input of ILC, and the executable is not larger than the limit of
/// .agents/aot-optimization.md.
/// </summary>
public static class NativeAotHostGate
{
    /// <summary>The size limit of the Native AOT <c>morphir</c>, in bytes. .agents/aot-optimization.md has the same value.</summary>
    public const long SizeLimitBytes = 100_000_000;

    /// <summary>The assemblies that only an IDE loads. Roslyn Workspaces and the code fixes are not in a host.</summary>
    public static readonly IReadOnlyList<string> IdeOnlyAssemblies =
    [
        "Microsoft.CodeAnalysis.Workspaces",
        "Microsoft.CodeAnalysis.CSharp.Workspaces",
        "Morphir.Lang.CSharp.CodeFixes",
    ];

    /// <summary>
    /// The names of the assemblies in an ILC response file: each input line (no leading <c>-</c>) and each
    /// <c>-r:</c> reference that names a <c>.dll</c>.
    /// </summary>
    public static IReadOnlyList<string> AssembliesOf(IEnumerable<string> responseLines) =>
    [
        .. responseLines
            .Select(line => line.Trim().Trim('"'))
            .Select(line => line.StartsWith("-r:", StringComparison.Ordinal) ? line[3..].Trim('"') : line.StartsWith('-') ? "" : line)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1])),
    ];

    /// <summary>The problems of a published host. An empty list means that the host passes.</summary>
    public static IReadOnlyList<string> Problems(IEnumerable<string> responseLines, long executableBytes, long limitBytes)
    {
        var problems = AssembliesOf(responseLines)
            .Where(assembly => IdeOnlyAssemblies.Contains(assembly, StringComparer.OrdinalIgnoreCase))
            .Select(assembly => $"The Native AOT morphir has the IDE-only assembly {assembly} in its closure.")
            .ToList();
        if (executableBytes > limitBytes)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture,
                $"The Native AOT morphir is {executableBytes} bytes, which is more than the limit of {limitBytes} bytes."));
        }

        return problems;
    }
}
