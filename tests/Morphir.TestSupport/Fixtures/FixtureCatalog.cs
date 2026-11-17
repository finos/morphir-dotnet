// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// The one owner of the files below <c>tests/Fixtures</c> that tests reach by intent. Each entry has its SHA-256 and
/// the <c>PROVENANCE.md</c> that has its row. The files are embedded in this assembly, so a test reads them without
/// knowing where the repository is. A file below <c>tests/Fixtures</c> is an entry here or is in a directory of
/// <see cref="ExistingOwners"/>; <c>FixtureOwnershipTests</c> in <c>tests/Morphir.Build.Tests</c> fails on any other.
/// </summary>
public static class FixtureCatalog
{
    private const string ResourcePrefix = "Morphir.TestSupport.Fixtures/";

    /// <summary>The pinned kit data of finos/morphir <c>spec/ir/mck/</c>, as reference data. The kit is not run.</summary>
    public static class Kit
    {
        private const string Provenance = "MorphirTrees/PROVENANCE.md";

        /// <summary>The kit's document-tree cases.</summary>
        public static FixtureEntry DocumentTreeFeature { get; } = new(
            "the kit's document-tree cases", "MorphirTrees/kit/document-tree.feature",
            "a1aa74103357f788fcd0e6dac589b9f7e9c2e519b3f98eec66033c4d47148524", Provenance);

        /// <summary>The kit's single-document cases, which the tree cases refer to.</summary>
        public static FixtureEntry DistributionsFeature { get; } = new(
            "the kit's distribution cases", "MorphirTrees/kit/distributions.feature",
            "05ae2cfe848f98bced7a6dda407a6c08ffac15ed0e334173f7025de963fa3936", Provenance);

        /// <summary>The kit's version and YAML profile cases, which the tree cases refer to.</summary>
        public static FixtureEntry VersionsFeature { get; } = new(
            "the kit's version and YAML profile cases", "MorphirTrees/kit/versions.feature",
            "b300695cc442fff827b093289eaa3e4a05bbac2221f37b9ea8d4e262ca2ece09", Provenance);

        /// <summary>The pinned kit files.</summary>
        public static IReadOnlyList<FixtureEntry> Files { get; } = [DocumentTreeFeature, DistributionsFeature, VersionsFeature];

        private static readonly Lazy<KitFeature> DocumentTreeCases =
            new(() => KitTreeCaseReader.ReadFeature(DocumentTreeFeature.RelativePath, ReadText(DocumentTreeFeature)));

        /// <summary>
        /// Each case of <c>document-tree.feature</c> with a tree or a diagnostic, in the kit's order. The cases that pin
        /// only one tree file's spelling are <see cref="FileSpellings"/>.
        /// </summary>
        public static IReadOnlyList<KitTreeCase> DocumentTree => DocumentTreeCases.Value.Trees;

        /// <summary>
        /// Each case of <c>document-tree.feature</c> that pins the spelling of one tree file and has no tree
        /// (<c>document-tree-0001</c>, <c>-0002</c>, <c>-0010</c>), in the kit's order. With <see cref="DocumentTree"/>
        /// it covers every case of the feature.
        /// </summary>
        public static IReadOnlyList<KitFileSpelling> FileSpellings => DocumentTreeCases.Value.FileSpellings;

        /// <summary>The tree case with this kit id.</summary>
        public static KitTreeCase Case(string id) =>
            DocumentTree.FirstOrDefault(kitCase => kitCase.Id == id)
            ?? throw new KeyNotFoundException($"The pinned kit has no tree case '{id}'. Its tree cases are {string.Join(", ", DocumentTree.Select(kitCase => kitCase.Id))}.");
    }

    /// <summary>The repository's IR fixtures in <c>tests/Fixtures/MorphirIR</c>, by intent.</summary>
    public static class Models
    {
        private const string Provenance = "MorphirIR/PROVENANCE.md";

        /// <summary>A v3 Library from finos/morphir (the greeting example): typed attributes and deep nesting.</summary>
        public static FixtureEntry V3Library { get; } = new(
            "a v3 Library", "MorphirIR/greeting-example-v3.json",
            "539664c494509047ff485e37aff86a900978fa717bde8da30c24dc72599a0423", Provenance);

        /// <summary>A v3 Library with one module and one value, <c>daily-rate</c>.</summary>
        public static FixtureEntry V3DailyRate { get; } = new(
            "a v3 Library with one value", "MorphirIR/daily-rate-v3.json",
            "d93da69798fd94ac1362738587b0d3586533173daef67ad11e7695a167a37f37", Provenance);

        /// <summary>The v4 form of <see cref="V3DailyRate"/>.</summary>
        public static FixtureEntry V4DailyRate { get; } = new(
            "a v4 Library with one value", "MorphirIR/daily-rate-v4.json",
            "1209abae3b32764518e01138fe42a57212cd4c3791963fe00e1ecf46ae109f7f", Provenance);

        /// <summary>A v3 Library whose value's body does not have its declared type.</summary>
        public static FixtureEntry V3DailyRateTypeMismatch { get; } = new(
            "a v3 Library with a type mismatch", "MorphirIR/daily-rate-mismatch-v3.json",
            "b8d68dd08cff4b218d66a2f1b73fa866cf483e0e0691f3e472da152d997519d3", Provenance);

