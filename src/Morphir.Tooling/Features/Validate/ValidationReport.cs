using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Validate;

/// <summary>
/// The report of <c>morphir validate</c>: one JSON document with <c>--json</c>, or a table and a summary line. It is
/// built only by <see cref="From(HostIdentity, string, AnalysisResult)"/> and the failure factories,
/// which are the one place that turns typed values into text.
/// </summary>
public sealed record ValidationReport(string Host, bool Success, int ExitCode, string? Input, string? Family, string? FormatVersion,
    IReadOnlyList<ValidationAnalyzer> Analyzers, IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    /// <summary>The report of an analysis. Exit code 2 when an analyzer failed, 1 for an error diagnostic, else 0.</summary>
    public static ValidationReport From(HostIdentity host, string input, AnalysisResult result)
    {
        var exitCode = result.HasFailures ? 2 : result.HasErrors ? 1 : 0;
        var diagnostics = result.HostDiagnostics.Select(diagnostic => ValidationDiagnostic.From(diagnostic, null))
            .Concat(result.Diagnostics.Select(item => ValidationDiagnostic.From(item.Diagnostic, item.Extension.Value)))
            .ToArray();
        var analyzers = result.Runs
            .Select(run => new ValidationAnalyzer(run.Analyzer.Value, run.Outcome.WireValue, run.Outcome.ReasonOrNull, run.Outcome.DetailOrNull))
            .ToArray();
        return new(host.Host.WireValue, exitCode == 0, exitCode, input, result.Family, result.FormatVersion, analyzers, diagnostics);
    }

    /// <summary>A run that stopped before any analyzer ran, with one diagnostic.</summary>
    public static ValidationReport Failed(HostIdentity host, int exitCode, string? input, string code, string message) =>
        new(host.Host.WireValue, false, exitCode, input, null, null, [], [new ValidationDiagnostic("error", code, message, null, null, null, null)]);

    /// <summary>A run that stopped at a loader or configuration failure. A cancelled load exits with 130, any other with 2.</summary>
    public static ValidationReport Failed(HostIdentity host, string input, GenerationFailure failure) =>
        new(host.Host.WireValue, false, failure.Kind == GenerationFailureKind.Cancelled ? 130 : 2, input, null, null, [],
            failure.Diagnostics.Select(diagnostic => new ValidationDiagnostic(diagnostic.Severity, diagnostic.Code, Located(diagnostic),
                null, null, null, null)).ToArray());

    /// <summary>The path and the location before the message, so a diagnostic names the file that it is about.</summary>
    private static string Located(GenerationDiagnostic diagnostic) => (diagnostic.Path, diagnostic.Location) switch
    {
        ({ } path, { } location) when location != "$" => $"{path} at {location}: {diagnostic.Message}",
        ({ } path, _) => $"{path}: {diagnostic.Message}",
        _ => diagnostic.Message
    };

    /// <summary>True when the run stopped before analysis: there is no model, so the text output goes to stderr.</summary>
    [JsonIgnore]
    public bool Stopped => Family is null;

    public string ToJson() => JsonSerializer.Serialize(this, ValidationJsonContext.Default.ValidationReport);

    /// <summary>The text output: a table of the diagnostics, a line for each analyzer that did not run, and a summary line.</summary>
    public string ToText(TerminalProfile terminal)
    {
        var lines = new List<string>();
        if (Diagnostics.Count > 0)
            lines.Add(TerminalTable.Render(["SEVERITY", "CODE", "LOCATION", "MESSAGE", "EXTENSION"],
                Diagnostics.Select(diagnostic => (IReadOnlyList<string>)
                    [diagnostic.Severity, diagnostic.Code ?? "", diagnostic.Location?.ToString() ?? "", diagnostic.Message, diagnostic.Extension ?? ""]),
                terminal));
        foreach (var analyzer in Analyzers.Where(analyzer => analyzer.Status != "ran"))
            lines.Add(analyzer.Detail is { } detail ? $"{analyzer.Id}: {analyzer.Status} ({detail})" : $"{analyzer.Id}: not run ({analyzer.Reason})");
        int Count(string severity) => Diagnostics.Count(diagnostic => diagnostic.Severity == severity);
        int Runs(string status) => Analyzers.Count(analyzer => analyzer.Status == status);
        static string Plural(int count, string word) => count == 1 ? $"{count} {word}" : $"{count} {word}s";
        lines.Add($"{Plural(Count("error"), "error")}, {Plural(Count("warning"), "warning")}, {Count("info")} info, {Plural(Count("hint"), "hint")}. " +
            $"Analyzers: {Runs("ran")} ran, {Runs("not-run")} not run, {Runs("failed")} failed.");
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>One analyzer: <c>ran</c>, <c>failed</c> (with a detail) or <c>not-run</c> (with a reason).</summary>
public sealed record ValidationAnalyzer(string Id, string Status, string? Reason, string? Detail);

/// <summary>
/// A diagnostic in the MEP <c>Diagnostic</c> shape, with the id of the extension that reported it. A diagnostic of the
/// command itself (an unreadable input, no analyzer for the model's family) has no extension.
/// </summary>
public sealed record ValidationDiagnostic(string Severity, string? Code, string Message, ValidationLocation? Location,
    IReadOnlyList<ValidationRelated>? Related, JsonElement? Data, string? Extension)
{
    internal static ValidationDiagnostic From(Morphir.Mep.Diagnostic diagnostic, string? extension) =>
        new(diagnostic.Severity.WireValue, diagnostic.CodeOrNull(), diagnostic.Message, ValidationLocation.From(diagnostic.LocationOrNull()),
            diagnostic.Related.Count == 0 ? null
                : diagnostic.Related.Select(related => new ValidationRelated(ValidationLocation.From(related.Location)!, related.Message)).ToArray(),
            diagnostic.DataOrNull(), extension);
}

/// <summary>A range in a document, identified by URI. Positions are zero-based; <c>character</c> counts UTF-16 code units.</summary>
public sealed record ValidationLocation(string Uri, ValidationRange Range)
{
    internal static ValidationLocation? From(Morphir.Mep.SourceLocation? location) => location is null ? null
        : new(location.Uri, new(new(location.Range.Start.Line, location.Range.Start.Character), new(location.Range.End.Line, location.Range.End.Character)));

    /// <summary>The URI and the one-based start position, as people read it.</summary>
    public override string ToString() => $"{Uri}:{Range.Start.Line + 1}:{Range.Start.Character + 1}";
}

public sealed record ValidationRange(ValidationPosition Start, ValidationPosition End);

public sealed record ValidationPosition(uint Line, uint Character);

public sealed record ValidationRelated(ValidationLocation Location, string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ValidationReport))]
internal partial class ValidationJsonContext : JsonSerializerContext;
