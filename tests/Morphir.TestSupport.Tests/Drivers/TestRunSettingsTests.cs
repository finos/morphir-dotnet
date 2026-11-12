// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The target and the host of a black-box run: the closed sets, the hosts that each target has, and the parser of
/// the run settings (design DRV-2 of the change consolidate-black-box-tests).
/// </summary>
public class TestRunSettingsTests
{
    private const string ExistingFile = "/opt/morphir/morphir";

    private static Dictionary<string, string> Variables(params (string Name, string Value)[] variables) =>
        variables.ToDictionary(variable => variable.Name, variable => variable.Value);

    private static TestRunSettings Parse(params (string Name, string Value)[] variables)
    {
        var environment = Variables(variables);
        return TestRunSettings.Parse(environment.GetValueOrDefault, path => path == ExistingFile);
    }

    private static string ErrorOf(params (string Name, string Value)[] variables)
    {
        var environment = Variables(variables);
        TestRunSettings.TryParse(environment.GetValueOrDefault, path => path == ExistingFile, out _, out var error)
            .Should().BeFalse();
        return error!;
    }

    [Test]
    public void TheDefaultIsTheDevelopmentBuildWithTheNativeHost()
    {
        var settings = Parse();

        settings.Target.Should().Be(new TestTarget.DevelopmentBuild());
        settings.DefaultHost.Should().Be(MorphirHost.Native);
        settings.Lane.Should().Be(TestLane.None);
        settings.KeepFailed.Should().BeFalse();
    }

    [Test]
    public void TheDevelopmentBuildHasTheThreeHosts()
    {
        var target = Parse(("MORPHIR_TEST_TARGET", "dev")).Target;

        target.Should().Be(new TestTarget.DevelopmentBuild());
        target.Name.Should().Be("dev");
        target.Hosts.Should().Equal(MorphirHost.Native, MorphirHost.Managed, MorphirHost.DotnetTool);
        target.DefaultHost.Should().Be(MorphirHost.Native);
    }

    [Test]
    [Arguments("trimmed")]
    [Arguments("untrimmed")]
    [Arguments("aot")]
    [Arguments("native-aot")]
    public void APublishedNativeFormHasOnlyTheNativeHost(string name)
    {
        var settings = Parse(("MORPHIR_TEST_TARGET", name));

        settings.Target.Name.Should().Be(name);
        settings.Target.Hosts.Should().Equal(MorphirHost.Native);
        settings.DefaultHost.Should().Be(MorphirHost.Native);
    }

    [Test]
    public void TheNamesOfThePublishedFormsGiveTheirCases()
    {
        Parse(("MORPHIR_TEST_TARGET", "trimmed")).Target.Should().Be(new TestTarget.Trimmed());
        Parse(("MORPHIR_TEST_TARGET", "untrimmed")).Target.Should().Be(new TestTarget.Untrimmed());
        Parse(("MORPHIR_TEST_TARGET", "aot")).Target.Should().Be(new TestTarget.NativeAot());
        Parse(("MORPHIR_TEST_TARGET", "native-aot")).Target.Should().Be(new TestTarget.Packaged(PackagedOffering.NativeAot));
        Parse(("MORPHIR_TEST_TARGET", "self-contained")).Target.Should().Be(new TestTarget.Packaged(PackagedOffering.SelfContained));
        Parse(("MORPHIR_TEST_TARGET", "framework-dependent")).Target.Should().Be(new TestTarget.Packaged(PackagedOffering.FrameworkDependent));
        Parse(("MORPHIR_TEST_TARGET", "dotnet-tool")).Target.Should().Be(new TestTarget.Packaged(PackagedOffering.DotnetTool));
    }

    [Test]
    [Arguments("self-contained", MorphirHost.Managed)]
    [Arguments("framework-dependent", MorphirHost.Managed)]
    [Arguments("dotnet-tool", MorphirHost.DotnetTool)]
    public void AnOfferingWithOneHostHasThatHostAsTheDefaultWithNoHostSetting(string name, MorphirHost host)
    {
        var settings = Parse(("MORPHIR_TEST_TARGET", name));

        settings.Target.Hosts.Should().Equal(host);
        settings.DefaultHost.Should().Be(host);
    }

    [Test]
    [Arguments("native", MorphirHost.Native)]
    [Arguments("managed", MorphirHost.Managed)]
    [Arguments("tool", MorphirHost.DotnetTool)]
    [Arguments("dotnet-morphir", MorphirHost.DotnetTool)]
    [Arguments(" Managed ", MorphirHost.Managed)]
    public void TheHostSettingChangesTheDefaultHostOfTheDevelopmentBuild(string value, MorphirHost host) =>
        Parse(("MORPHIR_TEST_HOST", value)).DefaultHost.Should().Be(host);

