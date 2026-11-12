using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Foundatio.Mediator;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Scenarios;
using Morphir.Testing;
using Morphir.IR.Pipeline;

using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
public sealed class GenerationHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-handler-" + Guid.NewGuid().ToString("N"));
    public GenerationHandlerTests() => Directory.CreateDirectory(_root);

    [Test]
    public void HostedCommandFactoriesReceiveTheApplicationsServiceProvider()
    {
        IServiceProvider? supplied = null;
        using var app = MorphirCli.CreateBuilder([]).AddCommand((IServiceProvider services) =>
        {
            supplied = services;
            return new System.CommandLine.Command("test-hosted");
        }).Build();
        supplied.Should().BeSameAs(app.Services);
    }

    [Test]
    public async Task ExistingHostMediatorResolvesTheGenerationScenarioEndToEnd()
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4.0.0", "text");
        using var app = MorphirCli.CreateBuilder([]).AddScenario<GenerationScenario>().Build();
        var mediator = app.Services.GetRequiredService<IMediator>();
        var result = await mediator.InvokeAsync<Outcome<GenerationSuccess>>(new GenerateRequest(new(input, "fsharp", Path.Combine(_root, "output")), new(_root)));
        var success = ((Outcome<GenerationSuccess>.Success)result).Value;
        success.SourceVersion.Should().Be("4.0.0");
        var files = success.Publication.Artifacts.Select(path => Tuple.Create(path, File.ReadAllText(Path.Combine(success.OutputDirectory, path))));
        GenerationCases.checkDailyRate("4.0.0", "text", "file", files);
    }

    [Test]
    public async Task GenerationStagesFromALoadedModelGiveTheSameResultAsTheHandler()
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4.0.0", "text");
        var loader = new GenerationModelLoader(new BoundedModelCapture());
        var backends = new RegisteredBackendResolver([new FSharpGenerationBackend()]);
        var viaHandler = await GenerateHandler.Handle(new(new(input, "fsharp", Path.Combine(_root, "handler")), new(_root)),
            new CliGenerationSettingsResolver(), loader, backends, new GenerationPublisher(), default);

        var resolution = await new CliGenerationSettingsResolver().ResolveAsync(new(input, "fsharp", Path.Combine(_root, "stages")), new(_root), default);
        var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
        var loaded = ((Outcome<LoadedModel>.Success)await loader.LoadAsync(settings.Input, settings.Limits, default)).Value;
        var viaStages = await GenerationStages.RunFrom(loaded, settings, backends, new GenerationPublisher(), default);

        var expected = ((Outcome<GenerationSuccess>.Success)viaHandler).Value;
        var actual = ((Outcome<GenerationSuccess>.Success)viaStages).Value;
        actual.Should().BeEquivalentTo(expected, options => options.Excluding(success => success.OutputDirectory).Excluding(success => success.Publication));
        actual.Publication.Artifacts.Should().Equal(expected.Publication.Artifacts);
        foreach (var path in expected.Publication.Artifacts)
            File.ReadAllText(Path.Combine(actual.OutputDirectory, path)).Should().Be(File.ReadAllText(Path.Combine(expected.OutputDirectory, path)));
    }

    [Test]
    public async Task GenerationStagesNameTheBackendInAFailure()
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4.0.0", "text");
        var resolution = await new CliGenerationSettingsResolver().ResolveAsync(
            new(input, "fsharp", Path.Combine(_root, "stages")) { Options = ["no-such-option=1"] }, new(_root), default);
        var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
        var loaded = ((Outcome<LoadedModel>.Success)await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(settings.Input, settings.Limits, default)).Value;
        var result = await GenerationStages.RunFrom(loaded, settings, new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), default);
        ((Outcome<GenerationSuccess>.Failure)result).Error.Backend.Should().NotBeNull();
    }

    [Test]
    public async Task SemanticFailureNeverReachesPublication()
    {
        var input = Path.Combine(_root, "model.json"); File.WriteAllText(input, GenerateSteps.V4Model.Replace("5000", "2147483648"));
        var output = Path.Combine(_root, "output"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "prior.fs"), "prior");
        var result = await GenerateHandler.Handle(new(new(input, "fsharp", output), new(_root)), new CliGenerationSettingsResolver(),
            new GenerationModelLoader(new BoundedModelCapture()), new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), default);
        ((Outcome<GenerationSuccess>.Failure)result).Error.Kind.Should().Be(GenerationFailureKind.Semantic);
        File.ReadAllText(Path.Combine(output, "prior.fs")).Should().Be("prior");
        Directory.GetFiles(output).Should().HaveCount(1);
    }
    /// <summary>
    /// A target that is not a valid target id (for example <c>C#</c>) names no registered target. It must fail exactly
    /// like a well-formed unknown target, with the same code, message pattern and list of available targets.
    /// </summary>
    [Test]
    [Arguments("C#")]
    [Arguments("CSharp")]
    [Arguments("a..b")]
    public async Task MalformedTargetFailsLikeAnUnknownTarget(string malformed)
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4.0.0", "text");
        using var app = MorphirCli.CreateBuilder([]).AddScenario<GenerationScenario>().Build();
        var mediator = app.Services.GetRequiredService<IMediator>();
        async Task<GenerationDiagnostic> FailureFor(string target)
        {
            var result = await mediator.InvokeAsync<Outcome<GenerationSuccess>>(new GenerateRequest(new(input, target, Path.Combine(_root, "output")), new(_root)));
            var failure = ((Outcome<GenerationSuccess>.Failure)result).Error;
            failure.Kind.Should().Be(GenerationFailureKind.Invocation, string.Join("; ", failure.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
            return failure.Diagnostics.Single();
        }

        var unknown = await FailureFor("cobol");
        var refused = await FailureFor(malformed);
        refused.Code.Should().Be("unknown-target").And.Be(unknown.Code);
        refused.Message.Should().Be(unknown.Message.Replace("'cobol'", $"'{malformed}'"));
        refused.Message.Should().EndWith("Available targets: csharp, fsharp.");
        Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
    }

    public void Dispose() => TolerantDirectory.TryDelete(_root);
}
