// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The release job downloads each platform's uploaded outputs into a folder of its own and flattens them into one
/// release-assets folder. A name that two platforms both upload must hold the same bytes; a per-platform SHA256SUMS is
/// never copied, so it cannot overwrite another platform's or the final one. One SHA256SUMS is then written over the
/// Windows toolchain outputs of both Windows platforms.
/// </summary>
public class ReleaseAssetsTests
{
    [Test]
    public async Task FlatteningCopiesEveryFileOnceAndAcceptsAByteIdenticalDuplicate()
    {
        await using var sandbox = TestSandbox.Create("release-assets-");
        var x64 = Source(sandbox, "morphirup-win-x64", ("morphirup-win-x64.exe", "x64"), ("morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt", "notices"));
        var arm64 = Source(sandbox, "morphirup-win-arm64", ("morphirup-win-arm64.exe", "arm64"), ("morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt", "notices"));
        var assets = sandbox.PathOf("release-assets");

        var result = ReleaseAssets.Flatten([x64, arm64], assets);

        result.Problems.Should().BeEmpty();
        result.Files.Should().Equal("morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt", "morphirup-win-arm64.exe", "morphirup-win-x64.exe");
        (await File.ReadAllTextAsync(Path.Combine(assets, "morphirup-win-arm64.exe"))).Should().Be("arm64");
        (await File.ReadAllTextAsync(Path.Combine(assets, "morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt"))).Should().Be("notices");
    }

    [Test]
    public async Task FlatteningRefusesTwoDifferentFilesUnderOneNameAndKeepsTheFirst()
    {
        await using var sandbox = TestSandbox.Create("release-assets-");
        var x64 = Source(sandbox, "windows-release-toolchain-win-x64", ("morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt", "one"));
        var arm64 = Source(sandbox, "windows-release-toolchain-win-arm64", ("morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt", "two"));
        var assets = sandbox.PathOf("release-assets");

        var result = ReleaseAssets.Flatten([x64, arm64], assets);

        result.Problems.Should().ContainSingle().Which.Should()
            .Contain("morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt").And.Contain("windows-release-toolchain-win-arm64");
        (await File.ReadAllTextAsync(Path.Combine(assets, "morphirup-v1.2.3-THIRD-PARTY-NOTICES.txt"))).Should().Be("one", "nothing is overwritten");
    }

    [Test]
    public async Task APerPlatformChecksumFileIsNeverCopied()
    {
        await using var sandbox = TestSandbox.Create("release-assets-");
        var x64 = Source(sandbox, "windows-release-toolchain-win-x64", ("SHA256SUMS", "x64 sums"), ("morphir-toolchain-win-x64.json", "{}"));
        var arm64 = Source(sandbox, "windows-release-toolchain-win-arm64", ("SHA256SUMS", "arm64 sums"), ("morphir-toolchain-win-arm64.json", "{}"));
        var assets = sandbox.PathOf("release-assets");
        Directory.CreateDirectory(assets);
        await File.WriteAllTextAsync(Path.Combine(assets, "SHA256SUMS"), "final");

        var result = ReleaseAssets.Flatten([x64, arm64], assets);

        result.Problems.Should().BeEmpty();
        result.Files.Should().Equal("morphir-toolchain-win-arm64.json", "morphir-toolchain-win-x64.json");
        (await File.ReadAllTextAsync(Path.Combine(assets, "SHA256SUMS"))).Should().Be("final");
    }

    [Test]
    public async Task FlatteningRefusesAFolderInsideAnUploadedOutput()
    {
        await using var sandbox = TestSandbox.Create("release-assets-");
        var source = Source(sandbox, "morphirup-win-arm64", ("morphirup-win-arm64.exe", "arm64"));
        Directory.CreateDirectory(Path.Combine(source, "nested"));

        var result = ReleaseAssets.Flatten([source], sandbox.PathOf("release-assets"));

        result.Problems.Should().ContainSingle().Which.Should().Contain("nested");
    }

