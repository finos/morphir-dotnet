// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.TestSupport;

namespace Morphir.BlackBox.Tests.Hooks;

/// <summary>
/// The hooks of the test process. Before the first test they capture the Morphir locations of the machine and start
/// the session, which reads the settings and resolves the target: a wrong setting or a missing artifact stops the
/// run here. After the last test they print the skip counts and fail the run if a test changed the machine.
/// </summary>
public static class RunHooks
{
    private static MachineTripwire? _tripwire;
    private static BlackBoxSession? _session;

    /// <summary>The session of this test process.</summary>
    public static BlackBoxSession Session =>
        _session ?? throw new TestSessionException("The session of the run did not start: see the error of the assembly hook.");

    [TUnit.Core.Before(HookType.Assembly)]
    public static void StartTheRun()
    {
        _tripwire = MachineTripwire.Capture();
        _session = BlackBoxSession.Start(typeof(RunHooks).Assembly);
    }

    [TUnit.Core.After(HookType.Assembly)]
    public static void EndTheRun()
    {
        // The build reads this line for its report (SkipCounts.TryParseReportLine). The test framework holds the
        // output of a hook, so the line goes to the console of the process.
        if (_session is not null) GlobalContext.Current.OriginalConsoleOut.WriteLine(_session.Skips.ReportLine());
        _tripwire?.AssertUnchanged();
    }
}
