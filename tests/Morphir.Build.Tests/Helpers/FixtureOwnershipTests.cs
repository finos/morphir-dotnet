// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.TestSupport.Fixtures;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// Each file under <c>tests/Fixtures</c> has one owner: an entry of <see cref="FixtureCatalog"/>, or a directory of
/// <see cref="FixtureCatalog.ExistingOwners"/> that a test project owned before the catalog. A file with neither
/// fails here, so a new fixture comes in through the catalog with its provenance entry and hash.
/// </summary>
[Category("PlatformSpecific")]
public class FixtureOwnershipTests
{
    private static readonly string Root = TestFixture.GetRepositoryRoot();

    private static string FixturesDirectory => Path.Combine(Root, "tests", "Fixtures");

    private static string Read(string relative) => File.ReadAllText(Path.Combine(FixturesDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static void EachFileHasAnOwner(string fixturesDirectory) =>
        FixtureCatalog.UnownedFiles(fixturesDirectory).Should().BeEmpty(
            "each file under tests/Fixtures has an entry in FixtureCatalog or is in a directory of FixtureCatalog.ExistingOwners");

    [Test]
    public void EachFixtureFileHasAnOwner() => EachFileHasAnOwner(FixturesDirectory);

    [Test]
    public void AnUnownedFixtureFailsAGuardTest()
    {
        using var sandbox = TestSandbox.Create("morphir-fixture-owners-");
        var fixtures = sandbox.PathOf("Fixtures");
        void Plant(string relative)
        {
            var path = Path.Combine(fixtures, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{}");
        }

        Plant("MorphirIon/kit/owned-by-its-directory.feature");
        Plant("MorphirTrees/README.md");
        EachFileHasAnOwner(fixtures);

        Plant("MorphirTrees/kit/planted.feature");
        Plant("Unowned/planted.json");
        var act = () => EachFileHasAnOwner(fixtures);

        act.Should().Throw<Exception>().WithMessage("*MorphirTrees/kit/planted.feature*Unowned/planted.json*");
        FixtureCatalog.UnownedFiles(fixtures).Should().Equal("MorphirTrees/kit/planted.feature", "Unowned/planted.json");
    }

    /// <summary>
    /// The upstream IR tests read the corpus from <see cref="FixtureCatalog.UpstreamIr"/>, a fixed list, so a JSON document
    /// added to one of its folders without an entry would not be tested.
    /// </summary>
    [Test]
    public void EachUpstreamIrDocumentIsInTheCatalog() =>
        FixtureCatalog.UpstreamIr.Uncatalogued(FixturesDirectory).Should().BeEmpty(
            "each *.json of the folders of FixtureCatalog.UpstreamIr is a document of FixtureCatalog.UpstreamIr.Documents");

    [Test]
    public void AnUncataloguedUpstreamIrDocumentIsFound()
    {
        using var sandbox = TestSandbox.Create("morphir-upstream-ir-");
        var fixtures = sandbox.PathOf("Fixtures");
        foreach (var document in FixtureCatalog.UpstreamIr.Documents.Append(FixtureCatalog.UpstreamIr.Documents[0] with { RelativePath = FixtureCatalog.UpstreamIr.Directory + "v4/planted.json" }))
        {
            var path = Path.Combine(fixtures, document.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{}");
        }

        FixtureCatalog.UpstreamIr.Uncatalogued(fixtures).Should().Equal("v4/planted.json");
    }

    [Test]
    public void EachOwnerOfTheCatalogIsInTheRepository()
    {
        foreach (var owner in FixtureCatalog.ExistingOwners)
        {
            var path = Path.Combine(FixturesDirectory, owner.RelativePath.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            (owner.RelativePath.EndsWith('/') ? Directory.Exists(path) : File.Exists(path))
                .Should().BeTrue($"the owner {owner.RelativePath} is in tests/Fixtures");
            Directory.Exists(Path.Combine(Root, "tests", owner.Project)).Should().BeTrue($"{owner.Project} is a test project");
        }
    }

    [Test]
    public void EachFileOfTheCatalogHasItsBytesOnDiskAndItsRowInItsProvenance()
    {
        foreach (var entry in FixtureCatalog.All)
        {
            var bytes = File.ReadAllBytes(Path.Combine(FixturesDirectory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(entry.Sha256, $"{entry.RelativePath} on disk has the bytes that the catalog pins");

            var folder = entry.Provenance[..^"PROVENANCE.md".Length];
            entry.Provenance.Should().EndWith("/PROVENANCE.md");
            entry.RelativePath.Should().StartWith(folder, "a provenance file describes the files of its own folder");
            var provenance = Read(entry.Provenance);
            var name = entry.RelativePath[(entry.Provenance.Length - "PROVENANCE.md".Length)..];
            provenance.Split('\n').Should().Contain(
                line => line.Contains($"`{name}`", StringComparison.Ordinal) && line.Contains(entry.Sha256, StringComparison.Ordinal),
                $"{entry.Provenance} has a row for {name} with its SHA-256");
        }
    }
}
