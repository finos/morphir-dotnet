// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of one session.</summary>
public sealed record ProcessSessionOptions
{
    /// <summary>The time limit of the complete session. Default: <see cref="ProcessSessionDriver.DefaultTimeout"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Variables for this session, added after the workspace contained the process. A null value removes a variable.</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }

    /// <summary>The directory in which the process starts, as a full path. The default is the work area of the workspace.</summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// Starts a product process that stays alive (design DRV-1 of the change consolidate-black-box-tests): the test
/// writes framed input, reads the output as it comes, can interrupt the process, and gets its exit and the time
/// from the interrupt to the exit. The workspace contains the process, the time limit and the process gate apply,
/// and the end of the test stops the process tree.
/// </summary>
public interface IProcessSessionDriver
{
    TestTarget Target { get; }

    MorphirHost Host { get; }

    /// <summary>The file of the host: an assembly that the driver starts with <c>dotnet</c>, or an executable.</summary>
    string Artifact { get; }

    /// <summary>Starts the host with <paramref name="arguments"/>. Returns when the process runs.</summary>
    Task<IProcessSession> StartAsync(IReadOnlyList<string> arguments, ProcessSessionOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>One line of standard error, with the time from the start of the session at which the test read it.</summary>
public sealed record SessionLine(TimeSpan At, string Text);

/// <summary>A running product process. Dispose stops its process tree when it is still alive.</summary>
public interface IProcessSession : IAsyncDisposable
{
    /// <summary>The process id, for example for an interrupt.</summary>
    int ProcessId { get; }

    /// <summary>The command line of the session, for a message.</summary>
    string CommandLine { get; }

    /// <summary>The time since the process started.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>Each byte of standard output so far.</summary>
    byte[] StandardOutput { get; }

    /// <summary>Each line of standard error so far.</summary>
    IReadOnlyList<SessionLine> StandardError { get; }

    /// <summary>The largest working set seen while the process ran, in bytes, or 0 where the platform does not say.</summary>
    long PeakWorkingSet { get; }

    /// <summary>Writes the bytes to standard input and flushes, with stdin left open. A process that stopped reading ends the write quietly.</summary>
    Task WriteAsync(params byte[][] input);

    /// <summary>Waits until <paramref name="condition"/> holds, the process exits, or the time passes. Returns the last value of the condition.</summary>
    Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout);

    /// <summary>
    /// Interrupts the process as a user does: SIGINT on Linux and macOS, the console control event
    /// <paramref name="windowsEvent"/> on Windows. Returns when the interrupt was sent, in UTC.
    /// </summary>
    Task<DateTimeOffset> InterruptAsync(ConsoleControlEvent windowsEvent = ConsoleControlEvent.CtrlC);

    /// <summary>
    /// Waits for the process to exit by itself. A process that is alive at the time limit has its process tree stopped,
    /// and the result has no exit code. Standard input closes after the process closed its standard output.
    /// </summary>
    Task<SessionExit> WaitForExitAsync(TimeSpan timeout);
}

/// <summary>What one session did.</summary>
/// <param name="ExitCode">The exit code, or null when the driver stopped the process.</param>
/// <param name="StandardOutput">Each byte of standard output.</param>
/// <param name="StandardError">Each line of standard error.</param>
/// <param name="ExitedAt">When the process closed its output, from the start of the session.</param>
/// <param name="ExitTimeUtc">When the process closed its output, in UTC.</param>
/// <param name="InterruptedAt">When the test interrupted the process, or null.</param>
/// <param name="PeakWorkingSet">The largest working set seen while the process ran, or 0.</param>
public sealed record SessionExit(
    int? ExitCode,
    byte[] StandardOutput,
    IReadOnlyList<SessionLine> StandardError,
    TimeSpan ExitedAt,
    DateTimeOffset ExitTimeUtc,
    DateTimeOffset? InterruptedAt,
    long PeakWorkingSet)
{
    /// <summary>The time from the interrupt to the exit, or null when the test did not interrupt the process.</summary>
    public TimeSpan? FromInterruptToExit => InterruptedAt is { } sent ? ExitTimeUtc - sent : null;

    /// <summary>The lines of standard error that are <c>morphir/command-event</c> objects, as (name, object, time read).</summary>
    public IReadOnlyList<(string Name, JsonElement Json, TimeSpan At)> Events => StandardError
        .Select(line => (line, Json: TryParse(line.Text)))
        .Where(item => item.Json is { ValueKind: JsonValueKind.Object } json
            && json.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.String
            && profile.GetString() == "morphir/command-event"
            && json.TryGetProperty("event", out var name) && name.ValueKind == JsonValueKind.String)
        .Select(item => (item.Json!.Value.GetProperty("event").GetString()!, item.Json.Value, item.line.At))
        .ToArray();

    public IReadOnlyList<string> EventNames => Events.Select(e => e.Name).ToArray();

    /// <summary>The text of each <c>output</c> event, joined in order with nothing between them.</summary>
    public string GuestOutput => string.Concat(Events.Where(e => e.Name == "output")
        .Select(e => e.Json.TryGetProperty("output", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : ""));

    /// <summary>Standard output read as Content-Length frames, or the reason that it is something else.</summary>
    public (IReadOnlyList<byte[]> Frames, string? Problem) Frames => ContentLengthFrames.Read(StandardOutput);

    /// <summary>The exit, the size of standard output and the first lines of standard error, for a failure message.</summary>
    public string Describe() =>
        $"exit {(ExitCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none (stopped)")}; stdout {StandardOutput.Length} bytes; stderr:{Environment.NewLine}" +
        string.Join(Environment.NewLine, StandardError.Take(30).Select(line => line.Text.Length > 600 ? line.Text[..600] + "..." : line.Text));

