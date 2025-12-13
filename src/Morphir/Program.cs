using System.CommandLine;

namespace Morphir;

using Extism.Sdk;

internal static class Program
{
    private static int Main(string[] args)
    {
        Option<FileInfo> currentDirectoryOption = new("-C")
        {
            Description = "Override the current directory",
            Required = false
        };
        Argument<FileInfo> wasmPluginPathArgument = new("wasm-plugin-path")
        {
            Description = "Path to the Wasm plugin",
            Arity = ArgumentArity.ExactlyOne,
        };

        var rootCommand = new RootCommand("Morphir command line");
        Command infoCommand = new("info", "Get information about the workspace/project");
        rootCommand.Subcommands.Add(infoCommand);
        Command runCommand = new("run", "Run the Morphir compiler");
        runCommand.Options.Add(currentDirectoryOption);
        runCommand.Arguments.Add(wasmPluginPathArgument);

        rootCommand.Subcommands.Add(runCommand);

        infoCommand.SetAction(parseResult => { Console.WriteLine("Morphir command line info:"); });
        runCommand.SetAction(parseResult =>
        {
            var currentDirOverride = parseResult.GetValue(currentDirectoryOption);
            if (currentDirOverride != null)
            {
                Console.WriteLine($"Current directory override: {currentDirOverride.FullName}");
                Console.WriteLine($"Current directory override exists: {currentDirOverride.Exists}");
            }

            Console.WriteLine("Morphir command line run:");
            var wasmPluginPath = parseResult.GetValue(wasmPluginPathArgument);
            if (wasmPluginPath == null)
            {
                throw new InvalidOperationException("Wasm plugin path is required");
            }
            RunWasmPlugin(currentDirOverride ?? new FileInfo(Directory.GetCurrentDirectory()),
                wasmPluginPath);
        });
        return rootCommand.Parse(args).Invoke();
    }

    private static void RunWasmPlugin(FileInfo currentDirectory, FileInfo wasmPluginPath)
    {
        // Placeholder for the actual implementation of the Wasm plugin
        Console.WriteLine($"Running Wasm plugin in directory: {currentDirectory.FullName}");
        Console.WriteLine($"Using Wasm plugin at path: {wasmPluginPath.FullName}");
        var manifest = new Manifest(new UrlWasmSource("https://github.com/extism/plugins/releases/latest/download/count_vowels.wasm"));
        using var plugin = new Plugin(manifest, new HostFunction[] { }, withWasi: true);
        var output = plugin.Call("count_vowels", "Hello, World!");
        Console.WriteLine(output);


    }
}
