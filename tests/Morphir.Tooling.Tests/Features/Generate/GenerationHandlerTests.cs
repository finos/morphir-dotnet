using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Foundatio.Mediator;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Scenarios;
using Morphir.Testing;
using Morphir.IR.Pipeline;

using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Generate;

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
