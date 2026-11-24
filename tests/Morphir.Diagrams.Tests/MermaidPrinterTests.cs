// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>Canonical Mermaid text for flowcharts and sequences: print, read, print gives the same text.</summary>
public class MermaidPrinterTests
{
    private static IEnumerable<(string Name, string Source)> Corpus()
    {
        using var reference = JsonDocument.Parse(DiagramCorpus.File(FixtureCatalog.DiagramsMbt.Flowcharts, "/reference.json"));

        foreach (var fixture in reference.RootElement.GetProperty("fixtures").EnumerateArray().Where(fixture => fixture.GetProperty("accepted").GetBoolean()))
        {
            yield return (fixture.GetProperty("name").GetString()!, fixture.GetProperty("source").GetString()!);
        }

        foreach (var file in DiagramCorpus.Files(FixtureCatalog.DiagramsMbt.Sequence).Where(file => file.Key.EndsWith(".mmd", StringComparison.Ordinal)))
        {
            yield return (file.Key, file.Value);
        }
    }

    [Test]
    public void PrintingWhatWasPrintedGivesTheSameTextForEveryCorpusDiagram()
    {
        var failures = new List<string>();
        var count = 0;

        foreach (var (name, source) in Corpus())
        {
            count++;
            Mermaid.TryParse(source, out var document, out _).ShouldBeTrue(name);

            if (!Mermaid.TryPrint(document!.Diagram, out var first, out var diagnostics))
            {
                failures.Add($"{name}: {string.Join(", ", diagnostics)}");
            }
            else if (!Mermaid.TryParse(first, out var reread, out var errors))
            {
                failures.Add($"{name}: printed text does not read: {string.Join(", ", errors)}\n{first}");
            }
            else if (!Mermaid.TryPrint(reread!.Diagram, out var second, out _) || second != first)
            {
                failures.Add($"{name}: second print differs\n{first}\n---\n{second}");
            }
            else if (reread.Diagram.ToString() != document.Diagram.ToString())
            {
                failures.Add($"{name}: {reread.Diagram} after printing, {document.Diagram} before");
            }
        }

        count.ShouldBe(186);
        failures.ShouldBeEmpty();
    }

    [Test]
    public void AFlowchartPrintsItsSubgraphsShapesAndLinks()
    {
        Mermaid.TryParse("flowchart LR\n  subgraph pay [Payments]\n    A([Cart]) -->|pay| B{Ok?}\n  end\n  B -.-> C@{ shape: doc, label: \"Receipt\" }\n", out var document, out _)
            .ShouldBeTrue();

        Mermaid.TryPrint(document!.Diagram, out var text, out _).ShouldBeTrue();

        text.ShouldBe("""
            flowchart LR
              subgraph pay ["Payments"]
                A(["Cart"])
                B{"Ok?"}
              end
              C@{ shape: doc, label: "Receipt" }
              A -->|"pay"| B
              B -.-> C

            """.Replace("\r\n", "\n"));
    }

    [Test]
    public void ASequencePrintsCreationDestructionFramesAndNumbers()
    {
        const string source = """
            sequenceDiagram
              autonumber 10 5
              participant A
              create participant B
              A->>B: new
              loop Every minute
                A-)B: tick
              end
              destroy B
              A-xB: stop
            """;

        Mermaid.TryParse(source, out var document, out _).ShouldBeTrue();
        Mermaid.TryPrint(document!.Diagram, out var text, out _).ShouldBeTrue();

        text.ShouldBe("""
            sequenceDiagram
              participant A
              create participant B
              autonumber 10 5
              A->>B: new
              loop Every minute
                A-)B: tick
              end
              destroy B
              A-xB: stop

            """.Replace("\r\n", "\n"));
    }
}
