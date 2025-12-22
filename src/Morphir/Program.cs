using System.Text.Json.Serialization;
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
    /// <summary>
    /// Main entry point for the CLI application.
    /// </summary>
    public static int Main(string[] args)
    {
        // Handle --version flag explicitly to output only the version string
        // This must be done before creating the builder to avoid console redirection
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            // Console redirection happens in MorphirCli.CreateBuilder, so we need to do it manually here
            Tooling.Infrastructure.ConsoleOutputHelper.RedirectConsoleToStderr();
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout(VersionInfo.Version);
            return 0;
        }

        // Create and configure the CLI application using the builder pattern
        var app = MorphirCli
            .CreateBuilder(args, "Morphir command line")
            .AddScenario<IrToolingScenario>()
            .AddScenario<WasmPluginScenario>()
            .AddCommand(CreateInfoCommand())
            .Build();

        return app.Run();
    }

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
