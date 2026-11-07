using System.Text.Json;
using FluentAssertions;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features;

/// <summary>
/// The state that CLI step classes share in one scenario. Reqnroll injects one instance per scenario,
/// so compile and generate steps can check the same command result.
/// </summary>
public sealed class CliRun
{
    /// <summary>The result of the last CLI command in the scenario.</summary>
    public CliExecutionResult? Result { get; set; }

    /// <summary>The generation output directory, when a step other than the generate steps chose it.</summary>
    public string? GenerationOutput { get; set; }

    /// <summary>Checks the exit code and that stdout holds exactly one JSON report that agrees with it.</summary>
    public void ShouldExitWithOneJsonReport(int exitCode)
    {
        Result.Should().NotBeNull();
        Result!.ExitCode.Should().Be(exitCode, Result.CombinedOutput);
        using var json = JsonDocument.Parse(Result.StandardOutput);
        json.RootElement.GetProperty("success").GetBoolean().Should().Be(exitCode == 0);
        json.RootElement.GetProperty("diagnostics").ValueKind.Should().Be(JsonValueKind.Array);
    }

    /// <summary>Parses the single JSON report on stdout.</summary>
    public JsonDocument Report() => JsonDocument.Parse(Result!.StandardOutput);
}
