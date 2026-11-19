// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>PlantUML activity diagrams in both dialects, read into the swimlane model as structured flow.</summary>
public class PlantUmlActivityTests
{
    private static SwimlaneDiagram Read(string body)
    {
        PlantUml.TryParse($"@startuml\n{body}\n@enduml\n", out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return DiagramMother.Of<SwimlaneDiagram>(document);
    }

    private static string Label(SwimlaneDiagram model, DiagramId id) =>
        model.Steps.Single(step => step.Id.Equals(id)) is var step && step.Label.Length > 0 ? step.Label : step.Role.ToString();

    private static IEnumerable<string> Flow(SwimlaneDiagram model) =>
        model.Connectors.Select(connector => $"{Label(model, connector.Source)}>{Label(model, connector.Target)}{(connector.Label is null ? "" : $"[{connector.Label}]")}");

    private static (IReadOnlyDictionary<string, string> Files, JsonElement[] Fixtures, HashSet<string> Refusals) Corpus()
    {
        var files = PinnedCorpus.Files(FixtureCatalog.DiagramsMbt.PlantUmlActivity)
            .ToDictionary(file => file.Key.Replace("tests/corpus/plantuml-activity/", ""), file => file.Value);
        using var reference = JsonDocument.Parse(files["reference.json"]);
        using var expectations = JsonDocument.Parse(files["expectations.json"]);
        return (files, reference.RootElement.GetProperty("fixtures").EnumerateArray().Select(fixture => fixture.Clone()).ToArray(),
            expectations.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet());
    }

    [Test]
    public void EveryReferenceFixtureIsReadWithTheReferenceActions()
    {
        var (files, fixtures, refusals) = Corpus();
        var mismatches = new List<string>();

        foreach (var fixture in fixtures)
        {
            var name = fixture.GetProperty("name").GetString()!;
            var read = PlantUml.TryParse(files["fixtures/" + name], out var document, out var diagnostics);

            if (read == refusals.Contains(name))
            {
                mismatches.Add($"{name}: accepted {read} {string.Join(", ", diagnostics)}");
                continue;
            }

            if (!read)
            {
                continue;
            }

            // Modern references are instruction trees, legacy references node lists; repeat's backward action is a property.
            var expected = fixture.TryGetProperty("instructions", out var instructions)
                ? Regex.Matches(instructions.ToString(), "\"kind\": ?\"InstructionSimple\"").Count + Regex.Matches(instructions.ToString(), "\"backward\": ?\"[^\"]+\"").Count
                : fixture.GetProperty("nodes").EnumerateArray().Count(node => node.GetProperty("role").GetString() == "ACTIVITY");
            var actions = DiagramMother.Of<SwimlaneDiagram>(document).Steps.Count(step => step.Role.Equals(SwimlaneRole.Action));

            if (actions != expected)
            {
                mismatches.Add($"{name}: {actions} actions, PlantUML {expected}");
            }
        }

        fixtures.Length.ShouldBe(96);
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
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public void TheLegacyDialectReadsBranchesAndBars()
    {
        var model = Read("(*) --> \"Read\"\nif \"Ready?\" then\n  -->[yes] \"Write\"\nelse\n  -->[no] ===B1===\nendif\n--> (*)");

        Flow(model).ShouldContain("Ready?>Write[yes]");
        model.Steps.ShouldContain(step => step.Role.Equals(SwimlaneRole.Fork));
    }

    [Test]
    public void MixingTheDialectsIsReported()
    {
        PlantUml.TryParse("@startuml\n(*) --> \"A\"\n:modern;\n@enduml\n", out _, out var diagnostics).ShouldBeFalse();

        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("plantuml/mixed-activity-dialect");
    }
}
