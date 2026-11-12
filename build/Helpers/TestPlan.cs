// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Morphir.Build.Helpers;

// The plan of the Test target. This file is pure: it holds the test assemblies in run order and decides which of them
// a scope runs, with which arguments. Build.Testing.cs reads the test sources, starts the processes and reports.
//
// Most tests are managed logic that gives the same result on each operating system, so CI runs them once, on Linux.
// A test whose result can depend on the operating system has the marker PlatformSpecific, and the other operating
// systems run only those tests (scope "platform").

/// <summary>How a test assembly selects tests, and so how the platform filter reaches it.</summary>
public enum TestFramework
{
    /// <summary>TUnit on Microsoft.Testing.Platform. The marker is <c>[Category("PlatformSpecific")]</c>, or the
    /// Reqnroll tag <c>@PlatformSpecific</c>, which Reqnroll.TUnit turns into that category.</summary>
    TUnit,

    /// <summary>Expecto with its own command line. The marker is <c>testLabel "PlatformSpecific"</c>.</summary>
    Expecto,

    /// <summary>An executable that is one check and takes no test filter.</summary>
    Program,
}

/// <summary>One test assembly of the Test target, with the arguments that its tests need in every run.</summary>
public sealed record TestSuite
{
    private TestSuite(
        string project, TestFramework framework, ImmutableArray<string> arguments,
        bool usesSourceProjects = false, bool runsAlone = false, string? group = null) =>
        (Project, Framework, Arguments, UsesSourceProjects, RunsAlone, Group) = (project, framework, arguments, usesSourceProjects, runsAlone, group);

    /// <summary>The project name, which is also the folder below tests/ and the assembly name.</summary>
    public string Project { get; }

    /// <summary>
    /// The purpose group of the black-box project that this suite runs (<see cref="TestPlan.Groups"/>), or null for
    /// a suite that is a complete assembly. A group is a category of its tests, and the suite runs as its own
    /// process with one filter.
    /// </summary>
    public string? Group { get; }

    /// <summary>The name of the suite in the log and in the report.</summary>
    public string Name => Group is null ? Project : $"{Project} ({Group})";

    /// <summary>The name of the directory of the suite below the test results directory.</summary>
    public string ResultsDirectoryName => Group is null ? Project : $"{Project}.{Group}";

    public TestFramework Framework { get; }

    public ImmutableArray<string> Arguments { get; }

    /// <summary>
    /// True when tests of the suite run MSBuild (<c>dotnet build</c>, <c>pack</c> or <c>publish</c>) on a project of
    /// this repository. Those runs read and write the obj and bin folders that all suites share, so only one such
    /// suite runs at a time. A test must not replace the assemblies of the solution build there: pack with
    /// <c>--no-build</c>, build a copy with <c>-p:BuildProjectReferences=false</c>, or use <c>--artifacts-path</c>.
    /// </summary>
    public bool UsesSourceProjects { get; }

    /// <summary>
    /// True when no other suite can run at the same time. Use it for a suite that has tests with a time budget:
    /// other suites take processor time, and then a test that is correct exceeds its budget.
    /// </summary>
    public bool RunsAlone { get; }

    /// <summary>Only Microsoft.Testing.Platform has the HangDump extension, and only the TUnit suites run on it.</summary>
    public bool TakesHangDump => Framework == TestFramework.TUnit;

    public static TestSuite TUnit(string project, params string[] arguments) =>
        new(project, TestFramework.TUnit, [.. arguments]);

    public static TestSuite Expecto(string project, params string[] arguments) =>
        new(project, TestFramework.Expecto, [.. arguments]);

    public static TestSuite Program(string project) => new(project, TestFramework.Program, []);

    /// <summary>The same suite, with <see cref="UsesSourceProjects"/> set.</summary>
    public TestSuite WithSourceProjects() => new(Project, Framework, Arguments, usesSourceProjects: true, RunsAlone, Group);

    /// <summary>The same suite, with <see cref="RunsAlone"/> set.</summary>
    public TestSuite Alone() => new(Project, Framework, Arguments, UsesSourceProjects, runsAlone: true, Group);

    /// <summary>The suite that runs only the tests of <paramref name="group"/> of this TUnit assembly.</summary>
    public TestSuite InGroup(string group)
    {
        if (Framework != TestFramework.TUnit)
            throw new InvalidOperationException($"Only a TUnit suite has a group: the group is a category. {Project} is {Framework}.");
        if (!TestPlan.Groups.Contains(group))
            throw new ArgumentException($"'{group}' is not a group. The groups are: {string.Join(", ", TestPlan.Groups)}.", nameof(group));
        return new(Project, Framework, Arguments, UsesSourceProjects, RunsAlone, group);
    }

