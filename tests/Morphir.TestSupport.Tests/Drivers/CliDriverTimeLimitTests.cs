// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The time limit of a command is for the command: a test runner runs many commands through the process gate, so a
/// command can wait for a place longer than its own limit, and the wait must not use the limit. From
/// GenerationReportingTests.OperationalCliFailureBudgetStartsAfterTheProcessSlotIsGranted of Morphir.Tooling.Tests,
/// which tested CliTestHelper.
/// </summary>
[Category("PlatformSpecific")]
public class CliDriverTimeLimitTests
{
    [Test]
    public async Task TheTimeLimitStartsWhenTheCommandHasAPlaceOfTheGate()
    {
        await using var workspace = ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        var gate = new ProcessGate(1);
        var driver = new CliDriver(new TestTarget.DevelopmentBuild(), MorphirHost.Native,
            CliArtifacts.OfDevelopmentBuild(typeof(CliDriverTimeLimitTests).Assembly).PathOf(MorphirHost.Native), workspace, gate);
        var limit = TimeSpan.FromSeconds(5);
        var held = await gate.EnterAsync();

        var run = driver.RunAsync(["generate", "--input", workspace.File("missing.json").Path, "--target", "fsharp", "--output", workspace.Directory("out").Path, "--json"],
            new CliRunOptions { Timeout = limit });
        await Task.Delay(limit + TimeSpan.FromSeconds(2));
        held.Dispose();
        var result = await run;

        result.ShouldExitWith(2);
        GenerationReport.Of(result).ShouldFail("operational");
        workspace.Exists(workspace.Directory("out")).Should().BeFalse();
    }
}
