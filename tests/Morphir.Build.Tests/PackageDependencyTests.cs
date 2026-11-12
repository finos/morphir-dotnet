using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// Tests that the packed library set is installable: every Morphir package a packed
/// package depends on is packed alongside it, at the same version. Otherwise consumers
/// cannot restore the package from NuGet.org.
/// </summary>
[Category("PlatformSpecific")]
public class PackageDependencyTests
{
    [Test]
    public async Task PackedPackages_MorphirDependencies_ArePackedAtSameVersion()
    {
        var packages = ReadPackedPackages();
        packages.Should().NotBeEmpty("packages should exist after build");

        var packed = packages.ToDictionary(p => p.Id, p => p.Version, StringComparer.OrdinalIgnoreCase);

        var missing = packages
            .SelectMany(p => p.MorphirDependencies.Select(d => (From: p.Id, To: d.Id, d.Version)))
            .Where(d => !packed.TryGetValue(d.To, out var version) || !VersionRangeAllows(d.Version, version))
            .Select(d => $"{d.From} -> {d.To} {d.Version}")
            .Distinct()
            .ToList();

        missing.Should().BeEmpty("every Morphir dependency of a packed package should also be packed at its version");
        await Task.CompletedTask;
    }

    private sealed record PackageInfo(string Id, string Version, IReadOnlyList<(string Id, string Version)> MorphirDependencies);

    private static List<PackageInfo> ReadPackedPackages()
    {
        var packagesDir = TestFixture.GetPackagesDirectory();
        if (!Directory.Exists(packagesDir))
        {
            return new List<PackageInfo>();
        }

        var result = new List<PackageInfo>();
        foreach (var path in Directory.GetFiles(packagesDir, "*.nupkg"))
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
                                                    && !e.FullName.Contains('/'));
            using var stream = entry.Open();
            var metadata = XDocument.Load(stream).Root!.Elements().Single(e => e.Name.LocalName == "metadata");

            string Value(string name) => metadata.Elements().Single(e => e.Name.LocalName == name).Value;

            var dependencies = metadata.Descendants()
                .Where(e => e.Name.LocalName == "dependency")
                .Select(e => (Id: (string)e.Attribute("id")!, Version: (string?)e.Attribute("version") ?? string.Empty))
                .Where(d => d.Id.StartsWith("Morphir", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            result.Add(new PackageInfo(Value("id"), Value("version"), dependencies));
        }

        return result;
    }

    // Project references pack as a minimum version ("0.3.0-rc.2") or an exact range ("[0.3.0-rc.2]").
    private static bool VersionRangeAllows(string range, string version) =>
        string.Equals(range.Trim('[', ']', '(', ')'), version, StringComparison.OrdinalIgnoreCase);
}