    private static JsonElement? TryParse(string line)
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

/// <summary>The Content-Length framing of a process that speaks a framed protocol on its standard streams.</summary>
public static class ContentLengthFrames
{
    /// <summary>A frame of the UTF-8 of <paramref name="json"/>.</summary>
    public static byte[] Frame(string json) => Frame(Encoding.UTF8.GetBytes(json));

    /// <summary>A frame of <paramref name="body"/>: the header <c>Content-Length: n</c>, an empty line and the body.</summary>
    public static byte[] Frame(byte[] body) => [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];

    /// <summary>
    /// Reads <paramref name="output"/> strictly as frames: each a header of exactly one line <c>Content-Length: n</c>,
    /// an empty line and n bytes, and nothing else anywhere.
    /// </summary>
    public static (IReadOnlyList<byte[]> Frames, string? Problem) Read(byte[] output)
    {
        var frames = new List<byte[]>();
        var offset = 0;
        while (offset < output.Length)
        {
            var found = output.AsSpan(offset).IndexOf("\r\n\r\n"u8);
            if (found < 0) return (frames, $"stdout has bytes that are not a frame at byte {offset}: {Show(output, offset)}");
            var header = Encoding.ASCII.GetString(output, offset, found);
            if (!header.StartsWith("Content-Length: ", StringComparison.Ordinal)
                || !int.TryParse(header["Content-Length: ".Length..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var length))
            {
                return (frames, $"frame {frames.Count + 1} at byte {offset} has the header '{header}', not one Content-Length line");
            }

            var start = offset + found + 4;
            if (start + length > output.Length) return (frames, $"frame {frames.Count + 1} declares {length} bytes and stdout ends first");
            frames.Add(output[start..(start + length)]);
            offset = start + length;
        }

        return (frames, null);
    }

    private static string Show(byte[] bytes, int offset) => Encoding.UTF8.GetString(bytes, offset, Math.Min(200, bytes.Length - offset));
}

/// <summary>The session driver for a host that is one file: an assembly, which it starts with <c>dotnet</c>, or an executable.</summary>
public sealed class ProcessSessionDriver : IProcessSessionDriver
{
    /// <summary>The time limit of a session with no other limit.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private readonly string _artifact;
    private readonly IScenarioWorkspace _workspace;
    private readonly ProcessGate? _gate;

    /// <param name="artifact">The file of the host, from <see cref="CliArtifacts.PathOf"/>.</param>
    /// <param name="gate">The process gate of each session. The default is <see cref="ProcessGate.Shared"/>.</param>
    public ProcessSessionDriver(TestTarget target, MorphirHost host, string artifact, IScenarioWorkspace workspace, ProcessGate? gate = null)
    {
        Target = target;
        Host = host;
        _artifact = artifact;
        _workspace = workspace;
        _gate = gate;
    }

    public TestTarget Target { get; }

    public MorphirHost Host { get; }

    public string Artifact => _artifact;

    public async Task<IProcessSession> StartAsync(IReadOnlyList<string> arguments, ProcessSessionOptions? options = null, CancellationToken cancellation = default)
    {
        var assembly = _artifact.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo(assembly ? "dotnet" : _artifact) { StandardErrorEncoding = new UTF8Encoding(false) };
        if (assembly) info.ArgumentList.Add(_artifact);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var commandLine = string.Join(' ', new[] { info.FileName }.Concat(info.ArgumentList));
        _workspace.Contain(info);
        if (options?.Environment is { } environment)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null) info.Environment.Remove(name);
                else info.Environment[name] = value;
            }
        }

        if (options?.WorkingDirectory is { } directory) info.WorkingDirectory = directory;
        var session = new ProcessSession(commandLine);
        await session.StartAsync(info, options?.Timeout ?? DefaultTimeout, _gate, cancellation).ConfigureAwait(false);
        return session;
    }

    private sealed class ProcessSession(string commandLine) : IProcessSession
    {
        private readonly MemoryStream _stdout = new();
        private readonly List<SessionLine> _stderr = [];
        private readonly Stopwatch _clock = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<Stream> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<ChildProcessResult>? _run;
        private Stream? _stdin;
        private int _processId;
        private long _peakWorkingSet;
        private TimeSpan _exitedAt;
        private DateTimeOffset _exitTimeUtc;
        private DateTimeOffset? _interruptedAt;
        private SessionExit? _exit;

