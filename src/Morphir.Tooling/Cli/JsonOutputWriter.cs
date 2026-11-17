// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Morphir.Tooling.Cli;

/// <summary>
/// The machine output of one command run (adopt-streaming-json, decisions JSON-2 and JSON-4). In
/// <see cref="JsonOutputMode.Stream"/> each event is one compact UTF-8 JSON object with an <c>event</c> member, on one
/// line that ends with <c>\n</c>, flushed when it is written; the last line is the one
/// <c>{"event":"completed","report":…}</c> record. In <see cref="JsonOutputMode.Document"/> events write nothing and
/// the report is written alone, as <paramref name="reportInfo"/> writes it (indented when its context is).
/// </summary>
/// <remarks>
/// Serialization goes only through the source-generated <paramref name="reportInfo"/> (Native AOT). Event members
/// and the completed record use the encoder of its options, so a relaxed context keeps quotes readable in both modes.
/// </remarks>
/// <typeparam name="TReport">The report type of the command.</typeparam>
public sealed class JsonOutputWriter<TReport>
{
    /// <summary>The <c>event</c> name of the last record.</summary>
    public const string CompletedEvent = "completed";

    private readonly TextWriter _stdout;
    private readonly JsonTypeInfo<TReport> _reportInfo;
    private readonly JsonWriterOptions _lineOptions;
    private bool _completed;

    /// <summary>A writer of <paramref name="mode"/> on <paramref name="stdout"/>, which must be a JSON mode.</summary>
    public JsonOutputWriter(TextWriter stdout, JsonOutputMode mode, JsonTypeInfo<TReport> reportInfo)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(reportInfo);
        if (mode is not (JsonOutputMode.Stream or JsonOutputMode.Document))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "A JSON output writer needs the stream or the document mode.");
        _stdout = stdout;
        Mode = mode;
        _reportInfo = reportInfo;
        _lineOptions = new JsonWriterOptions
        {
            Encoder = reportInfo.Options.Encoder,
            Indented = false,
            MaxDepth = reportInfo.Options.MaxDepth,
        };
    }

    /// <summary>The mode of this writer.</summary>
    public JsonOutputMode Mode { get; }

    /// <summary>
    /// In stream mode, writes one record <c>{"event":<paramref name="name"/>,…}</c> whose other members
    /// <paramref name="members"/> writes, and flushes it. In document mode, writes nothing.
    /// </summary>
    public void Event(string name, Action<Utf8JsonWriter> members)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(members);
        if (name == CompletedEvent)
            throw new ArgumentException($"The {CompletedEvent} record is written by {nameof(Completed)}.", nameof(name));
        ThrowIfCompleted();
        if (Mode != JsonOutputMode.Stream) return;
        WriteLine(Line(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("event", name);
            members(writer);
            writer.WriteEndObject();
        }));
    }

    /// <summary>
    /// Writes the last output, once: in stream mode the <c>completed</c> record with <paramref name="report"/> under
    /// <c>report</c>; in document mode the report alone. Both carry the same report.
    /// </summary>
    public void Completed(TReport report)
    {
        ThrowIfCompleted();
        _completed = true;
        if (Mode == JsonOutputMode.Document)
        {
            WriteLine(JsonSerializer.Serialize(report, _reportInfo));
            return;
        }

        // The report is serialized at depth 0, as in a document, so the record adds no level to its depth limit.
        var body = Utf8(writer => JsonSerializer.Serialize(writer, report, _reportInfo));
        WriteLine(Line(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("event", CompletedEvent);
            writer.WritePropertyName("report");
            writer.WriteRawValue(body.WrittenSpan, skipInputValidation: true);
            writer.WriteEndObject();
        }));
    }

    private void ThrowIfCompleted()
    {
        if (_completed) throw new InvalidOperationException($"The {CompletedEvent} output was written; nothing follows it.");
    }

    private string Line(Action<Utf8JsonWriter> write) => Encoding.UTF8.GetString(Utf8(write).WrittenSpan);

    private ArrayBufferWriter<byte> Utf8(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, _lineOptions))
            write(writer);
        return buffer;
    }

    private void WriteLine(string text)
    {
        _stdout.Write(text);
        _stdout.Write('\n');
        _stdout.Flush();
    }
}
