// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Text;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Tooling.Cli;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>The output streams of the compile command. Stdout holds only command output.</summary>
public interface ICompileOutput
{
    /// <summary>Standard output, for the JSON output or the published path.</summary>
    Stream StandardOutput { get; }

    /// <summary>Standard error, for human-mode diagnostics and the summary.</summary>
    TextWriter StandardError { get; }
}

/// <summary>The process console.</summary>
public sealed class ConsoleCompileOutput : ICompileOutput
{
    private readonly Lazy<Stream> _stdout = new(Console.OpenStandardOutput);

    /// <inheritdoc />
    public Stream StandardOutput => _stdout.Value;

    /// <inheritdoc />
    public TextWriter StandardError => Console.Error;
}

/// <summary>
/// <c>morphir compile</c>: CLI binding and presentation only (CLI-1). The handler owns orchestration. Human mode
/// streams each stage's diagnostics to stderr as the stage completes; <c>--json</c> streams the compile events as
/// JSON Lines and ends with the report (STRM-2); <c>--json=document</c> writes the report alone (adopt-streaming-json).
/// </summary>
public sealed class CompileCommand : Command, IMorphirParseErrorHandler
{
    private readonly Option<string?> _language = new("--language", "-l") { Description = "Source language, for example fsharp" };
    private readonly Option<string?> _extension = new("--extension") { Description = "Frontend extension; overrides [frontend.<language>].extension" };
    private readonly Option<string?> _extensionId = new("--extension-id") { Description = "Installed frontend full extension identity; mutually exclusive with --extension" };
    private readonly Option<string[]> _input = new("--input", "-i")
    {
        Description = "Source files in compilation order, or one directory",
        AllowMultipleArgumentsPerToken = true,
        Arity = ArgumentArity.OneOrMore
    };
    private readonly Option<string?> _projectFile = new("--project-file") { Description = "A build-tool project file that names the ordered sources, for example an .fsproj" };
    private readonly Option<string?> _packageName = new("--package-name") { Description = "Package name; overrides project.name" };
    private readonly Option<string?> _config = new("--config") { Description = "Explicit configuration file" };
    private readonly Option<string?> _project = new("--project") { Description = "Project directory, workspace member path or project name" };
    private readonly Option<string?> _irVersion = new("--ir-version") { Description = "IR format version, for example 3, 4 or 4.0.0; overrides ir.format_version" };
    private readonly Option<string> _json = JsonOutputOption.Create();
    private readonly Option<bool> _typesOnly = new("--types-only") { Description = "Not implemented; rejected" };
    private readonly Option<string?> _output = new("--output", "-o") { Description = "Install a copy of the IR in this directory. Files that an earlier install did not write stay unchanged" };
    private readonly Option<string?> _outDir = new("--out-dir") { Description = "Out root for the compile task; overrides MORPHIR_OUT_DIR and workspace.out_dir" };
    private readonly Option<string?> _target = new("--target") { Description = "Generation target to run on the compiled IR, for example fsharp or csharp" };
    private readonly Option<string?> _generateOutput = new("--generate-output") { Description = "Owned output directory of the generation; needed with --target" };
    private readonly Option<string[]> _generateOptions = new("--option")
    {
        Description = "Backend KEY=VALUE option for --target, repeatable",
        AllowMultipleArgumentsPerToken = false,
        Arity = ArgumentArity.OneOrMore
    };

    private readonly IServiceProvider _services;

