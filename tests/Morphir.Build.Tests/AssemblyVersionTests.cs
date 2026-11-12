using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// Tests that every Morphir assembly shipped in the tool package carries the release
/// version, and that Morphir assemblies reference each other at that same version.
/// A mismatch (for example a reference to Morphir.Models 1.0.0.0 when the shipped file
/// is 0.3.0.0) makes the runtime refuse to load the assembly.
/// </summary>
[Category("PlatformSpecific")]
public class AssemblyVersionTests
{
    [Test]
    public async Task ToolPackage_MorphirAssemblies_HaveChangelogVersion()
    {
        var expected = ExpectedAssemblyVersion();
        var assemblies = ReadToolPackageAssemblies();

        assemblies.Should().NotBeEmpty("the tool package should bundle Morphir assemblies");

        var wrong = assemblies
            .Where(a => a.Version != expected)
            .Select(a => $"{a.Name} {a.Version}")
            .ToList();

        wrong.Should().BeEmpty($"every Morphir assembly should be version {expected}");
        await Task.CompletedTask;
    }

    [Test]
    public async Task ToolPackage_MorphirAssemblyReferences_HaveChangelogVersion()
    {
        var expected = ExpectedAssemblyVersion();
        var assemblies = ReadToolPackageAssemblies();

        assemblies.Should().NotBeEmpty("the tool package should bundle Morphir assemblies");

        var wrong = assemblies
            .SelectMany(a => a.MorphirReferences.Select(r => (From: a.Name, To: r.Name, r.Version)))
            .Where(r => r.Version != expected)
            .Select(r => $"{r.From} -> {r.To} {r.Version}")
            .ToList();

        wrong.Should().BeEmpty($"every reference between Morphir assemblies should be version {expected}");
        await Task.CompletedTask;
    }

    private sealed record AssemblyInfo(string Name, Version Version, IReadOnlyList<(string Name, Version Version)> MorphirReferences);

    private static Version ExpectedAssemblyVersion()
    {
        var changelogVersion = GetChangelogVersion();
        changelogVersion.Should().NotBeNullOrEmpty("CHANGELOG.md should have a released version");

        // AssemblyVersion is major.minor.patch.0; prerelease labels are not part of it.
        var core = changelogVersion.Split('-', '+')[0];
        return new Version($"{core}.0");
    }

    private static List<AssemblyInfo> ReadToolPackageAssemblies()
    {
        var package = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
        package.Should().NotBeNull("Tool package should exist after build");

        var result = new List<AssemblyInfo>();
        using var archive = ZipFile.OpenRead(package!);

        foreach (var entry in archive.Entries.Where(IsMorphirAssembly))
        {
            using var buffer = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(buffer);
            }

            buffer.Position = 0;
            using var pe = new PEReader(buffer);
            var metadata = pe.GetMetadataReader();
            var definition = metadata.GetAssemblyDefinition();

            var references = metadata.AssemblyReferences
                .Select(metadata.GetAssemblyReference)
                .Select(r => (Name: metadata.GetString(r.Name), r.Version))
                .Where(r => IsMorphirName(r.Name))
                .ToList();

            result.Add(new AssemblyInfo(metadata.GetString(definition.Name), definition.Version, references));
        }

        return result;
    }

    private static bool IsMorphirAssembly(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("tools/", StringComparison.Ordinal)
        && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        && IsMorphirName(Path.GetFileNameWithoutExtension(entry.Name));

    private static bool IsMorphirName(string name) =>
        name.Equals("dotnet-morphir", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Morphir", StringComparison.OrdinalIgnoreCase);

    private static string GetChangelogVersion()
    {
        var changelogPath = Path.Combine(TestFixture.GetRepositoryRoot(), "CHANGELOG.md");
        if (!File.Exists(changelogPath))
        {
            return string.Empty;
        }

        var match = File.ReadLines(changelogPath)
            .Select(line => System.Text.RegularExpressions.Regex.Match(line, @"^## \[(\d+\.\d+\.\d+[^\]]*)\]"))
            .FirstOrDefault(m => m.Success);

        return match?.Groups[1].Value ?? string.Empty;
    }
}
