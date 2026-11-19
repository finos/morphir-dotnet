// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using Morphir.Diagrams;
using Morphir.Knowledge;
using Morphir.Knowledge.Rdf;

var rendered = Mermaid.RenderAnimated("""
    sequenceDiagram
      autonumber
      opt Valid order
        Customer->>+Orders: Submit
        Orders->>Orders: Validate
        Orders-->>-Customer: Accepted
      end
    """);
if (rendered.Output is not { } html)
    return Fail(rendered);

Console.WriteLine(html);

// A sidecar dataset: a comment on the first message and a fact about a participant.
const string sidecar = """
    <urn:review:1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://www.w3.org/ns/oa#Annotation> .
    <urn:review:1> <http://www.w3.org/ns/oa#bodyValue> "Is the order idempotent?" .
    <urn:review:1> <http://www.w3.org/ns/oa#hasTarget> _:target .
    _:target <http://www.w3.org/ns/oa#hasSource> <https://example.org/diagrams/orders> .
    _:target <http://www.w3.org/ns/oa#hasSelector> _:selector .
    _:selector <http://www.w3.org/1999/02/22-rdf-syntax-ns#value> "message=Customer/Orders/0" .
    <https://example.org/diagrams/orders#participant=Orders> <https://example.org/vocab/owner> "fulfilment" .

    """;
if (!NQuads.TryRead(Encoding.UTF8.GetBytes(sidecar), out var dataset, out var readError))
{
    Console.Error.WriteLine(readError);
    return 1;
}

var options = SvgRenderOptions.Default
    .WithDocument(DocumentId.Create(Iri.Create("https://example.org/diagrams/orders")))
    .WithMetadata(dataset, MetadataEmbedding.RdfXml, NamedGraphHandling.Reject);
var svg = Mermaid.RenderSvg("sequenceDiagram\n  Customer->>Orders: Submit\n  Orders-->>Customer: Accepted", options);
if (svg.Output is not { } markup || svg.Diagnostics.Length > 0)
    return Fail(svg);

if (!DiagramMetadata.TryRead(Encoding.UTF8.GetBytes(markup), out var extracted, out var metadataError))
{
    Console.Error.WriteLine(metadataError);
    return 1;
}

// A BPMN view, which reads its XML through Morphir.Bpmn with DTDs refused.
var bpmn = DiagramSource.RenderSvg("""
    <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="D" targetNamespace="urn:smoke">
      <bpmn:process id="P">
        <bpmn:startEvent id="Start"/><bpmn:userTask id="Review" name="Review"/><bpmn:endEvent id="End"/>
        <bpmn:sequenceFlow id="F1" sourceRef="Start" targetRef="Review"/><bpmn:sequenceFlow id="F2" sourceRef="Review" targetRef="End"/>
      </bpmn:process>
    </bpmn:definitions>
    """);
if (bpmn.Output is not { } bpmnSvg || !bpmnSvg.Contains("data-ref=\"node=Review\"", StringComparison.Ordinal))
    return Fail(bpmn);
Console.WriteLine("bpmn render: ok");

var expected = Canonical(dataset);
var actual = Canonical(extracted);
Console.WriteLine(actual);
Console.WriteLine(expected == actual ? "metadata round trip: ok" : "metadata round trip: FAIL");
return expected == actual ? 0 : 1;

static string Canonical(RdfDataset dataset)
{
    if (!RdfCanonicalization.TryCanonicalize(dataset, CanonicalizationOptions.Default, out var canonical, out var error))
        throw new InvalidOperationException(error.ToString());
    if (!NQuads.TryWrite(canonical, out var output, out var writeError))
        throw new InvalidOperationException(writeError.ToString());
    return Encoding.UTF8.GetString(output.Bytes);
}

static int Fail(RenderedDiagram rendered)
{
    foreach (var diagnostic in rendered.Diagnostics)
        Console.Error.WriteLine(diagnostic);
    return 1;
}
