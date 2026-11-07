// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// Tests for the Myriad plugin package check in PackageValidator.
/// </summary>
public class MyriadPluginPackageTests
{
    private static readonly string[] CompleteEntries =
    [
        "tools/plugin/Morphir.Myriad.SystemTextJson.dll",
        "tools/plugin/FSharp.Core.dll",
        "tools/host/Myriad.dll",
        "tools/host/FSharp.Core.dll",
        "tasks/net10.0/Morphir.Myriad.SystemTextJson.Build.dll",
        "tasks/net10.0/Morphir.Myriad.Hosting.dll",
        "build/Morphir.Myriad.SystemTextJson.props",
        "build/Morphir.Myriad.SystemTextJson.targets",
        "README.md",
        "ThirdPartyNotices.txt",
    ];

    private static string WritePackage(IEnumerable<string> entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"plugin-{Guid.NewGuid():N}.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            archive.CreateEntry(entry);
        }

        return path;
    }

    [Test]
    [Arguments("Morphir.Myriad.?.*.nupkg", "Morphir.Myriad", "Morphir.Myriad.Build.dll")]
    [Arguments("Morphir.Myriad.SystemTextJson.?.*.nupkg", "Morphir.Myriad.SystemTextJson", "Morphir.Myriad.SystemTextJson.Build.dll")]
    public async Task ValidateMyriadPluginPackage_PackedPluginPackages_ReturnSuccess(string pattern, string packageId, string taskAssembly)
    {
        var package = TestFixture.FindLatestPackage(pattern);
        package.Should().NotBeNull($"{packageId} package should exist after packing");

        var result = PackageValidator.ValidateMyriadPluginPackage(package!, packageId, taskAssembly);

        result.Errors.Should().BeEmpty($"{packageId} should be a valid Myriad plugin package");
        result.IsValid.Should().BeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateMyriadPluginPackage_CompletePackage_ReturnsSuccess()
    {
        var path = WritePackage(CompleteEntries);
        try
        {
            var result = PackageValidator.ValidateMyriadPluginPackage(path, "Morphir.Myriad.SystemTextJson", "Morphir.Myriad.SystemTextJson.Build.dll");
            result.IsValid.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateMyriadPluginPackage_PackageWithoutHost_ReturnsFailureNamingTheHost()
    {
        var path = WritePackage(CompleteEntries.Where(entry => entry != "tools/host/Myriad.dll"));
        try
        {
            var result = PackageValidator.ValidateMyriadPluginPackage(path, "Morphir.Myriad.SystemTextJson", "Morphir.Myriad.SystemTextJson.Build.dll");
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle().Which.Should().Contain("tools/host/Myriad.dll");
        }
        finally
        {
            File.Delete(path);
        }

        await Task.CompletedTask;
    }
}
