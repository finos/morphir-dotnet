// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport.Drivers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// Keeps the test-manager skill equal to the repository: each path, tag, command, guard test and type that the
/// skill names is there. The skill is the guidance for a person or an agent that adds a test, so a name that is
/// not there sends the reader to the wrong place.
/// </summary>
public class TestManagerSkillTests
{
    private static readonly string Root = TestFixture.GetRepositoryRoot();

    private static readonly string[] SkillFiles =
    [
        ".agents/skills/test-manager/SKILL.md",
        ".agents/skills/test-manager/references/classification.md",
        ".agents/skills/test-manager/references/black-box-patterns.md",
    ];

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static readonly Lazy<string> Skill = new(() => string.Join("\n", SkillFiles.Select(Read)));

    // The text of a code span, with no code block: a block is an example.
    private static IEnumerable<string> CodeSpans() =>
        Regex.Matches(Regex.Replace(Skill.Value, "```.*?```", "", RegexOptions.Singleline), "`([^`\n]+)`")
            .Select(match => match.Groups[1].Value).Distinct();

    private static bool Exists(string relative)
    {
        var path = Path.Combine(Root, relative.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) || Directory.Exists(path);
    }

    [Test]
    public void EachPathOfTheSkillIsInTheRepository()
    {
        var paths = CodeSpans()
            .Where(span => Regex.IsMatch(span, @"^(tests|build|docs|src|conformance|scripts|examples|\.agents)/[\w./-]+$"))
            .ToArray();

        paths.Should().Contain(["tests/Morphir.BlackBox.Tests/README.md", "build/Helpers/TestPlan.cs", "docs/contributing/ci-os-matrix.md"]);
        paths.Where(path => !Exists(path)).Should().BeEmpty("each path that the skill names is a file or a directory of the repository");
    }

    [Test]
    public void EachFolderAndFileOfTheBlackBoxProjectThatTheSkillNamesIsThere()
    {
        var relative = CodeSpans()
            .Where(span => Regex.IsMatch(span, @"^(Integration|EndToEnd|Smoke|Journeys|Steps|Hooks|Fixtures)/[\w./-]*$"))
            .ToArray();

        relative.Should().Contain(["Integration/Generation.feature", "Journeys/ModelToCSharp.feature", "Smoke/Version.feature", "Hooks/ScenarioHooks.cs"]);
        relative.Where(path => !Exists("tests/Morphir.BlackBox.Tests/" + path)).Should().BeEmpty();
    }

    [Test]
    public void EachTagOfTheSkillIsATagOfTheCode()
    {
        var known = TestPlan.Groups
            .Append(TestNeeds.LaneTag)
            .Concat(TestLanes.All.Where(lane => lane != TestLane.None).Select(TestLanes.Name))
            .Concat(MorphirHosts.All.Select(host => TestNeeds.HostTagPrefix + host.Name()))
            .Concat(DriverCapabilities.All.Select(DriverCapabilities.TagOf))
            .Select(tag => "@" + tag)
            .ToHashSet();

        var tags = Regex.Matches(Skill.Value, @"(?<![\w/])@[a-z][a-z0-9-]*").Select(match => match.Value).Distinct().ToArray();

        tags.Should().Contain(["@integration", "@e2e", "@smoke", "@journey", "@lane", "@packages", "@native-aot", "@published", "@host-managed"]);
        tags.Where(tag => !known.Contains(tag)).Should().BeEmpty("each tag that the skill names is a group, a lane, a host or a capability of the code");
    }

    [Test]
    public void EachGuardTestOfTheSkillIsATestOfTheBuildTests()
    {
        var source = Read("tests/Morphir.Build.Tests/Helpers/BlackBoxRulesTests.cs");
        var guards = CodeSpans().Where(span => Regex.IsMatch(span, @"^(Each|The)[A-Z][A-Za-z]{20,}$")).ToArray();

        guards.Should().HaveCountGreaterThanOrEqualTo(8);
        guards.Where(guard => !source.Contains($"public void {guard}(", StringComparison.Ordinal))
            .Should().BeEmpty("each guard test that the skill names is a test of BlackBoxRulesTests");
    }

    [Test]
    public void EachCommandOfTheSkillUsesATargetAndOptionsOfTheBuild()
    {
        var build = string.Join("\n", Directory.EnumerateFiles(Path.Combine(Root, "build"), "*.cs").Select(File.ReadAllText));
        var commands = Skill.Value.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("./build.sh", StringComparison.Ordinal)).ToArray();

