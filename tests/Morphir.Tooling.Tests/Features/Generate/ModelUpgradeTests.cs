// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Files;
using Morphir.IR.Model;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// <c>generate</c> upgrades a classic model when the selected backend reads the current IR only, and gives the model
/// unchanged to a backend that reads the classic model.
/// </summary>
[Category("PlatformSpecific")]
public sealed class ModelUpgradeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-upgrade-" + Guid.NewGuid().ToString("N"));
    public ModelUpgradeTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private sealed record Options : ValidatedBackendOptions;

    /// <summary>A backend that reads the families and versions it is given, and records the model that it receives.</summary>
    private sealed class RecordingBackend(string target, params BackendCapability[] capabilities) : IGenerationBackend
    {
        public ModelFamily? SeenFamily { get; private set; }
        public string? SeenVersion { get; private set; }

        public BackendDescriptor Descriptor { get; } =
            new(ExtensionId.Create("test-" + target), ExtensionName.Create("Recording test backend"), TargetId.Create(target), capabilities);

        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => new Outcome<ValidatedBackendOptions>.Success(new Options());

        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            SeenFamily = request.Model.Model.Family;
            SeenVersion = request.Model.Model.SourceVersion.Text;
            return ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Success(new GenerationArtifacts(VFileTreeModule.empty, [])));
        }
    }

    /// <summary>A backend that gives each request to <paramref name="inner"/> and records the model that the request has.</summary>
    private sealed class RecordingDecorator(IGenerationBackend inner) : IGenerationBackend
    {
        public ModelFamily? SeenFamily { get; private set; }
        public string? SeenVersion { get; private set; }

        public BackendDescriptor Descriptor => inner.Descriptor;

        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => inner.ValidateOptions(options);

        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            SeenFamily = request.Model.Model.Family;
            SeenVersion = request.Model.Model.SourceVersion.Text;
            return inner.GenerateAsync(request, ct);
        }
    }

    private static BackendCapability Classic() =>
        new(ModelFamily.Classic, SourceFormatVersions.Create("1", "2", "3", "3.0.0", "3.1.0"), DistributionKind.Library);

    private static BackendCapability Current() =>
        new(ModelFamily.V4, SourceFormatVersions.Create("4", "4.0.0"), DistributionKind.Library);

    private async Task<(Outcome<GenerationSuccess> Result, LoadedModel Loaded)> Run(string version, string target, IGenerationBackend backend)
    {
        var input = GenerationInputFixtures.DailyRate(Path.Combine(_root, Guid.NewGuid().ToString("N")), version, "json");
        var loader = new GenerationModelLoader(new BoundedModelCapture());
        var resolution = await new CliGenerationSettingsResolver().ResolveAsync(new(input, target, Path.Combine(_root, "out-" + Guid.NewGuid().ToString("N"))), new(_root), default);
        var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
        var loaded = ((Outcome<LoadedModel>.Success)await loader.LoadAsync(settings.Input, settings.Limits, default)).Value;
        var result = await GenerationStages.RunFrom(loaded, settings, new RegisteredBackendResolver([backend]), new GenerationPublisher(), default);
        return (result, loaded);
    }

    [Test]
    public async Task AClassicModelIsUpgradedForABackendThatReadsTheCurrentIrOnly()
    {
        var backend = new RecordingBackend("current-only", Current());
        var (result, _) = await Run("3.0.0", "current-only", backend);
        result.Should().BeOfType<Outcome<GenerationSuccess>.Success>();
        backend.SeenFamily.Should().Be(ModelFamily.V4);
        backend.SeenVersion.Should().Be("4");
        // The report names the model as it was loaded.
        ((Outcome<GenerationSuccess>.Success)result).Value.SourceVersion.Should().Be("3.0.0");
    }

    [Test]
    public async Task AClassicModelIsNotUpgradedForABackendThatReadsIt()
    {
        var backend = new RecordingBackend("classic", Classic(), Current());
        var (result, _) = await Run("3.0.0", "classic", backend);
        result.Should().BeOfType<Outcome<GenerationSuccess>.Success>();
        backend.SeenFamily.Should().Be(ModelFamily.Classic);
        backend.SeenVersion.Should().Be("3.0.0");
    }

    [Test]
    public async Task ACurrentModelIsNeverChanged()
    {
        var backend = new RecordingBackend("current-only", Current());
        var (result, _) = await Run("4.0.0", "current-only", backend);
        result.Should().BeOfType<Outcome<GenerationSuccess>.Success>();
        backend.SeenFamily.Should().Be(ModelFamily.V4);
        backend.SeenVersion.Should().Be("4.0.0");
    }

    [Test]
    public async Task ABackendThatReadsNeitherFamilyStillGivesTheIncompatibleModelFailure()
    {
        var backend = new RecordingBackend("neither", new BackendCapability(ModelFamily.Classic, SourceFormatVersions.Create("1"), DistributionKind.Specs));
        var (result, _) = await Run("3.0.0", "neither", backend);
        var failure = (Outcome<GenerationSuccess>.Failure)result;
        failure.Error.Diagnostics.Select(d => d.Code).Should().Contain("incompatible-model");
        backend.SeenFamily.Should().BeNull();
    }

    [Test]
    public async Task TheBuiltInFSharpBackendKeepsReadingClassicModels()
    {
        var input = GenerationInputFixtures.DailyRate(Path.Combine(_root, "fsharp"), "3.0.0", "json");
        var resolver = new CliGenerationSettingsResolver();
        var resolution = await resolver.ResolveAsync(new(input, "fsharp", Path.Combine(_root, "fsharp-out")), new(_root), default);
        var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
        var loaded = ((Outcome<LoadedModel>.Success)await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(settings.Input, settings.Limits, default)).Value;
        var backend = new RecordingDecorator(new FSharpGenerationBackend());
        var result = await GenerationStages.RunFrom(loaded, settings, new RegisteredBackendResolver([backend]), new GenerationPublisher(), default);
        result.Should().BeOfType<Outcome<GenerationSuccess>.Success>();
        // The F# backend itself gets the classic model, not an upgraded model.
        backend.SeenFamily.Should().Be(ModelFamily.Classic);
        backend.SeenVersion.Should().Be("3.0.0");
    }

    [Test]
    public async Task AnUpgradeFailureNamesTheNodeAndTheInputFile()
    {
        var input = GenerationInputFixtures.DailyRate(Path.Combine(_root, "failing"), "3.0.0", "json");
        var resolution = await new CliGenerationSettingsResolver().ResolveAsync(new(input, "csharp", Path.Combine(_root, "failing-out")), new(_root), default);
        var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
        var loaded = ((Outcome<LoadedModel>.Success)await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(settings.Input, settings.Limits, default)).Value;
        const string location = "morphir://ir/workspace/input?format=3.0.0#/module/inventory/value/daily-rate/body";
        var data = System.Text.Json.JsonDocument.Parse($"{{\"location\":\"{location}\"}}").RootElement.Clone();

        var result = ModelUpgrade.Upgrade(loaded, _ => Morphir.Compilation.Checked<Morphir.MiddleEnd.UpgradeOutcome>.Fail([
            Morphir.Compilation.Diagnostic.Create(Morphir.Compilation.DiagnosticSeverity.Error,
                Morphir.MiddleEnd.UpgradeCodes.Unrepresentable, "The node has no form.", null).WithData(data)]));

        var failure = result.Should().BeOfType<Outcome<LoadedModel>.Failure>().Subject;
        var diagnostic = failure.Error.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("ir.upgrade_unrepresentable");
        diagnostic.Location.Should().Be(location);
        diagnostic.Path.Should().Be(loaded.Original.SourcePath);
    }

    [Test]
    public void TheBuiltInCSharpBackendReadsTheCurrentIrOnly()
    {
        new CSharpGenerationBackend().Descriptor.Capabilities.Select(capability => capability.Family)
            .Should().Equal(ModelFamily.V4);
    }

    [Test]
    public async Task AClassicModelIsUpgradedForTheCSharpBackendAndTheAdmissionPassRuns()
    {
        var input = GenerationInputFixtures.DailyRate(Path.Combine(_root, "csharp"), "3.0.0", "json");
        var resolution = await new CliGenerationSettingsResolver().ResolveAsync(new(input, "csharp", Path.Combine(_root, "csharp-out")), new(_root), default);
        var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
        var loaded = ((Outcome<LoadedModel>.Success)await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(settings.Input, settings.Limits, default)).Value;
        var result = await GenerationStages.RunFrom(loaded, settings, new RegisteredBackendResolver([new CSharpGenerationBackend()]), new GenerationPublisher(), default);
        var success = ((Outcome<GenerationSuccess>.Success)result).Value;
        // The report names the model as it was loaded, and the admission pass ran on the upgraded model.
        success.SourceVersion.Should().Be("3.0.0");
        success.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == "transform.skipped");
        success.Passes!.Ran.Should().Contain("morphir-dotnet.csharp.admission");
    }
}
