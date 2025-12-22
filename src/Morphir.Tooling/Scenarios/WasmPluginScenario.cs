using System.CommandLine;
using Extism.Sdk;

namespace Morphir.Tooling.Scenarios;

/// <summary>
/// Scenario for WASM plugin execution commands.
/// Provides the 'run' command for executing Morphir compiler with WASM plugins.
/// </summary>
public class WasmPluginScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder)
    {
        builder.AddCommand(CreateRunCommand());

        // Register WASM-specific services (if any needed in the future)
        builder.ConfigureServices(services =>
        {
            // Future: Add WASM plugin-specific services here
            // services.AddSingleton<IWasmPluginLoader>();
            // services.AddSingleton<IPluginCache>();
        });
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
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Current directory override: {currentDirOverride.FullName}");
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Current directory override exists: {currentDirOverride.Exists}");
            }

            Infrastructure.ConsoleOutputHelper.WriteLineToStdout("Morphir command line run:");
            var wasmPluginPath = parseResult.GetValue(wasmPluginPathArgument);
            if (wasmPluginPath == null)
            {
                throw new InvalidOperationException("Wasm plugin path is required");
            }

            RunWasmPlugin(
                currentDirOverride ?? new FileInfo(Directory.GetCurrentDirectory()),
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
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Running Wasm plugin in directory: {currentDirectory.FullName}");
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Using Wasm plugin at path: {wasmPluginPath.FullName}");

        var manifest = new Manifest(new UrlWasmSource("https://github.com/extism/plugins/releases/latest/download/count_vowels.wasm"));
        using var plugin = new Plugin(manifest, new HostFunction[] { }, withWasi: true);
        var output = plugin.Call("count_vowels", "Hello, World!");
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout(output);
    }
}
