// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Layout

open System
open System.Collections
open System.Collections.Generic
open System.Collections.Immutable
open Morphir.Diagrams
open Morphir.Svg

/// A part of a layout beyond the baseline that a renderer may draw natively. The set is closed: a new feature is
/// added only with a new drawable, shape kind, path command or arrowhead that a renderer must declare to receive.
[<RequireQualifiedAccess>]
type LayoutFeature =
    /// Rectangles with rounded corners.
    | RoundedRectangles
    /// Ellipses and circles.
    | Ellipses
    /// Diamonds.
    | Diamonds
    /// Cylinders.
    | Cylinders
    /// Hexagons.
    | Hexagons
    /// Shapes with any outline.
    | Paths
    /// One line through all of its waypoints.
    | Polylines
    /// The line's exact final path.
    | ExactRoutes
    /// A smooth curve through the waypoints of a curved line. The curve may differ from the exact path.
    | SmoothCurves
    /// Line ends that stay bound to shapes, images or containers when those move.
    | AttachedConnectors
    /// Line ends bound to another line, at a fraction of its length.
    | LineTargets
    /// The open arrow and the filled circle.
    | OpenAndCircleHeads
    /// The cross, the asynchronous head and the four half heads.
    | MessageHeads
    /// Text that wraps within its box.
    | WrappedText
    /// Text at its rotation.
    | RotatedText
    /// Containers with headers.
    | Containers
    /// Family roles.
    | FamilyRoles
    /// The drawables of composite elements and groups in page-wide paint order, not as units.
    | InterleavedPainting
    /// A native container's children in page-wide paint order relative to drawables outside it.
    | InterleavedContainers
    /// A fill's opacity without changing its stroke.
    | FillOpacity
    /// A stroke's or text's opacity without changing the fill.
    | StrokeAndTextOpacity
    /// Dash patterns other than the theme's default dash.
    | DashPatterns
    /// Links from drawables.
    | Links
    /// Images drawn from SVG documents.
    | SvgImages
    /// Several pages in one output.
    | Pages

    member internal this.Index =
        match this with
        | LayoutFeature.RoundedRectangles -> 0
        | LayoutFeature.Ellipses -> 1
        | LayoutFeature.Diamonds -> 2
        | LayoutFeature.Cylinders -> 3
        | LayoutFeature.Hexagons -> 4
        | LayoutFeature.Paths -> 5
        | LayoutFeature.Polylines -> 6
        | LayoutFeature.ExactRoutes -> 7
        | LayoutFeature.SmoothCurves -> 8
        | LayoutFeature.AttachedConnectors -> 9
        | LayoutFeature.LineTargets -> 10
        | LayoutFeature.OpenAndCircleHeads -> 11
        | LayoutFeature.MessageHeads -> 12
        | LayoutFeature.WrappedText -> 13
        | LayoutFeature.RotatedText -> 14
        | LayoutFeature.Containers -> 15
        | LayoutFeature.FamilyRoles -> 16
        | LayoutFeature.InterleavedPainting -> 17
        | LayoutFeature.InterleavedContainers -> 18
        | LayoutFeature.FillOpacity -> 19
        | LayoutFeature.StrokeAndTextOpacity -> 20
        | LayoutFeature.DashPatterns -> 21
        | LayoutFeature.Links -> 22
        | LayoutFeature.SvgImages -> 23
        | LayoutFeature.Pages -> 24

    /// The feature as documentation writes it, such as attached-connectors.
    member this.WireValue =
        match this with
        | LayoutFeature.RoundedRectangles -> "rounded-rectangles"
        | LayoutFeature.Ellipses -> "ellipses"
        | LayoutFeature.Diamonds -> "diamonds"
        | LayoutFeature.Cylinders -> "cylinders"
        | LayoutFeature.Hexagons -> "hexagons"
        | LayoutFeature.Paths -> "paths"
        | LayoutFeature.Polylines -> "polylines"
        | LayoutFeature.ExactRoutes -> "exact-routes"
        | LayoutFeature.SmoothCurves -> "smooth-curves"
        | LayoutFeature.AttachedConnectors -> "attached-connectors"
        | LayoutFeature.LineTargets -> "line-targets"
        | LayoutFeature.OpenAndCircleHeads -> "open-and-circle-heads"
        | LayoutFeature.MessageHeads -> "message-heads"
        | LayoutFeature.WrappedText -> "wrapped-text"
        | LayoutFeature.RotatedText -> "rotated-text"
        | LayoutFeature.Containers -> "containers"
        | LayoutFeature.FamilyRoles -> "family-roles"
        | LayoutFeature.InterleavedPainting -> "interleaved-painting"
        | LayoutFeature.InterleavedContainers -> "interleaved-containers"
        | LayoutFeature.FillOpacity -> "fill-opacity"
        | LayoutFeature.StrokeAndTextOpacity -> "stroke-and-text-opacity"
        | LayoutFeature.DashPatterns -> "dash-patterns"
        | LayoutFeature.Links -> "links"
        | LayoutFeature.SvgImages -> "svg-images"
        | LayoutFeature.Pages -> "pages"

    override this.ToString() = this.WireValue

    /// Every feature, in the order of its index.
    static member All: IReadOnlyList<LayoutFeature> =
        [|
            LayoutFeature.RoundedRectangles
            LayoutFeature.Ellipses
            LayoutFeature.Diamonds
            LayoutFeature.Cylinders
            LayoutFeature.Hexagons
            LayoutFeature.Paths
            LayoutFeature.Polylines
            LayoutFeature.ExactRoutes
            LayoutFeature.SmoothCurves
            LayoutFeature.AttachedConnectors
            LayoutFeature.LineTargets
            LayoutFeature.OpenAndCircleHeads
            LayoutFeature.MessageHeads
            LayoutFeature.WrappedText
            LayoutFeature.RotatedText
            LayoutFeature.Containers
            LayoutFeature.FamilyRoles
            LayoutFeature.InterleavedPainting
            LayoutFeature.InterleavedContainers
            LayoutFeature.FillOpacity
            LayoutFeature.StrokeAndTextOpacity
            LayoutFeature.DashPatterns
            LayoutFeature.Links
            LayoutFeature.SvgImages
            LayoutFeature.Pages
        |]

