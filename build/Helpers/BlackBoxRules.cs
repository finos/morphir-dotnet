// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Morphir.Build.Helpers;

// The rules of the black-box test project (tests/Morphir.BlackBox.Tests) that a program can check. This file is
// pure: it takes the text of a file and gives the violations. tests/Morphir.Build.Tests/Helpers/BlackBoxRulesTests.cs
// reads the files of the repository and applies each rule.

/// <summary>One black-box test: a Gherkin scenario or a framework test, with its tags or categories.</summary>
/// <param name="File">The path of the file, relative to the black-box project, with <c>/</c>.</param>
/// <param name="Tags">The tags without <c>@</c>: those of the feature, the rule and the scenario, or the categories of the class and the method.</param>
public sealed record BlackBoxTest(string File, int Line, string Name, ImmutableArray<string> Tags);

/// <summary>One violation of a rule, with the place to correct.</summary>
public sealed record RuleViolation(string Rule, string File, int Line, string Message)
{
    public override string ToString() => $"{File}:{Line}: {Rule}: {Message}";
}

/// <summary>A project reference of an MSBuild file. <paramref name="Path"/> is relative to the repository root, with <c>/</c>.</summary>
public sealed record ProjectReferenceUse(string Path, int Line, bool ExposesAssembly);

/// <summary>
/// One run of a pull request that executes black-box tests: its lane, its default host, and for each host that it
/// has the capability tags that its drivers support.
/// </summary>
public sealed record PullRequestRun(
    string Lane, string Name, string DefaultHost, ImmutableDictionary<string, ImmutableHashSet<string>> CapabilitiesOfHost);

public static class BlackBoxRules
{
    /// <summary>The tag that a test with a lane also has.</summary>
    public const string LaneMarker = TestPlan.LaneMarker;

    /// <summary>The lanes: what a test needs beyond the development build.</summary>
    public static readonly ImmutableArray<string> LaneTags = ["packages", "native-aot", "published"];

    /// <summary>The lane whose tests the release and manual workflows run, not a pull request.</summary>
    public const string PublishedLane = "published";

    public const string HostTagPrefix = "host-";

    public const string CapabilityTagPrefix = "needs-";

    /// <summary>The folder of the black-box project for the tests of each group.</summary>
    public static readonly ImmutableDictionary<string, string> Folders = ImmutableDictionary.CreateRange(
    [
        KeyValuePair.Create("integration", "Integration"),
        KeyValuePair.Create("e2e", "EndToEnd"),
        KeyValuePair.Create("smoke", "Smoke"),
        KeyValuePair.Create("journey", "Journeys"),
    ]);

    static string At(string tag) => "@" + tag;

    static string AtList(IEnumerable<string> tags) => string.Join(", ", tags.Select(At));

    // ---- Tests of a feature file --------------------------------------------------------------------------------

    static readonly Regex ScenarioLine = new(@"^(Scenario Outline|Scenario Template|Scenario|Example)\s*:\s*(.*)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The scenarios of a feature file, each with the tags of its feature, of its rule, of the scenario and of its
    /// example tables.
    /// </summary>
    public static ImmutableArray<BlackBoxTest> ParseFeature(string file, string text)
    {
        var tests = ImmutableArray.CreateBuilder<BlackBoxTest>();
        ImmutableArray<string> feature = [], rule = [];
        var pending = new List<string>();
        var inDocString = false;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("\"\"\"", StringComparison.Ordinal) || line.StartsWith("```", StringComparison.Ordinal))
            {
                inDocString = !inDocString;
                continue;
            }

            if (inDocString || line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('@'))
            {
                // A comment can follow the tags of a line.
                var tags = line.Split(" #")[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                pending.AddRange(tags.Where(tag => tag.StartsWith('@')).Select(tag => tag[1..]));
            }
            else if (line.StartsWith("Feature:", StringComparison.Ordinal))
            {
                feature = [.. pending];
                pending.Clear();
            }
            else if (line.StartsWith("Rule:", StringComparison.Ordinal))
            {
                rule = [.. pending];
                pending.Clear();
            }
            else if (ScenarioLine.Match(line) is { Success: true } scenario)
            {
                tests.Add(new BlackBoxTest(file, index + 1, scenario.Groups[2].Value.Trim(), [.. feature.Concat(rule).Concat(pending).Distinct()]));
                pending.Clear();
            }
            else if (line.StartsWith("Examples:", StringComparison.Ordinal) || line.StartsWith("Scenarios:", StringComparison.Ordinal))
            {
                if (tests.Count > 0 && pending.Count > 0)
                    tests[^1] = tests[^1] with { Tags = [.. tests[^1].Tags.Concat(pending).Distinct()] };
                pending.Clear();
            }
            else
            {
                pending.Clear();
            }
        }

        return tests.ToImmutable();
    }

