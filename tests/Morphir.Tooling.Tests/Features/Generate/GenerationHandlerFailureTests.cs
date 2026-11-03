using FluentAssertions;
using Microsoft.Extensions.Logging;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerationHandlerFailureTests
{
    [Test]
    [Arguments("3")]
    [Arguments("4")]
    public async Task UnexpectedProviderFailureIsLoggedOnceAndNeverPublished(string version)
    {
        var root = Path.Combine(Path.GetTempPath(), "morphir-provider-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = GenerationInputFixtures.DailyRate(root, version, "json");
            var output = Path.Combine(root, "output");
            var logger = new CaptureLogger();
            var result = await GenerateHandler.Handle(new(new(path, "failing", output), new(root)), new CliGenerationSettingsResolver(),
                new GenerationModelLoader(new BoundedModelCapture()), new RegisteredBackendResolver([new BrokenBackend()]), new GenerationPublisher(), default, logger);
            var error = ((Outcome<GenerationSuccess>.Failure)result).Error;
            error.Kind.Should().Be(GenerationFailureKind.Operational);
            error.Diagnostics[0].Code.Should().Be("generation-failed");
            error.Diagnostics[0].Message.Should().NotContain("Simulated");
            GenerationReport.From(result, new(path, "failing", output)).Backend.Should().Be("test.failing");
            logger.Errors.Should().ContainSingle().Which.Should().BeOfType<IOException>();
            Directory.Exists(output).Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed record Options : ValidatedBackendOptions;
    private sealed class BrokenBackend : IGenerationBackend
    {
        public BackendDescriptor Descriptor { get; } = new("test.failing", new("failing"),
            [new(ModelFamily.Classic, ["3"], DistributionKind.Library), new(ModelFamily.V4, ["4"], DistributionKind.Library)]);
        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => new Outcome<ValidatedBackendOptions>.Success(new Options());
        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct) => throw new IOException("Simulated backend failure");
    }
    private sealed class CaptureLogger : ILogger<GenerateRequest>
    {
        public List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (exception is not null) Errors.Add(exception); }
    }
}
