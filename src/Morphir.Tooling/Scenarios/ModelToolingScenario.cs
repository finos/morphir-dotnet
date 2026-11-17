using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Foundatio.Mediator;
using Morphir.Tooling.Cli;
using Serilog;

namespace Morphir.Tooling.Scenarios;

/// <summary>
/// The <c>model</c> command group: the commands that act on a Morphir model, the IR together with its decorators,
/// metadata and later additions. It replaced the <c>ir</c> group, which has no alias: <c>morphir ir</c> is an unknown
/// command.
/// </summary>
public class ModelToolingScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder)
    {
        builder.AddCommand(rootCommand =>
        {
            var modelCommand = new Command("model", "Commands that act on a Morphir model");
            modelCommand.Subcommands.Add(new ModelVerifyCommand());
            rootCommand.Subcommands.Add(modelCommand);
        });
    }

    /// <summary>
    /// <c>model verify</c>: schema validation of a Morphir model, a single IR JSON file or the directory of a document
    /// tree, at IR versions 1 to 4. The result goes to stdout as text, as the one
    /// <c>completed</c> record of <c>--json</c>, or as one JSON document with <c>--json=document</c>. <c>--quiet</c>
    /// suppresses the text and the log; a JSON mode is still written, as it is the result that the caller asked for.
    /// The report states the exit code (<c>exitCode</c>). A parse error is a result with exit code 2, in the JSON
    /// mode that the tokens ask for.
    /// </summary>
    private sealed class ModelVerifyCommand : Command, IMorphirParseErrorHandler
    {
        /// <summary>The exit code of arguments that do not parse.</summary>
        private const int UsageExitCode = 2;

        private readonly Argument<FileInfo> _filePath = new("model-path")
        {
            Description = "Path to the Morphir model to verify: an IR JSON file, or the directory of a document tree"
        };

        private readonly Option<int?> _schemaVersion = CreateSchemaVersionOption();

        private readonly Option<string> _json = JsonOutputOption.Create();

        private readonly Option<bool> _quiet = new("--quiet")
        {
            Description = "Write no text and no log, only the exit code (0 = valid, 1 = invalid); a --json report is still written"
        };

        /// <summary>
        /// <c>--schema-version</c>: 1 to 4. Another number does not parse, so it is a usage error with exit code 2 and
        /// not a model that is not valid.
        /// </summary>
        private static Option<int?> CreateSchemaVersionOption()
        {
            var option = new Option<int?>("--schema-version")
            {
                Description = "Schema version to validate against (1, 2, 3 or 4). Detected from the file, or from the manifest of a tree, if not given."
            };
            option.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<int?>() is { } version and not (>= 1 and <= 4))
                    result.AddError($"Schema version must be 1, 2, 3 or 4, not {version}.");
            });
            return option;
        }

        public ModelVerifyCommand() : base("verify", "Verify a Morphir model against the schema of its IR version")
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
                var command = new Features.ModelVerify.VerifyModel(
                    FilePath: filePath.FullName,
                    SchemaVersion: schemaVersion,
                    JsonOutput: mode != JsonOutputMode.None,
                    Quiet: quiet
                );

                // Execute command via mediator
                var result = await mediator.InvokeAsync<Features.ModelVerify.VerifyModelResult>(command);

                // Format output
                FormatVerifyOutput(result, mode, quiet);

                await host.StopAsync();

                return result.ExitCode;
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

            var usage = new Features.ModelVerify.VerifyModelResult(
                IsValid: false,
                SchemaVersion: "unknown",
                DetectionMethod: "none",
                FilePath: "",
                Errors: [.. messages.Select(message => new Features.ModelVerify.ValidationError(Path: "$", Message: message, Expected: "Valid arguments"))],
                Timestamp: DateTime.UtcNow)
            {
                ExitCode = UsageExitCode,
            };
            JsonReportOutput.Write(Infrastructure.ConsoleOutputHelper.Stdout, mode, usage, Features.ModelVerify.VerifyModelResult.JsonInfo);
            return UsageExitCode;
        }
    }

    /// <summary>
    /// Writes the result of <c>model verify</c>. A JSON mode always writes its report, also with <c>--quiet</c>;
    /// <c>--quiet</c> alone writes nothing.
    /// </summary>
    private static void FormatVerifyOutput(
        Features.ModelVerify.VerifyModelResult result,
        JsonOutputMode mode,
        bool quiet)
    {
        if (mode != JsonOutputMode.None)
        {
            // Source-generated: the trimmed executable disables reflection-based serialization.
            JsonReportOutput.Write(Infrastructure.ConsoleOutputHelper.Stdout, mode, result, Features.ModelVerify.VerifyModelResult.JsonInfo);
        }
        else if (!quiet)
        {
            // Human-readable output
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Validation Result: {(result.IsValid ? "✓ VALID" : "✗ INVALID")}");
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"File: {result.FilePath}");
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Schema Version: v{result.SchemaVersion} ({result.DetectionMethod})");
            if (result.Layout == Features.ModelVerify.ModelLayouts.DocumentTree)
            {
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Layout: {result.Layout}");
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Files Checked: {result.FilesChecked}");
            }

            Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");

            if (!result.IsValid && result.Errors.Count > 0)
            {
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Found {result.Errors.Count} validation error(s):");
                Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");

                foreach (var error in result.Errors)
                {
                    if (error.File != null)
                        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  File: {error.File}");

                    if (error.Code != null)
                        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"  Code: {error.Code}");

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
