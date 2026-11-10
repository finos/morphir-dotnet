// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>One line of the worker's stderr, with when the test read it.</summary>
public sealed record WorkerStderrLine(TimeSpan At, string Text);

/// <summary>
/// A running <c>&lt;host&gt; extension worker</c> process, driven the way a conformance runner drives it: frames written
/// to stdin, which stays open until the worker exits; every stdout byte kept; stderr kept line by line with the time
/// each line was read. The process has no window (<c>CreateNoWindow</c>, no shell) and counts against the shared
/// <see cref="ProcessGate"/>. Disposing it stops the process tree if it is still alive.
/// </summary>
public sealed class WorkerProcess : IAsyncDisposable
{
    readonly Process process;
    readonly IDisposable slot;
    readonly MemoryStream stdout = new();
    readonly List<WorkerStderrLine> stderr = [];
    readonly Task readStdout;
    readonly Task readStderr;
    readonly Stopwatch clock;
    long peakWorkingSet;

    WorkerProcess(Process process, IDisposable slot, Stopwatch clock)
    {
        this.process = process;
        this.slot = slot;
        this.clock = clock;
        readStdout = Task.Run(ReadStdout);
        readStderr = Task.Run(ReadStderr);
    }

    /// <summary>The worker's process id.</summary>
    public int Id => process.Id;

    /// <summary>Time since the worker started.</summary>
    public TimeSpan Elapsed => clock.Elapsed;

    /// <summary>Starts <c>executable extension worker arguments...</c>, with <paramref name="environment"/> added to the
    /// inherited environment.</summary>
    public static async Task<WorkerProcess> StartAsync(string executable, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        info.ArgumentList.Add("extension");
        info.ArgumentList.Add("worker");
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>()) info.Environment[name] = value;

        var slot = await ProcessGate.Shared.EnterAsync();
        var process = new Process { StartInfo = info };
        process.Start();
        return new WorkerProcess(process, slot, Stopwatch.StartNew());
    }

    /// <summary>A Content-Length frame of the UTF-8 of <paramref name="json"/>.</summary>
    public static byte[] Frame(string json) => Frame(Encoding.UTF8.GetBytes(json));

    /// <summary>A Content-Length frame of <paramref name="body"/>.</summary>
    public static byte[] Frame(byte[] body) => [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];

    /// <summary>Writes frames to stdin and flushes, without closing it. Returns when the last byte was written; a
    /// worker that stopped reading ends the write quietly.</summary>
    public async Task WriteAsync(params byte[][] frames)
    {
        try
        {
            foreach (var frame in frames)
            {
                await process.StandardInput.BaseStream.WriteAsync(frame);
                await process.StandardInput.BaseStream.FlushAsync();
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
            // The worker exited and closed its end.
        }
    }

    /// <summary>Waits until a stderr line satisfies <paramref name="match"/>, the worker exits, or the timeout passes.</summary>
    public async Task<bool> WaitForLineAsync(Func<string, bool> match, TimeSpan timeout)
    {
        var deadline = clock.Elapsed + timeout;
        while (clock.Elapsed < deadline)
        {
            if (StderrLines.Any(line => match(line.Text))) return true;
            if (process.HasExited) return StderrLines.Any(line => match(line.Text));
            SampleMemory();
            await Task.Delay(10);
        }

        return false;
    }

    /// <summary>Waits until <paramref name="condition"/> holds, the worker exits, or the timeout passes.</summary>
    public async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = clock.Elapsed + timeout;
        while (clock.Elapsed < deadline)
        {
            if (condition()) return true;
            if (process.HasExited) return condition();
            SampleMemory();
            await Task.Delay(10);
        }

        return false;
    }

    /// <summary>Waits for the worker to exit by itself. A worker still alive at the timeout has its process tree
    /// stopped, and the result says so. Stdin is closed only after the worker has exited.</summary>
    public async Task<WorkerRun> WaitForExitAsync(TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        var stopped = false;
        try
        {
            while (!process.HasExited)
            {
                SampleMemory();
                await Task.Delay(10, deadline.Token);
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            Kill();
        }

        await process.WaitForExitAsync();
        var exitedAt = clock.Elapsed;
        await Task.WhenAny(Task.WhenAll(readStdout, readStderr), Task.Delay(TimeSpan.FromSeconds(5)));
        CloseInput();
        return new WorkerRun(
            stopped ? null : process.ExitCode,
            Stdout,
            StderrLines,
            exitedAt,
            process.ExitTime.ToUniversalTime(),
            peakWorkingSet);
    }

    /// <summary>Every stdout byte so far.</summary>
    public byte[] Stdout
    {
        get
        {
            lock (stdout) return stdout.ToArray();
        }
    }

    /// <summary>Every stderr line so far.</summary>
    public IReadOnlyList<WorkerStderrLine> StderrLines
    {
        get
        {
            lock (stderr) return [.. stderr];
        }
    }

    /// <summary>The largest working set seen while the worker ran, in bytes, or 0 where the platform does not say.</summary>
    public long PeakWorkingSet
    {
        get
        {
            SampleMemory();
            return Interlocked.Read(ref peakWorkingSet);
        }
    }

    void SampleMemory()
    {
        try
        {
            process.Refresh();
            if (process.HasExited) return;
            var peak = Math.Max(process.PeakWorkingSet64, process.WorkingSet64);
            long seen;
            do
            {
                seen = Interlocked.Read(ref peakWorkingSet);
                if (peak <= seen) return;
            }
            while (Interlocked.CompareExchange(ref peakWorkingSet, peak, seen) != seen);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The process exited between the check and the read.
        }
    }

    async Task ReadStdout()
    {
        var buffer = new byte[16384];
        var stream = process.StandardOutput.BaseStream;
        int count;
        while ((count = await stream.ReadAsync(buffer)) > 0)
        {
            lock (stdout) stdout.Write(buffer, 0, count);
        }
    }

    async Task ReadStderr()
    {
        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            lock (stderr) stderr.Add(new WorkerStderrLine(clock.Elapsed, line));
        }
    }

    void CloseInput()
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
        }
    }

    void Kill()
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            Kill();
            await process.WaitForExitAsync();
        }

        CloseInput();
        process.Dispose();
        slot.Dispose();
    }
}

