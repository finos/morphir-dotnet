using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;
using Morphir.Testing;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public class FSharpGenerationBackendTests
{
    private sealed record OtherOptions : ValidatedBackendOptions;

    [Test]
    [Arguments("options")]
    [Arguments("target")]
    [Arguments("cancelled")]
    [Arguments("requirements")]
    public async Task DirectBackendRequestsRespectValidatedBoundaries(string boundary)
    {
        using var json = JsonDocument.Parse(GenerateSteps.V4Model);
        var distribution = Morphir.Json.V4.V4Json.readDistribution(json.RootElement).ResultValue.Distribution;
        var model = new VersionedModel(SourceFormatVersionModule.parse("4").ResultValue, DistributionModel.NewV4(distribution));
        var descriptor = ModelDescriptor.From(model);
        if (boundary == "requirements") descriptor = descriptor with { RequiredSemantics = ["example.required"] };
        var loaded = new LoadedModel(model, descriptor, ModelRepresentation.Json, new RetainedInput.File("model.json", new RetainedEntry("model.json", [])));
        var backend = new FSharpGenerationBackend();
        var options = boundary == "options" ? new OtherOptions() : ((Outcome<ValidatedBackendOptions>.Success)backend.ValidateOptions(BackendOptions.Empty)).Value;
        using var cancellation = new CancellationTokenSource();
        if (boundary == "cancelled") cancellation.Cancel();
        var result = await backend.GenerateAsync(new(loaded, new(boundary == "target" ? "other" : "fsharp"), options), cancellation.Token);
        var error = ((Outcome<GenerationArtifacts>.Failure)result).Error;
        error.Kind.Should().Be(boundary switch { "cancelled" => GenerationFailureKind.Cancelled, "requirements" => GenerationFailureKind.Semantic, _ => GenerationFailureKind.Invocation });
    }

    [Test]
    public async Task RegisteredDescriptorGeneratesV4ThroughThePublicTypedApi()
    {
        using var json = JsonDocument.Parse(GenerateSteps.V4Model);
        var decoded = Morphir.Json.V4.V4Json.readDistribution(json.RootElement);
        if (decoded.IsError) throw new InvalidOperationException($"{decoded.ErrorValue.Rule} at {decoded.ErrorValue.Location}: {decoded.ErrorValue.Message}");
        var distribution = decoded.ResultValue.Distribution;
        var model = new VersionedModel(SourceFormatVersionModule.parse("4").ResultValue, DistributionModel.NewV4(distribution));
        var original = new RetainedInput.File("kept/model.json", new RetainedEntry("model.json", "unused"u8));
        var loaded = new LoadedModel(model, ModelDescriptor.From(model), ModelRepresentation.Json, original);
        var provider = new FSharpGenerationBackend();
        var resolved = ((Outcome<IGenerationBackend>.Success)new RegisteredBackendResolver([provider]).Resolve(new("fsharp"), loaded.Descriptor)).Value;
        var options = ((Outcome<ValidatedBackendOptions>.Success)resolved.ValidateOptions(BackendOptions.Empty)).Value;
        var result = (Outcome<GenerationArtifacts>.Success)await resolved.GenerateAsync(new(loaded, new("fsharp"), options), default);
        var files = VFileTreeModule.allFiles(result.Value.Tree).Select(file => Tuple.Create(file.Path.Value, VFileModule.readText(file).ResultValue));
        GenerationCases.checkDailyRate("4", "typed", "memory", files);
        VFileTreeModule.tryGet(Morphir.CodeGeneration.FSharp.Transpiler.manifest, result.Value.Tree).Value.Length.Should().Be(1);
    }

    [Test]
    [Arguments("optimize", "false", true)]
    [Arguments("optimize", "\"false\"", false)]
    [Arguments("unknown", "true", false)]
    public void ValidatesOnlyDeclaredBooleanOptions(string key, string value, bool valid)
    {
        using var json = JsonDocument.Parse(value);
        var result = new FSharpGenerationBackend().ValidateOptions(new BackendOptions(new Dictionary<string, JsonElement> { [key] = json.RootElement }));
        (result is Outcome<ValidatedBackendOptions>.Success).Should().Be(valid);
        if (!valid) ((Outcome<ValidatedBackendOptions>.Failure)result).Error.Kind.Should().Be(GenerationFailureKind.Invocation);
    }
}
