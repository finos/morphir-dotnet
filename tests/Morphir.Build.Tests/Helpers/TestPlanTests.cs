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
        TestPlan.Suites.Where(suite => suite.UsesSourceProjects).Select(suite => suite.Name).Should().BeEquivalentTo(
            "Morphir.TestSupport.Tests",
            "Morphir.Mep.Tests",
            "Morphir.Frontend.FSharp.Extension.Tests",
            "Morphir.Myriad.Tests",
            // The package driver packs projects of the repository with no build: it reads their obj and bin folders.
            "Morphir.BlackBox.Tests (integration)",
            "Morphir.BlackBox.Tests (e2e)");

    [Test]
    public void ThePackagesLaneRunSelectsOnlyTheTestsOfThePackagesLane()
    {
        var run = TestPlan.PackagesLaneRun;

        run.Suite.Project.Should().Be(TestPlan.BlackBoxProject);
        run.Suite.Group.Should().BeNull("the lane is not a group: it has the tests of each group that need the packed packages");
        run.Suite.TakesHangDump.Should().BeTrue();
        run.Arguments.Should().Equal("--treenode-filter", "/*/*/*/*[Category=packages]");
    }

    [Test]
    public void ThePackagesLaneRunGetsItsLaneAndTheFolderOfThePackedPackages()
    {
        var environment = TestPlan.PackagesLaneEnvironment("/repo/artifacts/packages");

        environment.Should().Equal(new Dictionary<string, string>
        {
            ["MORPHIR_TEST_LANE"] = "packages",
            ["MORPHIR_TEST_PACKAGES"] = "/repo/artifacts/packages",
        });
    }

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
    public void TheDefaultParallelismIsHalfTheProcessorsRoundedUpFromOneToFour()
    {
        TestPlan.DefaultParallelism(1).Should().Be(1);
        TestPlan.DefaultParallelism(2).Should().Be(1);
        // The macOS runner of GitHub Actions has 3 processors.
        TestPlan.DefaultParallelism(3).Should().Be(2);
        TestPlan.DefaultParallelism(4).Should().Be(2);
        TestPlan.DefaultParallelism(8).Should().Be(4);
        TestPlan.DefaultParallelism(18).Should().Be(4);
    }

    [Test]
    public void TheSuitesOfTheTestTargetAreUnique()
    {
        TestPlan.Suites.Should().HaveCount(38);
        TestPlan.Suites.Select(suite => suite.Name).Should().OnlyHaveUniqueItems();
        TestPlan.Suites.Where(suite => suite.Group is null).Select(suite => suite.Project).Should().OnlyHaveUniqueItems();
    }

    // A group of the black-box project is a suite (design GRP-1 of the change consolidate-black-box-tests).

    private static readonly TestSuite Integration = TestSuite.TUnit("H.BlackBox.Tests").InGroup("integration");
    private static readonly TestSuite SmokeGroup = TestSuite.TUnit("H.BlackBox.Tests").InGroup("smoke");
    private const string IntegrationFilter = "/*/*/*/*[(Category=integration)&(Category!=lane)]";

    [Test]
    public void AGroupSuiteHasItsGroupInItsNameAndInItsResultDirectory()
    {
        Integration.Project.Should().Be("H.BlackBox.Tests");
        Integration.Group.Should().Be("integration");
        Integration.Name.Should().Be("H.BlackBox.Tests (integration)");
        Integration.ResultsDirectoryName.Should().Be("H.BlackBox.Tests.integration");
        Unit.Group.Should().BeNull();
        Unit.Name.Should().Be("A.Tests");
        Unit.ResultsDirectoryName.Should().Be("A.Tests");
    }

    [Test]
    public void AGroupSuiteSelectsItsGroupAndNoTestOfALaneInTheScopeAll()
    {
        var run = TestPlan.Select(TestScope.All, [Integration], _ => false).Single();

        run.Arguments.Should().Equal("--treenode-filter", IntegrationFilter);
    }

    [Test]
    public void AGroupSuiteHasOneFilterAndNeverTwoInTheScopePlatform()
    {
        var run = TestPlan.Select(TestScope.Platform, [Integration], _ => true).Single();

        run.Arguments.Should().Equal("--treenode-filter", IntegrationFilter);
        run.Arguments.Count(argument => argument == "--treenode-filter").Should().Be(1);
    }

    [Test]
    public void AGroupSuiteKeepsItsOwnArguments()
    {
        var suite = TestSuite.TUnit("H.BlackBox.Tests", "--maximum-parallel-tests", "1").InGroup("smoke");

        TestPlan.Select(TestScope.All, [suite], _ => false).Single().Arguments
            .Should().Equal("--maximum-parallel-tests", "1", "--treenode-filter", "/*/*/*/*[(Category=smoke)&(Category!=lane)]");
    }

    [Test]
    public void AGroupCanUseSourceProjectsAndCanRunAlone()
    {
        var sources = Integration.WithSourceProjects();
        var alone = Integration.Alone();

        sources.Group.Should().Be("integration");
        sources.UsesSourceProjects.Should().BeTrue();
        alone.Group.Should().Be("integration");
        alone.RunsAlone.Should().BeTrue();
        TestSuite.TUnit("H.BlackBox.Tests").WithSourceProjects().InGroup("e2e").UsesSourceProjects.Should().BeTrue();
        TestPlan.NextRunnable(Runs(sources), running: [OtherSources]).Should().BeNull();
    }

    [Test]
    public void TwoGroupsOfOneProjectAreTwoRuns()
    {
        var runs = TestPlan.Select(TestScope.All, [Integration, SmokeGroup], _ => false);

        runs.Should().HaveCount(2);
        runs.Select(run => run.Suite.Project).Should().Equal("H.BlackBox.Tests", "H.BlackBox.Tests");
        runs.Select(run => run.Suite.ResultsDirectoryName).Should().OnlyHaveUniqueItems();
        Integration.Should().NotBe(SmokeGroup);
    }

    [Test]
    public void OnlyATUnitSuiteHasAGroup()
    {
        var group = () => TestSuite.Expecto("C.Tests").InGroup("smoke");

        group.Should().Throw<InvalidOperationException>().WithMessage("*TUnit*");
    }

    [Test]
    public void AnUnknownGroupIsRefused()
    {
        var group = () => TestSuite.TUnit("H.BlackBox.Tests").InGroup("unit");

        group.Should().Throw<ArgumentException>().WithMessage("*integration, e2e, smoke, journey*");
        TestPlan.Groups.Should().Equal("integration", "e2e", "smoke", "journey");
    }

    [Test]
    public void TheOptionTestGroupSelectsOnlyTheSuitesOfThatGroup()
    {
        TestSuite[] suites = [Unit, Integration, Lists, SmokeGroup];

        TestPlan.SuitesOf("smoke", suites).Should().Equal(SmokeGroup);
        TestPlan.SuitesOf(null, suites).Should().Equal(suites);
        TestPlan.SuitesOf(" Smoke ", suites).Should().Equal(SmokeGroup);
    }

    [Test]
    public void AGroupThatIsNotAGroupOrThatHasNoSuiteIsAnError()
    {
        var unknown = () => TestPlan.SuitesOf("unit", [Unit, SmokeGroup]);
        var empty = () => TestPlan.SuitesOf("journey", [Unit, SmokeGroup]);

        unknown.Should().Throw<ArgumentException>().WithMessage("*'unit'*").WithMessage("*integration, e2e, smoke, journey*");
        empty.Should().Throw<ArgumentException>().WithMessage("*journey*").WithMessage("*no suite*");
    }

    [Test]
    [Arguments("integration")]
    [Arguments("e2e")]
    [Arguments("smoke")]
    [Arguments("journey")]
    public void AGroupOfTheBlackBoxProjectThatHasATestIsASuiteOfThePlan(string group)
    {
        var suite = TestPlan.SuitesOf(group, TestPlan.Suites).Should().ContainSingle().Subject;

        suite.Project.Should().Be("Morphir.BlackBox.Tests");
        suite.Framework.Should().Be(TestFramework.TUnit);
    }

    // The fixed share of the process limit (design DRV-4).

    [Test]
    [Arguments(8, 2, 2, 4)]
    [Arguments(8, 3, 3, 2)]
    [Arguments(1, 2, 1, 1)]
    [Arguments(8, 1, 1, 8)]
    [Arguments(3, 4, 3, 1)]
    public void EachLaneGetsAFixedShareOfTheMachineLimit(int machine, int parallelism, int lanes, int share)
    {
        var limit = TestPlan.ShareProcessLimit(machine, parallelism);

        limit.Lanes.Should().Be(lanes);
        limit.Share.Should().Be(share);
        (limit.Lanes * limit.Share).Should().BeLessThanOrEqualTo(machine);
        limit.Of(Unit).Should().Be(share);
    }

    [Test]
    public void ASuiteThatRunsAloneGetsTheMachineLimit() =>
        TestPlan.ShareProcessLimit(machineLimit: 8, parallelism: 3).Of(Timed).Should().Be(8);

    [Test]
    public void ASuiteThatStartsInALaneThatAnotherSuiteLeftGetsTheSameShare()
    {
        var limit = TestPlan.ShareProcessLimit(machineLimit: 8, parallelism: 2);

        // The share is a property of the run, not of the moment: the first suite and a later suite get the same.
        limit.Of(Unit).Should().Be(4);
        limit.Of(Serial).Should().Be(4);
        limit.Of(Integration).Should().Be(4);
    }

    [Test]
    [Arguments(null, 18, 9)]
    [Arguments("", 4, 2)]
    [Arguments("6", 18, 6)]
    [Arguments("0", 2, 2)]
    [Arguments("many", 3, 2)]
    public void TheMachineLimitIsTheVariableOfTheCallerOrTheDefaultOfTheProcessGate(string? variable, int processors, int limit) =>
        TestPlan.MachineProcessLimit(variable, processors).Should().Be(limit);

    [Test]
    public void ALimitOrAParallelismBelowOneIsRefused()
    {
        var noLimit = () => TestPlan.ShareProcessLimit(machineLimit: 0, parallelism: 2);
        var noLane = () => TestPlan.ShareProcessLimit(machineLimit: 8, parallelism: 0);

        noLimit.Should().Throw<ArgumentOutOfRangeException>();
        noLane.Should().Throw<ArgumentOutOfRangeException>();
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

    [Test]
    public void ALaneRunSelectsEachGroupOfTheBlackBoxProjectWithTheFilterOfTheLane()
    {
        var run = TestPlan.LaneRun("native-aot");

        run.Suite.Project.Should().Be("Morphir.BlackBox.Tests");
        run.Suite.Group.Should().BeNull();
        run.Arguments.Should().Equal("--treenode-filter", "/*/*/*/*[Category=native-aot]");
        ((Action)(() => TestPlan.LaneRun("none"))).Should().Throw<ArgumentException>().WithMessage("*packages, native-aot, published*");
    }

    [Test]
    [Arguments("Skipped tests by reason: lane 2, host 0, capability 1", "lane 2, host 0, capability 1")]
    [Arguments("[10:01:02 INF] Skipped tests by reason: lane 0, host 0, capability 0 ", "lane 0, host 0, capability 0")]
    [Arguments("Test run summary: Passed!", null)]
    public void TheSkipCountsOfABlackBoxRunAreReadFromItsOutput(string line, string? counts) =>
        TestPlan.SkipCountsOf(line).Should().Be(counts);

    private static IEnumerable<string> ArgumentsOf(string project) =>
        TestPlan.Suites.Single(suite => suite.Project == project).Arguments;
}
