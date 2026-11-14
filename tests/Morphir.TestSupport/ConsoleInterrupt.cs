// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Morphir.TestSupport;

/// <summary>The console control event a test sends to a worker on Windows.</summary>
public enum ConsoleControlEvent
{
    /// <summary><c>CTRL_C_EVENT</c>, which .NET reports as SIGINT.</summary>
    CtrlC = 0,

    /// <summary><c>CTRL_BREAK_EVENT</c>, which .NET reports as SIGQUIT.</summary>
    CtrlBreak = 1,
}

/// <summary>
/// Interrupts one child process the way a user or a runner does, without touching the test process.
/// </summary>
/// <remarks>
/// <para><b>Unix.</b> <c>kill(pid, SIGINT)</c>.</para>
/// <para>
/// <b>Windows.</b> A console control event reaches every process attached to a console, so the test process cannot
/// send one to a child without receiving it. The worker is started with <c>CreateNoWindow</c>, which gives it a console
/// of its own with no window. The <see cref="ProcessHelper"/>, started with <c>CreateNoWindow</c> too, detaches from its
/// own console, attaches to the worker's, ignores Ctrl+C itself, and calls <c>GenerateConsoleCtrlEvent(event, 0)</c>,
/// which signals every process on that console: the worker, and the helper, which ignores Ctrl+C and exits anyway for
/// Ctrl+Break. No window opens at any point. The helper prints the UTC time right before the event is generated, so the
/// test measures from the event and not from the helper's start-up.
/// </para>
/// </remarks>
public static class ConsoleInterrupt
{
    const int SigInt = 2;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    static extern int Kill(int pid, int signal);

    /// <summary>
    /// The gate of the helper. The target of an interrupt is usually a session that holds a slot of
    /// <see cref="ProcessGate.Shared"/> for its whole life. With a share of one slot (the Test target gives 1 to each test
    /// process on a machine with 12 processors) a helper on the shared gate waits for that slot until the time limit of
    /// the session kills its target. The helper is short and runs only next to its target, so it has a gate of its own.
    /// </summary>
    static readonly ProcessGate HelperGate = new(4);

    /// <summary>Sends the interruption and returns when it was sent, in UTC.</summary>
    public static async Task<DateTime> SendAsync(int pid, ConsoleControlEvent windowsEvent = ConsoleControlEvent.CtrlC)
    {
        if (!OperatingSystem.IsWindows())
        {
            var sent = DateTime.UtcNow;
            if (Kill(pid, SigInt) != 0) throw new InvalidOperationException($"kill({pid}, SIGINT) failed: errno {Marshal.GetLastPInvokeError()}");
            return sent;
        }

        // ChildProcess hides the helper's window and reads its output. A Ctrl+Break also ends the helper, which is why
        // the time is taken just before the event, not after it.
        var info = ProcessHelper.StartInfo("send-ctrl", pid.ToString(CultureInfo.InvariantCulture), ((int)windowsEvent).ToString(CultureInfo.InvariantCulture));
        var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(30), gate: HelperGate);
        var lines = result.StandardOutput.Split('\n').Select(line => line.Trim()).ToArray();
        if (lines.FirstOrDefault(line => line.StartsWith("attach-failed ", StringComparison.Ordinal)) is { } attach)
        {
            throw AttachFailure(pid, attach, result);
        }

        var sending = lines.FirstOrDefault(line => line.StartsWith("sending ", StringComparison.Ordinal));
        if (sending is null || lines.Any(line => line.StartsWith("generate-failed", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"The console control helper did not send the event (exit {result.ExitCode}): {result.CombinedOutput}");
        }

        return new DateTime(long.Parse(sending["sending ".Length..], CultureInfo.InvariantCulture), DateTimeKind.Utc);
    }

    /// <summary>
    /// AttachConsole gives ERROR_INVALID_PARAMETER (87) both for a process that does not exist and for a process with
    /// no console. A target started with <c>CreateNoWindow</c> has a console of its own, also when the test process has
    /// none, so 87 nearly always means that the target had exited: the message says which case it was.
    /// </summary>
    static InvalidOperationException AttachFailure(int pid, string attach, ChildProcessResult result) =>
        IsRunning(pid)
            ? new InvalidOperationException(
                $"The console control helper could not attach to the console of process {pid}, which is running ({attach}; exit {result.ExitCode}). " +
                $"Was it started with no console (DETACHED_PROCESS) instead of CreateNoWindow? {result.CombinedOutput}")
            : new InvalidOperationException(
                $"Process {pid} had exited before the interrupt, so the console control helper could not attach to its console ({attach}). " +
                "Check that the target was still running, for example that the time limit of its session had not stopped it.");

    static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true; // It exists, but this process may not query it.
        }
    }
}