/// An immutable set of layout features.
[<Sealed>]
type LayoutFeatures private (bits: int64) =
    member internal _.Bits = bits

    /// Whether the set holds the feature.
    member _.Contains(feature: LayoutFeature) =
        ArgumentNullException.ThrowIfNull feature
        bits &&& (1L <<< feature.Index) <> 0L

    /// The set with the feature added.
    member _.Add(feature: LayoutFeature) =
        ArgumentNullException.ThrowIfNull feature
        LayoutFeatures(bits ||| (1L <<< feature.Index))

    /// The set without the feature.
    member _.Remove(feature: LayoutFeature) =
        ArgumentNullException.ThrowIfNull feature
        LayoutFeatures(bits &&& ~~~(1L <<< feature.Index))

    /// The features in either set.
    member _.Union(other: LayoutFeatures) =
        ArgumentNullException.ThrowIfNull other
        LayoutFeatures(bits ||| other.Bits)

    /// The features in this set that are not in the other.
    member _.Except(other: LayoutFeatures) =
        ArgumentNullException.ThrowIfNull other
        LayoutFeatures(bits &&& ~~~other.Bits)

    /// Whether every feature of this set is in the other.
    member _.IsSubsetOf(other: LayoutFeatures) =
        ArgumentNullException.ThrowIfNull other
        bits &&& ~~~other.Bits = 0L

    /// Whether the set is empty.
    member _.IsEmpty = bits = 0L

    /// The number of features.
    member _.Count = Numerics.BitOperations.PopCount(uint64 bits)

    /// The features in the order of LayoutFeature.All.
    member this.Items: IReadOnlyList<LayoutFeature> =
        LayoutFeature.All |> Seq.filter this.Contains |> Seq.toArray :> IReadOnlyList<_>

    override this.ToString() =
        if bits = 0L then "none" else this.Items |> Seq.map (fun feature -> feature.WireValue) |> String.concat ", "

    override _.Equals(other: obj | null) =
        match other with
        | :? LayoutFeatures as other -> other.Bits = bits
        | _ -> false

    override _.GetHashCode() = bits.GetHashCode()

    interface IEnumerable<LayoutFeature> with
        member this.GetEnumerator() = this.Items.GetEnumerator()

    interface IEnumerable with
        member this.GetEnumerator() = (this.Items :> IEnumerable).GetEnumerator()

    /// The empty set.
    static member None = LayoutFeatures 0L

    /// Every feature.
    static member All = LayoutFeatures((1L <<< LayoutFeature.All.Count) - 1L)

    /// A set of the given features.
    static member Of([<ParamArray>] features: LayoutFeature array) =
        ArgumentNullException.ThrowIfNull features
        features |> Array.fold (fun (set: LayoutFeatures) feature -> set.Add feature) (LayoutFeatures 0L)

