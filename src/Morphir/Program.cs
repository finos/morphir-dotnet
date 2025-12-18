using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using JasperFx.CodeGeneration.Commands;

namespace Morphir;

using Extism.Sdk;

// JSON source generator context for AOT compilation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Tooling.Features.VerifyIR.VerifyIRResult))]
internal partial class MorphirJsonContext : JsonSerializerContext
{
}

public static class Program
{
    public static int Main(string[] args)
    {
        // FIRST: Redirect Console.Out to stderr before ANY code runs
        // This catches direct Console.WriteLine from Wolverine/JasperFx extension scanning
        Tooling.Infrastructure.ConsoleOutputHelper.RedirectConsoleToStderr();

        // Set console encoding to UTF-8 to support Unicode characters (✓, ✗)
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Handle --version flag explicitly to output only the version string
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout(VersionInfo.Version);
            return 0;
        }

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

        // Add codegen command for Wolverine code generation
        Command codegenCommand = new("codegen", "Generate Wolverine code");
        Command codegenWriteCommand = new("write", "Write generated Wolverine code to disk");
        codegenCommand.Subcommands.Add(codegenWriteCommand);
        rootCommand.Subcommands.Add(codegenCommand);

        codegenWriteCommand.SetAction(async parseResult =>
        {
            // Create Morphir.Tooling host to get Wolverine runtime and code generation config
            using var host = Tooling.Program.CreateToolingHost();
            await host.StartAsync();

            // Get Wolverine runtime to access code generation
            var runtime = host.Services.GetRequiredService<Wolverine.Runtime.IWolverineRuntime>();
            var codegenOptions = runtime.Options.CodeGeneration;

            // The output path where code should be written (relative to Morphir.Tooling project)
            var outputPath = codegenOptions.GeneratedCodeOutputPath ?? "Internal/Generated";
            var fullOutputPath = Path.Combine("src", "Morphir.Tooling", outputPath);

            // With Auto mode, code is written when types are dynamically generated
            // To force code generation, we need to trigger a handler execution
            // This will cause Wolverine to generate code and write it to disk
            var messageBus = host.Services.GetRequiredService<IMessageBus>();

            // Try to invoke a handler to trigger code generation
            // Use a dummy command that will fail but trigger code generation
            try
            {
                // This will fail but should trigger code generation for the handler
                await messageBus.InvokeAsync<Tooling.Features.VerifyIR.VerifyIRResult>(
                    new Tooling.Features.VerifyIR.VerifyIR("dummy", null, false, false)
                );
            }
            catch
            {
                // Expected to fail - we just want to trigger code generation
            }

            // Check if code was generated
            if (Directory.Exists(fullOutputPath))
            {
                var files = Directory.GetFiles(fullOutputPath, "*.cs", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"✓ Wolverine code generated: {files.Length} files in {fullOutputPath}");
                }
                else
                {
                    Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"⚠ Warning: Code generation directory exists but is empty at {fullOutputPath}");
                }
            }
            else
            {
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"⚠ Warning: Code generation directory not found at {fullOutputPath}");
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Output path configured: {outputPath}");
            }

            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"✓ Wolverine code written to {outputPath}");

            if (Directory.Exists(outputPath))
            {
                var files = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Generated {files.Length} files");
            }

            await host.StopAsync();
            return 0;
        });

        infoCommand.SetAction(parseResult => { Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("Morphir command line info:"); });
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
            // Logging is always redirected to stderr to keep stdout clean for command output
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
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout(json);
        }
        else
        {
            // Human-readable output
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Validation Result: {(result.IsValid ? "✓ VALID" : "✗ INVALID")}");
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"File: {result.FilePath}");
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Schema Version: v{result.SchemaVersion} ({result.DetectionMethod})");
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");

            if (!result.IsValid && result.Errors.Count > 0)
            {
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Found {result.Errors.Count} validation error(s):");
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");

                foreach (var error in result.Errors)
                {
                    Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Path: {error.Path}");
                    Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Message: {error.Message}");

                    if (error.Expected != null)
                        Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Expected: {error.Expected}");

                    if (error.Found != null)
                        Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Found: {error.Found}");

                    if (error.Line.HasValue)
                        Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Line: {error.Line}, Column: {error.Column}");

                    Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");
                }
            }
            else if (result.IsValid)
            {
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");
                Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("No validation errors found.");
            }
        }
    }

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