    // ---- Tests of a C# file -------------------------------------------------------------------------------------

    static readonly Regex ClassLine = new(@"\bclass\s+(\w+)", RegexOptions.CultureInvariant);
    static readonly Regex MethodName = new(@"(\w+)\s*(?:<[^>()]*>)?\s*\(", RegexOptions.CultureInvariant);
    static readonly Regex TestAttribute = new(@"(?<![\w.])(?:TUnit\.Core\.)?Test(?:Attribute)?(?![\w.])", RegexOptions.CultureInvariant);
    static readonly Regex CategoryAttribute = new(@"\bCategory(?:Attribute)?\(\s*""([^""]+)""\s*\)", RegexOptions.CultureInvariant);

    /// <summary>
    /// The framework tests of a C# file: each method with the <c>[Test]</c> attribute, with the categories of its
    /// class and its own categories. A category of a base class is not read.
    /// </summary>
    public static ImmutableArray<BlackBoxTest> ParseFrameworkTests(string file, string source)
    {
        // The structure comes from the text with no comment and no string. The names of the categories are strings,
        // so they come from the text with no comment.
        var structure = Strip(source, keepStrings: false).Split('\n');
        var withStrings = Strip(source, keepStrings: true).Split('\n');
        var tests = ImmutableArray.CreateBuilder<BlackBoxTest>();
        string? className = null;
        ImmutableArray<string> classCategories = [];
        var attributes = new StringBuilder();
        var hasTest = false;
        for (var index = 0; index < structure.Length; index++)
        {
            var line = structure[index].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('['))
            {
                attributes.Append(withStrings[index]).Append('\n');
                hasTest |= TestAttribute.IsMatch(line);
                // A declaration can follow its attributes on the same line. The tests of this project do not do that.
                continue;
            }

            if (ClassLine.Match(line) is { Success: true } type)
            {
                className = type.Groups[1].Value;
                classCategories = Categories(attributes.ToString());
            }
            else if (hasTest && className is not null && MethodName.Match(line) is { Success: true } method)
            {
                tests.Add(new BlackBoxTest(
                    file, index + 1, $"{className}.{method.Groups[1].Value}", [.. classCategories.Concat(Categories(attributes.ToString())).Distinct()]));
            }

            attributes.Clear();
            hasTest = false;
        }