        /// <summary>A v3 Library with a constant expression across two modules.</summary>
        public static FixtureEntry V3ConstantFolding { get; } = new(
            "a v3 Library with a constant expression", "MorphirIR/constant-folding-v3.json",
            "84d6311b9c71bf915e9dbdb4b26afc4ff2c6b8529980cf1d9dc1528a3b3071ee", Provenance);

        /// <summary>The v4 form of <see cref="V3ConstantFolding"/>.</summary>
        public static FixtureEntry V4ConstantFolding { get; } = new(
            "a v4 Library with a constant expression", "MorphirIR/constant-folding-v4.json",
            "edbab7c2b612d93cf075c7a71b78882f3a796b9e403cc7586937d6549179703e", Provenance);

        /// <summary>
        /// <see cref="V4DailyRate"/> with the output type of its value spelled as a node with the tag <c>Struct</c>,
        /// which no type node has, in the flattened module spelling of <see cref="V4DailyRate"/>. The parent's v4
        /// schema checks a module's types and values only in the wrapper <c>{ "access", "value" }</c>, so the schema
        /// alone accepts this file, and <c>model verify</c> finds the node by decoding it.
        /// </summary>
        public static FixtureEntry V4DailyRateUnknownTypeTag { get; } = new(
            "a v4 Library with an unknown tag in one type node", "MorphirIR/daily-rate-unknown-type-tag-v4.json",
            "7d25e4f44621846ab7264439d0ca457a1c4e9e1fcb66faf1f515000e95ee992b", Provenance);

        /// <summary><see cref="V4DailyRateUnknownTypeTag"/> and the JSON location of its unknown tag.</summary>
        public static FaultyFixtureModel V4WithAnUnknownTypeTag { get; } = new(
            V4DailyRateUnknownTypeTag,
            new FixtureFault(null, "/distribution/Library/def/modules/inventory/values/daily-rate/ExpressionBody/outputType", []));

        /// <summary>Text that is not JSON.</summary>
        public static FixtureEntry Malformed { get; } = new(
            "a model file that is not JSON", "MorphirIR/malformed-model.txt",
            "e2579a145407c34c0a22f779fcd001d07fe56f3860645e0915f90e4543665bd1", Provenance);

        /// <summary>A v1 Library with no modules, the smallest valid model of format version 1.</summary>
        public static FixtureEntry V1EmptyLibrary { get; } = new(
            "a v1 Library with no modules", "MorphirIR/empty-library-v1.json",
            "adb360f70001e3f19a84fba55f323c08e6d446266710d8d6128afe04389fdc95", Provenance);

        /// <summary>A v2 Library with no modules, the smallest valid model of format version 2.</summary>
        public static FixtureEntry V2EmptyLibrary { get; } = new(
            "a v2 Library with no modules", "MorphirIR/empty-library-v2.json",
            "c13b52cff9d1fc3939e21d672ffc458d3a62991db343f7158bfffb0c103cc6a3", Provenance);

        /// <summary>A Library with no member <c>formatVersion</c>, which no schema accepts.</summary>
        public static FixtureEntry NoFormatVersion { get; } = new(
            "a model with no format version", "MorphirIR/no-format-version.json",
            "fd1150056eed9943ac2944d91c00ae4a83a0faa3a486d88a582b4754904a268e", Provenance);

        /// <summary>A model whose <c>formatVersion</c> is the string <c>"3"</c>, not a number.</summary>
        public static FixtureEntry StringFormatVersion { get; } = new(
            "a model whose format version is a string", "MorphirIR/string-format-version.json",
            "a772afcac09350b2792fc894010e4cab06e0ee19bcc7225d717f3a0e037e0562", Provenance);

        /// <summary>The model fixtures.</summary>
        public static IReadOnlyList<FixtureEntry> All { get; } =
        [
            V3Library, V3DailyRate, V4DailyRate, V3DailyRateTypeMismatch, V3ConstantFolding, V4ConstantFolding, Malformed,
            V1EmptyLibrary, V2EmptyLibrary, NoFormatVersion, StringFormatVersion, V4DailyRateUnknownTypeTag,
        ];
    }

    /// <summary>
    /// Document trees of this repository, as files below <c>tests/Fixtures/MorphirTrees</c>: one written by hand
    /// (<see cref="AcmeDemo"/>), and the trees that the document-tree store wrote from mother cases
    /// (<see cref="Written"/>), as <see cref="FixtureTree"/>s. A test reads them through a source of files:
    /// <see cref="ReadBytes"/>, a directory, an embedded provider, or <see cref="FixtureTrees.WriteTree"/>.
    /// </summary>
    public static class Trees
    {
        private const string AcmeDemoProvenance = "MorphirTrees/acme-demo/PROVENANCE.md";

        /// <summary>The directory of <see cref="AcmeDemo"/> below <c>tests/Fixtures</c>.</summary>
        public const string AcmeDemoDirectory = "MorphirTrees/acme-demo/";

        private static FixtureEntry AcmeDemoFile(string path, string sha256) =>
            new($"the acme/demo v4 tree file {path}", AcmeDemoDirectory + path, sha256, AcmeDemoProvenance);

