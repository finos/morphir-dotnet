// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The diagrams and snapshots that the F# tests use, each named by what it represents.
module Morphir.Diagrams.FSharp.Tests.DiagramMother

open Morphir.Diagrams
open Morphir.Knowledge

/// Two nodes joined by one link.
let pair = "flowchart LR\n  A --> B"

/// The checkout flowchart with two parallel Cart → Billing links.
let checkout = "flowchart LR\n  Cart --> Billing\n  Cart --> Billing"

let checkoutDocument = DocumentId.Create(Iri.Create "https://example.org/diagrams/checkout")

/// A snapshot of the source under the checkout document, with the derived revision.
let snapshot source =
    match Mermaid.Parse source with
    | Error _ -> failwith "The source does not parse."
    | Ok document ->
        match DiagramSnapshot.TryFromSource(checkoutDocument, document) with
        | Ok snapshot -> snapshot
        | Error error -> failwith (error.ToString())

/// A selection of the referenced elements of a snapshot.
let select snapshot references =
    match DiagramSelection.TryCreate(snapshot, references |> List.map DiagramElementRef.Create) with
    | Ok selection -> selection
    | Error error -> failwith (error.ToString())
