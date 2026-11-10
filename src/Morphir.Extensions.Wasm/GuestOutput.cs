// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace Morphir.Extensions.Wasm;

/// <summary>Where a guest output came from: the contract's <c>wasi.stdout</c>, <c>wasi.stderr</c>,
/// <c>logging.recorded</c> and <c>http.recorded</c> policies all make it guest output.</summary>
public enum GuestOutputSource
{
    /// <summary>Bytes the guest wrote to WASI stdout, fd 1.</summary>
    Stdout,

    /// <summary>Bytes the guest wrote to WASI stderr, fd 2.</summary>
    Stderr,

    /// <summary>A message of an Extism <c>log_info</c>, <c>log_warn</c> or <c>log_error</c>; <c>trace</c> and
    /// <c>debug</c> messages are dropped before they become output.</summary>
    Log,

    /// <summary>A message of <c>morphir_log</c>, whatever its level.</summary>
    MorphirLog,

    /// <summary>An <c>http_request</c> the host refused, with its method and URL.</summary>
    RefusedHttp,
}

/// <summary>
/// One guest output, as text a report shows: <c>stdout: …</c>, <c>stderr: …</c>, <c>log info: …</c>,
/// <c>morphir_log &lt;level&gt;: …</c> or <c>refused http_request &lt;method&gt; &lt;url&gt;: the guest has no network
/// access</c>, the forms the MoonBit worker reports. Bytes that are not UTF-8 are replaced with U+FFFD.
/// </summary>
/// <param name="Source">Where the output came from.</param>
/// <param name="Text">The text; when <paramref name="Cut"/>, its first part, ending with <c>…</c>.</param>
/// <param name="Cut">Whether the text was cut to <see cref="GuestOutputTail.MessageBytes"/>.</param>
public sealed record GuestOutput(GuestOutputSource Source, string Text, bool Cut = false);

/// <summary>An <c>http_request</c> the host refused: the method and the URL the guest asked for. No connection was
/// made (contract policy <c>http.request</c>, <c>stops-call-fails-session</c>).</summary>
/// <param name="Method">The request's <c>method</c>, or <c>GET</c> when it names none.</param>
/// <param name="Url">The request's <c>url</c>, or empty when it names none or is not a JSON object.</param>
public sealed record RefusedHttpRequest(string Method, string Url);

/// <summary>What <see cref="GuestOutputTail.Drain"/> took: the kept outputs, oldest first, and the UTF-8 bytes of
/// output text that were dropped.</summary>
public sealed record GuestOutputDrain(IReadOnlyList<GuestOutput> Outputs, long DroppedBytes);

/// <summary>
/// The guest output of one session, bounded (the spec's "Guest output stays out of results"). It keeps the newest
/// outputs whose reported text is at most <see cref="TailBytes"/>, each cut to <see cref="MessageBytes"/> and ending
/// with <c>…</c>, and counts in <see cref="DroppedBytes"/> the UTF-8 bytes of output text it did not keep: the oldest
/// outputs above the bound and the cut ends. Memory stays bounded whatever the guest writes. The sizes and the rule
/// are the MoonBit worker's (its <c>OutputTail</c>, <c>OUTPUT_TAIL_BYTES</c> and <c>EVENT_MESSAGE_BYTES</c>), which
/// task 4.1's <c>output</c> events follow; the pinned contract fixes the <c>droppedBytes</c> member and no size.
/// Reported text is counted as JSON string text, where a control character takes six bytes.
/// </summary>
/// <remarks>
/// Nothing here is written to the process's stdout or stderr. The worker (task 4.1) drains the tail after each call
/// and reports it as <c>output</c> events; a session that is not drained keeps the newest outputs of all its calls.
/// The tail is safe to read from another thread than the one the guest's calls run on.
/// </remarks>
public sealed class GuestOutputTail
{
    /// <summary>The most bytes of reported output text the tail keeps: 64 KiB, the MoonBit worker's
    /// <c>OUTPUT_TAIL_BYTES</c>.</summary>
    public const int TailBytes = 65536;