    public bool Equals(TestSuite? other) =>
        other is not null && Project == other.Project && Framework == other.Framework && Group == other.Group
        && UsesSourceProjects == other.UsesSourceProjects && RunsAlone == other.RunsAlone
        && Arguments.SequenceEqual(other.Arguments);

    public override int GetHashCode() => HashCode.Combine(Project, Framework, Group);
}

/// <summary>Which tests a Test run selects. The set is closed.</summary>
public sealed class TestScope
{
    /// <summary>Every test of every suite.</summary>
    public static readonly TestScope All = new("all");

    /// <summary>Only the tests with the marker <see cref="TestPlan.PlatformMarker"/>.</summary>
    public static readonly TestScope Platform = new("platform");

    private TestScope(string name) => Name = name;

    public string Name { get; }

    public static bool TryParse(string input, [NotNullWhen(true)] out TestScope? scope)
    {
        scope = input.Trim().ToLowerInvariant() switch
        {
            "all" => All,
            "platform" => Platform,
            _ => null,
        };
        return scope is not null;
    }

    public override string ToString() => Name;
}

/// <summary>One suite that a Test run starts, with its complete arguments.</summary>
public sealed record TestRun(TestSuite Suite, ImmutableArray<string> Arguments);

/// <summary>
/// The share of the process limit of the machine that each test process gets (<see cref="TestPlan.ShareProcessLimit"/>).
/// <paramref name="Lanes"/> is the number of test processes that run at the same time.
/// </summary>
public readonly record struct ProcessLimit(int Machine, int Lanes, int Share)
{
    /// <summary>
    /// The limit of the test process of <paramref name="suite"/>. A suite that runs alone gets the limit of the
    /// machine, because no other test process runs then. Each other suite gets the same share, at each moment.
    /// </summary>
    public int Of(TestSuite suite) => suite.RunsAlone ? Machine : Share;
}

public static class TestPlan
{
    /// <summary>The word that marks a test whose result can depend on the operating system.</summary>
    public const string PlatformMarker = "PlatformSpecific";

    /// <summary>The marker of a black-box test that needs more than the development build. A group suite does not select it.</summary>
    public const string LaneMarker = "lane";

    /// <summary>The purpose groups of the black-box project. Each is a tag of its scenarios and a category of its tests.</summary>
    public static readonly ImmutableArray<string> Groups = ["integration", "e2e", "smoke", "journey"];

    /// <summary>The project of the black-box tests. A group of it is a suite.</summary>
    public const string BlackBoxProject = "Morphir.BlackBox.Tests";

