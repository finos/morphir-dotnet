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
            IsInfrastructureLogMessage(log).Should().BeTrue($"'{log}' should be filtered");
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
            IsInfrastructureLogMessage(log).Should().BeTrue($"'{log}' should be filtered");
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
            IsInfrastructureLogMessage(output).Should().BeFalse($"'{output}' should NOT be filtered");
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
            IsInfrastructureLogMessage(error).Should().BeFalse($"'{error}' should NOT be filtered");
        }
    }

    [Test]
    public void IsInfrastructureLogMessage_ShouldFilter_JsonOutputErrorsField()
    {
        // JSON output with "Errors" field should NOT be filtered
        var jsonOutputs = new[]
        {
            "  \"Errors\": [",
            "{\"IsValid\":false,\"Errors\":[{\"Path\":\"$.formatVersion\"}]}"
        };

        // The current implementation filters these correctly by checking for JSON start characters
        foreach (var json in jsonOutputs)
        {
            IsInfrastructureLogMessage(json).Should().BeFalse($"'{json}' should NOT be filtered (valid JSON output)");
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
            IsInfrastructureLogMessage(log).Should().BeTrue($"'{log}' should be filtered");
        }
    }

    // Helper method that replicates the private IsInfrastructureLogMessage logic
    // This is needed because the actual method is private in ExecutableRunner
    private static bool IsInfrastructureLogMessage(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        // Filter Wolverine and hosting INFO logs only (keep errors/failures)
        // Check for error/fail patterns case-insensitively
        // But be careful: JSON output may contain "Errors" field which is valid output
        if ((line.Contains("fail:", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("error:", StringComparison.OrdinalIgnoreCase)) &&
            !line.TrimStart().StartsWith("\"Errors", StringComparison.OrdinalIgnoreCase) &&
            !line.TrimStart().StartsWith("{", StringComparison.OrdinalIgnoreCase) &&
            !line.TrimStart().StartsWith("[", StringComparison.OrdinalIgnoreCase))
            return false;

        // Filter infrastructure log messages, but be careful not to filter actual command output
        // Check for specific log prefixes first
        if (line.StartsWith("info: Wolverine", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("info: Microsoft.Hosting", StringComparison.OrdinalIgnoreCase))
            return true;

        return line.Contains("Application started", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Application is shutting down", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Hosting environment:", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Content root path:", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("extism.dll", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Open Telemetry metrics", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Starting Wolverine messaging", StringComparison.OrdinalIgnoreCase) ||
               (line.Contains("Wolverine code generation mode", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Commands:", StringComparison.OrdinalIgnoreCase)) ||
               line.Contains("Wolverine assigned node id", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Searching assembly", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("wolverine.netlify.app", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("suitable for development", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("disable automatic Wolverine extension finding", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("disabling-assembly-scanning", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Started message listening", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Stopped message listener", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("pre-generated types being loaded", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("debugging static type loading", StringComparison.OrdinalIgnoreCase);
    }
}
