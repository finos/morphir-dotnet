using System.Text;
using FluentAssertions;
using Morphir.IR.Model;
using Morphir.Testing;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerationModelLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-load-" + Guid.NewGuid().ToString("N"));
    public GenerationModelLoaderTests() => Directory.CreateDirectory(_root);
    private async Task<Outcome<LoadedModel>> Load(string text, InputFormat format = InputFormat.Auto, ModelReadLimits? limits = null)
    {
        var path = Path.Combine(_root, "model.json");
        await File.WriteAllTextAsync(path, text);
        return await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(path, format), limits ?? ModelReadLimits.Default, default);
    }

    [Test]
    [Arguments("1")]
    [Arguments("2")]
    [Arguments("3")]
    [Arguments("3.1.0")]
    [Arguments("4")]
    [Arguments("4.0.0")]
    public async Task JsonUsesSharedCodecsAndPreservesExactVersion(string version)
    {
        var text = version.StartsWith('4') ? GenerateSteps.V4Model : File.ReadAllText(Path.Combine(GenerateSteps.RepositoryRoot(), "examples", "car-rental", "models", $"car-rental-v{version[0]}.json"));
        if (version.Contains('.')) text = text.Replace($"\"formatVersion\": {version[0]}", $"\"formatVersion\": \"{version}\"")
            .Replace($"\"formatVersion\":{version[0]}", $"\"formatVersion\":\"{version}\"");
        var loaded = ((Outcome<LoadedModel>.Success)await Load(text)).Value;
        loaded.Model.SourceVersion.Text.Should().Be(version);
        loaded.Descriptor.Kind.Should().Be(DistributionKind.Library);
        loaded.Descriptor.Family.Should().Be(version.StartsWith('4') ? ModelFamily.V4 : ModelFamily.Classic);
        loaded.Representation.Should().Be(ModelRepresentation.Json);
        var original = (RetainedInput.File)loaded.Original;
        File.WriteAllText(original.SourcePath, "changed");
        Encoding.UTF8.GetString(original.Entry.CopyBytes()).Should().Be(text);
    }

    [Test]
    [Arguments("{ broken", "malformed-json")]
    [Arguments("{\"distribution\":[]}", "unsupported-version")]
    [Arguments("{\"formatVersion\":99,\"distribution\":[]}", "unsupported-version")]
    [Arguments("{\"formatVersion\":4,\"formatVersion\":4,\"distribution\":[]}", "duplicate_format_version")]
    public async Task JsonFailuresKeepCodecRules(string text, string code)
    {
        var error = ((Outcome<LoadedModel>.Failure)await Load(text)).Error;
        error.Kind.Should().Be(GenerationFailureKind.Semantic);
        error.Diagnostics[0].Code.Should().Be(code);
        error.Diagnostics[0].Path.Should().EndWith("model.json");
        error.Diagnostics[0].Location.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ExplicitJsonDoesNotFallBackToIon()
    {
        var result = await Load("$ion_1_0 morphir::{}", InputFormat.Json);
        ((Outcome<LoadedModel>.Failure)result).Error.Diagnostics[0].Code.Should().Be("malformed-json");
    }

    [Test]
    [Arguments("nodes")]
    [Arguments("depth")]
    public async Task JsonBudgetsRunBeforeModelAllocation(string budget)
    {
        var limits = budget == "nodes" ? new ModelReadLimits(MaxNodes: 1) : new ModelReadLimits(MaxDepth: 1);
        var result = await Load(GenerateSteps.V4Model, limits: limits);
        ((Outcome<LoadedModel>.Failure)result).Error.Diagnostics[0].Code.Should().Be("input-limit");
    }
    public void Dispose() => Directory.Delete(_root, true);
}
