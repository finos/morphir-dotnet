using FluentAssertions;
using TUnit.Core;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// Unit tests for ExecutableRunner log filtering to ensure infrastructure logs don't contaminate test output
/// </summary>
public class ExecutableRunnerTests
{
    [Test]
    public void IsInfrastructureLogMessage_ShouldFilter_WolverineFx58Patterns()
    {
        // WolverineFx 5.8.0 log patterns
        var wolverine58Logs = new[]
        {
            "[07:05:03 INF] Searching assembly Morphir.Tooling, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null for Wolverine message handlers",
            "[07:05:03 INF] Wolverine assigned node id for envelope persistence is -2035786101",
            "[07:05:03 INF] Started message listening at stub://replies/",
            "[07:05:03 INF] Application started. Press Ctrl+C to shut down.",
            "[07:05:03 INF] Hosting environment: Production",
            "[07:05:03 INF] Content root path: /home/runner/work/morphir-dotnet/morphir-dotnet",
            "[07:05:03 INF] Application is shutting down...",
            "[07:05:30 INF] Starting Wolverine messaging for application assembly Morphir.Tooling, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null",
            "[07:05:30 INF] The Wolverine code generation mode is Static with pre-generated types being loaded from Morphir.Tooling, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null.",
            "[07:05:30 INF] See https://wolverine.netlify.app/guide/codegen.html for more information about debugging static type loading issues with Wolverine"
        };

        foreach (var log in wolverine58Logs)
        {
            ExecutableRunner.IsInfrastructureLogMessage(log).Should().BeTrue($"'{log}' should be filtered");
        }
    }

    [Test]
    public void IsInfrastructureLogMessage_ShouldFilter_WolverineFx59Patterns()
    {
        // WolverineFx 5.9.0 new log patterns
        var wolverine59Logs = new[]
        {
            "[07:06:06 INF] Exporting Open Telemetry metrics from Wolverine with name Wolverine:Morphir.Tooling, version 5.9.0.0",
            // All 5.8.0 patterns should still work
            "[07:06:06 INF] Starting Wolverine messaging for application assembly Morphir.Tooling, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null",
            "[07:06:06 INF] The Wolverine code generation mode is Static with pre-generated types being loaded from Morphir.Tooling, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null.",
            "[07:06:06 INF] See https://wolverine.netlify.app/guide/codegen.html for more information about debugging static type loading issues with Wolverine",
            "[07:06:06 INF] Searching assembly Morphir.Tooling, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null for Wolverine message handlers",
            "[07:06:06 INF] Wolverine assigned node id for envelope persistence is -1376780885",
            "[07:06:06 INF] Started message listening at stub://replies/",
            "[07:06:06 INF] Application started. Press Ctrl+C to shut down.",
            "[07:06:06 INF] Hosting environment: Production",
            "[07:06:06 INF] Content root path: /home/runner/work/morphir-dotnet/morphir-dotnet",
            "[07:06:06 INF] Application is shutting down..."
        };

        foreach (var log in wolverine59Logs)
        {
            ExecutableRunner.IsInfrastructureLogMessage(log).Should().BeTrue($"'{log}' should be filtered");
        }
    }

    [Test]
    public void IsInfrastructureLogMessage_ShouldNotFilter_ActualCommandOutput()
    {
        // Actual command output that should NOT be filtered
        var commandOutputs = new[]
        {
            "✓ VALID",
            "✗ INVALID",
            "Schema Version: v3",
            "Morphir command line",
            "Commands:",
            "Description:",
            "Usage:",
            "{\"IsValid\":true,\"SchemaVersion\":\"3\",\"DetectionMethod\":\"auto\"}",
            "[{\"Path\":\"$.formatVersion\",\"Message\":\"Missing required property\"}]",
            "⚠ Warning: Code generation directory not found",
            "✓ Wolverine code written to src/Morphir.Tooling/Internal/Generated"
        };

        foreach (var output in commandOutputs)
        {
            ExecutableRunner.IsInfrastructureLogMessage(output).Should().BeFalse($"'{output}' should NOT be filtered");
        }
    }

    [Test]
    public void IsInfrastructureLogMessage_ShouldNotFilter_ErrorMessages()
    {
        // Error messages that should NOT be filtered (except when they're JSON output)
        var errorMessages = new[]
        {
            "fail: Validation failed",
            "error: File not found",
            "Error: Invalid schema version"
        };

        foreach (var error in errorMessages)
        {
            ExecutableRunner.IsInfrastructureLogMessage(error).Should().BeFalse($"'{error}' should NOT be filtered");
        }
    }

    [Test]
    public void IsInfrastructureLogMessage_ShouldNotFilter_JsonOutputErrorsField()
    {
        // JSON output with "Errors" field should NOT be filtered
        var jsonOutputs = new[]
        {
            "  \"Errors\": [",
            "{\"IsValid\":false,\"Errors\":[{\"Path\":\"$.formatVersion\"}]}"
        };

        // These should NOT be filtered because they are valid JSON output
        foreach (var json in jsonOutputs)
        {
            ExecutableRunner.IsInfrastructureLogMessage(json).Should().BeFalse($"'{json}' should NOT be filtered (valid JSON output)");
        }
    }

    [Test]
    public void IsInfrastructureLogMessage_ShouldFilter_InfoPrefixedLogs()
    {
        // Logs with "info:" prefix from Microsoft.Hosting or Wolverine
        var infoPrefixedLogs = new[]
        {
            "info: Wolverine.WolverineExtensions[0]",
            "info: Microsoft.Hosting.Lifetime[0]"
        };

        foreach (var log in infoPrefixedLogs)
        {
            ExecutableRunner.IsInfrastructureLogMessage(log).Should().BeTrue($"'{log}' should be filtered");
        }
    }
}
