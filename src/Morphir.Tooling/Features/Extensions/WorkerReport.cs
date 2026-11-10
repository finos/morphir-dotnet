// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Morphir.Extensions.Wasm;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The worker's report on stderr (task 4.1). With <c>--json</c> each event is one <c>morphir/command-event</c> line:
/// the envelope the MoonBit worker writes (<c>profile</c>, <c>contractVersion</c>, <c>command</c>, <c>sequence</c>,
/// <c>event</c>) and the members the pinned contract's <c>worker.events</c> gives the event, and no other. Without it
/// each event is one text line that starts with <c>morphir extension worker: </c>. A message in an event (of an
/// output, a fault or a refusal) is cut to 4 KiB of JSON text and then ends with <c>…</c>, as MoonBit cuts it, so that
/// the last fault line always fits whole in a 64 KiB stderr tail. Every line is written with
/// <see cref="Utf8JsonWriter"/>: no reflection.
/// </summary>
public sealed class WorkerReport
{
    /// <summary>The profile of an event line.</summary>
    public const string EventProfile = "morphir/command-event";

    /// <summary>The profile of the help result and of a usage failure.</summary>
    public const string ResultProfile = "morphir/command-result";

    /// <summary>The version of the command-event and command-result profiles that the MoonBit worker writes.</summary>
    public const string ProfileVersion = "0.1.0";

    const string TextPrefix = "morphir " + WorkerInvocation.Command + ": ";
    const string CutMarker = "…";