        /// <summary>
        /// A v4 tree of the design's worked example, <c>acme/demo</c> with dependency <c>morphir/sdk</c>, whose manifest
        /// has <c>version</c>, <c>created</c> and <c>layout</c>. Each entry's path below <see cref="AcmeDemoDirectory"/>
        /// is the file's location in the tree.
        /// </summary>
        public static IReadOnlyList<FixtureEntry> AcmeDemo { get; } =
        [
            AcmeDemoFile("deps/morphir/sdk/@/basics/module.json", "0e47c0eb26d876b6f2db262a4bd12a9a1c5a8f0dfee16f65df971d9826db8ca6"),
            AcmeDemoFile("deps/morphir/sdk/@/basics/money.type.json", "f5d0b3a8151c9bc5f603ac5fe64d8185ade1d5a7ccbfaf558eb07a3e1b256c5e"),
            AcmeDemoFile("deps/morphir/sdk/@/basics/zero.value.json", "76413d075cfda4a3e31e8bddc9ff1146585c0f4c3710f9adaad4fd62f679203b"),
            AcmeDemoFile("manifest.json", "61eea341dfbc0f3a13c21b4bd818791af1fff7b473a5aba6359d404ae5fbaa33"),
            AcmeDemoFile("pkg/acme/demo/billing/invoice-line.type.json", "88345efdb446a5b6d8124dfd3f21334ce0da1270673ad1e23d66e6ccf9833d10"),
            AcmeDemoFile("pkg/acme/demo/billing/invoice.type.json", "e230307487bf89efdfa19f351685dfb8139e10df072a39fa50497e18c1a57767"),
            AcmeDemoFile("pkg/acme/demo/billing/module.json", "87858643a1037664b6e5b15a9b59f0b39800081aa0ab8bd7c50aa00ffa08fa4c"),
            AcmeDemoFile("pkg/acme/demo/billing/total.value.json", "4d912bf97b7298f07ae90213193eb13c9cae225cf3301536144130581c072f3a"),
        ];

        /// <summary><see cref="AcmeDemo"/> as a tree: a v4 tree whose manifest has <c>version</c> and <c>created</c>.</summary>
        public static FixtureTree AcmeDemoTree { get; } =
            FixtureTree.Below("the acme/demo v4 tree, whose manifest has a version and a created date", AcmeDemoDirectory, AcmeDemo);

        private const string WrittenProvenance = "MorphirTrees/PROVENANCE.md";

        private const string WrittenV4Directory = "MorphirTrees/written-v4-library-with-dependency/";

        private const string WrittenV3Directory = "MorphirTrees/written-v3-library-with-dependency/";

        private const string WrittenV4UnknownTypeTagDirectory = "MorphirTrees/written-v4-unknown-type-tag/";

        /// <summary>The directory of the module <c>pricing/models</c> of <c>my-org/finance</c> in the written trees.</summary>
        private const string Pricing = "pkg/my-org/finance/pricing/models/";

        private static FixtureEntry WrittenFile(string directory, string tree, string path, string sha256) =>
            new($"{tree} file {path}", directory + path, sha256, WrittenProvenance);

        private static readonly FixtureEntry[] WrittenV4Files =
        [
            WrittenFile(WrittenV4Directory, "the written v4 tree", "deps/morphir/_sdk/@/api/module.json", "e88833b3b5849cee61ee739eca26e44ee34dcffb179913dc0946045efa04e115"),
            WrittenFile(WrittenV4Directory, "the written v4 tree", "deps/morphir/_sdk/@/api/money.type.json", "60fdada51a9f8e69a326095b91d5c2c74fe3445e50e97c69171daf36055f98ab"),
            WrittenFile(WrittenV4Directory, "the written v4 tree", "deps/morphir/_sdk/@/api/zero.value.json", "8d31d6c801c2648a1a12d7e27019c14b429761343a57d67f8eac9472b2f4b0c2"),
            WrittenFile(WrittenV4Directory, "the written v4 tree", "manifest.json", "364827525a4e877ef167d91093626d76cd0bf87b9851802ecf8db67d3c510bf0"),
            WrittenFile(WrittenV4Directory, "the written v4 tree", Pricing + "default-currency.value.json", "765b553c9bf3198b4cebb8114b0968114fc3e18105e379563bf5bbf04dc86bf1"),
            WrittenFile(WrittenV4Directory, "the written v4 tree", Pricing + "invoice.type.json", "39db582b34df8e2180b22d5c74bb4f9fba0a85070e788e56b0feaf959b86033d"),
            WrittenFile(WrittenV4Directory, "the written v4 tree", Pricing + "module.json", "c40d1fa7ec51bb00a35006b9bf94cb0ae77c67db169d5d4c0deb163634f6028f"),
            WrittenFile(WrittenV4Directory, "the written v4 tree", Pricing + "same-invoice.value.json", "7b7b27ecd5a16f8c6d54922cf22392de81137362e13ff2f9bc8b99862850e253"),
            WrittenFile(WrittenV4Directory, "the written v4 tree", Pricing + "status.type.json", "fdc8acf6e3b600474bb815b8f2b53d8dea4c84d11554aed2ddbd62a74d18121f"),
        ];

