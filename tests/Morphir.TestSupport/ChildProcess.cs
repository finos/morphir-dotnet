// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Morphir.TestSupport;

/// <summary>Captured outcome of a child process that ran to completion.</summary>
public sealed record ChildProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAt,
    DateTimeOffset ExitedAt)
{
    /// <summary>Standard output followed by standard error, for assertion messages.</summary>
    public string CombinedOutput =>
        string.IsNullOrEmpty(StandardError) ? StandardOutput : $"{StandardOutput}{Environment.NewLine}{StandardError}";
}

/// <summary>A child exceeded its timeout. Its process tree was killed and awaited before this was thrown.</summary>
public sealed class ChildProcessTimeoutException(
    string command,
    TimeSpan timeout,
    bool processExited,
    string standardOutput,
    string standardError)
    : TimeoutException(
        $"'{command}' did not finish within {timeout.TotalSeconds:0.#}s and was killed" +
        (processExited ? "." : ", but it had not exited when waiting stopped.") +
        $"{Environment.NewLine}stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{standardError}")
{
    public TimeSpan Timeout { get; } = timeout;
    public bool ProcessExited { get; } = processExited;
    public string StandardOutput { get; } = standardOutput;
    public string StandardError { get; } = standardError;
}

/// <summary>
/// Limits how many child processes a test process runs at once. Test frameworks run tests in parallel,
/// and unthrottled CLI or build children starve each other until their timeouts expire.
/// </summary>
public sealed class ProcessGate
{
    /// <summary>Environment variable that overrides the shared gate's limit.</summary>
    public const string LimitVariable = "MORPHIR_TEST_MAX_PROCESSES";

    private readonly SemaphoreSlim _slots;

    public ProcessGate(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        Limit = limit;
        _slots = new SemaphoreSlim(limit, limit);
    }

    public int Limit { get; }

    /// <summary>The gate every <see cref="ChildProcess"/> call uses unless given another.</summary>
    public static ProcessGate Shared { get; } =
        new(LimitFrom(Environment.GetEnvironmentVariable(LimitVariable), Environment.ProcessorCount));

    /// <summary>A positive integer value wins; otherwise half the processors, and never fewer than two.</summary>
    public static int LimitFrom(string? value, int processorCount) =>
        int.TryParse(value, out var limit) && limit > 0 ? limit : Math.Max(2, processorCount / 2);

    /// <summary>
    /// Holds one slot until the result is disposed. Use it around code under test that starts its own child
    /// process, so that the process counts against the same limit as <see cref="ChildProcess"/> calls.
    /// </summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Slot(_slots);
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}

/// <summary>
/// Runs child processes for tests. Output is read concurrently, the timeout starts once a gate slot is held,
/// and a child that times out or is cancelled has its whole process tree killed and awaited before the call
/// returns, so its working directory can be cleaned up.
/// </summary>
/// <remarks>
/// A descendant can outlive the direct child while keeping its pipes. Windows still reaches it through its
/// recorded parent. On Linux the child starts under <c>setsid</c>, so its whole process group is killed,
/// orphans included. macOS has no <c>setsid</c>: there an orphan cannot be reached, and
/// <see cref="ChildProcessTimeoutException.ProcessExited"/> reports false while it still holds the pipes.
/// </remarks>
public static class ChildProcess
{
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);

    private static readonly string? Setsid = OperatingSystem.IsLinux()
        ? new[] { "/usr/bin/setsid", "/bin/setsid" }.FirstOrDefault(File.Exists)
        : null;

    private const int SigKill = 9;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int pid, int signal);

    /// <summary>A start info with an argument list and no shell, window or inherited console.</summary>
    public static ProcessStartInfo StartInfo(string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static async Task<ChildProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        ProcessGate? gate = null,
        string? standardInput = null)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = standardInput is not null;
        var command = Describe(startInfo);
        var ownGroup = StartInNewSession(startInfo);

        using var slot = await (gate ?? ProcessGate.Shared).EnterAsync(cancellationToken).ConfigureAwait(false);
        using var process = new Process { StartInfo = startInfo };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        process.Start();
        var startedAt = DateTimeOffset.UtcNow;
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), deadline.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            // A grandchild that inherited the pipes can keep them open after the child exits.
            await Task.WhenAll(stdout, stderr).WaitAsync(deadline.Token).ConfigureAwait(false);
            return new ChildProcessResult(process.ExitCode, await stdout, await stderr, startedAt, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            var parentExited = await KillTreeAsync(process, ownGroup).ConfigureAwait(false);
            var (output, outputClosed) = await Completed(stdout).ConfigureAwait(false);
            var (error, errorClosed) = await Completed(stderr).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Open pipes mean a descendant is still running, even when the direct child has exited.
            throw new ChildProcessTimeoutException(command, timeout, parentExited && outputClosed && errorClosed, output, error);
        }
    }

    /// <summary>On Linux, runs the command through setsid so it leads its own process group, with the same PID.</summary>
    private static bool StartInNewSession(ProcessStartInfo info)
    {
        if (Setsid is null) return false;
        if (info.FileName == Setsid) return true; // Already wrapped: the same start info was run before.
        var command = info.FileName;
        info.FileName = Setsid;
        if (info.ArgumentList.Count > 0) info.ArgumentList.Insert(0, command);
        else info.Arguments = "\"" + command + "\"" + (string.IsNullOrEmpty(info.Arguments) ? "" : " " + info.Arguments);
        return true;
    }

    private static async Task<bool> KillTreeAsync(Process process, bool ownGroup)
    {
        // The group outlives an exited leader, so this also reaches orphaned descendants.
        if (ownGroup) SendSignal(-process.Id, SigKill);

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }

        using var wait = new CancellationTokenSource(KillWait);
        try
        {
            await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Report below that it had not exited.
        }

        return process.HasExited;
    }

    /// <summary>The captured text, and whether the pipe closed (no process still holds it).</summary>
    private static async Task<(string Text, bool Closed)> Completed(Task<string> reader)
    {
        try
        {
            return (await reader.WaitAsync(KillWait).ConfigureAwait(false), true);
        }
        catch (TimeoutException)
        {
            return (string.Empty, false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return (string.Empty, true);
        }
    }

    private static string Describe(ProcessStartInfo info) =>
        info.ArgumentList.Count == 0 ? $"{info.FileName} {info.Arguments}".Trim() : $"{info.FileName} {string.Join(' ', info.ArgumentList)}";
}
