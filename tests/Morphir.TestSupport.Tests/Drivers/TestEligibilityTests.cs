// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The one decision that says if a test runs: the lane, then the host, then the capabilities, with one skip reason
/// (design GRP-1 and DRV-5 of the change consolidate-black-box-tests).
/// </summary>
public class TestEligibilityTests
{
    private static readonly TestTarget Development = new TestTarget.DevelopmentBuild();
    private static readonly TestTarget NativeAot = new TestTarget.NativeAot();

    private static RunProfile Run(TestLane lane = TestLane.None, TestTarget? target = null, MorphirHost? defaultHost = null)
    {
        target ??= Development;
        return new RunProfile(lane, target, defaultHost ?? target.DefaultHost, host => DriverCapabilities.Of(target, host));
    }

    private static TestNeeds Needs(params string[] tags) => TestNeeds.FromTags(tags);

    private static Eligibility.Skipped Skipped(Eligibility eligibility) =>
        eligibility.Should().BeOfType<Eligibility.Skipped>().Subject;

    [Test]
    public void ATestWithNoTagNeedsNoLaneNoHostAndNoCapability()
    {
        var needs = Needs("integration", "generation", "PlatformSpecific");

        needs.Lane.Should().Be(TestLane.None);
        needs.Host.Should().BeNull();
        needs.Capabilities.Should().BeEmpty();
    }

    [Test]
    public void TheTagsOfATestGiveItsLaneItsHostAndItsCapabilities()
    {
        var needs = Needs("e2e", "lane", "packages", "host-managed", "needs-generation", "needs-in-process-fsharp-frontend");

        needs.Lane.Should().Be(TestLane.Packages);
        needs.Host.Should().Be(MorphirHost.Managed);
        needs.Capabilities.Should().Equal(DriverCapability.Generation, DriverCapability.InProcessFSharpFrontend);
    }

    [Test]
    [Arguments("host-native", MorphirHost.Native)]
    [Arguments("host-managed", MorphirHost.Managed)]
    [Arguments("host-tool", MorphirHost.DotnetTool)]
    [Arguments("Host-Tool", MorphirHost.DotnetTool)]
    public void AHostTagNamesTheHost(string tag, MorphirHost host) => Needs(tag).Host.Should().Be(host);

    [Test]
    [Arguments("native-aot", TestLane.NativeAot)]
    [Arguments("published", TestLane.Published)]
    public void ALaneTagNamesTheLane(string tag, TestLane lane) => Needs("lane", tag).Lane.Should().Be(lane);

    [Test]
    public void EachCapabilityHasATag()
    {
        foreach (var capability in DriverCapabilities.All)
            Needs(DriverCapabilities.TagOf(capability)).Capabilities.Should().Equal(capability);

        DriverCapabilities.TagOf(DriverCapability.SelfContainedFrontend).Should().Be("needs-self-contained-frontend");
    }

    [Test]
    [Arguments(new[] { "lane", "packages", "published" }, "two lanes: packages, published")]
    [Arguments(new[] { "host-managed", "host-tool" }, "two hosts: managed, tool")]
    [Arguments(new[] { "needs-warp-drive" }, "needs-warp-drive")]
    [Arguments(new[] { "host-jit" }, "host-jit")]
    public void TagsThatDoNotGiveOneNeedAreAnErrorOfTheTest(string[] tags, string reason)
    {
        var parse = () => Needs(tags);

        parse.Should().Throw<TestSessionException>().WithMessage($"*{reason}*");
    }

    // The lane table of design GRP-1: the lane of the run, the lane of the test, and if the run executes the test.
    [Test]
    [Arguments(TestLane.None, TestLane.None, true)]
    [Arguments(TestLane.None, TestLane.Packages, false)]
    [Arguments(TestLane.None, TestLane.NativeAot, false)]
    [Arguments(TestLane.None, TestLane.Published, false)]
    [Arguments(TestLane.Packages, TestLane.None, false)]
    [Arguments(TestLane.Packages, TestLane.Packages, true)]
    [Arguments(TestLane.Packages, TestLane.NativeAot, false)]
    [Arguments(TestLane.Packages, TestLane.Published, false)]
    [Arguments(TestLane.NativeAot, TestLane.None, false)]
    [Arguments(TestLane.NativeAot, TestLane.Packages, false)]
    [Arguments(TestLane.NativeAot, TestLane.NativeAot, true)]
    [Arguments(TestLane.NativeAot, TestLane.Published, false)]
    [Arguments(TestLane.Published, TestLane.None, true)]
    [Arguments(TestLane.Published, TestLane.Packages, false)]
    [Arguments(TestLane.Published, TestLane.NativeAot, false)]
    [Arguments(TestLane.Published, TestLane.Published, true)]
    public void ARunExecutesTheTestsOfItsLane(TestLane run, TestLane test, bool executes)
    {
        var needs = new TestNeeds(test, Host: null, Capabilities: []);

        var eligibility = TestEligibility.Decide(needs, Run(run));

        if (executes)
        {
            eligibility.Should().Be(new Eligibility.Eligible(MorphirHost.Native));
        }
        else
        {
            var skipped = Skipped(eligibility);
            skipped.Kind.Should().Be(SkipKind.Lane);
            skipped.Reason.Should().Be($"lane: test {test.Name()}, run {run.Name()}");
        }
    }

    [Test]
    public void ATestWithNoHostTagRunsOnTheDefaultHostOfTheRun()
    {
        TestEligibility.Decide(Needs(), Run()).Should().Be(new Eligibility.Eligible(MorphirHost.Native));
        TestEligibility.Decide(Needs(), Run(defaultHost: MorphirHost.DotnetTool)).Should().Be(new Eligibility.Eligible(MorphirHost.DotnetTool));
    }

