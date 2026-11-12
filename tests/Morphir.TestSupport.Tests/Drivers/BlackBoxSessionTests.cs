// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The session of a black-box run: it reads the environment once, resolves the target to its own artifact before
/// the first test, and makes a workspace and a driver set for each test (design DRV-2 and DRV-4 of the change
/// consolidate-black-box-tests).
/// </summary>
[Category("PlatformSpecific")]
public class BlackBoxSessionTests
{
    private static readonly System.Reflection.Assembly ThisAssembly = typeof(BlackBoxSessionTests).Assembly;

    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static Func<string, string?> Variables(params (string Name, string Value)[] variables)
    {
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
        return environment.GetValueOrDefault;
    }

    private static BlackBoxSession Session(params (string Name, string Value)[] variables) =>
        BlackBoxSession.Start(ThisAssembly, Variables(variables), File.Exists, Fixtures);

    [Test]
    public void ASessionWithNoSettingIsTheDevelopmentBuildOfTheTestAssembly()
    {
        var session = Session();

        session.Settings.Should().Be(TestRunSettings.Default);
        session.Profile.Lane.Should().Be(TestLane.None);
        session.Artifacts.Configuration.Should().Be(CliArtifacts.OfDevelopmentBuild(ThisAssembly).Configuration);
    }

    [Test]
    public void TheEnvironmentIsReadOnceWhenTheSessionStarts()
    {
        var reads = new List<string>();
        var session = BlackBoxSession.Start(ThisAssembly, name => { reads.Add(name); return null; }, File.Exists, Fixtures);
        var count = reads.Count;

        session.Decide(TestNeeds.None);
        _ = session.Profile;

        reads.Should().HaveCount(count).And.Contain(["MORPHIR_TEST_TARGET", "MORPHIR_TEST_HOST", "MORPHIR_TEST_LANE", "MORPHIR_TEST_KEEP"]);
    }

    [Test]
    public void AWrongSettingStopsTheSessionBeforeTheFirstTest()
    {
        var start = () => Session(("MORPHIR_TEST_TARGET", "fast"));

        start.Should().Throw<TestSessionException>().WithMessage("*MORPHIR_TEST_TARGET*");
    }

    [Test]
    public void AMissingArtifactStopsTheSessionWithTheBuildCommandAndNoOtherFormOrConfigurationIsUsed()
    {
        var metadata = new Dictionary<string, string>
        {
            ["Morphir.TestSupport.Cli.Native"] = "/repo/src/Morphir/bin/Debug/net11.0/morphir.dll",
            ["Morphir.TestSupport.Cli.Managed"] = "/repo/src/Morphir.Managed/bin/Debug/net11.0/morphir.dll",
            ["Morphir.TestSupport.Cli.Tool"] = "/repo/src/Morphir.Tool/bin/Debug/net11.0/dotnet-morphir.dll",
            ["Morphir.TestSupport.Cli.Configuration"] = "Debug",
        };
        var asked = new List<string>();

        // The Release build and the published forms are there. The Debug build of the test assembly is not.
        var start = () => BlackBoxSession.Start(metadata, "A.Tests", Variables(), path =>
        {
            asked.Add(path);
            return !path.Contains("/Debug/", StringComparison.Ordinal);
        }, Fixtures);

        start.Should().Throw<TestSessionException>().WithMessage("*dotnet build src/Morphir/Morphir.csproj -c Debug*");
        asked.Should().OnlyContain(path => metadata.ContainsValue(path));
    }

    [Test]
    [Arguments("trimmed")]
    [Arguments("untrimmed")]
    [Arguments("aot")]
    [Arguments("native-aot")]
    [Arguments("self-contained")]
    [Arguments("framework-dependent")]
    [Arguments("dotnet-tool")]
    public void ATargetWithNoDriverYetStopsTheSessionAndDoesNotUseTheDevelopmentBuild(string target)
    {
        var start = () => Session(("MORPHIR_TEST_TARGET", target));

        start.Should().Throw<TestSessionException>().WithMessage($"*'{target}'*").WithMessage("*dev, executable*");
    }

    [Test]
    public async Task AnExecutableAtAPathIsTheOneArtifactOfTheSession()
    {
        var executable = CliArtifacts.OfDevelopmentBuild(ThisAssembly).PathOf(MorphirHost.DotnetTool);
        var session = Session(
            ("MORPHIR_TEST_TARGET", "executable"), ("MORPHIR_TEST_EXECUTABLE", executable), ("MORPHIR_TEST_HOST", "tool"), ("MORPHIR_TEST_LANE", "published"));

        var host = session.Decide(TestNeeds.None).Should().BeOfType<Eligibility.Eligible>().Which.Host;
        await using var drivers = session.BeginTest(host);
        var result = await drivers.Cli.RunAsync(["--version"]);

        host.Should().Be(MorphirHost.DotnetTool);
        result.ShouldExitWith(0);
        result.CommandLine.Should().Contain(executable);
        session.Decide(TestNeeds.FromTags(["host-native"])).Should().BeOfType<Eligibility.Skipped>();
    }

