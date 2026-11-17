using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Foundatio.Mediator;
using Morphir.Tooling.Cli;
using Serilog;

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
            irCommand.Subcommands.Add(new IrVerifyCommand());

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
    /// <c>ir verify</c>: schema validation of a Morphir IR JSON file. The result goes to stdout as text, as the one
    /// <c>completed</c> record of <c>--json</c>, or as one JSON document with <c>--json=document</c>; <c>--quiet</c>
    /// writes nothing. A parse error is a result with exit code 2, in the JSON mode that the tokens ask for.
    /// </summary>
    private sealed class IrVerifyCommand : Command, IMorphirParseErrorHandler
    {
        /// <summary>The exit code of arguments that do not parse.</summary>
        private const int UsageExitCode = 2;

        private readonly Argument<FileInfo> _filePath = new("file-path") { Description = "Path to the Morphir IR JSON file to verify" };

        private readonly Option<int?> _schemaVersion = new("--schema-version")
        {
            Description = "Schema version to validate against (1, 2, or 3). Auto-detected if not specified."
        };

        private readonly Option<string> _json = JsonOutputOption.Create();

        private readonly Option<bool> _quiet = new("--quiet")
        {
            Description = "Suppress output, return only exit code (0 = valid, 1 = invalid)"
        };

        public IrVerifyCommand() : base("verify", "Verify Morphir IR JSON against schema")
        {
            Arguments.Add(_filePath);
            Options.Add(_schemaVersion);
            Options.Add(_json);
            Options.Add(_quiet);
            SetAction(async parseResult =>
            {
                var filePath = parseResult.GetValue(_filePath);
                var schemaVersion = parseResult.GetValue(_schemaVersion);
                var mode = JsonOutputOption.ModeOf(parseResult);
                var quiet = parseResult.GetValue(_quiet);

                if (filePath == null)
                {
                    Console.Error.WriteLine("Error: File path is required");
                    return 1;
                }

                // Create temporary host for this command
                // Note: In the future, we'll use the app-wide host from MorphirCliApp
                using var host = CreateCommandHost(quiet);
                await host.StartAsync();

                var mediator = host.Services.GetRequiredService<IMediator>();

                // Create and send command
                var command = new Features.VerifyIR.VerifyIR(
                    FilePath: filePath.FullName,
                    SchemaVersion: schemaVersion,
                    JsonOutput: mode != JsonOutputMode.None,
                    Quiet: quiet
                );

                // Execute command via mediator
                var result = await mediator.InvokeAsync<Features.VerifyIR.VerifyIRResult>(command);

                // Format output
                FormatVerifyOutput(result, mode, quiet);

                await host.StopAsync();

                return result.IsValid ? 0 : 1;
            });
        }

        /// <inheritdoc />
        public int HandleParseErrors(ParseResult result, IReadOnlyList<string> messages)
        {
            var mode = JsonOutputOption.ScanTokens([.. result.Tokens.Select(token => token.Value)]);
            if (mode == JsonOutputMode.None)
            {
                foreach (var message in messages) Console.Error.WriteLine($"Error: {message}");
                return UsageExitCode;
            }

            var usage = new Features.VerifyIR.VerifyIRResult(
                IsValid: false,
                SchemaVersion: "unknown",
                DetectionMethod: "none",
                FilePath: "",
                Errors: [.. messages.Select(message => new Features.VerifyIR.ValidationError(Path: "$", Message: message, Expected: "Valid arguments"))],
                Timestamp: DateTime.UtcNow);
            JsonReportOutput.Write(Infrastructure.ConsoleOutputHelper.Stdout, mode, usage, Features.VerifyIR.VerifyIRResult.JsonInfo);
            return UsageExitCode;
        }
    }

    /// <summary>
    /// Formats the output for the IR verify command.
    /// </summary>
    private static void FormatVerifyOutput(
        Features.VerifyIR.VerifyIRResult result,
        JsonOutputMode mode,
        bool quiet)
    {
        if (quiet)
        {
            // Quiet mode: no output, only exit code
            return;
        }

        if (mode != JsonOutputMode.None)
        {
            // Source-generated: the trimmed executable disables reflection-based serialization.
            JsonReportOutput.Write(Infrastructure.ConsoleOutputHelper.Stdout, mode, result, Features.VerifyIR.VerifyIRResult.JsonInfo);
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
    private static IHost CreateCommandHost(bool quiet)
    {
        var builder = Host.CreateApplicationBuilder();

        // Configure logging
        builder.Logging.ClearProviders();

        Serilog.Log.Logger = Infrastructure.CliLogging.Create(writeToStandardError: !quiet);

        builder.Logging.AddSerilog(Serilog.Log.Logger, dispose: true);

        // Configure services
        Infrastructure.MediatorRegistration.Add(builder.Services);
        builder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
        builder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

        return builder.Build();
    }
}
