using Extism.Sdk;
using JasperFx.CommandLine;

namespace Morphir.CLI.Commands;

/// <summary>
/// Input model for run command
/// </summary>
public class RunInput
{
    [Description("Override the current directory")]
    [FlagAlias("C")]
    public string? CurrentDirectory { get; set; }

    [Description("Path to the Wasm plugin")]
    public string WasmPluginPath { get; set; } = string.Empty;
}

/// <summary>
/// Command to run the Morphir compiler with Wasm plugin
/// </summary>
[Description("Run the Morphir compiler")]
public class RunCommand : JasperFxCommand<RunInput>
{
    public override bool Execute(RunInput input)
    {
        // Validate required input
        if (string.IsNullOrEmpty(input.WasmPluginPath))
        {
            Console.Error.WriteLine("Error: Wasm plugin path is required");
            return false;
        }

        // Determine current directory
        var currentDir = !string.IsNullOrEmpty(input.CurrentDirectory)
            ? new FileInfo(input.CurrentDirectory)
            : new FileInfo(Directory.GetCurrentDirectory());

        if (currentDir.Exists)
        {
            Console.WriteLine($"Current directory override: {currentDir.FullName}");
            Console.WriteLine($"Current directory override exists: {currentDir.Exists}");
        }

        Console.WriteLine("Morphir command line run:");

        var wasmPluginPath = new FileInfo(input.WasmPluginPath);
        RunWasmPlugin(currentDir, wasmPluginPath);

        return true;
    }

    private static void RunWasmPlugin(FileInfo currentDirectory, FileInfo wasmPluginPath)
    {
        // Placeholder for the actual implementation of the Wasm plugin
        Console.WriteLine($"Running Wasm plugin in directory: {currentDirectory.FullName}");
        Console.WriteLine($"Using Wasm plugin at path: {wasmPluginPath.FullName}");

        var manifest = new Manifest(
            new UrlWasmSource("https://github.com/extism/plugins/releases/latest/download/count_vowels.wasm"));
        using var plugin = new Plugin(manifest, new HostFunction[] { }, withWasi: true);
        var output = plugin.Call("count_vowels", "Hello, World!");
        Console.WriteLine(output);
    }
}