        private static readonly FixtureEntry[] WrittenV3Files =
        [
            WrittenFile(WrittenV3Directory, "the written v3 tree", "deps/morphir/_sdk/@/api/module.json", "b6c2ba068b2caa8fab16e02cd0dcadfaa6c3e11e2fa0818e50868edc43a2c5ac"),
            WrittenFile(WrittenV3Directory, "the written v3 tree", "deps/morphir/_sdk/@/api/money.type.json", "113ffed5a1167e3ac72e6d9d53d3181798a173b210cb4d0806f53e1f1d415ece"),
            WrittenFile(WrittenV3Directory, "the written v3 tree", "deps/morphir/_sdk/@/api/zero.value.json", "ae1a65a6bc4a22dabcf2f2bc6afa2bf1592922ad8edf2d411f0d84278236a804"),
            WrittenFile(WrittenV3Directory, "the written v3 tree", "manifest.json", "78a3a5b2f928c3622e391b15c6ba97439270e4db2578ac1748d05e105ee100b6"),
            WrittenFile(WrittenV3Directory, "the written v3 tree", Pricing + "default-currency.value.json", "1ee1fbe087017465b141c0b2d5455f03cadafa3b05106098a98c2e7d587800de"),
            WrittenFile(WrittenV3Directory, "the written v3 tree", Pricing + "invoice.type.json", "a2eb31f5ccb5e1d63b6e594c6df153521980d5bba4db0cf304854acf50aa8904"),
            WrittenFile(WrittenV3Directory, "the written v3 tree", Pricing + "module.json", "46b86cbbf579eb4b1262f76dc0e63db8b2781267e7a4d842db682215bc9c61a0"),
            WrittenFile(WrittenV3Directory, "the written v3 tree", Pricing + "same-invoice.value.json", "b269aaa997d9b9ac1e314a69025e4f8450b0ba09173aa735c46f2bc1789cba7b"),
            WrittenFile(WrittenV3Directory, "the written v3 tree", Pricing + "status.type.json", "ff987a5f45e2bd1973cea494d0e091ff7c2fed788f24ded79b5b6179cf1b767f"),
        ];

        private static readonly FixtureEntry WrittenV4UnknownTypeTagFile =
            WrittenFile(WrittenV4UnknownTypeTagDirectory, "the written v4 tree with an unknown type tag", Pricing + "invoice.type.json",
                "ca2b3a89a1a982fe7a59c6c491920ee365fde3ef5fdc555e451290e9fde43039");

        /// <summary>
        /// The files of the trees that the document-tree store wrote from cases of the distribution mother.
        /// <c>WrittenTreeTests</c> in <c>tests/Morphir.Models.Tests</c> fails when the store writes other bytes.
        /// </summary>
        public static IReadOnlyList<FixtureEntry> Written { get; } = [.. WrittenV4Files, .. WrittenV3Files, WrittenV4UnknownTypeTagFile];

        /// <summary>
        /// The tree of <c>DistributionMother.v4LibraryWithDependency</c> (<c>my-org/finance</c>, module
        /// <c>pricing/models</c>, dependency <c>morphir/SDK</c>) as the document-tree store writes it, the store that
        /// <c>model convert</c> writes through: <c>DocumentTreeMother.v4LibraryWithDependency</c>.
        /// </summary>
        public static FixtureTree WrittenV4LibraryWithDependency { get; } =
            FixtureTree.Below("the tree that the store writes from a v4 Library with a dependency", WrittenV4Directory, WrittenV4Files);

        /// <summary>
        /// The classic tree of <c>DistributionMother.v3LibraryWithDependency</c>, with numbered attribute slots, as the
        /// store writes it, every file saying <c>"3.1.0"</c>: <c>DocumentTreeMother.v3LibraryWithDependency</c>.
        /// </summary>
        public static FixtureTree WrittenV3LibraryWithDependency { get; } =
            FixtureTree.Below("the tree that the store writes from a v3 Library with a dependency", WrittenV3Directory, WrittenV3Files);

        /// <summary>
        /// <see cref="WrittenV4LibraryWithDependency"/> with the type expression of <c>invoice</c> spelled as a node
        /// with the tag <c>Struct</c>, which no type node has: <c>DocumentTreeMother.Invalid.typeFileWithAnUnknownTypeTag</c>.
        /// </summary>
        public static FaultyFixtureTree WrittenV4WithAnUnknownTypeTag { get; } = new(
            WrittenV4LibraryWithDependency.WithFile(Pricing + "invoice.type.json", WrittenV4UnknownTypeTagFile,
                "the written v4 tree with an unknown tag in one type node"),
            new FixtureFault(Pricing + "invoice.type.json", "/def/Public/TypeAliasDefinition/typeExp", []));

        /// <summary>
        /// <see cref="WrittenV4LibraryWithDependency"/> with no file for the value <c>default-currency</c>, which the
        /// module file of <c>pricing/models</c> lists: <c>DocumentTreeMother.Invalid.moduleListingAMissingValue</c>.
        /// A report names the missing file, the module file and the value.
        /// </summary>
        public static FaultyFixtureTree WrittenV4WithAMissingValueFile { get; } = new(
            WrittenV4LibraryWithDependency.Without(Pricing + "default-currency.value.json",
                "the written v4 tree with no file for one value"),
            new FixtureFault(Pricing + "default-currency.value.json", null, [Pricing + "module.json", "default-currency"]));

        /// <summary>
        /// <see cref="WrittenV4LibraryWithDependency"/> with its manifest also at <c>manifest.yaml</c>, the manifest of the
        /// YAML profile (JSON is YAML, so the bytes are the same): a tree of two profiles, which a reader refuses with
        /// <c>tree.ambiguous_profile</c>, naming both manifests and no one file.
        /// </summary>
        public static FaultyFixtureTree WrittenV4WithASecondManifestInTheYamlProfile { get; } = new(
            WrittenV4LibraryWithDependency.With("manifest.yaml", WrittenV4Files.Single(file => file.RelativePath == WrittenV4Directory + "manifest.json"),
                "the written v4 tree with a second manifest in the YAML profile"),
            new FixtureFault(null, null, ["manifest.json", "manifest.yaml"]));
    }

