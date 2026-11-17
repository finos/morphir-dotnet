// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The checks of the two machine-output forms of a command (adopt-streaming-json, test-driver-contract).
/// <c>--json=document</c> writes the report as exactly one JSON document. <c>--json</c> writes JSON Lines: each line
/// one JSON object with an <c>event</c> member, and exactly one <c>{"event":"completed","report":…}</c> record, on the
/// last line. Each failure is a <see cref="CommandAssertionException"/> that names the line at fault.
/// </summary>
/// <remarks>The names of the convention are defined here: the testing library references no product project.</remarks>
public static class JsonOutputAssertions
{
    /// <summary>The member that names the event of a record.</summary>
    public const string EventMember = "event";

    /// <summary>The <c>event</c> of the last record of a stream.</summary>
    public const string CompletedEvent = "completed";

    /// <summary>The member of the <c>completed</c> record that holds the report.</summary>
    public const string ReportMember = "report";

    /// <summary>
    /// Standard output is a stream that ends with one <c>completed</c> record. Returns its report.
    /// </summary>
    public static JsonElement ReportOfStream(this CommandResult result) => result.ReportOfStream(maxDepth: 0);

    /// <inheritdoc cref="ReportOfStream(CommandResult)"/>
    /// <param name="result">The result of the command.</param>
    /// <param name="maxDepth">The depth that the parse of a line accepts; 0 is the default of the parser (64).</param>
    public static JsonElement ReportOfStream(this CommandResult result, int maxDepth)
    {
        var records = result.Records(maxDepth);
        var completed = records.Select((record, index) => (record, line: index + 1)).Where(item => IsCompleted(item.record)).ToArray();
        if (completed.Length == 0) throw new CommandAssertionException("The stream has no completed record.", result);
        if (completed.Length > 1)
            throw new CommandAssertionException(
                $"The stream has {completed.Length} completed records, on lines {string.Join(", ", completed.Select(item => item.line))}. It must have one.", result);
        var (last, line) = completed[0];
        if (line != records.Count)
            throw new CommandAssertionException(
                $"The completed record is on line {line}, not on the last line, {records.Count}: {LinesOf(result)[records.Count - 1]}", result);
        return last.TryGetProperty(ReportMember, out var report) && report.ValueKind == JsonValueKind.Object
            ? report
            : throw new CommandAssertionException($"The completed record on line {line} has no report object.", result);
    }

    /// <summary>
    /// Standard output is exactly one JSON document, with no text before or after it, and it is a report, not the
    /// <c>completed</c> record of a stream. Returns the document.
    /// </summary>
    public static JsonElement ReportOfDocument(this CommandResult result) => result.ReportOfDocument(maxDepth: 0);

    /// <inheritdoc cref="ReportOfDocument(CommandResult)"/>
    /// <param name="result">The result of the command.</param>
    /// <param name="maxDepth">The depth that the parse accepts; 0 is the default of the parser (64).</param>
    public static JsonElement ReportOfDocument(this CommandResult result, int maxDepth)
    {
        var document = result.OneDocument(maxDepth);
        return IsCompleted(document)
            ? throw new CommandAssertionException("Standard output is a completed record of a stream, not a report: the command streamed.", result)
            : document;
    }

    /// <summary>The records of a stream, in order, the <c>completed</c> record last. Each line must be one.</summary>
    public static IReadOnlyList<JsonElement> Events(this CommandResult result)
    {
        result.ReportOfStream();
        return result.Records(maxDepth: 0);
    }

    /// <summary>
    /// The report in either form: the <c>report</c> of a stream, or the one document of a command that has not
    /// moved to the stream. A driver reads its report through this, so it works before and after its command
    /// streams. A document is a stream when it has an <c>event</c> member, as the one line of a command with no
    /// events does. Output that is neither fails with the checks of the stream when its first line is a record,
    /// and with the checks of the document otherwise.
    /// </summary>
    /// <param name="result">The result of the command.</param>
    /// <param name="maxDepth">The depth that a parse accepts; 0 is the default of the parser (64).</param>
    public static JsonElement ReportOfEither(this CommandResult result, int maxDepth = 0)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardOutput) && TryParse(result.StandardOutput, maxDepth, out var document)
            && !(document.ValueKind == JsonValueKind.Object && document.TryGetProperty(EventMember, out _)))
            return document;
        var first = LinesOf(result).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "";
        return TryParse(first, maxDepth, out var record) && record.ValueKind == JsonValueKind.Object && record.TryGetProperty(EventMember, out _)
            ? result.ReportOfStream(maxDepth)
            : result.ReportOfDocument(maxDepth);
    }

    /// <summary>The one JSON document of standard output, of any kind. A parse failure names its line.</summary>
    internal static JsonElement OneDocument(this CommandResult result, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new CommandAssertionException("Standard output is empty, not one JSON document.", result);

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput, new JsonDocumentOptions { MaxDepth = maxDepth });
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            var place = error.LineNumber is { } index && index < LinesOf(result).Length
                ? $" At line {index + 1}: {LinesOf(result)[index]}."
                : "";
            throw new CommandAssertionException($"Standard output is not exactly one JSON document: {error.Message}{place}", result);
        }
    }

    private static List<JsonElement> Records(this CommandResult result, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new CommandAssertionException("Standard output is empty, not a JSON Lines stream.", result);

        var lines = LinesOf(result);
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var records = new List<JsonElement>(count);
        for (var index = 0; index < count; index++)
        {
            var line = lines[index];
            if (!TryParse(line, maxDepth, out var record))
                throw new CommandAssertionException($"Line {index + 1} of standard output is not JSON: {line}", result);
            if (record.ValueKind != JsonValueKind.Object || !record.TryGetProperty(EventMember, out var name) || name.ValueKind != JsonValueKind.String)
                throw new CommandAssertionException($"Line {index + 1} of standard output is not a JSON object with an event member: {line}", result);
            records.Add(record);
        }

        return records;
    }

    private static bool IsCompleted(JsonElement record) =>
        record.ValueKind == JsonValueKind.Object && record.TryGetProperty(EventMember, out var name)
        && name.ValueKind == JsonValueKind.String && name.GetString() == CompletedEvent;

    /// <summary>The lines of standard output, without their line ends (<c>\n</c> or <c>\r\n</c>).</summary>
    private static string[] LinesOf(CommandResult result) =>
        [.. result.StandardOutput.Split('\n').Select(line => line.EndsWith('\r') ? line[..^1] : line)];

    private static bool TryParse(string text, int maxDepth, out JsonElement element)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = maxDepth });
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }
}
