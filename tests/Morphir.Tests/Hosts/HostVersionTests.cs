using System.Runtime.InteropServices;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.TestSupport;

namespace Morphir.Tests.Hosts;

/// <summary>
/// Each host starts by parsing its build version with <c>ReleaseVersion.Create</c>, which throws on a version that is
/// not canonical SemVer. These tests run <c>--version</c> on both built hosts and parse the output with the same parser.
/// </summary>
public sealed class HostVersionTests
{
    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
        ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    [Test]
    [Arguments("morphir.dll")]
    [Arguments("dotnet-morphir.dll")]
    public async Task VersionOutputParsesAsAReleaseVersion(string assembly)
    {
        // Every child goes through the shared runner (gate, timeout, process-tree cleanup) and a sandbox.
        var start = ChildProcess.StartInfo(DotnetHost(), Path.Combine(AppContext.BaseDirectory, assembly), "--version");
        await using var sandbox = TestSandbox.Create("morphir-host-version-");
        start.WorkingDirectory = sandbox.Work;
        sandbox.Apply(start);
        var result = await ChildProcess.RunAsync(start, TimeSpan.FromSeconds(60));

        result.ExitCode.Should().Be(0, result.StandardError);
        var output = result.StandardOutput.Trim();
        ReleaseVersion.TryParse(output, out var version, out var failure).Should().BeTrue(failure?.ToString());
        version!.ToString().Should().Be(output);
    }

    [Test]
    [Arguments("0.0.0-dev")]
    [Arguments("0.4.0-alpha.1+a5f91af10d75bd58027e4ac4173a056e7360d667")]
    public void TheVersionsThatTheBuildWritesParse(string version) =>
        ReleaseVersion.TryParse(version, out _, out _).Should().BeTrue();
}
