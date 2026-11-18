// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport.Fixtures;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The black-box scenarios of the kit's document-tree cases (add-model-convert 6.3, "Kit tree cases are met"): the
/// pinned cases are the cases of <see cref="FixtureCatalog.Kit"/>, and
/// <c>tests/Morphir.BlackBox.Tests/Integration/ModelConversionKitCases.feature</c> has a scenario for each of them,
/// named with its kit id and its title. A move of the kit pin that adds, drops or renames a case fails here until the
/// feature follows it.
/// </summary>
public partial class KitCaseCoverageTests
{
    private const string Feature = "Integration/ModelConversionKitCases.feature";

    /// <summary>The cases of the kit's <c>document-tree.feature</c> that the catalog pins.</summary>
    private static readonly string[] PinnedIds = [.. Enumerable.Range(1, 17).Select(number => $"document-tree-{number:D4}")];

    private static IEnumerable<(string Id, string Title)> CatalogCases() =>
        FixtureCatalog.Kit.FileSpellings.Select(spelling => (spelling.Id, spelling.Title))
            .Concat(FixtureCatalog.Kit.DocumentTree.Select(kitCase => (kitCase.Id, kitCase.Title)))
            .OrderBy(kitCase => kitCase.Id, StringComparer.Ordinal);

    private static string FeatureText() =>
        File.ReadAllText(Path.Combine(TestFixture.GetRepositoryRoot(), "tests", "Morphir.BlackBox.Tests", Feature)).Replace("\r\n", "\n");

    private static IReadOnlyList<string> ScenarioNames() => [.. BlackBoxRules.ParseFeature(Feature, FeatureText()).Select(test => test.Name)];

    /// <summary>Each scenario's name, with the kit ids that its Given steps (the steps before its first When) name.</summary>
    private static IEnumerable<(string Name, string[] Ids)> ScenarioGivens()
    {
        string? name = null;
        var ids = new List<string>();
        var givens = false;
        foreach (var line in FeatureText().Split('\n').Select(line => line.Trim()))
        {
            if (line.StartsWith("Scenario:", StringComparison.Ordinal))
            {
                if (name is not null) yield return (name, [.. ids]);
                name = line["Scenario:".Length..].Trim();
                ids.Clear();
                givens = true;
            }
            else if (line.StartsWith("When ", StringComparison.Ordinal))
            {
                givens = false;
            }
            else if (givens && (line.StartsWith("Given ", StringComparison.Ordinal) || line.StartsWith("And ", StringComparison.Ordinal)))
            {
                ids.AddRange(KitId().Matches(line).Select(match => match.Value));
            }
        }

        if (name is not null) yield return (name, [.. ids]);
    }

    [GeneratedRegex(@"document-tree-\d{4}")]
    private static partial Regex KitId();

    [Test]
    public void ThePinnedKitCasesAreTheCasesOfTheCatalog() =>
        CatalogCases().Select(kitCase => kitCase.Id).Should().Equal(PinnedIds);

    [Test]
    public void EachPinnedKitCaseHasAScenarioNamedWithItsIdAndTitle() =>
        ScenarioNames().Should().Contain(CatalogCases().Select(kitCase => $"{kitCase.Id} {kitCase.Title}"));

    /// <summary>
    /// The Given steps of each scenario load the case of its own id. Only a case that rejects a document
    /// (<c>document-tree-0016</c>) also names the kit tree case that holds the document.
    /// </summary>
    [Test]
    public void EachScenarioLoadsTheKitCaseOfItsOwnId()
    {
        var scenarios = ScenarioGivens().ToArray();
        scenarios.Should().NotBeEmpty();
        foreach (var (name, ids) in scenarios)
        {
            var own = name.Split(' ')[0];
            ids.Should().Contain(own, $"the Given steps of '{name}' load its own kit case");
            var rejects = FixtureCatalog.Kit.DocumentTree.Any(kitCase => kitCase.Id == own && kitCase.RejectedDocument is not null);
            var others = ids.Where(id => id != own).ToArray();
            if (rejects)
                others.Should().OnlyContain(id => FixtureCatalog.Kit.Case(id).Files.Count > 0, $"'{name}' names only a tree case that holds its rejected document");
            else
                others.Should().BeEmpty($"the Given steps of '{name}' load no other kit case");
        }
    }

    [Test]
    public void EachScenarioOfTheKitCasesIsOfAPinnedKitCase() =>
        ScenarioNames().Where(name => !PinnedIds.Any(id => name.StartsWith(id + " ", StringComparison.Ordinal)))
            .Should().BeEmpty("each scenario of {0} is of one pinned kit case", Feature);
}