    [Test]
    public async Task TheWindowsChecksumFileCoversBothPlatformsOutputsSortedAndNotItself()
    {
        await using var sandbox = TestSandbox.Create("release-assets-");
        var x64 = Source(sandbox, "windows-release-toolchain-win-x64",
            ("morphir-toolchain-win-x64.json", "x64 bundle"), ("morphirup-win-x64.exe", "x64"), ("SHA256SUMS", "x64 sums"));
        var arm64 = Source(sandbox, "windows-release-toolchain-win-arm64",
            ("morphir-toolchain-win-arm64.json", "arm64 bundle"), ("morphirup-win-arm64.exe", "arm64"), ("SHA256SUMS", "arm64 sums"));
        var assets = sandbox.PathOf("release-assets");
        ReleaseAssets.Flatten([x64, arm64], assets).Problems.Should().BeEmpty();
        // A file in the folder that no Windows toolchain output uploaded is not listed.
        await File.WriteAllTextAsync(Path.Combine(assets, "morphir-linux-x64"), "linux");

        var result = ReleaseAssets.WriteWindowsToolchainChecksums([x64, arm64], assets);

        result.Problems.Should().BeEmpty();
        var lines = (await File.ReadAllTextAsync(Path.Combine(assets, ReleaseAssets.WindowsToolchainChecksumFileName))).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Select(line => line[66..]).Should().Equal(
            "morphir-toolchain-win-arm64.json", "morphir-toolchain-win-x64.json", "morphirup-win-arm64.exe", "morphirup-win-x64.exe");
        lines[0].Should().Be($"{ReleaseArchive.Sha256(Path.Combine(assets, "morphir-toolchain-win-arm64.json"))}  morphir-toolchain-win-arm64.json");
    }

    [Test]
    [Arguments("win-x64")]
    [Arguments("win-arm64")]
    public async Task TheWindowsChecksumFileIsNotWrittenWhenAPlatformsBundleOrManagerIsMissing(string missing)
    {
        await using var sandbox = TestSandbox.Create("release-assets-");
        var sources = ReleaseAssets.WindowsToolchainPlatforms
            .Where(rid => rid != missing)
            .Select(rid => Source(sandbox, $"windows-release-toolchain-{rid}",
                ($"morphir-toolchain-{rid}.json", "bundle"), (MorphirupRelease.ExecutableName(rid), "manager")))
            .ToList();
        var assets = sandbox.PathOf("release-assets");
        ReleaseAssets.Flatten(sources, assets).Problems.Should().BeEmpty();

        var result = ReleaseAssets.WriteWindowsToolchainChecksums(sources, assets);

        result.Problems.Should().HaveCount(2).And.OnlyContain(problem => problem.Contains(missing));
        File.Exists(Path.Combine(assets, ReleaseAssets.WindowsToolchainChecksumFileName)).Should().BeFalse();
    }

    [Test]
    public void TheWindowsToolchainPlatformsAreTheTwoWindowsRids()
    {
        ReleaseAssets.WindowsToolchainPlatforms.Should().Equal("win-x64", "win-arm64");
        ReleaseAssets.WindowsToolchainRequiredFiles("win-arm64").Should().Equal("morphir-toolchain-win-arm64.json", "morphirup-win-arm64.exe");
    }

    [Test]
    public async Task TheLineWriterIsTheOneTheMorphirupChecksumFileUses()
    {
        await using var sandbox = TestSandbox.Create("release-assets-");
        var assets = Directory.CreateDirectory(sandbox.PathOf("assets")).FullName;
        await File.WriteAllTextAsync(Path.Combine(assets, "b"), "two");
        await File.WriteAllTextAsync(Path.Combine(assets, "a"), "one");

        ReleaseAssets.Sha256Lines(assets, ["b", "a"]).Should().Be(
            $"{ReleaseArchive.Sha256(Path.Combine(assets, "a"))}  a\n{ReleaseArchive.Sha256(Path.Combine(assets, "b"))}  b\n");
    }

    private static string Source(TestSandbox sandbox, string artifact, params (string Name, string Content)[] files)
    {
        var folder = Directory.CreateDirectory(sandbox.PathOf("downloads", artifact)).FullName;
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(folder, name), content);
        return folder;
    }
}
