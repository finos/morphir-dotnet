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
        [".excalidraw"] = new(StringComparer.Ordinal)
        {
            ["flowchart"] = "60822fed935c5f89d6ffbe0345a126fd0af61b2a0d7144c7dc3d598c5557079a",
            ["sequence"] = "f877d83b70c3656013a100ecd49283587d5a871f80b958ca7ddb50a96bdf609d",
            ["state"] = "f34588f964d50ca1444c0e8cb90fcec126a601715e319375361a67182438b93e",
            ["swimlane"] = "d8b93794868991250073baf872e1ee714301eeb1d04522376d801753339b9daa",
            ["architecture"] = "6c22bba7937d5ce0788ef407b1d31cbc374c2833c8156b920296660e94814811",
            ["activity"] = "f5d9f8a0c4ffd1ed1599de0a26c2652e850e60a484cda62cd0f2380ac5133302",
            ["railroad"] = "0072eff697e513d2591731b83cf6060438cc1b37b78199087ea48e89460e9dc3",
            ["data-tree"] = "bc004b640cc0461281c5d95671d5d7c4173a7892d6e71cb770940bac06a74c3e",
            ["bpmn"] = "78d5bb71b1a00e40200fc4359d5cc906b853157ecaa99994e5fe3acd9263aeb3",
        },
        [".excalidraw.md"] = new(StringComparer.Ordinal)
        {
            ["flowchart"] = "333b9b0e2ea9bffc3e18990fd785d7d6902d4d12e554de637a7dcb99efc06002",
            ["sequence"] = "55a18921d6cb6549f7734b5f855152b4a3bba001e9d8fdaf42d57a5e6f0f6870",
            ["state"] = "1d187336ee22815288547faa61d3af2b00ea7212e1fd8c1d3f8f5264fb9ecd2a",
            ["swimlane"] = "b2a4665a6c5f6d3cd6099e24b7a815e79fc6f24ceec4d82bfdfdf0bdbcb71408",
            ["architecture"] = "3e103181cf8a9761c2228228550ce165f449dd28dd6d249fb8f22bd67ad0dfa5",
            ["activity"] = "2159fc5d63ab89ed9cbc80af90283eab6bc339cf0cb2976f10ff54564ab70a6e",
            ["railroad"] = "613e9081655599ea7bc2ba4a4f0141d20d136ea9b483a11977c9b0b93853dbe6",
            ["data-tree"] = "35f1f59971f6bc579f49bbc2cceab8b8bde9dc7b8e40b138eb2e2800b07046fb",
            ["bpmn"] = "a0a75a0f2ebb8a830b24a478f834b357c1793ede5188e4fb678227b192da6d60",
        },
    };

    public static IEnumerable<Func<DiagramRenderer>> Renderers() =>
        [() => new DrawIoRenderer(), () => new ExcalidrawRenderer(), () => new ObsidianExcalidrawRenderer()];

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
