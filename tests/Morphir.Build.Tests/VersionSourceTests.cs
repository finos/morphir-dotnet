using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// Tests where a source project's package version comes from. Release builds take it from
/// CHANGELOG.md. An explicit -p:Version or -p:PackageVersion wins, so local and test builds
/// can pack a throwaway version into an isolated feed.
/// </summary>
public class VersionSourceTests
{
    private const string OverrideVersion = "0.0.0-versionsource";

    [Test]
    [Arguments("src/Morphir.Core/Morphir.Core.csproj")]
    [Arguments("src/Morphir.SDK.CSharp/Morphir.SDK.CSharp.csproj")]
    public async Task PackageVersion_WithoutOverride_IsChangelogVersion(string project)
    {
        var version = ResolvePackageVersion(project);

        version.Should().Be(GetChangelogVersion());
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("src/Morphir.Core/Morphir.Core.csproj", "PackageVersion")]
    [Arguments("src/Morphir.Core/Morphir.Core.csproj", "Version")]
    [Arguments("src/Morphir.SDK.CSharp/Morphir.SDK.CSharp.csproj", "PackageVersion")]
    [Arguments("src/Morphir.SDK.CSharp/Morphir.SDK.CSharp.csproj", "Version")]
    public async Task PackageVersion_WithExplicitOverride_IsOverride(string project, string property)
    {
        var version = ResolvePackageVersion(project, $"-p:{property}={OverrideVersion}");

        version.Should().Be(OverrideVersion);
        await Task.CompletedTask;
    }

    private static string ResolvePackageVersion(string project, params string[] extraArguments)
    {
        var root = TestFixture.GetRepositoryRoot();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in new[]
                 {
                     "msbuild", Path.Combine(root, project), "-t:SetVersionFromChangelog",
                     "-getProperty:PackageVersion", "-nologo", "-nodeReuse:false",
                 }.Concat(extraArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, $"msbuild should evaluate {project}: {stdout} {stderr.Result}");
        return stdout;
    }

    private static string GetChangelogVersion()
    {
        var changelogPath = Path.Combine(TestFixture.GetRepositoryRoot(), "CHANGELOG.md");
        var match = File.ReadLines(changelogPath)
            .Select(line => Regex.Match(line, @"^## \[(\d+\.\d+\.\d+[^\]]*)\]"))
            .First(m => m.Success);

        return match.Groups[1].Value;
    }
}