        return tests.ToImmutable();
    }

    static ImmutableArray<string> Categories(string attributes) =>
        [.. CategoryAttribute.Matches(attributes).Select(match => match.Groups[1].Value)];

    // ---- One group, and the folder agrees -----------------------------------------------------------------------

    /// <summary>A test has exactly one group, and its file is in the folder of that group.</summary>
    public static ImmutableArray<RuleViolation> GroupViolations(IEnumerable<BlackBoxTest> tests)
    {
        const string rule = "one-group";
        var violations = ImmutableArray.CreateBuilder<RuleViolation>();
        foreach (var test in tests)
        {
            var groups = test.Tags.Where(TestPlan.Groups.Contains).ToArray();
            if (groups.Length == 0)
            {
                violations.Add(new(rule, test.File, test.Line, $"'{test.Name}' has no group. Give it one of: {AtList(TestPlan.Groups)}."));
            }
            else if (groups.Length > 1)
            {
                var count = groups.Length == 2 ? "two" : groups.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                violations.Add(new(rule, test.File, test.Line, $"'{test.Name}' has {count} groups: {AtList(groups)}. A test has one group."));
            }
            else if (test.File.Split('/')[0] != Folders[groups[0]] || !test.File.Contains('/'))
            {
                violations.Add(new(rule, test.File, test.Line, $"'{test.Name}' has the group {At(groups[0])}, so its file must be in the folder {Folders[groups[0]]}."));
            }
        }

        return violations.ToImmutable();
    }

    // ---- At most one lane ---------------------------------------------------------------------------------------

    /// <summary>A test with a lane has the lane marker and exactly one lane tag; a test with no lane has neither.</summary>
    public static ImmutableArray<RuleViolation> LaneViolations(IEnumerable<BlackBoxTest> tests)
    {
        const string rule = "one-lane";
        var violations = ImmutableArray.CreateBuilder<RuleViolation>();
        foreach (var test in tests)
        {
            var lanes = test.Tags.Where(LaneTags.Contains).ToArray();
            var marked = test.Tags.Contains(LaneMarker);
            if (lanes.Length > 1)
                violations.Add(new(rule, test.File, test.Line, $"'{test.Name}' has two lanes: {AtList(lanes)}. A test has at most one lane."));
            else if (lanes.Length == 1 && !marked)
                violations.Add(new(rule, test.File, test.Line, $"'{test.Name}' has the lane {At(lanes[0])} and not {At(LaneMarker)}. A test with a lane has both, so that a group run does not select it."));
            else if (lanes.Length == 0 && marked)
                violations.Add(new(rule, test.File, test.Line, $"'{test.Name}' has {At(LaneMarker)} and no lane. Give it one of: {AtList(LaneTags)}."));
        }

        return violations.ToImmutable();
    }

    // ---- No assembly from src/ is visible -----------------------------------------------------------------------

    /// <summary>The project references of an MSBuild file. <paramref name="file"/> is its path relative to the repository root.</summary>
    public static ImmutableArray<ProjectReferenceUse> ProjectReferences(string file, string xml)
    {
        var directory = file.Contains('/') ? file[..file.LastIndexOf('/')] : "";
        var document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        return
        [
            .. document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference" && element.Attribute("Include") is not null)
                .Select(element =>
                {
                    var include = element.Attribute("Include")!.Value.Replace("$(MSBuildThisFileDirectory)", "").Replace('\\', '/');
                    var setting = element.Attribute("ReferenceOutputAssembly")?.Value
                        ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "ReferenceOutputAssembly")?.Value;
                    return new ProjectReferenceUse(
                        Normalize(directory + "/" + include),
                        ((IXmlLineInfo)element).LineNumber,
                        ExposesAssembly: !string.Equals(setting?.Trim(), "false", StringComparison.OrdinalIgnoreCase));
                }),
        ];
    }

    /// <summary>A reference to a project below <c>src/</c> must only order the build: <c>ReferenceOutputAssembly="false"</c>.</summary>
    public static ImmutableArray<RuleViolation> ReferenceViolations(string file, string xml) =>
    [
        .. ProjectReferences(file, xml)
            .Where(reference => IsProduct(reference.Path) && reference.ExposesAssembly)
            .Select(reference => new RuleViolation(
                "no-product-assembly", file, reference.Line,
                $"the reference to {reference.Path} makes a product assembly visible. A reference below src/ has ReferenceOutputAssembly=\"false\".")),
    ];

    /// <summary>
    /// The compile dependencies of a restored project (<c>obj/project.assets.json</c>) have no project from
    /// <c>src/</c>, also not through another reference. <paramref name="projectDirectory"/> is the directory of the
    /// project, relative to the repository root.
    /// </summary>
    public static ImmutableArray<RuleViolation> AssetsViolations(string file, string assetsJson, string projectDirectory)
    {
        using var document = JsonDocument.Parse(assetsJson);
        var root = document.RootElement;
        var violations = ImmutableArray.CreateBuilder<RuleViolation>();
        if (!root.TryGetProperty("libraries", out var libraries) || !root.TryGetProperty("targets", out var targets)) return [];

        // The name of a library with no version, to its key.
        var keys = libraries.EnumerateObject().ToDictionary(library => library.Name.Split('/')[0], library => library.Name, StringComparer.OrdinalIgnoreCase);
        var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("project", out var project) && project.TryGetProperty("restore", out var restore)
            && restore.TryGetProperty("frameworks", out var frameworks))
        {
            foreach (var framework in frameworks.EnumerateObject())
            {
                if (!framework.Value.TryGetProperty("projectReferences", out var references)) continue;
                foreach (var reference in references.EnumerateObject())
                    direct.Add(Path.GetFileNameWithoutExtension(reference.Name.Replace('\\', '/')));
            }
        }

        foreach (var library in libraries.EnumerateObject())
        {
            if (!IsType(library.Value, "project") || !library.Value.TryGetProperty("path", out var path)) continue;
            var projectPath = Normalize(projectDirectory + "/" + path.GetString()!.Replace('\\', '/'));
            if (!IsProduct(projectPath)) continue;

            var visible = targets.EnumerateObject().Any(target =>
                target.Value.TryGetProperty(library.Name, out var entry)
                && entry.TryGetProperty("compile", out var compile)
                && compile.EnumerateObject().Any(asset => !asset.Name.EndsWith("_._", StringComparison.Ordinal)));
            if (!visible) continue;

            var name = library.Name.Split('/')[0];
            var origin = direct.Contains(name)
                ? "It is a direct reference."
                : $"It comes through {Bringer(name, direct, keys, targets) ?? "another reference"}.";
            violations.Add(new("no-product-assembly", file, 1, $"the assembly of {projectPath} is visible. {origin}"));
        }

        return violations.ToImmutable();
    }

    // The direct reference from which the dependencies lead to the library.
    static string? Bringer(string name, HashSet<string> direct, Dictionary<string, string> keys, JsonElement targets)
    {
        foreach (var start in direct.Order(StringComparer.OrdinalIgnoreCase))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
            var queue = new Queue<string>([start]);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!keys.TryGetValue(current, out var key)) continue;
                foreach (var target in targets.EnumerateObject())
                {
                    if (!target.Value.TryGetProperty(key, out var entry) || !entry.TryGetProperty("dependencies", out var dependencies)) continue;
                    foreach (var dependency in dependencies.EnumerateObject())
                    {
                        if (string.Equals(dependency.Name, name, StringComparison.OrdinalIgnoreCase)) return start;
                        if (seen.Add(dependency.Name)) queue.Enqueue(dependency.Name);
                    }
                }
            }
        }

        return null;
    }

    static bool IsType(JsonElement library, string type) =>
        library.TryGetProperty("type", out var value) && value.GetString() == type;

    static bool IsProduct(string path) => path.StartsWith("src/", StringComparison.Ordinal);

    // The path with each "." and ".." removed.
    static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == ".." && parts.Count > 0 && parts[^1] != "..") parts.RemoveAt(parts.Count - 1);
            else parts.Add(part);
        }

        return string.Join('/', parts);
    }

    // ---- Steps and tests use drivers (a partial check) ----------------------------------------------------------

    // The names that go past a driver. The check finds these names only: a call through another type can pass it.
    static readonly (string Name, Regex Use)[] PastDrivers =
    [
        ("System.Diagnostics", new(@"\bSystem\.Diagnostics\b", RegexOptions.CultureInvariant)),
        ("ChildProcess", new(@"\bChildProcess\b", RegexOptions.CultureInvariant)),
        ("Process", new(@"\bProcess\b", RegexOptions.CultureInvariant)),
        ("TestSandbox", new(@"\bTestSandbox\b", RegexOptions.CultureInvariant)),
        ("File.", new(@"\bFile\.", RegexOptions.CultureInvariant)),
        ("Directory.", new(@"\bDirectory\.", RegexOptions.CultureInvariant)),
        ("Path.GetTempPath", new(@"\bPath\.GetTempPath\b", RegexOptions.CultureInvariant)),
        ("Environment.", new(@"\bEnvironment\.", RegexOptions.CultureInvariant)),
    ];

    /// <summary>
    /// A source of the black-box project, with its comments and string literals removed, has no name that starts a
    /// process, makes a file or a directory, or reads the environment. The check is partial: it finds the listed
    /// names, and the review check list of the test-manager skill has the rule.
    /// </summary>
    public static ImmutableArray<RuleViolation> DriverUseViolations(string file, string source)
    {
        var violations = ImmutableArray.CreateBuilder<RuleViolation>();
        var lines = Strip(source, keepStrings: false).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            foreach (var (name, use) in PastDrivers)
            {
                if (!use.IsMatch(lines[index])) continue;
                violations.Add(new("use-drivers", file, index + 1,
                    $"'{name}' goes past the drivers. A step or a test reaches the product, the file system and other processes only through a driver interface."));
            }
        }

        return violations.ToImmutable();
    }

    /// <summary>
    /// The C# source with each comment replaced by spaces, and each string and character literal too when
    /// <paramref name="keepStrings"/> is false. The line breaks stay, so a line number is the same. An expression in
    /// an interpolated string is a part of the string.
    /// </summary>
    public static string Strip(string source, bool keepStrings)
    {
        var text = source.Replace("\r\n", "\n");
        var result = new StringBuilder(text.Length);
        var index = 0;

        void Blank(int end, bool keep)
        {
            for (; index < end; index++) result.Append(keep || text[index] == '\n' ? text[index] : ' ');
        }

        while (index < text.Length)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (current == '/' && next == '/')
            {
                var end = text.IndexOf('\n', index);
                Blank(end < 0 ? text.Length : end, keep: false);
            }
            else if (current == '/' && next == '*')
            {
                var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                Blank(end < 0 ? text.Length : end + 2, keep: false);
            }
            else if (current == '\'')
            {
                // A character literal: 'a', '\n', '"'.
                var end = index + 1;
                while (end < text.Length && text[end] != '\'' && text[end] != '\n') end += text[end] == '\\' ? 2 : 1;
                Blank(Math.Min(end + 1, text.Length), keepStrings);
            }
            else if (current == '"' || ((current is '@' or '$') && StringStart(text, index) >= 0))
            {
                var quote = current == '"' ? index : StringStart(text, index);
                var verbatim = text[index..quote].Contains('@');
                var quotes = 0;
                while (quote + quotes < text.Length && text[quote + quotes] == '"') quotes++;
                int end;
                if (quotes >= 3)
                {
                    // A raw string ends at the same number of quotes.
                    var close = text.IndexOf(new string('"', quotes), quote + quotes, StringComparison.Ordinal);
                    end = close < 0 ? text.Length : close + quotes;
                }
                else
                {
                    end = quote + 1;
                    while (end < text.Length)
                    {
                        if (verbatim && text[end] == '"' && end + 1 < text.Length && text[end + 1] == '"') end += 2;
                        else if (!verbatim && text[end] == '\\') end += 2;
                        else if (text[end] == '"' || (!verbatim && text[end] == '\n')) break;
                        else end++;
                    }

                    end = Math.Min(end + 1, text.Length);
                }

                Blank(end, keepStrings);
            }
            else
            {
                result.Append(current);
                index++;
            }
        }

        return result.ToString();
    }

    // The index of the first quote of a string that starts with @ or $ at the index, or -1.
    static int StringStart(string text, int index)
    {
        var end = index;
        while (end < text.Length && text[end] is '@' or '$') end++;
        return end < text.Length && text[end] == '"' ? end : -1;
    }

    // ---- The step table is current ------------------------------------------------------------------------------

    static readonly Regex StepAttribute = new(@"\[\s*(Given|When|Then)\(\s*@?""((?:[^""\\]|\\.)*)""\s*\)\s*\]", RegexOptions.CultureInvariant);
    static readonly Regex StepCell = new(@"`((?:Given|When|Then) [^`]+)`", RegexOptions.CultureInvariant);

    /// <summary>The steps of a step class, each as its keyword and its pattern, in the order of the source.</summary>
    public static ImmutableArray<string> StepPatterns(string source) =>
        [.. StepAttribute.Matches(Strip(source, keepStrings: true)).Select(match => $"{match.Groups[1].Value} {match.Groups[2].Value}")];

    /// <summary>
    /// The step table of the README has exactly the steps of the step classes, and no two steps have the same
    /// pattern. A step in the table is a code span that starts with its keyword, in a table row.
    /// </summary>
    public static ImmutableArray<RuleViolation> StepTableViolations(
        string readmeFile, string readme, IEnumerable<(string File, string Source)> stepSources)
    {
        const string rule = "step-table";
        var violations = ImmutableArray.CreateBuilder<RuleViolation>();
        var table = readme.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.TrimStart().StartsWith('|'))
            .SelectMany(line => StepCell.Matches(line).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, source) in stepSources)
        {
            foreach (var step in StepPatterns(source))
            {
                if (owners.TryGetValue(step, out var first))
                {
                    violations.Add(new(rule, file, 1, $"the step '{step}' is also in {first}. Reuse the step."));
                    continue;
                }

                owners[step] = file;
                if (!table.Contains(step))
                    violations.Add(new(rule, file, 1, $"the step '{step}' is not in the step table of {readmeFile}."));
            }
        }

        foreach (var step in table.Where(step => !owners.ContainsKey(step)).Order(StringComparer.Ordinal))
            violations.Add(new(rule, readmeFile, 1, $"the step table has '{step}', and no step class has that step."));

        return violations.ToImmutable();
    }

    // ---- Each pull request test has one run that supports it ----------------------------------------------------

    /// <summary>
    /// Each test that a pull request must run (a test with no lane, or with a lane other than the published lane)
    /// has one pull request run of its lane that has its host and supports all its capabilities together. So a skip
    /// cannot hide a test in pull request CI.
    /// </summary>
    public static ImmutableArray<RuleViolation> RunSupportViolations(IEnumerable<BlackBoxTest> tests, IEnumerable<PullRequestRun> runs)
    {
        const string rule = "pull-request-run";
        var all = runs.ToArray();
        var violations = ImmutableArray.CreateBuilder<RuleViolation>();
        foreach (var test in tests)
        {
            var lane = test.Tags.FirstOrDefault(LaneTags.Contains) ?? "none";
            if (lane == PublishedLane) continue;

            var laneRuns = all.Where(run => run.Lane == lane).ToArray();
            if (laneRuns.Length == 0)
            {
                violations.Add(new(rule, test.File, test.Line, $"no pull request run executes the lane {lane}, so no pull request runs '{test.Name}'."));
                continue;
            }

            var named = test.Tags.FirstOrDefault(tag => tag.StartsWith(HostTagPrefix, StringComparison.Ordinal))?[HostTagPrefix.Length..];
            var capabilities = test.Tags.Where(tag => tag.StartsWith(CapabilityTagPrefix, StringComparison.Ordinal)).ToArray();
            var reasons = new List<string>();
            foreach (var run in laneRuns)
            {
                var host = named ?? run.DefaultHost;
                if (!run.CapabilitiesOfHost.TryGetValue(host, out var supported))
                {
                    reasons.Add($"{run.Name} does not have the host {host}");
                    continue;
                }

                var missing = capabilities.Where(capability => !supported.Contains(capability)).ToArray();
                if (missing.Length == 0)
                {
                    reasons.Clear();
                    break;
                }

                reasons.Add($"{run.Name} does not support {AtList(missing)}");
            }

            if (reasons.Count == 0) continue;
            var needs = $"host {named ?? laneRuns[0].DefaultHost}" + (capabilities.Length == 0 ? "" : $"; {AtList(capabilities)}");
            violations.Add(new(rule, test.File, test.Line, $"no pull request run supports '{test.Name}' ({needs}). {string.Join("; ", reasons)}."));
        }

        return violations.ToImmutable();
    }

    // ---- Each group with a test is a suite of the plan ----------------------------------------------------------

    /// <summary>
    /// A group that has a test with no lane has a suite in the test plan, and a suite of a group selects a test.
    /// <paramref name="project"/> is the name of the black-box project.
    /// </summary>
    public static ImmutableArray<RuleViolation> SuiteViolations(IEnumerable<BlackBoxTest> tests, IEnumerable<TestSuite> suites, string project)
    {
        const string rule = "group-suite";
        var violations = ImmutableArray.CreateBuilder<RuleViolation>();
        var selected = tests.Where(test => !test.Tags.Any(LaneTags.Contains) && !test.Tags.Contains(LaneMarker)).ToArray();
        var registered = suites.Where(suite => suite.Project == project && suite.Group is not null).Select(suite => suite.Group!).ToHashSet();

        foreach (var group in TestPlan.Groups)
        {
            var first = selected.FirstOrDefault(test => test.Tags.Contains(group));
            if (first is not null && !registered.Contains(group))
            {
                violations.Add(new(rule, first.File, first.Line,
                    $"'{first.Name}' has the group {At(group)}, and the test plan has no suite for that group. Add TestSuite.TUnit(\"{project}\").InGroup(\"{group}\") to TestPlan.Suites."));
            }
            else if (first is null && registered.Contains(group))
            {
                violations.Add(new(rule, "build/Helpers/TestPlan.cs", 1,
                    $"the suite of the group {group} selects no test: no test with no lane has {At(group)}. A run of a group with no test fails."));
            }
        }

        return violations.ToImmutable();
    }
}
