// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using Morphir.Diagrams.Layout;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>SVG path data parsed and normalised to absolute move, line, cubic, quadratic, arc and close commands.</summary>
public class PathDataTests
{
    private static PathData Parse(string text)
    {
        PathData.TryParse(text, out var path, out var failure).ShouldBeTrue(failure?.Message);
        return path;
    }

    [Test]
    [Arguments("M10,20 L30,40 Z", "M10,20 L30,40 Z")]
    [Arguments("m10 20 l20 20 z", "M10,20 L30,40 Z")]
    [Arguments("M10,20 H30 V40 h-5 v-5", "M10,20 L30,20 L30,40 L25,40 L25,35")]
    [Arguments("M0,0 C10,0 20,10 20,20 S30,40 40,40", "M0,0 C10,0 20,10 20,20 C20,30 30,40 40,40")]
    [Arguments("M0,0 c10,0 20,10 20,20 s10,20 20,20", "M0,0 C10,0 20,10 20,20 C20,30 30,40 40,40")]
    [Arguments("M0,0 Q10,10 20,0 T40,0", "M0,0 Q10,10 20,0 Q30,-10 40,0")]
    [Arguments("M0,0 q10,10 20,0 t20,0", "M0,0 Q10,10 20,0 Q30,-10 40,0")]
    [Arguments("M0,0 S10,10 20,0", "M0,0 C0,0 10,10 20,0")]
    [Arguments("M0,0 A5,5 0 1 0 10,0", "M0,0 A5,5 0 1,0 10,0")]
    [Arguments("M0,0 a5,5 30 0 1 10,0", "M0,0 A5,5 30 0,1 10,0")]
    [Arguments("M1 2 3 4 5 6", "M1,2 L3,4 L5,6")]
    [Arguments("m1 2 3 4", "M1,2 L4,6")]
    [Arguments("M0,0a3.5,3.5 0 1 1 0,7", "M0,0 A3.5,3.5 0 1,1 0,7")]
    [Arguments("M0 0a1 1 0 1012 0", "M0,0 A1,1 0 1,0 12,0")]
    [Arguments("M.5-.5 l1e1.5", "M0.5,-0.5 L10.5,0")]
    [Arguments("M0,0 L10,10 z m5,5 l1,1", "M0,0 L10,10 Z M5,5 L6,6")]
    [Arguments("M100.02,0.01 L0,0", "M100.02,0.01 L0,0")]
    public void EachCommandIsNormalisedToAbsoluteCommands(string text, string normalised) =>
        Parse(text).ToString().ShouldBe(normalised);

    [Test]
    [Arguments("")]
    [Arguments("L10,10")]
    [Arguments("M10")]
    [Arguments("M0,0 X1,1")]
    [Arguments("M0,0 A5,5 0 2 0 10,0")]
    public void MalformedPathDataIsRefused(string text)
    {
        PathData.TryParse(text, out _, out var failure).ShouldBeFalse();
        failure.Code.ShouldBe("layout/invalid-path");
        Should.Throw<ArgumentException>(() => PathData.Create(text));
    }

    [Test]
    public void ANullPathIsRefused() => PathData.TryParse(null, out _, out _).ShouldBeFalse();

    [Test]
    public void NormalisedCommandsAreAbsoluteAndMatchable()
    {
        var commands = Parse("m1,1 h2 v2 q1,1 2,0 c1,1 2,2 3,3 a1,1 0 0 1 2,0 z").Commands;

        commands.Select(command => command.Match(
            move: _ => "M", line: _ => "L", cubic: (_, _, _) => "C", quadratic: (_, _) => "Q",
            arc: (_, _, _, _, _, _) => "A", close: () => "Z")).ShouldBe(["M", "L", "L", "Q", "C", "A", "Z"]);
        commands[1].Match(move: _ => 0.0, line: end => end.X.ToPixels(), cubic: (_, _, _) => 0, quadratic: (_, _) => 0, arc: (_, _, _, _, _, _) => 0, close: () => 0)
            .ShouldBe(3.0);
    }

    [Test]
    public void EveryBpmnPathOfTheCorpusParsesAndPrintsStably()
    {
        var paths = new List<string>();

        foreach (var source in DiagramCorpus.Sources().Where(source => source.Path.EndsWith(".bpmn", StringComparison.Ordinal)))
        {
            var rendered = DiagramSource.RenderSvg(source.Source);
            if (rendered.Output is null) continue;
            paths.AddRange(XDocument.Parse(rendered.Output).Descendants().Where(element => element.Name.LocalName == "path")
                .Select(element => (string?)element.Attribute("d")).OfType<string>());
        }

        paths.Count.ShouldBeGreaterThan(100);
        paths.ShouldContain(path => path.Contains('a') && path.Contains('c') && path.Contains('s'));

        foreach (var text in paths)
        {
            var normalised = Parse(text);
            Parse(normalised.ToString()).ShouldBe(normalised, text);
            normalised.ToString().ShouldNotContain("a", Case.Sensitive);
            normalised.ToString().ShouldNotContain("H", Case.Sensitive);
        }
    }