    /// <summary>Builds the command over the application's services.</summary>
    public CompileCommand(IServiceProvider services) : base("compile", "Compile source code to Morphir IR")
    {
        _services = services;
        foreach (var option in new Option[] { _language, _extension, _extensionId, _input, _projectFile, _packageName, _config, _project, _irVersion, _json, _typesOnly, _output, _outDir })
            Options.Add(option);
        // CLI-2: compile can continue into generation.
        foreach (var option in new Option[] { _target, _generateOutput, _generateOptions })
            Options.Add(option);
        SetAction(async (result, ct) =>
        {
            var output = Output;
            var mode = JsonOutputOption.ModeOf(result);
            if (result.GetValue(_extension) is not null && result.GetValue(_extensionId) is not null)
                return await RenderInvocationAsync(output, [Invalid("Choose --extension or --extension-id, not both.")], mode);
            var context = Context;
            var target = CompileTarget.Parse(result.GetValue(_target), result.GetValue(_generateOutput), result.GetValue(_generateOptions),
                context.WorkingDirectory);
            if (!target.TryGetValue(out var generation))
                return await RenderInvocationAsync(output, target.Diagnostics, mode);
            var jsonOutput = JsonOutput(output, mode);
            var observer = mode switch
            {
                JsonOutputMode.Stream => (ICompileEventObserver)new JsonLinesObserver(new JsonLinesWriter(jsonOutput!)),
                JsonOutputMode.None => new HumanObserver(output.StandardError),
                _ => null
            };
            var mediator = services.GetRequiredService<IMediator>();
            var compiled = await mediator.InvokeAsync<CompileResult>(
                new CompileCommandRequest(Bind(result) with { Target = generation }, context, observer), ct);
            var report = CompileReport.From(compiled);
            // A note, not a diagnostic: it goes to stderr in every mode, so stdout and the JSON output stay as they were.
            if (compiled.Settings?.IgnoredBackendExtension is { } ignored && generation?.Target is Morphir.Tooling.Features.Generate.RequestedTarget.Valid requested)
                await output.StandardError.WriteLineAsync(
                    $"note: compile --target {requested.Id.Value} runs the built-in backend. {ProjectCompileSettingsResolver.Describe(ignored.Origin)} sets {ProjectCompileSettingsResolver.CodegenExtensionKey(requested.Id.Value)} = {ignored.Value}, which only morphir generate runs.");
            // As in generate: a host that cannot run the formatter says so on stderr, in every mode.
            if (report.Generation is { } generated)
                foreach (var warning in Morphir.Tooling.Features.Generate.GenerateCommand.FormattingWarnings(generated.Formatting, generated.Backend ?? generated.Target))
                    await output.StandardError.WriteLineAsync(warning);
            await RenderAsync(output, report, mode, jsonOutput, CancellationToken.None);
            return report.ExitCode;
        });
    }

    private ICompileOutput Output => _services.GetService<ICompileOutput>() ?? new ConsoleCompileOutput();

    private CompileContext Context => _services.GetService<CompileContextSource>()?.Current() ?? CompileContext.Current();

    private CompileOverrides Bind(ParseResult result) => new()
    {
        Language = result.GetValue(_language),
        Extension = result.GetValue(_extension),
        ExtensionId = result.GetValue(_extensionId),
        Inputs = result.GetValue(_input) ?? [],
        ProjectFile = result.GetValue(_projectFile),
        PackageName = result.GetValue(_packageName),
        Config = result.GetValue(_config),
        Project = result.GetValue(_project),
        IrVersion = result.GetValue(_irVersion),
        TypesOnly = result.GetValue(_typesOnly),
        Output = result.GetValue(_output),
        OutDir = result.GetValue(_outDir),
    };

    /// <inheritdoc />
    public int HandleParseErrors(ParseResult result)
    {
        // A parse error has no valid parse result, so the output mode comes from the tokens (JSON-4).
        var mode = JsonOutputOption.ScanTokens([.. result.Tokens.Select(token => token.Value)]);
        return RenderInvocationAsync(Output, OptionValueRewrite.ErrorMessages(result).Select(Invalid), mode).GetAwaiter().GetResult();
    }

    private static Diagnostic Invalid(string message) => Diagnostic.Create(Severity.Error, CompileCodes.InvalidArguments, message, null);

