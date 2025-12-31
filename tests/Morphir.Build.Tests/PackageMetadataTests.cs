using System.Xml.Linq;
using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// Tests for validating NuGet package metadata (version, authors, license, etc.)
/// </summary>
public class PackageMetadataTests
{
    [Test]
    public async Task AllPackages_HaveSameVersion()
    {
        // Arrange
        var packages = new[]
        {
            TestFixture.FindLatestPackage("Morphir.Core.*.nupkg"),
            TestFixture.FindLatestPackage("Morphir.Tooling.*.nupkg"),
            TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg"),
            // NOTE: Morphir.SDK is excluded from version check as it's in independent alpha development
            // and will be versioned separately until it reaches stable release
        }.Where(p => p != null).ToList();

        packages.Should().HaveCountGreaterThan(1, "Multiple packages should exist to compare versions");

        // Act
        var versions = new List<string>();
        foreach (var package in packages)
        {
            var version = await GetPackageVersion(package!);
            versions.Add(version);
        }

        // Assert
        versions.Distinct().Should().ContainSingle(
            "All packages should have the same version number");
    }

    [Test]
    public async Task PackageVersions_MatchChangelogVersion()
    {
        // Arrange
        var changelogVersion = GetChangelogVersion();
        changelogVersion.Should().NotBeNullOrEmpty("CHANGELOG.md should have a version");

        var toolPackage = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
        toolPackage.Should().NotBeNull("Tool package should exist after build");

        // Act
        var packageVersion = await GetPackageVersion(toolPackage!);

        // Assert
        packageVersion.Should().Be(changelogVersion,
            "Package version should match the latest version in CHANGELOG.md");
    }

    [Test]
    public async Task ToolPackage_HasCorrectMetadata()
    {
        // Arrange
        var package = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
        package.Should().NotBeNull("Tool package should exist after build");

        // Act
        var nuspec = await GetNuspecMetadata(package!);

        // Assert
        nuspec.Id.Should().Be("Morphir.Tool", "Package ID should be Morphir.Tool");
        nuspec.Authors.Should().Contain("FINOS", "Package should list FINOS as an author");
        nuspec.License.Should().NotBeNullOrEmpty("Package should have a license");
        nuspec.ProjectUrl.Should().Contain("morphir-dotnet", "Package should link to morphir-dotnet repository");
        nuspec.PackageType.Should().Be("DotnetTool", "Tool package should have PackageType=DotnetTool");
    }

    [Test]
    public async Task AllPackages_HaveReleaseNotes()
    {
        // Arrange
        var packages = new[]
        {
            TestFixture.FindLatestPackage("Morphir.Core.*.nupkg"),
            TestFixture.FindLatestPackage("Morphir.Tooling.*.nupkg"),
            TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg"),
            // NOTE: Morphir.SDK is excluded as it's a new alpha package with minimal release notes
        }.Where(p => p != null).ToList();

        packages.Should().NotBeEmpty("At least one package should exist after build");

        foreach (var package in packages)
        {
            // Act
            var nuspec = await GetNuspecMetadata(package!);

            // Assert
            nuspec.ReleaseNotes.Should().NotBeNullOrEmpty(
                $"Package {Path.GetFileName(package)} should have release notes");
            nuspec.ReleaseNotes.Should().Contain("###",
                $"Package {Path.GetFileName(package)} release notes should have markdown sections");
        }
    }

    // Helper methods

    private async Task<string> GetPackageVersion(string packagePath)
    {
        var nuspecContent = await GetNuspecContent(packagePath);
        var doc = XDocument.Parse(nuspecContent);
        var ns = doc.Root!.GetDefaultNamespace();

        return doc.Root
            .Element(ns + "metadata")
            ?.Element(ns + "version")
            ?.Value ?? throw new Exception("Version not found in nuspec");
    }

    private async Task<NuspecMetadata> GetNuspecMetadata(string packagePath)
    {
        var nuspecContent = await GetNuspecContent(packagePath);
        var doc = XDocument.Parse(nuspecContent);
        var ns = doc.Root!.GetDefaultNamespace();
        var metadata = doc.Root.Element(ns + "metadata")!;

        return new NuspecMetadata
        {
            Id = metadata.Element(ns + "id")?.Value ?? string.Empty,
            Version = metadata.Element(ns + "version")?.Value ?? string.Empty,
            Authors = metadata.Element(ns + "authors")?.Value ?? string.Empty,
            License = GetLicenseValue(metadata, ns),
            ProjectUrl = metadata.Element(ns + "projectUrl")?.Value ?? string.Empty,
            ReleaseNotes = metadata.Element(ns + "releaseNotes")?.Value ?? string.Empty,
            PackageType = metadata.Element(ns + "packageTypes")?.Element(ns + "packageType")?.Attribute("name")?.Value ?? string.Empty
        };
    }

    private string GetLicenseValue(XElement metadata, XNamespace ns)
    {
        // Try license element first, then fall back to licenseUrl for older packages
        return metadata.Element(ns + "license")?.Value
               ?? metadata.Element(ns + "licenseUrl")?.Value
               ?? string.Empty;
    }

    private async Task<string> GetNuspecContent(string packagePath)
    {
        // .nuspec file is in the root of the .nupkg (which is a zip file)
        var entries = TestFixture.GetPackageEntries(packagePath);
        var nuspecEntry = entries.FirstOrDefault(e => e.EndsWith(".nuspec"));

        if (nuspecEntry == null)
        {
            throw new FileNotFoundException($"No .nuspec file found in package {packagePath}");
        }

        return await TestFixture.ReadPackageEntry(packagePath, nuspecEntry);
    }

    private string GetChangelogVersion()
    {
        var repoRoot = TestFixture.GetRepositoryRoot();
        var changelogPath = Path.Combine(repoRoot, "CHANGELOG.md");

        if (!File.Exists(changelogPath))
        {
            return string.Empty;
        }

        var lines = File.ReadAllLines(changelogPath);

        // Find first version line like "## [0.3.0] - 2025-12-18"
        foreach (var line in lines)
        {
            if (line.StartsWith("## [") && !line.Contains("Unreleased"))
            {
                var versionStart = line.IndexOf('[') + 1;
                var versionEnd = line.IndexOf(']', versionStart);
                if (versionStart > 0 && versionEnd > versionStart)
                {
                    return line.Substring(versionStart, versionEnd - versionStart);
                }
            }
        }

        return string.Empty;
    }

    private record NuspecMetadata
    {
        public string Id { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Authors { get; init; } = string.Empty;
        public string License { get; init; } = string.Empty;
        public string ProjectUrl { get; init; } = string.Empty;
        public string ReleaseNotes { get; init; } = string.Empty;
        public string PackageType { get; init; } = string.Empty;
    }
}
