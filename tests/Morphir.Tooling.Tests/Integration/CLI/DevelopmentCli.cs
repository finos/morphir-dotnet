using Morphir.TestSupport.Drivers;

namespace Morphir.Tooling.Tests.Integration.CLI;

/// <summary>
/// Runs the native host of the development build through the CLI driver, for a step class that gives the product
/// its own folder and its own environment (the configuration and migration features). Those features test discovery
/// and containment, so the product gets exactly the Morphir variables of the scenario: the helper removes the
/// isolation variables that a workspace adds.
/// </summary>
internal static class DevelopmentCli
{
    /// <summary>A published CLI to run instead of the development build.</summary>
    private const string ExecutableVariable = "MORPHIR_CONFIGURATION_CLI";

    private static readonly Lazy<CliArtifacts> DevelopmentBuild =
        new(() => CliArtifacts.OfDevelopmentBuild(typeof(DevelopmentCli).Assembly));

    public static async Task<CommandResult> RunAsync(
        IReadOnlyList<string> arguments, string workingDirectory, IEnumerable<KeyValuePair<string, string>> environment)
    {
        // The workspace gives the process a home and machine locations of its own. The scenario then sets its own.
        await using var workspace = ScenarioWorkspace.Create(workingDirectory);
        var variables = new Dictionary<string, string?>
        {
            ["MORPHIR_HOME"] = null,
            ["MORPHIR_DISCOVERY_BOUNDARY"] = null,
            ["MORPHIR_ISOLATED"] = null,
        };
        foreach (var (name, value) in environment) variables[name] = value;

        var driver = Environment.GetEnvironmentVariable(ExecutableVariable) is { Length: > 0 } executable
            ? new CliDriver(new TestTarget.Executable(executable, MorphirHost.Native), MorphirHost.Native, executable, workspace)
            : new CliDriver(new TestTarget.DevelopmentBuild(), MorphirHost.Native, DevelopmentBuild.Value.PathOf(MorphirHost.Native), workspace);
        return await driver.RunAsync(arguments, new CliRunOptions
        {
            Timeout = TimeSpan.FromSeconds(60),
            WorkingDirectory = workingDirectory,
            Environment = variables,
        });
    }
}
