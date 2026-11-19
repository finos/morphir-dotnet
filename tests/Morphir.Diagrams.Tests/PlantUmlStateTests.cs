// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>PlantUML state blocks read into the state model, checked against the pinned PlantUML 1.2026.9 reference.</summary>
public class PlantUmlStateTests
{
    private static (IReadOnlyDictionary<string, string> Files, JsonElement[] Fixtures, HashSet<string> Refusals) Corpus()
    {
        var files = PinnedCorpus.Files(FixtureCatalog.DiagramsMbt.PlantUmlState)
            .ToDictionary(file => file.Key.Replace("tests/corpus/plantuml-state/", ""), file => file.Value);
        using var reference = JsonDocument.Parse(files["reference.json"]);
        using var expectations = JsonDocument.Parse(files["expectations.json"]);
        var fixtures = reference.RootElement.GetProperty("fixtures").EnumerateArray().Select(fixture => fixture.Clone()).ToArray();
        return (files, fixtures, expectations.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet());
    }

    private static StateDiagram Read(string body)
    {
        PlantUml.TryParse($"@startuml\n{body}\n@enduml\n", out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return DiagramMother.Of<StateDiagram>(document);
    }

    [Test]
    public void EveryReferenceFixtureIsAcceptedOrRefusedAsPlantUmlDoesWithItsLinks()
    {
        var (files, fixtures, refusals) = Corpus();
        var mismatches = new List<string>();

        foreach (var fixture in fixtures)
        {
            var name = fixture.GetProperty("name").GetString()!;
            var accepted = fixture.GetProperty("status").GetString() == "accepted" && !refusals.Contains(name);
            var read = PlantUml.TryParse(files["fixtures/" + name], out var document, out var diagnostics);

            if (read != accepted)
            {
                mismatches.Add($"{name}: accepted {read} {string.Join(", ", diagnostics)}");
            }
            else if (read)
            {
                var model = DiagramMother.Of<StateDiagram>(document);

                // PlantUML counts a note's attachment as a link.
                if (model.Transitions.Length + model.Notes.Length != fixture.GetProperty("links").GetArrayLength())
                {
                    mismatches.Add($"{name}: {model}");
                }
            }
        }

        fixtures.Length.ShouldBe(122);
        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void PrintingWhatWasPrintedGivesTheSameTextForEveryAcceptedFixture()
    {
        var (files, fixtures, _) = Corpus();
        var failures = new List<string>();

        foreach (var fixture in fixtures)
        {
            if (!PlantUml.TryParse(files["fixtures/" + fixture.GetProperty("name").GetString()], out var document, out _))
            {
                continue;
            }

            if (!PlantUml.TryPrint(document!.Diagram, out var first, out var diagnostics))
            {
                failures.Add($"{fixture.GetProperty("name")}: {string.Join(", ", diagnostics)}");
            }
            else if (!PlantUml.TryParse(first, out var reread, out var errors) || !PlantUml.TryPrint(reread!.Diagram, out var second, out _) || second != first)
            {
                failures.Add($"{fixture.GetProperty("name")}: {string.Join(", ", errors)}\n{first}");
            }
            else if (reread.Diagram.ToString() != document.Diagram.ToString())
            {
                failures.Add($"{fixture.GetProperty("name")}: {reread.Diagram} after printing, {document.Diagram} before");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public void HistoryEntryPointsAndBarRegionsAreRead()
    {
        var model = Read("state Service {\n  state In <<entryPoint>>\n  [*] --> Worker\n  ||\n  [*] --> Monitor\n}\nPaused --> Service[H] : resume\nPaused --> Service[H*] : deep\n");

        model.States.Single(state => state.Id.Value == "In").Role.ShouldBe(StateRole.EntryPoint);
        model.States.Single(state => state.Id.Value == "Service::history").Role.ShouldBe(StateRole.ShallowHistory);
        model.States.Single(state => state.Id.Value == "Service::deep-history").Role.ShouldBe(StateRole.DeepHistory);
        model.Regions.Count(region => region.Owner?.Value == "Service").ShouldBe(2);
    }

    [Test]
    public void DirectionWordsInArrowsAndReversedArrowsKeepTheirMeaning()
    {
        var model = Read("A -right-> B : r\nC <-- D : back\nE -[#red,dashed]-> F\n");

        model.Transitions.Select(transition => $"{transition.Source}>{transition.Target}").ShouldBe(["A>B", "D>C", "E>F"]);
    }

    [Test]
    public void ATransitionBetweenConcurrentRegionsIsRefused()
    {
        PlantUml.TryParse("@startuml\nstate C {\n  state A\n  --\n  state B\n  A --> B\n}\n@enduml\n", out _, out var diagnostics).ShouldBeFalse();

        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("plantuml/cross-region");
    }
}
