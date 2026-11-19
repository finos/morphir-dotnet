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
