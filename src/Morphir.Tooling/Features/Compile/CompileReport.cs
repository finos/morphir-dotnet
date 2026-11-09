// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.Compilation;

namespace Morphir.Tooling.Features.Compile;

/// <summary>A position: zero-based line, and character in UTF-16 code units.</summary>
public sealed record CompileReportPosition(int Line, int Character);

/// <summary>A range with an exclusive end.</summary>
public sealed record CompileReportRange(CompileReportPosition Start, CompileReportPosition End);

/// <summary>A range in one document.</summary>
public sealed record CompileReportLocation(string Uri, CompileReportRange Range);

/// <summary>A related location and its message.</summary>
public sealed record CompileReportRelated(CompileReportLocation Location, string Message);

/// <summary>A diagnostic in the frontend (MEP) shape.</summary>
public sealed record CompileReportDiagnostic(
    string Severity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CompileReportLocation? Location,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CompileReportRelated[]? Related,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Data)
{
    /// <summary>The report shape of <paramref name="diagnostic"/>. Nothing is lost.</summary>
    public static CompileReportDiagnostic From(Diagnostic diagnostic) => new(
        diagnostic.Severity.WireValue,
        diagnostic.CodeOrNull?.Value,
        diagnostic.Message,
        diagnostic.LocationOrNull is { } location ? LocationOf(location) : null,
        diagnostic.Related.Count == 0 ? null : [.. diagnostic.Related.Select(related => new CompileReportRelated(LocationOf(related.Location), related.Message))],
        diagnostic.Data);

    private static CompileReportLocation LocationOf(Morphir.Compilation.Location location) =>
        new(location.Uri, new(PositionOf(location.Range.Start), PositionOf(location.Range.End)));

    private static CompileReportPosition PositionOf(Morphir.Compilation.Position position) => new(position.Line, position.Character);
}

/// <summary>The selected sources: the project file (if any), the root, and the documents in compilation order.</summary>
public sealed record CompileReportSources(string? ProjectFile, string Root, string[] Documents);

/// <summary>Where a setting came from: <c>cli</c> with its flag, <c>configuration</c> with its file and key, or <c>default</c>.</summary>
public sealed record CompileReportOrigin(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Flag,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? File,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Key)
{
    /// <summary>The report shape of <paramref name="origin"/>.</summary>
    public static CompileReportOrigin From(SettingOrigin origin) => origin.Match(
        cli => new CompileReportOrigin("cli", cli.Flag, null, null),
        config => new CompileReportOrigin("configuration", null, config.File, config.Key),
        _ => new CompileReportOrigin("default", null, null, null));
}

/// <summary>
/// The one JSON report of <c>morphir compile --json</c>, and the report of the last <c>--json-lines</c> record.
/// Members are camelCase. <c>installed</c> lists the <c>-o</c> installations of this run.
/// </summary>
public sealed record CompileReport(
    bool Success,
    int ExitCode,
    string? Language,
    string? Frontend,
    string? IrVersion,
    CompileReportSources? Sources,
    string[] Modules,
    string? Output,
    string[] Installed,
    CompileReportDiagnostic[] Diagnostics,
    string? FailureKind,
    Dictionary<string, CompileReportOrigin>? Origins)
{
    /// <summary>The report of a folded compile result.</summary>
    public static CompileReport From(CompileResult result)
    {
        var diagnostics = result.Diagnostics.Select(diagnostic => CompileReportDiagnostic.From(diagnostic.Diagnostic)).ToArray();
        var origins = result.Settings?.Origins().ToDictionary(origin => origin.Name, origin => CompileReportOrigin.From(origin.Origin), StringComparer.Ordinal);
        var language = result.Settings?.Language?.Value.Value;
        var irVersion = result.Settings?.IrVersion.Value.Text;
        return result.Conclusion.Match(
            success => new CompileReport(true, 0, success.Language, success.Frontend, success.IrVersion.Text,
                SourcesOf(success.Sources, result.Settings!.WorkingDirectory), [.. success.Modules], success.Publication.IrPath, [],
                diagnostics, null, origins),
            failure => new CompileReport(false, failure.Kind.ExitCode(), language, failure.Frontend, irVersion,
                failure.Sources is { } sources ? SourcesOf(sources, result.Settings!.WorkingDirectory) : null, [], null, [],
                diagnostics, failure.Kind.WireValue(), origins));
    }

    /// <summary>The report of an invocation that could not run, for example a parse error.</summary>
    public static CompileReport Invocation(IEnumerable<Diagnostic> diagnostics) =>
        new(false, CompileFailureKind.Invocation.ExitCode(), null, null, null, null, [], null, [],
            [.. diagnostics.Select(CompileReportDiagnostic.From)], CompileFailureKind.Invocation.WireValue(), null);

    private static CompileReportSources SourcesOf(SelectedSources sources, string workingDirectory) =>
        new(sources.Selection.ProjectFileOrNull is { } projectFile ? Display(projectFile, workingDirectory) : null,
            Display(sources.AbsoluteRoot, workingDirectory), [.. sources.Selection.Documents]);

    /// <summary>A path relative to the working directory when it is inside it, else absolute, with '/' separators.</summary>
    private static string Display(string path, string workingDirectory)
    {
        var full = Path.GetFullPath(path, workingDirectory);
        var relative = Path.GetRelativePath(workingDirectory, full);
        var inside = !(relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative));
        return (inside ? relative : full).Replace('\\', '/');
    }

    /// <summary>The indented JSON of <c>--json</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, CompileJsonContext.Relaxed.CompileReport);
}

