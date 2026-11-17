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

        /// <summary>Text that is not JSON.</summary>
        public static FixtureEntry Malformed { get; } = new(
            "a model file that is not JSON", "MorphirIR/malformed-model.txt",
            "e2579a145407c34c0a22f779fcd001d07fe56f3860645e0915f90e4543665bd1", Provenance);

        /// <summary>The model fixtures.</summary>
        public static IReadOnlyList<FixtureEntry> All { get; } =
            [V3Library, V3DailyRate, V4DailyRate, V3DailyRateTypeMismatch, V3ConstantFolding, V4ConstantFolding, Malformed];
    }

    /// <summary>
    /// Document trees written by hand for this repository, as files below <c>tests/Fixtures/MorphirTrees</c>. A test
    /// reads them through a source of files: <see cref="ReadBytes"/>, a directory or an embedded provider.
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

    /// <summary>Every file that the catalog owns and serves.</summary>
    public static IReadOnlyCollection<FixtureEntry> All { get; } = [.. Kit.Files, .. Models.All, .. Trees.AcmeDemo, Rfc8785.Text];

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
