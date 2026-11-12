using System.Text.Json;
using FluentAssertions;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>Runs <c>morphir extension list</c> through the real CLI process.</summary>
[Category("PlatformSpecific")]
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
            .Should().Equal("morphir-dotnet.fsharp", "morphir.csharp", "morphir.fsharp", "morphir.type-validator");
        // A development build of the AOT entry point: it states no deployment and has no in-process F# frontend.
        document.RootElement.TryGetProperty("deployment", out _).Should().BeFalse();
        var frontend = document.RootElement.GetProperty("extensions")[0];
        frontend.GetProperty("available").GetBoolean().Should().BeFalse();
        frontend.GetProperty("reason").GetString().Should().Contain("morphir-fsharp-frontend");
    }

    [Test]
    public async Task TextListsOneRowForEachBuiltInExtension()
    {
        var result = await Cli("extension list");
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var rows = result.StandardOutput.Replace("\r\n", "\n").Split('\n').Where(row => row.StartsWith('│')).ToArray();
        rows.Should().HaveCount(5);
        rows[1].Should().StartWith("│ morphir-dotnet.fsharp ").And.Contain("│ frontend (fsharp) ");
        rows[2].Should().StartWith("│ morphir.csharp ");
        rows[3].Should().StartWith("│ morphir.fsharp ");
        rows[4].Should().StartWith("│ morphir.type-validator ").And.Contain("│ validator (1, 2, 3) ");
        result.StandardOutput.Should().NotContain("\u001b[", "redirected output is plain text");
    }
}
