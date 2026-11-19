// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The worker session of a scenario that the step classes share: its exit, and the temporary directory that the
/// session got. The worker steps check it, whichever step class started the session (the host, a copy of the host
/// or an installed packaged offering).
/// </summary>
public sealed class WorkerRun
{
    private SessionExit? _exit;

    public SessionExit Exit
    {
        get => _exit ?? throw new InvalidOperationException("No worker has run in this scenario.");
        set => _exit = value;
    }

    /// <summary>The exit, or null before a worker ran.</summary>
    public SessionExit? ExitOrNull => _exit;

    /// <summary>The temporary directory of the session, or null for the temporary directory of the workspace.</summary>
    public WorkspaceDirectory? Temp { get; set; }
}
