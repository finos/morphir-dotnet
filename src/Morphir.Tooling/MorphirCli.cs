using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Foundatio.Mediator;

namespace Morphir.Tooling;

/// <summary>
/// Factory for creating MorphirCli builders and applications.
/// </summary>
public static class MorphirCli
{
    /// <summary>
    /// Creates a new MorphirCli builder with the specified arguments and description.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <param name="description">Description of the CLI application</param>
    /// <returns>A configured MorphirCliBuilder</returns>
    public static MorphirCliBuilder CreateBuilder(string[] args, string? description = null)
    {
        return new MorphirCliBuilder(args, description ?? "Morphir CLI");
    }

    /// <summary>
    /// Creates a default MorphirCli application with optional configuration.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>A configured and built MorphirCliApp</returns>
    public static MorphirCliApp CreateDefaultApp(string[] args, Action<MorphirCliBuilder>? configure = null)
    {
        var builder = CreateBuilder(args);
        configure?.Invoke(builder);
        return builder.Build();
    }
}

/// <summary>
/// Builder for configuring and creating a MorphirCli application.
/// Provides a fluent API for registering commands, services, and scenarios.
/// </summary>
public sealed class MorphirCliBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;
    private readonly System.Runtime.ExceptionServices.ExceptionDispatchInfo? _initializationFailure;
    private readonly RootCommand _rootCommand;
    private readonly List<Action<IServiceCollection>> _serviceConfigurations = new();
    private readonly List<Action<RootCommand>> _commandConfigurations = new();
    private readonly List<Func<IServiceProvider, Command>> _hostedCommands = new();
    private readonly List<Action<RootCommand, IServiceProvider>> _hostedCommandConfigurations = new();
    private readonly List<ICommandProvider> _commandProviders = new();

    internal MorphirCliBuilder(string[] args, string description = "Morphir CLI")
    {
        // CRITICAL: Redirect console FIRST before any other code runs
        // This ensures all Console.WriteLine calls go to stderr
        Infrastructure.ConsoleOutputHelper.RedirectConsoleToStderr();
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Args = args;
        _rootCommand = new RootCommand(description);
        // Help must not load appsettings or perform configuration discovery.
        // Ordinary command execution retains the existing host defaults.
        var help = args.Any(arg => arg is "--help" or "-h" or "-?" or "/?");
        try
        {
            _hostBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = help });
        }
        catch (Exception error) when ((args.FirstOrDefault() == "config"
            || args.Length >= 2 && args[0] is "project" or "workspace" && args[1] == "migrate")
            && error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            // Retain the failure for an opted-in command's structured renderer.
            // A minimal host allows argument validation and help to keep their own contracts.
            _initializationFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
            _hostBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        }

        // Apply default configuration
        ConfigureDefaultLogging();
        ConfigureDefaultServices();
    }

    /// <summary>
    /// Gets the command line arguments for this application.
    /// </summary>
    public string[] Args { get; }

    /// <summary>
    /// Gets the underlying host application builder.
    /// Use this for advanced configuration scenarios.
    /// </summary>
    public IHostApplicationBuilder HostBuilder => _hostBuilder;

    /// <summary>
    /// Gets the root command for this CLI application.
    /// Use this for direct command manipulation if needed.
    /// </summary>
    public RootCommand RootCommand => _rootCommand;

    /// <summary>
    /// Configures services for the application.
    /// </summary>
    /// <param name="configure">Action to configure the service collection</param>
    /// <returns>This builder for chaining</returns>
    public MorphirCliBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        _serviceConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds a command to the root command.
    /// </summary>
    /// <param name="command">The command to add</param>
    /// <returns>This builder for chaining</returns>
    public MorphirCliBuilder AddCommand(Command command)
    {
        _rootCommand.Subcommands.Add(command);
        return this;
    }

    /// <summary>Adds a command whose action uses this application's service provider.</summary>
    public MorphirCliBuilder AddCommand(Func<IServiceProvider, Command> createCommand)
    {
        _hostedCommands.Add(createCommand);
        return this;
    }

    /// <summary>
    /// Configures commands using an action.
    /// </summary>
    /// <param name="configure">Action to configure the root command</param>
    /// <returns>This builder for chaining</returns>
    public MorphirCliBuilder AddCommand(Action<RootCommand> configure)
    {
        _commandConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Changes the command tree after the host is built, with its service provider. Use it to add a subcommand
    /// that needs services to a command that <see cref="AddCommand(Action{RootCommand})"/> created.
    /// </summary>
    public MorphirCliBuilder ConfigureHostedCommands(Action<RootCommand, IServiceProvider> configure)
    {
        _hostedCommandConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds a scenario-based configuration bundle.
    /// Scenarios group related commands, services, and configuration together.
    /// </summary>
    /// <typeparam name="TScenario">The scenario type to add</typeparam>
    /// <returns>This builder for chaining</returns>
    public MorphirCliBuilder AddScenario<TScenario>() where TScenario : IMorphirCliScenario, new()
    {
        var scenario = new TScenario();
        scenario.Configure(this);
        return this;
    }

    /// <summary>
    /// Adds a command provider for modular command registration.
    /// </summary>
    /// <typeparam name="TProvider">The command provider type</typeparam>
    /// <returns>This builder for chaining</returns>
    public MorphirCliBuilder AddCommandProvider<TProvider>() where TProvider : ICommandProvider, new()
    {
        _commandProviders.Add(new TProvider());
        return this;
    }

    /// <summary>
    /// Adds a command provider instance.
    /// </summary>
    /// <param name="provider">The command provider to add</param>
    /// <returns>This builder for chaining</returns>
    public MorphirCliBuilder AddCommandProvider(ICommandProvider provider)
    {
        _commandProviders.Add(provider);
        return this;
    }

    /// <summary>
    /// Builds the MorphirCli application.
    /// </summary>
    /// <returns>A configured MorphirCliApp ready to run</returns>
    public MorphirCliApp Build()
    {
        if (_initializationFailure is not null)
        {
            var handlers = _commandProviders.OfType<IMorphirHostInitializationErrorHandler>().ToArray();
            if (handlers.Length == 0) _initializationFailure.Throw();
            foreach (var handler in handlers) handler.HandleHostInitializationFailure();
        }

        // Apply all service configurations
        foreach (var configure in _serviceConfigurations)
        {
            configure(_hostBuilder.Services);
        }

        // Apply all command configurations
        foreach (var configure in _commandConfigurations)
        {
            configure(_rootCommand);
        }

        // Apply all command providers
        foreach (var provider in _commandProviders)
        {
            provider.RegisterCommands(_rootCommand, _hostBuilder.Services);
        }

        var host = _hostBuilder.Build();
        foreach (var createCommand in _hostedCommands)
            _rootCommand.Subcommands.Add(createCommand(host.Services));
        foreach (var configure in _hostedCommandConfigurations)
            configure(_rootCommand, host.Services);
        return new MorphirCliApp(host, _rootCommand, Args);
    }

    private void ConfigureDefaultLogging()
    {
        // CRITICAL: Clear default logging providers FIRST to prevent stdout contamination
        // The default Console logger writes to stdout, which breaks JSON output and scriptability
        _hostBuilder.Logging.ClearProviders();

        // Configure Serilog to write ALL logs to stderr only
        // This keeps stdout clean for command output (JSON, formatted results, etc.)
        // This is critical for CLI tools: stdout = data, stderr = diagnostics
        Log.Logger = Infrastructure.CliLogging.Create();

        // Add Serilog to the logging builder (not services)
        _hostBuilder.Logging.AddSerilog(Log.Logger, dispose: true);
    }

    private void ConfigureDefaultServices()
    {
        // Configure Foundatio.Mediator - uses source generators for handler discovery (trimming/AOT-friendly)
        Infrastructure.MediatorRegistration.Add(_hostBuilder.Services);

        // Register infrastructure services
        _hostBuilder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
        _hostBuilder.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();
    }
}

/// <summary>
/// Represents a configured MorphirCli application ready to run.
/// </summary>
public sealed class MorphirCliApp : IDisposable, IAsyncDisposable
{
    private readonly IHost _host;
    private readonly RootCommand _rootCommand;
    private readonly string[] _args;

    internal MorphirCliApp(IHost host, RootCommand rootCommand, string[] args)
    {
        _host = host;
        _rootCommand = rootCommand;
        _args = args;
    }

    /// <summary>
    /// Gets the service provider for this application.
    /// </summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>
    /// Runs the CLI application asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Exit code (0 = success, non-zero = failure)</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        await _host.StartAsync(cancellationToken);
        try
        {
            var result = _rootCommand.Parse(_args);
            return result.Errors.Count > 0 && result.CommandResult.Command is IMorphirParseErrorHandler handler
                ? handler.HandleParseErrors(result)
                : await result.InvokeAsync(cancellationToken: cancellationToken);
        }
        finally
        {
            await _host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs the CLI application synchronously.
    /// </summary>
    /// <returns>Exit code (0 = success, non-zero = failure)</returns>
    public int Run()
    {
        _host.Start();
        try
        {
            return Invoke(_rootCommand.Parse(_args));
        }
        finally
        {
            _host.StopAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Disposes the application and its underlying host.
    /// </summary>
    public void Dispose() => _host.Dispose();

    private static int Invoke(System.CommandLine.ParseResult result) =>
        result.Errors.Count > 0 && result.CommandResult.Command is IMorphirParseErrorHandler handler
            ? handler.HandleParseErrors(result)
            : result.Invoke();

    /// <summary>
    /// Asynchronously disposes the application and its underlying host.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _host.Dispose();
        }
    }
}
