// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>Mermaid architecture diagrams and the icon catalogue that draws their services.</summary>
public class ArchitectureTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static IEnumerable<JsonElement> Fixtures()
    {
        using var reference = JsonDocument.Parse(DiagramCorpus.File(FixtureCatalog.DiagramsMbt.Architecture, "/reference.json"));
        return reference.RootElement.GetProperty("fixtures").EnumerateArray().Select(fixture => fixture.Clone()).ToList();
    }

    private static ArchitectureDiagram Read(string source)
    {
        Mermaid.TryParse(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return DiagramMother.Of<ArchitectureDiagram>(document);
    }

    [Test]
    public void EveryReferenceFixtureIsAcceptedOrRefusedAsMermaidDoesWithItsServicesAndEdges()
    {
        var mismatches = new List<string>();

        foreach (var fixture in Fixtures())
        {
            var normalized = fixture.GetProperty("normalized");
            var accepted = normalized.GetProperty("accepted").GetBoolean();
            var read = Mermaid.TryParse(fixture.GetProperty("source").GetString(), out var document, out var diagnostics);

            if (read != accepted)
            {
                mismatches.Add($"{fixture.GetProperty("name")}: accepted {read} {string.Join(", ", diagnostics)}");
                continue;
            }

            if (!accepted)
            {
                continue;
            }

            var model = DiagramMother.Of<ArchitectureDiagram>(document);
            var edges = normalized.GetProperty("edges").EnumerateArray().Select(edge =>
                $"{edge.GetProperty("source").GetString()}{edge.GetProperty("sourceSide").GetString()}{edge.GetProperty("sourceGroup").GetBoolean()}{edge.GetProperty("sourceHead").GetBoolean()}>" +
                $"{edge.GetProperty("target").GetString()}{edge.GetProperty("targetSide").GetString()}{edge.GetProperty("targetGroup").GetBoolean()}{edge.GetProperty("targetHead").GetBoolean()}");
            var ours = model.Edges.Select(edge =>
                $"{edge.Source}{edge.SourceSide}{edge.SourceGroup}{edge.SourceHead}>{edge.Target}{edge.TargetSide}{edge.TargetGroup}{edge.TargetHead}");
            var services = normalized.GetProperty("services").EnumerateArray().Select(service => $"{service.GetProperty("id").GetString()}|{service.GetProperty("label").GetString()}");

            if (!ours.SequenceEqual(edges) || !model.Services.Select(service => $"{service.Id}|{service.Label}").SequenceEqual(services))
            {
                mismatches.Add($"{fixture.GetProperty("name")}: {model}");
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void PrintingWhatWasPrintedGivesTheSameTextForEveryAcceptedFixture()
    {
        var failures = new List<string>();

        foreach (var fixture in Fixtures().Where(fixture => fixture.GetProperty("normalized").GetProperty("accepted").GetBoolean()))
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
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public void AGroupBoundaryEdgeKeepsItsServiceAsTheEndpoint()
    {
        var edge = Read("architecture-beta\n  group g\n  group h\n  service db in g\n  service api in h\n  db{group}:R --> L:api\n").Edges.ShouldHaveSingleItem();

        (edge.Source.Value, edge.SourceGroup, edge.TargetGroup, edge.TargetHead).ShouldBe(("db", true, false, true));
    }

    [Test]
    public void AGroupUsedAsAnEndpointIsReported()
    {
        Mermaid.TryParse("architecture-beta\n  group g\n  service a\n  a:R -- L:g\n", out _, out var diagnostics).ShouldBeFalse();

        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("mermaid/architecture-group-endpoint");
    }

    [Test]
    public void AnEdgeLeavesAndEntersByItsPorts()
    {
        var svg = FlowchartGeometry.Render("architecture-beta\n  service a(server)[A]\n  service b(database)[B]\n  a:R -- L:b\n  service c(disk)[C]\n  a:B -- T:c\n");
        var a = FlowchartGeometry.Node(svg, "a");
        var b = FlowchartGeometry.Node(svg, "b");
        var c = FlowchartGeometry.Node(svg, "c");

        var across = FlowchartGeometry.Route(svg, "a", "b");
        across[0].X.ShouldBe(a.Right, 1);
        across[^1].X.ShouldBe(b.Left, 1);
        var down = FlowchartGeometry.Route(svg, "a", "c");
        down[0].Y.ShouldBe(a.Bottom, 1);
        down[^1].Y.ShouldBe(c.Top, 1);
    }

    [Test]
    public void ServicesDrawTheirIconsFromScopedAccessibleSymbols()
    {
        var first = XElement.Parse(Mermaid.RenderSvg("architecture-beta\n  service api(lambda)[Orders]\n  service web(browser)[Web]\n  web:R --> L:api\n").Output!);
        var second = XElement.Parse(Mermaid.RenderSvg("architecture-beta\n  service queue(sqs)[Events]\n").Output!);

        var symbols = first.Descendants(Svg + "symbol").ToList();
        symbols.Count.ShouldBe(2);
        symbols.ShouldAllBe(symbol => symbol.Element(Svg + "title") != null);
        first.Descendants(Svg + "use").Select(use => (string?)use.Attribute("aria-label")).ShouldBe(["lambda", "browser"], ignoreOrder: true);

        IEnumerable<string> Ids(XElement svg) => svg.DescendantsAndSelf().Select(element => (string?)element.Attribute("id")).OfType<string>();
        Ids(first).Intersect(Ids(second)).ShouldBeEmpty();
    }

    [Test]
    public void AnUnknownIconDrawsAMarkedFallbackWithAWarning()
    {
        Mermaid.TryParse("architecture-beta\n  service a(nonesuch)[A]\n", out var document, out _).ShouldBeTrue();

        document!.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("mermaid/unknown-icon");
        Mermaid.RenderSvg("architecture-beta\n  service a(nonesuch)[A]\n").Output!.ShouldContain("stroke-dasharray=\"3 2\"");
    }

    [Test]
    public void EveryCatalogueKeyResolvesAndTheCatalogueMatchesItsProvenance()
    {
        foreach (var key in new[] { "lambda", "azuresql", "bigquery", "actor", "kubernetes", "externalsystem", "cloud", "disk", "internet", "LAMBDA" })
        {
            DiagramIcons.Contains(key).ShouldBeTrue(key);
        }

        DiagramIcons.Keys.Length.ShouldBe(526);

        using var stream = typeof(Mermaid).Assembly.GetManifestResourceStream("Morphir.Diagrams.Icons.json.gz")!;
        Convert.ToHexStringLower(SHA256.HashData(stream)).ShouldBe("eb0097ee152cbb25e8c231c0e991d8d80e8c0aa5c4553f3a7cdf234f1584d895");
    }
}
