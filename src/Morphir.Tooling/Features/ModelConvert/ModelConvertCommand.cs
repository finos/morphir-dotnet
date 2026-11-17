// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Cli;

namespace Morphir.Tooling.Features.ModelConvert;

/// <summary>The output streams of <c>model convert</c>. Stdout holds only the command's output.</summary>
public interface IModelConvertOutput
{
    /// <summary>Standard output: the JSON output, or the output path in human mode.</summary>
    Stream StandardOutput { get; }

    /// <summary>Standard error: the diagnostics, the notices and the summary in human mode.</summary>
    TextWriter StandardError { get; }
}

/// <summary>The process console.</summary>
public sealed class ConsoleModelConvertOutput : IModelConvertOutput
{
    private readonly Lazy<Stream> _stdout = new(Console.OpenStandardOutput);

    /// <inheritdoc />
    public Stream StandardOutput => _stdout.Value;

    /// <inheritdoc />
    public TextWriter StandardError => Console.Error;
}

/// <summary>
/// <c>morphir model convert</c>: CLI binding and presentation (CLI-2). <see cref="ModelConversion"/> runs the
/// conversion. <c>--layout=document-tree|single-file</c> chooses the output layout (default: the input's) and
/// <c>--compact[=names]</c> asks for a compacted output, its mode given only with <c>=</c>. Human mode writes the
/// diagnostics and notices to stderr as they occur and the output path to stdout; <c>--json</c> streams the stage
/// events and a <c>fileWritten</c> record per file and ends with the report; <c>--json=document</c> writes the report
/// alone. A parse error is a report with exit code 2, in the JSON mode that the tokens ask for.
/// </summary>
/// <remarks>
/// <c>--compact=names</c> writes each file, the single file or each file of a tree, under the parent's writer policy
/// <c>morphir.compact/0.1.0-draft.1</c>, and the report names the mode and the policy.
/// </remarks>
public sealed class ModelConvertCommand : Command, IMorphirParseErrorHandler
{
    /// <summary>The name of the compaction option.</summary>
    public const string CompactOptionName = "--compact";

    /// <summary>The compaction modes, in the order that an unknown-mode error lists them.</summary>
    public static IReadOnlyList<string> CompactModes { get; } = ["names"];

    private readonly Option<string> _input = new("--input", "-i") { Description = "The model to convert: a single file, or a document tree's directory", Required = true };
    private readonly Option<string> _output = new("--output", "-o") { Description = "Where to write the converted model; it must not exist, or be an empty directory for a tree", Required = true };
    private readonly Option<string?> _layout = new("--layout")
    {
        Description = "The output layout: document-tree or single-file. Defaults to the input's layout",
        HelpName = string.Join('|', ModelConvertNames.Layouts),
    };
    private readonly Option<string> _compact = OptionValueRewrite.CreateOption(CompactOptionName, CompactModes[0], CompactModes,
        "Compact the output: --compact means --compact=names, which gives repeated names an alias in each file's own context (writer policy morphir.compact/0.1.0-draft.1)");
    private readonly Option<string> _json = JsonOutputOption.Create();

    private readonly IServiceProvider _services;

    /// <summary>Builds the command over the application's services.</summary>
    public ModelConvertCommand(IServiceProvider services)
        : base("convert", "Convert a Morphir model between a single file and a document tree, keeping its IR version")
    {
        _services = services;
        _layout.AcceptOnlyFromAmong([.. ModelConvertNames.Layouts]);
        foreach (var option in new Option[] { _input, _output, _layout, _compact, _json }) Options.Add(option);
        SetAction(async (result, ct) =>
        {
            var output = Output;
            var mode = JsonOutputOption.ModeOf(result);
            var json = JsonOutput(output, mode);
            var request = new ConvertRequest(result.GetValue(_input)!, result.GetValue(_output)!,
                result.GetValue(_layout) is { } layout ? ModelConvertNames.LayoutOf(layout) : null,
                result.GetResult(_compact) is { Implicit: false } ? result.GetValue(_compact) ?? CompactModes[0] : null);
            IConvertObserver observer = json is null ? new HumanObserver(output.StandardError) : new JsonObserver(json);
            var report = await new ModelConversion(Store).RunAsync(request, observer, ct);
            await RenderAsync(output, report, json);
            return report.ExitCode;
        });
    }

    private IModelConvertOutput Output => _services.GetService<IModelConvertOutput>() ?? new ConsoleModelConvertOutput();

