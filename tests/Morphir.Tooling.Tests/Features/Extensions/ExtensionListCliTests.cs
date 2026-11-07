using System.Text.Json;
using FluentAssertions;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>Runs <c>morphir extension list</c> through the real CLI process.</summary>
public sealed class ExtensionListCliTests
{
    private static Task<CliExecutionResult> Cli(string arguments) =>
        new CliTestHelper(Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI")).ExecuteCommandAsync(arguments, timeout: TimeSpan.FromSeconds(60));

    [Test]
    [Arguments("extension list --json")]
    [Arguments("extension list --local --json")]
    [Arguments("extension list --platform linux-x64 --json")]
    public async Task JsonListsTheBuiltInGeneratorsOnStdoutOnly(string arguments)
    {
        var result = await Cli(arguments);
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("host").GetString().Should().Be("morphir");
        document.RootElement.GetProperty("extensions").EnumerateArray().Select(entry => entry.GetProperty("id").GetString())
            .Should().Equal("morphir.csharp", "morphir.fsharp");
    }

    [Test]
    public async Task TextListsOneRowForEachBuiltInGenerator()
    {
        var result = await Cli("extension list");
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var rows = result.StandardOutput.Split('\n');
        rows.Should().HaveCount(3);
        rows[1].Should().StartWith("morphir.csharp ");
        rows[2].Should().StartWith("morphir.fsharp ");
    }
}