    /// <summary>
    /// RFC 8785, the JSON Canonicalization Scheme, as the RFC Editor publishes it: the source of the test vectors of the
    /// canonical JSON writer.
    /// </summary>
    public static class Rfc8785
    {
        /// <summary>The plain text of RFC 8785.</summary>
        public static FixtureEntry Text { get; } = new(
            "the text of RFC 8785", "Rfc8785/rfc8785.txt",
            "63d52294eb0e3f0014174288186d388b4ddbf2c67d1ce8af1d9726eb0c3ab240", "Rfc8785/PROVENANCE.md");

        private static readonly Lazy<Rfc8785Vectors> Read = new(() => Rfc8785Reader.Read(ReadText(Text)));

        /// <summary>The examples of the RFC: its number samples, its primitive sample and its sorting sample.</summary>
        public static Rfc8785Vectors Vectors => Read.Value;
    }

    /// <summary>
    /// The name-context data of finos/morphir (<c>ir-context</c> 0.1.0-draft.1), pinned as reference data below
    /// <c>tests/Fixtures/MorphirContext</c>: the parent defines the envelope, its refusals and the expected expansions.
    /// </summary>
    public static class Context
    {
        private const string Provenance = "MorphirContext/PROVENANCE.md";

        /// <summary>The directory of the envelope examples below <c>tests/Fixtures</c>.</summary>
        public const string ExamplesDirectory = "MorphirContext/examples/";

        private static FixtureEntry ExampleFile(string path, string sha256) =>
            new($"the name-context example {path}", ExamplesDirectory + path, sha256, Provenance);

        /// <summary>The index of the examples: each case's file, IR version, file kind, expected outcome and twin.</summary>
        public static FixtureEntry Cases { get; } = ExampleFile("cases.json", "af1a59afdfbd8ae17c7724512f9ce8739df81fcafd6f78128a58e77d71dfa2dc");

        /// <summary>Each example file and expanded twin, by its path below <see cref="ExamplesDirectory"/>.</summary>
        public static IReadOnlyList<FixtureEntry> ExampleFiles { get; } =
        [
            ExampleFile("expanded/v3-manifest.json", "c2c8a7f3bd68e78f24e2c95c430461b1448c2994e2c5ad1bf873c0bc93a80ef3"),
            ExampleFile("expanded/v3-module.json", "240f7695b3e8dea2f03c9201d218ba5933129deca7583ff80918206800828c21"),
            ExampleFile("expanded/v3-single-file.json", "6095ee94f276efb07075adbbc41242ea8494f686a05faf38c9a556e90c41c738"),
            ExampleFile("expanded/v3-type.json", "116f2e287b96d097c602994ca14c30aa1d3238cd3c945f233ccdde5ea13f787b"),
            ExampleFile("expanded/v3-value.json", "a0c2faf84b7479ded2e595d1734c1035c4f865a93b930bd45302a01d3c354a99"),
            ExampleFile("expanded/v4-manifest.json", "1f67490668d233d8b2e6a53cc4d312ee725eef837effdec1b899d6e767c1d0a9"),
            ExampleFile("expanded/v4-module.json", "8469e9cb66313616c859419bbf1d0514ed77026ffa3dd6a4b1c4fba2214b5e5e"),
            ExampleFile("expanded/v4-single-file.json", "ed8dc46dfb35e75ad2f3bd6afeeea52f20cdef6e869c6226b186cfd39c8c9e5f"),
            ExampleFile("expanded/v4-type.json", "7802da9f3c6d409d3f20e4f1b7f969de7ba7b69b021e2cf4a15e48313fab8f7e"),
            ExampleFile("expanded/v4-value.json", "a33aa101c97101bb3ed38d1a560b1735eee9acd285e00410f6d88c6fb193de2d"),
            ExampleFile("invalid/context-after-document.json", "eb748071304c955bb0cd9e3a2d158894c889d749797cb0d9a4317c1ed5697d36"),
            ExampleFile("invalid/duplicate-entry.json", "10e58f219400543684d371f1a427875d56289d2b22a60cfa0e42b0d6b7aba440"),
            ExampleFile("invalid/name-too-long.json", "42c85f4a0caae1b875c3afd671b0596294808549037a853ec596cd5aa92008fb"),
            ExampleFile("invalid/too-many-entries.json", "f86c270d0a61607d62dbd356039b687bec9c493b2be5e35fc0078ae3987d757c"),
            ExampleFile("invalid/undeclared-schema.json", "49bbc95d6d8599edcbfbd81033f65a33d81b50a7365ae2d108132003ff329f08"),
            ExampleFile("invalid/unknown-alias.json", "1bd3db99eec20dd4626bd5e348e0b9219ecc89ba92cd39e8e95f2b4a0df5a693"),
            ExampleFile("invalid/unknown-critical.json", "fb950b9b9463e46ceab12c1ae6c9a9d3c048518bc0f8cf3667c2cae872521e4b"),
            ExampleFile("valid/v3-manifest.json", "2c1608b66f23df6b6fe50d91f4e520bd74b7f4a24d92dffefc8e3902656ca95b"),
            ExampleFile("valid/v3-module.json", "1667174463458a1e047ded1adeedf2d616a95fd053d72f38848d39d031562513"),
            ExampleFile("valid/v3-single-file.json", "ef822e3859dcf730e3f6fef365c19fe4f65937e8edfcc7990a348dd434ceb93e"),
            ExampleFile("valid/v3-type.json", "e536f0ee95f6f42c708cc03fa5d8fd0db2bbb595b2dc614b559ab6ed4ea8241c"),
            ExampleFile("valid/v3-value.json", "56b7151ea22b625f30a23c6374d48ee396fc7e305173cc7f8357bd808bb1bf52"),
            ExampleFile("valid/v4-manifest.json", "a146a74881ceec07a1246bae075ce52cde1b0595308e45191d3920e7398cf52c"),
            ExampleFile("valid/v4-module.json", "c3ce4d01b7f2590fdfe55af5ce48891d126adfe06bdd2bd806878b31f3eb65ae"),
            ExampleFile("valid/v4-single-file.json", "a1583d61b731a0734d8b866747432f3ff08b642473feb91d7afc5ed017f1f589"),
            ExampleFile("valid/v4-type.json", "db101eda5e41ff38e2f009098226e4d7cc15392f4b1307272d10e3654f8c9143"),
            ExampleFile("valid/v4-value.json", "94ec9bae07b361f6dabb7da1103e3aa122f20982a1d003738a9fd65599404a2e"),
        ];