    [Test]
    public void ADefaultHostThatTheTargetDoesNotHaveStopsWithTheHostAndTheHostsOfTheTarget()
    {
        var error = ErrorOf(("MORPHIR_TEST_TARGET", "aot"), ("MORPHIR_TEST_HOST", "managed"));

        error.Should().Contain("MORPHIR_TEST_HOST").And.Contain("managed").And.Contain("aot");
        error.Should().Contain("has the hosts: native");
    }

    [Test]
    public void AnUnknownTargetGivesAMessageThatListsTheValidValues()
    {
        var error = ErrorOf(("MORPHIR_TEST_TARGET", "fast"));

        error.Should().Contain("MORPHIR_TEST_TARGET").And.Contain("'fast'");
        error.Should().Contain("dev, trimmed, untrimmed, aot, native-aot, self-contained, framework-dependent, dotnet-tool, executable");
    }

    [Test]
    public void AnUnknownHostGivesAMessageThatListsTheValidValues()
    {
        var error = ErrorOf(("MORPHIR_TEST_HOST", "jit"));

        error.Should().Contain("MORPHIR_TEST_HOST").And.Contain("'jit'").And.Contain("native, managed, tool");
    }

    [Test]
    public void AnExecutableAtAPathRunsOnTheHostThatTheRunNames()
    {
        var settings = Parse(
            ("MORPHIR_TEST_TARGET", "executable"), ("MORPHIR_TEST_EXECUTABLE", ExistingFile), ("MORPHIR_TEST_HOST", "tool"));

        settings.Target.Should().Be(new TestTarget.Executable(ExistingFile, MorphirHost.DotnetTool));
        settings.Target.Name.Should().Be("executable");
        settings.Target.Hosts.Should().Equal(MorphirHost.DotnetTool);
        settings.DefaultHost.Should().Be(MorphirHost.DotnetTool);
    }

    [Test]
    public void AnExecutableWithNoPathIsAnError() =>
        ErrorOf(("MORPHIR_TEST_TARGET", "executable"), ("MORPHIR_TEST_HOST", "native"))
            .Should().Contain("MORPHIR_TEST_EXECUTABLE").And.Contain("path");

    [Test]
    public void AnExecutableAtAPathWithNoFileIsAnError() =>
        ErrorOf(("MORPHIR_TEST_TARGET", "executable"), ("MORPHIR_TEST_EXECUTABLE", "/opt/none"), ("MORPHIR_TEST_HOST", "native"))
            .Should().Contain("/opt/none").And.Contain("no file");

    [Test]
    public void AnExecutableWithNoHostStops() =>
        ErrorOf(("MORPHIR_TEST_TARGET", "executable"), ("MORPHIR_TEST_EXECUTABLE", ExistingFile))
            .Should().Contain("MORPHIR_TEST_HOST").And.Contain("native, managed, tool");

    [Test]
    public void AnExecutablePathWithAnotherTargetIsAnError() =>
        ErrorOf(("MORPHIR_TEST_TARGET", "trimmed"), ("MORPHIR_TEST_EXECUTABLE", ExistingFile))
            .Should().Contain("MORPHIR_TEST_EXECUTABLE").And.Contain("executable");

    [Test]
    public void AHostThatATargetDoesNotHaveGivesTheHostAndTheTargetInItsReason()
    {
        var target = new TestTarget.NativeAot();

        target.Has(MorphirHost.Native).Should().BeTrue();
        target.Has(MorphirHost.Managed).Should().BeFalse();
        target.MissingHostReason(MorphirHost.Managed).Should().Be("host: managed is not in target aot");
    }

    [Test]
    [Arguments("", TestLane.None)]
    [Arguments("none", TestLane.None)]
    [Arguments("packages", TestLane.Packages)]
    [Arguments("native-aot", TestLane.NativeAot)]
    [Arguments("published", TestLane.Published)]
    public void TheLaneOfTheRunParses(string value, TestLane lane) =>
        Parse(("MORPHIR_TEST_LANE", value)).Lane.Should().Be(lane);

    [Test]
    public void AnUnknownLaneGivesAMessageThatListsTheValidValues() =>
        ErrorOf(("MORPHIR_TEST_LANE", "nightly"))
            .Should().Contain("MORPHIR_TEST_LANE").And.Contain("'nightly'").And.Contain("none, packages, native-aot, published");

    [Test]
    public void TheKeepSettingKeepsTheWorkspaceOfAFailedTest()
    {
        Parse(("MORPHIR_TEST_KEEP", "failed")).KeepFailed.Should().BeTrue();
        ErrorOf(("MORPHIR_TEST_KEEP", "always")).Should().Contain("MORPHIR_TEST_KEEP").And.Contain("failed");
    }

    [Test]
    public void ParseStopsTheRunWithTheMessage()
    {
        var parse = () => Parse(("MORPHIR_TEST_TARGET", "fast"));

        parse.Should().Throw<TestSessionException>().WithMessage("*MORPHIR_TEST_TARGET*");
    }
}
