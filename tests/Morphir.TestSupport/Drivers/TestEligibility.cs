// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// Something that a test needs from the drivers of a target and a host. The set is closed. A test states a need
/// with the tag of <see cref="DriverCapabilities.TagOf"/>.
/// </summary>
public enum DriverCapability
{
    /// <summary>The <c>ir verify</c> command.</summary>
    ModelVerification,

    /// <summary>The <c>generate</c> command.</summary>
    Generation,

    /// <summary>The F# frontend in the host process: the managed host and the tool host.</summary>
    InProcessFSharpFrontend,

    /// <summary>The self-contained F# frontend process, which the native host starts as an extension.</summary>
    SelfContainedFrontend,

    /// <summary>The <c>compile</c> command.</summary>
    Compilation,

    /// <summary>The <c>config</c> command.</summary>
    Configuration,

    /// <summary>The <c>project migrate</c> and <c>workspace migrate</c> commands.</summary>
    Migration,

    /// <summary>The <c>extension list</c> and <c>extension runtimes</c> commands.</summary>
    Extensions,

    /// <summary>The <c>validate</c> command.</summary>
    ModelValidation,

    /// <summary>The extension guests of the development build (<see cref="Drivers.BuiltExtension"/>).</summary>
    BuiltExtensions,

    /// <summary>A host with the dynamic-code switch of the runtime off, as in Native AOT (<see cref="CliRunOptions.WithoutDynamicCode"/>).</summary>
    HostWithoutDynamicCode,

    /// <summary>Each host of the target in one test (<see cref="DriverSet.CliOf"/>): the development build has three.</summary>
    AllHosts,

    /// <summary>A host that no release profile built, so it states no deployment: the development build.</summary>
    UnprofiledBuild,

    /// <summary>The routing of a command to an external tool, as <c>morphir mck</c> runs <c>morphir-mck</c> (<see cref="IToolRoutingDriver"/>).</summary>
    ToolRouting,
    /// <summary>
    /// The build output and the project files of the programs of the repository that are not the CLI: the library
    /// hosts, the probes and <c>morphirup</c> (<see cref="ILibraryHostDriver"/>, <see cref="IArtifactInspectionDriver"/>).
    /// Only the development build has them.
    /// </summary>
    BuiltPrograms,
}

/// <summary>The tags of the capabilities, and the capabilities that the drivers of a target and a host support.</summary>
public static class DriverCapabilities
{
    /// <summary>The start of a capability tag.</summary>
    public const string TagPrefix = "needs-";

    public static ImmutableArray<DriverCapability> All { get; } = [.. Enum.GetValues<DriverCapability>()];

    /// <summary>The tag of a capability, for example <c>needs-self-contained-frontend</c>.</summary>
    public static string TagOf(DriverCapability capability) =>
        TagPrefix + Regex.Replace(capability.ToString(), "(?<=[a-z])(?=[A-Z])", "-").ToLowerInvariant();

    public static bool TryFromTag(string tag, out DriverCapability capability)
    {
        foreach (var candidate in All)
        {
            if (!TagOf(candidate).Equals(tag, StringComparison.OrdinalIgnoreCase)) continue;
            capability = candidate;
            return true;
        }

        capability = default;
        return false;
    }

    /// <summary>
    /// The capabilities that the drivers support for <paramref name="host"/> of <paramref name="target"/>. A host
    /// that the target does not have supports none. Add a capability here in the change that adds its driver.
    /// </summary>
    public static ImmutableHashSet<DriverCapability> Of(TestTarget target, MorphirHost host)
    {
        if (!target.Has(host)) return [];

        // Each host has the commands. The F# frontend is in the managed host and in the tool host. No target gives
        // the self-contained frontend process yet.
        ImmutableHashSet<DriverCapability> commands =
        [
            DriverCapability.ModelVerification, DriverCapability.Generation, DriverCapability.Compilation, DriverCapability.Configuration,
            DriverCapability.Migration, DriverCapability.Extensions, DriverCapability.ModelValidation, DriverCapability.ToolRouting,
        ];
        if (host is MorphirHost.Managed or MorphirHost.DotnetTool) commands = commands.Add(DriverCapability.InProcessFSharpFrontend);
        // The programs and the extension guests are build output of the development build: a published target has none.
        // The development build runs each host as an assembly, which can also run with the dynamic-code switch off.
        if (target is TestTarget.DevelopmentBuild)
        {
            commands = commands.Union(
            [
                DriverCapability.BuiltPrograms, DriverCapability.BuiltExtensions, DriverCapability.HostWithoutDynamicCode, DriverCapability.AllHosts,
                DriverCapability.UnprofiledBuild,
            ]);
        }

        return commands;
    }
}