    [Test]
    public void ATestThatNamesAHostRunsOnThatHostAndNotOnTheDefaultHost() =>
        TestEligibility.Decide(Needs("host-managed"), Run()).Should().Be(new Eligibility.Eligible(MorphirHost.Managed));

    [Test]
    public void ATestThatNamesAHostThatTheTargetDoesNotHaveIsSkippedWithTheHostAndTheTarget()
    {
        var skipped = Skipped(TestEligibility.Decide(Needs("host-managed"), Run(TestLane.Published, NativeAot)));

        skipped.Kind.Should().Be(SkipKind.Host);
        skipped.Reason.Should().Be("host: managed is not in target aot");
    }

    [Test]
    public void ATestWithNoHostTagIsNotSkippedForItsHostOnATargetWithOneHost()
    {
        var target = new TestTarget.Packaged(PackagedOffering.SelfContained);

        TestEligibility.Decide(Needs(), Run(TestLane.Published, target)).Should().Be(new Eligibility.Eligible(MorphirHost.Managed));
    }

    [Test]
    public void AnUnsupportedCapabilityIsSkippedWithTheCapabilityTheTargetAndTheHost()
    {
        var skipped = Skipped(TestEligibility.Decide(Needs("needs-generation", "needs-self-contained-frontend"), Run()));

        skipped.Kind.Should().Be(SkipKind.Capability);
        skipped.Reason.Should().Be("capability: SelfContainedFrontend on target dev, host native");
    }

    [Test]
    public void ACapabilityOfAnotherHostIsNotACapabilityOfTheHostOfTheTest()
    {
        TestEligibility.Decide(Needs("needs-in-process-fsharp-frontend", "host-tool"), Run())
            .Should().Be(new Eligibility.Eligible(MorphirHost.DotnetTool));

        Skipped(TestEligibility.Decide(Needs("needs-in-process-fsharp-frontend"), Run())).Reason
            .Should().Be("capability: InProcessFSharpFrontend on target dev, host native");
    }

    [Test]
    public void ATestForWhichTwoChecksFailGetsTheFirstAsItsOneReason()
    {
        // The lane and the host both fail: the reason is the lane.
        var laneAndHost = Skipped(TestEligibility.Decide(Needs("lane", "packages", "host-managed"), Run(TestLane.None, NativeAot)));
        laneAndHost.Kind.Should().Be(SkipKind.Lane);
        laneAndHost.Reason.Should().Be("lane: test packages, run none");

        // The host and the capability both fail: the reason is the host.
        var hostAndCapability = Skipped(TestEligibility.Decide(
            Needs("host-managed", "needs-self-contained-frontend"), Run(TestLane.Published, NativeAot)));
        hostAndCapability.Kind.Should().Be(SkipKind.Host);
    }

    [Test]
    public void TheDevelopmentBuildSupportsVerificationAndGenerationOnEachHost()
    {
        foreach (var host in MorphirHosts.All)
            DriverCapabilities.Of(Development, host).Should().Contain([DriverCapability.ModelVerification, DriverCapability.Generation]);
    }

    [Test]
    public void TheFSharpFrontendIsInTheManagedHostAndInTheToolHost()
    {
        DriverCapabilities.Of(Development, MorphirHost.Native).Should().NotContain(DriverCapability.InProcessFSharpFrontend);
        DriverCapabilities.Of(Development, MorphirHost.Managed).Should().Contain(DriverCapability.InProcessFSharpFrontend);
        DriverCapabilities.Of(Development, MorphirHost.DotnetTool).Should().Contain(DriverCapability.InProcessFSharpFrontend);
    }

    [Test]
    public void OnlyTheDevelopmentBuildHasTheBuiltPrograms()
    {
        foreach (var host in MorphirHosts.All)
            DriverCapabilities.Of(Development, host).Should().Contain(DriverCapability.BuiltPrograms);

        DriverCapabilities.Of(NativeAot, MorphirHost.Native).Should().NotContain(DriverCapability.BuiltPrograms);
        DriverCapabilities.TagOf(DriverCapability.BuiltPrograms).Should().Be("needs-built-programs");
    }

    [Test]
    public void AHostThatATargetDoesNotHaveSupportsNoCapability() =>
        DriverCapabilities.Of(NativeAot, MorphirHost.Managed).Should().BeEmpty();

    [Test]
    public void TheSkipsOfARunAreCountedByTheKindOfReason()
    {
        var skips = new SkipCounts();
        skips.Add(new Eligibility.Skipped(SkipKind.Lane, "lane: test packages, run none"));
        skips.Add(new Eligibility.Skipped(SkipKind.Lane, "lane: test published, run none"));
        skips.Add(new Eligibility.Skipped(SkipKind.Capability, "capability: SelfContainedFrontend on target dev, host native"));

        skips.Of(SkipKind.Lane).Should().Be(2);
        skips.Of(SkipKind.Host).Should().Be(0);
        skips.Of(SkipKind.Capability).Should().Be(1);
        skips.ReportLine().Should().Be("Skipped tests by reason: lane 2, host 0, capability 1");
        SkipCounts.TryParseReportLine("  Skipped tests by reason: lane 2, host 0, capability 1", out var parsed).Should().BeTrue();
        parsed.Should().Equal(new Dictionary<SkipKind, int> { [SkipKind.Lane] = 2, [SkipKind.Host] = 0, [SkipKind.Capability] = 1 });
        SkipCounts.TryParseReportLine("Test run summary: Passed!", out _).Should().BeFalse();
    }
}
