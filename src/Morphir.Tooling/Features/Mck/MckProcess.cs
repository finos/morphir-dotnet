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

    /// <summary>The shell convention for signal exit codes: this base plus the signal number.</summary>
    private const int SignalExitCodeBase = 128;

    /// <summary>The exit code when the routing was cancelled: the shell's code for an interrupt.</summary>
    private const int CancelledExitCode = SignalExitCodeBase + 2;

    /// <summary>The conventional exit code of a process stopped by SIGTERM; also used for SIGHUP.</summary>
    private const int TerminatedExitCode = SignalExitCodeBase + 15;

    /// <summary>At most this many failures are named when part of the tool's tree cannot be stopped.</summary>
    private const int ReportedFailures = 3;

    /// <summary>
    /// Runs <paramref name="executable"/> and returns its exit code.
    /// <list type="bullet">
    /// <item>Ctrl+C reaches the tool directly, through the console (Windows) or the foreground process group (Unix). This
    /// process ignores it and waits, so the tool decides how to stop.</item>
    /// <item>On Windows this process first joins a job object that ends every process in it when this process ends, even
    /// when it is killed. The tool and its children inherit the job.</item>
    /// <item>On Unix, SIGTERM or SIGHUP to this process stops the tool's process tree, and the exit code is 143.</item>
    /// <item>Cancelling <paramref name="cancellationToken"/> stops the tool's process tree, and the exit code is 130.
    /// When it is already cancelled, nothing starts.</item>
    /// </list>
    /// The signal handlers and the cancellation are registered before the tool starts, so a request that arrives
    /// while it is being started stops it as soon as it has started.
    /// </summary>
    internal static int Run(string executable, IReadOnlyList<string> arguments, TextWriter diagnostics, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        var termination = new MckTermination();
        void Terminate(PosixSignalContext context)
        {
            context.Cancel = true;
            termination.Request(TerminatedExitCode);
        }

        using var term = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, Terminate);
        using var hangup = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGHUP, Terminate);
        using var cancel = cancellationToken.Register(() => termination.Request(CancelledExitCode));
        ConsoleCancelEventHandler interrupt = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += interrupt;
        try
        {
            if (termination.ExitCode is { } before)
            {
                Report(diagnostics, $"morphir mck: {(before == CancelledExitCode ? "cancelled" : "terminated")} before {MckTool.ExecutableName} started; nothing was run.");
                return before;
            }

            if (OperatingSystem.IsWindows() && !KillOnCloseJob.TryJoin(out var reason))
                Report(diagnostics, $"morphir mck: the processes of {MckTool.ExecutableName} will not be stopped if morphir is killed ({reason}).");

            Process? process;
            try
            {
                process = Process.Start(start);
            }
            catch (Win32Exception exception)
            {
                Report(diagnostics, $"morphir mck: {executable} could not be started: {exception.Message}");
                return NotStartedExitCode;
            }

            if (process is null)
            {
                Report(diagnostics, $"morphir mck: {executable} could not be started.");
                return NotStartedExitCode;
            }

            using (process)
            {
                try
                {
                    termination.Attach(() => StopTree(() => process.Kill(entireProcessTree: true), diagnostics));
                    process.WaitForExit();
                    return termination.ExitCode ?? process.ExitCode;
                }
                finally
                {
                    // Before the process is disposed: a later signal or cancellation then touches nothing.
                    termination.Detach();
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= interrupt;
        }
    }

    /// <summary>
    /// Stops the tool's process tree with <paramref name="kill"/>, for a signal handler or a cancellation callback, so it
    /// never throws. A tool that has already exited is not an error. When part of the tree cannot be stopped (the
    /// <see cref="AggregateException"/> of <see cref="Process.Kill(bool)"/>), or the tree cannot be read, it says so on
    /// <paramref name="diagnostics"/>, naming at most three failures. On Windows the job object still ends the tree when
    /// morphir ends.
    /// </summary>
    internal static void StopTree(Action kill, TextWriter diagnostics)
    {
        try
        {
            kill();
        }
        catch (InvalidOperationException)
        {
            // The tool has exited.
        }
        catch (AggregateException exception)
        {
            var failures = exception.Flatten().InnerExceptions;
            var named = string.Join("; ", failures.Take(ReportedFailures).Select(failure => Shorten(failure.Message)));
            Report(diagnostics, $"morphir mck: {failures.Count} processes of the tree of {MckTool.ExecutableName} could not be stopped: {named}" +
                (failures.Count > ReportedFailures ? $"; and {failures.Count - ReportedFailures} more." : "."));
        }
        catch (Exception exception) when (exception is Win32Exception or NotSupportedException)
        {
            Report(diagnostics, $"morphir mck: the process tree of {MckTool.ExecutableName} could not be stopped: {Shorten(exception.Message)}");
        }
    }

    /// <summary>Writes a diagnostic line; a standard error that is closed must not fail a handler.</summary>
    private static void Report(TextWriter diagnostics, string message)
    {
        try
        {
            diagnostics.WriteLine(message);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Nowhere left to report it.
        }
    }

    private static string Shorten(string message) => message.Length <= 200 ? message : message[..200] + "...";
}
