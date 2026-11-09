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
    public async Task JsonListsTheBuiltInExtensionsOnStdoutOnly(string arguments)
    {
        var result = await Cli(arguments);
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("host").GetString().Should().Be("morphir");
        document.RootElement.GetProperty("extensions").EnumerateArray().Select(entry => entry.GetProperty("id").GetString())
            .Should().Equal("morphir.csharp", "morphir.fsharp", "morphir.type-validator");
    }

    [Test]
    public async Task TextListsOneRowForEachBuiltInExtension()
    {
        var result = await Cli("extension list");
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var rows = result.StandardOutput.Replace("\r\n", "\n").Split('\n').Where(row => row.StartsWith('│')).ToArray();
        rows.Should().HaveCount(4);
        rows[1].Should().StartWith("│ morphir.csharp ");
        rows[2].Should().StartWith("│ morphir.fsharp ");
        rows[3].Should().StartWith("│ morphir.type-validator ").And.Contain("│ validator (1, 2, 3) ");
        result.StandardOutput.Should().NotContain("\u001b[", "redirected output is plain text");
    }
}
