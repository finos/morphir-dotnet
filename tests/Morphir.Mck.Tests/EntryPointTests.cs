using FluentAssertions;
using Morphir.Mck;

namespace Morphir.Mck.Tests;

public sealed class EntryPointTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DedicatedHelpAcceptsDirectAndForwardedArguments(bool forwarded)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var arguments = forwarded ? new[] { "mck", "wasm-host", "run", "--help" } : ["wasm-host", "run", "--help"];
        var status = await MckApplication.RunAsync(arguments, output, error);
        status.Should().Be(0);
        (output.ToString() + error).Should().Contain("--kit").And.Contain("--host");
    }

    [Test]
    public async Task MissingArgumentsReturnUsageJsonOnStdout()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var status = await MckApplication.RunAsync(["wasm-host", "run"], output, error);
        status.Should().Be(2);
        using var json = System.Text.Json.JsonDocument.Parse(output.ToString());
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("mck.usage");
    }
}
