using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Morphir.Tooling.Infrastructure;

/// <summary>The limits of one child process: a deadline and a byte budget for each output stream.</summary>
internal sealed record ProcessLimits(TimeSpan Deadline, int MaxOutputBytes);

/// <summary>How a bounded child process ended.</summary>
internal abstract record ProcessRun
{
    private ProcessRun() { }

    /// <summary>The process exited and closed its output streams.</summary>
    /// <param name="ProcessId">The process identity, so a caller can check that nothing remains.</param>
    public sealed record Exited(int ExitCode, string StandardOutput, string StandardError, int ProcessId) : ProcessRun;

    /// <summary>The process could not start, for example because the executable does not exist.</summary>
    public sealed record NotStarted(string Reason) : ProcessRun;

    /// <summary>The process did not end before the deadline. Its process tree was stopped.</summary>
    public sealed record DeadlineExceeded(TimeSpan Deadline) : ProcessRun;

    /// <summary>The process wrote more than the output budget. Its process tree was stopped.</summary>
    public sealed record OutputLimitExceeded(int MaxOutputBytes) : ProcessRun;
}

/// <summary>
/// Runs a child process with explicit arguments and no shell, under a deadline and an output budget. When
/// the deadline, the budget or cancellation stops the process, its whole process tree is stopped and awaited
/// before the call returns.
/// </summary>
/// <remarks>
/// A descendant can outlive the direct child. On Linux the child starts under <c>setsid</c>, so it leads its
/// own process group with the same process identity, and the whole group is stopped, orphans included. The
/// group is also stopped after a normal exit, so no descendant remains. Windows reaches a descendant through
/// its recorded parent. macOS has no <c>setsid</c>, so there an orphan that left the tree cannot be reached.
/// </remarks>
internal static class BoundedProcess
{
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);

    private const int SigKill = 9;

    private static readonly string? Setsid = OperatingSystem.IsLinux()
        ? new[] { "/usr/bin/setsid", "/bin/setsid" }.FirstOrDefault(File.Exists)
        : null;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int pid, int signal);

    /// <summary>
    /// Runs <paramref name="startInfo"/> to completion. Cancellation stops the process tree and throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    internal static async Task<ProcessRun> RunAsync(ProcessStartInfo startInfo, ProcessLimits limits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        var ownGroup = StartInNewSession(startInfo);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return new ProcessRun.NotStarted("The process did not start.");
        }
        catch (Exception failure) when (failure is Win32Exception or FileNotFoundException or DirectoryNotFoundException
                                          or UnauthorizedAccessException)
        {
            return new ProcessRun.NotStarted(failure.Message);
        }

        // The child gets no input. Closing the stream stops a prompt from waiting for one.
        process.StandardInput.Close();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(limits.Deadline);
        var overflow = new OverflowFlag();
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, limits.MaxOutputBytes, overflow, stop);
        var error = ReadBoundedAsync(process.StandardError.BaseStream, limits.MaxOutputBytes, overflow, stop);
        var completed = Task.WhenAll(output, error, process.WaitForExitAsync(CancellationToken.None));
        try
        {
            // The pipes close only when the process and every descendant that holds them have ended.
            await completed.WaitAsync(stop.Token).ConfigureAwait(false);
            if (ownGroup) SendSignal(-process.Id, SigKill); // A descendant that closed its pipes can remain.
            return new ProcessRun.Exited(process.ExitCode, await output, await error, process.Id);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Observe the abandoned reads, so that their failures are not unobserved task exceptions.
            _ = completed.ContinueWith(static task => task.Exception, TaskScheduler.Default);
            await StopTreeAsync(process, ownGroup).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return overflow.IsSet
                ? new ProcessRun.OutputLimitExceeded(limits.MaxOutputBytes)
                : new ProcessRun.DeadlineExceeded(limits.Deadline);
        }
    }

    /// <summary>Reads a stream to its end as UTF-8. Over the budget, it marks the overflow and stops the run.</summary>
    private static async Task<string> ReadBoundedAsync(Stream stream, int maxBytes, OverflowFlag overflow,
        CancellationTokenSource stop)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, stop.Token).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes)
            {
                overflow.Set();
                await stop.CancelAsync().ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
            }
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>On Linux, runs the command through <c>setsid</c>, so it leads its own process group.</summary>
    private static bool StartInNewSession(ProcessStartInfo startInfo)
    {
        if (Setsid is null) return false;
        if (startInfo.FileName == Setsid) return true; // Already wrapped: the same start info ran before.
        // Under setsid a missing executable fails only in exec. Resolve it first, so it still does not start.
        var command = Resolve(startInfo.FileName);
        if (command is null) return false;
        startInfo.FileName = Setsid;
        if (startInfo.ArgumentList.Count > 0 || string.IsNullOrEmpty(startInfo.Arguments))
            startInfo.ArgumentList.Insert(0, command);
        else
            startInfo.Arguments = $"\"{command}\" {startInfo.Arguments}";
        return true;
    }

    /// <summary>The executable as a path: as given when it has a directory, else the first match on PATH.</summary>
    private static string? Resolve(string fileName)
    {
        if (fileName.Contains(Path.DirectorySeparatorChar))
            return File.Exists(fileName) ? fileName : null;
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }

    private static async Task StopTreeAsync(Process process, bool ownGroup)
    {
        // The group outlives an exited leader, so this also reaches orphaned descendants.
        if (ownGroup) SendSignal(-process.Id, SigKill);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
        catch (Win32Exception)
        {
            // A member of the tree ended while it was being stopped.
        }

        using var wait = new CancellationTokenSource(KillWait);
        try
        {
            await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller gets the failure; it does not wait longer for a process that does not stop.
        }
    }

    private sealed class OverflowFlag
    {
        private int _set;
        public bool IsSet => Volatile.Read(ref _set) == 1;
        public void Set() => Volatile.Write(ref _set, 1);
    }
}
