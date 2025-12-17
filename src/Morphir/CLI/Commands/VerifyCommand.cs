using JasperFx.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Morphir.CLI.Commands;

/// <summary>
/// Input model for IR verify command
/// </summary>
public class VerifyInput
{
    [Description("Path to the Morphir IR JSON file to verify")]
    public string FilePath { get; set; } = string.Empty;

    [Description("Schema version to validate against (1, 2, or 3). Auto-detected if not specified.")]
    [FlagAlias("schema-version")]
    public int? SchemaVersion { get; set; }

    [Description("Output results in JSON format")]
    [FlagAlias("json")]
    public bool JsonOutput { get; set; }

    [Description("Suppress output, return only exit code (0 = valid, 1 = invalid)")]
    [FlagAlias("quiet")]
    public bool Quiet { get; set; }
}

/// <summary>
/// Command to verify Morphir IR JSON against schema
/// </summary>
[Description("Verify Morphir IR JSON against schema")]
public class VerifyCommand : JasperFxAsyncCommand<VerifyInput>
{
    public override async Task<bool> Execute(VerifyInput input)
    {
        // Validate required input
        if (string.IsNullOrEmpty(input.FilePath))
        {
            Console.Error.WriteLine("Error: File path is required");
            return false;
        }

        // Create WolverineFx host
        // Always redirect logs to stderr to keep stdout clean for command output
        using var host = Tooling.Program.CreateToolingHost(logToStdErr: true);
        await host.StartAsync();

        var messageBus = host.Services.GetRequiredService<IMessageBus>();

        // Create and send command to Wolverine handler
        var command = new Tooling.Features.VerifyIR.VerifyIR(
            FilePath: input.FilePath,
            SchemaVersion: input.SchemaVersion,
            JsonOutput: input.JsonOutput,
            Quiet: input.Quiet
        );

        // Execute command via message bus
        var result = await messageBus.InvokeAsync<Tooling.Features.VerifyIR.VerifyIRResult>(command);

        // Format output
        FormatOutput(result, input.JsonOutput, input.Quiet);

        await host.StopAsync();

        return result.IsValid;
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
            var json = System.Text.Json.JsonSerializer.Serialize(
                result,
                MorphirJsonContext.Default.VerifyIRResult);
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
}
