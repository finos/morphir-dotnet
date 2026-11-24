using System.Text;
using FluentAssertions;
using Morphir.IR.Model;
using Morphir.Models.TestSupport;
using Morphir.TestSupport;
using Morphir.TestSupport.Fixtures;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Features.TaskRecords;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
public sealed class GenerationDocumentTreeLoaderTests
{
    private static string CopyOrdersTree(string root, string version, string profile, string? folderName = null)
    {
        var directory = Path.Combine(root, folderName ?? $"{version}-{profile}");
        var documents = DocumentTreeMother.BoundedOrders.CatalogDocuments(version, profile);
        foreach (var pair in documents)
        {
            var target = Path.Combine(directory, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, FixtureCatalog.ReadBytes(pair.Value));
        }
        return directory;
    }

    private static ValueTask<Outcome<LoadedModel>> LoadAsync(string path, InputFormat format = InputFormat.Auto,
        ModelReadLimits? limits = null, CancellationToken ct = default) =>
        new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(path, format), limits ?? ModelReadLimits.Default, ct);

    private static Task<Outcome<GenerationSuccess>> RunGenerateAsync(string input, string target, string output,
        string? inputFormat = null, IGenerationBackend? backend = null)
    {
        IGenerationBackend selected = backend ?? (target == "csharp" ? new CSharpGenerationBackend() : new FSharpGenerationBackend());
        return GenerateHandler.Handle(
            new GenerateRequest(new(input, target, output, inputFormat), new(Path.GetDirectoryName(output)!)),
            new CliGenerationSettingsResolver(),
            new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([selected]),
            new GenerationPublisher(),
            default);
    }

    [Test]
    [Arguments("v3.1", "plain-json", "3.1.0", ModelRepresentation.JsonTree)]
    [Arguments("v3.1", "contextual-json", "3.1.0", ModelRepresentation.JsonTree)]
    [Arguments("v3.1", "plain-yaml", "3.1.0", ModelRepresentation.YamlTree)]
    [Arguments("v3.1", "contextual-yaml", "3.1.0", ModelRepresentation.YamlTree)]
    [Arguments("v4", "plain-json", "4", ModelRepresentation.JsonTree)]
    [Arguments("v4", "contextual-json", "4", ModelRepresentation.JsonTree)]
    [Arguments("v4", "plain-yaml", "4", ModelRepresentation.YamlTree)]
    [Arguments("v4", "contextual-yaml", "4", ModelRepresentation.YamlTree)]
    public async Task LoadsJsonAndYamlDocumentTreesInPlainAndCompactedForms(
        string version, string profile, string expectedVersion, ModelRepresentation expectedRepresentation)
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var treePath = CopyOrdersTree(sandbox.Work, version, profile);
        var explicitFormat = expectedRepresentation == ModelRepresentation.JsonTree ? InputFormat.Json : InputFormat.Yaml;

        foreach (var format in new[] { InputFormat.Auto, explicitFormat })
        {
            var outcome = await LoadAsync(treePath, format);
            if (outcome is Outcome<LoadedModel>.Failure failure)
                throw new InvalidOperationException(string.Join("; ", failure.Error.Diagnostics));

            var loaded = ((Outcome<LoadedModel>.Success)outcome).Value;
            loaded.Representation.Should().Be(expectedRepresentation);
            loaded.Model.SourceVersion.Text.Should().Be(expectedVersion);
            loaded.Descriptor.Family.Should().Be(version == "v4" ? ModelFamily.V4 : ModelFamily.Classic);
            loaded.Descriptor.Kind.Should().Be(DistributionKind.Library);
            loaded.Original.Should().BeOfType<RetainedInput.Tree>();
        }
    }

    private static string WriteTreeBytes(string root, string folderName, IReadOnlyDictionary<string, byte[]> documents)
    {
        var directory = Path.Combine(root, folderName);
        foreach (var pair in documents)
        {
            var target = Path.Combine(directory, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, pair.Value);
        }
        return directory;
    }

    [Test]
    public async Task PreservesExplicitFourZeroZeroFormatVersionInJsonAndYamlTrees()
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var jsonTree = WriteTreeBytes(sandbox.Work, "v400-json", DocumentTreeMother.YamlDocuments.OrdersTreeWithExplicitVersion("plain-json", "4.0.0"));
        var loadedJson = ((Outcome<LoadedModel>.Success)await LoadAsync(jsonTree)).Value;
        loadedJson.Model.SourceVersion.Text.Should().Be("4.0.0");
        loadedJson.Representation.Should().Be(ModelRepresentation.JsonTree);

        var yamlTree = WriteTreeBytes(sandbox.Work, "v400-yaml", DocumentTreeMother.YamlDocuments.OrdersTreeWithExplicitVersion("plain-yaml", "4.0.0"));
        var loadedYaml = ((Outcome<LoadedModel>.Success)await LoadAsync(yamlTree)).Value;
        loadedYaml.Model.SourceVersion.Text.Should().Be("4.0.0");
        loadedYaml.Representation.Should().Be(ModelRepresentation.YamlTree);
    }

    [Test]
    [Arguments("json", "plain-yaml")]
    [Arguments("yaml", "plain-json")]
    [Arguments("ion", "plain-json")]
    [Arguments("ion", "plain-yaml")]
    public async Task RejectsExplicitInputFormatMismatchOnDocumentTrees(string requestedFormat, string actualProfile)
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var treePath = CopyOrdersTree(sandbox.Work, "v4", actualProfile, $"mismatch-{requestedFormat}-{actualProfile}");
        var format = requestedFormat switch
        {
            "json" => InputFormat.Json,
            "yaml" => InputFormat.Yaml,
            _ => InputFormat.Ion
        };

        var failure = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, format)).Error;
        failure.Diagnostics[0].Code.Should().Be("unsupported-format");
    }

    [Test]
    public async Task RejectsExplicitYamlFormatOnIonTree()
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var ionTree = GenerationInputFixtures.DailyRate(Path.Combine(sandbox.Work, "ion-tree"), "4.0.0", "tree");
        var failure = ((Outcome<LoadedModel>.Failure)await LoadAsync(ionTree, InputFormat.Yaml)).Error;
        failure.Diagnostics[0].Code.Should().Be("unsupported-format");
    }

    [Test]
    public async Task RejectsUndefinedInputFormatEnumWithTypedInvocationFailure()
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var undefinedFormat = (InputFormat)999;

        var filePath = Path.Combine(sandbox.Work, "model.yaml");
        File.WriteAllText(filePath, DocumentTreeMother.YamlDocuments.V4DailyRateYaml("4.0.0"));
        var fileFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(filePath, undefinedFormat)).Error;
        fileFailure.Kind.Should().Be(GenerationFailureKind.Invocation);
        fileFailure.Diagnostics[0].Code.Should().Be("invalid-input-format");

        var treePath = CopyOrdersTree(sandbox.Work, "v4", "plain-yaml");
        var treeFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, undefinedFormat)).Error;
        treeFailure.Kind.Should().Be(GenerationFailureKind.Invocation);
        treeFailure.Diagnostics[0].Code.Should().Be("invalid-input-format");
    }

    [Test]
    [Arguments("manifest.json", "manifest.yaml")]
    [Arguments("manifest.json", "manifest.ion")]
    [Arguments("manifest.yaml", "manifest.ion")]
    [Arguments("manifest.yaml", "manifest.yml")]
    public async Task RejectsAmbiguousMultiManifestTreesUnderEveryFormat(string firstManifest, string secondManifest)
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var baseProfile = firstManifest == "manifest.json" ? "plain-json" : "plain-yaml";
        var treePath = CopyOrdersTree(sandbox.Work, "v4", baseProfile, $"ambiguous-{firstManifest}-{secondManifest}");
        File.WriteAllText(
            Path.Combine(treePath, secondManifest),
            secondManifest.EndsWith(".ion", StringComparison.Ordinal)
                ? DocumentTreeMother.YamlDocuments.MinimalIonManifest
                : DocumentTreeMother.YamlDocuments.MinimalV4PlainYaml);

        foreach (var format in new[] { InputFormat.Auto, InputFormat.Json, InputFormat.Yaml, InputFormat.Ion })
        {
            var failure = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, format)).Error;
            failure.Diagnostics[0].Code.Should().Be("tree.ambiguous_profile");
        }
    }

    [Test]
    [Arguments("plain-json")]
    [Arguments("plain-yaml")]
    public async Task GenerationReportPreservesMemberPathAndNonRootNestedCodecLocation(string profile)
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var treePath = WriteTreeBytes(sandbox.Work, $"nested-location-{profile}", DocumentTreeMother.YamlDocuments.OrdersTreeWithUnknownBodyTag(profile));
        var extension = profile.EndsWith("-yaml", StringComparison.Ordinal) ? ".yaml" : ".json";
        var relativeMember = $"pkg/acme/orders/domain/order.value{extension}";

        var output = Path.Combine(sandbox.Work, "out");
        var overrides = new GenerateOverrides(treePath, "fsharp", output);
        var outcome = await RunGenerateAsync(treePath, "fsharp", output);
        var report = GenerationReport.From(outcome, overrides);

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Diagnostics.Should().NotBeEmpty();
        report.Diagnostics[0].Path.Should().Be(relativeMember);
        report.Diagnostics[0].Location.Should().NotBe("$").And.StartWith("$.def");
        report.Diagnostics[0].Message.Should().NotStartWith("$.def");
    }

    [Test]
    public async Task PreservesYamlPointerLocationContainingColonSpaceInGenerationReport()
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var yamlPath = Path.Combine(sandbox.Work, "colon-key.yaml");
        File.WriteAllText(yamlPath, DocumentTreeMother.YamlDocuments.NestedYamlKeyWithColonSpace);

        var output = Path.Combine(sandbox.Work, "out");
        var overrides = new GenerateOverrides(yamlPath, "fsharp", output, "yaml");
        var outcome = await RunGenerateAsync(yamlPath, "fsharp", output, "yaml");
        var report = GenerationReport.From(outcome, overrides);

        report.Success.Should().BeFalse();
        report.Diagnostics.Should().NotBeEmpty();
        report.Diagnostics[0].Code.Should().Be("unsupported_yaml_feature");
        report.Diagnostics[0].Location.Should().Be("/outer/note: detail");
        report.Diagnostics[0].Message.Should().NotStartWith("/outer/note");
    }

    [Test]
    public async Task PreservesStructuredNestedContextValidationLocationInGenerationReport()
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var yamlPath = Path.Combine(sandbox.Work, "invalid-context.yaml");
        File.WriteAllText(yamlPath, DocumentTreeMother.YamlDocuments.ContextualYamlWithInvalidAliasEntry);

        var output = Path.Combine(sandbox.Work, "out");
        var overrides = new GenerateOverrides(yamlPath, "fsharp", output, "yaml");
        var outcome = await RunGenerateAsync(yamlPath, "fsharp", output, "yaml");
        var report = GenerationReport.From(outcome, overrides);

        report.Success.Should().BeFalse();
        report.Diagnostics.Should().NotBeEmpty();
        report.Diagnostics[0].Code.Should().Be("context.invalid_entry");
        report.Diagnostics[0].Location.Should().Be("$.context.names.bad");
    }

    [Test]
    public async Task EnforcesSharedAggregateExpansionByteBudgetAcrossTreeMembers()
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var treePath = Path.Combine(sandbox.Work, "aggregate-expansion");
        var documents = DocumentTreeMother.YamlDocuments.CompactedTwoMemberExpandingTree(400);
        foreach (var pair in documents)
        {
            var target = Path.Combine(treePath, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, pair.Value);
        }

        // Total raw bytes are ~900; each compacted member expands to ~1,500 bytes (fits 2,200 individually),
        // while both members combined expand to ~3,000+ bytes (exceeding the shared 2,200-byte allowance).
        const long sharedBudget = 2200;
        var refused = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, InputFormat.Auto, new(MaxBytes: sharedBudget))).Error;
        refused.Diagnostics[0].Code.Should().Be("tree.file_too_large");

        var accepted = await LoadAsync(treePath, InputFormat.Auto, new(MaxBytes: sharedBudget * 4));
        accepted.Should().BeOfType<Outcome<LoadedModel>.Success>();
    }

    [Test]
    [Arguments("plain-json")]
    [Arguments("plain-yaml")]
    public async Task EnforcesReadLimitsAndCancellationOnJsonAndYamlTrees(string profile)
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var treePath = CopyOrdersTree(sandbox.Work, "v4", profile, $"limits-{profile}");

        var nodeFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, limits: new(MaxNodes: 1))).Error;
        nodeFailure.Diagnostics[0].Code.Should().Contain("limit");

        var depthFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, limits: new(MaxDepth: 1))).Error;
        depthFailure.Diagnostics[0].Code.Should().Contain("limit");

        var entryFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, limits: new(MaxEntries: 1))).Error;
        entryFailure.Diagnostics[0].Code.Should().Be("input-limit");

        var byteFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, limits: new(MaxBytes: 10))).Error;
        byteFailure.Diagnostics[0].Code.Should().Be("input-limit");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, ct: cts.Token)).Error;
        cancelled.Kind.Should().Be(GenerationFailureKind.Cancelled);

        // Direct Decode with a cancelled token also returns typed Cancelled outcome.
        var direct = new RetainedInput.File(
            Path.Combine(sandbox.Work, "single.yaml"),
            new RetainedEntry("single.yaml", Encoding.UTF8.GetBytes(DocumentTreeMother.YamlDocuments.MinimalV4PlainYaml)));
        var directCancelled = ((Outcome<LoadedModel>.Failure)GenerationModelLoader.Decode(
            direct, new(direct.SourcePath, InputFormat.Yaml), ModelReadLimits.Default, cts.Token)).Error;
        directCancelled.Kind.Should().Be(GenerationFailureKind.Cancelled);
    }

    [Test]
    [Arguments("plain-json")]
    [Arguments("plain-yaml")]
    public async Task DirectDecodeValidatesInvalidLimitsWithoutThrowingOnJsonAndYamlTrees(string profile)
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var treePath = CopyOrdersTree(sandbox.Work, "v4", profile, $"invalid-limits-{profile}");
        var captured = await new BoundedModelCapture().CaptureAsync(new(treePath), ModelReadLimits.Default, default);
        var retained = ((Outcome<RetainedInput>.Success)captured).Value;

        foreach (var invalidLimits in new[]
        {
            new ModelReadLimits(MaxDepth: 0),
            new ModelReadLimits(MaxNodes: 0),
            new ModelReadLimits(MaxBytes: 0),
            new ModelReadLimits(MaxEntries: 0),
            new ModelReadLimits(MaxPathLength: 0),
        })
        {
            var outcome = GenerationModelLoader.Decode(retained, new(treePath, InputFormat.Auto), invalidLimits, default);
            var failure = outcome.Should().BeOfType<Outcome<LoadedModel>.Failure>().Subject.Error;
            failure.Kind.Should().Be(GenerationFailureKind.Invocation);
            failure.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-input-limits");
        }
    }

    [Test]
    [Arguments("yaml", InputFormat.Auto)]
    [Arguments("yml", InputFormat.Auto)]
    [Arguments("txt", InputFormat.Yaml)]
    public async Task LoadsSingleFileYamlWithAutoAndExplicitFormat(string extension, InputFormat format)
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var yamlPath = Path.Combine(sandbox.Work, $"single.{extension}");
        File.WriteAllText(yamlPath, DocumentTreeMother.YamlDocuments.V4DailyRateYaml("4.0.0"));

        var outcome = await LoadAsync(yamlPath, format);
        if (outcome is Outcome<LoadedModel>.Failure failure)
            throw new InvalidOperationException(string.Join("; ", failure.Error.Diagnostics));

        var loaded = ((Outcome<LoadedModel>.Success)outcome).Value;
        loaded.Representation.Should().Be(ModelRepresentation.Yaml);
        loaded.Model.SourceVersion.Text.Should().Be("4.0.0");
        loaded.Descriptor.Family.Should().Be(ModelFamily.V4);
        loaded.Descriptor.Kind.Should().Be(DistributionKind.Library);
    }

    [Test]
    public async Task SingleFileYamlEnforcesBudgetsAndRejectsMepEnvelopes()
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var yamlPath = Path.Combine(sandbox.Work, "budget.yaml");
        File.WriteAllText(yamlPath, DocumentTreeMother.YamlDocuments.V4DailyRateYaml("4"));

        var nodeFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(yamlPath, InputFormat.Yaml, new(MaxNodes: 1))).Error;
        nodeFailure.Diagnostics[0].Code.Should().Contain("limit");

        var depthFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(yamlPath, InputFormat.Yaml, new(MaxDepth: 1))).Error;
        depthFailure.Diagnostics[0].Code.Should().Contain("limit");

        var mepPath = Path.Combine(sandbox.Work, "mep.yaml");
        File.WriteAllText(mepPath, DocumentTreeMother.YamlDocuments.MepEnvelopeYaml);
        var mepFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(mepPath, InputFormat.Auto)).Error;
        mepFailure.Diagnostics[0].Code.Should().Be("unsupported-profile");

        // YAML containing a MEP marker alongside $schema must still report unsupported-profile, not context.undeclared_schema.
        var mepWithSchemaPath = Path.Combine(sandbox.Work, "mep-with-schema.yaml");
        File.WriteAllText(mepWithSchemaPath, DocumentTreeMother.YamlDocuments.MepEnvelopeWithSchemaYaml);
        foreach (var format in new[] { InputFormat.Auto, InputFormat.Yaml })
        {
            var mepSchemaFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(mepWithSchemaPath, format)).Error;
            mepSchemaFailure.Diagnostics[0].Code.Should().Be("unsupported-profile");
        }

        // Extensionless YAML MEP wrappers (unquoted, quoted, escaped, flow mapping, reordered keys, CR/LF comments, ---)
        // must be classified identically as YAML and return unsupported-profile in both Auto and explicit YAML modes.
        foreach (var (name, content) in new[]
        {
            ("mep-envelope-first", DocumentTreeMother.YamlDocuments.MepEnvelopeFirstWithSchemaYaml),
            ("mep-morphir-model", DocumentTreeMother.YamlDocuments.MepMorphirModelYaml),
            ("mep-jsonrpc", DocumentTreeMother.YamlDocuments.MepJsonRpcYaml),
            ("mep-commented-envelope", DocumentTreeMother.YamlDocuments.MepCommentedEnvelopeYaml),
            ("mep-double-quoted-envelope", DocumentTreeMother.YamlDocuments.MepDoubleQuotedEnvelopeYaml),
            ("mep-single-quoted-envelope", DocumentTreeMother.YamlDocuments.MepSingleQuotedEnvelopeYaml),
            ("mep-commented-quoted-morphir-model", DocumentTreeMother.YamlDocuments.MepCommentedQuotedMorphirModelYaml),
            ("mep-single-quoted-jsonrpc", DocumentTreeMother.YamlDocuments.MepSingleQuotedJsonRpcYaml),
            ("mep-document-start-commented", DocumentTreeMother.YamlDocuments.MepDocumentStartCommentedYaml),
            ("mep-cr-only-comment", DocumentTreeMother.YamlDocuments.MepCrOnlyCommentEnvelopeYaml),
            ("mep-flow-mapping", DocumentTreeMother.YamlDocuments.MepFlowMappingEnvelopeYaml),
            ("mep-payload-first", DocumentTreeMother.YamlDocuments.MepPayloadFirstEnvelopeYaml),
            ("mep-escaped-double-quoted", DocumentTreeMother.YamlDocuments.MepEscapedDoubleQuotedEnvelopeYaml),
        })
        {
            var extensionlessPath = Path.Combine(sandbox.Work, name);
            File.WriteAllText(extensionlessPath, content);
            foreach (var format in new[] { InputFormat.Auto, InputFormat.Yaml })
            {
                var failure = ((Outcome<LoadedModel>.Failure)await LoadAsync(extensionlessPath, format)).Error;
                failure.Diagnostics[0].Code.Should().Be("unsupported-profile", $"{name} should classify as unsupported-profile in {format} mode");
            }
        }

        // Truncated malformed JSON stays malformed-json in Auto mode rather than reclassifying as YAML.
        var malformedJsonPath = Path.Combine(sandbox.Work, "malformed-json");
        File.WriteAllText(malformedJsonPath, DocumentTreeMother.YamlDocuments.MalformedJsonBrace);
        var malformedJsonFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(malformedJsonPath, InputFormat.Auto)).Error;
        malformedJsonFailure.Diagnostics[0].Code.Should().Be("malformed-json");

        // Compacted YAML that fits raw byte capture limits but exceeds MaxBytes on expansion is refused.
        var bombPath = Path.Combine(sandbox.Work, "compacted-bomb.yaml");
        var bombText = DocumentTreeMother.YamlDocuments.CompactedExpandingManifestYaml(20, 12);
        File.WriteAllText(bombPath, bombText);
        var tightByteLimit = Encoding.UTF8.GetByteCount(bombText) + 32;
        var expansionFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(bombPath, InputFormat.Yaml, new(MaxBytes: tightByteLimit))).Error;
        expansionFailure.Diagnostics[0].Code.Should().Be("tree.file_too_large");
    }

    [Test]
    public async Task AutoModePreservesYamlDiagnosticsAndRefusesMalformedTrailingCommaJsonWithoutSilentFallback()
    {
        await using var sandbox = TestSandbox.Create("morphir-auto-classifier-");

        // 1. Extensionless invalid YAML with unclosed distribution sequence must report invalid_yaml at /distribution/0 (not malformed-json).
        var invalidSeqPath = Path.Combine(sandbox.Work, "invalid-seq-yaml");
        File.WriteAllText(invalidSeqPath, DocumentTreeMother.YamlDocuments.MalformedDistributionSequenceYaml);
        var invalidSeqFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(invalidSeqPath, InputFormat.Auto)).Error;
        invalidSeqFailure.Diagnostics[0].Code.Should().Be("invalid_yaml");
        invalidSeqFailure.Diagnostics[0].Location.Should().Be("/distribution/0");

        // 2. Extensionless YAML exceeding node budget must report YAML node-budget diagnostic (not malformed-json).
        var validYamlPath = Path.Combine(sandbox.Work, "budget-yaml");
        File.WriteAllText(validYamlPath, DocumentTreeMother.YamlDocuments.V4DailyRateYaml("4"));
        var nodeBudgetFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(validYamlPath, InputFormat.Auto, new(MaxNodes: 1))).Error;
        nodeBudgetFailure.Diagnostics[0].Code.Should().Be("tree.document_node_limit");

        // 3. Extensionless YAML with undeclared $schema (including plain block/flow keys containing '::', '::(key)', or 'morphir::key')
        // must report context.undeclared_schema in both Auto and explicit YAML modes (not malformed-json or malformed_ion).
        foreach (var (name, content) in new[]
        {
            ("bad-schema-yaml", DocumentTreeMother.YamlDocuments.UndeclaredSchemaYaml),
            ("block-colon-colon-yaml", DocumentTreeMother.YamlDocuments.BlockColonColonKeyWithUndeclaredSchemaYaml),
            ("flow-colon-colon-yaml", DocumentTreeMother.YamlDocuments.FlowColonColonKeyWithUndeclaredSchemaYaml),
            ("block-colon-colon-paren-yaml", DocumentTreeMother.YamlDocuments.BlockColonColonParenKeyWithUndeclaredSchemaYaml),
            ("flow-colon-colon-paren-yaml", DocumentTreeMother.YamlDocuments.FlowColonColonParenKeyWithUndeclaredSchemaYaml),
            ("block-morphir-colon-colon-yaml", DocumentTreeMother.YamlDocuments.BlockMorphirColonColonKeyWithUndeclaredSchemaYaml),
        })
        {
            var badSchemaPath = Path.Combine(sandbox.Work, name);
            File.WriteAllText(badSchemaPath, content);
            foreach (var format in new[] { InputFormat.Auto, InputFormat.Yaml })
            {
                var badSchemaFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(badSchemaPath, format)).Error;
                badSchemaFailure.Diagnostics[0].Code.Should().Be("context.undeclared_schema", $"{name} should route to YAML and report context.undeclared_schema in {format} mode");
            }
        }

        // 3b. Ion-annotated quoted string containing ': ' inside quotes is NOT classified as YAML in Auto mode.
        var ionQuotedColonPath = Path.Combine(sandbox.Work, "ion-quoted-colon");
        File.WriteAllText(ionQuotedColonPath, DocumentTreeMother.YamlDocuments.IonAnnotatedQuotedStringWithColonSpace);
        var ionQuotedColonFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(ionQuotedColonPath, InputFormat.Auto)).Error;
        ionQuotedColonFailure.Diagnostics[0].Code.Should().Be("malformed-json");

        // 3c. Ion annotation markers (morphir::v4::{}, morphir::bare, morphir_model::{}) remain Ion in Auto mode.
        foreach (var (name, content, expectedCode) in new[]
        {
            ("ion-stacked", DocumentTreeMother.YamlDocuments.IonStackedMorphirAnnotationStub, "invalid_document"),
            ("ion-bare", DocumentTreeMother.YamlDocuments.IonBareMorphirAnnotationStub, "invalid_document"),
            ("ion-mep", FixtureCatalog.ReadText(FixtureCatalog.UpstreamIr.RichModelIon), "unsupported-profile"),
        })
        {
            var ionPath = Path.Combine(sandbox.Work, name);
            File.WriteAllText(ionPath, content);
            var ionFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(ionPath, InputFormat.Auto)).Error;
            ionFailure.Diagnostics[0].Code.Should().Be(expectedCode, $"{name} should stay Ion in Auto mode");
        }

        // 4. Extensionless malformed JSON (trailing comma in v4 daily-rate JSON) must fail as malformed-json, NOT silently decode as YAML.
        var trailingCommaPath = Path.Combine(sandbox.Work, "trailing-comma-json");
        File.WriteAllText(trailingCommaPath, DocumentTreeMother.YamlDocuments.TrailingCommaV4DailyRateJson);
        var trailingCommaOutcome = await LoadAsync(trailingCommaPath, InputFormat.Auto);
        var trailingCommaFailure = trailingCommaOutcome.Should().BeOfType<Outcome<LoadedModel>.Failure>().Subject.Error;
        trailingCommaFailure.Diagnostics[0].Code.Should().Be("malformed-json");

        var output = Path.Combine(sandbox.Work, "trailing-comma-out");
        var genOutcome = await RunGenerateAsync(trailingCommaPath, "fsharp", output);
        var genReport = GenerationReport.From(genOutcome, new GenerateOverrides(trailingCommaPath, "fsharp", output));
        genReport.Success.Should().BeFalse();
        genReport.ExitCode.Should().NotBe(0);
        genReport.Diagnostics[0].Code.Should().Be("malformed-json");

        // 5. Valid binary Ion fixture with a .yaml suffix still classifies and decodes as IonBinary in Auto mode (Ion precedence).
        var ionBinaryPath = GenerationInputFixtures.DailyRate(Path.Combine(sandbox.Work, "ion-src"), "4.0.0", "ion-binary");
        var binaryIonWithYamlSuffix = Path.Combine(sandbox.Work, "binary-ion.yaml");
        File.Copy(ionBinaryPath, binaryIonWithYamlSuffix);
        var binaryIonLoaded = ((Outcome<LoadedModel>.Success)await LoadAsync(binaryIonWithYamlSuffix, InputFormat.Auto)).Value;
        binaryIonLoaded.Representation.Should().Be(ModelRepresentation.IonBinary);
        binaryIonLoaded.Model.SourceVersion.Text.Should().Be("4.0.0");

        // 6. Extensionless v4 Ion with a block comment containing ': ' between morphir:: and '{' succeeds in Auto mode.
        var commentedIonPath = Path.Combine(sandbox.Work, "ion-comment-after-annotation");
        File.WriteAllText(commentedIonPath, DocumentTreeMother.YamlDocuments.V4DailyRateIonWithBlockCommentAfterAnnotation);
        var commentedIonOutcome = await LoadAsync(commentedIonPath, InputFormat.Auto);
        var commentedIonLoaded = commentedIonOutcome.Should().BeOfType<Outcome<LoadedModel>.Success>().Subject.Value;
        commentedIonLoaded.Representation.Should().Be(ModelRepresentation.IonText);
        commentedIonLoaded.Model.SourceVersion.Text.Should().Be("4.0.0");

        // 7. Extensionless valid v4 YAML starting with morphir::[key]: ignored or custom:: "ignored" succeeds in Auto mode.
        foreach (var (name, content) in new[]
        {
            ("yaml-morphir-bracket-key", DocumentTreeMother.YamlDocuments.V4DailyRateYamlWithMorphirBracketKey),
            ("yaml-custom-double-colon-quoted", DocumentTreeMother.YamlDocuments.V4DailyRateYamlWithCustomDoubleColonQuotedValue),
        })
        {
            var yamlPath = Path.Combine(sandbox.Work, name);
            File.WriteAllText(yamlPath, content);
            var yamlOutcome = await LoadAsync(yamlPath, InputFormat.Auto);
            var yamlLoaded = yamlOutcome.Should().BeOfType<Outcome<LoadedModel>.Success>($"{name} should decode as YAML in Auto mode").Subject.Value;
            yamlLoaded.Representation.Should().Be(ModelRepresentation.Yaml);
            yamlLoaded.Model.SourceVersion.Text.Should().Be("4");
        }
    }

    [Test]
    [Arguments("fsharp", "v4")]
    [Arguments("fsharp", "v3.1")]
    [Arguments("csharp", "v4")]
    [Arguments("csharp", "v3.1")]
    public async Task BuiltInBackendsGenerateIdenticalFilesFromSingleFileJsonAndAllJsonYamlTreeProfiles(string target, string version)
    {
        await using var sandbox = TestSandbox.Create("morphir-tree-gen-");
        var plainJsonTree = CopyOrdersTree(sandbox.Work, version, "plain-json", $"parity-{target}-{version}-plain-json");
        var baselineLoaded = ((Outcome<LoadedModel>.Success)await LoadAsync(plainJsonTree)).Value;
        var singleFilePath = Path.Combine(sandbox.Work, $"parity-{target}-{version}-single.json");

        var singleFileBytes = DocumentTreeMother.YamlDocuments.WriteSingleFileJson(baselineLoaded.Model);
        File.WriteAllBytes(singleFilePath, singleFileBytes);

        var singleOut = Path.Combine(sandbox.Work, $"out-{target}-{version}-single");
        var singleResult = ((Outcome<GenerationSuccess>.Success)await RunGenerateAsync(singleFilePath, target, singleOut)).Value;
        singleResult.Publication.Artifacts.Should().NotBeEmpty();
        var expectedFiles = singleResult.Publication.Artifacts.ToDictionary(
            path => path,
            path => File.ReadAllText(Path.Combine(singleOut, path)),
            StringComparer.Ordinal);

        foreach (var (profile, expectedRep, explicitFormat) in new[]
        {
            ("plain-json", "JsonTree", "json"),
            ("contextual-json", "JsonTree", "json"),
            ("plain-yaml", "YamlTree", "yaml"),
            ("contextual-yaml", "YamlTree", "yaml"),
        })
        {
            var treePath = CopyOrdersTree(sandbox.Work, version, profile, $"parity-{target}-{version}-{profile}");
            var treeOut = Path.Combine(sandbox.Work, $"out-{target}-{version}-{profile}");
            var outcome = await RunGenerateAsync(treePath, target, treeOut, explicitFormat);
            if (outcome is Outcome<GenerationSuccess>.Failure failure)
                throw new InvalidOperationException($"{target}/{version}/{profile} failed: {string.Join("; ", failure.Error.Diagnostics)}");

            var success = ((Outcome<GenerationSuccess>.Success)outcome).Value;
            success.Representation.Should().Be(expectedRep);
            success.Publication.Artifacts.Should().Equal(singleResult.Publication.Artifacts);
            foreach (var artifact in success.Publication.Artifacts)
            {
                File.ReadAllText(Path.Combine(treeOut, artifact))
                    .Should().Be(expectedFiles[artifact], $"{target} {version} {profile} must match single-file JSON for {artifact}");
            }
        }
    }

    [Test]
    [Arguments(IrStorageFormat.Json, IrLayout.DocumentTree, "JsonTree", "json", "yaml")]
    [Arguments(IrStorageFormat.Yaml, IrLayout.DocumentTree, "YamlTree", "yaml", "json")]
    [Arguments(IrStorageFormat.Yaml, IrLayout.SingleFile, "Yaml", "yaml", "json")]
    public async Task RecordedJsonAndYamlInputsFlowThroughBuiltInGeneration(
        IrStorageFormat storageFormat, IrLayout layout, string expectedRepresentation, string matchingFormat, string conflictingFormat)
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/orders\"\n\n[frontend]\nlanguage = \"fsharp\"\n");
        var outRoot = ((Outcome<OutRoot>.Success)OutRoot.Resolve(new OutRootRequest(workspace.Root, workspace.Root))).Value;
        var store = new CompileTaskStore(outRoot, MemberPath.Root, new TaskStoreOptions { Diagnostics = TextWriter.Null });

        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            if (layout == IrLayout.DocumentTree)
            {
                var profile = storageFormat == IrStorageFormat.Yaml ? "plain-yaml" : "plain-json";
                var documents = DocumentTreeMother.BoundedOrders.CatalogDocuments("v4", profile);
                var destTree = Path.Combine(run.Dest, "ir-tree");
                foreach (var pair in documents)
                {
                    var target = Path.Combine(destTree, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, FixtureCatalog.ReadBytes(pair.Value));
                }
                var descriptor = new IrDescriptor(
                    RecordPath.Parse("ir-tree"),
                    layout,
                    storageFormat,
                    SourceFormatVersion.Create("4"));
                run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
            }
            else
            {
                File.WriteAllText(Path.Combine(run.Dest, "morphir-ir.yaml"), DocumentTreeMother.YamlDocuments.V4DailyRateYaml("4"));
                var descriptor = new IrDescriptor(
                    RecordPath.Parse("morphir-ir.yaml"),
                    IrLayout.SingleFile,
                    IrStorageFormat.Yaml,
                    SourceFormatVersion.Create("4"));
                run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
            }
        }

        var resolver = new RecordGenerationSettingsResolver(
            new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var context = new InvocationContext(workspace.Root) { Environment = workspace.Context().Environment };

        // Conflicting --input-format is refused.
        var conflict = await GenerateHandler.Handle(
            new GenerateRequest(new(Target: "fsharp", Output: "gen-conflict", InputFormat: conflictingFormat), context),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), default);
        conflict.ShouldFail("generate.input_format_conflict");

        // Matching --input-format and auto both succeed through the built-in backend.
        foreach (var formatOption in new string?[] { null, matchingFormat })
        {
            var generated = (await GenerateHandler.Handle(
                new GenerateRequest(new(Target: "fsharp", Output: $"gen-{formatOption ?? "auto"}", InputFormat: formatOption), context),
                resolver, new GenerationModelLoader(new BoundedModelCapture()),
                new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), default)).ShouldSucceed();

            generated.Representation.Should().Be(expectedRepresentation);
            generated.Publication.Artifacts.Should().NotBeEmpty();
        }
    }
}
