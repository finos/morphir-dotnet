using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Foundatio.Mediator;
using Serilog;
using Serilog.Events;

namespace Morphir.Tooling;

/// <summary>
/// Extension methods for MorphirCliBuilder to support common configuration patterns.
/// </summary>
public static class MorphirCliExtensions
{
    /// <summary>
    /// Adds a mediator-based command with automatic DI resolution.
    /// This simplifies command registration for commands that use the mediator pattern.
    /// </summary>
    /// <typeparam name="TCommand">The command type to send via mediator</typeparam>
    /// <typeparam name="TResult">The result type returned by the mediator</typeparam>
    /// <param name="builder">The MorphirCli builder</param>
    /// <param name="command">The System.CommandLine command to register</param>
    /// <param name="commandFactory">Factory to create the mediator command from parse result</param>
    /// <param name="resultHandler">Handler to process the mediator result and return exit code</param>
    /// <returns>This builder for chaining</returns>
    public static MorphirCliBuilder AddMediatorCommand<TCommand, TResult>(
        this MorphirCliBuilder builder,
        Command command,
        Func<ParseResult, TCommand> commandFactory,
        Func<TResult, ParseResult, int> resultHandler)
        where TCommand : notnull
    {
        command.SetAction(async parseResult =>
        {
            // Get the built host from the builder
            // Note: We need to build a temporary host here since the builder hasn't been built yet
            // This is a limitation of the current design - we'll address this in the refactoring
            using var tempHost = CreateTempHost(builder);
            await tempHost.StartAsync();

            var mediator = tempHost.Services.GetRequiredService<IMediator>();
            var cmd = commandFactory(parseResult);
            var result = await mediator.InvokeAsync<TResult>(cmd);

            await tempHost.StopAsync();
            return resultHandler(result, parseResult);
        });

        builder.AddCommand(command);
        return builder;
    }

    /// <summary>
    /// Configures the CLI for dotnet tool packaging.
    /// Adds tool-specific metadata and configuration.
    /// </summary>
    /// <param name="builder">The MorphirCli builder</param>
    /// <returns>This builder for chaining</returns>
    public static MorphirCliBuilder ConfigureForDotnetTool(this MorphirCliBuilder builder)
    {
        // Add tool-specific metadata, version info, etc.
        // Future: Add IToolMetadata service registration
        return builder;
    }

    /// <summary>
    /// Configures the CLI for standalone executable.
    /// Adds standalone-specific configuration such as plugin discovery.
    /// </summary>
    /// <param name="builder">The MorphirCli builder</param>
    /// <returns>This builder for chaining</returns>
    public static MorphirCliBuilder ConfigureForStandalone(this MorphirCliBuilder builder)
    {
        // Standalone-specific configuration (plugin discovery, etc.)
        // Future: Add plugin discovery services
        return builder;
    }

    // Helper to create a temporary host for mediator commands
    // This is a workaround until we refactor to use a single app-wide host
    private static IHost CreateTempHost(MorphirCliBuilder builder)
    {
        var tempBuilder = Host.CreateApplicationBuilder();

        // Apply the same default configuration
        tempBuilder.Logging.ClearProviders();

        Serilog.Log.Logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        tempBuilder.Logging.AddSerilog(Serilog.Log.Logger, dispose: true);
        tempBuilder.Services.AddMediator();
        tempBuilder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
        tempBuilder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

        return tempBuilder.Build();
    }
}