        /// <summary>The pinned files.</summary>
        public static IReadOnlyList<FixtureEntry> Files { get; } = [Cases, .. ExampleFiles];

        /// <summary>
        /// The writer policy <c>morphir.compact/0.1.0-draft.1</c> and its fixed cases: each case's plain input and the
        /// bytes a writer of the policy must produce from it, worked out by hand in the parent.
        /// </summary>
        public static class Policy
        {
            /// <summary>The directory of the policy below <c>tests/Fixtures</c>.</summary>
            public const string Directory = "MorphirContext/policy/";

            private static FixtureEntry PolicyFile(string path, string sha256) =>
                new($"the compaction policy file {path}", Directory + path, sha256, Provenance);

            private static (FixtureEntry Input, FixtureEntry Expected) CaseFiles(string id, string inputSha256, string expectedSha256) =>
                (PolicyFile($"cases/{id}/input.json", inputSha256), PolicyFile($"cases/{id}/expected.json", expectedSha256));

            /// <summary>The index of the cases: each case's IR version, file kind and whether the policy compacts it.</summary>
            public static FixtureEntry Cases { get; } = PolicyFile("cases.json", "30a1f81fb36de0c16d27fd2da0ae5b945d0b044ca8ea4b453aeb344c64e3d7c6");

            private static readonly IReadOnlyList<(FixtureEntry Input, FixtureEntry Expected)> CaseEntries =
            [
                CaseFiles("alias-allocation-order", "b0849cd38a8eecbc8434abb70c5691c3eb436d542c30e552d03774da3fbd5bb6", "8bbe9b9f71b6ffa5572a98128e2b7898d8f0d2d3c4994bcebaf6a896376e1650"),
                CaseFiles("look-alike-literal", "17003e3d0805c5534a0806dc3a42550cffeb2668e94dd66d3a59e9199eb2d526", "4330a6a96238922e067a1ebc96a8406be1f9508a8717542fe20d7ff7fd786989"),
                CaseFiles("loss", "8b190fa3bc42529434b9ea60c77672ef6c181343238eb74e0bde2f32bdff1d47", "e4f4751f3575d340623fe0877c37a94dfc7bc60c2003aa84ed3011f88b8e3f99"),
                CaseFiles("loss-boundary-accepted", "5754658c078ef7e2a6c91556d109b9b12092f04b7697d3a02ac8718bdbf17cd1", "31894d067ae94b82ead16537ee5171d0681546ba60078b936b4146059a2f1065"),
                CaseFiles("loss-boundary-declined", "508b1976472a382daa84f5a820431e26ee96aeed4d4bce89249f00a585948d24", "f7f0f6e84b735cf6b826d691ece0072268ea868c5b16a95927050e4230cffc32"),
                CaseFiles("name-used-once", "a92482d6cf13773b925a895fc81e3d1d8fc32ae580ac9a5b14566d7fa07df654", "773e28d958fe38b2c7d680bcf9464cddcdee2f1edce99f9383673629c48e0785"),
                CaseFiles("non-ascii-content", "2c1976d2746d2da17cfd173423b848d6f356f6b104ff10f472d5e83ee2fce299", "7a514d56dbc6fa3669222c14d3a45382a008bc800b697214dc451100ccc433c1"),
                CaseFiles("table-order", "7a3b8852fc440c12f95580046373bd22475232162bc5065b91daa0e408096a78", "f0d935742646e528a1887ded40ae85bac1ac513f0dc14fd6e21738cb67e45f70"),
                CaseFiles("tie", "2918018c96afe65ff3f965472cca003d19be799eb0cddb51bfbd35c79686f267", "9d6d101e7de67aeab176b16b71ef740e9c254573382c066bdc7a7201cfafa8a4"),
                CaseFiles("v3-manifest", "15a9ac21999f566db71818b281293d3e0ba6d207c8c01fba9bb542a564d86be1", "931931d96940b4cefe0c72f12b23f247d207ff13229a959cc8ad4371d345f226"),
                CaseFiles("v3-module", "b82f7aedb41abf17284cc6c7b5bf0d44bc23ae369f8d1c37ea9d6bae0faa9899", "b0eaa6ffab8b88babb3569acb0107c7aeda0b6942511bf3547802e6e6c0baaed"),
                CaseFiles("v3-single-file", "9b710fdbe68e652e63c11a13c85bb939fee8a744e920cb76c29444d17b107971", "a6c55d93b534330cab4184aad98a3c794fa24e440ee0b3e596dd9b655b17396d"),
                CaseFiles("v3-type", "9cdee5ac533636ebd96737ce711943bf48a4983cbbe81dc0525808b7899cadca", "d25dc9016de0f0d57d1ee912bc8a223acf04218791c3f0dbaba115f3a54696a1"),
                CaseFiles("v3-value", "32d5cdd430cfcf1c11cfc69991ad7e32e2f82ea3e7a6fccfe2759cc16d1218cf", "aa79ceb5791718fb0fa0ba621ce02d30810f32940745b2d4bfc2f666d2f05403"),
                CaseFiles("v4-manifest", "33b360f36f3f2ca8857c56bc868835cd6dcef4dd753ca8c48028714482caced2", "580af09c3fa4c00af59438e482779cced39dd21af769d3087cd0644fa2d8795a"),
                CaseFiles("v4-module", "882fb7562030dc63d20bd4f7b43e208101b62c248622fca8a9af319253da2014", "26cf6103628dbd2fc7458a7d26f67e75baf4016dd9c005fdbef244a6321bb3bc"),
                CaseFiles("v4-single-file", "338baa506c270ffaa667afed5dd116e77d58c4329084bdf9226ea1bfba7f0a34", "0c4f00c49dc71b5eda77fdeb165b57df92b8836a23bd7b4b494e6c81c17e7a1d"),
                CaseFiles("v4-type", "b92972aa55c7808ed87f711d1dc818d57f7a4ffe5fc7ea00b7b07c05aa69ab9a", "9327b83ace0d0607e7b0f1f637c7c8ddce9517da2783b4f967d67a36076a25cf"),
                CaseFiles("v4-value", "6b0d959c17720785b161fde523f8d4e19f8e98a8a3409d6689023b6451d8eff6", "b542f1d5a421d83ac3c76503975b5aae242947a5d3e25dba2c487c8a85872791"),
            ];

