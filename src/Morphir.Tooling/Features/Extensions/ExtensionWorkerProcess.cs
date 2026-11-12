// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using Morphir.Extensions;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The worker mode on the process's own stdio (task 4.1, design WRK-2). Both entry points, <c>morphir</c> and
/// <c>dotnet-morphir</c>, call <see cref="Run"/> before they build the CLI host, so the worker starts fast, no log line
/// or banner can reach stdout, and the generic host's console lifetime, which would install its own Ctrl+C handler,
/// never exists in a worker process: the worker's handler is the only one, so the two cannot race.
/// </summary>
/// <remarks>
/// <para>
/// <b>Interruption.</b> SIGINT (Ctrl+C) interrupts the worker on every platform. On Windows the worker also treats
/// Ctrl+Break as Ctrl+C: .NET reports <c>CTRL_BREAK_EVENT</c> as <see cref="PosixSignal.SIGQUIT"/>, and a parent can
/// send only Ctrl+Break to a child in its own process group, so a runner that starts the worker that way can still
/// interrupt it (the bounds evidence of task 2.5). The handler cancels the signal's default action and cancels the
/// interruption token; the running call stops at the channel's next epoch tick, and the worker exits with 130 and no
/// <c>fault</c> event. SIGTERM and Unix SIGQUIT keep the platform's default.
/// </para>
/// <para>
/// <b>The workspace root.</b> The contract's policy <c>workspaceInfo.root</c> is <c>private-empty-directory</c> and
/// names no location. The worker creates one private directory of its own in the system's temporary directory,
/// <c>morphir-extension-worker-&lt;32 hex digits&gt;</c> (readable only by the user on Unix), passes it as the root
/// below which the session creates the guest's workspace, and removes it, with anything below it, when it exits: after
/// any result, after an interruption, and from <see cref="AppDomain.ProcessExit"/> when the runtime ends the process
/// for SIGTERM. Only a kill that runs no code leaves it behind.
/// </para>
/// </remarks>
public static class ExtensionWorkerProcess
{
    /// <summary>Whether the command line selects the worker mode: its first two arguments are <c>extension worker</c>.</summary>
    public static bool IsInvocation(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Count >= 2 && args[0] == "extension" && args[1] == "worker";
    }

    /// <summary>Runs the worker mode for <paramref name="args"/> (starting with <c>extension worker</c>) as
    /// <paramref name="host"/>, on the process's stdin, stdout and stderr, and returns the exit status.</summary>
    public static int Run(IReadOnlyList<string> args, HostIdentity host)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(host);

        // Stdout is the frame stream: a stray Console.Out write from any library goes to stderr instead.
        ConsoleOutputHelper.RedirectConsoleToStderr();
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var stderr = Console.OpenStandardError();
        using var interrupt = new CancellationTokenSource();
        using var signals = Interruption.Register(interrupt);
        var root = WorkspaceRoot.Create();
        try
        {
            var context = new WorkerContext(WorkerBuildIdentity.Of(host), root.Path, new WasmModuleLoader(WasmRuntime.Process, host));
            return ExtensionWorker.RunAsync([.. args.Skip(2)], new WorkerIo(stdin, stdout, stderr), context, interrupt.Token)
                .GetAwaiter().GetResult();
        }
        finally
        {
            root.Dispose();
        }
    }

    /// <summary>The signal registrations that turn Ctrl+C (and Ctrl+Break on Windows) into the interruption token.</summary>
    static class Interruption
    {
        public static IDisposable Register(CancellationTokenSource interrupt)
        {
            void Interrupt(PosixSignalContext context)
            {
                context.Cancel = true;
                try
                {
                    interrupt.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The worker has finished.
                }
            }

            List<PosixSignalRegistration> registrations = [PosixSignalRegistration.Create(PosixSignal.SIGINT, Interrupt)];
            if (OperatingSystem.IsWindows())
            {
                registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Interrupt));
            }

            return new Registrations(registrations);
        }

        sealed class Registrations(List<PosixSignalRegistration> registrations) : IDisposable
        {
            public void Dispose()
            {
                foreach (var registration in registrations) registration.Dispose();
            }
        }
    }

    /// <summary>The worker's private directory, below which the session creates the guest's workspace.</summary>
    sealed class WorkspaceRoot : IDisposable
    {
        int removed;

        WorkspaceRoot(string path)
        {
            Path = path;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        public string Path { get; }

        public static WorkspaceRoot Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"morphir-extension-worker-{Guid.NewGuid():N}");
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return new WorkspaceRoot(path);
        }

        void OnProcessExit(object? sender, EventArgs e) => Remove();

        void Remove()
        {
            if (Interlocked.Exchange(ref removed, 1) != 0) return;
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A file the guest's session still holds; nothing else can be done at exit.
            }
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            Remove();
        }
    }
}
