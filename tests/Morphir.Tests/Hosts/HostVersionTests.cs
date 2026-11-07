using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Morphir.Extensions;

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
        var start = new ProcessStartInfo(DotnetHost()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, assembly));
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(0);
        ReleaseVersion.TryParse(output.Trim(), out var version, out var failure).Should().BeTrue(failure?.ToString());
        version!.ToString().Should().Be(output.Trim());
    }

    [Test]
    [Arguments("0.0.0-dev")]
    [Arguments("0.4.0-alpha.1+a5f91af10d75bd58027e4ac4173a056e7360d667")]
    public void TheVersionsThatTheBuildWritesParse(string version) =>
        ReleaseVersion.TryParse(version, out _, out _).Should().BeTrue();
}
