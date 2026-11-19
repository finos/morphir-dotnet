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
}