    private static async Task<int> RenderInvocationAsync(ICompileOutput output, IEnumerable<Diagnostic> diagnostics, JsonOutputMode mode)
    {
        var report = CompileReport.Invocation(diagnostics);
        if (mode == JsonOutputMode.None)
            foreach (var diagnostic in report.Diagnostics) await output.StandardError.WriteLineAsync(Human(diagnostic));
        await RenderAsync(output, report, mode, JsonOutput(output, mode), CancellationToken.None);
        return report.ExitCode;
    }

    /// <summary>The shared JSON output writer of <paramref name="mode"/>, or null in human mode.</summary>
    private static JsonOutputWriter<CompileReport>? JsonOutput(ICompileOutput output, JsonOutputMode mode) =>
        mode == JsonOutputMode.None ? null : JsonLinesWriter.Output(output.StandardOutput, mode);

    private static async Task RenderAsync(ICompileOutput output, CompileReport report, JsonOutputMode mode, JsonOutputWriter<CompileReport>? jsonOutput,
        CancellationToken ct)
    {
        switch (mode)
        {
            case JsonOutputMode.Stream or JsonOutputMode.Document:
                // Stream mode writes the completed record; document mode writes the report alone.
                jsonOutput!.Completed(report);
                break;
            default:
                await output.StandardError.WriteLineAsync(Summary(report));
                if (report.Output is { } published)
                {
                    await output.StandardOutput.WriteAsync(Encoding.UTF8.GetBytes(published + "\n"), ct);
                    await output.StandardOutput.FlushAsync(ct);
                }
                break;
        }
    }

    /// <summary>The human summary on stderr. It lists the same diagnostics that were streamed.</summary>
    public static string Summary(CompileReport report)
    {
        var counts = report.Diagnostics.GroupBy(diagnostic => diagnostic.Severity)
            .Select(group => $"{group.Count()} {group.Key}(s)");
        var listed = report.Diagnostics.Length == 0
            ? ""
            : ": " + string.Join(", ", report.Diagnostics.Select(diagnostic => $"{diagnostic.Severity} {diagnostic.Code ?? "(no code)"}"));
        var generated = report.Generation is { Backend: { } backend } generation && report.Success
            ? $", generated {generation.Artifacts.Length} artifact(s) with {backend} to {generation.Output}"
            : "";
        var outcome = report.Success
            ? $"compile succeeded: {report.Modules.Length} module(s), IR version {report.IrVersion}, {report.Output}{generated}"
            : $"compile failed ({report.FailureKind}, exit code {report.ExitCode})";
        var total = report.Diagnostics.Length == 0 ? "no diagnostics" : string.Join(", ", counts);
        return $"{outcome}; {total}{listed}";
    }

    /// <summary>One diagnostic as one stderr line: <c>uri:line:character: severity code: message</c>, one-based.</summary>
    public static string Human(CompileReportDiagnostic diagnostic)
    {
        var place = diagnostic.Location is { } location
            ? $"{location.Uri}:{location.Range.Start.Line + 1}:{location.Range.Start.Character + 1}: "
            : "";
        var code = diagnostic.Code is null ? "" : " " + diagnostic.Code;
        return $"{place}{diagnostic.Severity}{code}: {diagnostic.Message}";
    }

    private sealed class HumanObserver(TextWriter error) : ICompileEventObserver
    {
        public async ValueTask OnEventAsync(CompileEvent compileEvent, CancellationToken ct)
        {
            if (compileEvent is not CompileEvent.DiagnosticsReported reported) return;
            foreach (var diagnostic in reported.Diagnostics) await error.WriteLineAsync(Human(CompileReportDiagnostic.From(diagnostic)));
            await error.FlushAsync(ct);
        }
    }

    private sealed class JsonLinesObserver(JsonLinesWriter writer) : ICompileEventObserver
    {
        public ValueTask OnEventAsync(CompileEvent compileEvent, CancellationToken ct) => writer.WriteEventAsync(compileEvent, ct);
    }
}
