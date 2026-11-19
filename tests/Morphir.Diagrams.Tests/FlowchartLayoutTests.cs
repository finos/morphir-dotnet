// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Xml.Linq;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public class FlowchartLayoutTests
{
    [Test]
    [Arguments("LR")]
    [Arguments("RL")]
    [Arguments("TD")]
    [Arguments("BT")]
    public void ASubgraphBoxHoldsItsOwnNodesAndNoOthers(string direction)
    {
        var svg = FlowchartGeometry.Render(DiagramMother.OrderPipeline(direction));
        var checkout = FlowchartGeometry.Group(svg, "Checkout");

        foreach (var member in new[] { "Gateway", "Orders", "Rules", "Store", "Reject" })
            checkout.Contains(FlowchartGeometry.Node(svg, member)).ShouldBeTrue(member);
        foreach (var outsider in new[] { "Browser", "Bus", "Audit" })
            checkout.Intersects(FlowchartGeometry.Node(svg, outsider)).ShouldBeFalse(outsider);
    }

    [Test]
    [Arguments("LR")]
    [Arguments("RL")]
    [Arguments("TD")]
    [Arguments("BT")]
    public void LinksOutOfADecisionLeaveFromDifferentCornersAndKeepTheirLabelsApart(string direction)
    {
        var svg = FlowchartGeometry.Render(DiagramMother.OrderPipeline(direction));
        var corners = FlowchartGeometry.Corners(svg, "Rules");
        var yes = FlowchartGeometry.Route(svg, "Rules", "Store");
        var no = FlowchartGeometry.Route(svg, "Rules", "Reject");

        corners.ShouldContain(yes[0]);
        corners.ShouldContain(no[0]);
        yes[0].ShouldNotBe(no[0]);
        FlowchartGeometry.LabelChip(svg, "edge=Rules/Store/0").Intersects(FlowchartGeometry.LabelChip(svg, "edge=Rules/Reject/0")).ShouldBeFalse();
    }

    [Test]
    [Arguments("LR")]
    [Arguments("RL")]
    [Arguments("TD")]
    [Arguments("BT")]
    public void LinksOutOfOneNodeToDifferentTargetsShareNoStretchOfLine(string direction)
    {
        var svg = FlowchartGeometry.Render(DiagramMother.OrderPipeline(direction));

        FlowchartGeometry.Overlap(FlowchartGeometry.Route(svg, "Orders", "Rules"), FlowchartGeometry.Route(svg, "Orders", "Bus")).ShouldBeFalse();
        FlowchartGeometry.Overlap(FlowchartGeometry.Route(svg, "Rules", "Store"), FlowchartGeometry.Route(svg, "Rules", "Reject")).ShouldBeFalse();
    }

    [Test]
    public void ANodeAfterASubgraphLinesUpWithTheNodeThatLeadsToIt()
    {
        var svg = FlowchartGeometry.Render(DiagramMother.NestedDecision);

        foreach (var (source, target) in new[] { ("E", "F"), ("D", "G") })
            FlowchartGeometry.Route(svg, source, target).Select(point => point.Y).Distinct().ShouldHaveSingleItem($"{source} to {target}");
    }
}

/// <summary>Reads the boxes, corners and routes of a rendered flowchart.</summary>
internal static class FlowchartGeometry
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public static XElement Render(string source) =>
        XElement.Parse(Mermaid.RenderSvg(source).Output.ShouldNotBeNull());

    public static Box Node(XElement svg, string id) => Bounds(Outline(svg, id));

    public static Box Group(XElement svg, string id) =>
        Bounds(svg.Descendants(Svg + "g").Single(g => (string?)g.Attribute("data-group") == id).Element(Svg + "rect").ShouldNotBeNull());

    /// <summary>The corners of a node drawn as a polygon.</summary>
    public static IReadOnlyList<(double X, double Y)> Corners(XElement svg, string id) =>
        Points(((string?)Outline(svg, id).Attribute("points")).ShouldNotBeNull());

    /// <summary>The points of the connector from the source to the target, from the start.</summary>
    public static IReadOnlyList<(double X, double Y)> Route(XElement svg, string source, string target) =>
        Points(((string?)svg.Descendants(Svg + "path")
            .Single(path => (string?)path.Attribute("data-source") == source && (string?)path.Attribute("data-target") == target)
            .Attribute("d")).ShouldNotBeNull().Replace('M', ' ').Replace('L', ' '));

    public static Box LabelChip(XElement svg, string reference) =>
        Bounds(svg.Descendants(Svg + "g").Single(g => (string?)g.Attribute("data-ref") == reference)
            .Elements(Svg + "rect").Single(rect => (string?)rect.Attribute("class") == "mask"));

    /// <summary>Whether two routes run along a common stretch of line, in the same direction or not.</summary>
    public static bool Overlap(IReadOnlyList<(double X, double Y)> first, IReadOnlyList<(double X, double Y)> second) =>
        Segments(first).Any(a => Segments(second).Any(b => Shared(a, b)));

    private static XElement Outline(XElement svg, string id) =>
        svg.Descendants(Svg + "g").Single(g => (string?)g.Attribute("data-node") == id)
            .Elements().First(e => (string?)e.Attribute("class") == "outline");

    private static IEnumerable<((double X, double Y) From, (double X, double Y) To)> Segments(IReadOnlyList<(double X, double Y)> points) =>
        points.Zip(points.Skip(1)).Where(pair => pair.First != pair.Second);

    private static bool Shared(((double X, double Y) From, (double X, double Y) To) a, ((double X, double Y) From, (double X, double Y) To) b)
    {
        static bool Apart(double a1, double a2, double b1, double b2) =>
            Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) - Math.Max(Math.Min(a1, a2), Math.Min(b1, b2)) <= 0;

        var aHorizontal = a.From.Y == a.To.Y;
        var bHorizontal = b.From.Y == b.To.Y;
        if (aHorizontal && bHorizontal)
            return a.From.Y == b.From.Y && !Apart(a.From.X, a.To.X, b.From.X, b.To.X);
        if (a.From.X == a.To.X && b.From.X == b.To.X)
            return a.From.X == b.From.X && !Apart(a.From.Y, a.To.Y, b.From.Y, b.To.Y);
        return false;
    }

    private static IReadOnlyList<(double X, double Y)> Points(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(','))
            .Select(pair => (Number(pair[0]), Number(pair[1])))
            .ToArray();

    private static Box Bounds(XElement shape)
    {
        double Attribute(string name) => Number(((string?)shape.Attribute(name)).ShouldNotBeNull());

        switch (shape.Name.LocalName)
        {
            case "rect":
                return new Box(Attribute("x"), Attribute("y"), Attribute("x") + Attribute("width"), Attribute("y") + Attribute("height"));
            case "circle":
                var r = Attribute("r");
                return new Box(Attribute("cx") - r, Attribute("cy") - r, Attribute("cx") + r, Attribute("cy") + r);
            default:
                var points = Points(((string?)shape.Attribute("points")).ShouldNotBeNull());
                return new Box(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        }
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    internal readonly record struct Box(double Left, double Top, double Right, double Bottom)
    {
        public bool Contains(Box other) =>
            Left <= other.Left && Top <= other.Top && other.Right <= Right && other.Bottom <= Bottom;

        public bool Intersects(Box other) =>
            Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    }
}
