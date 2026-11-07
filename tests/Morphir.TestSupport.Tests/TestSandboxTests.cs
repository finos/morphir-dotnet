// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using FluentAssertions;

namespace Morphir.TestSupport.Tests;

public class TestSandboxTests
{
    private static ProcessStartInfo PrintEnvironment() =>
        OperatingSystem.IsWindows()
            ? ChildProcess.StartInfo("cmd.exe", "/c", "set")
            : ChildProcess.StartInfo("/usr/bin/env");

    private static async Task<Dictionary<string, string>> ChildEnvironment(ProcessStartInfo info)
    {
        var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(30));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains('='))
            .Select(line => (Key: line[..line.IndexOf('=')], Value: line[(line.IndexOf('=') + 1)..]))
            .GroupBy(pair => pair.Key, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    [Test]
    public async Task Apply_ContainsMorphirAndMachineLocations()
    {
        using var sandbox = TestSandbox.Create();
        var info = PrintEnvironment();
        info.Environment["MORPHIR_PROFILES"] = "inherited"; // as if inherited from the developer's shell
        info.Environment["MORPHIR_IR__FORMAT_VERSION"] = "4";
        sandbox.Apply(info);

        var environment = await ChildEnvironment(info);

        environment["HOME"].Should().Be(sandbox.Home);
        environment["USERPROFILE"].Should().Be(sandbox.Home);
        environment["APPDATA"].Should().Be(sandbox.AppData);
        environment["LOCALAPPDATA"].Should().Be(sandbox.LocalAppData);
        environment["PROGRAMDATA"].Should().Be(sandbox.ProgramData);
        environment["XDG_CONFIG_HOME"].Should().Be(sandbox.XdgConfigHome);
        environment["XDG_CACHE_HOME"].Should().Be(sandbox.XdgCacheHome);
        environment["MORPHIR_HOME"].Should().Be(sandbox.MorphirHome);
        environment["MORPHIR_DISCOVERY_BOUNDARY"].Should().Be(sandbox.Root);
        environment["MORPHIR_ISOLATED"].Should().Be("1");
        environment["DOTNET_CLI_HOME"].Should().Be(sandbox.DotnetCliHome);
        environment["MSBUILDDISABLENODEREUSE"].Should().Be("1");
        environment.Keys.Where(key => key.StartsWith("MORPHIR_", StringComparison.OrdinalIgnoreCase))
            .Should().BeEquivalentTo(["MORPHIR_HOME", "MORPHIR_DISCOVERY_BOUNDARY", "MORPHIR_ISOLATED"],
                "inherited MORPHIR_* variables are removed");
    }

    [Test]
    public async Task Apply_KeepsTheRealNuGetPackagesFolder()
    {
        using var sandbox = TestSandbox.Create();
        var info = PrintEnvironment();
        sandbox.Apply(info);

        var environment = await ChildEnvironment(info);

        environment["NUGET_PACKAGES"].Should().Be(TestSandbox.RealNuGetPackagesFolder);
        TestSandbox.RealNuGetPackagesFolder.Should().NotStartWith(sandbox.Root);
    }

    [Test]
    public async Task Apply_WithoutIsolation_LeavesIsolationUnset()
    {
        using var sandbox = TestSandbox.Create();
        var info = PrintEnvironment();
        info.Environment["MORPHIR_ISOLATED"] = "1";
        sandbox.Apply(info, new SandboxOptions { Isolated = false });

        var environment = await ChildEnvironment(info);

        environment.Should().NotContainKey("MORPHIR_ISOLATED");
        environment["MORPHIR_HOME"].Should().Be(sandbox.MorphirHome);
    }

    [Test]
    public async Task Apply_WithoutHomeRedirect_KeepsMachineLocations()
    {
        using var sandbox = TestSandbox.Create();
        var info = PrintEnvironment();
        sandbox.Apply(info, new SandboxOptions { RedirectHome = false });

        var environment = await ChildEnvironment(info);

        environment.GetValueOrDefault(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME")
            .Should().Be(Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME"));
        environment["MORPHIR_HOME"].Should().Be(sandbox.MorphirHome, "Morphir stays contained either way");
        environment["MORPHIR_DISCOVERY_BOUNDARY"].Should().Be(sandbox.Root);
    }

    [Test]
    public async Task DotnetRunsInsideTheSandbox()
    {
        using var sandbox = TestSandbox.Create();
        var info = ChildProcess.StartInfo("dotnet", "--version");
        info.WorkingDirectory = sandbox.Work;
        sandbox.Apply(info);

        var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(60));

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.StandardOutput.Trim().Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Test]
    public async Task EnvironmentSnapshot_ReplacesInheritedMorphirVariables()
    {
        using var sandbox = TestSandbox.Create();
        var inherited = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["MORPHIR_PROFILES"] = "inherited",
            ["MORPHIR_HOME"] = "/real/home/.morphir",
        };

        var environment = sandbox.EnvironmentSnapshot(inherited, new SandboxOptions { Isolated = false });

        environment["PATH"].Should().Be("/usr/bin");
        environment["MORPHIR_HOME"].Should().Be(sandbox.MorphirHome);
        environment["MORPHIR_DISCOVERY_BOUNDARY"].Should().Be(sandbox.Root);
        environment.Should().NotContainKey("MORPHIR_PROFILES").And.NotContainKey("MORPHIR_ISOLATED");
        inherited["MORPHIR_HOME"].Should().Be("/real/home/.morphir", "the input is not mutated");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Dispose_RemovesTheSandbox()
    {
        var sandbox = TestSandbox.Create();
        await File.WriteAllTextAsync(Path.Combine(sandbox.Work, "file.txt"), "x");

        sandbox.Dispose();

        Directory.Exists(sandbox.Root).Should().BeFalse();
    }

    [Test]
    public async Task Layout_IsInsideTheRoot()
    {
        using var sandbox = TestSandbox.Create();

        new[] { sandbox.Home, sandbox.AppData, sandbox.LocalAppData, sandbox.ProgramData, sandbox.XdgConfigHome,
                sandbox.XdgCacheHome, sandbox.DotnetCliHome, sandbox.Work }
            .Should().OnlyContain(path => path.StartsWith(sandbox.Root) && Directory.Exists(path));
        sandbox.MorphirHome.Should().Be(Path.Combine(sandbox.Home, ".morphir"));
        Directory.Exists(sandbox.MorphirHome).Should().BeFalse("tests decide whether a global home exists");
        sandbox.PathOf("work", "app").Should().Be(Path.Combine(sandbox.Root, "work", "app"));
        await Task.CompletedTask;
    }
}