/// Walks drawables.
module internal Drawables =
    /// Each drawable and all the drawables it gathers, depth first in containment order.
    let rec flatten (drawables: ImmutableArray<Drawable>) : Drawable seq =
        seq {
            for drawable in drawables do
                yield drawable
                yield! flatten drawable.Kind.Children
        }

    /// The paint-order indices of a drawable and everything it gathers.
    let rec paintIndices (drawable: Drawable) : int seq =
        seq {
            match drawable.Kind with
            | DrawableKind.Element children
            | DrawableKind.Group children ->
                for child in children do
                    yield! paintIndices child
            | DrawableKind.Container container ->
                yield drawable.PaintOrder
                for child in container.Children do
                    yield! paintIndices child
            | _ -> yield drawable.PaintOrder
        }

    /// Whether the paint orders of a unit and everything it gathers are consecutive among the page's leaves.
    let consecutive (leaves: int[]) (drawable: Drawable) =
        let own = paintIndices drawable |> Seq.sort |> Seq.toArray

        if own.Length <= 1 then
            true
        else
            // The number of the page's leaves from the unit's first index to its last, duplicates included.
            let bound (value: int) (upper: bool) =
                let mutable low = 0
                let mutable high = leaves.Length

                while low < high do
                    let middle = (low + high) / 2
                    if (if upper then leaves[middle] <= value else leaves[middle] < value) then low <- middle + 1 else high <- middle

                low

            bound own[own.Length - 1] true - bound own[0] false = own.Length

    /// The default dash of the baseline: 4 pixels on, 4 off.
    let defaultDash = ImmutableArray.Create(Distance.FromPixels 4.0, Distance.FromPixels 4.0)

    /// Whether a dash is solid or the default dash.
    let isBaselineDash (dash: ImmutableArray<Distance>) =
        dash.IsEmpty || (dash.Length = 2 && dash[0].Equals defaultDash[0] && dash[1].Equals defaultDash[1])

    /// The polyline through a line's waypoints as path data.
    let polyline (line: LayoutLine) =
        PathData.OfCommands(
            line.Waypoints
            |> Seq.mapi (fun index point -> if index = 0 then PathCommand.MoveTo point else PathCommand.LineTo point)
        )

    /// Whether a line's exact path draws differently from the polyline through its waypoints.
    let exactDiffers (line: LayoutLine) =
        match line.ExactPath with
        | null -> false
        | exact ->
            // Repeated points draw nothing, so they are dropped before comparing.
            let points (path: PathData) =
                path.Commands
                |> Seq.map (function
                    | PathCommand.MoveTo point
                    | PathCommand.LineTo point -> Some point
                    | _ -> None)
                |> Seq.toList

            let straight = points exact

            if straight |> List.exists Option.isNone then
                true
            else
                let dedupe (items: LayoutPoint list) =
                    items |> List.fold (fun kept point -> match kept with | last :: _ when last.Equals point -> kept | _ -> point :: kept) [] |> List.rev

                let collinear (items: LayoutPoint list) =
                    // Drop points in the middle of a straight run.
                    let rec walk (acc: LayoutPoint list) (rest: LayoutPoint list) =
                        match rest with
                        | a :: b :: c :: tail ->
                            let cross = (b.X - a.X).QuarterEmu * (c.Y - b.Y).QuarterEmu - (b.Y - a.Y).QuarterEmu * (c.X - b.X).QuarterEmu
                            if cross = 0L then walk acc (a :: c :: tail) else walk (a :: acc) (b :: c :: tail)
                        | other -> List.rev acc @ other
                    walk [] items

                let exactPoints = straight |> List.choose id |> dedupe |> collinear
                let waypoints = line.Waypoints |> Seq.toList |> dedupe |> collinear
                exactPoints.Length <> waypoints.Length || List.exists2 (fun (a: LayoutPoint) b -> not (a.Equals b)) exactPoints waypoints

    let private heads (head: EdgeHead) =
        match head with
        | EdgeHead.OpenArrow
        | EdgeHead.Circle -> Some LayoutFeature.OpenAndCircleHeads
        | EdgeHead.Cross
        | EdgeHead.Async
        | EdgeHead.HalfUpper
        | EdgeHead.HalfLower
        | EdgeHead.HalfUpperFilled
        | EdgeHead.HalfLowerFilled -> Some LayoutFeature.MessageHeads
        | EdgeHead.None
        | EdgeHead.Arrow -> None

    let private textFeatures (text: LayoutText) =
        [
            if text.Rotation <> 0 then LayoutFeature.RotatedText
            if text.Wraps then LayoutFeature.WrappedText
            if not text.Ink.IsOpaque then LayoutFeature.StrokeAndTextOpacity
            match text.chip with
            | Some chip when not chip.chipColor.IsOpaque -> LayoutFeature.FillOpacity
            | _ -> ()
        ]

    let private paintFeatures (paint: LayoutPaint) (backdrop: LayoutColor option) =
        [
            match paint.fill, backdrop with
            | Some fill, None when not fill.IsOpaque -> LayoutFeature.FillOpacity
            | _ -> ()
            match paint.stroke with
            | Some stroke when not stroke.IsOpaque -> LayoutFeature.StrokeAndTextOpacity
            | _ -> ()
            if not (isBaselineDash paint.Dash) then LayoutFeature.DashPatterns
        ]

    /// The features that one drawable uses by itself, not counting what it gathers.
    let own (lines: IReadOnlyDictionary<DrawableId, Drawable>) (leaves: int[]) (drawable: Drawable) =
        [
            if not drawable.Links.IsEmpty then LayoutFeature.Links

            match drawable.Kind with
            | DrawableKind.Shape shape ->
                match shape.Kind with
                | ShapeKind.Rectangle -> ()
                | ShapeKind.RoundedRectangle _ -> LayoutFeature.RoundedRectangles
                | ShapeKind.Ellipse -> LayoutFeature.Ellipses
                | ShapeKind.Diamond -> LayoutFeature.Diamonds
                | ShapeKind.Cylinder _ -> LayoutFeature.Cylinders
                | ShapeKind.Hexagon _ -> LayoutFeature.Hexagons
                | ShapeKind.Path _ -> LayoutFeature.Paths

                if shape.Role <> null then LayoutFeature.FamilyRoles
                yield! paintFeatures shape.Paint shape.backdrop
                yield! shape.text |> Option.map textFeatures |> Option.defaultValue []
            | DrawableKind.Line line ->
                if line.Waypoints.Length > 2 then LayoutFeature.Polylines
                if exactDiffers line then LayoutFeature.ExactRoutes
                if line.Route = RouteStyle.Curved then LayoutFeature.SmoothCurves

                for lineEnd in [ line.Source; line.Target ] do
                    yield! heads lineEnd.Head |> Option.toList

                    match lineEnd.binding with
                    | Some binding ->
                        LayoutFeature.AttachedConnectors
                        if lines.ContainsKey binding.Target then LayoutFeature.LineTargets
                    | None -> ()

                yield! paintFeatures line.Paint None
                yield! line.label |> Option.map textFeatures |> Option.defaultValue []
            | DrawableKind.Text text -> yield! textFeatures text
            | DrawableKind.Image _ -> LayoutFeature.SvgImages
            | DrawableKind.Element _
            | DrawableKind.Group _ -> if not (consecutive leaves drawable) then LayoutFeature.InterleavedPainting
            | DrawableKind.Container container ->
                LayoutFeature.Containers
                if not (consecutive leaves drawable) then LayoutFeature.InterleavedContainers
                yield! paintFeatures container.Paint None
                yield! container.header |> Option.map textFeatures |> Option.defaultValue []
        ]

    /// The paint-order indices of every leaf drawable of the page, sorted.
    let leaves (roots: ImmutableArray<Drawable>) =
        roots |> Seq.collect paintIndices |> Seq.sort |> Seq.toArray

    /// The lines of a page by identifier.
    let lineTable (roots: ImmutableArray<Drawable>) =
        flatten roots
        |> Seq.filter (fun drawable -> match drawable.Kind with | DrawableKind.Line _ -> true | _ -> false)
        |> Seq.map (fun drawable -> KeyValuePair(drawable.Id, drawable))
        |> Dictionary
        :> IReadOnlyDictionary<_, _>

    /// The features that the drawables of a page use.
    let features (roots: ImmutableArray<Drawable>) =
        let lines = lineTable roots
        let leaves = leaves roots

        flatten roots
        |> Seq.collect (own lines leaves)
        |> Seq.fold (fun (set: LayoutFeatures) feature -> set.Add feature) LayoutFeatures.None

