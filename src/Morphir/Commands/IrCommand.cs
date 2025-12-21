using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Foundatio.Mediator;

namespace Morphir;

public static partial class Program
{
    /// <summary>
    /// Creates the 'ir' command with subcommands for IR utilities.
    /// </summary>
    /// <returns>Configured IR command</returns>
    private static Command CreateIrCommand()
    {
        var irCommand = new Command("ir", "Morphir IR utilities");

        // Verify subcommand
        var verifyCommand = CreateIrVerifyCommand();
        irCommand.Subcommands.Add(verifyCommand);

        return irCommand;
    }

    /// <summary>
    /// Creates the 'ir verify' subcommand for schema validation.
    /// </summary>
    /// <returns>Configured verify command</returns>
    private static Command CreateIrVerifyCommand()
    {
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

            // Create Foundatio mediator host
            // Logging is always redirected to stderr to keep stdout clean for command output
            using var host = Tooling.Program.CreateToolingHost();
            await host.StartAsync();

            var mediator = host.Services.GetRequiredService<IMediator>();

            // Create and send command
            var command = new Tooling.Features.VerifyIR.VerifyIR(
                FilePath: filePath.FullName,
                SchemaVersion: schemaVersion,
                JsonOutput: jsonOutput,
                Quiet: quiet
            );

            // Execute command via mediator
            var result = await mediator.InvokeAsync<Tooling.Features.VerifyIR.VerifyIRResult>(command);

            // Format output
            FormatVerifyOutput(result, jsonOutput, quiet);

            await host.StopAsync();

            return result.IsValid ? 0 : 1;
        });

        return verifyCommand;
    }

    /// <summary>
    /// Formats the output for the IR verify command.
    /// </summary>
    private static void FormatVerifyOutput(
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
}