/// <summary>What one test needs, from its tags or categories: a lane, a host and capabilities.</summary>
/// <param name="Lane"><see cref="TestLane.None"/> for a test that needs only the development build.</param>
/// <param name="Host">Null for a test that runs on the default host of the run.</param>
public sealed record TestNeeds(TestLane Lane, MorphirHost? Host, ImmutableArray<DriverCapability> Capabilities)
{
    /// <summary>The tag that a test with a lane also has, so that one filter excludes each lane.</summary>
    public const string LaneTag = "lane";

    /// <summary>The start of a host tag: <c>host-native</c>, <c>host-managed</c>, <c>host-tool</c>.</summary>
    public const string HostTagPrefix = "host-";

    /// <summary>A test with no need: no lane, the default host, no capability.</summary>
    public static TestNeeds None { get; } = new(TestLane.None, Host: null, Capabilities: []);

    /// <summary>
    /// Reads the needs from the tags of a scenario or the categories of a framework test. Other tags are ignored.
    /// Two lanes, two hosts, or an unknown host or capability tag are an error of the test.
    /// </summary>
    public static TestNeeds FromTags(IEnumerable<string> tags)
    {
        var lanes = new List<TestLane>();
        var hosts = new List<MorphirHost>();
        var capabilities = new List<DriverCapability>();
        foreach (var tag in tags.Select(tag => tag.Trim().TrimStart('@')))
        {
            if (TestLanes.TryParse(tag, out var lane) && lane != TestLane.None)
            {
                if (!lanes.Contains(lane)) lanes.Add(lane);
            }
            else if (tag.StartsWith(HostTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!MorphirHosts.TryParse(tag[HostTagPrefix.Length..], out var host))
                    throw new TestSessionException($"The tag '{tag}' names no host. The host tags are: {string.Join(", ", MorphirHosts.All.Select(known => HostTagPrefix + known.Name()))}.");
                if (!hosts.Contains(host)) hosts.Add(host);
            }
            else if (tag.StartsWith(DriverCapabilities.TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!DriverCapabilities.TryFromTag(tag, out var capability))
                    throw new TestSessionException($"The tag '{tag}' names no capability. The capability tags are: {string.Join(", ", DriverCapabilities.All.Select(DriverCapabilities.TagOf))}.");
                if (!capabilities.Contains(capability)) capabilities.Add(capability);
            }
        }

        if (lanes.Count > 1)
            throw new TestSessionException($"The test has two lanes: {string.Join(", ", lanes.Select(TestLanes.Name))}. A test has at most one lane.");
        if (hosts.Count > 1)
            throw new TestSessionException($"The test has two hosts: {string.Join(", ", hosts.Select(MorphirHosts.Name))}. A test names at most one host.");

        return new TestNeeds(lanes.Count == 1 ? lanes[0] : TestLane.None, hosts.Count == 1 ? hosts[0] : null, [.. capabilities]);
    }
}

/// <summary>What a run gives: its lane, its target, its default host, and the capabilities of the drivers for a host.</summary>
public sealed record RunProfile(
    TestLane Lane, TestTarget Target, MorphirHost DefaultHost, Func<MorphirHost, IReadOnlySet<DriverCapability>> Supports)
{
    /// <summary>The profile of <paramref name="settings"/>, with the capabilities of <see cref="DriverCapabilities.Of"/>.</summary>
    public static RunProfile Of(TestRunSettings settings) =>
        new(settings.Lane, settings.Target, settings.DefaultHost, host => DriverCapabilities.Of(settings.Target, host));
}

