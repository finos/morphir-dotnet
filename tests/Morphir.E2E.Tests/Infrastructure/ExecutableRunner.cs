using System.Diagnostics;
using System.Text;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// Executes Morphir CLI commands via process execution for E2E testing
/// </summary>
public class ExecutableRunner
{
    private readonly string _executablePath;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

    public ExecutableRunner(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            throw new ArgumentException("Executable path cannot be null or empty", nameof(executablePath));

        if (!File.Exists(executablePath))
            throw new FileNotFoundException($"Executable not found: {executablePath}");

        _executablePath = executablePath;
    }

    /// <summary>
    /// Execute a CLI command and return the result
    /// </summary>
    public async Task<ExecutableExecutionResult> ExecuteCommandAsync(
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = arguments,
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

        var actualTimeout = timeout ?? _defaultTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(actualTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore errors when killing process
            }
            throw new TimeoutException($"Command execution timed out after {actualTimeout.TotalSeconds} seconds");
        }

        return new ExecutableExecutionResult(
            ExitCode: process.ExitCode,
            StandardOutput: stdoutBuilder.ToString().Trim(),
            StandardError: stderrBuilder.ToString().Trim()
        );
    }

    /// <summary>
    /// Check if a line is an infrastructure log message that should be filtered
    /// </summary>
    internal static bool IsInfrastructureLogMessage(string line)
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

        // Filter Serilog formatted log messages: [HH:mm:ss INF] or [HH:mm:ss WRN] etc
        // These are infrastructure logs from Wolverine/hosting that we want to suppress
        // Pattern: [XX:XX:XX INF] or [XX:XX:XX WRN] or [XX:XX:XX DBG]
        if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\[\d{2}:\d{2}:\d{2}\s+(INF|WRN|DBG)\]"))
            return true;

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

/// <summary>
/// Result of executable execution
/// </summary>
public record ExecutableExecutionResult(
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

    /// <summary>
    /// Check if output contains any of the provided texts (case-insensitive)
    /// </summary>
    public bool OutputContainsAny(params string[] texts) =>
        texts.Any(text => OutputContains(text));
}