            /// <summary>The pinned files.</summary>
            public static IReadOnlyList<FixtureEntry> Files { get; } =
                [Cases, .. CaseEntries.SelectMany(files => new[] { files.Input, files.Expected })];

            private static readonly Lazy<PolicyCaseIndex> Index = new(() => PolicyCaseIndex.Read(ReadText(Cases), Files, Directory));

            /// <summary>The policy's name, as the index states it.</summary>
            public static string Name => Index.Value.Policy;

            /// <summary>Every case of the index, in its order.</summary>
            public static IReadOnlyList<PolicyCase> All => Index.Value.Cases;

            /// <summary>The case with this id, such as <c>name-used-once</c>.</summary>
            public static PolicyCase Case(string id) =>
                All.FirstOrDefault(policyCase => policyCase.Id == id)
                ?? throw new KeyNotFoundException($"The pinned policy has no case '{id}'. Its cases are {string.Join(", ", All.Select(policyCase => policyCase.Id))}.");
        }

        /// <summary>
        /// The parent's naming conformance corpus: the canonical names, paths and FQNames and the refused names of
        /// the naming grammar, whose spellings a context table admits.
        /// </summary>
        public static class Naming
        {
            /// <summary>The corpus file.</summary>
            public static FixtureEntry Conformance { get; } = new(
                "the naming conformance corpus", "MorphirContext/naming/naming-conformance.json",
                "81b598e45bbe5361ec5cd178f85d9a8d538eb473a226ac1961d79561e3a11607", Provenance);

            /// <summary>The pinned files.</summary>
            public static IReadOnlyList<FixtureEntry> Files { get; } = [Conformance];

            private static readonly Lazy<NamingConformance> Read = new(() => NamingConformance.Read(ReadText(Conformance)));

            /// <summary>The corpus, in its shipped style.</summary>
            public static NamingConformance Corpus => Read.Value;
        }

        private static readonly Lazy<ContextExampleIndex> Index = new(() => ContextExampleIndex.Read(ReadText(Cases), ExampleFiles, ExamplesDirectory));

        /// <summary>The envelope's schema URL, as the index states it.</summary>
        public static string SchemaUrl => Index.Value.SchemaUrl;

        /// <summary>The most entries one table may hold, as the index states it.</summary>
        public static int MaxTableEntries => Index.Value.MaxTableEntries;

        /// <summary>The most UTF-8 bytes one expanded spelling may have, as the index states it.</summary>
        public static int MaxSpellingBytes => Index.Value.MaxSpellingBytes;

        /// <summary>Every example of the index, in its order.</summary>
        public static IReadOnlyList<ContextExample> Examples => Index.Value.Examples;

        /// <summary>The example with this id, such as <c>v4-type</c>.</summary>
        public static ContextExample Example(string id) =>
            Examples.FirstOrDefault(example => example.Id == id)
            ?? throw new KeyNotFoundException($"The pinned context examples have no case '{id}'. They are {string.Join(", ", Examples.Select(example => example.Id))}.");

        /// <summary>
        /// The location in a tree of each tree-file kind of the examples. The examples share one model, package
        /// <c>my-org/billing</c> with module <c>invoicing</c>, the type <c>invoice-line</c> and the value
        /// <c>line-total</c>, so their tree-file examples of one IR version are the files of one tree.
        /// </summary>
        public static IReadOnlyList<(string Kind, string Location)> TreeLocations { get; } =
        [
            ("manifest", "manifest.json"),
            ("module", "pkg/my-org/billing/invoicing/module.json"),
            ("type", "pkg/my-org/billing/invoicing/invoice-line.type.json"),
            ("value", "pkg/my-org/billing/invoicing/line-total.value.json"),
        ];