    static readonly JsonWriterOptions WriterOptions = new()
    {
        // Non-ASCII text stays UTF-8, as the 4 KiB cut counts it; quotes, backslashes and control characters are
        // escaped. These lines are never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    readonly Stream sink;
    readonly bool json;
    int sequence;

    /// <summary>A report written to <paramref name="sink"/>, as JSON lines when <paramref name="json"/>.</summary>
    public WorkerReport(Stream sink, bool json)
    {
        ArgumentNullException.ThrowIfNull(sink);
        this.sink = sink;
        this.json = json;
    }

    /// <summary>The module has loaded: its declared profile, the SHA-256 of its file, this worker's build identity and
    /// the bounds it enforces. All three bounds are enforced by the engine.</summary>
    public void Started(WasmAbi abi, string moduleSha256, string buildIdentity) =>
        Emit("started",
            writer =>
            {
                writer.WriteString("abi", abi.Id);
                writer.WriteString("moduleSha256", moduleSha256);
                writer.WriteString("buildIdentity", buildIdentity);
                writer.WriteStartObject("bounds");
                foreach (var bound in new[] { "memoryCeiling", "instructionBudget", "callDeadline" })
                {
                    writer.WriteStartObject(bound);
                    writer.WriteBoolean("enforced", true);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            },
            () => $"serving {abi} module {moduleSha256}");

    /// <summary>One guest output, as text.</summary>
    public void Output(string text) =>
        Emit("output", writer => writer.WriteString("output", Cut(text)), () => Cut(text));

    /// <summary>The bytes of guest output of one call that were not reported.</summary>
    public void Dropped(long bytes)
    {
        var text = $"{bytes.ToString(CultureInfo.InvariantCulture)} bytes of guest output were dropped";
        Emit("output",
            writer =>
            {
                writer.WriteString("output", text);
                writer.WriteNumber("droppedBytes", bytes);
            },
            () => text);
    }

    /// <summary>The drained guest output of a call: each kept output, then the dropped count when there is one.</summary>
    public void Outputs(GuestOutputDrain drain)
    {
        ArgumentNullException.ThrowIfNull(drain);
        foreach (var output in drain.Outputs) Output(output.Text);
        if (drain.DroppedBytes > 0) Dropped(drain.DroppedBytes);
    }

    /// <summary>The guest stopped serving: the contract's fault kind and a message. A fault has no code.</summary>
    public void Fault(string kind, string? message) =>
        Emit("fault",
            writer =>
            {
                writer.WriteStartObject("fault");
                writer.WriteString("kind", kind);
                if (message is not null) writer.WriteString("message", Cut(message));
                writer.WriteEndObject();
            },
            () => message is null ? $"fault {kind}" : $"fault {kind}: {Cut(message)}");

    /// <summary>
    /// The worker refused to serve before any request, with one of the contract's refusal codes: a module refusal
    /// (<c>refusals</c>) or one of the worker's own (<c>worker.refusals</c>: another build identity, or a host without
    /// a usable engine).
    /// </summary>
    public void Refused(string code, string message) =>
        Emit("refused",
            writer =>
            {
                writer.WriteStartObject("error");
                writer.WriteString("code", code);
                writer.WriteString("message", Cut(message));
                writer.WriteEndObject();
            },
            () => $"refused {code}: {Cut(message)}");

    /// <summary>The worker exits with <paramref name="exitCode"/>, which is returned.</summary>
    public int Exited(int exitCode)
    {
        Emit("exited", writer => writer.WriteNumber("exitCode", exitCode), () => $"exit {exitCode.ToString(CultureInfo.InvariantCulture)}");
        return exitCode;
    }

    void Emit(string name, Action<Utf8JsonWriter> members, Func<string> text)
    {
        byte[] line;
        if (json)
        {
            sequence++;
            line = Document(writer =>
            {
                writer.WriteString("profile", EventProfile);
                writer.WriteString("contractVersion", ProfileVersion);
                writer.WriteString("command", WorkerInvocation.Command);
                writer.WriteNumber("sequence", sequence);
                writer.WriteString("event", name);
                members(writer);
            });
        }
        else
        {
            line = Encoding.UTF8.GetBytes(TextLine(text()) + "\n");
        }

        sink.Write(line);
        sink.Flush();
    }

    /// <summary>One text line of the report: the prefix and the text, with line breaks written as <c>\n</c> and
    /// <c>\r</c>, so that one event stays one line.</summary>
    public static string TextLine(string text) =>
        TextPrefix + text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="text"/> cut at a character boundary to at most 4 KiB (<see cref="GuestOutputTail.MessageBytes"/>)
    /// of JSON string text, ending with <c>…</c> when it was cut. A quote or a backslash counts two bytes and a control
    /// character six, as <see cref="GuestOutputTail.ReportedBytes"/> counts them.
    /// </summary>
    public static string Cut(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (GuestOutputTail.ReportedBytes(text) <= GuestOutputTail.MessageBytes) return text;
        var budget = GuestOutputTail.MessageBytes - GuestOutputTail.ReportedBytes(CutMarker);
        var kept = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cost = GuestOutputTail.ReportedBytes(rune.ToString());
            if (bytes + cost > budget) break;
            bytes += cost;
            kept.Append(rune.ToString());
        }

        return kept.Append(CutMarker).ToString();
    }

    /// <summary>The <c>morphir/command-result</c> of <c>--help --json</c>, as one line: the help text and
    /// <c>result.buildIdentity</c>.</summary>
    public static byte[] HelpResult(string help, string buildIdentity) => Document(writer =>
    {
        ResultEnvelope(writer, "succeeded", 0);
        writer.WriteStartObject("result");
        writer.WriteString("help", help);
        writer.WriteString("buildIdentity", buildIdentity);
        writer.WriteEndObject();
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
    });

    /// <summary>The <c>morphir/command-result</c> failure of a usage error, as one line: <c>error.code</c>
    /// <c>usage</c> and exit status 2. The worker writes it on stderr, because stdout is the frame stream.</summary>
    public static byte[] UsageFailure(string message, int exitCode) => Document(writer =>
    {
        ResultEnvelope(writer, "failed", exitCode);
        writer.WriteStartObject("error");
        writer.WriteString("code", "usage");
        writer.WriteString("message", Cut(message));
        writer.WriteEndObject();
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
    });

    /// <summary>The text line of a usage error, without <c>--json</c>.</summary>
    public static byte[] UsageText(string message) => Encoding.UTF8.GetBytes(TextLine(message) + "\n");

    static void ResultEnvelope(Utf8JsonWriter writer, string state, int exitCode)
    {
        writer.WriteString("profile", ResultProfile);
        writer.WriteString("contractVersion", ProfileVersion);
        writer.WriteString("command", WorkerInvocation.Command);
        writer.WriteString("state", state);
        writer.WriteNumber("exitCode", exitCode);
    }

    /// <summary>One JSON object with these members, followed by a line feed.</summary>
    static byte[] Document(Action<Utf8JsonWriter> members)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            members(writer);
            writer.WriteEndObject();
        }

        return [.. buffer.WrittenSpan, (byte)'\n'];
    }
}
