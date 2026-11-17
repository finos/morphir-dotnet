// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Files;
using Morphir.IR.Trees;
using Morphir.Models.TestSupport;
using Morphir.TestSupport.Fixtures;
using Morphir.Tooling.Features.ModelVerify;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Tests.Features.ModelVerify;

/// <summary>
/// The checks of a document tree by <c>model verify</c> (design VER-1), on trees in memory: the trees that the store
/// writes from the mother cases and the kit's tree cases verify, the parent's tree-file schemas find an error of a
/// file's kind, the store's diagnostics pass through with their codes and files, and a <c>--schema-version</c> that
/// contradicts the manifest is refused. The black-box scenarios of <c>ModelVerification.feature</c> run the command on
/// trees on disk.
/// </summary>
public class TreeVerificationTests
{
    private const string TreePath = "/model";

    private static readonly SchemaValidator Validator = new(new SchemaLoader());

    private static Task<VerifyModelResult> Verify(VFileTree tree, int? schemaVersion = null) =>
        TreeVerification.VerifyAsync(tree, TreePath, schemaVersion, Validator, CancellationToken.None);

    private static VFileTree Written(StoredTree tree)
    {
        var written = DocumentTreeStoreDriver.write(tree);
        if (written.IsError) throw new InvalidOperationException($"The mother's tree is not written: {string.Join("; ", written.ErrorValue)}");
        return written.ResultValue;
    }

    private static VFileTree Texts(IEnumerable<(string Location, string Text)> files) =>
        DocumentTreeStoreDriver.ofTexts(ListModule.OfSeq(files.Select(file => Tuple.Create(file.Location, file.Text))));

    private static VFileTree WithText(VFileTree tree, string location, Func<string, string> change) =>
        DocumentTreeStoreDriver.withText(location, FuncConvert.FromFunc(change), tree);

    /// <summary>The valid trees of the mother that the store writes, by name.</summary>
    private static readonly Dictionary<string, Func<StoredTree>> WrittenTreeCases = new()
    {
        ["acmeDemo"] = () => DocumentTreeMother.acmeDemo,
        ["v4LibraryWithDependency"] = () => DocumentTreeMother.v4LibraryWithDependency,
        ["privateAndDocumentedMembers"] = () => DocumentTreeMother.privateAndDocumentedMembers,
        ["v4Application"] = () => DocumentTreeMother.v4Application,
        ["windowsReservedStems"] = () => DocumentTreeMother.windowsReservedStems,
        ["emptyModule"] = () => DocumentTreeMother.emptyModule,
        ["noModules"] = () => DocumentTreeMother.noModules,
        ["dependencyWithoutModules"] = () => DocumentTreeMother.dependencyWithoutModules,
        ["pathAtTheBudget"] = () => DocumentTreeMother.pathAtTheBudget.Tree,
        ["pathOverTheBudget"] = () => DocumentTreeMother.pathOverTheBudget.Tree,
        ["v3Library"] = () => DocumentTreeMother.v3Library,
        ["v3Specs"] = () => DocumentTreeMother.v3Specs,
        ["v3PrivateAndDocumentedMembers"] = () => DocumentTreeMother.v3PrivateAndDocumentedMembers,
        ["v3LibraryWithDependency"] = () => DocumentTreeMother.v3LibraryWithDependency,
    };

    /// <summary>The names of <see cref="WrittenTreeCases"/>, so a test is named by its case.</summary>
    public static IEnumerable<Func<string>> WrittenTrees() => WrittenTreeCases.Keys.Select(name => (Func<string>)(() => name));

    [Test]
    [MethodDataSource(nameof(WrittenTrees))]
    public async Task EachTreeThatTheStoreWritesIsValid(string name)
    {
        var tree = WrittenTreeCases[name]();
        var files = Written(tree);

        var result = await Verify(files);

        result.Errors.Should().BeEmpty($"the store wrote {name}, and the parent's schemas and the store accept it");
        result.IsValid.Should().BeTrue();
        result.Layout.Should().Be(ModelLayouts.DocumentTree);
        result.FilesChecked.Should().Be(VFileTreeModule.fileCount(files));
        result.SchemaVersion.Should().Be(tree.IsV4Tree ? "4" : "3");
        result.DetectionMethod.Should().Be("manifest");
        result.ExitCode.Should().Be(0);
        result.CompactedFiles.Should().BeEmpty();
    }

