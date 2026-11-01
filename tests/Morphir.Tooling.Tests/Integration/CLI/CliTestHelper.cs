using System.Diagnostics;
using System.Reflection;
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
            Arguments = $"run --project \"{_cliProjectPath}\" --configuration {typeof(CliTestHelper).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration} --no-build -- {arguments}",
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
            if (e.Data != null)
                stdoutBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
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
    /// Find repository root by looking for a .git directory or worktree file
    /// </summary>
    private static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root (.git directory or file)");
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
