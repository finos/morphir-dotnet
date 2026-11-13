using FluentAssertions;
using Morphir.Tooling.Infrastructure;
using Serilog.Core;
using Serilog.Events;

namespace Morphir.Tooling.Tests.Infrastructure;

/// <summary>
/// The levels of the CLI logger. The black-box scenarios of compile and generate check that the mediator's start-up
/// message is not on stderr; these tests check that the same levels keep its warnings and errors and the CLI's own
/// information messages.
/// </summary>
public sealed class CliLoggingTests
{
    private sealed class Collect : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static IReadOnlyList<LogEventLevel> Written(string category, params LogEventLevel[] levels)
    {
        var sink = new Collect();
        using (var logger = CliLogging.Levels().WriteTo.Sink(sink).CreateLogger())
        {
            var context = logger.ForContext(Constants.SourceContextPropertyName, category);
            foreach (var level in levels) context.Write(level, "message");
        }
        return sink.Events.Select(e => e.Level).ToList();
    }

    [Test]
    public void TheMediatorWritesOnlyItsWarningsAndErrors()
    {
        Written(CliLogging.MediatorCategory, LogEventLevel.Debug, LogEventLevel.Information, LogEventLevel.Warning, LogEventLevel.Error)
            .Should().Equal(LogEventLevel.Warning, LogEventLevel.Error);
        Written(CliLogging.MediatorCategory + ".Mediator", LogEventLevel.Information, LogEventLevel.Warning)
            .Should().Equal(LogEventLevel.Warning);
    }

    [Test]
    public void TheCliKeepsItsInformationMessages() =>
        Written("Morphir.Tooling.Features.Compile", LogEventLevel.Debug, LogEventLevel.Information, LogEventLevel.Warning)
            .Should().Equal(LogEventLevel.Information, LogEventLevel.Warning);
}
