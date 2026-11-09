using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Validate;

/// <summary>
/// <c>morphir validate</c>: CLI binding and presentation only. The report goes to stdout (a table, or one JSON document
/// with <c>--json</c>); diagnostics of a run that stopped before analysis go to stderr in text mode.
/// </summary>
public sealed class ValidateCommand : Command, IMorphirParseErrorHandler
{
    private readonly HostIdentity _host;
    private readonly Option<string?> _input = new("--input") { Description = "Model file or Ion tree" };
    private readonly Option<string?> _format = new("--input-format") { Description = "auto, json or ion" };
    private readonly Option<bool> _json = new("--json") { Description = "Output one JSON report" };

    public ValidateCommand(IServiceProvider services) : base("validate", "Check a Morphir model with the enabled analyzers")
    {
        _host = services.GetRequiredService<HostIdentity>();
        Options.Add(_input); Options.Add(_format); Options.Add(_json);
        SetAction(async (result, ct) =>
        {
            var report = await services.GetRequiredService<ModelValidation>().RunAsync(
                new ValidateOverrides(result.GetValue(_input), result.GetValue(_format)), new InvocationContext(Environment.CurrentDirectory), ct);
            return Render(report, result.GetValue(_json), TerminalProfile.ForStdout());
        });
    }

    public int HandleParseErrors(ParseResult result)
    {
        // A bad value for an option cannot be read. Parser diagnostics still use the requested JSON mode.
        var json = result.Tokens.Any(token => token.Value is "--json" or "--json=true");
        var report = new ValidationReport(_host.Host.WireValue, false, 2, null, null, null, [],
            result.Errors.Select(error => new ValidationDiagnostic("error", "invalid-arguments", error.Message, null, null, null, null)).ToArray());
        return Render(report, json, TerminalProfile.Plain);
    }

    private static int Render(ValidationReport report, bool json, TerminalProfile terminal)
    {
        if (json) ConsoleOutputHelper.WriteLineToStdout(report.ToJson());
        else if (report.Stopped)
            foreach (var diagnostic in report.Diagnostics) Console.Error.WriteLine($"Error: {diagnostic.Code}: {diagnostic.Message}");
        else ConsoleOutputHelper.WriteLineToStdout(report.ToText(terminal));
        return report.ExitCode;
    }
}
