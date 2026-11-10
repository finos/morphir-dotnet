// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The release assets of the morphirup manager: the bare executable and a tarball with it under <c>{rid}/</c>, on the
/// platforms that releases build, named like the <c>morphir</c> assets, and a checksum file over all of them.
/// </summary>
public class MorphirupReleaseTests
{
    [Test]
    public void TheAssetsAreNamedLikeTheMorphirAssets()
    {
        MorphirupRelease.ExecutableName("win-x64").Should().Be("morphirup-win-x64.exe");
        MorphirupRelease.ExecutableName("linux-arm64").Should().Be("morphirup-linux-arm64");
        MorphirupRelease.ArchiveName("osx-arm64", "0.4.0-alpha.4").Should().Be("morphirup-osx-arm64-v0.4.0-alpha.4.tar.gz");
        MorphirupRelease.ChecksumFileName("0.4.0-alpha.4").Should().Be("morphirup-v0.4.0-alpha.4.sha256");
        MorphirupRelease.LaunchName("win-x64").Should().Be("morphirup.exe");
        MorphirupRelease.LaunchName("linux-x64").Should().Be("morphirup");
    }

    [Test]
    public void TheMorphirAndMorphirupNamePatternsDoNotOverlap()
    {
        // deployment.yml downloads and attaches by these prefixes; neither may pick up the other's assets.
        foreach (var rid in MorphirupRelease.Platforms)
        {
            MorphirupRelease.ExecutableName(rid).Should().NotStartWith("morphir-");
            MorphirupRelease.ArchiveName(rid, "1.2.3").Should().NotStartWith("morphir-");
            LegacyAsset.TrimmedSingleFile.ExecutableName(rid).Should().NotStartWith("morphirup-");
            LegacyAsset.TrimmedSingleFile.TarballName(rid, "1.2.3").Should().NotStartWith("morphirup-");
        }

        MorphirupRelease.ChecksumFileName("1.2.3").Should().StartWith("morphirup-");
    }

