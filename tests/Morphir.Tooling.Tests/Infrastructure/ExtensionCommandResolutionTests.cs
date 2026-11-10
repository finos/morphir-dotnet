// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Infrastructure;

/// <summary>
/// A bare extension command name is searched only on PATH, never in the working directory or the host's own
/// directory, as process start would do on Windows (CreateProcess) and in .NET on Unix. The resolved absolute path
/// is what starts. A decoy that a checkout plants in the working directory is never chosen.
/// </summary>
public class ExtensionCommandResolutionTests
{
    private static readonly string Suffix = OperatingSystem.IsWindows() ? ".exe" : "";

    /// <summary>Writes an empty executable file (on Unix with the execute bits) and returns its path.</summary>
    private static string Executable(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + Suffix);
        File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static (bool Resolved, string Executable, string[] Arguments, string? Error) Resolve(
        string extension, string workingDirectory, string? searchPath, string? dotnetHost = null)
    {
        var resolved = MepExtensionCommand.TryResolve(extension, workingDirectory, searchPath, ".COM;.EXE;.BAT;.CMD", dotnetHost,
            out var executable, out var arguments, out var error);
        return (resolved, executable, arguments, error);
    }

    [Test]
    public async Task ABareNameIsTheAbsolutePathOnPathNotADecoyInTheWorkingDirectory()
    {
        using var sandbox = TestSandbox.Create("morphir-resolve-");
        var work = sandbox.PathOf("checkout");
        Executable(work, "morphir-fsharp-backend");
        var installed = Executable(sandbox.PathOf("tools"), "morphir-fsharp-backend");

        var resolved = Resolve("morphir-fsharp-backend", work, sandbox.PathOf("tools"));

        resolved.Resolved.Should().BeTrue(resolved.Error);
        resolved.Executable.Should().Be(installed);
        resolved.Arguments.Should().BeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task ABareNameOnlyInTheWorkingDirectoryIsNotFound()
    {
        using var sandbox = TestSandbox.Create("morphir-resolve-");
        var work = sandbox.PathOf("checkout");
        Executable(work, "morphir-fsharp-backend");
        Directory.CreateDirectory(sandbox.PathOf("empty"));

        var resolved = Resolve("morphir-fsharp-backend", work, sandbox.PathOf("empty"));

        resolved.Resolved.Should().BeFalse();
        resolved.Error.Should().Contain("morphir-fsharp-backend").And.Contain("PATH");
        await Task.CompletedTask;
    }

    [Test]
    public async Task RelativePathEntriesAreNotSearched()
    {
        using var sandbox = TestSandbox.Create("morphir-resolve-");
        var work = sandbox.PathOf("checkout");
        Executable(work, "guest");

        // "." and "checkout" would point into the working directory of a process start.
        var resolved = Resolve("guest", work, string.Join(Path.PathSeparator, ".", "checkout", ""));

        resolved.Resolved.Should().BeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task TheFirstPathEntryWithTheCommandWins()
    {
        using var sandbox = TestSandbox.Create("morphir-resolve-");
        var first = Executable(sandbox.PathOf("first"), "guest");
        Executable(sandbox.PathOf("second"), "guest");

        var resolved = Resolve("guest", sandbox.PathOf("checkout"),
            string.Join(Path.PathSeparator, sandbox.PathOf("missing"), sandbox.PathOf("first"), sandbox.PathOf("second")));

        resolved.Executable.Should().Be(first);
        await Task.CompletedTask;
    }

    [Test]
    public async Task APathWithADirectoryIsRelativeToTheWorkingDirectory()
    {
        using var sandbox = TestSandbox.Create("morphir-resolve-");
        var work = sandbox.PathOf("checkout");

        var resolved = Resolve(Path.Combine("tools", "guest"), work, null);

        resolved.Resolved.Should().BeTrue(resolved.Error);
        resolved.Executable.Should().Be(Path.GetFullPath(Path.Combine(work, "tools", "guest")));
        await Task.CompletedTask;
    }

    [Test]
    public async Task ADllRunsOnTheDotnetOnPathWhenNoHostIsGivenNotADecoyDotnet()
    {
        using var sandbox = TestSandbox.Create("morphir-resolve-");
        var work = sandbox.PathOf("checkout");
        Executable(work, "dotnet");
        var dotnet = Executable(sandbox.PathOf("sdk"), "dotnet");
        var dll = Path.Combine(work, "guest.dll");

        var resolved = Resolve(dll, work, sandbox.PathOf("sdk"));

        resolved.Executable.Should().Be(dotnet);
        resolved.Arguments.Should().Equal(dll);

        var hosted = Resolve(dll, work, sandbox.PathOf("sdk"), dotnetHost: "/opt/dotnet/dotnet");
        hosted.Executable.Should().Be("/opt/dotnet/dotnet");
        await Task.CompletedTask;
    }
}
