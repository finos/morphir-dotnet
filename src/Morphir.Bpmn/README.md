# Morphir.Bpmn

Reads, validates, traverses and writes BPMN 2.0.2 XML documents. It does not depend on the
diagram packages; `Morphir.Diagrams` uses it to draw BPMN views.

```csharp
using System;
using Morphir.Bpmn;

const string source = """
    <?xml version="1.0" encoding="UTF-8"?>
    <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Defs" targetNamespace="urn:orders">
      <bpmn:process id="Orders">
        <bpmn:startEvent id="Start"/>
        <bpmn:task id="Check" name="Check order"/>
        <bpmn:endEvent id="End"/>
        <bpmn:sequenceFlow id="F1" sourceRef="Start" targetRef="Check"/>
        <bpmn:sequenceFlow id="F2" sourceRef="Check" targetRef="End"/>
      </bpmn:process>
    </bpmn:definitions>
    """;

if (BpmnDocument.TryRead(source, out var document, out var diagnostics))
{
    Console.WriteLine(document.Validate().Length);
    Console.WriteLine(BpmnTraversal.Route(document, "Start", "End", BpmnEdgeKinds.SequenceFlow, BpmnDirection.Forward, 1000));
    Console.WriteLine(document.Write(BpmnWriteFormat.Pretty));
}
else
{
    Console.WriteLine(diagnostics[0]);
}
```

## Reading

`BpmnDocument.Read` (F#) and `BpmnDocument.TryRead` (C#) read the XML with DTD processing
prohibited and no resolver. A `<!DOCTYPE` or `<!ENTITY` declaration gives an
`xml/prohibited-declaration` diagnostic before parsing, so no entity is ever expanded. Malformed
XML gives `xml/malformed` with its line and column.

The document keeps its `Source`, and `Nodes` is a lossless tree: every element with its prefix,
namespace and attributes in order (namespace declarations included), and every text, CDATA,
comment, processing instruction and white-space node. Values keep their text, so a waypoint's
`x="123.4567890123456789"` is written back unchanged.

## The typed view

`Elements` lists every element in document order as a `BpmnElement`, identified by namespace
URI and local name, so the prefix a document binds does not matter. An element has its `Kind`
(`task`, `sequenceFlow`, `BPMNShape`), `Id`, `Name`, `Parent`, `Children`, `Text` (an
expression's body) and `Documentation`. `Field(name)` gives an attribute or a child element's
text, and `References(name)` gives the identifiers that a reference names, from an attribute
(`sourceRef="A"`) or from child elements (`<flowNodeRef>A</flowNodeRef>`). `Processes`,
`Collaborations`, `Choreographies` and `Planes` select the main parts. Unknown elements and
extension content stay in the tree and are written back in order.

`Find(id)` returns `Found`, `Missing` or `Ambiguous` when several elements share the ID.

## Validation

`Validate()` returns findings of three kinds: `xml`, `structure` (`bpmn/not-definitions`,
`bpmn/duplicate-id`, `bpmn/missing-field`) and `reference` (`bpmn/unresolved-reference`,
`bpmn/ambiguous-reference`, `bpmn/wrong-reference-target`, such as a sequence flow that ends at
a data object). A reference keeps its original value whether it resolves or not. A reference of
the form `prefix:Id` resolves to `Id` when no element has the whole value.

## Writing

`Write(BpmnWriteFormat.Canonical)` writes `<?xml version="1.0" encoding="UTF-8"?>`, then each
top-level comment, instruction and the root element on its own line. White space between
elements is dropped; mixed content keeps every text node. Attributes and children keep their
order, and text escapes `&`, `<`, `>` and carriage returns, with `"`, tab and line feed escaped
in attributes. Writing a canonical document again gives the same bytes.
`BpmnWriteFormat.Pretty` writes the same content with element-only content indented by two
spaces.

## Traversal and graph queries

`BpmnTraversal.Walk` visits every node in document order with its depth. The visitor returns
`Continue`, `SkipChildren` or `Stop`. The walk keeps its own stack, so a document that nests
10,000 elements is visited without a stack overflow.

`BpmnTraversal.Reachable` and `BpmnTraversal.Route` search breadth first over the selected
`BpmnEdgeKinds`: `Containment`, `SequenceFlow`, `MessageFlow`, `Association` (associations and
data associations) and `Reference` (fields such as `bpmnElement`, `attachedToRef`,
`processRef`, `participantRef` and `messageFlowRef`), forward, in reverse or both ways. Each
element is visited once, so cycles end. A search that visits more elements than its limit
returns `LimitReached`. A search that reaches a connection whose other end is a shared
identifier returns `Ambiguous`, and one that reaches a reference naming no element returns
`Missing`: references into imported documents are not followed.