    /// <summary>The kit's tree cases that a reader reads back: each has a tree and no diagnostic.</summary>
    public static IEnumerable<Func<string>> KitTrees() =>
        FixtureCatalog.Kit.DocumentTree
            .Where(kitCase => kitCase.Diagnostic is null && kitCase.Files.Count > 0)
            .Select(kitCase => (Func<string>)(() => kitCase.Id));

    [Test]
    [MethodDataSource(nameof(KitTrees))]
    public async Task EachTreeOfTheKitIsValid(string id)
    {
        var kitCase = FixtureCatalog.Kit.Case(id);

        var result = await Verify(Texts(kitCase.Files.Select(file => (file.Key + ".json", file.Value))));

        result.Errors.Should().BeEmpty($"{id} is a tree of the kit that a reader reads back");
        result.FilesChecked.Should().Be(kitCase.Files.Count);
    }

    [Test]
    public async Task ATreeFileThatBreaksTheSchemaOfItsKindNamesTheFileAndTheMember()
    {
        var files = WithText(Written(DocumentTreeMother.acmeDemo), "manifest.json",
            text => text.Replace("\"pathBudget\": 4000", "\"pathBudget\": \"4000\"", StringComparison.Ordinal));

        var result = await Verify(files);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.File == "manifest.json" && error.Path == "/pathBudget" && error.Code == null,
            "the schema of a distribution manifest has a whole number pathBudget");
        result.Errors.Should().Contain(error => error.File == "manifest.json" && error.Code == "invalid_type",
            "the store refuses the path budget too");
    }

    /// <summary>
    /// The <c>formatVersion</c> 4 of the manifest is the first branch of the schema's <c>anyOf</c> (<c>const 4</c>) and
    /// fails the second (a string); the manifest's other error is listed and the branch that failed is not.
    /// </summary>
    [Test]
    public async Task AnErrorOfABranchThatAnotherBranchSatisfiesIsNotListed()
    {
        var files = WithText(Written(DocumentTreeMother.acmeDemo), "manifest.json",
            text => text.Replace("\"pathBudget\": 4000", "\"pathBudget\": \"4000\"", StringComparison.Ordinal));

        var result = await Verify(files);

        result.Errors.Should().Contain(error => error.File == "manifest.json" && error.Path == "/pathBudget");
        result.Errors.Should().NotContain(error => error.Path == "/formatVersion",
            "formatVersion 4 satisfies the anyOf of FormatVersion, so its failing string branch is no error");
    }

    [Test]
    public async Task AV3TreeIsNotCheckedAgainstTheV4TreeFileSchemas()
    {
        // A v3 module file has no v4 file-kind schema: the parent publishes none for v3. The store accepts its classic
        // spelling, which the v4 ModuleManifestFile schema would refuse (formatVersion "3.1.0").
        var result = await Verify(Written(DocumentTreeMother.v3Library));

        result.Errors.Should().BeEmpty();
        result.SchemaVersion.Should().Be("3");
    }

    /// <summary>The name-context examples' tree of IR <paramref name="ir"/>, with the files that <paramref name="compacted"/> names compacted.</summary>
    private static VFileTree ContextTree(int ir, MorphirFileMother.CompactedFiles compacted) => Texts(
        MorphirFileMother.tree(ir, compacted).Select(file => (file.Item1, file.Item2)));

    [Test]
    [Arguments(3)]
    [Arguments(4)]
    public async Task ACompactedTreeIsVerifiedAfterExpansion(int ir)
    {
        var result = await Verify(ContextTree(ir, MorphirFileMother.CompactedFiles.EveryFile));

        result.Errors.Should().BeEmpty("each file of the parent's example tree is valid once expanded");
        result.IsValid.Should().BeTrue();
        result.SchemaVersion.Should().Be(ir.ToString(System.Globalization.CultureInfo.InvariantCulture), "the version is the compacted manifest's");
        result.FilesChecked.Should().Be(4);
        result.CompactedFiles.Should().Equal(FixtureCatalog.Context.TreeLocations.Select(entry => entry.Location).Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task OnlyTheCompactedFilesOfATreeAreListed()
    {
        var plain = await Verify(ContextTree(4, MorphirFileMother.CompactedFiles.NoFile));
        var one = await Verify(ContextTree(4, MorphirFileMother.CompactedFiles.ValueFileOnly));

        plain.Errors.Should().BeEmpty();
        plain.CompactedFiles.Should().BeEmpty();
        one.Errors.Should().BeEmpty();
        one.CompactedFiles.Should().Equal(FixtureCatalog.Context.TreeLocation("value"));
    }

    /// <summary>
    /// A v4 tree file is checked against the schema of its kind after expansion: a compacted type file whose expanded
    /// document breaks the schema has the schema's error, located in the file as written (inside the envelope's
    /// <c>document</c>), and a schema check of the envelope itself would have refused the file at its top level.
    /// </summary>
    [Test]
    public async Task TheSchemaOfAKindChecksTheExpandedDocument()
    {
        var typeFile = FixtureCatalog.Context.TreeLocation("type");
        var files = WithText(ContextTree(4, MorphirFileMother.CompactedFiles.EveryFile), typeFile,
            text => text.Replace("\"access\": \"Public\"", "\"access\": \"Open\"", StringComparison.Ordinal));

        var result = await Verify(files);

        result.Errors.Should().Contain(error => error.File == typeFile && error.Code == null && error.Path.StartsWith("/document/def", StringComparison.Ordinal),
            "the schema checks the expanded document, whose access is not one the schema allows");
        result.Errors.Should().NotContain(error => error.File == typeFile && (error.Path == "" || error.Path == "/document") && error.Code == null,
            "the envelope is not checked as a document");
        result.Errors.Where(error => error.File == typeFile).Should().OnlyContain(error => error.Path.StartsWith("/document", StringComparison.Ordinal),
            "each error of the expanded file is located inside the envelope's document");
    }

    /// <summary>
    /// An error of the store in an expanded tree file, here an unknown tag of a type node, is located in the file as
    /// written: the JSON location of the expanded document inside the envelope's <c>document</c>. The same fault in the
    /// plain twin has the location without the prefix.
    /// </summary>
    [Test]
    [Arguments(3)]
    [Arguments(4)]
    public async Task AnErrorOfTheStoreInAnExpandedFileIsLocatedInTheEnvelopesDocument(int ir)
    {
        var typeFile = FixtureCatalog.Context.TreeLocation("type");
        static string Broken(string text) => text.Replace("\"Record\"", "\"Recrd\"", StringComparison.Ordinal);
        var compacted = await Verify(WithText(ContextTree(ir, MorphirFileMother.CompactedFiles.EveryFile), typeFile, Broken));
        var plain = await Verify(WithText(ContextTree(ir, MorphirFileMother.CompactedFiles.NoFile), typeFile, Broken));

        var plainErrors = plain.Errors.Where(error => error.Code != null).ToArray();
        plainErrors.Should().NotBeEmpty("the plain twin's type file has the fault");
        compacted.Errors.Where(error => error.Code != null).Select(error => (error.File, error.Code, error.Path)).Should().Equal(
            plainErrors.Select(error => (error.File, error.Code, "/document" + error.Path)),
            "the store's errors of the plain twin, each inside the envelope's document");
    }

    /// <summary>The parent's invalid examples, each a type file, by id.</summary>
    public static IEnumerable<Func<string>> InvalidContextExamples() =>
        MorphirFileMother.invalidExamples.Select(example => (Func<string>)(() => example.Id));

    [Test]
    [MethodDataSource(nameof(InvalidContextExamples))]
    public async Task AnInvalidContextFailsVerificationWithItsCodeAtItsFile(string id)
    {
        var example = MorphirFileMother.example(id);
        var typeFile = FixtureCatalog.Context.TreeLocation("type");
        var files = WithText(ContextTree(4, MorphirFileMother.CompactedFiles.NoFile), typeFile, _ => FixtureCatalog.ReadText(example.File));

        var result = await Verify(files);

        result.IsValid.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Errors.Should().Contain(error => error.Code == example.Expected && error.File == typeFile, $"{id} is refused with {example.Expected}");
        result.Errors.Should().OnlyContain(error => error.File == typeFile && error.Code != null && error.Code.StartsWith("context.", StringComparison.Ordinal),
            "a refused file is not also checked against the schema of its kind");
    }

    [Test]
    public async Task AnUnknownAliasNamesTheFileAndTheAlias()
    {
        var typeFile = FixtureCatalog.Context.TreeLocation("type");
        var files = WithText(ContextTree(4, MorphirFileMother.CompactedFiles.NoFile), typeFile,
            _ => FixtureCatalog.ReadText(MorphirFileMother.example("unknown-alias").File));

        var result = await Verify(files);

        var error = result.Errors.Should().ContainSingle().Which;
        error.Code.Should().Be("context.unknown_alias");
        error.File.Should().Be(typeFile);
        error.Message.Should().Contain("'zz'");
        result.CompactedFiles.Should().Equal(typeFile);
    }

    /// <summary>The invalid trees of the mother, each a refusal of the store, by name.</summary>
    private static readonly Dictionary<string, Func<VFileTree>> InvalidTreeCases = new()
    {
        ["moduleListingAMissingType"] = DocumentTreeMother.Invalid.moduleListingAMissingType,
        ["moduleListingAMissingValue"] = DocumentTreeMother.Invalid.moduleListingAMissingValue,
        ["typeFileWithAnUnknownTypeTag"] = DocumentTreeMother.Invalid.typeFileWithAnUnknownTypeTag,
        ["bothManifestProfiles"] = DocumentTreeMother.Invalid.bothManifestProfiles,
        ["typeFileOutsideTheTree"] = DocumentTreeMother.Invalid.typeFileOutsideTheTree,
        ["versionedDependencySegment"] = DocumentTreeMother.Invalid.versionedDependencySegment,
        ["typeFileWithAnotherName"] = DocumentTreeMother.Invalid.typeFileWithAnotherName,
        ["noManifest"] = DocumentTreeMother.Invalid.noManifest,
        ["moduleFileThatIsNotJson"] = DocumentTreeMother.Invalid.moduleFileThatIsNotJson,
        ["unreadableTypeFile"] = DocumentTreeMother.Invalid.unreadableTypeFile,
        ["pathBudgetUnderTheMinimum"] = DocumentTreeMother.Invalid.pathBudgetUnderTheMinimum,
        ["dependencyListedTwice"] = DocumentTreeMother.Invalid.dependencyListedTwice,
        ["v3ManifestOfAnotherRelease"] = DocumentTreeMother.Invalid.v3ManifestOfAnotherRelease,
        ["v3ModuleListingAMissingType"] = DocumentTreeMother.Invalid.v3ModuleListingAMissingType,
        ["v3PayloadThatIsNotClassicJson"] = DocumentTreeMother.Invalid.v3PayloadThatIsNotClassicJson,
    };

    /// <summary>The names of <see cref="InvalidTreeCases"/>, so a test is named by its case.</summary>
    public static IEnumerable<Func<string>> InvalidTrees() => InvalidTreeCases.Keys.Select(name => (Func<string>)(() => name));

    [Test]
    [MethodDataSource(nameof(InvalidTrees))]
    public async Task EachRefusalOfTheStoreIsAnErrorWithItsCodeAndFile(string name)
    {
        var files = InvalidTreeCases[name]();
        var read = DocumentTreeStoreDriver.read(files);
        read.IsError.Should().BeTrue($"{name} is a refusal of the store");

        var result = await Verify(files);

        result.IsValid.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        foreach (var error in read.ErrorValue)
        {
            var (pointer, message) = StoreLocation.Split(error.Message);
            result.Errors.Should().ContainSingle(
                reported => reported.Code == error.Code && reported.File == OptionModule.ToObj(error.File)
                    && reported.Path == pointer && reported.Message == message,
                $"{name}: the store's {error.Code} is an error of the report with its file and its JSON location");
        }
    }

    [Test]
    public async Task AnErrorInsideOneTreeFileNamesTheFileAndItsJsonLocation()
    {
        var result = await Verify(DocumentTreeMother.Invalid.typeFileWithAnUnknownTypeTag());

        result.Errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            File = DocumentTreeMother.Invalid.financeInvoiceTypeFile,
            Path = "/def/Public/TypeAliasDefinition/typeExp",
            Code = "unknown_node",
        });
    }

    [Test]
    public async Task AModuleFileThatListsAValueWithNoFileIsMissingMemberAtTheMissingFile()
    {
        var result = await Verify(DocumentTreeMother.Invalid.moduleListingAMissingValue());

        var error = result.Errors.Should().ContainSingle().Which;
        error.Code.Should().Be("missing_member");
        error.File.Should().Be(DocumentTreeMother.Invalid.financeDefaultCurrencyValueFile);
        error.Message.Should().Contain("pkg/my-org/finance/pricing/models/module.json").And.Contain("'default-currency'");
    }

    [Test]
    [Arguments(3, "4")]
    [Arguments(1, "4")]
    [Arguments(4, "3")]
    public async Task ASchemaVersionThatContradictsTheManifestIsRefusedWithExitCode2(int given, string manifest)
    {
        var tree = manifest == "4" ? DocumentTreeMother.v4LibraryWithDependency : DocumentTreeMother.v3LibraryWithDependency;

        var result = await Verify(Written(tree), given);

        result.ExitCode.Should().Be(2);
        result.IsValid.Should().BeFalse();
        result.SchemaVersion.Should().Be(manifest);
        result.FilesChecked.Should().Be(0, "nothing is checked");
        var error = result.Errors.Should().ContainSingle().Which;
        error.Code.Should().Be(TreeVerification.SchemaVersionConflict);
        error.File.Should().Be("manifest.json");
        error.Path.Should().Be("/formatVersion");
        error.Message.Should().Contain($"--schema-version={given}").And.Contain($"IR version {manifest}");
    }

    [Test]
    public async Task ASchemaVersionThatAgreesWithTheManifestIsManual()
    {
        var result = await Verify(Written(DocumentTreeMother.v4LibraryWithDependency), 4);

        result.IsValid.Should().BeTrue();
        result.DetectionMethod.Should().Be("manual");
        result.SchemaVersion.Should().Be("4");
    }

    [Test]
    public async Task ATreeWithNoManifestHasTheStoresDiagnosticAndNoVersion()
    {
        var result = await Verify(DocumentTreeMother.Invalid.noManifest());

        result.SchemaVersion.Should().Be("unknown");
        result.Errors.Should().ContainSingle(error => error.Code == "tree.missing_manifest");
    }

    [Test]
    public async Task ATreeWithNoManifestAndAVersionThatNoTreeHasIsRefused()
    {
        var result = await Verify(DocumentTreeMother.Invalid.noManifest(), 2);

        result.ExitCode.Should().Be(2);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(TreeVerification.SchemaVersionConflict);
    }

    [Test]
    [Arguments("$.def.Public.TypeAliasDefinition.typeExp: The object is not a node.", "/def/Public/TypeAliasDefinition/typeExp", "The object is not a node.")]
    [Arguments("$: Unknown member.", "", "Unknown member.")]
    [Arguments("$.types[2]: Not a name.", "/types/2", "Not a name.")]
    [Arguments("$.fileNames[\"a/b~c\"]: Not a stem.", "/fileNames/a~1b~0c", "Not a stem.")]
    [Arguments("The module file lists the value 'x', and the tree has no file for it.", "", "The module file lists the value 'x', and the tree has no file for it.")]
    [Arguments("$ is not a location: at all", "", "$ is not a location: at all")]
    public void TheJsonLocationOfAStoreMessageIsAJsonPointer(string storeMessage, string pointer, string message)
    {
        StoreLocation.Split(storeMessage).Should().Be((pointer, message));
    }

    [Test]
    [Arguments("$", "")]
    [Arguments("$.distribution.Library.def.modules.inventory.values.daily-rate", "/distribution/Library/def/modules/inventory/values/daily-rate")]
    [Arguments("$.modules[\"a b\"][0]", "/modules/a b/0")]
    [Arguments("not a location", "not a location")]
    public void AJsonLocationIsAJsonPointer(string location, string pointer)
    {
        StoreLocation.Pointer(location).Should().Be(pointer);
    }
}
