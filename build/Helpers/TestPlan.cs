// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

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
    private TestSuite(string project, TestFramework framework, ImmutableArray<string> arguments, bool usesSourceProjects = false, bool runsAlone = false) =>
        (Project, Framework, Arguments, UsesSourceProjects, RunsAlone) = (project, framework, arguments, usesSourceProjects, runsAlone);

    /// <summary>The project name, which is also the folder below tests/ and the assembly name.</summary>
    public string Project { get; }

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
    public TestSuite WithSourceProjects() => new(Project, Framework, Arguments, usesSourceProjects: true, RunsAlone);

    /// <summary>The same suite, with <see cref="RunsAlone"/> set.</summary>
    public TestSuite Alone() => new(Project, Framework, Arguments, UsesSourceProjects, runsAlone: true);

    public bool Equals(TestSuite? other) =>
        other is not null && Project == other.Project && Framework == other.Framework
        && UsesSourceProjects == other.UsesSourceProjects && RunsAlone == other.RunsAlone
        && Arguments.SequenceEqual(other.Arguments);

    public override int GetHashCode() => HashCode.Combine(Project, Framework);
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

public static class TestPlan
{
    /// <summary>The word that marks a test whose result can depend on the operating system.</summary>
    public const string PlatformMarker = "PlatformSpecific";

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
        TestSuite.TUnit("Morphir.SDK.Conformance.Tests"),
        TestSuite.TUnit("Morphir.SDK.CSharp.Tests"),
        TestSuite.Expecto("Morphir.IR.Pipeline.Plugins.Tests"),
        TestSuite.Expecto("Morphir.Extensions.Tests"),
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
    ];

    /// <summary>
    /// The number of test assemblies that run at the same time when the caller gives no number: half the processors,
    /// from 1 to 4. Each assembly also runs its own tests in parallel, so more assemblies only compete for the processors.
    /// </summary>
    public static int DefaultParallelism(int processors) => Math.Clamp(processors / 2, 1, 4);

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

    /// <summary>True when a test source file or feature file has the platform marker.</summary>
    public static bool MentionsPlatformMarker(string sourceText) =>
        sourceText.Contains(PlatformMarker, StringComparison.Ordinal);

    /// <summary>
    /// The suites that <paramref name="scope"/> runs, in the order of <paramref name="suites"/>. The scope
    /// <see cref="TestScope.Platform"/> runs only the suites for which <paramref name="hasPlatformTests"/> is true,
    /// with the filter of their framework. A <see cref="TestFramework.Program"/> has no filter, so it does not run.
    /// </summary>
    public static ImmutableArray<TestRun> Select(
        TestScope scope, IEnumerable<TestSuite> suites, Func<TestSuite, bool> hasPlatformTests)
    {
        if (scope == TestScope.All)
            return [.. suites.Select(suite => new TestRun(suite, suite.Arguments))];

        return
        [
            .. suites
                .Where(suite => suite.Framework != TestFramework.Program && hasPlatformTests(suite))
                .Select(suite => new TestRun(suite, [.. suite.Arguments, .. PlatformFilter(suite.Framework)])),
        ];
    }

    static ImmutableArray<string> PlatformFilter(TestFramework framework) => framework switch
    {
        // A TUnit run that selects no test exits with code 8, so a marker that selects nothing fails the run.
        TestFramework.TUnit => ["--treenode-filter", $"/*/*/*/*[Category={PlatformMarker}]"],
        TestFramework.Expecto => ["--filter-test-list", PlatformMarker],
        _ => [],
    };
}
