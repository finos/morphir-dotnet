// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The test infrastructure starts no PowerShell of its own. A child that a test needs is the process helper
/// (tests/Fixtures/process-helper/process-helper.cs) or a program of the repository: Windows PowerShell 5.1 spends
/// more than a minute on its first start on a fresh CI image ("Preparing modules for first use"), and such a test then
/// measures that start and not the code under test. A script of the build or of CI is tested in PowerShell, with
/// Pester (tests/Scripts). The one other exception is a delivered install script: users run install-morphirup.ps1 in
/// PowerShell, and the same end-to-end scenarios run it on Windows and install-morphirup.sh elsewhere, so PowerShell
/// is the runtime of the thing under test there.
/// </summary>
public partial class TestShellRulesTests
{
    /// <summary>The files that may start PowerShell, or name it as a program, with the reason.</summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["build/Build.Testing.cs"] = "the TestScripts target runs the Pester tests of tests/Scripts",
        ["tests/Morphir.TestSupport/Drivers/InstallScriptDriver.cs"] =
            "runs the delivered scripts/install-morphirup.ps1 in PowerShell 7, else Windows PowerShell, as a user does (EndToEnd/InstallScripts.feature)",
        ["tests/Morphir.TestSupport.Tests/Drivers/InstallScriptDriverTests.cs"] =
            "checks the command line of InstallScriptDriver; it starts no process",
    };

    private static readonly string[] Extensions = [".cs", ".fs", ".fsx", ".csproj", ".fsproj", ".props", ".targets"];

    // A program name in a string: "powershell", "powershell.exe", "pwsh" or "pwsh.exe".
    [GeneratedRegex("""["'/\\](powershell|pwsh)(\.exe)?["']""", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellProgram();

    [Test]
    [Arguments("ChildProcess.StartInfo(\"powershell.exe\", \"-Command\", script)")]
    [Arguments("ChildProcess.StartInfo(\"pwsh\", \"-NoProfile\")")]
    [Arguments("Path.Combine(Environment.SystemDirectory, \"WindowsPowerShell\", \"v1.0\", \"powershell.exe\")")]
    public void APowerShellProgramIsFound(string line) => PowerShellProgram().IsMatch(line).Should().BeTrue();

    [Test]
    [Arguments("// The digests were computed with PowerShell's SHA256.")]
    [Arguments("var script = Path.Combine(root, \"scripts\", \"install-morphirup.ps1\");")]
    public void AMentionOfPowerShellIsNotAProgram(string line) => PowerShellProgram().IsMatch(line).Should().BeFalse();

    [Test]
    public void EachAllowedFileExists() =>
        Allowed.Keys.Where(file => !File.Exists(Path.Combine(TestFixture.GetRepositoryRoot(), file))).Should()
            .BeEmpty("an exception of a file that moved or was deleted must go with it");

    [Test]
    public void OnlyTheRunOfThePesterTestsAndTheInstallScriptDriverStartPowerShell()
    {
        var root = TestFixture.GetRepositoryRoot();
        var found = new[] { "tests", "build" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories))
            .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"))
            .Where(file => !Allowed.ContainsKey(file) && file != "tests/Morphir.Build.Tests/Helpers/TestShellRulesTests.cs")
            .SelectMany(file => File.ReadLines(Path.Combine(root, file))
                .Select((text, index) => (file, line: index + 1, text))
                .Where(line => PowerShellProgram().IsMatch(line.text)))
            .Select(line => $"{line.file}:{line.line}: {line.text.Trim()}")
            .ToArray();

        found.Should().BeEmpty(
            "test infrastructure starts the process helper or a program of the repository, not PowerShell; " +
            "a test of a PowerShell script is a Pester test in tests/Scripts");
    }
}
