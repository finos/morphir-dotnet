// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Xml.Linq;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>PlantUML regular expressions and EBNF grammars as railroad diagrams, and traces drawn over them.</summary>
public class RailroadTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <summary>Each regex and EBNF fixture with whether PlantUML accepts it and the library does not refuse it on purpose.</summary>
    private static IEnumerable<(string Name, string Source, bool Accepted)> Fixtures()
    {
        var files = DiagramCorpus.Files(FixtureCatalog.DiagramsMbt.PlantUml);
        string File(string suffix) => files.Single(file => file.Key.EndsWith(suffix, StringComparison.Ordinal)).Value;

        using var expectations = JsonDocument.Parse(File("/expectations.json"));
        var refusals = expectations.RootElement.EnumerateObject()
            .Where(entry => entry.Value.GetProperty("category").GetString() == "library-refusal")
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        using var reference = JsonDocument.Parse(File("/reference.json"));

        return reference.RootElement.GetProperty("fixtures").EnumerateArray()
            .Select(fixture => fixture.GetProperty("name").GetString()!)
            .Where(name => name.StartsWith("regex/", StringComparison.Ordinal) || name.StartsWith("ebnf/", StringComparison.Ordinal))
            .Select(name => (
                name,
                File("/fixtures/" + name),
                reference.RootElement.GetProperty("fixtures").EnumerateArray().Single(fixture => fixture.GetProperty("name").GetString() == name)
                    .GetProperty("status").GetString() == "accepted" && !refusals.Contains(name)))
            .ToList();
    }

    private static RailroadDiagram Read(string source)
    {
        PlantUml.TryParse(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return DiagramMother.Of<RailroadDiagram>(document);
    }

    private static RailroadDiagram Regex(string pattern) => Read($"@startregex\n{pattern}\n@endregex\n");

    private static RailroadDiagram Ebnf(string rules) => Read($"@startebnf\n{rules}\n@endebnf\n");

    private static XDocument Render(RailroadDiagram model, SvgRenderOptions? options = null) =>
        XDocument.Parse(SvgRenderer.Render(Diagram.NewRailroad(model), options ?? SvgRenderOptions.Default).Output!);

    [Test]
    public void EveryRegexAndEbnfFixtureIsAcceptedOrRefusedAsPlantUmlDoes()
    {
        var fixtures = Fixtures().ToList();
        var mismatches = fixtures.Where(fixture => PlantUml.TryParse(fixture.Source, out _, out _) != fixture.Accepted).Select(fixture => fixture.Name);

        fixtures.Count.ShouldBe(125);
        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void PrintingKeepsTheStructureAndPrintingAgainGivesTheSameText()
    {
        var failures = new List<string>();

        foreach (var fixture in Fixtures().Where(fixture => fixture.Accepted))
        {
            var model = Read(fixture.Source);
            PlantUml.TryPrint(Diagram.NewRailroad(model), out var first, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
            var again = Read(first!);
            PlantUml.TryPrint(Diagram.NewRailroad(again), out var second, out _).ShouldBeTrue();

            if (second != first || again.Fingerprint != model.Fingerprint)
            {
                failures.Add($"{fixture.Name}: {first}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public void EqualAlternativesAreDistinctOccurrences()
    {
        var model = Regex("ab|ab");

        model.Rules.ShouldHaveSingleItem().Body.Kind.ShouldBe(RailroadKind.Choice);
        model.OccurrenceIds.ShouldBe(["track:0.0", "track:0.1"]);
        model.OccurrenceOf(model.Rules[0].Body.Children[1]).ShouldBe("track:0.1");
    }

    [Test]
    public void ALargeBoundStaysANumberAndDrawsOneBox()
    {
        var model = Regex("a{2,1000000}");
        var repeat = model.Rules[0].Body;

        repeat.Kind.ShouldBe(RailroadKind.Repeat);
        (repeat.Minimum, repeat.Maximum).ShouldBe((2, 1000000));
        model.OccurrenceIds.ShouldHaveSingleItem();
        Render(model).Descendants(Svg + "text").Select(text => text.Value).ShouldContain("2..1000000");
    }

    [Test]
    public void GroupsNestedPastTheDepthLimitAreRefusedWithALimitDiagnostic()
    {
        var pattern = new string('(', 70) + "a" + new string(')', 70);

        PlantUml.TryParse($"@startregex\n{pattern}\n@endregex\n", out _, out var diagnostics).ShouldBeFalse();
        diagnostics.ShouldHaveSingleItem().Code.ShouldEndWith("-limit");
    }

    [Test]
    public void AnOptionalPartHasABypassAboveItsTrack()
    {
        // A track that runs above the top of the box bypasses it.
        bool Bypassed(RailroadDiagram model)
        {
            var svg = Render(model);
            var top = int.Parse(svg.Descendants(Svg + "rect").Single(rect => (string?)rect.Attribute("class") == "box").Attribute("y")!.Value);

            return svg.Descendants(Svg + "path")
                .Where(path => (string?)path.Attribute("class") == "track")
                .SelectMany(path => System.Text.RegularExpressions.Regex.Matches(path.Attribute("d")!.Value, @"V(-?\d+)"))
                .Any(found => int.Parse(found.Groups[1].Value) < top);
        }

        Bypassed(Ebnf("number = [ sign ];")).ShouldBeTrue();
        Bypassed(Ebnf("number = sign;")).ShouldBeFalse();
    }

    [Test]
    public void AReferenceIsABoxAndIsNotInlined()
    {
        var svg = Render(Ebnf("a = b;\nb = \"x\";"));
        string? Kind(string reference) => svg.Descendants().Single(element => (string?)element.Attribute("data-ref") == reference).Attribute("data-kind")?.Value;

        Kind("node=a:0").ShouldBe("nonterminal");
        Kind("node=b:0").ShouldBe("terminal");
        svg.Descendants().Count(element => element.Attribute("data-kind") is not null).ShouldBe(2);
    }

    [Test]
    public void TheTrackFollowsChoicesAndLoopsForExploration()
    {
        var diagram = Diagram.NewRailroad(Regex("a(b|c)+d"));

        DiagramExploration.Route(diagram, DiagramId.Create("track:0.0"), DiagramId.Create("track:0.2"), LinkFollowing.Arrowheads).ToString()
            .ShouldBe("Structural route track:0.0 → track:0.1.0.0.0 → track:0.2");
        DiagramExploration.Route(diagram, DiagramId.Create("track:0.1.0.0.1"), DiagramId.Create("track:0.1.0.0.0"), LinkFollowing.Arrowheads).ToString()
            .ShouldBe("Structural route track:0.1.0.0.1 → track:0.1.0.0.0");
        DiagramExploration.Route(diagram, DiagramId.Create("track:0.2"), DiagramId.Create("track:0.0"), LinkFollowing.Arrowheads)
            .ShouldBe(ExplorationResult.Unreachable);
    }

    [Test]
    public void AFailureWithNoTakenPrefixHighlightsOnlyThatBox()
    {
        var model = Ebnf("r = \"a\", \"b\", \"c\", \"d\";");
        var trace = RailroadTrace.Create(model, RailroadOutcome.NotMatched, [], model.OccurrenceIds[2]);

        var highlighted = Render(model, SvgRenderOptions.Default.WithRailroadTrace(trace))
            .Descendants().Where(element => element.Attribute("data-occurrence") is not null).ToList();

        highlighted.ShouldHaveSingleItem().Attribute("data-occurrence")!.Value.ShouldBe("r:0.2");
        highlighted[0].Parent!.Attribute("data-trace")!.Value.ShouldBe("failure");
    }

    [Test]
    public void MatchedTakenAndNotMatchedVisitsAreSeparateLayers()
    {
        var model = Regex("ab|cd");
        string[] Layers(RailroadOutcome outcome) =>
            Render(model, SvgRenderOptions.Default.WithRailroadTrace(RailroadTrace.Create(
                    model,
                    outcome,
                    [RailroadVisit.Create("track:0.0", RailroadVisitResult.NotMatched), RailroadVisit.Create("track:0.1", RailroadVisitResult.Matched, 0, 2)],
                    null)))
                .Descendants().Select(element => (string?)element.Attribute("data-trace")).OfType<string>().ToArray();

        Layers(RailroadOutcome.Matched).ShouldBe(["not-matched", "matched"]);
        Layers(RailroadOutcome.Exhausted).ShouldBe(["not-matched", "taken"]);
    }

    [Test]
    public void AnEmptyTraceRendersTheSameBytesAsNoTrace()
    {
        var model = Regex("a+b?");
        var plain = SvgRenderer.Render(Diagram.NewRailroad(model)).Output;
        var traced = SvgRenderer.Render(
            Diagram.NewRailroad(model),
            SvgRenderOptions.Default.WithRailroadTrace(RailroadTrace.Create(model, RailroadOutcome.Unsupported, [], null))).Output;

        traced.ShouldBe(plain);
        plain!.ShouldNotContain("data-trace");
    }

    [Test]
    public void ATraceForAnotherDiagramOrAMissingBoxIsRejected()
    {
        var model = Regex("abc");
        var stale = RailroadTrace.Create(Regex("abd"), RailroadOutcome.Matched, [RailroadVisit.Create("track:0", RailroadVisitResult.Matched)], null);
        var missing = RailroadTrace.Create(model, RailroadOutcome.Matched, [RailroadVisit.Create("track:9", RailroadVisitResult.Matched)], null);

        var rendered = SvgRenderer.Render(Diagram.NewRailroad(model), SvgRenderOptions.Default.WithRailroadTrace(stale));
        rendered.Output.ShouldBeNull();
        rendered.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("railroad/stale-trace");
        SvgRenderer.Render(Diagram.NewRailroad(model), SvgRenderOptions.Default.WithRailroadTrace(missing)).Diagnostics.ShouldHaveSingleItem().Code
            .ShouldBe("railroad/trace-occurrence");
    }

    [Test]
    public void ThePlayerVisitsRuleStartsAndBoxesInDrawingOrder()
    {
        using var data = AnimatedDiagramTests.ReadMotionData(DiagramSource.RenderAnimated("@startebnf\na = b, \"x\";\nb = \"y\";\n@endebnf\n").Output!);

        data.RootElement.GetProperty("steps").GetArrayLength().ShouldBe(0);
        data.RootElement.GetProperty("visits").EnumerateArray().Select(visit => visit.GetProperty("ref").GetString())
            .ShouldBe(["node=a", "node=a:0.0", "node=a:0.1", "node=b", "node=b:0"]);
    }
}