    /// <summary>The most bytes of one output's reported text: 4 KiB, the MoonBit worker's
    /// <c>EVENT_MESSAGE_BYTES</c>. A longer output is cut and ends with <c>…</c>.</summary>
    public const int MessageBytes = 4096;

    /// <summary>The most bytes of one payload the host reads. Each payload byte gives at least one byte of reported
    /// text, so a payload this long is always cut, and the bytes after it would be dropped anyway.</summary>
    internal const int PayloadBytes = MessageBytes;

    const string CutMarker = "…";

    readonly object gate = new();
    readonly Queue<(GuestOutput Output, int Cost, long Shown)> items = new();
    int kept;
    long dropped;

    /// <summary>The kept outputs, oldest first.</summary>
    public IReadOnlyList<GuestOutput> Outputs
    {
        get
        {
            lock (gate)
            {
                return items.Select(item => item.Output).ToArray();
            }
        }
    }

    /// <summary>The UTF-8 bytes of output text dropped since the tail was last drained.</summary>
    public long DroppedBytes
    {
        get
        {
            lock (gate)
            {
                return dropped;
            }
        }
    }

    /// <summary>The reported bytes of the kept outputs, at most <see cref="TailBytes"/>.</summary>
    public int KeptBytes
    {
        get
        {
            lock (gate)
            {
                return kept;
            }
        }
    }

    /// <summary>The bytes <paramref name="text"/> takes as the text of a JSON string: a quote or a backslash takes
    /// two, a control character six (its <c>\u</c> escape), and any other character its UTF-8 length.</summary>
    public static int ReportedBytes(string text)
    {
        var total = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            total += RuneCost(rune);
        }

        return total;
    }

    static int RuneCost(Rune rune) => rune.Value switch
    {
        0x22 or 0x5c => 2,
        < 0x20 => 6,
        _ => rune.Utf8SequenceLength,
    };

    /// <summary>Adds one output, cut to <see cref="MessageBytes"/>, and drops the oldest outputs above
    /// <see cref="TailBytes"/>.</summary>
    public void Add(GuestOutput output) => Add(output, 0);

    /// <summary>
    /// Adds an output whose text is the start of a longer one: <paramref name="elidedBytes"/> more bytes followed
    /// it and were never read, and count as dropped. The host reads at most <see cref="PayloadBytes"/> of a payload,
    /// so a guest that writes 200 MiB at once costs the host no more than one that writes 4 KiB.
    /// </summary>
    internal void Add(GuestOutput output, long elidedBytes)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegative(elidedBytes);
        var text = output.Text;
        var size = Encoding.UTF8.GetByteCount(text) + elidedBytes;
        var (shownText, wasCut) = CutText(text);
        var shown = wasCut ? Encoding.UTF8.GetByteCount(shownText) - Encoding.UTF8.GetByteCount(CutMarker) : size - elidedBytes;
        var cost = ReportedBytes(shownText);
        var item = wasCut ? output with { Text = shownText, Cut = true } : output;
        lock (gate)
        {
            dropped += size - shown;
            items.Enqueue((item, cost, shown));
            kept += cost;
            while (kept > TailBytes && items.TryDequeue(out var oldest))
            {
                kept -= oldest.Cost;
                dropped += oldest.Shown;
            }
        }
    }

    /// <summary>The text cut at a character boundary to at most <see cref="MessageBytes"/> of JSON text, with
    /// <c>…</c> at the end, and whether it was cut.</summary>
    static (string Text, bool Cut) CutText(string text)
    {
        if (ReportedBytes(text) <= MessageBytes)
        {
            return (text, false);
        }

        var budget = MessageBytes - ReportedBytes(CutMarker);
        var keptText = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cost = RuneCost(rune);
            if (bytes + cost > budget)
            {
                break;
            }

            bytes += cost;
            keptText.Append(rune.ToString());
        }

        return (keptText.Append(CutMarker).ToString(), true);
    }

    /// <summary>Takes the kept outputs and the dropped count, and empties the tail.</summary>
    public GuestOutputDrain Drain()
    {
        lock (gate)
        {
            var drained = new GuestOutputDrain(items.Select(item => item.Output).ToArray(), dropped);
            items.Clear();
            kept = 0;
            dropped = 0;
            return drained;
        }
    }
}
