using System.Text.Json;
using FluentAssertions;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerateCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-command-" + Guid.NewGuid().ToString("N"));
    public GenerateCommandTests() => Directory.CreateDirectory(_root);

    [Test]
    [Arguments("--target missing")]
    [Arguments("--option unknown=true")]
    [Arguments("--option optimize=not-a-boolean")]
    [Arguments("--input-format yaml")]
    [Arguments("--unavailable")]
    [Arguments("--option")]
    public async Task InvalidInvocationReturnsTwoWithoutPublication(string argument)
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4", "json");
        var output = Path.Combine(_root, "output");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var target = argument.StartsWith("--target ", StringComparison.Ordinal) ? "" : "--target fsharp";
        var result = await new CliTestHelper().ExecuteCommandAsync($"generate --input \"{input}\" {target} --output \"{output}\" --json {argument}", timeout.Token);
        result.ExitCode.Should().Be(2, result.CombinedOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        if (argument.StartsWith("--target ", StringComparison.Ordinal)) json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString().Should().Be("unknown-target");
        Directory.Exists(output).Should().BeFalse();
    }

    [Test]
    public async Task RepeatedOptionsPreserveExplicitFalseAndLastValue()
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4.0.0", "binary");
        var output = Path.Combine(_root, "output");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await new CliTestHelper().ExecuteCommandAsync($"generate --input \"{input}\" --target fsharp --output \"{output}\" --input-format ion --option optimize=true --option optimize=false --json", timeout.Token);
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("artifacts").GetArrayLength().Should().BeGreaterThan(0);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
