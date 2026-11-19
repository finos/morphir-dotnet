// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>One entry point for Mermaid, PlantUML and BPMN source: detection, the PlantUML envelope and limits.</summary>
public class DiagramSourceTests
{
    [Test]
    [Arguments("flowchart LR\n  A --> B\n", "mermaid")]
    [Arguments("﻿  stateDiagram-v2\n", "mermaid")]
    [Arguments("@startuml\nstart\n@enduml\n", "plantuml")]
    [Arguments("' a comment\n@startjson\n{}\n@endjson\n", "plantuml")]
    [Arguments("<?xml version=\"1.0\"?>\n<definitions/>", "bpmn")]
    public void TheLanguageComesFromTheFirstMeaningfulText(string source, string language) =>
        DiagramSource.Detect(source).WireValue.ShouldBe(language);

    [Test]
    public void AKeywordInAMermaidCommentDoesNotChooseTheFamily()
    {
        DiagramSource.TryParse("flowchart TD\n  %% sequenceDiagram\n  A --> B\n", out var document, out _).ShouldBeTrue();

        DiagramMother.FlowchartOf(document).Nodes.Length.ShouldBe(2);
    }

    [Test]
    [Arguments("classDiagram\n  class Order\n")]
    [Arguments("erDiagram\n  ORDER ||--o{ LINE : contains\n")]
    public void AKnownMermaidFamilyThatIsNotSupportedIsReportedAsSuch(string source)
    {
        DiagramSource.TryParse(source, out var document, out var diagnostics).ShouldBeFalse();

        document.ShouldBeNull();
        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("mermaid/unsupported-kind");
    }

    [Test]
    [Arguments("@startuml\nclass Order\nclass Line\n@enduml\n", "plantuml/unsupported-kind")]
    [Arguments("@startmindmap\n* root\n@endmindmap\n", "plantuml/unsupported-kind")]
    [Arguments("@startuml\nstart\n:work;\n", "plantuml/missing-end")]
    [Arguments("@startjson\n{}\n@endyaml\n", "plantuml/wrong-end")]
    [Arguments("@startjson\n{}\n@endjson\n@startjson\n{}\n@endjson\n", "plantuml/multiple-blocks")]
    [Arguments("@startjson\n{}\n@endjson\nmore\n", "plantuml/trailing-content")]
    [Arguments("@startuml\n!include https://example.org/x.puml\nstart\n@enduml\n", "plantuml/preprocessor-refused")]
    [Arguments("@startuml\nstate A\n:act;\n@enduml\n", "plantuml/ambiguous-kind")]
    public void TheEnvelopeOfAPlantUmlBlockIsChecked(string source, string code)
    {
        PlantUml.TryParse(source, out var document, out var diagnostics).ShouldBeFalse();

        document.ShouldBeNull();
        diagnostics.Select(diagnostic => diagnostic.Code).ShouldContain(code);
    }

    [Test]
    public void AnIncludeIsRefusedWithTheLineThatRequestedIt()
    {
        PlantUml.TryParse("@startuml\nstart\n!include https://example.org/x.puml\n@enduml\n", out _, out var diagnostics).ShouldBeFalse();

        var refusal = diagnostics.ShouldHaveSingleItem();
        refusal.Span.ShouldNotBeNull().Start.Line.ShouldBe(3);
    }

    [Test]
    public void DeepNestingGivesALimitDiagnosticAndNoStackOverflow()
    {
        var source = new StringBuilder("flowchart TD\n");

        for (var level = 0; level < 10_000; level++)
        {
            source.Append("subgraph g").Append(level).Append('\n');
        }

        source.Append("A --> B\n");

        DiagramSource.TryParse(source.ToString(), out var document, out var diagnostics).ShouldBeFalse();

        document.ShouldBeNull();
        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("mermaid/depth-limit");
    }

    [Test]
    public void SourceOverTheLimitGivesALimitDiagnostic()
    {
        var source = "flowchart TD\n" + new string(' ', 300_000);

        DiagramSource.TryParse(source, out _, out var diagnostics).ShouldBeFalse();

        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("mermaid/source-limit");
    }
}
