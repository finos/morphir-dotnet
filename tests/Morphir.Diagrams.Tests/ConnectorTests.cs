// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>Connector styles, attachments spread along a side, arrowhead approaches and bridges.</summary>
public partial class ConnectorTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <summary>D → E crosses the links that leave A for G and H.</summary>
    private const string Crossing = """
        flowchart TD
          A --> C
          B --> C
          D --> E
          A --> E
          F --> A
          A --> G
          A --> H
        """;

    private static XElement Render(string source, SvgRenderOptions options) =>
        XElement.Parse(Mermaid.RenderSvg(source, options).Output.ShouldNotBeNull());

    private static string PathOf(XElement svg, string source, string target) =>
        ((string?)svg.Descendants(Svg + "path")
            .Single(path => (string?)path.Attribute("data-source") == source && (string?)path.Attribute("data-target") == target)
            .Attribute("d")).ShouldNotBeNull();

    private static List<(int X, int Y)> Points(string data) =>
        Coordinates().Matches(data).Select(match => (
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))).ToList();

    [GeneratedRegex(@"(-?\d+),(-?\d+)")]
    private static partial Regex Coordinates();

    [Test]
    public void WithoutAStyleConnectorsAreElbows() =>
        Mermaid.RenderSvg(Crossing).Output.ShouldBe(
            Mermaid.RenderSvg(Crossing, SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Elbow)).Output);

    [Test]
    [Arguments("rounded", "Q")]
    [Arguments("curved", "C")]
    public void AStyleBendsTheConnectorsAndKeepsTheirEnds(string style, string command)
    {
        var chosen = style == "rounded" ? ConnectorStyle.Rounded : ConnectorStyle.Curved;
        var elbow = Render(Crossing, SvgRenderOptions.Default);
        var styled = Render(Crossing, SvgRenderOptions.Default.WithConnectorStyle(chosen));

        var before = Points(PathOf(elbow, "A", "G"));
        var after = PathOf(styled, "A", "G");

        after.ShouldContain(command);
        Points(after)[0].ShouldBe(before[0]);
        Points(after)[^1].ShouldBe(before[^1]);
    }

    [Test]
    public void LinksThatLeaveOneSideOfABoxAreSpacedEvenly()
    {
        // B is straight ahead of A; C, D and E turn once out of A's lower side.
        var svg = Render("flowchart LR\n  A --> B\n  A --> C\n  A --> D\n  A --> E\n", SvgRenderOptions.Default);

        var starts = new[] { "C", "D", "E" }.Select(target => Points(PathOf(svg, "A", target))[0]).OrderBy(point => point.X).ToList();

        starts.Select(point => point.Y).Distinct().ShouldHaveSingleItem();
        (starts[1].X - starts[0].X).ShouldBe(starts[2].X - starts[1].X);
        (starts[1].X - starts[0].X).ShouldBeGreaterThan(0);
    }

    [Test]
    public void ADecisionKeepsOnePointOnEachSide()
    {
        var svg = Render("flowchart LR\n  A{Ok?} --> B\n  A --> C\n  A --> D\n", SvgRenderOptions.Default);

        var starts = new[] { "B", "C", "D" }.Select(target => Points(PathOf(svg, "A", target))[0]).ToList();

        // One point straight ahead and one below, shared by the two links that turn once.
        starts.Distinct().Count().ShouldBe(2);
    }

    [Test]
    public void EveryArrowheadIsApproachedByAStraightSegmentOfAtLeastTwelvePixels()
    {
        foreach (var source in new[] { Crossing, DiagramMother.Checkout, DiagramMother.NestedDecision, DiagramMother.OrderPipeline("LR") })
        {
            var svg = Render(source, SvgRenderOptions.Default.WithConnectorStyle(ConnectorStyle.Rounded));

            foreach (var path in svg.Descendants(Svg + "path").Where(path => path.Attribute("marker-end") is not null))
            {
                var data = (string)path.Attribute("d")!;
                var last = data.LastIndexOfAny(['L', 'Q', 'A', 'C']);
                data[last].ShouldBe('L', data);
                var points = Points(data);
                var (end, before) = (points[^1], points[^2]);
                (Math.Abs(end.X - before.X) + Math.Abs(end.Y - before.Y)).ShouldBeGreaterThanOrEqualTo(12, data);
            }
        }
    }
}
