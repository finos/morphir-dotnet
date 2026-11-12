// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The CLI driver starts an assembly host with the full path of the dotnet host. On Linux the process runner starts
/// each child through setsid, which looks up its program in the PATH of the child, and a scenario can give the
/// product a PATH with no dotnet (the search path of a routing scenario).
/// </summary>
[Category("PlatformSpecific")]
public class DotnetHostTests
{
    [Test]
    public async Task ACommandWithAPathThatHasNoDotnetStartsTheHostByItsFullPath()
    {
        await using var workspace = ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        var empty = workspace.Directory("empty");
        workspace.CreateDirectory(empty);
        var cli = new CliDriver(new TestTarget.DevelopmentBuild(), MorphirHost.Native,
            CliArtifacts.OfDevelopmentBuild(typeof(DotnetHostTests).Assembly).PathOf(MorphirHost.Native), workspace);
        var options = new CliRunOptions { Environment = new Dictionary<string, string?> { ["PATH"] = empty.Path } };

        var result = await cli.RunAsync(["--version"], options);
        var commandLine = await cli.RunCommandLineAsync("--version", options);

        result.ShouldExitWith(0);
        commandLine.ShouldExitWith(0);
        result.CommandLine.Should().StartWith(DotnetHost.Path);
        commandLine.CommandLine.Should().StartWith(DotnetHost.Path);
        Path.IsPathRooted(DotnetHost.Path).Should().BeTrue("the test runs on a dotnet host whose path the process knows");
    }

    [Test]
    [Arguments("/usr/share/dotnet/dotnet", null, "/usr/share/dotnet/dotnet")]
    [Arguments(null, "/opt/dotnet/dotnet", "/opt/dotnet/dotnet")]
    [Arguments(null, @"C:\dotnet\dotnet.exe", @"C:\dotnet\dotnet.exe")]
    [Arguments(null, "/work/bin/Morphir.TestSupport.Tests", "dotnet")]
    [Arguments(null, null, "dotnet")]
    public void TheHostIsTheDotnetHostPathThenTheProcessThenTheName(string? hostPath, string? processPath, string expected) =>
        DotnetHost.Resolve(hostPath, processPath).Should().Be(expected);
}
