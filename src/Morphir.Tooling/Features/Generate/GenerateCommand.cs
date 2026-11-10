using System.CommandLine;
using System.Collections.Immutable;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.CodeGeneration.FSharp;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Generate;

/// <summary>CLI binding and presentation only. Target selection belongs to registered providers.</summary>
public sealed class GenerateCommand : Command, IMorphirParseErrorHandler
{
    private readonly Option<string?> _input = new("--input") { Description = "Model file or Ion tree. Without it, the IR of the most recent morphir compile of the project" };
    private readonly Option<string?> _target = new("--target") { Description = "Registered generation target" };
    private readonly Option<string?> _output = new("--output") { Description = "Owned output directory" };
    private readonly Option<string?> _format = new("--input-format") { Description = "auto, json or ion" };
    private readonly Option<string[]> _options = new("--option") { Description = "Backend KEY=VALUE option, repeatable", AllowMultipleArgumentsPerToken = false, Arity = ArgumentArity.OneOrMore };
    private readonly Option<bool> _json = new("--json") { Description = "Output one JSON report" };
    private readonly Option<string?> _project = new("--project") { Description = "Project directory, workspace member path or project name, for input from a prior compile and its configuration" };
    private readonly Option<string?> _config = new("--config") { Description = "Explicit configuration file, for input from a prior compile and [codegen.<target>].extension" };
    private readonly Option<string?> _extension = new("--extension") { Description = "Backend extension; overrides [codegen.<target>].extension" };
    private readonly Option<string?> _outDir = new("--out-dir") { Description = "Out root that holds the compile record; overrides MORPHIR_OUT_DIR and workspace.out_dir" };

    public GenerateCommand(IServiceProvider services) : base("generate", "Generate artifacts from a Morphir model")
    {
        Options.Add(_input); Options.Add(_target); Options.Add(_output); Options.Add(_format); Options.Add(_options); Options.Add(_json);
        Options.Add(_extension); Options.Add(_project); Options.Add(_config); Options.Add(_outDir);
        SetAction(async (result, ct) =>
        {
            var invocation = Bind(result);
            var mediator = services.GetRequiredService<IMediator>();
            var generated = await mediator.InvokeAsync<Outcome<GenerationSuccess>>(new GenerateRequest(invocation, new(Environment.CurrentDirectory)), ct);
            return Render(GenerationReport.From(generated, invocation), result.GetValue(_json));
        });
    }

    private GenerateOverrides Bind(ParseResult result) => new(result.GetValue(_input), result.GetValue(_target), result.GetValue(_output),
        result.GetValue(_format), result.GetValue(_options))
    {
        Project = result.GetValue(_project),
        Config = result.GetValue(_config),
        OutDir = result.GetValue(_outDir),
        Extension = result.GetValue(_extension),
    };

    public int HandleParseErrors(ParseResult result)
    {
        var failure = new GenerationFailure(GenerationFailureKind.Invocation,
            result.Errors.Select(error => new GenerationDiagnostic("invalid-arguments", error.Message)).ToImmutableArray());
        // A bad value for an option cannot be read. Parser diagnostics still use the requested JSON mode.
        var json = result.Tokens.Any(token => token.Value is "--json" or "--json=true");
        return Render(GenerationReport.From(new Outcome<GenerationSuccess>.Failure(failure), new()), json);
    }

    /// <summary>
    /// The warnings of a report, for stderr only: today one, when the backend was asked to format (the default) but
    /// this host cannot run the formatter, as the Native AOT <c>morphir</c> cannot run Fantomas.
    /// </summary>
    public static IReadOnlyList<string> Warnings(GenerationReport report) =>
        report.Formatting == FSharpFormatting.Unavailable.WireValue
            ? [$"warning: {report.Backend ?? report.Target} did not format the generated files, because this host cannot run the formatter (Fantomas needs dynamic code, which Native AOT does not have). The files are the generator's unformatted text. Pass --option format=false to ask for that text explicitly, or use dotnet-morphir for formatted files."]
            : [];

    private static int Render(GenerationReport report, bool json)
    {
        // Warnings go to stderr in every mode; stdout holds only the report or the artifact paths.
        foreach (var warning in Warnings(report)) Console.Error.WriteLine(warning);
        if (json) ConsoleOutputHelper.WriteLineToStdout(report.ToJson());
        else
        {
            foreach (var artifact in report.Artifacts) ConsoleOutputHelper.WriteLineToStdout(artifact);
            foreach (var diagnostic in report.Diagnostics) Console.Error.WriteLine($"{diagnostic.Path ?? report.Input}: {diagnostic.Code} at {diagnostic.Location ?? "$"}: {diagnostic.Message}");
        }
        return report.ExitCode;
    }
}
