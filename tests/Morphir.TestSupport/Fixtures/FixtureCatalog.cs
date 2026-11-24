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
        /// schema refuses this file but locates the fault only at the module, and <c>model verify</c> names the node by
        /// decoding it.
        /// </summary>
        public static FixtureEntry V4DailyRateUnknownTypeTag { get; } = new(
            "a v4 Library with an unknown tag in one type node", "MorphirIR/daily-rate-unknown-type-tag-v4.json",
            "7d25e4f44621846ab7264439d0ca457a1c4e9e1fcb66faf1f515000e95ee992b", Provenance);

        /// <summary><see cref="V4DailyRateUnknownTypeTag"/> and the JSON location of its unknown tag.</summary>
        public static FaultyFixtureModel V4WithAnUnknownTypeTag { get; } = new(
            V4DailyRateUnknownTypeTag,
            new FixtureFault(null, "/distribution/Library/def/modules/inventory/values/daily-rate/ExpressionBody/outputType", []));

        /// <summary>
        /// A v3 Specs single file, <c>"formatVersion": "3.1.0"</c>, as <c>model convert</c> writes it: the classic
        /// writer's file of <c>DistributionMother.v3Specs</c> (<c>DistributionMother.SingleFiles.v3SpecsFile</c>).
        /// </summary>
        public static FixtureEntry V3Specs { get; } = new(
            "a v3 Specs, as model convert writes it", "MorphirIR/specs-v3.json",
            "03e5bc1d910613719f9cd9fed8a91b21616b059979f0bab5e658e1b689b4fd93", Provenance);

        /// <summary>
        /// <see cref="V3Specs"/> with a member <c>metadata</c> in the specification of the value <c>default-currency</c>,
        /// where the classic model has no place for it (<c>DistributionMother.SingleFiles.v3SpecsWithANestedUnknownMember</c>).
        /// </summary>
        public static FixtureEntry V3SpecsWithAnUnknownMemberFile { get; } = new(
            "a v3 Specs with a member that the classic model has no place for", "MorphirIR/specs-unknown-member-v3.json",
            "c5cb852f6bb21ac2969e1da8292bfbb68a51c7dc07391f4cb0fb8b046d022499", Provenance);

        /// <summary><see cref="V3SpecsWithAnUnknownMemberFile"/> and the JSON location of its member, which a reader refuses with <c>unknown_member</c>.</summary>
        public static FaultyFixtureModel V3SpecsWithAnUnknownMember { get; } = new(
            V3SpecsWithAnUnknownMemberFile,
            new FixtureFault(null, "/distribution/3/modules/0/1/values/0/1/value/metadata", ["'metadata'"]));

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
            V1EmptyLibrary, V2EmptyLibrary, NoFormatVersion, StringFormatVersion, V4DailyRateUnknownTypeTag, V3Specs,
            V3SpecsWithAnUnknownMemberFile,
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

        private const string WrittenV3UnknownMemberDirectory = "MorphirTrees/written-v3-unknown-member/";

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

        private static readonly FixtureEntry WrittenV3UnknownMemberFile =
            WrittenFile(WrittenV3UnknownMemberDirectory, "the written v3 tree with an unknown member", Pricing + "default-currency.value.json",
                "3ef45299fb3ec24473042a452859b166301ed230b1118b4aade65d45c192ae22");

        /// <summary>
        /// The files of the trees that the document-tree store wrote from cases of the distribution mother.
        /// <c>WrittenTreeTests</c> in <c>tests/Morphir.Models.Tests</c> fails when the store writes other bytes.
        /// </summary>
        public static IReadOnlyList<FixtureEntry> Written { get; } = [.. WrittenV4Files, .. WrittenV3Files, WrittenV4UnknownTypeTagFile, WrittenV3UnknownMemberFile];

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
        /// <see cref="WrittenV3LibraryWithDependency"/> with a member <c>metadata</c> in the definition of the value
        /// <c>default-currency</c>, beside its <c>inputTypes</c>, <c>outputType</c> and <c>body</c>, where the classic
        /// model has no place for it: <c>DocumentTreeMother.Invalid.v3ValueFileWithANestedUnknownMember</c>. A reader
        /// refuses it with <c>unknown_member</c> at the member, so no conversion drops it.
        /// </summary>
        public static FaultyFixtureTree WrittenV3WithANestedUnknownMember { get; } = new(
            WrittenV3LibraryWithDependency.WithFile(Pricing + "default-currency.value.json", WrittenV3UnknownMemberFile,
                "the written v3 tree with an unknown member in one value definition"),
            new FixtureFault(Pricing + "default-currency.value.json", "/def/value/metadata", ["'metadata'"]));

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
    /// The upstream IR JSON documents of <c>tests/Fixtures/MorphirIon</c> (see its <c>PROVENANCE.md</c>) that the classic
    /// and v4 readers and writers are checked against: the migrated pairs, the record vectors, the v4 examples of
    /// finos/morphir and the plain IR of the MEP model-transport kit. The Ion files beside them stay with
    /// <c>Morphir.Ion.Tests</c>, which owns the folder.
    /// </summary>
    public static class UpstreamIr
    {
        private const string Provenance = "MorphirIon/PROVENANCE.md";

        /// <summary>The folder of the documents below <c>tests/Fixtures</c>.</summary>
        public const string Directory = "MorphirIon/";

        private static FixtureEntry Document(string name, string sha256) =>
            new($"the upstream IR document {name}", Directory + name, sha256, Provenance);

        /// <summary>Each document, by its path below <see cref="Directory"/>, in ordinal order.</summary>
        public static IReadOnlyList<FixtureEntry> Documents { get; } =
        [
            Document("pairs/books-and-records-example.json", "1b16d3ee46aa6dec65841000e3bf8d28a990837843aef63e0003c2ca4829a15e"),
            Document("pairs/complete-example.json", "fbaa25336c90b7f369c17f5f2104496d3de481dff211532319724c465ef1b095"),
            Document("pairs/direct-rules-example.json", "dd78f3baab0b7639567134c8d23dffc5a337f82a9521f0a73e33e635860118da"),
            Document("pairs/greeting-example.json", "2cf6a695a9f3859da30cea76a4b4d9acb81d3d55023affa5a9bc8a9c005e7004"),
            Document("pairs/rule-set-example.json", "a5bc8d6e2acfd4ff8d9a6c54cf1e9058de597c710ba458b31a8be1bd0c8414d1"),
            Document("pairs/v3-daily-rate.json", "5afebe65d650a3467d116aa3d83b26f01631657ab03ef2dfb4e146b1573f001d"),
            Document("pairs/v3-library-nodes.json", "f2ca693fc65ca3244bb96227ea45c7af2fb98be9faf7100e50cf3c94eac621ed"),
            Document("pairs/v3-specs.json", "079390570095546e6ff721edcea96e492d8f66640befdba25c5725d63aaadf2f"),
            Document("pairs/v4-application.json", "256b18910a28aede380d4ed4aefba5e6e60b790d0d828e4fe28276bb54771756"),
            Document("pairs/v4-empty-library.json", "2ba7fb00c8c0c1cc9b956043e1bbe5919be303a2946ed7558c796ad15e23fe69"),
            Document("pairs/v4-library-nodes.json", "f4251a5bae9d4d7abb8566e1e48d11eb8d31b3cfa22b269e8c96014a70e1affc"),
            Document("pairs/v4-specs.json", "5bc82d132605dd88db4d661246b8df0b3af91694dc3533414b82f2d30956c4ef"),
            Document("records/application.json", "8adc711586ca42d1ca0c8e63809f2b6eb56ca53903bcd087fdc323a6bd6805ed"),
            Document("records/document-numbers.json", "ed37fcd6d6f3e903fe95582d46fb11506e561f5094f5f8ae2f07b1be31d3d0cb"),
            Document("records/empty-library.json", "7a05a8318f10ad481d2e33f9b7b6122c41f548777200ab8f15a98ad2e082c363"),
            Document("records/library.json", "6ba238abdac2e6cfcc58e09324f39ba36d255c755dd636f163813b314edc123b"),
            Document("records/shorthand.json", "3869de77813dc556ec54e42b746fb313fb0b8841df34e06b67f91d37c84d7504"),
            Document("records/specs.json", "83e2b01e62de866aac3742b45e05644d3ad83c46f571d1c092ff8bd996b73fe0"),
            Document("records/v3-library.json", "210c803484dbda09b9f647a4190f3f8aa1dbb7fedc5243bfb00814b1170ded4c"),
            Document("records/v3-shorthand.json", "312dd7b9f690fbf55732ae94349c7366ff644dd54250f0509d6dd41c11d99eb3"),
            Document("v4/books-and-records-example.json", "bdc25e9fb332a49f7f12bfa80c907eca482ae5b63bad07575ef06194ab99fda3"),
            Document("v4/complete-example.json", "30fbea9879b377dde11d0a4916b926dba152069e7b24bd43e4cf33cb27fcd979"),
            Document("mep/fixtures/plain-ir.json", "fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f"),
        ];

        /// <summary>The path of a document below <see cref="Directory"/>, such as <c>pairs/v3-specs.json</c>.</summary>
        public static string NameOf(FixtureEntry document) => document.RelativePath[Directory.Length..];

        /// <summary>The folders below <see cref="Directory"/> each of whose <c>*.json</c> files is a document.</summary>
        public static IReadOnlyList<string> Folders { get; } = ["pairs/", "records/", "v4/"];

        /// <summary>
        /// The <c>*.json</c> files of <see cref="Folders"/> below a fixtures directory (paths below <see cref="Directory"/>,
        /// with <c>/</c>) that <see cref="Documents"/> does not list, so that no test would read them.
        /// <c>FixtureOwnershipTests</c> in <c>tests/Morphir.Build.Tests</c> fails on any.
        /// </summary>
        public static IReadOnlyList<string> Uncatalogued(string fixturesDirectory)
        {
            var listed = Documents.Select(NameOf).ToHashSet(StringComparer.Ordinal);
            return Folders
                .SelectMany(folder =>
                {
                    var path = Path.Combine(fixturesDirectory, (Directory + folder).TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
                    return System.IO.Directory.Exists(path)
                        ? System.IO.Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly).Select(file => folder + Path.GetFileName(file))
                        : Enumerable.Empty<string>();
                })
                .Where(name => !listed.Contains(name))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
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
        public static FixtureEntry Cases { get; } = ExampleFile("cases.json", "5b0d7d4da904c8752f61298750245f87596978c4a983898649d0ced1e2e2e8a0");

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
            ExampleFile("invalid/critical-not-a-list.json", "132661b9d83c52978db782520e3453602a3fba217e211f5cd174bfe58cff0b15"),
            ExampleFile("invalid/duplicate-entry.json", "10e58f219400543684d371f1a427875d56289d2b22a60cfa0e42b0d6b7aba440"),
            ExampleFile("invalid/mixed-case-entry.json", "59269e99f8e87272de2826d9926cccaf6dd061a071a287f08de670e733fce059"),
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
            public static FixtureEntry Cases { get; } = PolicyFile("cases.json", "73d59160c3af6fa1468ab9816415b0e1a01a9d69e394abf56b8e213c075ab60a");

            private static readonly IReadOnlyList<(FixtureEntry Input, FixtureEntry Expected)> CaseEntries =
            [
                CaseFiles("alias-allocation-order", "b0849cd38a8eecbc8434abb70c5691c3eb436d542c30e552d03774da3fbd5bb6", "8bbe9b9f71b6ffa5572a98128e2b7898d8f0d2d3c4994bcebaf6a896376e1650"),
                CaseFiles("exact-numbers", "c760fce5ec62da6bea51d324c88a3159c34a1950f4d3ec09e6d6611da76efb97", "7895d1d3f39c426641b9b7a5283d1e7a7cc6fd75da26751f3e1eeb307fdc8f54"),
                CaseFiles("look-alike-literal", "17003e3d0805c5534a0806dc3a42550cffeb2668e94dd66d3a59e9199eb2d526", "4330a6a96238922e067a1ebc96a8406be1f9508a8717542fe20d7ff7fd786989"),
                CaseFiles("loss", "8b190fa3bc42529434b9ea60c77672ef6c181343238eb74e0bde2f32bdff1d47", "e4f4751f3575d340623fe0877c37a94dfc7bc60c2003aa84ed3011f88b8e3f99"),
                CaseFiles("loss-boundary-accepted", "5754658c078ef7e2a6c91556d109b9b12092f04b7697d3a02ac8718bdbf17cd1", "31894d067ae94b82ead16537ee5171d0681546ba60078b936b4146059a2f1065"),
                CaseFiles("loss-boundary-declined", "508b1976472a382daa84f5a820431e26ee96aeed4d4bce89249f00a585948d24", "f7f0f6e84b735cf6b826d691ece0072268ea868c5b16a95927050e4230cffc32"),
                CaseFiles("name-used-once", "a92482d6cf13773b925a895fc81e3d1d8fc32ae580ac9a5b14566d7fa07df654", "773e28d958fe38b2c7d680bcf9464cddcdee2f1edce99f9383673629c48e0785"),
                CaseFiles("non-ascii-content", "2c1976d2746d2da17cfd173423b848d6f356f6b104ff10f472d5e83ee2fce299", "7a514d56dbc6fa3669222c14d3a45382a008bc800b697214dc451100ccc433c1"),
                CaseFiles("ordered-inputs", "47d233546f5d55713a63ca467f3ed06b3f93adf180e6d48f07282642f26c1bc9", "237926afbea6eaba0130d7d19a39a4a6c0a0a676e6524e25651641008b292b3a"),
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

    /// <summary>The pinned bounded-generation transcript from finos/morphir.</summary>
    public static class Mep
    {
        public static FixtureEntry BoundedGenerationSession { get; } = new(
            "the shared MEP bounded-generation orders session", "Mep/session-model-access.md",
            "2b564178596e1c7420fc8c689b31e7dbd68dabaed3d57ac0e9497ce21886cb34", "Mep/PROVENANCE.md");
    }

    /// <summary>The parent kit's shared Orders scale module.</summary>
    public static class OrdersScale
    {
        /// <summary>The pinned streaming Extism guest used by the production-payload controls.</summary>
        public static FixtureEntry Guest { get; } = new(
            "the Orders scale streaming WASM guest", "BoundedOrdersScale/orders-scale.wasm",
            "9c67d3e6130726d62abf662e1d7a656ab6e92c2059b634bea2e1d444fac7a8ef",
            "BoundedOrdersScale/PROVENANCE.md");
    }

    /// <summary>Test guests that select a fixed memory and frame allowance.</summary>
    public static class BoundedWasmLimits
    {
        private const string Directory = "BoundedWasmLimits/";
        private const string Provenance = Directory + "PROVENANCE.md";

        public static FixtureEntry StateBelowPage { get; } = new(
            "the WASM guest that selects state below one memory page", Directory + "state-below-page.wasm",
            "e1a45ea2a086d4b9935ba091719818654f1db9d564586e5c7dec355e0318150b", Provenance);

        public static FixtureEntry FrameOneUnder { get; } = new(
            "the WASM guest that selects a one-byte-short frame", Directory + "frame-one-under.wasm",
            "16a79de45520e9e779b8e46f9917ef33d59f844297a681523beed2b05c24a541", Provenance);

        public static FixtureEntry FrameExact { get; } = new(
            "the WASM guest that selects an exact-size frame", Directory + "frame-exact.wasm",
            "99a139a7d0126ffe9fb490dd082aabef9e4bd8ec50db6f557265cf812b0feede", Provenance);

        public static IReadOnlyList<FixtureEntry> Files { get; } =
        [
            StateBelowPage, FrameOneUnder, FrameExact,
            Source("state-below-page", "927d631b62442b938799c2a507ab0efbe59a52291b66dd20baab5088604b22e3"),
            Source("frame-one-under", "39995def01a1f9b411902c109337c69233bf1b7d3ef44505c4983117c4dc208f"),
            Source("frame-exact", "b1e236f111b8466638514ec427fd8bf880078864ef3154c019b7e5ee5d24397c"),
        ];

        private static FixtureEntry Source(string name, string sha256) =>
            new($"the source of the bounded WASM guest {name}", Directory + name + ".wat", sha256, Provenance);
    }

    /// <summary>The frozen bounded-generation receipt, schema and replay controls from finos/morphir.</summary>
    public static class BoundedGenerationReceipts
    {
        private const string Directory = "MorphirBoundedGenerationReceipts/";
        private const string Provenance = Directory + "PROVENANCE.md";

        public static FixtureEntry Controls { get; } = new(
            "the fixed bounded-generation receipt replay controls", Directory + "receipt.controls.json",
            "b5be6a8bbcc8a24306499ed58c529180569e75df0a392da511ac014b5fb88f3e", Provenance);

        public static FixtureEntry Schema { get; } = new(
            "the bounded-generation receipt schema", Directory + "receipt.schema.json",
            "daea631f7b6ded1ff981395d7477c66827ffdb9fd4f17a59458d1e1576602b07", Provenance);

        public static FixtureEntry Sample { get; } = new(
            "the synthetic qualifying WASM receipt", Directory + "fixtures/receipt-qualified-wasm.json",
            "48097614346a2a302cdd43959fdc03d949e05279e027c0a6d9692b1bf918d735", Provenance);

        /// <summary>The independent real scale A/B cases from parent PR #1198.</summary>
        public static FixtureEntry ScaleCases { get; } = new(
            "the real orders scale A/B expectations", Directory + "orders-scale.cases.json",
            "cde3dc58b49c229bac45af57126c2aa8b2f62c6e06766c84dd7f99bb8b0c4910", Provenance);

        public static IReadOnlyList<FixtureEntry> Files { get; } = [Controls, Schema, Sample, ScaleCases];
    }

    /// <summary>The frozen orders plain and contextual document trees for v3.1 and v4, in JSON and YAML.</summary>
    public static class BoundedOrders
    {
        public const string Directory = "MorphirBoundedOrders/";
        private const string Provenance = Directory + "PROVENANCE.md";

        /// <summary>Independent parent reader facts, never derived from a .NET codec.</summary>
        public static FixtureEntry Expected { get; } = Document("expected.json", "a5e585531daa19b5343130b5cd065eb8ac7ba5a7655d8ea21dc1d605fe134c58");
        /// <summary>The frozen parent version/profile selection grammar.</summary>
        public static FixtureEntry Cases { get; } = Document("cases.json", "fd72b801461873db04911ade8d4c5b9194da793a0c89cafc55be791290d1f8e4");

        private static FixtureEntry Document(string path, string sha256) =>
            new($"the frozen bounded orders document {path}", Directory + path, sha256, Provenance);

        public static FixtureEntry ExpandedV4 { get; } =
            Document("v4/single-file.json", "33649fb3718da07237c603ba7f9095621c59acee6c99def79c7a3220e4a2c47b");

        public static IReadOnlyList<FixtureEntry> Files { get; } =
        [
            Expected, Cases,
            Document("v3.1/contextual-json/deps/morphir/_sdk/@/basics/add.value.json", "c2cb8924aa3820614f4fde953265512be6fe87aabb9687548f55710109c5bb3b"),
            Document("v3.1/contextual-json/deps/morphir/_sdk/@/basics/int.type.json", "7ec7004dcece668d34211e4a90b25365477ee96c4e7973676e2cc72d084bf755"),
            Document("v3.1/contextual-json/deps/morphir/_sdk/@/basics/module.json", "9ffd7f011f6cccc2491fa94263ccb5591935a97ec00b92f11da1c3438b85ab38"),
            Document("v3.1/contextual-json/manifest.json", "650507f6f808c59f8114453bf9cb2517bd2affe97819b6fded58a538adc3c930"),
            Document("v3.1/contextual-json/pkg/acme/orders/audit/module.json", "4c6d31dc53bdd878e2bb57afc43678b91d7428f026aedbf17e9c99cffc963791"),
            Document("v3.1/contextual-json/pkg/acme/orders/domain/module.json", "f43986af381b890a652f26c69519afc8293e4ac7daed4b96464628439b909217"),
            Document("v3.1/contextual-json/pkg/acme/orders/domain/order.type.json", "94f0fd96a052690745b8d7a118737357189beca015bc2654869d12d6ad58301e"),
            Document("v3.1/contextual-json/pkg/acme/orders/domain/order.value.json", "db0d0ee2e2200a252849efa4708e1599f675c6b2c8d05b2088b43c6d4f472d17"),
            Document("v3.1/contextual-json/pkg/acme/orders/domain/total-in-_usd.value.json", "1885e3b748bb4c4564ab1d420999fe2847d1292d1c056908c46f287a991d9844"),
            Document("v3.1/contextual-yaml/deps/morphir/_sdk/@/basics/add.value.yaml", "7fd60a4ae08775acd1d402c88172438451c98a165aae43a7ccad18fc2000c06d"),
            Document("v3.1/contextual-yaml/deps/morphir/_sdk/@/basics/int.type.yaml", "6832cac69b727a1c4c4f7da35cd0b3231f7c57ba23644b653b58c53282843dab"),
            Document("v3.1/contextual-yaml/deps/morphir/_sdk/@/basics/module.yaml", "5e470f56c26dc3c09e9ed9ffcc825fc3d8e2a527427973b7f0e556f07d4cb452"),
            Document("v3.1/contextual-yaml/manifest.yaml", "87de6576a564b4948779c687eaf47f9b60b4bdf5e47a058cc5bced4bfe33885a"),
            Document("v3.1/contextual-yaml/pkg/acme/orders/audit/module.yaml", "b63eb8e4d645d5ff662e5bfcc5468a462b2b24532c8f8d1a6ab66dadfe659e8a"),
            Document("v3.1/contextual-yaml/pkg/acme/orders/domain/module.yaml", "3dd4e97f73113c73b603b2b3cca1dc5372a8701474afe7a4437f8a771cb74880"),
            Document("v3.1/contextual-yaml/pkg/acme/orders/domain/order.type.yaml", "4ad4cce7243213474ce72750c6ff561495a30e3f51db03cfffea1412a3fb0522"),
            Document("v3.1/contextual-yaml/pkg/acme/orders/domain/order.value.yaml", "35dd385bbffb207ffc253152f2b18e2d1b25841b0ac53e144de30df9467e8306"),
            Document("v3.1/contextual-yaml/pkg/acme/orders/domain/total-in-_usd.value.yaml", "b4b537a6c29557f4c768bd34eaa2f9aff5e03823b71db9d3f34898f9338dcec1"),
            Document("v4/contextual-json/deps/morphir/_sdk/@/basics/add.value.json", "250b426de0516e1a1b35167565657272add6ed4f773ab65e0fd9163aac04581e"),
            Document("v4/contextual-json/deps/morphir/_sdk/@/basics/int.type.json", "b1a8e7b33039224bb85ba5bbbbf0cbfa9a17213c46b90e47b5c6b098b23aff43"),
            Document("v4/contextual-json/deps/morphir/_sdk/@/basics/module.json", "0a4cd495c97cd3ed46dc4e5f0169dc76620f82978ea4a6366402ebf45a63ae53"),
            Document("v4/contextual-json/manifest.json", "dc765f931bd12dad3dbb9a7367ff907c8122e1c485758b6dcd4f43037224fd79"),
            Document("v4/contextual-json/pkg/acme/orders/audit/module.json", "436d562855210a0a6946a16b5ac1ef405036c7604e32b72a5e5044207cf1d112"),
            Document("v4/contextual-json/pkg/acme/orders/domain/module.json", "11ebadc15ca3b7d932c07eefbca3eef6f5ea2d116ff1d091c0102c2a9c49f3aa"),
            Document("v4/contextual-json/pkg/acme/orders/domain/order.type.json", "81f7e0d6fcbc524504c305caca4970596b2a8d7f4c82d1e03636600f59d85213"),
            Document("v4/contextual-json/pkg/acme/orders/domain/order.value.json", "dbc695403c12e5d2af6a8ab7d5ae5d8e16b8ba333ff4b60c5499620825bc1229"),
            Document("v4/contextual-json/pkg/acme/orders/domain/total-in-_usd.value.json", "6d668cc8651bcb2b90906286d09377dce9e5dbc1f564c1a3200b0f3f8a44435a"),
            Document("v4/contextual-yaml/deps/morphir/_sdk/@/basics/add.value.yaml", "d38845a4cc251ef3d0116c456987d8f2da9084ec3c0529bf412078f8541c5531"),
            Document("v4/contextual-yaml/deps/morphir/_sdk/@/basics/int.type.yaml", "5ac936d834f5d1c860e6e77b756344571c3d1615d505bf147ad39ab66f07197d"),
            Document("v4/contextual-yaml/deps/morphir/_sdk/@/basics/module.yaml", "ea51ad7e89eddd43cef86039bca32aabc18289096d1c5e3fc9e7006e6597c8f2"),
            Document("v4/contextual-yaml/manifest.yaml", "07e3c9905604d32d5ee81d1454b40265bd3e747018d97c68c07e66db52e0c5a2"),
            Document("v4/contextual-yaml/pkg/acme/orders/audit/module.yaml", "c3e4b5cff4f47396e27e4b9bb9f171c0aa3304cfa814d56db84b6b4c5dafc1fd"),
            Document("v4/contextual-yaml/pkg/acme/orders/domain/module.yaml", "facf19da57f2b184ee4df0744e129f49c556521117e6e90ffb65318f9b385edb"),
            Document("v4/contextual-yaml/pkg/acme/orders/domain/order.type.yaml", "ae93f6990dfcdf4f0de6bda7ec73c02abbe470ed469f837546f7b9fa34d1768a"),
            Document("v4/contextual-yaml/pkg/acme/orders/domain/order.value.yaml", "d937a64df6112da43a39344f802ed19cf354ae06c9677bc66b69a701fc53ff13"),
            Document("v4/contextual-yaml/pkg/acme/orders/domain/total-in-_usd.value.yaml", "bb8b6ae644f5484caed867c55cf81f666c550b5589168396471b3dbb1877ed2d"),
            Document("v3.1/plain-json/deps/morphir/_sdk/@/basics/add.value.json", "34286f5a937e100e8d16e65d900abba4c2604336a8958357b57693fb5188092d"),
            Document("v3.1/plain-json/deps/morphir/_sdk/@/basics/int.type.json", "200803d530066ef7d7b1412c6479382ce7aec12e237669faa212948dfe13ed21"),
            Document("v3.1/plain-json/deps/morphir/_sdk/@/basics/module.json", "a05f890f089a810f0da35ef220ee3e7e09d3dcbd1dd34439f217dff46a380fe6"),
            Document("v3.1/plain-json/manifest.json", "25138745c6c761304fe7d83e8f3c45cb1cb997f9c4f0da07f89a872cf712fe8a"),
            Document("v3.1/plain-json/pkg/acme/orders/audit/module.json", "b2f8491eb86ffa936596bea3ce70ef1ae4cbda315076bd08196c5a6f247b963b"),
            Document("v3.1/plain-json/pkg/acme/orders/domain/module.json", "46e88978c783a5743b4be092e10193942a1d251d27e8ca048f1058adedecd626"),
            Document("v3.1/plain-json/pkg/acme/orders/domain/order.type.json", "b09c5e72c5b1f353cc334c6059ebd031eedb85790cc536fff86533c749882c6a"),
            Document("v3.1/plain-json/pkg/acme/orders/domain/order.value.json", "1736ca10d492fbdf53e4f88974fe54efbe91607b50a1668769eade755ed5416f"),
            Document("v3.1/plain-json/pkg/acme/orders/domain/total-in-_usd.value.json", "a80f1398ad347b12b03a4f2a96d17c365d9a980014a8fc876a743709dd205bae"),
            Document("v3.1/plain-yaml/deps/morphir/_sdk/@/basics/add.value.yaml", "d01bd4797f3b2e77e1e1e884ea5a58429b301d573438cbfdf57ec937006e5b02"),
            Document("v3.1/plain-yaml/deps/morphir/_sdk/@/basics/int.type.yaml", "13bb51879327ea6dd9504b120858034184358b542951585a7df97585946c4f36"),
            Document("v3.1/plain-yaml/deps/morphir/_sdk/@/basics/module.yaml", "d4ae72b6abe51f4e0696200cc2a3889b2dbcc14398b66d93d465f94534f6266d"),
            Document("v3.1/plain-yaml/manifest.yaml", "a4368e1e8fdd8e2291e4cfa3d697a0929419b85ffd2a9ef39f62a924e7c13e9f"),
            Document("v3.1/plain-yaml/pkg/acme/orders/audit/module.yaml", "8c3a729de403bc3e0eb226e9bee8acef0cfa8259534ca0bc06765c22e58a4934"),
            Document("v3.1/plain-yaml/pkg/acme/orders/domain/module.yaml", "0b7e6e7425788c7733766ebfa689800984124b6012cdb2901e95b251d50a136c"),
            Document("v3.1/plain-yaml/pkg/acme/orders/domain/order.type.yaml", "5fbd79e69a49489bf6ad1c67f9275e64c51d54db98df0299615e742c9c5fec97"),
            Document("v3.1/plain-yaml/pkg/acme/orders/domain/order.value.yaml", "e771ff98beb16ce07198a8b2b27aa2108c3da593e79ff798af9ed401aa875f46"),
            Document("v3.1/plain-yaml/pkg/acme/orders/domain/total-in-_usd.value.yaml", "452bf177ca7bf1ad2d40c24221ed7d69d6d05e523d01f4a0d2dc3ae44d3e2189"),
            Document("v4/plain-json/deps/morphir/_sdk/@/basics/add.value.json", "d0a05067e8f35760070a362cc7ac9576af334237d7b63358e6649ad8146a4581"),
            Document("v4/plain-json/deps/morphir/_sdk/@/basics/int.type.json", "f705c58f4063de276c2d5b2b90519c7b90f77df8ea1102644f9109854924c823"),
            Document("v4/plain-json/deps/morphir/_sdk/@/basics/module.json", "f560f925c078f6c2dfe8cfd9d63492a7a6de56a27ce193932a7c8ec3ae0a777f"),
            Document("v4/plain-json/manifest.json", "16733854b16b962c16cbfe28e0b62134092ec268e6e2c21fda6452e3ba71becf"),
            Document("v4/plain-json/pkg/acme/orders/audit/module.json", "d9b482373b92d4d6f26467e4f98efcbf03f90abc0a0e3e367d5a281bdad59be6"),
            Document("v4/plain-json/pkg/acme/orders/domain/module.json", "f20321dd16d4e5f0f3c0b1a3c438028e7c52c8ad50070a1d1ad127e0cb5171a9"),
            Document("v4/plain-json/pkg/acme/orders/domain/order.type.json", "69b508ca018feb78f371e13bab6124e70e3c00337c2b7058745b44aad7cd56f4"),
            Document("v4/plain-json/pkg/acme/orders/domain/order.value.json", "23b1c85f2ee470a468e335098619d394fd23d7a70ee592d39a28612dc5e98fa0"),
            Document("v4/plain-json/pkg/acme/orders/domain/total-in-_usd.value.json", "f4e5dcae87eb9b022bf446711b4173cd04839da7cf4e2f122ca1600143acf9d4"),
            Document("v4/plain-yaml/deps/morphir/_sdk/@/basics/add.value.yaml", "8e3ff55a36b35261aa3cbd8dbba172f54bd4dc4104a713f4108b863fe2285e0c"),
            Document("v4/plain-yaml/deps/morphir/_sdk/@/basics/int.type.yaml", "db89fb411de58f801304f8c20678534d48d970550aa0296f3691aa908210c59e"),
            Document("v4/plain-yaml/deps/morphir/_sdk/@/basics/module.yaml", "a104becbf555457691c83508bc9eb1a1661b0a7b4d61786507fef0f3128dd937"),
            Document("v4/plain-yaml/manifest.yaml", "f566e3642bb3ea4d6eb62ab2f245063b9bb9e6d2782824ad7f34b98604021914"),
            Document("v4/plain-yaml/pkg/acme/orders/audit/module.yaml", "cf826379493b5397249f7683fe0d287af013f87f727264ea16fdcd28127eaeb1"),
            Document("v4/plain-yaml/pkg/acme/orders/domain/module.yaml", "415e5db36f30bda0186205f1851e48d5c2cde9db738e9c26c3c6e3591c2a3db7"),
            Document("v4/plain-yaml/pkg/acme/orders/domain/order.type.yaml", "557e0df61d43341e0163cbc1588f31737ae9e3a71d6a46b56468ba8da0a48b45"),
            Document("v4/plain-yaml/pkg/acme/orders/domain/order.value.yaml", "b9f627924c280a8d4bd064139db38b782891f7564f928b87ab3398e9923c5c78"),
            Document("v4/plain-yaml/pkg/acme/orders/domain/total-in-_usd.value.yaml", "e5fe89e9adb3211f987583804132d55eaf30b33a8e474fc128db5face8cb720c"),
        ];
    }


    /// <summary>The W3C RDF 1.1 N-Quads syntax cases for the dataset codec.</summary>
    public static class RdfNQuads
    {
        /// <summary>The positive and negative syntax inputs with their expected outcomes.</summary>
        public static FixtureEntry Corpus { get; } = new(
            "the RDF 1.1 N-Quads syntax cases", "RdfNQuads/rdf11-syntax.json",
            "a0c3dd336f6335e85627f9a49e79f01e16d07036dc7aac02c66dc4718f773a4a", "RdfNQuads/PROVENANCE.md");
    }

    /// <summary>The W3C RDF 1.1 XML syntax tests for the RDF/XML reader.</summary>
    public static class RdfXml
    {
        /// <summary>The evaluation inputs with their expected N-Triples, and the negative syntax inputs.</summary>
        public static FixtureEntry Corpus { get; } = new(
            "the RDF 1.1 XML syntax tests", "RdfXml/rdf11-xml.json",
            "0e7b96c043203942b71a49d0f0499d1880fee58de16ab6482a436a9b4b77307d", "RdfXml/PROVENANCE.md");
    }

    /// <summary>The W3C RDF Dataset Canonicalization (RDFC-1.0) tests.</summary>
    public static class RdfCanon
    {
        /// <summary>The evaluation, map and negative tests with their inputs and expected results.</summary>
        public static FixtureEntry Corpus { get; } = new(
            "the RDFC-1.0 canonicalization tests", "RdfCanon/rdfc10.json",
            "8de10283843668593cea24874775ed1639e3438fc6020e626d5fdbd3c91059fd", "RdfCanon/PROVENANCE.md");
    }

    /// <summary>The CommonMark examples for the OKF document parser.</summary>
    public static class OkfDocuments
    {
        /// <summary>The CommonMark 0.31.2 example corpus.</summary>
        public static FixtureEntry CommonMark { get; } = new(
            "the CommonMark 0.31.2 parser examples", "OkfDocuments/commonmark-0.31.2.json",
            "d431b29d97b6f73e69d547109cf5081578fac931e72afe95639ebe766c1b2a20", "OkfDocuments/PROVENANCE.md");
    }

    /// <summary>Pinned inputs for the WASM host command tests.</summary>
    public static class WasmHost
    {
        private const string Provenance = "MepWasm/PROVENANCE.md";

        /// <summary>The upstream WASM host cases and expectations.</summary>
        public static FixtureEntry Corpus { get; } = new(
            "the WASM host case corpus", "MepWasm/wasm-host.cases.json",
            "b97f18174214aac8a0a4d972db38564fcbea0b780ebcf6e1120d13bb145e9e19", Provenance);

        /// <summary>The module with a host import in an unknown namespace.</summary>
        public static FixtureEntry UnknownNamespaceModule { get; } = new(
            "the unknown-namespace WASM module", "MepWasm/fixtures/wasm/env-open-socket.wasm",
            "bebbfeda31c2703c2022bf4ba27206fc0fcf22a540dda195476569c973e7addd", Provenance);

        /// <summary>The provenance record read by the kit at startup.</summary>
        public static byte[] ReadProvenance() =>
            ReadResource("Morphir.TestSupport.Provenance/" + Provenance);
    }

    /// <summary>The diagrams.mbt test corpora at 60a8dd1, one JSON file per corpus with each input's path, SHA-256 and text.</summary>
    public static class DiagramsMbt
    {
        private const string Provenance = "DiagramsMbt/PROVENANCE.md";

        /// <summary>The Mermaid 12.2.0 sequence diagram examples.</summary>
        public static FixtureEntry Sequence { get; } = new(
            "the Mermaid 12.2.0 sequence diagram examples", "DiagramsMbt/mermaid-sequence.json", "7cd477db928f6a841c776c0c27c090d9f20fe6fe63be360877e3915333729605", Provenance);

        /// <summary>The Mermaid flowchart corpus.</summary>
        public static FixtureEntry Flowcharts { get; } = new(
            "the Mermaid flowchart corpus", "DiagramsMbt/mermaid-flowcharts.json", "da3b94ad1aae41a70c100aa0262018d9e7a8921b5e17107b1e26d4863bb67426", Provenance);

        /// <summary>The Mermaid state diagram corpus.</summary>
        public static FixtureEntry States { get; } = new(
            "the Mermaid state diagram corpus", "DiagramsMbt/mermaid-states.json", "f9b8acc9efc8429d2ca8109ab40540c13028e7a9b6f7a128ca24d6e7cb8180ce", Provenance);

        /// <summary>The Mermaid swimlane corpus.</summary>
        public static FixtureEntry Swimlanes { get; } = new(
            "the Mermaid swimlane corpus", "DiagramsMbt/mermaid-swimlanes.json", "aa6df8e9ecc434022ab278f31a4f3f94ec22ac9c91819239ec092a5967e8cac6", Provenance);

        /// <summary>The Mermaid architecture corpus.</summary>
        public static FixtureEntry Architecture { get; } = new(
            "the Mermaid architecture corpus", "DiagramsMbt/mermaid-architecture.json", "7a31ac39512bc195e4dc8df3db7ab411e54d932e9b3001070b5fcf01a69fac52", Provenance);

        /// <summary>The ZenUML corpus.</summary>
        public static FixtureEntry ZenUml { get; } = new(
            "the ZenUML corpus", "DiagramsMbt/zenuml.json", "5151feea04dc965d591bdee6d008231e9f40189a3d3ac18a1c543a82b441a9b7", Provenance);

        /// <summary>The PlantUML activity corpus.</summary>
        public static FixtureEntry PlantUmlActivity { get; } = new(
            "the PlantUML activity corpus", "DiagramsMbt/plantuml-activity.json", "eb8042306a84f9434d6cacedaca8c836dedee8a097b9b7178ac250132594430a", Provenance);

        /// <summary>The PlantUML state corpus.</summary>
        public static FixtureEntry PlantUmlState { get; } = new(
            "the PlantUML state corpus", "DiagramsMbt/plantuml-state.json", "3706af83fa431d5fd00d39b9655389c7e2334bd75377aea14e3e468be91b4ffe", Provenance);

        /// <summary>The PlantUML 1.2026.9 envelope, regex, EBNF, JSON and YAML corpus.</summary>
        public static FixtureEntry PlantUml { get; } = new(
            "the PlantUML 1.2026.9 envelope, regex, EBNF, JSON and YAML corpus", "DiagramsMbt/plantuml.json", "6937882e83a81962d78e2dd75067ada54922f104db9a894d465b464764933266", Provenance);

        /// <summary>The BPMN model, traversal and query corpus.</summary>
        public static FixtureEntry Bpmn { get; } = new(
            "the BPMN model, traversal and query corpus", "DiagramsMbt/bpmn.json", "8199636723b4ee0f413bdf25d07c1aa244ae675055f6530fdd0e409982b19acb", Provenance);

        /// <summary>Every pinned corpus.</summary>
        public static IReadOnlyList<FixtureEntry> Files { get; } =
            [Sequence, Flowcharts, States, Swimlanes, Architecture, ZenUml, PlantUmlActivity, PlantUmlState, PlantUml, Bpmn];
    }

    /// <summary>Digests of diagram output recorded before the diagram export refactor, which must not change.</summary>
    public static class DiagramExport
    {
        private const string Provenance = "DiagramExport/PROVENANCE.md";

        /// <summary>The SVG of every pinned corpus diagram under each baseline option set.</summary>
        public static FixtureEntry SvgDigests { get; } = new(
            "the SVG digests of every pinned corpus diagram", "DiagramExport/svg-digests.json", "b01892cf062c67f446a7c07563554010eb26205eb89bb1f5bbb88c4e184ea512", Provenance);

        /// <summary>Every digest fixture.</summary>
        public static IReadOnlyList<FixtureEntry> Files { get; } = [SvgDigests];
    }

    /// <summary>Every file that the catalog owns and serves.</summary>
    public static IReadOnlyCollection<FixtureEntry> All { get; } =
    [
        .. Kit.Files, Mep.BoundedGenerationSession, .. BoundedWasmLimits.Files, OrdersScale.Guest, .. BoundedGenerationReceipts.Files,
        .. Models.All, .. BoundedOrders.Files, BoundedOrders.ExpandedV4, .. Trees.AcmeDemo, .. Trees.Written,
        .. UpstreamIr.Documents, Rfc8785.Text, RdfNQuads.Corpus, RdfXml.Corpus, RdfCanon.Corpus, OkfDocuments.CommonMark,
        WasmHost.Corpus, WasmHost.UnknownNamespaceModule, .. Context.Files, .. Context.Policy.Files,
        .. Context.Naming.Files, .. DiagramsMbt.Files, .. DiagramExport.Files
    ];

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
    public static byte[] ReadBytes(FixtureEntry entry) => ReadResource(ResourcePrefix + entry.RelativePath);

    private static byte[] ReadResource(string resourceName)
    {
        using var stream = typeof(FixtureCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"No embedded resource named {resourceName} was found in the fixture catalog.");
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
