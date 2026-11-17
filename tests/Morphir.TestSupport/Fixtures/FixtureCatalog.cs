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
        /// which no type node has. Its module is in the wrapper <c>{ "access", "value" }</c>: the parent's v4 schema
        /// checks the types and values of a module only in that spelling.
        /// </summary>
        public static FixtureEntry V4DailyRateUnknownTypeTag { get; } = new(
            "a v4 Library with an unknown tag in one type node", "MorphirIR/daily-rate-unknown-type-tag-v4.json",
            "19de3b7d9bd17650ebc50302803c12b85791362555626803ac3a4866f4da196a", Provenance);

        /// <summary><see cref="V4DailyRateUnknownTypeTag"/> and the JSON location of its unknown tag.</summary>
        public static FaultyFixtureModel V4WithAnUnknownTypeTag { get; } = new(
            V4DailyRateUnknownTypeTag,
            new FixtureFault(null, "/distribution/Library/def/modules/inventory/value/values/daily-rate/ExpressionBody/outputType", []));

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
        /// The files of the trees that the document-tree store wrote from cases of the distribution mother, as
        /// <c>model convert</c> writes them. <c>WrittenTreeTests</c> in <c>tests/Morphir.Models.Tests</c> fails when the
        /// store writes other bytes.
        /// </summary>
        public static IReadOnlyList<FixtureEntry> Written { get; } = [.. WrittenV4Files, .. WrittenV3Files, WrittenV4UnknownTypeTagFile];

        /// <summary>
        /// The tree of <c>DistributionMother.v4LibraryWithDependency</c> (<c>my-org/finance</c>, module
        /// <c>pricing/models</c>, dependency <c>morphir/SDK</c>) as the store writes it, which is the tree that
        /// <c>model convert</c> writes from that model: <c>DocumentTreeMother.v4LibraryWithDependency</c>.
        /// </summary>
        public static FixtureTree WrittenV4LibraryWithDependency { get; } =
            FixtureTree.Below("the tree that model convert wrote from a v4 Library with a dependency", WrittenV4Directory, WrittenV4Files);

        /// <summary>
        /// The classic tree of <c>DistributionMother.v3LibraryWithDependency</c>, with numbered attribute slots, as the
        /// store writes it, every file saying <c>"3.1.0"</c>: <c>DocumentTreeMother.v3LibraryWithDependency</c>.
        /// </summary>
        public static FixtureTree WrittenV3LibraryWithDependency { get; } =
            FixtureTree.Below("the tree that model convert wrote from a v3 Library with a dependency", WrittenV3Directory, WrittenV3Files);

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
    }

    /// <summary>Every file that the catalog owns and serves.</summary>
    public static IReadOnlyCollection<FixtureEntry> All { get; } = [.. Kit.Files, .. Models.All, .. Trees.AcmeDemo, .. Trees.Written];

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
