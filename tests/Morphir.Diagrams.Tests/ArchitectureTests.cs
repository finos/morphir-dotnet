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
        using var reference = JsonDocument.Parse(PinnedCorpus.File(FixtureCatalog.DiagramsMbt.Architecture, "/reference.json"));
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
}
