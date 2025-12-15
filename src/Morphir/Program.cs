using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Morphir;

using Extism.Sdk;

// JSON source generator context for AOT compilation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Tooling.Features.VerifyIR.VerifyIRResult))]
internal partial class MorphirJsonContext : JsonSerializerContext
{
}

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

        // Add IR command with verify subcommand
        var irCommand = CreateIrCommand();
        rootCommand.Subcommands.Add(irCommand);

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

    private static Command CreateIrCommand()
    {
        var irCommand = new Command("ir", "Morphir IR utilities");

        // Verify subcommand
        var verifyCommand = new Command("verify", "Verify Morphir IR JSON against schema");

        var filePathArgument = new Argument<FileInfo>("file-path")
        {
            Description = "Path to the Morphir IR JSON file to verify"
        };
        verifyCommand.Arguments.Add(filePathArgument);

        var schemaVersionOption = new Option<int?>("--schema-version")
        {
            Description = "Schema version to validate against (1, 2, or 3). Auto-detected if not specified."
        };
        verifyCommand.Options.Add(schemaVersionOption);

        var jsonOutputOption = new Option<bool>("--json")
        {
            Description = "Output results in JSON format"
        };
        verifyCommand.Options.Add(jsonOutputOption);

        var quietOption = new Option<bool>("--quiet")
        {
            Description = "Suppress output, return only exit code (0 = valid, 1 = invalid)"
        };
        verifyCommand.Options.Add(quietOption);

        verifyCommand.SetAction(async parseResult =>
        {
            var filePath = parseResult.GetValue(filePathArgument);
            var schemaVersion = parseResult.GetValue(schemaVersionOption);
            var jsonOutput = parseResult.GetValue(jsonOutputOption);
            var quiet = parseResult.GetValue(quietOption);

            if (filePath == null)
            {
                Console.Error.WriteLine("Error: File path is required");
                return 1;
            }

            // Create WolverineFx host
            using var host = Tooling.Program.CreateToolingHost();
            await host.StartAsync();

            var messageBus = host.Services.GetRequiredService<IMessageBus>();

            // Create and send command
            var command = new Tooling.Features.VerifyIR.VerifyIR(
                FilePath: filePath.FullName,
                SchemaVersion: schemaVersion,
                JsonOutput: jsonOutput,
                Quiet: quiet
            );

            // Execute command via message bus
            var result = await messageBus.InvokeAsync<Tooling.Features.VerifyIR.VerifyIRResult>(command);

            // Format output
            FormatOutput(result, jsonOutput, quiet);

            await host.StopAsync();

            return result.IsValid ? 0 : 1;
        });

        irCommand.Subcommands.Add(verifyCommand);
        return irCommand;
    }

    private static void FormatOutput(
        Tooling.Features.VerifyIR.VerifyIRResult result,
        bool jsonOutput,
        bool quiet)
    {
        if (quiet)
        {
            // Quiet mode: no output, only exit code
            return;
        }

        if (jsonOutput)
        {
            // JSON output using source-generated serializer for AOT compatibility
            var json = JsonSerializer.Serialize(result, MorphirJsonContext.Default.VerifyIRResult);
            Console.WriteLine(json);
        }
        else
        {
            // Human-readable output
            Console.WriteLine($"Validation Result: {(result.IsValid ? "✓ VALID" : "✗ INVALID")}");
            Console.WriteLine($"File: {result.FilePath}");
            Console.WriteLine($"Schema Version: v{result.SchemaVersion} ({result.DetectionMethod})");
            Console.WriteLine($"Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");

            if (!result.IsValid && result.Errors.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Found {result.Errors.Count} validation error(s):");
                Console.WriteLine();

                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  Path: {error.Path}");
                    Console.WriteLine($"  Message: {error.Message}");

                    if (error.Expected != null)
                        Console.WriteLine($"  Expected: {error.Expected}");

                    if (error.Found != null)
                        Console.WriteLine($"  Found: {error.Found}");

                    if (error.Line.HasValue)
                        Console.WriteLine($"  Line: {error.Line}, Column: {error.Column}");

                    Console.WriteLine();
                }
            }
            else if (result.IsValid)
            {
                Console.WriteLine();
                Console.WriteLine("No validation errors found.");
            }
        }
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