        /// <summary>The location in the examples' tree of the file of <paramref name="kind"/>, such as <c>type</c>.</summary>
        public static string TreeLocation(string kind) =>
            TreeLocations.FirstOrDefault(entry => entry.Kind == kind).Location
            ?? throw new KeyNotFoundException($"The examples' tree has no file of the kind '{kind}'.");

        /// <summary>
        /// The examples' model as a document tree of IR <paramref name="ir"/> (3 or 4): at each location the example of
        /// its kind (<c>v4-type</c>, ...) when <paramref name="compacted"/> compacts that kind, or else the example's
        /// plain twin.
        /// </summary>
        public static FixtureTree Tree(int ir, ContextTreeCompaction compacted)
        {
            if (ir is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(ir), ir, "The examples have trees of the IR versions 3 and 4.");
            return FixtureTree.Of($"the v{ir} tree of the name-context examples, {compacted}", TreeLocations.Select(entry =>
            {
                var example = Example($"v{ir}-{entry.Kind}");
                var isCompacted = compacted switch
                {
                    ContextTreeCompaction.NoFile => false,
                    ContextTreeCompaction.ValueFileOnly => entry.Kind == "value",
                    ContextTreeCompaction.EveryFile => true,
                    _ => throw new ArgumentOutOfRangeException(nameof(compacted), compacted, null),
                };
                return new FixtureTreeFile(entry.Location, isCompacted ? example.File : example.Expanded!);
            }));
        }
    }

    /// <summary>Every file that the catalog owns and serves.</summary>
    public static IReadOnlyCollection<FixtureEntry> All { get; } = [.. Kit.Files, .. Models.All, .. Trees.AcmeDemo, .. Trees.Written, Rfc8785.Text, .. Context.Files, .. Context.Policy.Files, .. Context.Naming.Files];

    /// <summary>
    /// The directories and files below <c>tests/Fixtures</c> that a test project owned before the catalog, with that
    /// project. A file in one of them passes the guard test without an entry; a new directory is not added here.
    /// </summary>
    public static IReadOnlyList<FixtureOwner> ExistingOwners { get; } =
    [
        new("CSharpGeneration/", "Morphir.BlackBox.Tests"),
        new("Configuration/", "Morphir.Configuration.IO.Tests"),
        new("ExtensionAcquisition/", "Morphir.Extensions.Installation.Tests"),
        new("LocalToolchains/", "Morphir.Toolchain.Tests"),
        new("Mep/", "Morphir.Frontend.FSharp.Tests"),
        new("MepGuestKit/", "Morphir.Conformance.Tests"),
        new("MepWasm/", "Morphir.Extensions.Wasm.Tests"),
        new("MorphirIon/", "Morphir.Ion.Tests"),
        new("MorphirOut/", "Morphir.Frontend.FSharp.Tests"),
        new("MorphirSDK/", "Morphir.SDK.Conformance.Tests"),
        new("NodeAddresses/", "Morphir.MiddleEnd.Tests"),
        new("NuGetExtension/", "Morphir.Extensions.Installation.Tests"),
        new("Toolchain/", "Morphir.Toolchain.Tests"),
        new("Unicode/", "Morphir.Toolchain.Tests"),
        new("WorkerGuests/", "Morphir.Tooling.Tests"),
        new("process-helper/", "Morphir.TestSupport"),
        // The loader that F# test projects link to read the embedded fixtures; code, not data.
        new("ResourceLoader.fs", "Morphir.CodeGeneration.FSharp.Tests"),
    ];

    /// <summary>The files that this assembly embeds, below <c>tests/Fixtures</c>.</summary>
    public static IReadOnlyList<string> EmbeddedFiles { get; } =
        typeof(FixtureCatalog).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The bytes of an entry, as pinned.</summary>
    public static byte[] ReadBytes(FixtureEntry entry)
    {
        using var stream = typeof(FixtureCatalog).Assembly.GetManifestResourceStream(ResourcePrefix + entry.RelativePath)
            ?? throw new InvalidOperationException($"The catalog does not embed {entry.RelativePath}; add it to tests/Morphir.TestSupport/Morphir.TestSupport.csproj.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The text of an entry, read as UTF-8.</summary>
    public static string ReadText(FixtureEntry entry) => Encoding.UTF8.GetString(ReadBytes(entry));

    /// <summary>
    /// The files below a fixtures directory (paths relative to it, with <c>/</c>) that have no owner: no entry in
    /// <see cref="All"/> and no place in <see cref="ExistingOwners"/>. Each <c>README.md</c> and <c>PROVENANCE.md</c>
    /// describes its folder and needs no owner.
    /// </summary>
    public static IReadOnlyList<string> UnownedFiles(string fixturesDirectory)
    {
        var owned = All.Select(entry => entry.RelativePath).ToHashSet(StringComparer.Ordinal);

        return Directory.EnumerateFiles(fixturesDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(fixturesDirectory, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => Path.GetFileName(path) is not ("README.md" or "PROVENANCE.md"))
            .Where(path => !owned.Contains(path))
            .Where(path => !ExistingOwners.Any(owner => owner.RelativePath.EndsWith('/')
                ? path.StartsWith(owner.RelativePath, StringComparison.Ordinal)
                : path == owner.RelativePath))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
