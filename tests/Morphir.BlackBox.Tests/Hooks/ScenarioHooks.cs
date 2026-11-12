// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Reqnroll.BoDi;
using Reqnroll.UnitTestProvider;

namespace Morphir.BlackBox.Tests.Hooks;

/// <summary>
/// The hooks of one Gherkin scenario. Before the first step they make the one eligibility decision from the tags of
/// the scenario; a scenario that the run does not execute ends as skipped with its one reason. For a scenario that
/// runs they make its workspace and its drivers on its host, and a step class gets them through its constructor.
/// After the scenario they remove the workspace, or keep it for a failed scenario (MORPHIR_TEST_KEEP=failed).
/// </summary>
[Binding]
public sealed class ScenarioHooks(IObjectContainer container, ScenarioContext scenario, IUnitTestRuntimeProvider runtime)
{
    private DriverSet? _drivers;

    // The first hook of a scenario: a hook of a step class can use the drivers.
    [BeforeScenario(Order = 0)]
    public void BeginTheScenario()
    {
        var eligibility = TestStart.Decide(scenario.ScenarioInfo.CombinedTags);
        if (eligibility is Eligibility.Skipped skipped)
        {
            runtime.TestIgnore(skipped.Reason);
            return;
        }

        _drivers = RunHooks.Session.BeginTest(((Eligibility.Eligible)eligibility).Host);
        container.RegisterInstanceAs(_drivers);
        container.RegisterInstanceAs(_drivers.Workspace);
        container.RegisterInstanceAs(_drivers.Cli);
        container.RegisterInstanceAs(_drivers.Verification);
        container.RegisterInstanceAs(_drivers.Generation);
        container.RegisterInstanceAs(_drivers.LibraryHosts);
        container.RegisterInstanceAs(_drivers.BuiltFiles);
    }

    [AfterScenario(Order = int.MaxValue)]
    public async Task EndTheScenario()
    {
        if (_drivers is not null) await TestStart.EndAsync(_drivers, failed: scenario.TestError is not null);
    }
}
