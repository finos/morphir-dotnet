using System.CommandLine;
using System.Collections.Immutable;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Generate;

/// <summary>CLI binding and presentation only. Target selection belongs to registered providers.</summary>
public sealed class GenerateCommand : Command, IMorphirParseErrorHandler
{
    private readonly Option<string?> _input = new("--input") { Description = "Model file or Ion tree" };
    private readonly Option<string?> _target = new("--target") { Description = "Registered generation target" };
    private readonly Option<string?> _output = new("--output") { Description = "Owned output directory" };
    private readonly Option<string?> _format = new("--input-format") { Description = "auto, json or ion" };
    private readonly Option<string[]> _options = new("--option") { Description = "Backend KEY=VALUE option, repeatable", AllowMultipleArgumentsPerToken = false, Arity = ArgumentArity.OneOrMore };
    private readonly Option<bool> _json = new("--json") { Description = "Output one JSON report" };

    public GenerateCommand(IServiceProvider services) : base("generate", "Generate artifacts from a Morphir model")
    {
        Options.Add(_input); Options.Add(_target); Options.Add(_output); Options.Add(_format); Options.Add(_options); Options.Add(_json);
        SetAction(async (result, ct) =>
        {
            var invocation = Bind(result);
            var mediator = services.GetRequiredService<IMediator>();
            var generated = await mediator.InvokeAsync<Outcome<GenerationSuccess>>(new GenerateRequest(invocation, new(Environment.CurrentDirectory)), ct);
            return Render(GenerationReport.From(generated, invocation), result.GetValue(_json));
        });
    }

    private GenerateOverrides Bind(ParseResult result) => new(result.GetValue(_input), result.GetValue(_target), result.GetValue(_output),
        result.GetValue(_format), result.GetValue(_options));

    public int HandleParseErrors(ParseResult result)
    {
        var failure = new GenerationFailure(GenerationFailureKind.Invocation,
            result.Errors.Select(error => new GenerationDiagnostic("invalid-arguments", error.Message)).ToImmutableArray());
        // A bad value for an option cannot be read. Parser diagnostics still use the requested JSON mode.
        var json = result.Tokens.Any(token => token.Value is "--json" or "--json=true");
        return Render(GenerationReport.From(new Outcome<GenerationSuccess>.Failure(failure), new()), json);
    }

    private static int Render(GenerationReport report, bool json)
    {
        if (json) ConsoleOutputHelper.WriteLineToStdout(report.ToJson());
        else
        {
            foreach (var artifact in report.Artifacts) ConsoleOutputHelper.WriteLineToStdout(artifact);
            foreach (var diagnostic in report.Diagnostics) Console.Error.WriteLine($"{diagnostic.Path ?? report.Input}: {diagnostic.Code} at {diagnostic.Location ?? "$"}: {diagnostic.Message}");
        }
        return report.ExitCode;
    }
}
