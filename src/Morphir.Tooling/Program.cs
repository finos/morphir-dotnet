using System.Diagnostics.CodeAnalysis;
using System.IO;
using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Wolverine;

namespace Morphir.Tooling;

public static partial class Program
{
    public static IHost CreateToolingHost()
    {
        // FIRST: Redirect Console.Out to stderr before ANY code runs
        // This catches direct Console.WriteLine from Wolverine/JasperFx
        Infrastructure.ConsoleOutputHelper.RedirectConsoleToStderr();

        var builder = Host.CreateApplicationBuilder();

        // CRITICAL: Clear default logging providers FIRST to prevent stdout contamination
        // The default Console logger writes to stdout, which breaks JSON output and scriptability
        builder.Logging.ClearProviders();

        // Configure Serilog to write ALL logs to stderr only
        // This keeps stdout clean for command output (JSON, formatted results, etc.)
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

        // Now configure Wolverine - it will use our Serilog configuration
        // This ensures WolverineFx messages also go to stderr, not stdout

        builder.Services.AddWolverine(opts =>
        {
            // Suppress Wolverine message execution logging to prevent stdout contamination
            // These logs are written at Information level by default and can leak to stdout
            opts.Policies.MessageExecutionLogLevel(LogLevel.None);
            opts.Policies.MessageSuccessLogLevel(LogLevel.None);

            // Enable in-memory messaging (no external broker needed)
            opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
            opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

            // Auto-discover handlers in Features/ directory
            opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

            // Configure code generation for single-file executables
            // Set output path for pre-generated code
            // Use relative path from project root - when codegen runs, it will resolve relative to current directory
            opts.CodeGeneration.GeneratedCodeOutputPath = "src/Morphir.Tooling/Internal/Generated";

            // Use Auto mode by default - will use pre-generated code if available (generated at build time via MSBuild target when EnableWolverineCodeGeneration is true)
            // For Release builds with EnableWolverineCodeGeneration=true, ConfigureWolverineCodeGeneration sets Static mode
            // Static mode is required for trimmed executables to prevent fallback to dynamic generation
#if ENABLE_WOLVERINE_CODE_GENERATION
            ConfigureWolverineCodeGenerationWithSuppression(opts);
#endif
        });

        return builder.Build();
    }

    // Entry point for codegen command when Morphir.Tooling is built as executable
    // Wolverine 5.8.0 uses JasperFX CommandLine integration
    public static async Task<int> Main(string[] args)
    {
        // FIRST: Redirect Console.Out to stderr before ANY code runs
        // This catches direct Console.WriteLine from Wolverine/JasperFx
        Infrastructure.ConsoleOutputHelper.RedirectConsoleToStderr();

        // Configure Serilog to write ALL logs to stderr only BEFORE creating the host
        // This ensures no default console logging writes to stdout
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // CRITICAL: Clear default logging providers FIRST to prevent stdout contamination
                logging.ClearProviders();
                // Add Serilog - it's already configured to write to stderr
                logging.AddSerilog(Log.Logger, dispose: true);
            })
            .UseWolverine(opts =>
            {
                // Suppress Wolverine message execution logging to prevent stdout contamination
                // These logs are written at Information level by default and can leak to stdout
                opts.Policies.MessageExecutionLogLevel(LogLevel.None);
                opts.Policies.MessageSuccessLogLevel(LogLevel.None);

                // Enable in-memory messaging (no external broker needed)
                opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
                opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

                // Auto-discover handlers in Features/ directory
                opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

                // Configure code generation for single-file executables
                opts.CodeGeneration.GeneratedCodeOutputPath = "Internal/Generated";

                // Use Auto mode by default - will use pre-generated code if available (generated at build time via MSBuild target when EnableWolverineCodeGeneration is true)
                // For Release builds with EnableWolverineCodeGeneration=true, ConfigureWolverineCodeGeneration sets Static mode
                // Static mode is required for trimmed executables to prevent fallback to dynamic generation
#if ENABLE_WOLVERINE_CODE_GENERATION
                ConfigureWolverineCodeGeneration(opts);
#endif
            })
            .StartAsync();

        // Use JasperFX CommandLine integration (built into Wolverine 5.8.0)
        return await host.RunJasperFxCommands(args);
    }

#if ENABLE_WOLVERINE_CODE_GENERATION
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "ConfigureWolverineCodeGeneration uses reflection but only runs when pre-generated code exists")]
    private static void ConfigureWolverineCodeGenerationWithSuppression(Wolverine.WolverineOptions opts)
    {
        ConfigureWolverineCodeGeneration(opts);
    }
#endif
}
