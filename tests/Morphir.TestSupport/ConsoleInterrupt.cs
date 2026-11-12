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
/// of its own with no window. A helper process, Windows PowerShell started with <c>CreateNoWindow</c> too, detaches
/// from its own console, attaches to the worker's, ignores Ctrl+C itself, and calls
/// <c>GenerateConsoleCtrlEvent(event, 0)</c>, which signals every process on that console: the worker, and the helper,
/// which ignores Ctrl+C and exits anyway for Ctrl+Break. No window opens at any point. The helper prints the UTC time
/// right after the event was generated, so the test measures from the event and not from the helper's start-up.
/// </para>
/// </remarks>
public static class ConsoleInterrupt
{
    const int SigInt = 2;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    static extern int Kill(int pid, int signal);

    /// <summary>Sends the interruption and returns when it was sent, in UTC.</summary>
    public static async Task<DateTime> SendAsync(int pid, ConsoleControlEvent windowsEvent = ConsoleControlEvent.CtrlC)
    {
        if (!OperatingSystem.IsWindows())
        {
            var sent = DateTime.UtcNow;
            if (Kill(pid, SigInt) != 0) throw new InvalidOperationException($"kill({pid}, SIGINT) failed: errno {Marshal.GetLastPInvokeError()}");
            return sent;
        }

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Add-Type -TypeDefinition @'
            using System;
            using System.Runtime.InteropServices;
            public static class MorphirConsoleControl {
                [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeConsole();
                [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint processId);
                [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
                [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
            }
            '@
            $out = [Console]::Out
            [void][MorphirConsoleControl]::FreeConsole()
            if (-not [MorphirConsoleControl]::AttachConsole({{pid}})) { $out.WriteLine('attach-failed ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error()); $out.Flush(); exit 2 }
            [void][MorphirConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)
            $out.WriteLine('sending ' + [DateTime]::UtcNow.Ticks)
            $out.Flush()
            if (-not [MorphirConsoleControl]::GenerateConsoleCtrlEvent({{(int)windowsEvent}}, 0)) { $out.WriteLine('generate-failed ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error()); $out.Flush(); exit 3 }
            $out.WriteLine('sent')
            $out.Flush()
            [void][MorphirConsoleControl]::FreeConsole()
            exit 0
            """;
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script.Replace("\r\n", "\n", StringComparison.Ordinal)));
        var info = ChildProcess.StartInfo(powershell, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded);
        // ChildProcess hides the helper's window and reads its output. A Ctrl+Break also ends the helper, which is why
        // the time is taken just before the event, not after it.
        var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(60));
        var lines = result.StandardOutput.Split('\n').Select(line => line.Trim()).ToArray();
        var sending = lines.FirstOrDefault(line => line.StartsWith("sending ", StringComparison.Ordinal));
        if (sending is null || lines.Any(line => line.StartsWith("generate-failed", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"The console control helper did not send the event (exit {result.ExitCode}): {result.CombinedOutput}");
        }

        return new DateTime(long.Parse(sending["sending ".Length..], CultureInfo.InvariantCulture), DateTimeKind.Utc);
    }
}
