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
        MorphirupRelease.NoticesFileName("0.4.0-alpha.4").Should().Be("morphirup-v0.4.0-alpha.4-THIRD-PARTY-NOTICES.txt");
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
    [Category("PlatformSpecific")]
    public void ThePlatformsAreTheOnesTheReleaseWorkflowBuilds()
    {
        MorphirupRelease.Platforms.Should().Equal("linux-x64", "linux-arm64", "win-x64", "osx-arm64");
        string Workflow(string name) => File.ReadAllText(Path.Combine(PathHelper.GetRepositoryRoot(), ".github", "workflows", name));

        // The morphir executables (block style) and the morphirup matrices (flow style) list the same platforms and runners.
        // The runner's own glibc does not decide the Linux floor: morphirup-build.yml publishes the Linux executables in
        // the pinned LinuxBuildImage on that runner (see TheLinuxExecutablesAreBuiltAtThePinnedGlibcFloor).
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
    [Category("PlatformSpecific")]
    public void TheLinuxExecutablesAreBuiltAtThePinnedGlibcFloor()
    {
        // A Native AOT executable needs the glibc of the machine that linked it, or newer. The morphir executables link on
        // the runner (ubuntu-latest, glibc 2.39 today and higher when the label moves); morphirup links in an image pinned
        // by digest, so its floor moves only with a commit here.
        MorphirupRelease.GlibcFloor.Should().Be(new Version(2, 35), "Ubuntu 22.04 has glibc 2.35");
        MorphirupRelease.LinuxBuildImage.Should().MatchRegex(@"^ubuntu:22\.04@sha256:[0-9a-f]{64}$");

        var build = File.ReadAllText(Path.Combine(PathHelper.GetRepositoryRoot(), ".github", "workflows", "morphirup-build.yml"));
        build.Should().Contain($"--morphirup-build-image {MorphirupRelease.LinuxBuildImage}", "the Linux publish runs in the pinned image");
        build.Should().Contain($"debian:12-slim {MorphirupRelease.LinuxBuildImage}",
            "the executable runs in Debian 12 and in the image at the floor itself, neither with .NET or libicu");
    }

    [Test]
    public void TheHighestGlibcVersionComesFromTheVersionNeedsSection()
    {
        const string readelf = """
            Version symbols section '.gnu.version' contains 6 entries:
             Addr: 0x0000000000000a10  Offset: 0x000a10  Link: 5 (.dynsym)
              000:   0 (*local*)       2 (GLIBC_2.2.5)   3 (GLIBC_2.34)    4 (GLIBC_2.99)

            Version needs section '.gnu.version_r' contains 2 entries:
             Addr: 0x0000000000000a28  Offset: 0x000a28  Link: 6 (.dynstr)
              000000: Version: 1  File: libm.so.6  Cnt: 1
              0x0010:   Name: GLIBC_2.29  Flags: none  Version: 5
              0x0020: Version: 1  File: libc.so.6  Cnt: 4
              0x0030:   Name: GLIBC_2.2.5  Flags: none  Version: 2
              0x0040:   Name: GLIBC_2.34  Flags: none  Version: 3
              0x0050:   Name: GLIBC_2.17  Flags: none  Version: 4
              0x0060:   Name: GLIBC_PRIVATE  Flags: none  Version: 6
            """;

        // Only the needs section counts; the symbol table's names (GLIBC_2.99 here) are references into it.
        MorphirupRelease.HighestGlibc(readelf).Should().Be((new Version(2, 34), "libc.so.6"));
        MorphirupRelease.HighestGlibc("Version needs section '.gnu.version_r' contains 0 entries:").Should().BeNull();
        MorphirupRelease.IsAboveFloor(new Version(2, 38)).Should().BeTrue();
        MorphirupRelease.IsAboveFloor(new Version(2, 35)).Should().BeFalse();
        MorphirupRelease.IsAboveFloor(new Version(2, 35, 0)).Should().BeFalse();
        MorphirupRelease.IsAboveFloor(new Version(2, 2, 5)).Should().BeFalse();
    }

    [Test]
    public void TheMinimumMacOsVersionComesFromTheBuildVersionCommand()
    {
        const string otool = """
            Load command 8
                  cmd LC_UUID
              cmdsize 24
            Load command 9
                  cmd LC_BUILD_VERSION
              cmdsize 32
             platform 1
                minos 12.0
                  sdk 15.2
               ntools 1
            Load command 10
                  cmd LC_SOURCE_VERSION
              cmdsize 16
              version 0.0
            """;

        MorphirupRelease.MacOsMinimum(otool).Should().Be("12.0");
        MorphirupRelease.MacOsMinimum("Load command 0\n      cmd LC_SEGMENT_64\n").Should().BeNull();
    }

    [Test]
    [Category("PlatformSpecific")]
    public void TheReleaseJobAttachesAndChecksTheMorphirupAssets()
    {
        var workflow = File.ReadAllText(Path.Combine(PathHelper.GetRepositoryRoot(), ".github", "workflows", "deployment.yml"));
        workflow.Should().Contain("uses: ./.github/workflows/morphirup-build.yml", "the release builds morphirup with the pull request's definition");
        workflow.Should().Contain("pattern: morphirup-*");
        workflow.Should().Contain("./artifacts/release-assets/morphirup-*");
        workflow.Should().Contain("--target MorphirupChecksums");
        workflow.Should().Contain("pattern: morphir-*", "the morphir executables keep their own download");

        var build = File.ReadAllText(Path.Combine(PathHelper.GetRepositoryRoot(), ".github", "workflows", "morphirup-build.yml"));
        build.Should().Contain("./artifacts/release-assets/morphirup-v*-THIRD-PARTY-NOTICES.txt",
            "every platform uploads the notices file, which the release attaches with the morphirup-* assets");
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments("linux-x64")]
    [Arguments("win-x64")]
    public async Task TheArchiveHoldsTheExecutableAndItsNoticesUnderTheRidFolder(string rid)
    {
        await using var sandbox = TestSandbox.Create("morphirup-release-");
        var executable = sandbox.PathOf("published", MorphirupRelease.LaunchName(rid));
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllBytesAsync(executable, [1, 2, 3, 4]);
        var license = sandbox.PathOf("LICENSE");
        var notice = sandbox.PathOf("NOTICE");
        await File.WriteAllTextAsync(license, "Apache License");
        await File.WriteAllTextAsync(notice, "Morphir - FINOS");
        var assets = Directory.CreateDirectory(sandbox.PathOf("release assets")).FullName;

        var written = MorphirupRelease.WriteAssets(executable, rid, "1.2.3", assets, license, notice, "notices\n");

        written.Should().Equal(MorphirupRelease.ExecutableName(rid), MorphirupRelease.ArchiveName(rid, "1.2.3"), "morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt");
        (await File.ReadAllBytesAsync(Path.Combine(assets, MorphirupRelease.ExecutableName(rid)))).Should().Equal(1, 2, 3, 4);
        (await File.ReadAllTextAsync(Path.Combine(assets, "morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt"))).Should().Be("notices\n");

        await using var gzip = new GZipStream(File.OpenRead(Path.Combine(assets, MorphirupRelease.ArchiveName(rid, "1.2.3"))), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var entries = new Dictionary<string, (UnixFileMode Mode, byte[] Content)>();
        while (tar.GetNextEntry() is { } entry)
        {
            using var content = new MemoryStream();
            await entry.DataStream!.CopyToAsync(content);
            entries[entry.Name] = (entry.Mode, content.ToArray());
        }

        entries.Keys.Order(StringComparer.Ordinal).Should().Equal(MorphirupRelease.ArchiveEntries(rid));
        MorphirupRelease.ArchiveEntries(rid).Should().Equal(
            $"{rid}/LICENSE", $"{rid}/NOTICE", $"{rid}/THIRD-PARTY-NOTICES.txt", $"{rid}/{MorphirupRelease.LaunchName(rid)}");
        entries[$"{rid}/{MorphirupRelease.LaunchName(rid)}"].Mode.Should().Be(ReleaseArchive.ExecutableMode);
        entries[$"{rid}/{MorphirupRelease.LaunchName(rid)}"].Content.Should().Equal(1, 2, 3, 4);
        entries[$"{rid}/LICENSE"].Content.Should().Equal("Apache License"u8.ToArray());
        entries[$"{rid}/NOTICE"].Content.Should().Equal("Morphir - FINOS"u8.ToArray());
        entries[$"{rid}/THIRD-PARTY-NOTICES.txt"].Content.Should().Equal("notices\n"u8.ToArray());
    }

    [Test]
    [Category("PlatformSpecific")]
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
    [Category("PlatformSpecific")]
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