/// The laid-out form of one page of a diagram: drawables with exact geometry and resolved colours, in one palette.
[<Sealed>]
type DiagramLayout internal (bounds: LayoutBox, background: Color, drawables: ImmutableArray<Drawable>, svg: SvgDocument) =
    let features = lazy (Drawables.features drawables)
    let svgText = lazy (svg.Render())

    /// The page's box: the SVG's view box.
    member _.Bounds = bounds

    /// The page background.
    member _.Background = background

    /// The root drawables, by containment. Each holds its children.
    member _.Drawables = drawables

    /// The features that the drawables use.
    member _.Features = features.Value

    /// The SVG document from the drawing pass that produced this layout, in the layout's palette.
    member _.Svg = svg

    member internal _.SvgText = svgText.Value

    member internal _.WithDrawables(replaced: ImmutableArray<Drawable>) = DiagramLayout(bounds, background, replaced, svg)

    override _.ToString() = $"layout of {drawables.Length} drawables, {bounds}"

    override this.Equals(other: obj | null) =
        match other with
        | :? DiagramLayout as other ->
            other.Bounds.Equals bounds
            && other.Background.Equals background
            && other.Drawables.Length = drawables.Length
            && Seq.forall2 (fun (a: Drawable) (b: Drawable) -> a.Equals b) other.Drawables drawables
            && other.SvgText = this.SvgText
        | _ -> false

    override this.GetHashCode() = HashCode.Combine(bounds, background, drawables.Length, this.SvgText)

/// A layout, or the errors that prevented it, with every diagnostic of the drawing pass.
[<Sealed>]
type LayoutResult internal (layout: DiagramLayout option, diagnostics: ImmutableArray<Diagnostic>) =
    /// The layout, or null when an error prevented it.
    member _.Layout: DiagramLayout | null = Option.toObj layout

    /// Warnings, or the errors that prevented the layout.
    member _.Diagnostics = diagnostics

    override _.ToString() =
        match layout with
        | Some layout -> string layout
        | None -> $"no layout, {diagnostics.Length} diagnostics"
