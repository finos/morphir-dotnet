// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>Keeps the names in tests/Morphir.TestSupport/README.md equal to the code.</summary>
public class DriverDocumentationTests
{
    private static readonly string Readme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSupport.README.md"));

    private static IEnumerable<string> CodeSpans() =>
        Regex.Matches(Readme, "`([^`\n]+)`").Select(match => match.Groups[1].Value).Distinct();

    [Test]
    public void EachTypeInTheReadmeExistsInTheLibrary()
    {
        var types = typeof(TestSandbox).Assembly.GetTypes().Select(type => type.Name).ToHashSet();
        var named = CodeSpans()
            .Where(span => Regex.IsMatch(span, "^I?[A-Z][a-z]+([A-Z][a-z]+)+$"))
            // IsValid is a field of a report. CliTestHelper is an old name, in the table of old names.
            .Where(span => span is not ("IsValid" or "CliTestHelper"))
            .ToArray();

        named.Should().Contain(["IScenarioWorkspace", "ICliDriver", "IModelVerificationDriver", "IGenerationDriver", "BlackBoxSession", "TestEligibility"]);
        named.Where(name => !types.Contains(name)).Should().BeEmpty("each type that the README names is a type of Morphir.TestSupport");
    }

    [Test]
    public void EachEnvironmentVariableInTheReadmeIsOneThatTheCodeReads()
    {
        string[] current =
        [
            TestRunSettings.TargetVariable, TestRunSettings.HostVariable, TestRunSettings.ExecutableVariable,
            TestRunSettings.LaneVariable, TestRunSettings.KeepVariable, ProcessGate.LimitVariable,
        ];
        // The names that the new settings replace. The table "Old names" of the README is the only place for them.
        string[] old = ["MORPHIR_EXECUTABLE_TYPE", "MORPHIR_EXECUTABLE_PATH", "MORPHIR_GENERATION_CLI", "MORPHIR_DOTNET_TOOL_CLI", "MORPHIR_AOT_CLI"];

        var named = Regex.Matches(Readme, "MORPHIR_[A-Z_]+").Select(match => match.Value).Distinct().ToArray();

        named.Should().BeEquivalentTo(current.Concat(old));
    }

    [Test]
    public void EachTargetHostLaneAndCapabilityTagInTheReadmeIsAValueOfTheCode()
    {
        var spans = CodeSpans().ToHashSet();

        foreach (var target in TestTarget.Names) spans.Should().Contain(target);
        foreach (var host in MorphirHosts.All) spans.Should().Contain(MorphirHosts.Name(host)).And.Contain("@host-" + MorphirHosts.Name(host));
        foreach (var lane in TestLanes.All) spans.Should().Contain(lane == TestLane.None ? "none" : "@" + TestLanes.Name(lane));
        foreach (var capability in DriverCapabilities.All) spans.Should().Contain("@" + DriverCapabilities.TagOf(capability));
        spans.Where(span => span.StartsWith("@needs-", StringComparison.Ordinal))
            .Should().BeSubsetOf(DriverCapabilities.All.Select(capability => "@" + DriverCapabilities.TagOf(capability)));
    }
}
