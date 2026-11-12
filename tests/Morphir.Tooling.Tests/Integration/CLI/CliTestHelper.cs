using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Integration.CLI;

/// <summary>The CLI host that a test runs: the AOT-oriented <c>morphir</c> or the JIT <c>dotnet-morphir</c> tool.</summary>
public enum CliHost
{
    Morphir,
    DotnetTool
}

/// <summary>
/// Helper class for running CLI commands in integration tests
/// </summary>
public class CliTestHelper
{
    private readonly string _cliProjectPath;
    private readonly string _testDataDirectory;
    private readonly string? _executablePath;
    private readonly ProcessGate? _gate;

    /// <param name="executablePath">A published CLI to run instead of <c>dotnet run</c> on the CLI project.</param>
    /// <param name="gate">The process gate for each command. The default is <see cref="ProcessGate.Shared"/>.</param>
    public CliTestHelper(string? executablePath = null, ProcessGate? gate = null) : this(executablePath, CliHost.Morphir, gate) { }

    /// <summary>Runs <paramref name="host"/> from source, or <paramref name="executablePath"/> when one is given.</summary>
    /// <param name="gate">The process gate for each command. The default is <see cref="ProcessGate.Shared"/>.</param>
    public CliTestHelper(string? executablePath, CliHost host, ProcessGate? gate = null)
    {
        _gate = gate;
        if (executablePath is not null)
        {
            _executablePath = Path.GetFullPath(executablePath);
            if (!File.Exists(_executablePath)) throw new FileNotFoundException("Published CLI executable not found.", _executablePath);
        }
        // Navigate from test project to CLI project
        var currentDir = Directory.GetCurrentDirectory();
        var repoRoot = FindRepositoryRoot(currentDir);

        _cliProjectPath = host switch
        {
            CliHost.Morphir => Path.Combine(repoRoot, "src", "Morphir", "Morphir.csproj"),
            CliHost.DotnetTool => Path.Combine(repoRoot, "src", "Morphir.Tool", "Morphir.Tool.csproj"),
            _ => throw new ArgumentOutOfRangeException(nameof(host), host, "Unknown CLI host.")
        };
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

    /// <summary>Applied when a caller gives no timeout. It starts once a process slot is held.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Executes a CLI command and returns the result.</summary>
    /// <param name="arguments">The command line after the executable.</param>
    /// <param name="ct">
    /// Cancels the command, also while it waits for a process slot. Do not give a time budget with it: a
    /// <see cref="CancellationTokenSource"/> made with a delay starts to count before the slot is held, so a
    /// long queue for the gate uses the budget. Use <paramref name="timeout"/> for a budget.
    /// </param>
    /// <param name="timeout">The time budget for the command. It starts once a process slot is held.</param>
    public async Task<CliExecutionResult> ExecuteCommandAsync(string arguments, CancellationToken ct = default, TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        // Replace <test-data> placeholder with actual path
        arguments = arguments.Replace("<test-data>", _testDataDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath ?? "dotnet",
            Arguments = _executablePath is null
                ? $"run --project \"{_cliProjectPath}\" --configuration {typeof(CliTestHelper).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration} --no-build -- {arguments}"
                : arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Each invocation runs contained: a fake home and machine locations, inherited MORPHIR_* replaced by the
        // sandbox's MORPHIR_HOME, discovery boundary and isolation, and the sandbox work area as its directory.
        await using var sandbox = TestSandbox.Create("morphir-cli-");
        startInfo.WorkingDirectory = sandbox.Work;
        sandbox.Apply(startInfo);
        if (environment is not null)
            foreach (var (name, value) in environment)
                startInfo.Environment[name] = value;

        // The shared runner gates concurrency, reads both streams, and kills and awaits the tree on timeout.
        var result = await ChildProcess.RunAsync(startInfo, timeout ?? DefaultTimeout, ct, _gate);
        return new CliExecutionResult(
            ExitCode: result.ExitCode,
            StandardOutput: result.StandardOutput.Trim(),
            StandardError: result.StandardError.Trim()
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