    [Test]
    [Arguments("M0,0 A5,5 0 1 0 10,0")]
    [Arguments("M0,0 A40,20 30 0 1 60,10")]
    [Arguments("M10,10 A3.5,3.5 0 1 1 10,17 A3.5,3.5 0 1 1 10,10 Z")]
    [Arguments("M-8,0 A4,4 0 1 0 0,0 A4,4 0 1 0 -8,0 Z")]
    [Arguments("M0,0 A100,100 0 0 1 200,0")]
    public void ArcsBecomeCubicsWithinTheTolerance(string text)
    {
        var path = Parse(text);
        var cubic = path.WithoutArcs(Distance.FromPixels(0.01));

        cubic.ToString().ShouldNotContain("A", Case.Sensitive);
        PathGeometry.MaximumDistance(path, cubic).ShouldBeLessThanOrEqualTo(0.01);
    }

    [Test]
    public void ACircleOfZeroRadiusBecomesALine() =>
        Parse("M0,0 A0,0 0 0 1 10,0").WithoutArcs(Distance.FromPixels(0.01)).ToString().ShouldBe("M0,0 L10,0");

    [Test]
    public void EqualPathsAreEqual()
    {
        Parse("M0,0 L1,1").ShouldBe(Parse("m0,0 l1,1"));
        Parse("M0,0 L1,1").GetHashCode().ShouldBe(Parse("m0,0 l1,1").GetHashCode());
        PathData.Create("M0,0 L1,1").ShouldNotBe(PathData.Create("M0,0 L1,2"));
    }
}

/// <summary>Measures how far cubic curves stray from the ellipse of an arc.</summary>
internal static class PathGeometry
{
    private static (double X, double Y) P(LayoutPoint point) => (point.X.ToPixels(), point.Y.ToPixels());

    /// <summary>
    /// The largest distance, in pixels, from a point of the converted path to the ellipse of the first arc of the
    /// original. Every arc of the original lies on that ellipse.
    /// </summary>
    public static double MaximumDistance(PathData original, PathData converted)
    {
        (double X, double Y) current = (0, 0);
        (double Cx, double Cy, double Rx, double Ry, double Phi)? ellipse = null;

        foreach (var command in original.Commands)
        {
            var from = current;
            command.Match<int>(
                move: end => { current = P(end); return 0; },
                line: end => { current = P(end); return 0; },
                cubic: (_, _, end) => { current = P(end); return 0; },
                quadratic: (_, end) => { current = P(end); return 0; },
                arc: (rx, ry, rotation, large, sweep, end) =>
                {
                    ellipse ??= Centre(from, rx.ToPixels(), ry.ToPixels(), rotation, large, sweep, P(end));
                    current = P(end);
                    return 0;
                },
                close: () => 0);
        }

        var (cx, cy, radiusX, radiusY, phi) = ellipse!.Value;
        var (cos, sin) = (Math.Cos(phi), Math.Sin(phi));
        double Deviation((double X, double Y) point)
        {
            var (dx, dy) = (point.X - cx, point.Y - cy);
            var (x, y) = (cos * dx + sin * dy, -sin * dx + cos * dy);
            return Math.Max(radiusX, radiusY) * Math.Abs(Math.Sqrt(x * x / (radiusX * radiusX) + y * y / (radiusY * radiusY)) - 1);
        }

        var worst = 0.0;
        current = (0, 0);

        foreach (var command in converted.Commands)
        {
            var from = current;
            command.Match<int>(
                move: end => { current = P(end); worst = Math.Max(worst, Deviation(current)); return 0; },
                line: end => { current = P(end); return 0; },
                cubic: (c1, c2, end) =>
                {
                    var (a, b, to) = (P(c1), P(c2), P(end));
                    for (var i = 0; i <= 200; i++)
                    {
                        var t = i / 200.0; var u = 1 - t;
                        worst = Math.Max(worst, Deviation((u * u * u * from.X + 3 * u * u * t * a.X + 3 * u * t * t * b.X + t * t * t * to.X,
                            u * u * u * from.Y + 3 * u * u * t * a.Y + 3 * u * t * t * b.Y + t * t * t * to.Y)));
                    }
                    current = to;
                    return 0;
                },
                quadratic: (_, end) => { current = P(end); return 0; },
                arc: (_, _, _, _, _, end) => throw new InvalidOperationException("The converted path still has an arc."),
                close: () => 0);
        }

        return worst;
    }

    /// <summary>The centre, radii and rotation of an SVG arc (SVG 2, appendix B.2.4), with radii scaled up to fit.</summary>
    private static (double, double, double, double, double) Centre((double X, double Y) from, double rx, double ry, double rotation, bool large, bool sweep, (double X, double Y) to)
    {
        var phi = rotation * Math.PI / 180;
        var (cos, sin) = (Math.Cos(phi), Math.Sin(phi));
        var dx = (from.X - to.X) / 2; var dy = (from.Y - to.Y) / 2;
        var x1 = cos * dx + sin * dy; var y1 = -sin * dx + cos * dy;
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1) { rx *= Math.Sqrt(lambda); ry *= Math.Sqrt(lambda); }
        var numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        var factor = Math.Sqrt(Math.Max(0, numerator / (rx * rx * y1 * y1 + ry * ry * x1 * x1))) * (large == sweep ? -1 : 1);
        var cx1 = factor * rx * y1 / ry; var cy1 = -factor * ry * x1 / rx;
        return (cos * cx1 - sin * cy1 + (from.X + to.X) / 2, sin * cx1 + cos * cy1 + (from.Y + to.Y) / 2, rx, ry, phi);
    }
}