    /// <summary>The suites of the Test target, in run order.</summary>
    public static readonly ImmutableArray<TestSuite> Suites =
    [
        TestSuite.TUnit("Morphir.Core.Tests"),
        TestSuite.TUnit("Morphir.Tooling.Tests"),
        TestSuite.Expecto("Morphir.Configuration.Tests", "--sequenced"),
        TestSuite.TUnit("Morphir.Configuration.IO.Tests", "--maximum-parallel-tests", "1"),
        TestSuite.TUnit("Morphir.Configuration.Microsoft.Tests", "--maximum-parallel-tests", "1"),
        // Exercises the generated CLI DTO source without loading the CLI host.
        TestSuite.Program("Morphir.Configuration.Inspection.Smoke"),
        TestSuite.Expecto("Morphir.Internal.CodeGeneration.Tests"),
        TestSuite.Expecto("Morphir.Models.Tests"),
        TestSuite.Expecto("Morphir.IR.Pipeline.Tests"),
        TestSuite.Expecto("Morphir.SDK.Tests").WithSourceProjects(),
        TestSuite.TUnit("Morphir.TestSupport.Tests"),
        TestSuite.Expecto("Morphir.Frontend.FSharp.Tests"),
        TestSuite.Expecto("Morphir.Conformance.Tests"),
        TestSuite.TUnit("Morphir.SDK.Conformance.Tests"),
        TestSuite.TUnit("Morphir.SDK.CSharp.Tests"),
        TestSuite.Expecto("Morphir.IR.Pipeline.Plugins.Tests"),
        TestSuite.Expecto("Morphir.Extensions.Tests"),
        // BoundsTests, ChannelTests and EngineTests assert deadlines with a margin of 1 or 2 seconds.
        TestSuite.TUnit("Morphir.Extensions.Wasm.Tests").Alone(),
        TestSuite.Expecto("Morphir.Extensions.Installation.Tests"),
        TestSuite.Expecto("Morphir.Analyzers.Tests"),
        TestSuite.Expecto("Morphir.Mep.Tests").WithSourceProjects(),
        // HardeningTests, NormalizationTests and PrimitiveTests have tests with a time budget of 5 or 10 seconds.
        TestSuite.Expecto("Morphir.Toolchain.Tests").Alone(),
        TestSuite.Expecto("Morphir.Toolchain.IO.Tests"),
        TestSuite.Expecto("Morphir.Compilation.Tests"),
        TestSuite.Expecto("Morphir.Compilation.Mep.Tests"),
        TestSuite.Expecto("Morphir.Frontend.FSharp.Extension.Tests").WithSourceProjects(),
        TestSuite.TUnit("Morphir.Tests"),
        TestSuite.TUnit("Morphir.Managed.Tests"),
        TestSuite.Expecto("Morphir.CodeGeneration.FSharp.Tests"),
        TestSuite.Expecto("Morphir.CodeGeneration.FSharp.Extension.Tests").WithSourceProjects(),
        TestSuite.Expecto("Morphir.CodeGeneration.CSharp.Tests", "--sequenced"),
        TestSuite.Expecto("Morphir.Ion.Tests"),
        TestSuite.Expecto("Morphir.Myriad.Tests").WithSourceProjects(),
        TestSuite.TUnit("Morphir.Myriad.Integration.Tests").WithSourceProjects(),
        TestSuite.Expecto("Morphir.Myriad.SystemTextJson.Tests").WithSourceProjects(),
        // The groups of the black-box project. A group is registered here in the change that adds its first test,
        // because a group that selects no test fails its run.
        TestSuite.TUnit(BlackBoxProject).InGroup("integration"),
        TestSuite.TUnit(BlackBoxProject).InGroup("smoke"),
        TestSuite.TUnit(BlackBoxProject).InGroup("journey"),
    ];

    /// <summary>
    /// The number of test assemblies that run at the same time when the caller gives no number: half the processors,
    /// rounded up, from 1 to 4. Each assembly also runs its own tests in parallel, so more assemblies only compete for
    /// the processors.
    /// </summary>
    public static int DefaultParallelism(int processors) => Math.Clamp((processors + 1) / 2, 1, 4);

    /// <summary>
    /// The run that a free worker starts: the first of <paramref name="pending"/> in order that can run with the
    /// suites in <paramref name="running"/>. A suite that <see cref="TestSuite.UsesSourceProjects"/> waits while
    /// another such suite runs. A suite that <see cref="TestSuite.RunsAlone"/> waits until no suite runs, and no
    /// suite starts while it runs. Null when no run can start now.
    /// </summary>
    public static TestRun? NextRunnable(IEnumerable<TestRun> pending, IReadOnlyCollection<TestSuite> running)
    {
        if (running.Any(suite => suite.RunsAlone)) return null;

        var sourceProjectsInUse = running.Any(suite => suite.UsesSourceProjects);
        return pending.FirstOrDefault(run =>
            !(run.Suite.RunsAlone && running.Count > 0) && !(run.Suite.UsesSourceProjects && sourceProjectsInUse));
    }

