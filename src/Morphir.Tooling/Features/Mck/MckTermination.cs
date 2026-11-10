// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Tooling.Features.Mck;

/// <summary>
/// The stop requests for one routed tool: SIGTERM, SIGHUP or cancellation. It is armed before the tool starts, so a
/// request that arrives while the tool is being started is kept and honoured as soon as the tool is attached.
/// Requests, attachment and detachment are serialized, and a stop runs while the lock is held, so the tool's process
/// is never disposed during a stop and is never stopped after it has been detached.
/// </summary>
internal sealed class MckTermination
{
    private readonly Lock _gate = new();
    private Action? _stop;
    private bool _detached;
    private int? _exitCode;

    /// <summary>The exit code of the first request, or null when none came.</summary>
    internal int? ExitCode
    {
        get
        {
            lock (_gate) return _exitCode;
        }
    }

    /// <summary>
    /// Records a request with the exit code the CLI then reports. The first request decides the code. A running tool
    /// is stopped now; a tool that has not started yet is stopped when it is attached.
    /// </summary>
    internal void Request(int exitCode)
    {
        lock (_gate)
        {
            _exitCode ??= exitCode;
            if (!_detached) _stop?.Invoke();
        }
    }

    /// <summary>
    /// Attaches the started tool's stop action. Returns false, after stopping the tool, when a request came first.
    /// </summary>
    internal bool Attach(Action stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        lock (_gate)
        {
            if (_detached) return false;
            _stop = stop;
            if (_exitCode is null) return true;
            stop();
            return false;
        }
    }

    /// <summary>Detaches the tool before its process is disposed. Waits for a stop in progress.</summary>
    internal void Detach()
    {
        lock (_gate)
        {
            _detached = true;
            _stop = null;
        }
    }
}
