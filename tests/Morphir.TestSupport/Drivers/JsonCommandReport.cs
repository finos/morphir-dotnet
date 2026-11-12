// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>One diagnostic of a JSON command report.</summary>
/// <param name="Location">The <c>location</c> of the diagnostic: a text, or the <c>uri</c> of a location object.</param>
public sealed record ReportDiagnostic(string Code, string Message, string? Severity, string? Location);

/// <summary>
/// The parts that the JSON reports of the command families share: one JSON document on standard output, a field
/// <c>exitCode</c> that agrees with the exit code of the process, and a list <c>diagnostics</c>. A report type of a
/// command derives from it and adds the fields of its command.
/// </summary>
public abstract class JsonCommandReport
{
    /// <summary>The depth of a report that a parse accepts. A configuration report can nest more than 64 tables.</summary>
    public const int MaxDepth = 256;

    protected JsonCommandReport(CommandResult result, string name)
    {
        Result = result;
        Name = name;
        Document = result.ShouldWriteOneJsonDocument(MaxDepth);
        if (Document.ValueKind != JsonValueKind.Object)
            throw new CommandAssertionException($"The {name} report is not a JSON object.", result);
        if (Document.TryGetProperty("exitCode", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != result.ExitCode)
            throw new CommandAssertionException($"The {name} report disagrees with the command: exitCode is {code.GetInt32()}, but the exit code is {result.ExitCode}.", result);

        Diagnostics = Document.TryGetProperty("diagnostics", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(Diagnostic)]
            : [];
    }

    /// <summary>The result of the command that wrote the report.</summary>
    public CommandResult Result { get; }

    /// <summary>The report as one JSON object, for a check of a field that this type does not name.</summary>
    public JsonElement Document { get; }

    public IReadOnlyList<ReportDiagnostic> Diagnostics { get; }

    /// <summary>The name of the report in a failure message, for example <c>compilation</c>.</summary>
    protected string Name { get; }

    /// <summary>The report has a diagnostic with <paramref name="code"/>.</summary>
    public void ShouldHaveDiagnostic(string code) =>
        Check(Diagnostics.Any(diagnostic => diagnostic.Code == code), $"The {Name} report has no diagnostic {code}. It has: {DescribeDiagnostics()}.");

    /// <summary>The report has no diagnostic with <paramref name="code"/>.</summary>
    public void ShouldNotHaveDiagnostic(string code) =>
        Check(Diagnostics.All(diagnostic => diagnostic.Code != code), $"The {Name} report has the diagnostic {code}.");

    /// <summary>A diagnostic of the report has a message that contains <paramref name="text"/>, with the same case.</summary>
    public void ShouldHaveDiagnosticThatNames(string text) =>
        Check(Diagnostics.Any(diagnostic => diagnostic.Message.Contains(text, StringComparison.Ordinal)),
            $"No diagnostic of the {Name} report names '{text}'. It has: {DescribeDiagnostics()}.");

    /// <summary>The text of a field, by a path of member names, or null when a member is not there or is not a text.</summary>
    public string? Text(params string[] path) => Member(path) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>The field at a path of member names, or null when a member is not there.</summary>
    public JsonElement? Member(params string[] path)
    {
        var current = Document;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
        }

        return current;
    }

    /// <summary>The field at a path of member names; a field that is not there is a failure of the check.</summary>
    public JsonElement Required(params string[] path) =>
        Member(path) ?? throw new CommandAssertionException($"The {Name} report has no field {string.Join('.', path)}.", Result);

    /// <summary>Throws a <see cref="CommandAssertionException"/> with <paramref name="reason"/> when <paramref name="condition"/> is false.</summary>
    public void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, Result);
    }

    protected string DescribeDiagnostics() =>
        Diagnostics.Count == 0 ? "no diagnostic" : string.Join("; ", Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static ReportDiagnostic Diagnostic(JsonElement item)
    {
        string? Field(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var location = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("location", out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : Field(value, "uri")
            : null;
        return new ReportDiagnostic(Field(item, "code") ?? "", Field(item, "message") ?? "", Field(item, "severity"), location);
    }
}