    // PlatformSpecific is also a case of a Morphir IR type, so the word alone is not the marker.
    static readonly Regex PlatformMarkerUse = new(
        $@"Category\(\s*""{PlatformMarker}""\s*\)|@{PlatformMarker}\b|test(Label|List)\s+""{PlatformMarker}""",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// True when a test source file or feature file has the platform marker in one of its forms: a TUnit category,
    /// a Reqnroll tag, or an Expecto label or list name.
    /// </summary>
    public static bool MentionsPlatformMarker(string sourceText) => PlatformMarkerUse.IsMatch(sourceText);

    /// <summary>
    /// True when an output line of an Expecto run says that the run selected no test. Expecto exits with 0 then, so
    /// the scope <see cref="TestScope.Platform"/> uses this to fail a suite whose marker selects nothing.
    /// </summary>
    public static bool ReportsNoTests(string outputLine) =>
        outputLine.Contains("EXPECTO! 0 tests run", StringComparison.Ordinal);

    const string SkipCountsStart = "Skipped tests by reason: ";

    /// <summary>
    /// The count of skipped tests by reason (for example <c>lane 2, host 0, capability 1</c>) when an output line
    /// of a black-box test process reports it, or null. The test support library writes the line.
    /// </summary>
    public static string? SkipCountsOf(string outputLine)
    {
        var start = outputLine.IndexOf(SkipCountsStart, StringComparison.Ordinal);
        return start < 0 ? null : outputLine[(start + SkipCountsStart.Length)..].Trim();
    }

    /// <summary>
    /// The suites that <paramref name="scope"/> runs, in the order of <paramref name="suites"/>. The scope
    /// <see cref="TestScope.Platform"/> runs only the suites for which <paramref name="hasPlatformTests"/> is true,
    /// with the filter of their framework. A <see cref="TestFramework.Program"/> has no filter, so it does not run.
    /// </summary>
    /// <remarks>
    /// A suite of a group gets exactly one filter, its group filter, in both scopes. Each test of the black-box
    /// project is platform-specific (the assembly has the marker), so the platform filter would select the same
    /// tests, and a test process takes one filter.
    /// </remarks>
    public static ImmutableArray<TestRun> Select(
        TestScope scope, IEnumerable<TestSuite> suites, Func<TestSuite, bool> hasPlatformTests)
    {
        if (scope == TestScope.All)
            return [.. suites.Select(suite => new TestRun(suite, [.. suite.Arguments, .. GroupFilter(suite)]))];

        return
        [
            .. suites
                .Where(suite => suite.Framework != TestFramework.Program && hasPlatformTests(suite))
                .Select(suite => new TestRun(
                    suite, [.. suite.Arguments, .. suite.Group is null ? PlatformFilter(suite.Framework) : GroupFilter(suite)])),
        ];
    }

    /// <summary>
    /// The suites that the option <c>--test-group</c> selects: with no group, all of <paramref name="suites"/>; with
    /// a group, only the suites of that group. A value that is not a group, or a group with no suite, is an error.
    /// </summary>
    public static ImmutableArray<TestSuite> SuitesOf(string? group, IEnumerable<TestSuite> suites)
    {
        if (group is null) return [.. suites];

        var name = group.Trim().ToLowerInvariant();
        if (!Groups.Contains(name))
            throw new ArgumentException($"The test group '{group}' is not a group. The groups are: {string.Join(", ", Groups)}.", nameof(group));

        ImmutableArray<TestSuite> selected = [.. suites.Where(suite => suite.Group == name)];
        if (selected.IsEmpty)
            throw new ArgumentException($"The test group {name} has no suite in the plan: no test of the black-box project has it yet.", nameof(group));
        return selected;
    }

    /// <summary>
    /// True when a Test run also runs the Myriad package-consumer scripts after its suites. A run of one group runs
    /// only the suites of that group.
    /// </summary>
    public static bool RunsPackageConsumerScripts(string? group) => group is null;

    /// <summary>
    /// The limit of product processes for the complete Test run: the value of <c>MORPHIR_TEST_MAX_PROCESSES</c> of
    /// the caller when it is a positive number, or the default of the process gate of the test support library
    /// (half the processors, and not less than two).
    /// </summary>
    public static int MachineProcessLimit(string? variable, int processors) =>
        int.TryParse(variable, out var limit) && limit > 0 ? limit : Math.Max(2, processors / 2);

    /// <summary>
    /// The fixed share of <paramref name="machineLimit"/> for each test process. A test process has its own process
    /// gate, so with several test processes at the same time each one must get a part of one limit. There are never
    /// more test processes at the same time than the limit, and each gets the limit divided by their number,
    /// rounded down. The share does not depend on the moment at which a suite starts, and at each moment the sum of
    /// the shares is not more than the limit.
    /// </summary>
    public static ProcessLimit ShareProcessLimit(int machineLimit, int parallelism)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(machineLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);
        var lanes = Math.Min(parallelism, machineLimit);
        return new ProcessLimit(machineLimit, lanes, machineLimit / lanes);
    }

    // The tests of a lane need more than the development build, so the run of a group does not select them.
    static ImmutableArray<string> GroupFilter(TestSuite suite) => suite.Group is null
        ? []
        : ["--treenode-filter", $"/*/*/*/*[(Category={suite.Group})&(Category!={LaneMarker})]"];

    static ImmutableArray<string> PlatformFilter(TestFramework framework) => framework switch
    {
        // A TUnit run that selects no test exits with code 8, so a marker that selects nothing fails the run.
        TestFramework.TUnit => ["--treenode-filter", $"/*/*/*/*[Category={PlatformMarker}]"],
        // An Expecto run that selects no test exits with 0: see ReportsNoTests.
        TestFramework.Expecto => ["--filter-test-list", PlatformMarker],
        _ => [],
    };
}
