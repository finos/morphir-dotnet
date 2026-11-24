// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>
/// The layout of every corpus diagram paints what its SVG paints: the same primitives in the same order, with the
/// same geometry, colours and stroke widths, and each primitive in the drawable of the element whose group holds it.
/// </summary>
public partial class LayoutAgreementTests
{
    /// <summary>One painted primitive, as text that both sides format the same way, and the reference it belongs to.</summary>
    public sealed record Painted(string Description, string? Reference);

    private static readonly string[] Primitives = ["rect", "circle", "ellipse", "polygon", "polyline", "line", "path", "text", "use"];
    private static readonly string[] Skipped = ["title", "defs", "style", "metadata", "desc", "marker", "symbol"];

    private static string N(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string N(Distance distance) => N((decimal)distance.QuarterEmu / 38_100m);

    private static decimal Number(string text) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Normalised(string data) => Normalised(PathData.Create(data));

    private static string Normalised(PathData path) => string.Join(" ", path.Commands.Select(command => command.Match(
        move: point => $"M{N(point.X)},{N(point.Y)}",
        line: point => $"L{N(point.X)},{N(point.Y)}",
        cubic: (a, b, point) => $"C{N(a.X)},{N(a.Y)} {N(b.X)},{N(b.Y)} {N(point.X)},{N(point.Y)}",
        quadratic: (a, point) => $"Q{N(a.X)},{N(a.Y)} {N(point.X)},{N(point.Y)}",
        arc: (rx, ry, rotation, large, sweep, point) => $"A{N(rx)},{N(ry)} {rotation.ToString(CultureInfo.InvariantCulture)} {(large ? 1 : 0)},{(sweep ? 1 : 0)} {N(point.X)},{N(point.Y)}",
        close: () => "Z")));

    /// <summary>A marker for a colour written directly in the SVG rather than through the theme's variables.</summary>
    private static readonly Color Authored = Color.FromRgba(1, 2, 3, 0.5m);

    private static string Colour(Color? color) => color is null ? "none" : color.Equals(Authored) ? "authored" : color.ToCss();

    private static Color Of(LayoutColor color) =>
        color.Source.Match(family: _ => color.Color, baseRole: _ => color.Color, authored: () => Authored);

    private static string Paint(LayoutColor? fill, LayoutColor? stroke, Distance width) =>
        $"fill {Colour(fill is null ? null : Of(fill))} stroke {Colour(stroke is null ? null : Of(stroke))} {(stroke is null ? "0" : N(width))}";

    /// <summary>The primitives that the SVG paints, in document order, with their innermost reference.</summary>
    public static List<Painted> FromSvg(string svg)
    {
        var root = XDocument.Parse(svg, LoadOptions.PreserveWhitespace).Root!;
        var variables = Variables().Matches(root.Descendants().First(element => element.Name.LocalName == "style").Value)
            .GroupBy(match => match.Groups[1].Value).ToDictionary(group => group.Key, group => group.First().Groups[2].Value);
        var painted = new List<Painted>();

        // Colours that a writer or an author wrote directly are compared by source alone; the CSS reader has its own tests.
        Color? Resolve(string? value, decimal opacity)
        {
            if (value is null or "none") return null;
            if (!value.StartsWith("var(--", StringComparison.Ordinal)) return Authored;
            Color.TryParse(variables[value[6..^1]], out var color, out _).ShouldBeTrue(value);
            return Color.FromRgba(color.R, color.G, color.B, Math.Round(color.Opacity * opacity, 6));
        }

        decimal Opacity(XElement element, string name) =>
            ((string?)element.Attribute(name) is { } own ? Number(own) : 1m) * ((string?)element.Attribute("opacity") is { } all ? Number(all) : 1m);

        string PaintOf(XElement element, string defaultFill)
        {
            var stroke = Resolve((string?)element.Attribute("stroke"), Opacity(element, "stroke-opacity"));
            var width = stroke is null ? "0" : N((string?)element.Attribute("stroke-width") is { } text ? Number(text) : 1m);
            return $"fill {Colour(Resolve((string?)element.Attribute("fill") ?? defaultFill, Opacity(element, "fill-opacity")))} stroke {Colour(stroke)} {width}";
        }

        decimal A(XElement element, string name) => (string?)element.Attribute(name) is { } text ? Number(text) : 0m;

        void Walk(XElement element, string? reference)
        {
            var name = element.Name.LocalName;
            if (Skipped.Contains(name)) return;
            reference = (string?)element.Attribute("data-ref") ?? reference;

            if (!Primitives.Contains(name))
            {
                foreach (var child in element.Elements()) Walk(child, reference);
                return;
            }

            var description = name switch
            {
                "rect" => $"rect {N(A(element, "x"))},{N(A(element, "y"))} {N(A(element, "width"))}x{N(A(element, "height"))} {PaintOf(element, "#000000")}",
                "circle" => $"ellipse {N(A(element, "cx"))},{N(A(element, "cy"))} {N(A(element, "r"))},{N(A(element, "r"))} {PaintOf(element, "#000000")}",
                "ellipse" => $"ellipse {N(A(element, "cx"))},{N(A(element, "cy"))} {N(A(element, "rx"))},{N(A(element, "ry"))} {PaintOf(element, "#000000")}",
                "polygon" => $"path {PolygonPath((string)element.Attribute("points")!, close: true)} {PaintOf(element, "#000000")}",
                "polyline" => $"path {PolygonPath((string)element.Attribute("points")!, close: false)} {PaintOf(element, "#000000")}",
                "line" => $"path M{N(A(element, "x1"))},{N(A(element, "y1"))} L{N(A(element, "x2"))},{N(A(element, "y2"))} {PaintOf(element, "#000000")}",
                "path" => $"path {Normalised((string)element.Attribute("d")!)} {PaintOf(element, "#000000")}",
                "text" => TextOf(element),
                _ => $"image {N(A(element, "x"))},{N(A(element, "y"))} {N(A(element, "width"))}x{N(A(element, "height"))}",
            };

            string TextOf(XElement text)
            {
                var spans = text.Elements().Where(child => child.Name.LocalName == "tspan").ToArray();
                var lines = spans.Length == 0 ? [text.Value] : spans.Select(span => span.Value).ToArray();
                var dy = spans.Length == 0 ? 0m : A(spans[0], "dy");
                return $"text {N(A(text, "x"))},{N(A(text, "y") + dy)} {(string?)text.Attribute("text-anchor") ?? "start"} {N(A(text, "font-size"))} " +
                    $"{(string?)text.Attribute("font-weight") ?? "400"} {Colour(Resolve((string?)text.Attribute("fill") ?? "#000000", Opacity(text, "fill-opacity")))} [{string.Join("|", lines)}]";
            }

            painted.Add(new Painted(description, reference));
        }

        // The page background is the layout's background colour, not a drawable.
        var first = true;
        foreach (var child in root.Elements())
        {
            if (first && child.Name.LocalName == "rect" && (string?)child.Attribute("fill") == "var(--bg)") { first = false; continue; }
            if (Primitives.Contains(child.Name.LocalName)) first = false;
            Walk(child, null);
        }

        return painted;
    }

    private static string PolygonPath(string points, bool close)
    {
        var values = points.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
        var parts = Enumerable.Range(0, values.Length / 2).Select(index => $"{(index == 0 ? "M" : "L")}{N(values[2 * index])},{N(values[2 * index + 1])}");
        return string.Join(" ", parts) + (close ? " Z" : "");
    }

    /// <summary>The primitives that the layout's drawables stand for, in paint order, with the nearest reference.</summary>
    public static List<Painted> FromLayout(DiagramLayout layout)
    {
        var entries = new List<(int Order, int Part, Painted Painted)>();

        static string TextLine(LayoutText text) =>
            $"text {N(text.Origin.X)},{N(text.Origin.Y)} {text.Align} {N(text.Size)} {text.Weight} {Colour(Of(text.Ink))} [{string.Join("|", text.Lines)}]";

        void Text(LayoutText text, int order, ref int part, string? reference)
        {
            if (text.Background is { } background)
            {
                var box = text.BackgroundBox;
                entries.Add((order, part++, new Painted($"rect {N(box.X)},{N(box.Y)} {N(box.Width)}x{N(box.Height)} fill {Colour(Of(background))} stroke none 0", reference)));
            }

            entries.Add((order, part++, new Painted(TextLine(text), reference)));
        }

        void Shape(LayoutShape shape, int order, ref int part, string? reference)
        {
            var box = shape.Box;
            var paint = Paint(shape.Paint.Fill, shape.Paint.Stroke, shape.Paint.StrokeWidth);
            if (shape.Backdrop is { } backdrop)
                entries.Add((order, part++, new Painted($"rect {N(box.X)},{N(box.Y)} {N(box.Width)}x{N(box.Height)} fill {Colour(Of(backdrop))} stroke none 0", reference)));

            var cx = box.X + Distance.FromQuarterEmu(box.Width.QuarterEmu / 2);
            var cy = box.Y + Distance.FromQuarterEmu(box.Height.QuarterEmu / 2);
            var rect = $"rect {N(box.X)},{N(box.Y)} {N(box.Width)}x{N(box.Height)} {paint}";
            var outlines = shape.Kind.Match(
                rectangle: () => new[] { rect },
                roundedRectangle: _ => [rect],
                ellipse: () => [$"ellipse {N(cx)},{N(cy)} {N(Distance.FromQuarterEmu(box.Width.QuarterEmu / 2))},{N(Distance.FromQuarterEmu(box.Height.QuarterEmu / 2))} {paint}"],
                diamond: () => [$"path M{N(cx)},{N(box.Y)} L{N(box.Right)},{N(cy)} L{N(cx)},{N(box.Bottom)} L{N(box.X)},{N(cy)} Z {paint}"],
                cylinder: cap => [rect, $"ellipse {N(cx)},{N(box.Y + cap)} {N(Distance.FromQuarterEmu(box.Width.QuarterEmu / 2))},{N(cap)} fill none stroke {Colour(shape.Paint.Stroke is { } capStroke ? Of(capStroke) : null)} {N(shape.Paint.StrokeWidth)}"],
                hexagon: inset => [$"path M{N(box.X)},{N(cy)} L{N(box.X + inset)},{N(box.Y)} L{N(box.Right - inset)},{N(box.Y)} L{N(box.Right)},{N(cy)} L{N(box.Right - inset)},{N(box.Bottom)} L{N(box.X + inset)},{N(box.Bottom)} Z {paint}"],
                path: data => [$"path {Normalised(data)} {paint}"]);

            foreach (var outline in outlines) entries.Add((order, part++, new Painted(outline, reference)));
            if (shape.Text is { } text) Text(text, order, ref part, reference);
        }

        void Visit(Drawable drawable, string? reference)
        {
            reference = drawable.Reference?.ToString() ?? reference;
            var order = drawable.PaintOrder;
            var part = 0;

            drawable.Kind.Match(
                shape: shape => { Shape(shape, order, ref part, reference); return 0; },
                line: line =>
                {
                    var stroke = line.Paint;
                    var path = line.ExactPath is { } exact ? Normalised(exact) : "";
                    entries.Add((order, part++, new Painted($"path {path} {Paint(stroke.Fill, stroke.Stroke, stroke.StrokeWidth)}", reference)));
                    if (line.Label is { } label) Text(label, order, ref part, reference);
                    return 0;
                },
                text: text => { Text(text, order, ref part, reference); return 0; },
                image: image =>
                {
                    var box = image.Box;
                    entries.Add((order, part++, new Painted($"image {N(box.X)},{N(box.Y)} {N(box.Width)}x{N(box.Height)}", reference)));
                    return 0;
                },
                element: children => { foreach (var child in children) Visit(child, reference); return 0; },
                container: frame =>
                {
                    entries.Add((order, part++, new Painted(
                        $"rect {N(frame.Box.X)},{N(frame.Box.Y)} {N(frame.Box.Width)}x{N(frame.Box.Height)} {Paint(frame.Paint.Fill, frame.Paint.Stroke, frame.Paint.StrokeWidth)}", reference)));
                    if (frame.Header is { } header) Text(header, order, ref part, reference);
                    foreach (var child in frame.Children) Visit(child, reference);
                    return 0;
                },
                group: children => { foreach (var child in children) Visit(child, reference); return 0; });
        }

        foreach (var drawable in layout.Drawables) Visit(drawable, null);
        return [.. entries.OrderBy(entry => entry.Order).ThenBy(entry => entry.Part).Select(entry => entry.Painted)];
    }

    private static IEnumerable<(CorpusDiagram Source, DiagramLayout Layout)> Layouts() =>
        SvgBaselineTests.Diagrams.Select(item => (item.Source, DiagramExport.Layout(item.Diagram).Layout))
            .Where(item => item.Layout is not null)
            .Select(item => (item.Source, item.Layout!));

    [Test]
    public void PaintListsMatchAcrossThePinnedCorpora()
    {
        var failures = new List<string>();

        foreach (var (source, layout) in Layouts())
        {
            var svg = FromSvg(layout.Svg.Render()).Select(item => item.Description).ToList();
            var drawn = FromLayout(layout).Select(item => item.Description).ToList();
            var at = Enumerable.Range(0, Math.Max(svg.Count, drawn.Count)).FirstOrDefault(index => index >= svg.Count || index >= drawn.Count || svg[index] != drawn[index], -1);
            if (at >= 0)
                failures.Add($"{source} at {at}:\n  svg    {(at < svg.Count ? svg[at] : "(end)")}\n  layout {(at < drawn.Count ? drawn[at] : "(end)")}");
        }

        failures.ShouldBeEmpty(string.Join("\n", failures.Take(5)) + $"\n{failures.Count} diagrams differ");
    }

    [Test]
    public void ReferencesAgreeWithTheSvg()
    {
        var failures = new List<string>();

        foreach (var (source, layout) in Layouts())
        {
            var svg = FromSvg(layout.Svg.Render());
            var drawn = FromLayout(layout);
            if (svg.Count != drawn.Count) continue;

            for (var index = 0; index < svg.Count; index++)
            {
                // A sequence lifeline, drawn outside its participant's group, belongs to that participant.
                var documented = svg[index].Reference is null && drawn[index].Reference?.StartsWith("participant=", StringComparison.Ordinal) == true;
                if (svg[index].Reference != drawn[index].Reference && !documented)
                {
                    failures.Add($"{source} at {index} {svg[index].Description}: svg {svg[index].Reference ?? "none"}, layout {drawn[index].Reference ?? "none"}");
                    break;
                }
            }
        }

        failures.ShouldBeEmpty(string.Join("\n", failures.Take(5)) + $"\n{failures.Count} diagrams differ");
    }

    [GeneratedRegex("--([a-z0-9-]+): ([^;]+);")]
    private static partial Regex Variables();
}