    [Test]
    public void ThePlatformsAreTheOnesTheReleaseWorkflowBuilds()
    {
        MorphirupRelease.Platforms.Should().Equal("linux-x64", "linux-arm64", "win-x64", "osx-arm64");
        string Workflow(string name) => File.ReadAllText(Path.Combine(PathHelper.GetRepositoryRoot(), ".github", "workflows", name));

        // The morphir executables (block style) and the morphirup matrices (flow style) list the same platforms and runners.
        var morphir = Regex.Matches(Workflow("deployment.yml"), @"^\s*- rid: (\S+)\s*\n\s*os: (\S+)\s*$", RegexOptions.Multiline)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value)).ToList();
        morphir.Select(pair => pair.Item1).Should().Equal(MorphirupRelease.Platforms);
        foreach (var workflow in new[] { "deployment.yml", "morphirup.yml" })
        {
            var morphirup = Regex.Matches(Workflow(workflow), @"^\s*- \{ rid: (\S+), os: (\S+) \}\s*$", RegexOptions.Multiline)
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value)).ToList();
            morphirup.Should().Equal(morphir, $"{workflow} builds morphirup on the platforms and runners that morphir is released on");
        }
    }

    [Test]
    public void TheReleaseJobAttachesAndChecksTheMorphirupAssets()
    {
        var workflow = File.ReadAllText(Path.Combine(PathHelper.GetRepositoryRoot(), ".github", "workflows", "deployment.yml"));
        workflow.Should().Contain("uses: ./.github/workflows/morphirup-build.yml", "the release builds morphirup with the pull request's definition");
        workflow.Should().Contain("pattern: morphirup-*");
        workflow.Should().Contain("./artifacts/release-assets/morphirup-*");
        workflow.Should().Contain("--target MorphirupChecksums");
        workflow.Should().Contain("pattern: morphir-*", "the morphir executables keep their own download");
    }

    [Test]
    [Arguments("linux-x64")]
    [Arguments("win-x64")]
    public async Task TheArchiveHoldsTheExecutableUnderTheRidFolder(string rid)
    {
        await using var sandbox = TestSandbox.Create("morphirup-release-");
        var executable = sandbox.PathOf("published", MorphirupRelease.LaunchName(rid));
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllBytesAsync(executable, [1, 2, 3, 4]);
        var assets = Directory.CreateDirectory(sandbox.PathOf("release assets")).FullName;

        var written = MorphirupRelease.WriteAssets(executable, rid, "1.2.3", assets);

        written.Should().Equal(MorphirupRelease.ExecutableName(rid), MorphirupRelease.ArchiveName(rid, "1.2.3"));
        (await File.ReadAllBytesAsync(Path.Combine(assets, MorphirupRelease.ExecutableName(rid)))).Should().Equal(1, 2, 3, 4);

        await using var gzip = new GZipStream(File.OpenRead(Path.Combine(assets, MorphirupRelease.ArchiveName(rid, "1.2.3"))), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var entry = tar.GetNextEntry();
        entry!.Name.Should().Be($"{rid}/{MorphirupRelease.LaunchName(rid)}");
        entry.Mode.Should().Be(ReleaseArchive.ExecutableMode);
        using var content = new MemoryStream();
        await entry.DataStream!.CopyToAsync(content);
        content.ToArray().Should().Equal(1, 2, 3, 4);
        tar.GetNextEntry().Should().BeNull("the archive holds only the executable");
    }

    [Test]
    public async Task TheChecksumFileListsEveryMorphirupAssetAndOnlyThose()
    {
        await using var sandbox = TestSandbox.Create("morphirup-release-");
        var assets = Directory.CreateDirectory(sandbox.PathOf("assets")).FullName;
        await File.WriteAllTextAsync(Path.Combine(assets, "morphirup-win-x64.exe"), "windows");
        await File.WriteAllTextAsync(Path.Combine(assets, "morphirup-linux-x64"), "linux");
        await File.WriteAllTextAsync(Path.Combine(assets, "morphirup-linux-x64-v1.2.3.tar.gz"), "archive");
        await File.WriteAllTextAsync(Path.Combine(assets, "morphir-linux-x64"), "not morphirup");

        var path = MorphirupRelease.WriteChecksums(assets, "1.2.3");

        Path.GetFileName(path).Should().Be("morphirup-v1.2.3.sha256");
        var lines = (await File.ReadAllTextAsync(path)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // sha256sum format: the lowercase digest, two spaces, the file name; sorted by name with ordinal comparison.
        lines.Should().Equal(
            $"{ReleaseArchive.Sha256(Path.Combine(assets, "morphirup-linux-x64"))}  morphirup-linux-x64",
            $"{ReleaseArchive.Sha256(Path.Combine(assets, "morphirup-linux-x64-v1.2.3.tar.gz"))}  morphirup-linux-x64-v1.2.3.tar.gz",
            $"{ReleaseArchive.Sha256(Path.Combine(assets, "morphirup-win-x64.exe"))}  morphirup-win-x64.exe");
        MorphirupRelease.VerifyChecksums(assets, path).Should().BeEmpty();
    }

    [Test]
    public async Task VerificationFindsAChangedAMissingAndAnUnlistedAsset()
    {
        await using var sandbox = TestSandbox.Create("morphirup-release-");
        var assets = Directory.CreateDirectory(sandbox.PathOf("assets")).FullName;
        await File.WriteAllTextAsync(Path.Combine(assets, "morphirup-linux-x64"), "linux");
        await File.WriteAllTextAsync(Path.Combine(assets, "morphirup-win-x64.exe"), "windows");
        var path = MorphirupRelease.WriteChecksums(assets, "1.2.3");

        await File.WriteAllTextAsync(Path.Combine(assets, "morphirup-linux-x64"), "changed");
        File.Delete(Path.Combine(assets, "morphirup-win-x64.exe"));
        await File.WriteAllTextAsync(Path.Combine(assets, "morphirup-osx-arm64"), "unlisted");

        var problems = MorphirupRelease.VerifyChecksums(assets, path);
        problems.Should().HaveCount(3);
        problems.Should().Contain(problem => problem.Contains("morphirup-linux-x64") && problem.Contains("SHA-256"));
        problems.Should().Contain(problem => problem.Contains("morphirup-win-x64.exe") && problem.Contains("missing"));
        problems.Should().Contain(problem => problem.Contains("morphirup-osx-arm64") && problem.Contains("not listed"));
    }

    [Test]
    public void ACheckRunsWithOnlyTheSystemFoldersOnPathAndNoDotnetVariables()
    {
        var environment = MorphirupRelease.CheckEnvironment(new Dictionary<string, string?>
        {
            ["PATH"] = "C:\\dotnet;C:\\Windows\\System32",
            ["DOTNET_ROOT"] = "C:\\dotnet",
            ["DOTNET_HOST_PATH"] = "C:\\dotnet\\dotnet.exe",
            ["SystemRoot"] = "C:\\Windows",
            ["TEMP"] = "C:\\Temp",
            ["NUGET_PACKAGES"] = "C:\\nuget",
        }, windows: true);

        environment.Keys.Should().NotContain(key => key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase));
        environment["PATH"].Should().Be("C:\\Windows\\System32;C:\\Windows");
        environment.Should().ContainKey("SystemRoot").And.ContainKey("TEMP");
        environment.Should().NotContainKey("NUGET_PACKAGES", "only what any process needs is passed on");

        MorphirupRelease.CheckEnvironment(new Dictionary<string, string?> { ["PATH"] = "/usr/share/dotnet:/usr/bin", ["HOME"] = "/home/runner" }, windows: false)["PATH"]
            .Should().Be("/usr/bin:/bin:/usr/sbin:/sbin");
    }
}
