using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Wolverine;

namespace Morphir.Tooling;

public static class Program
{
    public static IHost CreateToolingHost(bool logToStdErr = false)
    {
        var builder = Host.CreateApplicationBuilder();

        // CRITICAL: Clear default logging providers FIRST to prevent stdout contamination
        // The default Console logger writes to stdout, which breaks JSON output and scriptability
        builder.Logging.ClearProviders();

        // Configure Serilog to write ALL logs to stderr only
        // This keeps stdout clean for command output (JSON, formatted results, etc.)
        if (logToStdErr)
        {
            // Write ALL log levels to stderr (not stdout)
            // This is critical for CLI tools: stdout = data, stderr = diagnostics
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(
                    standardErrorFromLevel: LogEventLevel.Verbose,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            // Add Serilog to the logging builder (not services)
            builder.Logging.AddSerilog(Log.Logger, dispose: true);
        }
        // If not logToStdErr, logging stays disabled (default providers cleared above)

        // Now configure Wolverine - it will use our Serilog configuration
        // This ensures WolverineFx messages also go to stderr, not stdout

        builder.Services.AddWolverine(opts =>
        {
            // Enable in-memory messaging (no external broker needed)
            opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
            opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

            // Auto-discover handlers in Features/ directory
            opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

            // Configure code generation for single-file executables
            // Set output path for pre-generated code
            // Use relative path from project root - when codegen runs, it will resolve relative to current directory
            opts.CodeGeneration.GeneratedCodeOutputPath = "src/Morphir.Tooling/Internal/Generated";

            // Use Auto mode: tries to locate pre-generated types from assembly,
            // but falls back to generating code dynamically and writes source to disk
            // This allows codegen write to work, and Static mode will be used when code exists
            opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
        });

        return builder.Build();
    }

    // Entry point for codegen command when Morphir.Tooling is built as executable
    // UseWolverine automatically adds Oakton command-line support
    public static async Task<int> Main(string[] args)
    {
        // UseWolverine adds Oakton integration automatically
        // The codegen command will be available via Oakton
        using var host = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                // Enable in-memory messaging (no external broker needed)
                opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
                opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

                // Auto-discover handlers in Features/ directory
                opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

                // Configure code generation for single-file executables
                opts.CodeGeneration.GeneratedCodeOutputPath = "Internal/Generated";

                // Use Auto mode: tries to locate pre-generated types from assembly,
                // but falls back to generating code dynamically and writes source to disk
                opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
            })
            .StartAsync();

        // UseWolverine should have registered Oakton commands
        // Return success - Oakton integration handles command execution
        return 0;
    }
}
