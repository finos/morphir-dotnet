using Morphir.TestSupport.Drivers;

namespace Morphir.Tooling.Tests.Integration.CLI;

/// <summary>
/// Runs the native host of the target of the testing library through the CLI driver, for a step class that gives the product
/// its own folder and its own environment (the configuration and migration features). Those features test discovery
/// and containment, so the product gets exactly the Morphir variables of the scenario: the helper removes the
/// isolation variables that a workspace adds.
/// </summary>
internal static class DevelopmentCli
{
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

        // The target of the testing library: the development build, or an executable at a path with the native host.
        var target = TestRunSettings.Parse(Environment.GetEnvironmentVariable, File.Exists).Target;
        var driver = target switch
        {
            TestTarget.DevelopmentBuild => new CliDriver(target, MorphirHost.Native, DevelopmentBuild.Value.PathOf(MorphirHost.Native), workspace),
            TestTarget.Executable { Host: MorphirHost.Native } executable => new CliDriver(target, MorphirHost.Native, executable.Path, workspace),
            _ => throw new TestSessionException(target.MissingHostReason(MorphirHost.Native)),
        };
        return await driver.RunAsync(arguments, new CliRunOptions
        {
            Timeout = TimeSpan.FromSeconds(60),
            WorkingDirectory = workingDirectory,
            Environment = variables,
        });
    }
}
