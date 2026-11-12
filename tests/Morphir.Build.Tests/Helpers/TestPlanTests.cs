// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The plan of the Test target: which test assemblies a scope runs, and the arguments that select the tests with the
/// <c>PlatformSpecific</c> marker.
/// </summary>
public class TestPlanTests
{
    private static readonly TestSuite Unit = TestSuite.TUnit("A.Tests");
    private static readonly TestSuite Serial = TestSuite.TUnit("B.Tests", "--maximum-parallel-tests", "1");
    private static readonly TestSuite Lists = TestSuite.Expecto("C.Tests", "--sequenced");
    private static readonly TestSuite Smoke = TestSuite.Program("D.Smoke");
    private static readonly TestSuite[] Suites = [Unit, Serial, Lists, Smoke];
    private static readonly TestSuite Sources = TestSuite.Expecto("E.Tests").WithSourceProjects();
    private static readonly TestSuite OtherSources = TestSuite.TUnit("F.Tests").WithSourceProjects();
    private static readonly TestSuite Timed = TestSuite.Expecto("G.Tests").Alone();

    private static TestRun[] Runs(params TestSuite[] suites) => [.. TestPlan.Select(TestScope.All, suites, _ => false)];

    [Test]
    [Arguments("all")]
    [Arguments(" All ")]
    public void TheScopeAllParses(string input)
    {
        TestScope.TryParse(input, out var scope).Should().BeTrue();
        scope.Should().Be(TestScope.All);
    }

    [Test]
    public void TheScopePlatformParses()
    {
        TestScope.TryParse("platform", out var scope).Should().BeTrue();
        scope.Should().Be(TestScope.Platform);
    }

    [Test]
    [Arguments("")]
    [Arguments("windows")]
    [Arguments("all,platform")]
    public void AnotherScopeDoesNotParse(string input) =>
        TestScope.TryParse(input, out _).Should().BeFalse();

    [Test]
    public void TheScopeAllRunsEverySuiteWithItsOwnArguments()
    {
        var runs = TestPlan.Select(TestScope.All, Suites, _ => false);

        runs.Select(run => run.Suite).Should().Equal(Suites);
        runs.Select(run => run.Arguments.ToArray()).Should().BeEquivalentTo(
            new[] { [], ["--maximum-parallel-tests", "1"], ["--sequenced"], Array.Empty<string>() },
            options => options.WithStrictOrdering());
    }

    [Test]
    public void TheScopePlatformRunsOnlyTheSuitesThatHaveMarkedTests()
    {
        var runs = TestPlan.Select(TestScope.Platform, Suites, suite => suite == Serial || suite == Lists);

        runs.Select(run => run.Suite).Should().Equal(Serial, Lists);
    }

    [Test]
    public void TheScopePlatformSelectsTheCategoryOfATUnitSuite()
    {
        var run = TestPlan.Select(TestScope.Platform, [Serial], _ => true).Single();

        run.Arguments.Should().Equal(
            "--maximum-parallel-tests", "1", "--treenode-filter", "/*/*/*/*[Category=PlatformSpecific]");
    }

    [Test]
    public void TheScopePlatformSelectsTheLabelOfAnExpectoSuite()
    {
        var run = TestPlan.Select(TestScope.Platform, [Lists], _ => true).Single();

        run.Arguments.Should().Equal("--sequenced", "--filter-test-list", "PlatformSpecific");
    }

    [Test]
    public void TheScopePlatformDoesNotRunAProgramBecauseItHasNoTestFilter() =>
        TestPlan.Select(TestScope.Platform, [Smoke], _ => true).Should().BeEmpty();

    [Test]
    public void OnlyTUnitSuitesTakeAHangDump()
    {
        Unit.TakesHangDump.Should().BeTrue();
        Lists.TakesHangDump.Should().BeFalse();
        Smoke.TakesHangDump.Should().BeFalse();
    }

    [Test]
    public void TheSuitesThatRunMSBuildOnSourceProjectsAreKnown() =>
        TestPlan.Suites.Where(suite => suite.UsesSourceProjects).Select(suite => suite.Project).Should().BeEquivalentTo(
            "Morphir.SDK.Tests",
            "Morphir.Mep.Tests",
            "Morphir.Frontend.FSharp.Extension.Tests",
            "Morphir.CodeGeneration.FSharp.Extension.Tests",
            "Morphir.Myriad.Tests",
            "Morphir.Myriad.Integration.Tests",
            "Morphir.Myriad.SystemTextJson.Tests");

    [Test]
    public void TheSuitesWithTimeBudgetsRunAlone() =>
        TestPlan.Suites.Where(suite => suite.RunsAlone).Select(suite => suite.Project).Should().Equal("Morphir.Extensions.Wasm.Tests", "Morphir.Toolchain.Tests");

