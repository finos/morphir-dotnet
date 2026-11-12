// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.BlackBox.Tests.Hooks;

namespace Morphir.BlackBox.Tests;

/// <summary>
/// The base class of a framework test of this project: a loop over a corpus, a property test or a test with a time
/// budget (a test of another form is a Gherkin scenario). Before the test it makes the same eligibility decision
/// as a scenario, from the categories of the test, and makes the workspace and the drivers. The test class has one
/// group category (<c>integration</c>, <c>e2e</c>, <c>smoke</c> or <c>journey</c>).
/// </summary>
public abstract class BlackBoxTest
{
    private DriverSet? _drivers;

    /// <summary>The workspace and the drivers of this test, on its host.</summary>
    protected DriverSet Drivers => _drivers ?? throw new TestSessionException("The drivers are there only while the test runs.");

    protected IScenarioWorkspace Workspace => Drivers.Workspace;

    [TUnit.Core.Before(HookType.Test)]
    public void BeginTheTest(TestContext context)
    {
        switch (TestStart.Decide(context.Metadata.TestDetails.Categories))
        {
            case Eligibility.Skipped skipped:
                Skip.Test(skipped.Reason);
                break;
            case Eligibility.Eligible eligible:
                _drivers = RunHooks.Session.BeginTest(eligible.Host);
                break;
        }
    }

    [TUnit.Core.After(HookType.Test)]
    public async Task EndTheTest(TestContext context)
    {
        if (_drivers is not null) await TestStart.EndAsync(_drivers, failed: context.Execution.Result?.State == TestState.Failed);
    }
}
