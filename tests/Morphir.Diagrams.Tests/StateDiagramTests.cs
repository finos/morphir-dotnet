// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Xml.Linq;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>State diagrams: the Mermaid reader against the pinned reference, the model, printing and drawing.</summary>
public class StateDiagramTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static StateDiagram Read(string source)
    {
        DiagramSource.TryParse(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return DiagramMother.Of<StateDiagram>(document);
    }

    private static IEnumerable<JsonElement> Fixtures()
    {
        using var reference = JsonDocument.Parse(PinnedCorpus.File(FixtureCatalog.DiagramsMbt.States, "/reference.json"));
        return reference.RootElement.GetProperty("fixtures").EnumerateArray().Select(fixture => fixture.Clone()).ToList();
    }

    [Test]
    public void EveryReferenceFixtureIsAcceptedOrRefusedAsMermaidDoesWithTheSameStatesAndTransitions()
    {
        var mismatches = new List<string>();

        foreach (var fixture in Fixtures())
        {
            var name = fixture.GetProperty("name").GetString();
            var accepted = fixture.GetProperty("accepted").GetBoolean();
            var read = Mermaid.TryParse(fixture.GetProperty("source").GetString(), out var document, out var diagnostics);

            if (read != accepted)
            {
                mismatches.Add($"{name}: accepted {read} {string.Join(", ", diagnostics)}");
                continue;
            }

            if (!accepted)
            {
                continue;
            }

            var normalized = fixture.GetProperty("normalized");
            var model = DiagramMother.Of<StateDiagram>(document);
            var states = model.States.Select(state => $"{state.Id}|{state.Role}|{state.Region}");
            var expected = normalized.GetProperty("states").EnumerateArray().Select(state =>
                $"{state.GetProperty("id").GetString()}|{state.GetProperty("role").GetString()!.ToLowerInvariant()}|{state.GetProperty("region").GetString()}");
            var transitions = model.Transitions.Select(transition => $"{transition.Source}>{transition.Target}");
            var expectedTransitions = normalized.GetProperty("transitions").EnumerateArray().Select(transition =>
                $"{transition.GetProperty("source").GetString()}>{transition.GetProperty("target").GetString()}");

            if (!states.SequenceEqual(expected) || !transitions.SequenceEqual(expectedTransitions))
            {
                mismatches.Add($"{name}: {string.Join(", ", states)} / {string.Join(", ", transitions)}");
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void PrintingWhatWasPrintedGivesTheSameTextForEveryAcceptedFixture()
    {
        var failures = new List<string>();

        foreach (var fixture in Fixtures().Where(fixture => fixture.GetProperty("accepted").GetBoolean()))
        {
            Mermaid.TryParse(fixture.GetProperty("source").GetString(), out var document, out _).ShouldBeTrue();

            if (!Mermaid.TryPrint(document!.Diagram, out var first, out var diagnostics))
            {
                failures.Add($"{fixture.GetProperty("name")}: {string.Join(", ", diagnostics)}");
            }
            else if (!Mermaid.TryParse(first, out var reread, out var errors) || !Mermaid.TryPrint(reread!.Diagram, out var second, out _) || second != first)
            {
                failures.Add($"{fixture.GetProperty("name")}: {string.Join(", ", errors)}\n{first}");
            }
            else if (reread.Diagram.ToString() != document.Diagram.ToString())
            {
                failures.Add($"{fixture.GetProperty("name")}: {reread.Diagram} after printing");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public void InitialStatesAreScopedToTheirRegion()
    {
        var model = Read("stateDiagram-v2\n  [*] --> Idle\n  state Active {\n    [*] --> Working\n  }\n  Idle --> Active\n");

        model.States.Where(state => state.Role.Equals(StateRole.Initial)).Select(state => state.Id.Value)
            .ShouldBe(["root::initial", "Active::region-0::initial"]);
    }

    [Test]
    public void ConcurrentRegionsBelongToOneComposite()
    {
        var model = Read("stateDiagram-v2\n  state Work {\n    A\n    --\n    B\n  }\n");

        model.Regions.Select(region => $"{region.Id}<{region.Owner}").ShouldBe(["root<", "Work::region-0<Work", "Work::region-1<Work"]);
        model.States.Single(state => state.Id.Value == "B").Region.ShouldBe("Work::region-1");
    }

    [Test]
    public void AChoiceIsAPseudostateAndParallelTransitionsStayDistinct()
    {
        var model = Read("stateDiagram-v2\n  state Check <<choice>>\n  A --> B : retry\n  A --> B : skip\n  B --> Check\n");

        model.States.Single(state => state.Id.Value == "Check").Role.ShouldBe(StateRole.Choice);
        model.Transitions.Take(2).Select(transition => transition.Label).ShouldBe(["retry", "skip"]);
    }

    [Test]
    public void ATransitionLabelSplitsIntoEventGuardAndAction()
    {
        var transition = Read("stateDiagram-v2\n  Idle --> Running : start [ready] / begin\n").Transitions.ShouldHaveSingleItem();

        (transition.Event, transition.Guard, transition.Action).ShouldBe(("start", "ready", "begin"));
    }

    [Test]
    public void ValidationReportsAMissingEndpointWithoutRepairingIt()
    {
        var model = StateDiagram.Create(
            FlowDirection.TopToBottom,
            [DiagramState.Create(DiagramId.Create("A"), "A", StateRole.Simple, "root", [])],
            [StateRegion.Create("root", null)],
            [StateTransition.Create(DiagramId.Create("A"), DiagramId.Create("Ghost"), "")]);

        model.Validate().Select(diagnostic => diagnostic.Code).ShouldBe(["state/missing-endpoint"]);
        model.Transitions.ShouldHaveSingleItem();
    }

    [Test]
    public void EveryMalformedStatementIsReportedWithItsLine()
    {
        Mermaid.TryParse("stateDiagram-v2\n  A --> B\n  A -> C\n  B --> C\n  }\n", out _, out var diagnostics).ShouldBeFalse();

        diagnostics.Select(diagnostic => diagnostic.Span!.Start.Line).ShouldBe([3, 5]);
    }
}
