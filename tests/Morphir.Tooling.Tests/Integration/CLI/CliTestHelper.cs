using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Morphir.Tooling.Tests.Integration.CLI;

/// <summary>
/// Helper class for running CLI commands in integration tests
/// </summary>
public class CliTestHelper
{
    private readonly string _cliProjectPath;
    private readonly string _testDataDirectory;

    public CliTestHelper()
    {
        // Navigate from test project to CLI project
        var currentDir = Directory.GetCurrentDirectory();
        var repoRoot = FindRepositoryRoot(currentDir);

        _cliProjectPath = Path.Combine(repoRoot, "src", "Morphir", "Morphir.csproj");
        _testDataDirectory = Path.Combine(repoRoot, "tests", "Morphir.Tooling.Tests", "TestData");

        if (!File.Exists(_cliProjectPath))
        {
            throw new InvalidOperationException($"CLI project not found at: {_cliProjectPath}");
        }

        if (!Directory.Exists(_testDataDirectory))
        {
            throw new InvalidOperationException($"Test data directory not found at: {_testDataDirectory}");
        }
    }

    /// <summary>
    /// Execute a CLI command and return the result
    /// </summary>
    public async Task<CliExecutionResult> ExecuteCommandAsync(string arguments, CancellationToken ct = default)
    {
        // Replace <test-data> placeholder with actual path
        arguments = arguments.Replace("<test-data>", _testDataDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{_cliProjectPath}\" --no-build -- {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null && !IsInfrastructureLogMessage(e.Data))
                stdoutBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null && !IsInfrastructureLogMessage(e.Data))
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        return new CliExecutionResult(
            ExitCode: process.ExitCode,
            StandardOutput: stdoutBuilder.ToString().Trim(),
            StandardError: stderrBuilder.ToString().Trim()
        );
    }

    /// <summary>
    /// Check if a line is an infrastructure log message that should be filtered
    /// </summary>
    private static bool IsInfrastructureLogMessage(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        // Filter Wolverine and hosting INFO logs only (keep errors/failures)
        if (line.Contains("fail:") || line.Contains("error:") || line.Contains("Error"))
            return false;

        return line.Contains("info: Wolverine") ||
               line.Contains("info: Microsoft.Hosting") ||
               line.Contains("Application started") ||
               line.Contains("Application is shutting down") ||
               line.Contains("Hosting environment:") ||
               line.Contains("Content root path:") ||
               line.Contains("extism.dll") ||
               line.Contains("Open Telemetry metrics") ||
               line.Contains("Starting Wolverine messaging") ||
               line.Contains("Wolverine code generation mode") ||
               line.Contains("Wolverine assigned node id") ||
               line.Contains("Searching assembly") ||
               line.Contains("wolverine.netlify.app") ||
               line.Contains("suitable for development") ||
               line.Contains("disable automatic Wolverine extension finding") ||
               line.Contains("disabling-assembly-scanning") ||
               line.Contains("Started message listening") ||
               line.Contains("Stopped message listener");
    }

    /// <summary>
    /// Find repository root by looking for .git directory
    /// </summary>
    private static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root (.git directory)");
    }

    /// <summary>
    /// Check if string is valid JSON
    /// </summary>
    public static bool IsValidJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parse JSON and get field value as a primitive (string, number, boolean)
    /// </summary>
    public static object? GetJsonFieldValue(string json, string fieldPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement;

            foreach (var segment in fieldPath.Split('.'))
            {
                if (current.TryGetProperty(segment, out var property))
                {
                    current = property;
                }
                else
                {
                    return null;
                }
            }

            // Extract the actual value before the JsonDocument is disposed
            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.GetInt32(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => current.GetArrayLength(),
                JsonValueKind.Object => current.EnumerateObject().Count(),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Result of CLI execution
/// </summary>
public record CliExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError
)
{
    /// <summary>
    /// Get combined output (stdout + stderr)
    /// </summary>
    public string CombinedOutput =>
        string.IsNullOrEmpty(StandardError)
            ? StandardOutput
            : $"{StandardOutput}\n{StandardError}";

    /// <summary>
    /// Check if output contains text (case-insensitive)
    /// </summary>
    public bool OutputContains(string text) =>
        CombinedOutput.Contains(text, StringComparison.OrdinalIgnoreCase);
}