/// <summary>What one worker process did.</summary>
/// <param name="ExitCode">Its exit status, or null when the test had to stop it.</param>
/// <param name="Stdout">Every stdout byte.</param>
/// <param name="StderrLines">Every stderr line.</param>
/// <param name="ExitedAt">When the test saw it exit, from its start.</param>
/// <param name="ExitTimeUtc">When it exited, as the operating system recorded it.</param>
/// <param name="PeakWorkingSet">The largest working set seen while it ran, or 0.</param>
public sealed record WorkerRun(int? ExitCode, byte[] Stdout, IReadOnlyList<WorkerStderrLine> StderrLines, TimeSpan ExitedAt,
    DateTime ExitTimeUtc, long PeakWorkingSet)
{
    /// <summary>The stderr lines that are <c>morphir/command-event</c> objects, as (name, object, time read).</summary>
    public IReadOnlyList<(string Name, JsonElement Json, TimeSpan At)> Events => StderrLines
        .Select(line => (line, Json: TryParse(line.Text)))
        .Where(item => item.Json is { ValueKind: JsonValueKind.Object } json
            && json.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.String
            && profile.GetString() == "morphir/command-event"
            && json.TryGetProperty("event", out var name) && name.ValueKind == JsonValueKind.String)
        .Select(item => (item.Json!.Value.GetProperty("event").GetString()!, item.Json.Value, item.line.At))
        .ToArray();

    public IReadOnlyList<string> EventNames => Events.Select(e => e.Name).ToArray();

    /// <summary>The text of every <c>output</c> event, joined in order with nothing between them.</summary>
    public string GuestOutput => string.Concat(Events.Where(e => e.Name == "output")
        .Select(e => e.Json.TryGetProperty("output", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : ""));

    /// <summary>Stdout read as Content-Length frames, or the reason it is something else.</summary>
    public (IReadOnlyList<byte[]> Frames, string? Problem) Frames => StdoutFrames.Read(Stdout);

    public string Describe() =>
        $"exit {(ExitCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none (stopped)")}; stdout {Stdout.Length} bytes; stderr:{Environment.NewLine}" +
        string.Join(Environment.NewLine, StderrLines.Take(30).Select(line => line.Text.Length > 600 ? line.Text[..600] + "..." : line.Text));

    static JsonElement? TryParse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Reads stdout strictly as Content-Length frames: each a header of exactly one line
/// <c>Content-Length: n</c>, an empty line and n bytes, and nothing else anywhere.</summary>
public static class StdoutFrames
{
    public static (IReadOnlyList<byte[]> Frames, string? Problem) Read(byte[] stdout)
    {
        var frames = new List<byte[]>();
        var offset = 0;
        while (offset < stdout.Length)
        {
            var found = stdout.AsSpan(offset).IndexOf("\r\n\r\n"u8);
            if (found < 0) return (frames, $"stdout has bytes that are not a frame at byte {offset}: {Show(stdout, offset)}");
            var header = Encoding.ASCII.GetString(stdout, offset, found);
            if (!header.StartsWith("Content-Length: ", StringComparison.Ordinal)
                || !int.TryParse(header["Content-Length: ".Length..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var length))
            {
                return (frames, $"frame {frames.Count + 1} at byte {offset} has the header '{header}', not one Content-Length line");
            }

            var start = offset + found + 4;
            if (start + length > stdout.Length) return (frames, $"frame {frames.Count + 1} declares {length} bytes and stdout ends first");
            frames.Add(stdout[start..(start + length)]);
            offset = start + length;
        }

        return (frames, null);
    }

    static string Show(byte[] bytes, int offset) => Encoding.UTF8.GetString(bytes, offset, Math.Min(200, bytes.Length - offset));
}
