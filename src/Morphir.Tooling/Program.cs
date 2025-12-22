using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Foundatio.Mediator;

namespace Morphir.Tooling;

public static partial class Program
{
    /// <summary>
    /// Creates a tooling host with default services and logging configuration.
    /// </summary>
    /// <remarks>
    /// This method is obsolete. Use MorphirCli.CreateBuilder() instead for new code.
    /// This method is kept for backward compatibility.
    /// </remarks>
    [Obsolete("Use MorphirCli.CreateBuilder() instead. This method will be removed in a future version.")]
    public static IHost CreateToolingHost()
    {
        // FIRST: Redirect Console.Out to stderr before ANY code runs
        // This catches direct Console.WriteLine from extension scanning
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

        // Configure Foundatio.Mediator - uses source generators for handler discovery (trimming/AOT-friendly)
        builder.Services.AddMediator();

        // Register infrastructure services
        builder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
        builder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

        return builder.Build();
    }

}
