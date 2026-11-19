// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport;

/// <summary>
/// The process helper of the test support library: a small program of the repository
/// (<c>tests/Fixtures/process-helper/process-helper.cs</c>) that the tests start instead of a shell, so a test of a
/// child process measures the child process and not the start-up of a shell. The build of Morphir.TestSupport builds
/// it ahead of the tests (<c>build/Morphir.ProcessHelper.targets</c>) into the folder <c>process-helper</c> of each
/// test output.
/// </summary>
public static class ProcessHelper
{
    /// <summary>The assembly of the helper in the output of this test process.</summary>
    public static string AssemblyPath { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "process-helper", "process-helper.dll");

    /// <summary>A start info that runs the helper with <c>dotnet exec</c> and <paramref name="arguments"/>.</summary>
    public static ProcessStartInfo StartInfo(params string[] arguments)
    {
        if (!File.Exists(AssemblyPath))
        {
            throw new FileNotFoundException(
                $"The process helper is not in the test output. Build the test project, which builds Morphir.TestSupport and with it the helper (build/Morphir.ProcessHelper.targets).",
                AssemblyPath);
        }

        return ChildProcess.StartInfo(DotnetHost.Path, ["exec", AssemblyPath, .. arguments]);
    }

    /// <summary>Writes <paramref name="size"/> times <c>a</c> to stdout, then <paramref name="size"/> times <c>b</c> to stderr.</summary>
    public static ProcessStartInfo WriteBoth(int size) => StartInfo("write-both", size.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Reads a line and answers <c>got &lt;line&gt;</c>, then reads to the end and writes <c>rest &lt;rest&gt;</c>.</summary>
    public static ProcessStartInfo Converse() => StartInfo("converse");
}
