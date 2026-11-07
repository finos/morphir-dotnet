using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Tooling;
using Morphir.Tooling.Scenarios;

namespace Morphir;

// JSON source generator context for AOT compilation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Tooling.Features.VerifyIR.VerifyIRResult))]
internal partial class MorphirJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Main entry point for the Morphir CLI tool.
/// Uses MorphirCli builder for centralized configuration.
/// </summary>
public static class Program
{
    /// <summary>The Native AOT host <c>morphir</c>, with the version of this build.</summary>
    internal static HostIdentity Host => new(MorphirHost.Native, ReleaseVersion.Create(VersionInfo.Version));

    /// <summary>
    /// Main entry point for the CLI application.
    /// </summary>
    public static int Main(string[] args) => Run(args, Host);

    /// <summary>
    /// Runs the CLI as the given host. <c>dotnet-morphir</c> calls this with its own host identity.
    /// </summary>
    public static int Run(string[] args, HostIdentity host)
    {
        // Handle --version flag explicitly to output only the version string
        // This must be done before creating the builder to avoid console redirection
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            // Console redirection happens in MorphirCli.CreateBuilder, so we need to do it manually here
            Tooling.Infrastructure.ConsoleOutputHelper.RedirectConsoleToStderr();
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout(host.Version.ToString());
            return 0;
        }

        return CreateApp(args, host).Run();
    }

    /// <summary>
    /// The shared composition of both hosts. The host identity is registered as a singleton.
    /// </summary>
    public static MorphirCliApp CreateApp(string[] args, HostIdentity host) =>
        MorphirCli
            .CreateBuilder(args, "Morphir command line")
            .ConfigureServices(services => services.AddSingleton(host))
            .AddScenario<IrToolingScenario>()
            .AddScenario<GenerationScenario>()
            .AddScenario<ManagementScenario>()
            .AddScenario<ConfigurationScenario>()
            .AddScenario<ProjectScenario>()
            .AddScenario<WorkspaceScenario>()
            .AddCommand(CreateInfoCommand())
            .Build();

    /// <summary>
    /// Creates the 'info' command for workspace/project information.
    /// </summary>
    private static System.CommandLine.Command CreateInfoCommand()
    {
        var infoCommand = new System.CommandLine.Command("info", "Get information about the workspace/project");
        infoCommand.SetAction(parseResult =>
        {
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("Morphir command line info:");
        });
        return infoCommand;
    }
}
