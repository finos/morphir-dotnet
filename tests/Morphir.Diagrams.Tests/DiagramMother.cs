// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Morphir.Knowledge;

namespace Morphir.Diagrams.Tests;

/// <summary>The diagrams, documents and snapshots that the diagram tests use, each named by what it represents.</summary>
internal static class DiagramMother
{
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
}
