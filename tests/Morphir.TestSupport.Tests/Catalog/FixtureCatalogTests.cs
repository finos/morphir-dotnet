// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.TestSupport.Fixtures;

namespace Morphir.TestSupport.Tests.Catalog;

/// <summary>The files that <see cref="FixtureCatalog"/> serves, and the kit tree cases it reads from them.</summary>
public class FixtureCatalogTests
{
    private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Normalized(string json) => JsonNode.Parse(json)!.ToJsonString();

    [Test]
    public void PinnedKitFilesMatchTheirHashes()
    {
        FixtureCatalog.Kit.Files.Select(entry => entry.RelativePath).Should().BeEquivalentTo(
            "MorphirTrees/kit/document-tree.feature",
            "MorphirTrees/kit/distributions.feature",
            "MorphirTrees/kit/versions.feature");

        foreach (var entry in FixtureCatalog.Kit.Files)
            Sha256Of(FixtureCatalog.ReadBytes(entry)).Should().Be(entry.Sha256, $"{entry.RelativePath} is the pinned upstream file");
    }

    [Test]
    public void EachFileOfTheCatalogMatchesItsHash()
    {
        foreach (var entry in FixtureCatalog.All)
            Sha256Of(FixtureCatalog.ReadBytes(entry)).Should().Be(entry.Sha256, $"{entry.RelativePath} has the bytes of its provenance entry");
    }

    [Test]
    public void TheCatalogServesExactlyTheFilesItOwns()
    {
        FixtureCatalog.EmbeddedFiles.Should().BeEquivalentTo(FixtureCatalog.All.Select(entry => entry.RelativePath));
        FixtureCatalog.All.Select(entry => entry.RelativePath).Should().OnlyHaveUniqueItems();
        FixtureCatalog.All.Select(entry => entry.Intent).Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void TheModelsAreTheRepositoryIrFixturesByIntent()
    {
        FixtureCatalog.Models.V3Library.RelativePath.Should().Be("MorphirIR/greeting-example-v3.json");
        FixtureCatalog.Models.V4DailyRate.RelativePath.Should().Be("MorphirIR/daily-rate-v4.json");
        JsonNode.Parse(FixtureCatalog.ReadText(FixtureCatalog.Models.V3Library))!["formatVersion"]!.GetValue<int>().Should().Be(3);
        JsonNode.Parse(FixtureCatalog.ReadText(FixtureCatalog.Models.V4DailyRate))!["formatVersion"]!.GetValue<int>().Should().Be(4);
        FixtureCatalog.All.Should().Contain(FixtureCatalog.Models.All);
    }

    [Test]
    public void DocumentTree0006YamlAndJsonLoadEqual()
    {
        // document-tree-0006 is document-tree-0003's set spelled in the JSON profile.
        var yaml = FixtureCatalog.Kit.Case("document-tree-0003");
        var json = FixtureCatalog.Kit.Case("document-tree-0006");

        json.Files.Keys.Should().BeEquivalentTo(yaml.Files.Keys);
        foreach (var (path, text) in yaml.Files)
            Normalized(json.Files[path]).Should().Be(Normalized(text), $"the file {path} says the same in both profiles");
        Normalized(json.CanonicalSingleFile!).Should().Be(Normalized(yaml.CanonicalSingleFile!));
    }

    [Test]
    public void TheTreeCasesOfTheKitAreRead()
    {
        var cases = FixtureCatalog.Kit.DocumentTree;

        cases.Select(kitCase => kitCase.Id).Should().Equal(
            "document-tree-0003", "document-tree-0004", "document-tree-0005", "document-tree-0006", "document-tree-0007",
            "document-tree-0008", "document-tree-0009", "document-tree-0011", "document-tree-0012", "document-tree-0013",
            "document-tree-0014", "document-tree-0015", "document-tree-0016", "document-tree-0017");

        var truncated = FixtureCatalog.Kit.Case("document-tree-0004");
        truncated.Title.Should().Be("A stem truncated for the path budget is recorded in fileNames");
        truncated.Files.Keys.Should().Equal("manifest", "pkg/my-org/my-project/domain/module", "pkg/my-org/my-project/domain/customer-relati__44a101f8.type");
        JsonNode.Parse(truncated.Files["manifest"])!["pathBudget"]!.GetValue<int>().Should().Be(64);
        truncated.Diagnostic.Should().BeNull();
        truncated.ReadOnly.Should().BeFalse();

        foreach (var kitCase in cases.Where(kitCase => kitCase.Files.Count > 0))
        {
            kitCase.CanonicalSingleFile.Should().NotBeNull($"{kitCase.Id} has a tree");
            JsonNode.Parse(kitCase.CanonicalSingleFile!)!["distribution"].Should().NotBeNull();
            foreach (var text in kitCase.Files.Values) JsonNode.Parse(text).Should().NotBeNull();
        }

        cases.Where(kitCase => kitCase.ReadOnly).Select(kitCase => kitCase.Id).Should().Equal("document-tree-0005", "document-tree-0017");

        var rejected = FixtureCatalog.Kit.Case("document-tree-0016");
        rejected.Files.Should().BeEmpty();
        rejected.CanonicalSingleFile.Should().BeNull();
        rejected.Diagnostic.Should().Be("version_mismatch");
        JsonNode.Parse(rejected.RejectedDocument!)!["formatVersion"]!.GetValue<string>().Should().Be("4.0.0");
    }

    [Test]
    public void AYamlSpellingIsTranslatedToJson()
    {
        // document-tree-0011's node file holds the classic payload: arrays, empty objects and an empty string.
        var node = JsonNode.Parse(FixtureCatalog.Kit.Case("document-tree-0011").Files["pkg/my-org/my-project/domain/user.type"])!;

        node["formatVersion"]!.GetValue<string>().Should().Be("3.1.0");
        node["def"]!["value"]!["doc"]!.GetValue<string>().Should().BeEmpty();
        node["def"]!["value"]!["value"]!.ToJsonString().Should().Be(
            """["TypeAliasDefinition",[],["Reference",{},[[["morphir"],["s","d","k"]],[["string"]],["string"]],[]]]""");
    }

    [Test]
    public void AnUnknownCaseIdIsRefused()
    {
        var act = () => FixtureCatalog.Kit.Case("document-tree-9999");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*document-tree-9999*");
    }
}
