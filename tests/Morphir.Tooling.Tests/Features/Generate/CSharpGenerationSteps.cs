using System.Text.Json;
using FluentAssertions;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Generate;

[Binding]
public sealed class CSharpGenerationSteps
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-csharp-bdd-" + Guid.NewGuid().ToString("N"));
    private string _input = "";
    private CliExecutionResult? _result;
    private string Output => Path.Combine(_root, "output");

    [Given("a C# daily-rate library at version {string} in {string}")]
    public void Model(string version, string format) => _input = GenerationInputFixtures.DailyRate(_root, version, format);

    [When("I invoke the real C# generation command")]
    public async Task Generate()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _result = await new CliTestHelper(Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI"))
            .ExecuteCommandAsync($"generate --input \"{_input}\" --target csharp --output \"{Output}\" --json", timeout.Token);
    }

    [Then("the C# command exits with {int} and one JSON report")]
    public void Report(int code)
    {
        _result.Should().NotBeNull();
        _result!.ExitCode.Should().Be(code, _result.CombinedOutput);
        using var report = JsonDocument.Parse(_result.StandardOutput);
        report.RootElement.GetProperty("success").GetBoolean().Should().Be(code == 0);
    }

    [Then("the C# report identifies the registered provider")]
    public void Provider()
    {
        using var report = JsonDocument.Parse(_result!.StandardOutput);
        report.RootElement.GetProperty("target").GetString().Should().Be("csharp");
        report.RootElement.GetProperty("backend").GetString().Should().Be("morphir.csharp");
    }

    [Then("the C# daily-rate source is published")]
    public void Source()
    {
        using var report = JsonDocument.Parse(_result!.StandardOutput);
        var files = report.RootElement.GetProperty("artifacts").EnumerateArray().Select(x => x.GetString()!).ToArray();
        files.Should().ContainSingle().Which.Should().EndWith(".g.cs");
        File.ReadAllText(Path.Combine(Output, files[0])).Should().Contain("5000").And.Contain("#nullable enable");
    }
}
