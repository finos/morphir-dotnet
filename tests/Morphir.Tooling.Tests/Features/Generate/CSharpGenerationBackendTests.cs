using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.CodeGeneration.CSharp;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Scenarios;

using Morphir.TestSupport;
using Morphir.Extensions;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class CSharpGenerationBackendTests
{
    private sealed record OtherOptions : ValidatedBackendOptions;

    [Test]
    public void StaticRegistrationIsIdempotentAndRetainsFSharp()
    {
        using var app = MorphirCli.CreateBuilder([]).AddScenario<GenerationScenario>().AddScenario<GenerationScenario>().Build();
        var backends = app.Services.GetServices<IGenerationBackend>().ToArray();
        backends.OfType<CSharpGenerationBackend>().Should().ContainSingle();
        backends.OfType<FSharpGenerationBackend>().Should().ContainSingle();
        app.Services.GetRequiredService<IGenerationBackendResolver>().Should().BeOfType<RegisteredBackendResolver>();
    }

    [Test]
    [Arguments("language-version", "14", true)]
    [Arguments("language-version", "15", true)]
    [Arguments("language-version", "13", false)]
    [Arguments("language-version", "16", false)]
    [Arguments("language-version", "14.5", false)]
    [Arguments("language-version", "\"14\"", false)]
    [Arguments("language-version", "\"preview\"", false)]
    [Arguments("language-version", "\"latest\"", false)]
    [Arguments("language-version", "true", false)]
    [Arguments("language-version", "null", false)]
    [Arguments("language-version", "[]", false)]
    [Arguments("language-version", "{}", false)]
    [Arguments("Language-Version", "14", false)]
    [Arguments("optimize", "true", false)]
    [Arguments("unknown", "14", false)]
    public void AcceptsOnlyExplicitNumericProfiles(string key, string json, bool valid)
    {
        var result = new CSharpGenerationBackend().ValidateOptions(ParseOptions(key, json));
        (result is Outcome<ValidatedBackendOptions>.Success).Should().Be(valid);
        if (!valid)
        {
            var error = ((Outcome<ValidatedBackendOptions>.Failure)result).Error;
            error.Kind.Should().Be(GenerationFailureKind.Invocation);
            error.Diagnostics[0].Code.Should().Be("invalid-backend-option");
        }
    }

    [Test]
    public void DescriptorDeclaresExactlyTheAdmittedLibraryVersions()
    {
        var descriptor = new CSharpGenerationBackend().Descriptor;
        descriptor.Id.Value.Should().Be("morphir.csharp");
        descriptor.Target.Value.Should().Be("csharp");
        descriptor.SupportedSemantics.Should().BeEmpty();
        descriptor.Capabilities.Should().HaveCount(2);
        descriptor.Capabilities.Single(x => x.Family.IsClassic).Versions.Select(version => version.Text).Should().BeEquivalentTo("1", "2", "3", "3.0.0", "3.1.0");
        descriptor.Capabilities.Single(x => x.Family.IsV4).Versions.Select(version => version.Text).Should().BeEquivalentTo("4", "4.0.0");
        descriptor.Capabilities.Should().OnlyContain(x => x.Kind.IsLibrary);
    }

    [Test]
    public void BothBackendDescriptorsBuildFromValidLiterals()
    {
        var csharp = new CSharpGenerationBackend().Descriptor;
        var fsharp = new FSharpGenerationBackend().Descriptor;
        (csharp.Id.Value, csharp.Name.Value, csharp.Target.Value).Should().Be(("morphir.csharp", "C# generator", "csharp"));
        (fsharp.Id.Value, fsharp.Name.Value, fsharp.Target.Value).Should().Be(("morphir.fsharp", "F# generator", "fsharp"));
        csharp.Description!.Value.Should().Be("Generates C# source from a Morphir library model.");
        fsharp.Description!.Value.Should().Be("Generates F# source from a Morphir library model.");
        fsharp.Capabilities.SelectMany(capability => capability.Versions).Select(version => version.Text)
            .Should().Equal("1", "2", "3", "3.0.0", "3.1.0", "4", "4.0.0");
        var refused = () => SourceFormatVersions.Create("4", "four");
        refused.Should().Throw<ArgumentException>().WithMessage("*four*");
    }

    [Test]
    [Arguments("CSharp", "unknown-target")]
    [Arguments("cs", "unknown-target")]
    [Arguments("csharp", "ambiguous-backend")]
    public void UsesTheGenericExactResolver(string target, string code)
    {
        var descriptor = new ModelDescriptor(SourceFormatVersionModule.parse("4").ResultValue, ModelFamily.V4, DistributionKind.Library);
        var result = new RegisteredBackendResolver([new CSharpGenerationBackend(), new CSharpGenerationBackend()]).Resolve(RequestedTarget.Parse(target), descriptor);
        ((Outcome<IGenerationBackend>.Failure)result).Error.Diagnostics[0].Code.Should().Be(code);
    }

    [Test]
    [Arguments("version")]
    [Arguments("kind")]
    [Arguments("requirements")]
    public void ResolverRefusesUnsupportedCapabilities(string boundary)
    {
        var model = new ModelDescriptor(SourceFormatVersionModule.parse("4").ResultValue, ModelFamily.V4, DistributionKind.Library);
        model = boundary switch
        {
            "version" => model with { Version = SourceFormatVersionModule.parse("4.1.0").ResultValue },
            "kind" => model with { Kind = DistributionKind.Specs },
            _ => model with { RequiredSemantics = ["example.required"] }
        };
        var result = new RegisteredBackendResolver([new CSharpGenerationBackend()]).Resolve(RequestedTarget.Parse("csharp"), model);
        ((Outcome<IGenerationBackend>.Failure)result).Error.Diagnostics[0].Code.Should()
            .Be(boundary == "requirements" ? "unsupported-required-semantics" : "incompatible-model");
    }

    [Test]
    [Arguments("target")]
    [Arguments("options")]
    [Arguments("requirements")]
    [Arguments("cancelled")]
    public async Task DirectRequestsCannotBypassValidatedBoundaries(string boundary)
    {
        var loaded = await Load("4", "json");
        if (boundary == "requirements") loaded = loaded with { Descriptor = loaded.Descriptor with { RequiredSemantics = ["example.required"] } };
        var backend = new CSharpGenerationBackend();
        var options = boundary == "options" ? new OtherOptions() : Validated(backend, BackendOptions.Empty);
        using var cancellation = new CancellationTokenSource();
        if (boundary == "cancelled") cancellation.Cancel();
        var result = await backend.GenerateAsync(new(loaded, TargetId.Create(boundary == "target" ? "other" : "csharp"), options), cancellation.Token);
        var error = ((Outcome<GenerationArtifacts>.Failure)result).Error;
        error.Kind.Should().Be(boundary switch
        {
            "cancelled" => GenerationFailureKind.Cancelled,
            "requirements" => GenerationFailureKind.Semantic,
            _ => GenerationFailureKind.Invocation
        });
    }

    [Test]
    [Arguments("\"Example.Generated\"", true)]
    [Arguments("\"@class.模型\"", true)]
    [Arguments("\"X;class Injected{}\"", false)]
    [Arguments("\"X..Y\"", false)]
    [Arguments("\"class\"", false)]
    [Arguments("14", false)]
    [Arguments("true", false)]
    [Arguments("null", false)]
    [Arguments("[]", false)]
    public void NamespaceOptionsUseTheCoreLiteralNameBoundary(string json, bool valid)
    {
        var result = new CSharpGenerationBackend().ValidateOptions(ParseOptions("namespace", json));
        (result is Outcome<ValidatedBackendOptions>.Success).Should().Be(valid);
        if (!valid) ((Outcome<ValidatedBackendOptions>.Failure)result).Error.Kind.Should().Be(GenerationFailureKind.Invocation);
    }

    [Test]
    [Arguments("3", "json")]
    [Arguments("4", "json")]
    [Arguments("3.0.0", "text")]
    [Arguments("4.0.0", "binary")]
    [Arguments("3.1.0", "tree")]
    public async Task ProviderAndCoreReturnIdenticalNamesAndBytes(string version, string format)
    {
        var loaded = await Load(version, format);
        var backend = new CSharpGenerationBackend();
        var before = loaded.Model;
        foreach (var name in new string?[] { null, "First.Generated", "Second.Generated" })
        {
            var options = name is null ? BackendOptions.Empty : ParseOptions("namespace", JsonSerializer.Serialize(name));
            var result = (Outcome<GenerationArtifacts>.Success)await backend.GenerateAsync(new(loaded, TargetId.Create("csharp"), Validated(backend, options)), default);
            var direct = name is null
                ? CSharpGenerator.Generate(loaded.Model, GenerationOptions.Default, default)
                : CSharpGenerator.Generate(loaded.Model, GenerationOptions.Default, NamespaceOverride.TryCreate(name).ResultValue, default);
            direct.Status.IsSuccess.Should().BeTrue();
            var files = VFileTreeModule.toStringMap(result.Value.Tree).ResultValue;
            files.Select(x => (x.Key, x.Value)).Should().Equal(direct.Sources.Select(x => (x.RelativePath, x.Text)));
            loaded.Model.Should().BeSameAs(before);
        }
    }

    [Test]
    public async Task LocatedCoreRefusalsRemainSemanticAndContainNoArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "morphir-csharp-refusal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = GenerationInputFixtures.DailyRate(root, "4", "json");
            File.WriteAllText(input, File.ReadAllText(input).Replace("\"WholeNumberLiteral\": 5000", "\"DocumentLiteral\": {\"unsupported\":true}"));
            var loaded = ((Outcome<LoadedModel>.Success)await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(input), ModelReadLimits.Default, default)).Value;
            var backend = new CSharpGenerationBackend();
            var result = await backend.GenerateAsync(new(loaded, TargetId.Create("csharp"), Validated(backend, BackendOptions.Empty)), default);
            var error = ((Outcome<GenerationArtifacts>.Failure)result).Error;
            error.Kind.Should().Be(GenerationFailureKind.Semantic);
            error.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("document-literal");
            error.Diagnostics[0].Path.Should().Be(input);
            error.Diagnostics[0].Location.Should().NotBeNullOrEmpty();
        }
        finally
        {
            TolerantDirectory.TryDelete(root);
        }
    }

    internal static BackendOptions ParseOptions(string key, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(new Dictionary<string, JsonElement> { [key] = document.RootElement });
    }

    internal static ValidatedBackendOptions Validated(IGenerationBackend backend, BackendOptions options) =>
        ((Outcome<ValidatedBackendOptions>.Success)backend.ValidateOptions(options)).Value;

    internal static async Task<LoadedModel> Load(string version, string format)
    {
        var root = Path.Combine(Path.GetTempPath(), "morphir-csharp-provider-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = GenerationInputFixtures.DailyRate(root, version, format);
            var result = await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(input), ModelReadLimits.Default, default);
            return ((Outcome<LoadedModel>.Success)result).Value;
        }
        finally
        {
            // The loader captures the model's bytes, so the fixture can go once loading returns.
            TolerantDirectory.TryDelete(root);
        }
    }
}
