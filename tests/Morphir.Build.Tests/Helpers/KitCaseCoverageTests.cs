// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
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
public class KitCaseCoverageTests
{
    private const string Feature = "Integration/ModelConversionKitCases.feature";

    /// <summary>The cases of the kit's <c>document-tree.feature</c> that the catalog pins.</summary>
    private static readonly string[] PinnedIds = [.. Enumerable.Range(1, 17).Select(number => $"document-tree-{number:D4}")];

    private static IEnumerable<(string Id, string Title)> CatalogCases() =>
        FixtureCatalog.Kit.FileSpellings.Select(spelling => (spelling.Id, spelling.Title))
            .Concat(FixtureCatalog.Kit.DocumentTree.Select(kitCase => (kitCase.Id, kitCase.Title)))
            .OrderBy(kitCase => kitCase.Id, StringComparer.Ordinal);

    private static IReadOnlyList<string> ScenarioNames()
    {
        var path = Path.Combine(TestFixture.GetRepositoryRoot(), "tests", "Morphir.BlackBox.Tests", Feature);
        return [.. BlackBoxRules.ParseFeature(Feature, File.ReadAllText(path)).Select(test => test.Name)];
    }

    [Test]
    public void ThePinnedKitCasesAreTheCasesOfTheCatalog() =>
        CatalogCases().Select(kitCase => kitCase.Id).Should().Equal(PinnedIds);

    [Test]
    public void EachPinnedKitCaseHasAScenarioNamedWithItsIdAndTitle() =>
        ScenarioNames().Should().Contain(CatalogCases().Select(kitCase => $"{kitCase.Id} {kitCase.Title}"));

    [Test]
    public void EachScenarioOfTheKitCasesIsOfAPinnedKitCase() =>
        ScenarioNames().Where(name => !PinnedIds.Any(id => name.StartsWith(id + " ", StringComparison.Ordinal)))
            .Should().BeEmpty("each scenario of {0} is of one pinned kit case", Feature);
}
