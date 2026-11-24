// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The diagrams and snapshots that the F# tests use, each named by what it represents.
module Morphir.Diagrams.FSharp.Tests.DiagramMother

open System.Collections.Immutable
open Morphir.Diagrams
open Morphir.Diagrams.Layout
open Morphir.Knowledge
open Morphir.Svg

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

// A small layout builder for pages that no diagram source draws.
let private point x y = LayoutPoint.Create(Distance.FromPixels x, Distance.FromPixels y)

let private paint: LayoutPaint =
    let black: LayoutColor = { color = Color.FromRgb(0, 0, 0); source = ColorSource.Authored }
    { fill = None; stroke = Some black; ink = black; strokeWidth = Distance.FromPixels 1.0; dash = ImmutableArray.Empty; cap = LineCap.Butt; join = LineJoin.Miter }

let private line id (points: (float * float) list) (target: LineBinding option) : Drawable =
    let points = points |> List.map (fun (x, y) -> point x y)

    { id = DrawableId.Create id
      reference = None
      paintOrder = int (id.Substring 1)
      kind =
        DrawableKind.Line
            { source = { point = List.head points; head = EdgeHead.None; binding = None }
              target = { point = List.last points; head = EdgeHead.Arrow; binding = target }
              waypoints = ImmutableArray.CreateRange points
              route = (if points.Length > 2 then RouteStyle.Orthogonal else RouteStyle.Straight)
              exactPath = None
              paint = paint
              label = None }
      links = ImmutableArray.Empty }

/// A page with a line that bends twice, d1, and a straight line, d2, whose target is bound to the middle of d1.
let lineBoundToABentLine =
    let bent = line "d1" [ 0.0, 0.0; 50.0, 0.0; 50.0, 50.0; 100.0, 50.0 ] None
    let bound = line "d2" [ 50.0, 100.0; 50.0, 25.0 ] (Some { target = DrawableId.Create "d1"; anchor = LineAnchor.Along Fraction.Half })
    DiagramLayout(LayoutBox.Create(Distance.Zero, Distance.Zero, Distance.FromPixels 100.0, Distance.FromPixels 100.0), Color.FromRgb(255, 255, 255), ImmutableArray.Create(bent, bound), SvgDocument.Create([||], [||]))
