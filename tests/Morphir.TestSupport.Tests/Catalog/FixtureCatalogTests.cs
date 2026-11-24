// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.TestSupport.Fixtures;

namespace Morphir.TestSupport.Tests.Catalog;

/// <summary>The files that <see cref="FixtureCatalog"/> serves, and the kit cases it reads from them.</summary>
/// <remarks>The hashes are of the checked-out bytes, whose line endings depend on the checkout.</remarks>
[Category("PlatformSpecific")]
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
    public void TheKitStatesCanonicalYamlAndJsonSpellingsAsReferenceData()
    {
        var spellings = FixtureCatalog.Kit.CanonicalSpellings;
        spellings.Count.Should().BeGreaterThan(40, "the pinned kit features state many YAML and JSON pairs");
        spellings.Select(spelling => spelling.Name).Should().OnlyHaveUniqueItems();

        var integer = spellings.Single(spelling => spelling.Id == "values-0001" && spelling.Feature == "values.feature");
        integer.Yaml.Should().Be("Literal:\n  IntegerLiteral: 42");
        integer.Json.Should().Be("{ \"Literal\": { \"IntegerLiteral\": 42 } }");

        var variable = spellings.Single(spelling => spelling.Id == "values-0002" && spelling.Feature == "values.feature");
        variable.Yaml.Should().Be("Variable: x", "an outline's YAML and JSON example rows are one pair");
        variable.Json.Should().Be("{ \"Variable\": \"x\" }");
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
    public void AWrittenTreeHasEachFileAtItsLocationInTheTree()
    {
        var tree = FixtureCatalog.Trees.WrittenV4LibraryWithDependency;

        tree.Files.Select(file => file.Location).Should().Contain(["manifest.json", "pkg/my-org/finance/pricing/models/module.json", "deps/morphir/_sdk/@/api/zero.value.json"]);
        tree.Files.Select(file => file.Location).Should().BeInAscendingOrder(StringComparer.Ordinal);
        tree.Files.Should().OnlyContain(file => file.Entry.RelativePath == "MorphirTrees/written-v4-library-with-dependency/" + file.Location);
        FixtureCatalog.Trees.WrittenV3LibraryWithDependency.Files.Should().HaveCount(tree.Files.Count);
    }

    [Test]
    public void AFaultyTreeDiffersFromItsTreeInOneFile()
    {
        var tree = FixtureCatalog.Trees.WrittenV4LibraryWithDependency;
        var unknownTag = FixtureCatalog.Trees.WrittenV4WithAnUnknownTypeTag;
        var missingValue = FixtureCatalog.Trees.WrittenV4WithAMissingValueFile;

        unknownTag.Tree.Files.Select(file => file.Location).Should().Equal(tree.Files.Select(file => file.Location));
        unknownTag.Tree.Files.Where(file => file.Entry != tree.Files.Single(other => other.Location == file.Location).Entry)
            .Select(file => file.Location).Should().Equal(unknownTag.Fault.File);
        FixtureCatalog.ReadText(unknownTag.Tree.Files.Single(file => file.Location == unknownTag.Fault.File).Entry).Should().Contain("\"Struct\"");

        missingValue.Tree.Files.Select(file => file.Location).Should().Equal(tree.Files.Select(file => file.Location).Where(location => location != missingValue.Fault.File));
        missingValue.Fault.Names.Should().Equal("pkg/my-org/finance/pricing/models/module.json", "default-currency");
    }

    [Test]
    public void ATreeRefusesAFileThatItHasNot()
    {
        var tree = FixtureCatalog.Trees.WrittenV4LibraryWithDependency;

        ((Action)(() => tree.Without("no/such.json", "a tree"))).Should().Throw<ArgumentException>().WithMessage("*no file at no/such.json*");
        ((Action)(() => tree.WithFile("no/such.json", tree.Files[0].Entry, "a tree"))).Should().Throw<ArgumentException>();
        ((Action)(() => FixtureTree.Below("a tree", "MorphirIR/", [tree.Files[0].Entry]))).Should().Throw<ArgumentException>().WithMessage("*is not below MorphirIR/*");
    }

    [Test]
    public async Task ATreeIsWrittenIntoAWorkspaceWithItsBytes()
    {
        await using var workspace = Morphir.TestSupport.Drivers.ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        var tree = FixtureCatalog.Trees.WrittenV4WithAMissingValueFile.Tree;

        var directory = workspace.WriteTree(tree, "model");

        workspace.FilesIn(directory).Should().Equal(tree.Files.Select(file => file.Location));
        foreach (var file in tree.Files)
            workspace.ReadBytes(directory.File(file.Location.Split('/'))).Should().Equal(FixtureCatalog.ReadBytes(file.Entry));
    }

    [Test]
    public void TheVectorsOfRfc8785AreRead()
    {
        var vectors = FixtureCatalog.Rfc8785.Vectors;

        vectors.Numbers.Should().HaveCount(26, "Table 1 of Appendix B has 26 rows");
        vectors.Numbers[0].Should().Be(new Rfc8785NumberSample(0x0000000000000000, "0", "Zero"));
        vectors.Numbers.Should().ContainSingle(sample => sample.Comment == "Max pos int").Which.Json.Should().Be("9007199254740992");
        vectors.Numbers.Where(sample => sample.Json is null).Select(sample => sample.Comment).Should().Equal("NaN", "Infinity");
        vectors.Numbers.Last().Json.Should().Be("1424953923781206.2");

        JsonNode.Parse(vectors.PrimitiveSample)!["literals"]!.AsArray().Should().HaveCount(3);
        vectors.PrimitiveSampleCanonical.Should().HaveCount(118, "Section 3.2.4 lists 118 bytes");
        vectors.PrimitiveSampleCanonical[..2].Should().Equal(0x7b, 0x22);
        vectors.PrimitiveSampleCanonical[^1].Should().Be(0x7d);

        JsonNode.Parse(vectors.SortingSample)!.AsObject().Count.Should().Be(7);
        vectors.SortedValues.Should().Equal(
            "Carriage Return", "One", "Control", "Latin Small Letter O With Diaeresis", "Euro Sign",
            "Emoji: Grinning Face", "Hebrew Letter Dalet With Dagesh");
    }

    [Test]
    public void TheContextExamplesOfTheParentAreRead()
    {
        FixtureCatalog.Context.SchemaUrl.Should().Be("https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json");
        FixtureCatalog.Context.MaxTableEntries.Should().Be(16384);
        FixtureCatalog.Context.MaxSpellingBytes.Should().Be(1024);

        var examples = FixtureCatalog.Context.Examples;
        examples.Should().HaveCount(19);
        examples.Where(example => example.IsValid).Should().HaveCount(10)
            .And.OnlyContain(example => example.Expanded != null && example.File.RelativePath.StartsWith("MorphirContext/examples/valid/"));
        examples.Where(example => !example.IsValid).Select(example => example.Expected).Should().BeEquivalentTo(
            "context.undeclared_schema", "context.unknown_alias", "context.duplicate_entry", "context.unknown_critical",
            "context.too_many_entries", "context.name_too_long", "context.after_document", "context.invalid_envelope",
            "context.invalid_entry");

        var v3Type = FixtureCatalog.Context.Example("v3-type");
        (v3Type.Ir, v3Type.Kind).Should().Be((3, "type"));
        JsonNode.Parse(FixtureCatalog.ReadText(v3Type.File))!["$schema"]!.GetValue<string>().Should().Be(FixtureCatalog.Context.SchemaUrl);
        JsonNode.Parse(FixtureCatalog.ReadText(v3Type.Expanded!))!.AsObject().ContainsKey("$schema").Should().BeFalse();
        FixtureCatalog.All.Should().Contain(FixtureCatalog.Context.Files);
    }

    [Test]
    [Arguments(3)]
    [Arguments(4)]
    public void TheContextExamplesOfOneIrVersionAreTheFilesOfOneTree(int ir)
    {
        Func<FixtureTreeFile, bool> IsEnvelope = file =>
            JsonNode.Parse(FixtureCatalog.ReadText(file.Entry))!.AsObject().ContainsKey("$schema");

        var every = FixtureCatalog.Context.Tree(ir, ContextTreeCompaction.EveryFile);
        every.Files.Select(file => file.Location).Should().Equal(
            "manifest.json",
            "pkg/my-org/billing/invoicing/invoice-line.type.json",
            "pkg/my-org/billing/invoicing/line-total.value.json",
            "pkg/my-org/billing/invoicing/module.json");
        every.Files.All(IsEnvelope).Should().BeTrue("every file is compacted");
        string[] kinds = ["manifest", "type", "value", "module"];
        every.Files.Select(file => file.Entry).Should().Equal(kinds.Select(kind => FixtureCatalog.Context.Example($"v{ir}-{kind}").File));

        FixtureCatalog.Context.Tree(ir, ContextTreeCompaction.NoFile).Files.Any(IsEnvelope).Should().BeFalse("no file is compacted");
        FixtureCatalog.Context.Tree(ir, ContextTreeCompaction.ValueFileOnly).Files.Where(IsEnvelope).Select(file => file.Location)
            .Should().Equal(FixtureCatalog.Context.TreeLocation("value"));
    }

    [Test]
    public void TheCompactionPolicyCasesOfTheParentAreRead()
    {
        FixtureCatalog.Context.Policy.Name.Should().Be("morphir.compact/0.1.0-draft.1");

        var cases = FixtureCatalog.Context.Policy.All;
        cases.Should().HaveCount(21);
        cases.Select(policyCase => policyCase.Id).Should().OnlyHaveUniqueItems();
        cases.Where(policyCase => !policyCase.Compacted).Select(policyCase => policyCase.Id).Should().BeEquivalentTo(
            "v4-manifest", "v3-manifest", "loss", "loss-boundary-declined");

        var nameUsedOnce = FixtureCatalog.Context.Policy.Case("name-used-once");
        (nameUsedOnce.Ir, nameUsedOnce.Kind, nameUsedOnce.Compacted).Should().Be((4, "type", true));
        nameUsedOnce.Input.RelativePath.Should().Be("MorphirContext/policy/cases/name-used-once/input.json");
        JsonNode.Parse(FixtureCatalog.ReadText(nameUsedOnce.Expected))!["$schema"]!.GetValue<string>().Should().Be(FixtureCatalog.Context.SchemaUrl);
        JsonNode.Parse(FixtureCatalog.ReadText(nameUsedOnce.Input))!.AsObject().ContainsKey("$schema").Should().BeFalse();
        FixtureCatalog.All.Should().Contain(FixtureCatalog.Context.Policy.Files);
    }

    [Test]
    public void TheNamingCorpusOfTheParentIsRead()
    {
        var corpus = FixtureCatalog.Context.Naming.Corpus;

        corpus.ShippedStyle.Should().Be("uppercase");
        corpus.Names.Should().HaveCount(17);
        corpus.Names.Should().ContainSingle(name => name.Canonical == "value-in-USD")
            .Which.LegacyArray.Should().Equal("value", "in", "u", "s", "d");
        corpus.Names.Where(name => name.LegacyArray == null).Select(name => name.Canonical).Should().Equal("FR2052A");
        corpus.Paths.Should().Equal("morphir/SDK", "US/FR2052A/data-tables", "my-org/domain/users");
        corpus.FqNames.Should().Equal("morphir/SDK:list#map", "my-org:domain#value-in-USD");
        corpus.Rejects.Should().HaveCount(14);
        corpus.Rejects.Where(reject => reject.ValidAsName).Select(reject => reject.Input).Should().Equal("value-in-USD", "USD-usd");
        FixtureCatalog.All.Should().Contain(FixtureCatalog.Context.Naming.Files);
    }

    [Test]
    public void TheReaderOfRfc8785RefusesATextWithoutItsVectors()
    {
        var act = () => Rfc8785Reader.Read("Appendix B.  Number Serialization Samples\n");
        act.Should().Throw<FormatException>();
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
    public void TheFileSpellingCasesOfTheKitAreRead()
    {
        var spellings = FixtureCatalog.Kit.FileSpellings;
        var feature = FixtureCatalog.ReadText(FixtureCatalog.Kit.DocumentTreeFeature);

        spellings.Select(spelling => (spelling.Id, spelling.LogicalPath)).Should().Equal(
            ("document-tree-0001", "manifest"), ("document-tree-0002", "module"), ("document-tree-0010", "manifest"));
        spellings.Select(spelling => spelling.Title).Should().Equal("Manifest file", "Module manifest, names only", "v3 manifest");

        // The canonical JSON is the kit's own spelling, kept as written.
        foreach (var spelling in spellings)
            feature.Should().Contain($"And its canonical JSON spelling is {spelling.CanonicalJson}\n", $"{spelling.Id} keeps the kit's canonical JSON");

        var v4Manifest = JsonNode.Parse(spellings[0].CanonicalJson)!;
        v4Manifest["formatVersion"]!.GetValue<int>().Should().Be(4);
        v4Manifest["pathBudget"]!.GetValue<int>().Should().Be(4000);
        JsonNode.Parse(spellings[2].CanonicalJson)!["formatVersion"]!.GetValue<string>().Should().Be("3.1.0");
        JsonNode.Parse(spellings[1].CanonicalJson)!["path"]!.GetValue<string>().Should().Be("my-org/domain");

        // document-tree-0002: a reader also accepts the module manifest's older member name, module for path.
        spellings[0].AcceptedSpellings.Should().BeEmpty();
        spellings[2].AcceptedSpellings.Should().BeEmpty();
        var accepted = JsonNode.Parse(spellings[1].AcceptedSpellings.Should().ContainSingle().Subject)!.AsObject();
        accepted["module"]!.GetValue<string>().Should().Be("my-org/domain");
        accepted.ContainsKey("path").Should().BeFalse();
    }

    [Test]
    public void ATreeOfAFileSpellingHoldsTheFileAndADefinitionForEachNameThatItLists()
    {
        var manifest = KitSpellingTrees.Canonical(FixtureCatalog.Kit.FileSpellings[0]);
        manifest.Files.Keys.Should().Equal("manifest");
        manifest.SpelledFile.Should().Be("manifest");

        var module = KitSpellingTrees.Canonical(FixtureCatalog.Kit.FileSpellings[1]);
        const string directory = "pkg/my-org/my-project/my-org/domain/";
        module.Files.Keys.Should().BeEquivalentTo(
            "manifest", directory + "module", directory + "user.type", directory + "user-_id.type", directory + "get-user.value");
        module.SpelledFile.Should().Be(directory + "module");
        module.Files["manifest"].Should().Be(FixtureCatalog.Kit.FileSpellings[0].CanonicalJson, "the module's tree has the manifest of its format version");
        JsonNode.Parse(module.Files[directory + "user-_id.type"])!["name"]!.GetValue<string>().Should().Be("user-ID");
        JsonNode.Parse(module.Files[directory + "get-user.value"])!["name"]!.GetValue<string>().Should().Be("get-user");

        var accepted = KitSpellingTrees.Accepted(FixtureCatalog.Kit.FileSpellings[1]);
        accepted.Files[directory + "module"].Should().Be(FixtureCatalog.Kit.FileSpellings[1].AcceptedSpellings[0]);
        FluentActions.Invoking(() => KitSpellingTrees.Accepted(FixtureCatalog.Kit.FileSpellings[0])).Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void TheWrittenTreeWithASecondManifestHasBothProfilesOfTheSameManifest()
    {
        var faulty = FixtureCatalog.Trees.WrittenV4WithASecondManifestInTheYamlProfile;
        var files = faulty.Tree.Files.ToDictionary(file => file.Location, file => file.Entry);

        files.Keys.Should().BeEquivalentTo(FixtureCatalog.Trees.WrittenV4LibraryWithDependency.Files.Select(file => file.Location).Append("manifest.yaml"));
        files["manifest.yaml"].Should().Be(files["manifest.json"]);
        faulty.Fault.File.Should().BeNull();
        faulty.Fault.Names.Should().Equal("manifest.json", "manifest.yaml");
        FluentActions.Invoking(() => faulty.Tree.With("manifest.yaml", files["manifest.json"], "twice")).Should().Throw<ArgumentException>();
    }

    [Test]
    public void TheTreeCasesAndTheFileSpellingsCoverEveryCaseOfTheFeature()
    {
        var feature = FixtureCatalog.ReadText(FixtureCatalog.Kit.DocumentTreeFeature);
        var ids = feature.Split('\n').Select(line => line.Trim())
            .Where(line => line.StartsWith("Scenario:", StringComparison.Ordinal))
            .Select(line => line["Scenario:".Length..].Trim().Split(' ')[0]);

        FixtureCatalog.Kit.DocumentTree.Select(kitCase => kitCase.Id)
            .Concat(FixtureCatalog.Kit.FileSpellings.Select(spelling => spelling.Id))
            .Should().BeEquivalentTo(ids).And.HaveCount(17);
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
    public void ThePinnedRdfXmlSuiteHasEveryActiveTestAndNoExclusions()
    {
        var suite = JsonNode.Parse(FixtureCatalog.ReadBytes(FixtureCatalog.RdfXml.Corpus))!;

        suite["revision"]!.GetValue<string>().Should().Be("56076c0efec4c353efcae2971be8f6005294ac64");
        suite["base"]!.GetValue<string>().Should().Be("https://w3c.github.io/rdf-tests/rdf/rdf11/rdf-xml/");
        suite["excluded"]!.AsArray().Should().BeEmpty("no pinned test declares a document type");
        var tests = suite["tests"]!.AsArray();
        tests.Count(test => test!["type"]!.GetValue<string>() == "eval").Should().Be(126);
        tests.Count(test => test!["type"]!.GetValue<string>() == "negative").Should().Be(40);
        tests.Should().OnlyContain(test => test!["type"]!.GetValue<string>() == "negative" || test!["result"] != null);
    }

    [Test]
    public void ThePinnedCanonicalizationSuiteHasEveryTest()
    {
        var suite = JsonNode.Parse(FixtureCatalog.ReadBytes(FixtureCatalog.RdfCanon.Corpus))!;

        suite["revision"]!.GetValue<string>().Should().Be("5ffaaa13c0e19a2a2f9e60d0ecd9efaf3edab633");
        var tests = suite["tests"]!.AsArray();
        tests.Count(test => test!["type"]!.GetValue<string>() == "eval").Should().Be(64);
        tests.Count(test => test!["type"]!.GetValue<string>() == "map").Should().Be(21);
        tests.Count(test => test!["type"]!.GetValue<string>() == "negative").Should().Be(1);
        tests.Count(test => test!["hashAlgorithm"]!.GetValue<string>() == "SHA384").Should().Be(2, "one evaluation test and its map test use SHA-384");
    }

    [Test]
    public void AnUnknownCaseIdIsRefused()
    {
        var act = () => FixtureCatalog.Kit.Case("document-tree-9999");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*document-tree-9999*");
    }
}
