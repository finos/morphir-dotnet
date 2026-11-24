// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Morphir.Diagrams.Export;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Canvas.Tests;

/// <summary>
/// Pins the output of each canvas renderer for one diagram of each family. The files behind these digests were opened
/// in the applications before they were recorded (see the package README).
/// </summary>
public class CanvasDigestTests
{
    private static readonly Dictionary<string, Dictionary<string, string>> Expected = new(StringComparer.Ordinal)
    {
        [".drawio"] = new(StringComparer.Ordinal)
        {
            ["flowchart"] = "eded2af1b6c0ae5a1aa180fe076c8137f6ad9b4a91957c2f83748499e666781b",
            ["sequence"] = "4a32f88afff2077db29089e2e945e29c47dd543e2b93e44404edeec6f2d97a94",
            ["state"] = "bada09f75c8d8c1e674365af9cac3a9a501f83c2b7f5368fb8d57cdc32dda462",
            ["swimlane"] = "30a39fb4e1acea1d0bfac8656cf2159a84d17c0d91a7dc3b219684f7806d9fc3",
            ["architecture"] = "4550e3003b6d2b9f752dad9b82656318a51acad83eca223fac11d3bf494183bc",
            ["activity"] = "77714e768b83e6863c8d1a2d601a875c7982b8319247ac07e53d4af3c9a7cc71",
            ["railroad"] = "4a7ad886566c2f5424d6ac753228bb79a1098a51637458ce57ee224a9ce8af51",
            ["data-tree"] = "a364073d4b57e52066872a1f82fba9fe071b86869b298bc4a5475f7bb42dd605",
            ["bpmn"] = "209b49c70d7a2e3890c665fd5db9999930e87627929630d9975ab85296952661",
        },
    };

    public static IEnumerable<Func<DiagramRenderer>> Renderers() =>
        [() => new DrawIoRenderer()];

    [Test]
    [MethodDataSource(nameof(Renderers))]
    public void EachFamilyRendersItsPinnedBytes(DiagramRenderer renderer)
    {
        var actual = DiagramCorpus.Representatives().ToDictionary(
            item => item.Family,
            item => DigestBaseline.Digest(CanvasMother.Render(renderer, CanvasMother.Layout(item.Diagram.Source))),
            StringComparer.Ordinal);

        actual.ShouldBe(Expected[renderer.FileExtension], string.Join("\n", actual.Select(pair => $"[\"{pair.Key}\"] = \"{pair.Value}\",")));
    }
}