    private IModelConversionStore Store => _services.GetService<IModelConversionStore>() ?? new DiskModelConversionStore();

    /// <inheritdoc />
    public int HandleParseErrors(ParseResult result, IReadOnlyList<string> messages)
    {
        // A parse error has no valid parse result, so the output mode comes from the tokens (JSON-4).
        var mode = JsonOutputOption.ScanTokens([.. result.Tokens.Select(token => token.Value)]);
        var output = Output;
        var report = ConvertReport.Usage(messages);
        if (mode == JsonOutputMode.None)
            foreach (var diagnostic in report.Diagnostics) output.StandardError.WriteLine(diagnostic.ToString());
        RenderAsync(output, report, JsonOutput(output, mode)).GetAwaiter().GetResult();
        return report.ExitCode;
    }

    /// <summary>The shared JSON output writer of <paramref name="mode"/> on stdout, or null in human mode.</summary>
    private static JsonOutputWriter<ConvertReport>? JsonOutput(IModelConvertOutput output, JsonOutputMode mode) =>
        mode == JsonOutputMode.None
            ? null
            : new(new StreamWriter(output.StandardOutput, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: -1, leaveOpen: true),
                mode, ConvertReport.JsonInfo);

    private static async Task RenderAsync(IModelConvertOutput output, ConvertReport report, JsonOutputWriter<ConvertReport>? json)
    {
        if (json is not null)
        {
            json.Completed(report);
            return;
        }

        await output.StandardError.WriteLineAsync(Summary(report));
        await output.StandardError.FlushAsync();
        if (report.Success && report.Output is { } written)
        {
            await output.StandardOutput.WriteAsync(Encoding.UTF8.GetBytes(written + "\n"));
            await output.StandardOutput.FlushAsync();
        }
    }

    /// <summary>The human summary on stderr.</summary>
    public static string Summary(ConvertReport report) => report.Success
        ? $"model convert: wrote {report.FilesWritten.Length} file(s) to {report.Output}: IR {report.IrVersion}, {report.InputLayout} to {report.OutputLayout}, {report.OutputEncoding}; {report.Notices.Length} notice(s)"
        : $"model convert failed (exit code {report.ExitCode}): {string.Join(", ", report.Diagnostics.Select(diagnostic => diagnostic.Code))}";

    /// <summary>Writes each diagnostic and notice to stderr as it occurs.</summary>
    private sealed class HumanObserver(TextWriter error) : IConvertObserver
    {
        public void StageStarted(string stage) { }

        public void Diagnostics(string? stage, IReadOnlyList<ConvertDiagnostic> diagnostics) => Write(diagnostics);

        public void Notices(string stage, IReadOnlyList<ConvertDiagnostic> notices) => Write(notices);

        public void FileWritten(string path) { }

        public void StageCompleted(string stage, bool passed) { }

        private void Write(IReadOnlyList<ConvertDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics) error.WriteLine(diagnostic.ToString());
            error.Flush();
        }
    }

    /// <summary>
    /// Writes each event as one JSON Lines record through the shared writer, flushed before the command goes on:
    /// <c>stageStarted</c>, <c>diagnostics</c>, <c>notices</c>, <c>fileWritten</c> and <c>stageCompleted</c>. In
    /// document mode the writer writes no event.
    /// </summary>
    private sealed class JsonObserver(JsonOutputWriter<ConvertReport> output) : IConvertObserver
    {
        public void StageStarted(string stage) => output.Event("stageStarted", record => record.WriteString("stage", stage));

        public void Diagnostics(string? stage, IReadOnlyList<ConvertDiagnostic> diagnostics) =>
            output.Event("diagnostics", record => Write(record, stage, "diagnostics", diagnostics));

        public void Notices(string stage, IReadOnlyList<ConvertDiagnostic> notices) =>
            output.Event("notices", record => Write(record, stage, "notices", notices));

        public void FileWritten(string path) => output.Event("fileWritten", record => record.WriteString("path", path));

        public void StageCompleted(string stage, bool passed) => output.Event("stageCompleted", record =>
        {
            record.WriteString("stage", stage);
            record.WriteBoolean("passed", passed);
        });

        private static void Write(Utf8JsonWriter record, string? stage, string name, IReadOnlyList<ConvertDiagnostic> items)
        {
            if (stage is not null) record.WriteString("stage", stage);
            record.WritePropertyName(name);
            JsonSerializer.Serialize(record, [.. items], ConvertJsonContext.Relaxed.ConvertDiagnosticArray);
        }
    }
}
