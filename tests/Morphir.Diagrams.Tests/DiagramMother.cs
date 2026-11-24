// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Morphir.Knowledge;

namespace Morphir.Diagrams.Tests;

/// <summary>The diagrams, documents and snapshots that the diagram tests use, each named by what it represents.</summary>
internal static class DiagramMother
{
    /// <summary>The model of a parsed diagram of the family <typeparamref name="T"/>; throws for another family.</summary>
    public static T Of<T>(DiagramDocument? document) where T : class =>
        (document ?? throw new InvalidOperationException("Expected a document.")).Diagram.Match<object>(
            flowchart => flowchart, sequence => sequence, state => state, swimlane => swimlane,
            architecture => architecture, railroad => railroad, dataTree => dataTree, bpmn => bpmn) as T
        ?? throw new InvalidOperationException($"Expected a {typeof(T).Name}, not {document.Diagram}.");

    /// <summary>The flowchart of a parsed document.</summary>
    public static Flowchart FlowchartOf(DiagramDocument? document) => Of<Flowchart>(document);

    /// <summary>The sequence diagram of a parsed document.</summary>
    public static SequenceDiagram SequenceOf(DiagramDocument? document) => Of<SequenceDiagram>(document);

    /// <summary>Two parallel Cart → Billing edges inside a payments subgraph, and Billing → Ledger.</summary>
    public const string Checkout = """
        flowchart LR
          subgraph payments
            Cart --> Billing
            Cart --> Billing
          end
          Billing --> Ledger
        """;

    /// <summary>The checkout with an unrelated Audit node and edge before the others.</summary>
    public const string CheckoutWithAudit = """
        flowchart LR
          Audit --> Cart
          subgraph payments
            Cart --> Billing
            Cart --> Billing
          end
          Billing --> Ledger
        """;

    /// <summary>Two Api → Db messages, a reply, notes inside and outside nested frames.</summary>
    public const string Exchange = """
        sequenceDiagram
          participant Api
          participant Db
          Api->>Db: Query
          loop Retry
            Db-->>Api: Rows
            alt Cached
              Note over Api: Hit
            end
          end
          Note over Db: Done
          Api->>Db: Close
        """;

    /// <summary>The checkout with the Ledger node renamed, a one-character edit of the source.</summary>
    public const string CheckoutWithRenamedLedger = """
        flowchart LR
          subgraph payments
            Cart --> Billing
            Cart --> Billing
          end
          Billing --> Ledgers
        """;

    /// <summary>
    /// An order pipeline in the given direction: a Checkout subgraph with a decision whose labelled yes and no links
    /// go to two nodes of one rank, a thick link from inside the subgraph to an Event bus outside it, and an Audit log
    /// in a rank that the subgraph spans.
    /// </summary>
    public static string OrderPipeline(string direction) => $$$"""
        flowchart {{{direction}}}
          Browser([Web app]) --> Gateway{{API gateway}}
          subgraph Checkout
            Gateway --> Orders[Order service]
            Orders --> Rules{Valid?}
            Rules -->|yes| Store[(Orders DB)]
            Rules -.->|no| Reject((Reject))
          end
          Orders ==> Bus[[Event bus]]
          Bus --> Audit>Audit log]
        """;

    /// <summary>A decision in a nested subgraph whose yes and no branches each lead on to one node outside it.</summary>
    public const string NestedDecision = """
        flowchart LR
          A[Start] --> B
          subgraph Outer
            B[Load] --> C{Ok?}
            subgraph Inner
              C -->|yes| D[Save]
              C -->|no| E[Retry]
            end
          end
          E --> F[Alert]
          D --> G[Done]
        """;

    public static readonly DocumentId CheckoutDocument = DocumentId.Create(Iri.Create("https://example.org/diagrams/checkout"));

    public static readonly DocumentId ExchangeDocument = DocumentId.Create(Iri.Create("https://example.org/diagrams/exchange"));

    public static DiagramDocument Document(string source) =>
        Mermaid.TryParse(source, out var document, out var diagnostics)
            ? document
            : throw new InvalidOperationException(string.Join("\n", diagnostics));

    public static Diagram Read(string source) => Document(source).Diagram;

    /// <summary>A snapshot of the source under the checkout document, with the derived revision.</summary>
    public static DiagramSnapshot Snapshot(string source) => Snapshot(CheckoutDocument, source);

    /// <summary>The exchange sequence diagram under its own document, with the derived revision.</summary>
    public static DiagramSnapshot ExchangeSnapshot() => Snapshot(ExchangeDocument, Exchange);

    private static DiagramSnapshot Snapshot(DocumentId document, string source) =>
        DiagramSnapshot.TryFromSource(document, Document(source), out var snapshot, out var error)
            ? snapshot
            : throw new InvalidOperationException(error.ToString());