        public int ProcessId => _processId;

        public string CommandLine => commandLine;

        public TimeSpan Elapsed => _clock.Elapsed;

        public byte[] StandardOutput
        {
            get
            {
                lock (_stdout) return _stdout.ToArray();
            }
        }

        public IReadOnlyList<SessionLine> StandardError
        {
            get
            {
                lock (_stderr) return [.. _stderr];
            }
        }

        public long PeakWorkingSet
        {
            get
            {
                SampleMemory();
                return Interlocked.Read(ref _peakWorkingSet);
            }
        }

        public async Task StartAsync(ProcessStartInfo info, TimeSpan timeout, ProcessGate? gate, CancellationToken cancellation)
        {
            using var link = cancellation.Register(_stop.Cancel);
            _run = ChildProcess.ConverseAsync(
                info,
                timeout,
                ConverseAsync,
                _stop.Token,
                gate,
                started: id =>
                {
                    _processId = id;
                    _clock.Start();
                },
                standardErrorLine: line =>
                {
                    lock (_stderr) _stderr.Add(new SessionLine(_clock.Elapsed, line));
                });
            var first = await Task.WhenAny(_started.Task, _run).ConfigureAwait(false);
            if (first == _run) await _run.ConfigureAwait(false);
            _stdin = await _started.Task.ConfigureAwait(false);
        }

        // The conversation lasts until the process closes its standard output, which it does when it exits. Then
        // ChildProcess closes standard input and waits for the exit.
        private async Task ConverseAsync(Stream stdin, Stream stdout, CancellationToken token)
        {
            _started.TrySetResult(stdin);
            var buffer = new byte[16384];
            int count;
            while ((count = await stdout.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                lock (_stdout) _stdout.Write(buffer, 0, count);
            }

            _exitedAt = _clock.Elapsed;
            _exitTimeUtc = DateTimeOffset.UtcNow;
        }

        public async Task WriteAsync(params byte[][] input)
        {
            var stdin = _stdin ?? throw new InvalidOperationException("The session did not start.");
            try
            {
                foreach (var bytes in input)
                {
                    await stdin.WriteAsync(bytes).ConfigureAwait(false);
                    await stdin.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                // The process exited and closed its end.
            }
        }

        public async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = _clock.Elapsed + timeout;
            while (_clock.Elapsed < deadline)
            {
                if (condition()) return true;
                if (_run!.IsCompleted) return condition();
                SampleMemory();
                await Task.Delay(10).ConfigureAwait(false);
            }

            return condition();
        }

        public async Task<DateTimeOffset> InterruptAsync(ConsoleControlEvent windowsEvent = ConsoleControlEvent.CtrlC)
        {
            var sent = new DateTimeOffset(await ConsoleInterrupt.SendAsync(_processId, windowsEvent).ConfigureAwait(false), TimeSpan.Zero);
            _interruptedAt = sent;
            return sent;
        }

        public async Task<SessionExit> WaitForExitAsync(TimeSpan timeout)
        {
            if (_exit is not null) return _exit;
            int? exitCode;
            var deadline = _clock.Elapsed + timeout;
            try
            {
                while (!_run!.IsCompleted)
                {
                    SampleMemory();
                    await Task.WhenAny(_run, Task.Delay(10)).ConfigureAwait(false);
                    if (_clock.Elapsed > deadline && !_run.IsCompleted) _stop.Cancel();
                }

                exitCode = (await _run.ConfigureAwait(false)).ExitCode;
            }
            catch (Exception error) when (error is OperationCanceledException or ChildProcessTimeoutException)
            {
                // The driver stopped the process tree: at the time limit of this wait, or of the session.
                exitCode = null;
                if (_exitTimeUtc == default)
                {
                    _exitedAt = _clock.Elapsed;
                    _exitTimeUtc = DateTimeOffset.UtcNow;
                }
            }

            _exit = new SessionExit(exitCode, StandardOutput, StandardError, _exitedAt, _exitTimeUtc, _interruptedAt, Interlocked.Read(ref _peakWorkingSet));
            return _exit;
        }

        private void SampleMemory()
        {
            if (_processId == 0 || _run is not { IsCompleted: false }) return;
            try
            {
                using var process = Process.GetProcessById(_processId);
                var peak = Math.Max(process.PeakWorkingSet64, process.WorkingSet64);
                long seen;
                do
                {
                    seen = Interlocked.Read(ref _peakWorkingSet);
                    if (peak <= seen) return;
                }
                while (Interlocked.CompareExchange(ref _peakWorkingSet, peak, seen) != seen);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                // The process exited between the check and the read.
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_run is { IsCompleted: false })
            {
                _stop.Cancel();
                try
                {
                    await _run.ConfigureAwait(false);
                }
                catch (Exception error) when (error is OperationCanceledException or ChildProcessTimeoutException)
                {
                    // Stopped.
                }
            }

            _stop.Dispose();
        }
    }
}
