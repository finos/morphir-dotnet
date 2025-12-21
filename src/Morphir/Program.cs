using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Foundatio.Mediator;

namespace Morphir;

using Extism.Sdk;

// JSON source generator context for AOT compilation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Tooling.Features.VerifyIR.VerifyIRResult))]
internal partial class MorphirJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Main entry point for the Morphir CLI tool.
/// Command implementations are organized in separate files using partial classes.
/// </summary>
public static partial class Program
{
    /// <summary>
    /// Main entry point for the CLI application.
    /// </summary>
    public static int Main(string[] args)
    {
        // FIRST: Redirect Console.Out to stderr before ANY code runs
        // This catches direct Console.WriteLine from extension scanning
        Tooling.Infrastructure.ConsoleOutputHelper.RedirectConsoleToStderr();

        // Set console encoding to UTF-8 to support Unicode characters (✓, ✗)
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Handle --version flag explicitly to output only the version string
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout(VersionInfo.Version);
            return 0;
        }

        var rootCommand = CreateRootCommand();
        return rootCommand.Parse(args).Invoke();
    }

    /// <summary>
    /// Creates the root command with all subcommands registered.
    /// </summary>
    private static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Morphir command line");

        // Register all subcommands
        rootCommand.Subcommands.Add(CreateInfoCommand());
        rootCommand.Subcommands.Add(CreateRunCommand());
        rootCommand.Subcommands.Add(CreateIrCommand());
        rootCommand.Subcommands.Add(CreateServerCommand());

        return rootCommand;
    }

    /// <summary>
    /// Creates the 'info' command for workspace/project information.
    /// </summary>
    private static Command CreateInfoCommand()
    {
        var infoCommand = new Command("info", "Get information about the workspace/project");
        infoCommand.SetAction(parseResult =>
        {
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("Morphir command line info:");
        });
        return infoCommand;
    }

    /// <summary>
    /// Creates the 'run' command for running the Morphir compiler with WASM plugins.
    /// </summary>
    private static Command CreateRunCommand()
    {
        var runCommand = new Command("run", "Run the Morphir compiler");

        var currentDirectoryOption = new Option<FileInfo>("-C")
        {
            Description = "Override the current directory",
            Required = false
        };
        runCommand.Options.Add(currentDirectoryOption);

        var wasmPluginPathArgument = new Argument<FileInfo>("wasm-plugin-path")
        {
            Description = "Path to the Wasm plugin",
            Arity = ArgumentArity.ExactlyOne,
        };
        runCommand.Arguments.Add(wasmPluginPathArgument);

        runCommand.SetAction(parseResult =>
        {
            var currentDirOverride = parseResult.GetValue(currentDirectoryOption);
            if (currentDirOverride != null)
            {
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Current directory override: {currentDirOverride.FullName}");
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Current directory override exists: {currentDirOverride.Exists}");
            }

            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("Morphir command line run:");
            var wasmPluginPath = parseResult.GetValue(wasmPluginPathArgument);
            if (wasmPluginPath == null)
            {
                throw new InvalidOperationException("Wasm plugin path is required");
            }
            RunWasmPlugin(currentDirOverride ?? new FileInfo(Directory.GetCurrentDirectory()),
                wasmPluginPath);
        });

        return runCommand;
    }

    /// <summary>
    /// Executes a WASM plugin for Morphir compilation.
    /// </summary>
    private static void RunWasmPlugin(FileInfo currentDirectory, FileInfo wasmPluginPath)
    {
        // Placeholder for the actual implementation of the Wasm plugin
        Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Running Wasm plugin in directory: {currentDirectory.FullName}");
        Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Using Wasm plugin at path: {wasmPluginPath.FullName}");
        var manifest = new Manifest(new UrlWasmSource("https://github.com/extism/plugins/releases/latest/download/count_vowels.wasm"));
        using var plugin = new Plugin(manifest, new HostFunction[] { }, withWasi: true);
        var output = plugin.Call("count_vowels", "Hello, World!");
        Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout(output);
    }
}