/// <summary>The check that a skipped test failed.</summary>
public enum SkipKind
{
    Lane,
    Host,
    Capability,
}

/// <summary>The result of <see cref="TestEligibility.Decide"/>.</summary>
public abstract record Eligibility
{
    private Eligibility() { }

    /// <summary>The test runs, on <paramref name="Host"/>.</summary>
    public sealed record Eligible(MorphirHost Host) : Eligibility;

    /// <summary>The test does not run. <paramref name="Reason"/> is its one reason: the first check that failed.</summary>
    public sealed record Skipped(SkipKind Kind, string Reason) : Eligibility;
}

/// <summary>
/// The one decision that says if a test runs. A Gherkin scenario and a framework test both get it, before the
/// first step.
/// </summary>
public static class TestEligibility
{
    /// <summary>
    /// Checks the lane, then the host, then the capabilities, and stops at the first check that fails. So a test is
    /// eligible, or it is skipped with exactly one reason.
    /// </summary>
    public static Eligibility Decide(TestNeeds needs, RunProfile run)
    {
        if (!Executes(run.Lane, needs.Lane))
            return new Eligibility.Skipped(SkipKind.Lane, $"lane: test {needs.Lane.Name()}, run {run.Lane.Name()}");

        var host = needs.Host ?? run.DefaultHost;
        if (!run.Target.Has(host))
            return new Eligibility.Skipped(SkipKind.Host, run.Target.MissingHostReason(host));

        var supported = run.Supports(host);
        foreach (var capability in needs.Capabilities)
        {
            if (!supported.Contains(capability))
                return new Eligibility.Skipped(SkipKind.Capability, $"capability: {capability} on target {run.Target.Name}, host {host.Name()}");
        }

        return new Eligibility.Eligible(host);
    }

    /// <summary>
    /// True when a run of <paramref name="run"/> executes a test of <paramref name="test"/>. A test with no lane runs
    /// on the development build and on a published target; a test with a lane runs only in the run of that lane.
    /// </summary>
    public static bool Executes(TestLane run, TestLane test) =>
        run == test || (run == TestLane.Published && test == TestLane.None);
}

/// <summary>The count of the skipped tests of a run, for each kind of reason. Safe for tests that run in parallel.</summary>
public sealed class SkipCounts
{
    private const string ReportStart = "Skipped tests by reason: ";

    private readonly int[] _counts = new int[Enum.GetValues<SkipKind>().Length];

    public void Add(Eligibility.Skipped skipped) => Interlocked.Increment(ref _counts[(int)skipped.Kind]);

    public int Of(SkipKind kind) => Volatile.Read(ref _counts[(int)kind]);

    /// <summary>The line that a test process writes when it ends, so that the build can show the counts in its report.</summary>
    public string ReportLine() =>
        ReportStart + string.Join(", ", Enum.GetValues<SkipKind>().Select(kind => $"{kind.ToString().ToLowerInvariant()} {Of(kind)}"));

    /// <summary>Reads the counts from a line of <see cref="ReportLine"/>.</summary>
    public static bool TryParseReportLine(string line, out IReadOnlyDictionary<SkipKind, int> counts)
    {
        var parsed = new Dictionary<SkipKind, int>();
        counts = parsed;
        var start = line.IndexOf(ReportStart, StringComparison.Ordinal);
        if (start < 0) return false;

        foreach (var part in line[(start + ReportStart.Length)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2 || !Enum.TryParse<SkipKind>(words[0], ignoreCase: true, out var kind) || !int.TryParse(words[1], out var count))
                return false;
            parsed[kind] = count;
        }

        return parsed.Count == Enum.GetValues<SkipKind>().Length;
    }
}
