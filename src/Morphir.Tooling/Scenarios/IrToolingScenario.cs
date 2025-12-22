using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Foundatio.Mediator;
using Serilog;
using Serilog.Events;

namespace Morphir.Tooling.Scenarios;

/// <summary>
/// Scenario for Morphir IR (Intermediate Representation) tooling commands.
/// Includes commands for IR verification, validation, and manipulation.
/// </summary>
public class IrToolingScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder)
    {
        builder.AddCommand(rootCommand =>
        {
            var irCommand = new Command("ir", "Morphir IR utilities");

            // Add verify subcommand
            var verifyCommand = CreateIrVerifyCommand();
            irCommand.Subcommands.Add(verifyCommand);

            rootCommand.Subcommands.Add(irCommand);
        });

        // Register IR-specific services (if any needed in the future)
        builder.ConfigureServices(services =>
        {
            // Future: Add IR-specific services here
            // services.AddSingleton<IrTypeChecker>();
            // services.AddSingleton<IrOptimizer>();
        });
    }

    /// <summary>
    /// Creates the 'ir verify' subcommand for schema validation.
    /// </summary>
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

            // Create temporary host for this command
            // Note: In the future, we'll use the app-wide host from MorphirCliApp
            using var host = CreateCommandHost();
            await host.StartAsync();

            var mediator = host.Services.GetRequiredService<IMediator>();

            // Create and send command
            var command = new Features.VerifyIR.VerifyIR(
                FilePath: filePath.FullName,
                SchemaVersion: schemaVersion,
                JsonOutput: jsonOutput,
                Quiet: quiet
            );

            // Execute command via mediator
            var result = await mediator.InvokeAsync<Features.VerifyIR.VerifyIRResult>(command);

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
        Features.VerifyIR.VerifyIRResult result,
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
            // JSON output - need to use source-generated serializer for AOT compatibility
            // Note: This will need to reference the MorphirJsonContext from Morphir project
            // For now, we'll use the standard serializer
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout(json);
        }
        else
        {
            // Human-readable output
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Validation Result: {(result.IsValid ? "✓ VALID" : "✗ INVALID")}");
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"File: {result.FilePath}");
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Schema Version: v{result.SchemaVersion} ({result.DetectionMethod})");
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");

            if (!result.IsValid && result.Errors.Count > 0)
            {
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Found {result.Errors.Count} validation error(s):");
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");

                foreach (var error in result.Errors)
                {
                    Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Path: {error.Path}");
                    Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Message: {error.Message}");

                    if (error.Expected != null)
                        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Expected: {error.Expected}");

                    if (error.Found != null)
                        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Found: {error.Found}");

                    if (error.Line.HasValue)
                        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Line: {error.Line}, Column: {error.Column}");

                    Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");
                }
            }
            else if (result.IsValid)
            {
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout("No validation errors found.");
            }
        }
    }

    /// <summary>
    /// Creates a temporary host for command execution.
    /// This is a temporary solution until we refactor to use app-wide host.
    /// </summary>
    private static IHost CreateCommandHost()
    {
        var builder = Host.CreateApplicationBuilder();

        // Configure logging
        builder.Logging.ClearProviders();

        Serilog.Log.Logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        builder.Logging.AddSerilog(Serilog.Log.Logger, dispose: true);

        // Configure services
        builder.Services.AddMediator();
        builder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
        builder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

        return builder.Build();
    }
}
