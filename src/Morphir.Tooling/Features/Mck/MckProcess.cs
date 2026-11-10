// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Mck;

/// <summary>
/// Starts the resolved tool the way a shell would start it (MCK-2, MCK-4): no shell, the same arguments, working
/// directory, environment and standard handles, and the tool's exit code.
/// </summary>
internal static class MckProcess
{
    /// <summary>The exit code when the tool could not be started: the shell's "found but not executable".</summary>
    private const int NotStartedExitCode = 126;

    /// <summary>The conventional exit code of a process stopped by SIGTERM.</summary>
    private const int TerminatedExitCode = 128 + 15;

    /// <summary>
    /// Runs <paramref name="executable"/> and returns its exit code.
    /// <list type="bullet">
    /// <item>Ctrl+C reaches the tool directly, through the console (Windows) or the foreground process group (Unix). This
    /// process ignores it and waits, so the tool decides how to stop.</item>
    /// <item>On Windows this process first joins a job object that ends every process in it when this process ends, even
    /// when it is killed. The tool and its children inherit the job.</item>
    /// <item>On Unix, SIGTERM or SIGHUP to this process stops the tool's process tree.</item>
    /// <item>Cancelling <paramref name="cancellationToken"/> stops the tool's process tree.</item>
    /// </list>
    /// </summary>
    internal static int Run(string executable, IReadOnlyList<string> arguments, TextWriter diagnostics, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        if (OperatingSystem.IsWindows() && !KillOnCloseJob.TryJoin(out var reason))
            diagnostics.WriteLine($"morphir mck: the processes of {MckTool.ExecutableName} will not be stopped if morphir is killed ({reason}).");

        ConsoleCancelEventHandler interrupt = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += interrupt;
        try
        {
            Process? process;
            try
            {
                process = Process.Start(start);
            }
            catch (Win32Exception exception)
            {
                diagnostics.WriteLine($"morphir mck: {executable} could not be started: {exception.Message}");
                return NotStartedExitCode;
            }

            if (process is null)
            {
                diagnostics.WriteLine($"morphir mck: {executable} could not be started.");
                return NotStartedExitCode;
            }

            using (process)
            {
                var terminated = 0;
                void Terminate(PosixSignalContext context)
                {
                    context.Cancel = true;
                    Interlocked.Exchange(ref terminated, 1);
                    StopTree(process);
                }

                using var term = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, Terminate);
                using var hangup = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGHUP, Terminate);
                using var cancel = cancellationToken.Register(() => StopTree(process));
                process.WaitForExit();
                return Volatile.Read(ref terminated) == 1 ? TerminatedExitCode : process.ExitCode;
            }
        }
        finally
        {
            Console.CancelKeyPress -= interrupt;
        }
    }

    private static void StopTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // It has exited, or the tree could not be read; the job object (Windows) remains.
        }
    }
}
