// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Hooks;

/// <summary>
/// The hooks of the test process. Before the first test they start the session, which records the Morphir
/// locations of the machine, reads the settings and resolves the target: a wrong setting or a missing artifact
/// stops the run here. After the last test they end the session, which prints the skip counts and fails the run if
/// a test changed the machine.
/// </summary>
public static class RunHooks
{
    private static BlackBoxSession? _session;

    /// <summary>The session of this test process.</summary>
    public static BlackBoxSession Session =>
        _session ?? throw new TestSessionException("The session of the run did not start: see the error of the assembly hook.");

    [TUnit.Core.Before(HookType.Assembly)]
    public static void StartTheRun() => _session = BlackBoxSession.Start(typeof(RunHooks).Assembly);

    // The test framework holds the output of a hook, so the session writes to the console of the process.
    [TUnit.Core.After(HookType.Assembly)]
    public static void EndTheRun() => _session?.EndRun(GlobalContext.Current.OriginalConsoleOut);
}
