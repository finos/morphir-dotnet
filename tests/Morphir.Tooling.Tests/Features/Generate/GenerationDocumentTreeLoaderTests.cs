using System.Text.Json;
using FluentAssertions;
using Morphir.IR.Model;
using Morphir.IR.Trees;
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
public sealed class GenerationDocumentTreeLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-tree-gen-" + Guid.NewGuid().ToString("N"));
    public GenerationDocumentTreeLoaderTests() => Directory.CreateDirectory(_root);

    private string CopyOrdersTree(string version, string profile, string? folderName = null)
    {
        var directory = Path.Combine(_root, folderName ?? $"{version}-{profile}");
        var documents = DocumentTreeMother.BoundedOrders.catalogDocuments(version, profile);
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
        var treePath = CopyOrdersTree(version, profile);
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

    [Test]
    public async Task PreservesExplicitFourZeroZeroFormatVersionInJsonAndYamlTrees()
    {
        var jsonTree = CopyOrdersTree("v4", "plain-json", "v400-json");
        var jsonManifest = Path.Combine(jsonTree, "manifest.json");
        File.WriteAllText(jsonManifest, File.ReadAllText(jsonManifest).Replace("\"formatVersion\": 4", "\"formatVersion\": \"4.0.0\""));
        var loadedJson = ((Outcome<LoadedModel>.Success)await LoadAsync(jsonTree)).Value;
        loadedJson.Model.SourceVersion.Text.Should().Be("4.0.0");
        loadedJson.Representation.Should().Be(ModelRepresentation.JsonTree);

        var yamlTree = CopyOrdersTree("v4", "plain-yaml", "v400-yaml");
        var yamlManifest = Path.Combine(yamlTree, "manifest.yaml");
        File.WriteAllText(yamlManifest, File.ReadAllText(yamlManifest).Replace("formatVersion: 4", "formatVersion: \"4.0.0\""));
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
        var treePath = CopyOrdersTree("v4", actualProfile, $"mismatch-{requestedFormat}-{actualProfile}");
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
        var ionTree = GenerationInputFixtures.DailyRate(Path.Combine(_root, "ion-tree"), "4.0.0", "tree");
        var failure = ((Outcome<LoadedModel>.Failure)await LoadAsync(ionTree, InputFormat.Yaml)).Error;
        failure.Diagnostics[0].Code.Should().Be("unsupported-format");
    }

    [Test]
    [Arguments("manifest.json", "manifest.yaml")]
    [Arguments("manifest.json", "manifest.ion")]
    [Arguments("manifest.yaml", "manifest.ion")]
    [Arguments("manifest.yaml", "manifest.yml")]
    public async Task RejectsAmbiguousMultiManifestTreesUnderEveryFormat(string firstManifest, string secondManifest)
    {
        var baseProfile = firstManifest == "manifest.json" ? "plain-json" : "plain-yaml";
        var treePath = CopyOrdersTree("v4", baseProfile, $"ambiguous-{firstManifest}-{secondManifest}");
        File.WriteAllText(Path.Combine(treePath, secondManifest), secondManifest.EndsWith(".ion", StringComparison.Ordinal) ? "morphir::{}" : "formatVersion: 4\n");

        foreach (var format in new[] { InputFormat.Auto, InputFormat.Json, InputFormat.Yaml, InputFormat.Ion })
        {
            var failure = ((Outcome<LoadedModel>.Failure)await LoadAsync(treePath, format)).Error;
            failure.Diagnostics[0].Code.Should().Be("tree.ambiguous_profile");
        }
    }

    [Test]
    [Arguments("plain-json")]
    [Arguments("plain-yaml")]
    public async Task EnforcesReadLimitsAndCancellationOnJsonAndYamlTrees(string profile)
    {
        var treePath = CopyOrdersTree("v4", profile, $"limits-{profile}");

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
    }

    [Test]
    [Arguments("yaml", InputFormat.Auto)]
    [Arguments("yml", InputFormat.Auto)]
    [Arguments("txt", InputFormat.Yaml)]
    public async Task LoadsSingleFileYamlWithAutoAndExplicitFormat(string extension, InputFormat format)
    {
        var yamlPath = Path.Combine(_root, $"single.{extension}");
        // Any valid JSON document is also valid YAML 1.2; verify with block-style YAML header too.
        using var parsed = JsonDocument.Parse(GenerateSteps.V4Model);
        var distributionJson = parsed.RootElement.GetProperty("distribution").GetRawText();
        File.WriteAllText(yamlPath, $"formatVersion: \"4.0.0\"\ndistribution: {distributionJson}\n");

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
        var yamlPath = Path.Combine(_root, "budget.yaml");
        using var parsed = JsonDocument.Parse(GenerateSteps.V4Model);
        var distributionJson = parsed.RootElement.GetProperty("distribution").GetRawText();
        File.WriteAllText(yamlPath, $"formatVersion: 4\ndistribution: {distributionJson}\n");

        var nodeFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(yamlPath, InputFormat.Yaml, new(MaxNodes: 1))).Error;
        nodeFailure.Diagnostics[0].Code.Should().Contain("limit");

        var depthFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(yamlPath, InputFormat.Yaml, new(MaxDepth: 1))).Error;
        depthFailure.Diagnostics[0].Code.Should().Contain("limit");

        var mepPath = Path.Combine(_root, "mep.yaml");
        File.WriteAllText(mepPath, "envelopeVersion: \"0.1.0\"\npayload: {}\n");
        var mepFailure = ((Outcome<LoadedModel>.Failure)await LoadAsync(mepPath, InputFormat.Auto)).Error;
        mepFailure.Diagnostics[0].Code.Should().Be("unsupported-profile");
    }

    [Test]
    [Arguments("fsharp", "v4")]
    [Arguments("fsharp", "v3.1")]
    [Arguments("csharp", "v4")]
    [Arguments("csharp", "v3.1")]
    public async Task BuiltInBackendsGenerateIdenticalFilesFromSingleFileJsonAndAllJsonYamlTreeProfiles(string target, string version)
    {
        // Join the plain-json tree into a single-file JSON baseline so both inputs hold the exact same model.
        var plainJsonTree = CopyOrdersTree(version, "plain-json", $"parity-{target}-{version}-plain-json");
        var baselineLoaded = ((Outcome<LoadedModel>.Success)await LoadAsync(plainJsonTree)).Value;
        var singleFilePath = Path.Combine(_root, $"parity-{target}-{version}-single.json");

        var singleFileBytes = baselineLoaded.Model.Distribution.Match(
            classic: dist =>
            {
                var written = Morphir.Json.Classic.ClassicJsonWriter.writeJsonAttributes(
                    Morphir.Json.Classic.ClassicWireProfileModule.v3, true, dist);
                written.IsOk.Should().BeTrue();
                return written.ResultValue;
            },
            v4: dist =>
            {
                var written = Morphir.Json.V4.V4JsonWriter.writeDistribution(true, dist);
                written.IsOk.Should().BeTrue();
                return written.ResultValue;
            });
        File.WriteAllBytes(singleFilePath, singleFileBytes);

        var singleOut = Path.Combine(_root, $"out-{target}-{version}-single");
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
            var treePath = CopyOrdersTree(version, profile, $"parity-{target}-{version}-{profile}");
            var treeOut = Path.Combine(_root, $"out-{target}-{version}-{profile}");
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
                var documents = DocumentTreeMother.BoundedOrders.catalogDocuments("v4", profile);
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
                using var parsed = JsonDocument.Parse(GenerateSteps.V4Model);
                var distributionJson = parsed.RootElement.GetProperty("distribution").GetRawText();
                File.WriteAllText(Path.Combine(run.Dest, "morphir-ir.yaml"), $"formatVersion: 4\ndistribution: {distributionJson}\n");
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

    public void Dispose() => TolerantDirectory.TryDelete(_root);
}