    [Test]
    public async Task EachTestGetsItsOwnWorkspaceAndADriverSetOfItsHost()
    {
        var session = Session();

        await using var first = session.BeginTest(MorphirHost.Native);
        await using var second = session.BeginTest(MorphirHost.Managed);

        first.Workspace.Root.Should().NotBe(second.Workspace.Root);
        first.Host.Should().Be(MorphirHost.Native);
        second.Cli.Host.Should().Be(MorphirHost.Managed);
        first.Capabilities.Should().NotContain(DriverCapability.InProcessFSharpFrontend);
        second.Capabilities.Should().Contain(DriverCapability.InProcessFSharpFrontend);
        (await second.Cli.RunAsync(["--version"])).CommandLine.Should().Contain("Morphir.Managed");
        first.Workspace.CopyFixture("Cli/valid-ir-v3.json");
        (await first.Verification.VerifyAsync(first.Workspace.File("Cli", "valid-ir-v3.json"))).ShouldExitWith(0);
        first.Generation.Should().NotBeNull();
    }

    [Test]
    public void ADriverSetForAHostThatTheTargetDoesNotHaveIsAnError()
    {
        var executable = CliArtifacts.OfDevelopmentBuild(ThisAssembly).PathOf(MorphirHost.Native);
        var session = Session(("MORPHIR_TEST_TARGET", "executable"), ("MORPHIR_TEST_EXECUTABLE", executable), ("MORPHIR_TEST_HOST", "native"));

        var begin = () => session.BeginTest(MorphirHost.Managed);

        begin.Should().Throw<TestSessionException>().WithMessage("host: managed is not in target executable");
    }

    [Test]
    public void TheSessionCountsTheSkipsOfItsDecisions()
    {
        var session = Session();

        session.Decide(TestNeeds.None).Should().BeOfType<Eligibility.Eligible>();
        session.Decide(TestNeeds.FromTags(["lane", "packages"])).Should().BeOfType<Eligibility.Skipped>();
        session.Decide(TestNeeds.FromTags(["needs-self-contained-frontend"])).Should().BeOfType<Eligibility.Skipped>();

        session.Skips.ReportLine().Should().Be("Skipped tests by reason: lane 1, host 0, capability 1");
    }

    [Test]
    public void TheEndOfTheRunWritesTheSkipCountsAndPassesWhenNoTestChangedTheMachine()
    {
        var watched = Directory.CreateTempSubdirectory("morphir-watched-").FullName;
        try
        {
            var session = BlackBoxSession.Start(ThisAssembly, Variables(), File.Exists, Fixtures, MachineTripwire.Capture([watched]));
            session.Decide(TestNeeds.FromTags(["lane", "packages"]));
            var output = new StringWriter();

            session.EndRun(output);

            output.ToString().Trim().Should().Be("Skipped tests by reason: lane 1, host 0, capability 0");
        }
        finally
        {
            TolerantDirectory.TryDelete(watched);
        }
    }

    [Test]
    public void TheEndOfTheRunFailsWhenATestChangedALocationOfTheMachine()
    {
        var watched = Directory.CreateTempSubdirectory("morphir-watched-").FullName;
        try
        {
            var session = BlackBoxSession.Start(ThisAssembly, Variables(), File.Exists, Fixtures, MachineTripwire.Capture([watched]));
            File.WriteAllText(Path.Combine(watched, "escaped.txt"), "escaped");
            var output = new StringWriter();

            var end = () => session.EndRun(output);

            end.Should().Throw<InvalidOperationException>().WithMessage("*escaped its sandbox*").WithMessage("*escaped.txt*");
            output.ToString().Should().Contain("Skipped tests by reason:", "the counts are written before the check of the machine");
        }
        finally
        {
            TolerantDirectory.TryDelete(watched);
        }
    }

    [Test]
    public async Task TheWorkspaceOfATestIsRemovedWhenTheTestEnds()
    {
        var session = Session(("MORPHIR_TEST_KEEP", "failed"));
        var passed = session.BeginTest(MorphirHost.Native);

        var kept = await passed.EndAsync(failed: false);

        kept.Should().BeNull();
        Directory.Exists(passed.Workspace.Root).Should().BeFalse();
    }

    [Test]
    public async Task TheWorkspaceOfAFailedTestStaysWhenTheRunKeepsEvidence()
    {
        var session = Session(("MORPHIR_TEST_KEEP", "failed"));
        var failed = session.BeginTest(MorphirHost.Native);
        try
        {
            var kept = await failed.EndAsync(failed: true);

            kept.Should().Be(failed.Workspace.Root);
            Directory.Exists(failed.Workspace.Root).Should().BeTrue();
        }
        finally
        {
            TolerantDirectory.TryDelete(failed.Workspace.Root);
        }
    }

    [Test]
    public async Task TheWorkspaceOfAFailedTestIsRemovedWhenTheRunDoesNotKeepEvidence()
    {
        var failed = Session().BeginTest(MorphirHost.Native);

        var kept = await failed.EndAsync(failed: true);

        kept.Should().BeNull();
        Directory.Exists(failed.Workspace.Root).Should().BeFalse();
    }
}
