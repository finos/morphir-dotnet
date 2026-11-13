using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Morphir.Tooling.Infrastructure;

/// <summary>
/// The Serilog logger of the CLI. Every log event goes to stderr, so stdout holds only the output of a command.
/// Framework start-up messages at Information, such as Foundatio.Mediator's count of the handlers it registered,
/// are not shown; their warnings and errors are.
/// </summary>
internal static class CliLogging
{
    /// <summary>The category of the start-up messages of Foundatio.Mediator.</summary>
    internal const string MediatorCategory = "Foundatio.Mediator";

    private const string OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>The levels of the CLI logger, with no sink.</summary>
    internal static LoggerConfiguration Levels() => new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
        .MinimumLevel.Override(MediatorCategory, LogEventLevel.Warning);

    /// <summary>The CLI logger. When <paramref name="writeToStandardError"/> is false it writes nowhere.</summary>
    internal static Logger Create(bool writeToStandardError = true)
    {
        var configuration = Levels();
        if (writeToStandardError)
            configuration.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose, outputTemplate: OutputTemplate);
        return configuration.CreateLogger();
    }
}