    [Test]
    public void TheNextRunIsTheFirstThatWaits()
    {
        var pending = Runs(Unit, Sources, Lists);

        TestPlan.NextRunnable(pending, running: []).Should().Be(pending[0]);
        TestPlan.NextRunnable(pending, running: [Serial]).Should().Be(pending[0]);
    }

    [Test]
    public void ASuiteThatUsesSourceProjectsStartsWhenNoOtherOneRuns()
    {
        var pending = Runs(Sources, Unit);

        TestPlan.NextRunnable(pending, running: [Serial]).Should().Be(pending[0]);
    }

    [Test]
    public void ASuiteThatUsesSourceProjectsWaitsWhileAnotherOneRuns()
    {
        var pending = Runs(Sources, Unit);

        TestPlan.NextRunnable(pending, running: [OtherSources]).Should().Be(pending[1]);
        TestPlan.NextRunnable(Runs(Sources), running: [OtherSources]).Should().BeNull();
    }

    [Test]
    public void ASuiteThatRunsAloneWaitsUntilNoSuiteRuns()
    {
        var pending = Runs(Timed, Unit);

        TestPlan.NextRunnable(pending, running: [Serial]).Should().Be(pending[1]);
        TestPlan.NextRunnable(Runs(Timed), running: [Serial]).Should().BeNull();
        TestPlan.NextRunnable(pending, running: []).Should().Be(pending[0]);
    }

    [Test]
    public void NoSuiteStartsWhileASuiteThatRunsAloneRuns() =>
        TestPlan.NextRunnable(Runs(Unit, Sources), running: [Timed]).Should().BeNull();

    [Test]
    public void NoRunStartsWhenNoRunWaits() =>
        TestPlan.NextRunnable([], running: []).Should().BeNull();

    [Test]
    public void TheDefaultParallelismIsHalfTheProcessorsFromOneToFour()
    {
        TestPlan.DefaultParallelism(1).Should().Be(1);
        TestPlan.DefaultParallelism(3).Should().Be(1);
        TestPlan.DefaultParallelism(4).Should().Be(2);
        TestPlan.DefaultParallelism(8).Should().Be(4);
        TestPlan.DefaultParallelism(18).Should().Be(4);
    }

    [Test]
    public void TheSuitesOfTheTestTargetAreUnique()
    {
        TestPlan.Suites.Should().HaveCount(35);
        TestPlan.Suites.Select(suite => suite.Project).Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void TheSuitesKeepTheArgumentsThatTheirTestsNeed()
    {
        ArgumentsOf("Morphir.Configuration.Tests").Should().Equal("--sequenced");
        ArgumentsOf("Morphir.CodeGeneration.CSharp.Tests").Should().Equal("--sequenced");
        ArgumentsOf("Morphir.Configuration.IO.Tests").Should().Equal("--maximum-parallel-tests", "1");
        ArgumentsOf("Morphir.Configuration.Microsoft.Tests").Should().Equal("--maximum-parallel-tests", "1");
    }

    [Test]
    [Arguments("[Category(\"PlatformSpecific\")]", true)]
    [Arguments("|> testLabel \"PlatformSpecific\"", true)]
    [Arguments("@PlatformSpecific @CLI", true)]
    [Arguments("[assembly: TUnit.Core.Category(\"PlatformSpecific\")]", true)]
    [Arguments("testList \"PlatformSpecific\" [", true)]
    [Arguments("[Category(\"CLI\")]", false)]
    // PlatformSpecific is also a case of a Morphir IR type, so the word alone is not the marker.
    [Arguments("Hint = PlatformSpecific \"jvm\"", false)]
    [Arguments("{ \"PlatformSpecific\": { \"platform\": \"wasm\" } }", false)]
    public void ASourceTextWithTheMarkerHasPlatformTests(string text, bool expected) =>
        TestPlan.MentionsPlatformMarker(text).Should().Be(expected);

    [Test]
    [Arguments("[04:49:13 INF] EXPECTO! 0 tests run in 00:00:00.0047448 for miscellaneous – 0 passed, 0 ignored", true)]
    [Arguments("[04:49:13 INF] EXPECTO! 10 tests run in 00:00:00.0910861 for miscellaneous – 10 passed", false)]
    [Arguments("[04:49:13 INF] EXPECTO! 143 tests run in 00:00:35.9465402 for PlatformSpecific – 143 passed", false)]
    public void AnExpectoRunThatSelectsNoTestIsFound(string line, bool expected) =>
        TestPlan.ReportsNoTests(line).Should().Be(expected);

    private static IEnumerable<string> ArgumentsOf(string project) =>
        TestPlan.Suites.Single(suite => suite.Project == project).Arguments;
}