    /// <summary>A BPMN Sales pool with a Clerk lane holding a task at fractional DI coordinates, an exclusive gateway and a user task, and a flow whose last waypoint lies two pixels outside the user task.</summary>
    public const string SalesPoolWithGatewayAndUserTask = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" id="D" targetNamespace="urn:t">
          <bpmn:collaboration id="C"><bpmn:participant id="Pool" name="Sales" processRef="P"/></bpmn:collaboration>
          <bpmn:process id="P">
            <bpmn:laneSet id="LS"><bpmn:lane id="Lane" name="Clerk"><bpmn:flowNodeRef>T</bpmn:flowNodeRef><bpmn:flowNodeRef>G</bpmn:flowNodeRef><bpmn:flowNodeRef>U</bpmn:flowNodeRef></bpmn:lane></bpmn:laneSet>
            <bpmn:task id="T" name="Check"/>
            <bpmn:exclusiveGateway id="G"/>
            <bpmn:userTask id="U" name="Approve"/>
            <bpmn:sequenceFlow id="F1" sourceRef="T" targetRef="G"/>
            <bpmn:sequenceFlow id="F2" sourceRef="G" targetRef="U"/>
          </bpmn:process>
          <bpmndi:BPMNDiagram id="DI"><bpmndi:BPMNPlane id="PL" bpmnElement="C">
            <bpmndi:BPMNShape id="S0" bpmnElement="Pool" isHorizontal="true"><dc:Bounds x="10" y="10" width="600" height="200"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S1" bpmnElement="Lane" isHorizontal="true"><dc:Bounds x="40" y="10" width="570" height="200"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S2" bpmnElement="T"><dc:Bounds x="100.02" y="70.5" width="100" height="80"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S3" bpmnElement="G" isMarkerVisible="true"><dc:Bounds x="250" y="85" width="50" height="50"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S4" bpmnElement="U"><dc:Bounds x="350" y="70" width="100" height="80"/></bpmndi:BPMNShape>
            <bpmndi:BPMNEdge id="E1" bpmnElement="F1"><di:waypoint x="200.02" y="110"/><di:waypoint x="250" y="110"/></bpmndi:BPMNEdge>
            <bpmndi:BPMNEdge id="E2" bpmnElement="F2"><di:waypoint x="300" y="110"/><di:waypoint x="348" y="110"/></bpmndi:BPMNEdge>
          </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    /// <summary>A BPMN Sales pool, with its rotated name, holding a task whose long name wraps.</summary>
    public const string SalesPoolWithLongTask = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" id="D" targetNamespace="urn:t">
          <bpmn:collaboration id="C"><bpmn:participant id="Pool" name="Sales" processRef="P"/></bpmn:collaboration>
          <bpmn:process id="P"><bpmn:task id="T" name="Check the order before it ships"/></bpmn:process>
          <bpmndi:BPMNDiagram id="DI"><bpmndi:BPMNPlane id="PL" bpmnElement="C">
            <bpmndi:BPMNShape id="S0" bpmnElement="Pool" isHorizontal="true"><dc:Bounds x="10" y="10" width="400" height="160"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S1" bpmnElement="T"><dc:Bounds x="100" y="50" width="100" height="80"/></bpmndi:BPMNShape>
          </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    /// <summary>A BPMN Sales pool with a Clerk lane holding one task.</summary>
    public const string SalesPoolWithClerkLane = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" id="D" targetNamespace="urn:t">
          <bpmn:collaboration id="C"><bpmn:participant id="Pool" name="Sales" processRef="P"/></bpmn:collaboration>
          <bpmn:process id="P">
            <bpmn:laneSet id="LS"><bpmn:lane id="Lane" name="Clerk"><bpmn:flowNodeRef>T</bpmn:flowNodeRef></bpmn:lane></bpmn:laneSet>
            <bpmn:task id="T" name="Check"/>
          </bpmn:process>
          <bpmndi:BPMNDiagram id="DI"><bpmndi:BPMNPlane id="PL" bpmnElement="C">
            <bpmndi:BPMNShape id="S0" bpmnElement="Pool" isHorizontal="true"><dc:Bounds x="10" y="10" width="400" height="160"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S1" bpmnElement="Lane" isHorizontal="true"><dc:Bounds x="40" y="10" width="370" height="160"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S2" bpmnElement="T"><dc:Bounds x="100" y="50" width="100" height="80"/></bpmndi:BPMNShape>
          </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    /// <summary>A BPMN process with a task and a data object.</summary>
    public const string TaskWithDataObject = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" id="D" targetNamespace="urn:t">
          <bpmn:process id="P">
            <bpmn:task id="T" name="Check"/>
            <bpmn:dataObjectReference id="Doc" name="Order"/>
          </bpmn:process>
          <bpmndi:BPMNDiagram id="DI"><bpmndi:BPMNPlane id="PL" bpmnElement="P">
            <bpmndi:BPMNShape id="S2" bpmnElement="T"><dc:Bounds x="100" y="50" width="100" height="80"/></bpmndi:BPMNShape>
            <bpmndi:BPMNShape id="S3" bpmnElement="Doc"><dc:Bounds x="250" y="60" width="36" height="50"/></bpmndi:BPMNShape>
          </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    /// <summary>A sequence with a message, a dashed reply, a self-message and a message that creates its receiver.</summary>
    public const string ExchangeWithSelfAndCreation = """
        sequenceDiagram
          participant A
          participant B
          A->>B: hi
          B-->>A: ok
          A->>A: self
          create participant C
          A->>C: new
        """;
}
