using System.Text.Json;
using Morphir.TestSupport;
using Morphir.TestSupport.Drivers;

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
    private static readonly Lazy<CliArtifacts> DevelopmentBuild =
        new(() => CliArtifacts.OfDevelopmentBuild(typeof(CliTestHelper).Assembly));

    private readonly string _testDataDirectory;
    private readonly string? _executablePath;
    private readonly MorphirHost _host;
    private readonly ProcessGate? _gate;

    /// <param name="executablePath">A published CLI to run instead of the development build of the CLI project.</param>
    /// <param name="gate">The process gate for each command. The default is <see cref="ProcessGate.Shared"/>.</param>
    public CliTestHelper(string? executablePath = null, ProcessGate? gate = null) : this(executablePath, CliHost.Morphir, gate) { }

    /// <summary>Runs the development build of <paramref name="host"/>, or <paramref name="executablePath"/> when one is given.</summary>
    /// <param name="gate">The process gate for each command. The default is <see cref="ProcessGate.Shared"/>.</param>
    public CliTestHelper(string? executablePath, CliHost host, ProcessGate? gate = null)
    {
        _gate = gate;
        if (executablePath is not null)
        {
            _executablePath = Path.GetFullPath(executablePath);
            if (!File.Exists(_executablePath)) throw new FileNotFoundException("Published CLI executable not found.", _executablePath);
        }

        _host = host switch
        {
            CliHost.Morphir => MorphirHost.Native,
            CliHost.DotnetTool => MorphirHost.DotnetTool,
            _ => throw new ArgumentOutOfRangeException(nameof(host), host, "Unknown CLI host.")
        };

        // Navigate from test project to the test data
        var currentDir = Directory.GetCurrentDirectory();
        var repoRoot = FindRepositoryRoot(currentDir);
        _testDataDirectory = Path.Combine(repoRoot, "tests", "Morphir.Tooling.Tests", "TestData");

        if (!Directory.Exists(_testDataDirectory))
        {
            throw new InvalidOperationException($"Test data directory not found at: {_testDataDirectory}");
        }
    }

    /// <summary>
    /// The CLI of the target of the testing library (<c>MORPHIR_TEST_TARGET</c>, <c>MORPHIR_TEST_HOST</c>,
    /// <c>MORPHIR_TEST_EXECUTABLE</c>; see tests/Morphir.TestSupport/README.md) on <paramref name="host"/>: the
    /// development build of that host, or the executable at a path when the target is <c>executable</c> with that
    /// host. A target that does not have the host skips the test with that reason: no other form is used.
    /// </summary>
    public static CliTestHelper ForTarget(CliHost host = CliHost.Morphir)
    {
        var settings = TestRunSettings.Parse(Environment.GetEnvironmentVariable, File.Exists);
        var wanted = host == CliHost.DotnetTool ? MorphirHost.DotnetTool : MorphirHost.Native;
        switch (settings.Target)
        {
            case TestTarget.DevelopmentBuild:
                return new CliTestHelper(null, host);
            case TestTarget.Executable executable when executable.Host == wanted:
                return new CliTestHelper(executable.Path, host);
            default:
                Skip.Test(settings.Target.MissingHostReason(wanted));
                throw new InvalidOperationException("Skip.Test returns no value.");
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

        // Each invocation runs contained in its own workspace: a fake home and machine locations, inherited MORPHIR_*
        // replaced by the workspace's MORPHIR_HOME, discovery boundary and isolation, and the work area as its directory.
        await using var workspace = ScenarioWorkspace.Create(_testDataDirectory);

        // The CLI driver starts the built assembly of the host with dotnet (not dotnet run, which evaluates the
        // project for each start). The path comes from the build of this test project (Morphir.CliArtifacts.targets),
        // so a missing build is an error. The driver gates concurrency, reads both streams, and kills and awaits the
        // tree on timeout.
        var driver = _executablePath is null
            ? new CliDriver(new TestTarget.DevelopmentBuild(), _host, DevelopmentBuild.Value.PathOf(_host), workspace, _gate)
            : new CliDriver(new TestTarget.Executable(_executablePath, _host), _host, _executablePath, workspace, _gate);
        var result = await driver.RunCommandLineAsync(
            arguments, new CliRunOptions { Timeout = timeout ?? DefaultTimeout, Environment = environment }, ct);
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