/// <summary>A <c>stageStarted</c> record.</summary>
public sealed record StageStartedRecord(string Event, string Stage);

/// <summary>A <c>diagnostics</c> record.</summary>
public sealed record DiagnosticsRecord(string Event, string Stage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Document,
    CompileReportDiagnostic[] Diagnostics);

/// <summary>A <c>stageCompleted</c> record.</summary>
public sealed record StageCompletedRecord(string Event, string Stage, bool Passed);

/// <summary>The last <c>completed</c> record, with the full report.</summary>
public sealed record CompletedRecord(string Event, CompileReport Report);

/// <summary>The indented JSON contract of <c>--json</c>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(CompileReport))]
internal partial class CompileJsonContext : JsonSerializerContext
{
    /// <summary>The context with relaxed escaping: stdout is not HTML, so quotes and apostrophes stay readable.</summary>
    public static CompileJsonContext Relaxed { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}

/// <summary>The compact JSON contract of <c>--json-lines</c> records (STRM-2).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(StageStartedRecord))]
[JsonSerializable(typeof(DiagnosticsRecord))]
[JsonSerializable(typeof(StageCompletedRecord))]
[JsonSerializable(typeof(CompletedRecord))]
internal partial class CompileJsonLinesContext : JsonSerializerContext
{
    /// <summary>The context with relaxed escaping, like the report.</summary>
    public static CompileJsonLinesContext Relaxed { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}

/// <summary>
/// Writes compile events as JSON Lines (STRM-2): one compact UTF-8 JSON object per line, each with an
/// <c>event</c> member, flushed when the event occurs. The <c>completed</c> record is written last, once.
/// </summary>
public sealed class JsonLinesWriter(Stream output)
{
    private static readonly byte[] NewLine = "\n"u8.ToArray();

    /// <summary>Writes the record of one stream event. A <see cref="CompileEvent.Completed"/> event writes nothing:
    /// its record needs the folded report, which <see cref="WriteCompletedAsync"/> writes.</summary>
    public ValueTask WriteEventAsync(CompileEvent compileEvent, CancellationToken ct) => compileEvent switch
    {
        CompileEvent.StageStarted started => WriteAsync(JsonSerializer.SerializeToUtf8Bytes(
            new StageStartedRecord("stageStarted", started.Stage.WireValue()), CompileJsonLinesContext.Relaxed.StageStartedRecord), ct),
        CompileEvent.DiagnosticsReported reported => WriteAsync(JsonSerializer.SerializeToUtf8Bytes(
            new DiagnosticsRecord("diagnostics", reported.Stage.WireValue(), reported.DocumentUri,
                [.. reported.Diagnostics.Select(CompileReportDiagnostic.From)]), CompileJsonLinesContext.Relaxed.DiagnosticsRecord), ct),
        CompileEvent.StageCompleted completed => WriteAsync(JsonSerializer.SerializeToUtf8Bytes(
            new StageCompletedRecord("stageCompleted", completed.Stage.WireValue(), completed.Passed), CompileJsonLinesContext.Relaxed.StageCompletedRecord), ct),
        _ => ValueTask.CompletedTask
    };

    /// <summary>Writes the last record, which holds the same report that <c>--json</c> writes.</summary>
    public ValueTask WriteCompletedAsync(CompileReport report, CancellationToken ct) =>
        WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new CompletedRecord("completed", report), CompileJsonLinesContext.Relaxed.CompletedRecord), ct);

    private async ValueTask WriteAsync(byte[] line, CancellationToken ct)
    {
        await output.WriteAsync(line, ct);
        await output.WriteAsync(NewLine, ct);
        await output.FlushAsync(ct);
    }

    /// <summary>The UTF-8 text of <paramref name="report"/> as one compact line, without the line end.</summary>
    public static string CompletedLine(CompileReport report) =>
        Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(new CompletedRecord("completed", report), CompileJsonLinesContext.Relaxed.CompletedRecord));
}
