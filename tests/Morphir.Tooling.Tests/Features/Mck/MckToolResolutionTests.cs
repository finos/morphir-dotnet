// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Mck;

namespace Morphir.Tooling.Tests.Features.Mck;

/// <summary>
/// MCK-3: <c>morphir mck</c> finds <c>morphir-mck</c> through an absolute <c>MORPHIR_MCK</c>, or else through the
/// absolute folders of PATH, in order. Relative and empty PATH entries are skipped, the current directory is never
/// searched, and on Windows only an <c>.exe</c> is a candidate, because anything else would need a shell.
/// </summary>
[Category("PlatformSpecific")]
public class MckToolResolutionTests
{
    private static readonly string Suffix = OperatingSystem.IsWindows() ? ".exe" : "";

    /// <summary>Writes an empty file (on Unix with the given mode) and returns its absolute path.</summary>
    private static string File(string directory, string name, bool executable = true)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        System.IO.File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows())
            System.IO.File.SetUnixFileMode(path, executable
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }

    private static string Tool(string directory) => File(directory, "morphir-mck" + Suffix);

    private static string PathOf(params string[] entries) => string.Join(Path.PathSeparator, entries);

    [Test]
    public void TheFirstAbsolutePathFolderWithTheToolWins()
    {
        using var sandbox = TestSandbox.Create("morphir-mck-resolve-");
        Directory.CreateDirectory(sandbox.PathOf("empty"));
        var first = Tool(sandbox.PathOf("first"));
        Tool(sandbox.PathOf("second"));

        var resolved = MckTool.TryResolve(null, PathOf(sandbox.PathOf("empty"), sandbox.PathOf("first"), sandbox.PathOf("second")),
            out var executable, out var error);

        resolved.Should().BeTrue(error);
        executable.Should().Be(first);
    }

    [Test]
    public void RelativeAndEmptyPathEntriesAreSkipped()
    {
        using var sandbox = TestSandbox.Create("morphir-mck-resolve-");
        Tool(sandbox.PathOf("tools"));
        // A relative entry that names the tool folder when read against the current directory.
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, sandbox.PathOf("tools"));
        if (Path.IsPathFullyQualified(relative)) Skip.Test("The sandbox is on another drive than the current directory.");

        var resolved = MckTool.TryResolve(null, PathOf(".", "", relative), out var executable, out var error);

        resolved.Should().BeFalse($"no absolute PATH folder holds the tool, but {executable} was chosen");
        error.Should().Contain("morphir-mck").And.Contain("MORPHIR_MCK").And.Contain("PATH");
    }

    [Test]
    public void NoToolAnywhereIsADiagnosticThatSaysWhereToGetIt()
    {
        var resolved = MckTool.TryResolve(null, null, out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("morphir-mck").And.Contain("MORPHIR_MCK").And.Contain("finos/morphir")
            .And.Contain("current directory");
    }

    [Test]
    public void AnAbsoluteMorphirMckWinsOverPath()
    {
        using var sandbox = TestSandbox.Create("morphir-mck-resolve-");
        var named = File(sandbox.PathOf("elsewhere"), "my-mck" + Suffix);
        Tool(sandbox.PathOf("tools"));

        var resolved = MckTool.TryResolve(named, sandbox.PathOf("tools"), out var executable, out var error);

        resolved.Should().BeTrue(error);
        executable.Should().Be(named);
    }

    [Test]
    public void ARelativeMorphirMckIsRefusedNotResolvedAgainstTheCurrentDirectory()
    {
        using var sandbox = TestSandbox.Create("morphir-mck-resolve-");
        Tool(sandbox.PathOf("tools"));

        var resolved = MckTool.TryResolve(Path.Combine("tools", "morphir-mck" + Suffix), sandbox.PathOf("tools"), out _, out var error);

        resolved.Should().BeFalse("a relative MORPHIR_MCK would depend on the current directory");
        error.Should().Contain("MORPHIR_MCK").And.Contain("absolute");
    }

    [Test]
    public void AMorphirMckThatDoesNotExistIsRefused()
    {
        using var sandbox = TestSandbox.Create("morphir-mck-resolve-");
        var missing = sandbox.PathOf("missing", "morphir-mck" + Suffix);

        var resolved = MckTool.TryResolve(missing, null, out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("MORPHIR_MCK").And.Contain(missing);
    }

    [Test]
    public void AnEmptyMorphirMckCountsAsUnset()
    {
        using var sandbox = TestSandbox.Create("morphir-mck-resolve-");
        var tool = Tool(sandbox.PathOf("tools"));

        MckTool.TryResolve("", sandbox.PathOf("tools"), out var executable, out var error).Should().BeTrue(error);
        executable.Should().Be(tool);
    }

    [Test]
    public void OnWindowsAScriptIsNotACandidate()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Scripts need a shell only on Windows.");
        using var sandbox = TestSandbox.Create("morphir-mck-resolve-");
        File(sandbox.PathOf("tools"), "morphir-mck.cmd");
        File(sandbox.PathOf("tools"), "morphir-mck.bat");
        var script = File(sandbox.PathOf("tools"), "morphir-mck.ps1");

        MckTool.TryResolve(null, sandbox.PathOf("tools"), out _, out _).Should().BeFalse("only morphir-mck.exe is a candidate");
        MckTool.TryResolve(script, null, out _, out var error).Should().BeFalse("MORPHIR_MCK must name an .exe on Windows");
        error.Should().Contain(".exe");
    }

    [Test]
    public void OnUnixAFileWithoutAnExecuteBitIsNotACandidate()
    {
        if (OperatingSystem.IsWindows()) Skip.Test("Windows has no execute bit.");
        using var sandbox = TestSandbox.Create("morphir-mck-resolve-");
        var plain = File(sandbox.PathOf("tools"), "morphir-mck", executable: false);

        MckTool.TryResolve(null, sandbox.PathOf("tools"), out _, out _).Should().BeFalse();
        MckTool.TryResolve(plain, null, out _, out var error).Should().BeFalse();
        error.Should().Contain("execute");
    }
}