        commands.Should().NotBeEmpty();
        foreach (var command in commands)
        {
            var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeWhile(word => word != "#").ToArray();
            for (var index = 1; index < words.Length; index++)
            {
                if (!words[index].StartsWith("--", StringComparison.Ordinal)) continue;
                if (words[index] == "--target")
                {
                    build.Should().MatchRegex($@"Target {words[index + 1]}\s*=>", $"the target of '{command}' is a target of the build");
                }
                else if (words[index] != "--skip")
                {
                    var parameter = string.Concat(words[index][2..].Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
                    build.Should().MatchRegex($@"readonly [\w?]+ {parameter}\b", $"the option {words[index]} of '{command}' is a parameter of the build");
                }
            }
        }

        foreach (var script in Regex.Matches(Skill.Value, @"dotnet fsi (scripts/[\w./-]+)").Select(match => match.Groups[1].Value).Distinct())
            Exists(script).Should().BeTrue($"{script} is a script of the repository");
        foreach (var assembly in Regex.Matches(Skill.Value, @"dotnet exec (tests/[\w.-]+)/bin/").Select(match => match.Groups[1].Value).Distinct())
            Exists(assembly).Should().BeTrue($"{assembly} is a test project");
    }

    [Test]
    public void EachTypeOfTheSkillIsATypeOfTheRepository()
    {
        string[] folders =
        [
            "tests/Morphir.TestSupport", "tests/Morphir.TestSupport.Tests", "tests/Morphir.BlackBox.Tests", "tests/Morphir.Build.Tests/Helpers",
            "tests/Morphir.Tooling.Tests/Features/Generate", "build", "src/Morphir.CodeGeneration.CSharp",
        ];
        var sources = string.Join("\n", folders.SelectMany(folder =>
            Directory.EnumerateFiles(Path.Combine(Root, folder.Replace('/', Path.DirectorySeparatorChar)), "*.*", SearchOption.AllDirectories)
                .Where(file => Path.GetExtension(file) is ".cs" or ".fs")
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText)));
        // PEReader is a type of the .NET libraries.
        string[] external = ["PEReader"];

        var types = CodeSpans()
            .Where(span => Regex.IsMatch(span, @"^I?[A-Z][a-z0-9]+([A-Z][A-Za-z0-9]*)+(\.[A-Z]\w+)?(\(\))?$"))
            .Where(span => !Regex.IsMatch(span, @"^(Each|The)[A-Z][A-Za-z]{20,}$"))
            .Except(external)
            .ToArray();

        types.Should().Contain(["BlackBoxTest", "IScenarioWorkspace", "CommandResultAssertions", "DriverCapability", "TestPlan.Suites"]);
        types.Where(type => type.TrimEnd('(', ')').Split('.').Any(part => !Regex.IsMatch(sources, $@"\b{Regex.Escape(part)}\b")))
            .Should().BeEmpty("each type and member that the skill names is in the code");
    }

    [Test]
    public void TheReviewerAgentOfEachToolPointsToTheSkill()
    {
        string[] agents =
        [
            ".claude/agents/test-manager-reviewer.md",
            ".codex/agents/test-manager-reviewer.toml",
            ".github/agents/test-manager-reviewer.agent.md",
            ".grok/agents/test-manager-reviewer.md",
        ];

        foreach (var agent in agents)
        {
            var text = Read(agent);
            text.Should().Contain(".agents/skills/test-manager/SKILL.md", $"{agent} reads the rules from the skill");
            text.Should().Contain("You do not edit files.");
            text.Should().Contain("path:line: rule N: problem. fix.");
        }
    }

    [Test]
    [Arguments("AGENTS.md")]
    [Arguments("CLAUDE.md")]
    [Arguments(".agents/skills-reference.md")]
    public void TheAgentDocumentsNameTheSkillAndLinkToIt(string document)
    {
        var text = Read(document);

        text.Should().Contain("test-manager");
        text.Should().MatchRegex(@"\]\(\./(\.agents/)?skills/test-manager/(SKILL\.md)?\)", $"{document} links to the skill");
    }

    [Test]
    public void TheSkillIsInTheListOfTheSkillsThatTheSyncScriptCopies() =>
        Read("scripts/sync-agent-skills.fsx").Should().MatchRegex(@"let ownedSkills = \[[^\]]*""test-manager""");
}
