using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Tooling.Cli;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Validate;

/// <summary>
/// <c>morphir validate</c>: CLI binding and presentation only. The report goes to stdout: a table, the one
/// <c>completed</c> record of <c>--json</c>, or one JSON document with <c>--json=document</c>. Diagnostics of a run
/// that stopped before analysis go to stderr in text mode.
/// </summary>
public sealed class ValidateCommand : Command, IMorphirParseErrorHandler
{
    private readonly HostIdentity _host;
    private readonly Option<string?> _input = new("--input") { Description = "Model file or Ion tree" };
    private readonly Option<string?> _format = new("--input-format") { Description = "auto, json or ion" };
    private readonly Option<string> _json = JsonOutputOption.Create();

    public ValidateCommand(IServiceProvider services) : base("validate", "Check a Morphir model with the enabled analyzers")
    {
        _host = services.GetRequiredService<HostIdentity>();
        Options.Add(_input); Options.Add(_format); Options.Add(_json);
        SetAction(async (result, ct) =>
        {
            var report = await services.GetRequiredService<ModelValidation>().RunAsync(
                new ValidateOverrides(result.GetValue(_input), result.GetValue(_format)), new InvocationContext(Environment.CurrentDirectory), ct);
            return Render(report, JsonOutputOption.ModeOf(result), TerminalProfile.ForStdout());
        });
    }

    public int HandleParseErrors(ParseResult result, IReadOnlyList<string> messages)
    {
        // A parse error has no valid parse result, so the output mode comes from the tokens (JSON-4).
        var mode = JsonOutputOption.ScanTokens([.. result.Tokens.Select(token => token.Value)]);
        var report = new ValidationReport(_host.Host.WireValue, false, 2, null, null, null, [],
            messages.Select(message => new ValidationDiagnostic("error", "invalid-arguments", message, null, null, null, null)).ToArray());
        return Render(report, mode, TerminalProfile.Plain);
    }

    private static int Render(ValidationReport report, JsonOutputMode mode, TerminalProfile terminal)
    {
        if (mode != JsonOutputMode.None) JsonReportOutput.Write(ConsoleOutputHelper.Stdout, mode, report, ValidationReport.JsonInfo);
        else if (report.Stopped)
            foreach (var diagnostic in report.Diagnostics) Console.Error.WriteLine($"Error: {diagnostic.Code}: {diagnostic.Message}");
        else ConsoleOutputHelper.WriteLineToStdout(report.ToText(terminal));
        return report.ExitCode;
    }
}
