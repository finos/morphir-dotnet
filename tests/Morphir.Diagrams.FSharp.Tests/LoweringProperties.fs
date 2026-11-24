// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Diagrams.FSharp.Tests.LoweringProperties

open System.Collections.Immutable
open Expecto
open FsCheck
open Morphir.Diagrams
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout
open Morphir.Svg
open Morphir.TestSupport.Fixtures

/// The layout of every pinned corpus diagram that lays out.
let private layouts =
    lazy
        (DiagramCorpus.Sources()
         |> Seq.choose (fun source ->
             match DiagramSource.Parse source.Source with
             | Ok document -> Option.ofObj (DiagramExport.Layout document.Diagram).Layout
             | Error _ -> None)
         |> Seq.toArray)

/// The features of a bit mask over LayoutFeature.All.
let private featuresOf (bits: int) =
    LayoutFeature.All
    |> Seq.indexed
    |> Seq.fold (fun (set: LayoutFeatures) (index, feature) -> if bits &&& (1 <<< index) <> 0 then set.Add feature else set) LayoutFeatures.None

/// How a renderer paints is not data that lowering can change, so paint order is only reported.
let private paintOrder = LayoutFeatures.Of(LayoutFeature.InterleavedPainting, LayoutFeature.InterleavedContainers)

let private byId (layout: DiagramLayout) =
    Drawables.flatten layout.Drawables |> Seq.map (fun drawable -> drawable.Id, drawable) |> dict

let private boxOf (drawable: Drawable) =
    match drawable.Kind with
    | DrawableKind.Shape shape -> Some shape.Box
    | DrawableKind.Image image -> Some image.Box
    | DrawableKind.Container container -> Some container.Box
    | _ -> None

let private lowers (index: PositiveInt) (bits: int) =
    let pages = layouts.Value
    let page = pages[index.Get % pages.Length]
    let declared = featuresOf bits
    let lowered, _ = Lowering.lower declared page
    let before, after = byId page, byId lowered

    // Only declared features and the baseline. With exact routes, a line's waypoints and route style only describe
    // its exact path.
    let described =
        if declared.Contains LayoutFeature.ExactRoutes then LayoutFeatures.Of(LayoutFeature.Polylines, LayoutFeature.SmoothCurves) else LayoutFeatures.None

    let used = (lowered.Features.Except paintOrder).Except described
    let onlyDeclared = used.IsSubsetOf declared

    // Each surviving drawable keeps its identifier, reference, box and paint-order index.
    let identity =
        after
        |> Seq.forall (fun pair ->
            match before.TryGetValue pair.Key with
            | true, original ->
                original.Reference = pair.Value.Reference
                && original.PaintOrder = pair.Value.PaintOrder
                && (match boxOf original, boxOf pair.Value with
                    | Some a, Some b -> a.Equals b
                    | _ -> true)
            | false, _ -> true)

    // Every binding names an eligible drawable on the same page.
    let eligible =
        after.Values
        |> Seq.forall (fun drawable ->
            match drawable.Kind with
            | DrawableKind.Line line ->
                [ line.Source; line.Target ]
                |> List.forall (fun lineEnd ->
                    match lineEnd.Binding with
                    | null -> true
                    | binding ->
                        declared.Contains LayoutFeature.AttachedConnectors
                        && (match after.TryGetValue binding.Target with
                            | true, target ->
                                match target.Kind with
                                | DrawableKind.Shape _
                                | DrawableKind.Image _ -> true
                                | DrawableKind.Container _ -> declared.Contains LayoutFeature.Containers
                                | DrawableKind.Line _ -> declared.Contains LayoutFeature.LineTargets
                                | _ -> false
                            | false, _ -> false))
            | _ -> true)

    (onlyDeclared |@ $"uses {used.Except declared} of {declared}")
    .&. (identity |@ "keeps identity")
    .&. (eligible |@ "binds only eligible drawables")

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

[<Tests>]
let tests =
    testList "lowering" [
        testPropertyWithConfig
            { FsCheckConfig.defaultConfig with maxTest = 400 }
            "every corpus layout lowers to any feature subset with its identity and eligible bindings"
            lowers

        test "a binding to a line that would be split is removed" {
            let bent = line "d1" [ 0.0, 0.0; 50.0, 0.0; 50.0, 50.0; 100.0, 50.0 ] None
            let bound = line "d2" [ 50.0, 100.0; 50.0, 25.0 ] (Some { target = DrawableId.Create "d1"; anchor = LineAnchor.Along Fraction.Half })
            let page = DiagramLayout(LayoutBox.Create(Distance.Zero, Distance.Zero, Distance.FromPixels 100.0, Distance.FromPixels 100.0), Color.FromRgb(255, 255, 255), ImmutableArray.Create(bent, bound), SvgDocument.Create([||], [||]))

            let lowered, degradations = Lowering.lower (LayoutFeatures.Of(LayoutFeature.AttachedConnectors, LayoutFeature.LineTargets)) page
            let target = (byId lowered).[DrawableId.Create "d2"]

            match target.Kind with
            | DrawableKind.Line line -> Expect.isNull line.Target.Binding "the target line becomes a group of segments"
            | _ -> failtest "d2 stays a line"

            Expect.isTrue (degradations |> List.exists (fun degradation -> degradation.Drawables.Contains(DrawableId.Create "d2"))) "the removal is a degradation"

            let kept, _ = Lowering.lower (LayoutFeatures.Of(LayoutFeature.AttachedConnectors, LayoutFeature.LineTargets, LayoutFeature.Polylines)) page

            match (byId kept).[DrawableId.Create "d2"].Kind with
            | DrawableKind.Line line -> Expect.isNotNull line.Target.Binding "a polyline stays one line"
            | _ -> failtest "d2 stays a line"
        }
    ]
