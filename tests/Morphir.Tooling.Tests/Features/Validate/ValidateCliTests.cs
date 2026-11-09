using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Validate;

/// <summary>Runs <c>morphir validate</c> through the real CLI process of each host.</summary>
public sealed class ValidateCliTests
{
    private static Task<CliExecutionResult> Cli(string arguments, CliHost host = CliHost.Morphir) =>
        new CliTestHelper(host == CliHost.Morphir ? Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI") : null, host)
            .ExecuteCommandAsync(arguments, timeout: TimeSpan.FromSeconds(60));

    private static string Fixture(string name) => ModelValidationTests.Fixture(name);

    [Test]
    [Arguments("daily-rate-mismatch-v3.json", 1)]
    [Arguments("daily-rate-v3.json", 0)]
    [Arguments("daily-rate-v4.json", 0)]
    public async Task StdoutHoldsOnlyTheJsonReportAndItsExitCodeIsTheProcessExitCode(string fixture, int exitCode)
    {
        var result = await Cli($"validate --input \"{Fixture(fixture)}\" --json");
        result.ExitCode.Should().Be(exitCode, result.CombinedOutput);
        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("exitCode").GetInt32().Should().Be(exitCode);
        document.RootElement.GetProperty("success").GetBoolean().Should().Be(exitCode == 0);
    }

    [Test]
    public async Task AMissingInputExitsWithTwoAndNamesThePath()
    {
        var result = await Cli("validate --input missing.json");
        result.ExitCode.Should().Be(2, result.CombinedOutput);
        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().Contain("missing.json");
    }

    [Test]
    public async Task AnUnknownOptionExitsWithTwo()
    {
        var result = await Cli("validate --input model.json --no-such-option --json");
        result.ExitCode.Should().Be(2, result.CombinedOutput);
        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("exitCode").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task TheTextOutputIsAPlainTableWhenStdoutIsRedirected()
    {
        var result = await Cli($"validate --input \"{Fixture("daily-rate-mismatch-v3.json")}\"");
        result.ExitCode.Should().Be(1, result.CombinedOutput);
        result.StandardOutput.Should().NotContain("\u001b[", "redirected output is plain text");
        var lines = result.StandardOutput.Replace("\r\n", "\n").Split('\n');
        lines.Should().Contain(line => line.StartsWith("│ SEVERITY "));
        lines.Should().Contain(line => line.StartsWith("│ error ") && line.Contains("type-mismatch"));
        lines[^1].Should().StartWith("1 error, 0 warnings");
    }

    [Test]
    [Arguments("daily-rate-mismatch-v3.json")]
    [Arguments("daily-rate-v4.json")]
    public async Task BothHostsWriteTheSameReportApartFromTheHost(string fixture)
    {
        var arguments = $"validate --input \"{Fixture(fixture)}\" --json";
        var native = await Cli(arguments);
        var tool = await Cli(arguments, CliHost.DotnetTool);
        native.ExitCode.Should().Be(tool.ExitCode, tool.CombinedOutput);
        var first = JsonNode.Parse(native.StandardOutput)!.AsObject();
        var second = JsonNode.Parse(tool.StandardOutput)!.AsObject();
        first["host"]!.GetValue<string>().Should().Be("morphir");
        second["host"]!.GetValue<string>().Should().Be("dotnet-morphir");
        first.Remove("host");
        second.Remove("host");
        first.ToJsonString().Should().Be(second.ToJsonString());
    }
}
