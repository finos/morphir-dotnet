// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Export

open System
open System.Collections.Generic
open System.Collections.Immutable
open Morphir.Diagrams
open Morphir.Diagrams.Layout
open Morphir.Svg

/// One fallback applied to a layout for a renderer: the feature it stands in for, what the drawables became, and the
/// drawables it affects.
[<Sealed>]
type Degradation internal (feature: LayoutFeature, fallback: string, drawables: ImmutableArray<DrawableId>, references: ImmutableArray<DiagramElementRef>) =
    /// The feature that the renderer does not declare.
    member _.Feature = feature

    /// What the drawables became, such as rectangle or free end. It is display text for people, not a value to match.
    member _.Fallback = fallback

    /// The identifiers of the drawables it affects.
    member _.Drawables = drawables

    /// The references of the drawables it affects that have one.
    member _.References = references

    override _.ToString() =
        let named =
            if references.IsEmpty then $"{drawables.Length} drawables"
            else references |> Seq.map (fun reference -> reference.ToString()) |> String.concat ", "

        $"{feature.WireValue} becomes {fallback}: {named}"

    override _.Equals(other: obj | null) =
        match other with
        | :? Degradation as other ->
            other.Feature = feature && other.Fallback = fallback && other.Drawables.Length = drawables.Length && Seq.forall2 (=) other.Drawables drawables
        | _ -> false

    override _.GetHashCode() = HashCode.Combine(feature, fallback, drawables.Length)

/// Lowers layouts to the features a renderer declares. Lowering is a pure function of the page and the features. Its
/// passes run in a fixed order: containers, family roles, shape kinds and paths, images, bindings, routes, arrowheads,
/// text, opacity, dash patterns, links and paint order.
[<RequireQualifiedAccess>]
module internal Lowering =
    type private Recorder(owners: IReadOnlyDictionary<DrawableId, DiagramElementRef>) =
        // The first feature that degraded each drawable in each pass, with its form.
        let first = Dictionary<DrawableId * int, LayoutFeature * string * DiagramElementRef option>()
        let order = ResizeArray<DrawableId * int>()
        let mutable pass = 0

        /// Starts the next pass of lowering.
        member _.Pass(number: int) = pass <- number

        // A drawable has one degradation for each pass: a later fallback in the same pass, such as a cylinder that
        // becomes a rectangle after paths, refines it. A drawable with no reference names the element that holds it.
        member _.Record (feature: LayoutFeature) (fallback: string) (drawable: Drawable) =
            let key = drawable.Id, pass

            if not (first.ContainsKey key) then
                let reference =
                    drawable.reference
                    |> Option.orElse (match owners.TryGetValue drawable.Id with | true, owner -> Some owner | false, _ -> None)

                first[key] <- (feature, fallback, reference)
                order.Add key

        member _.Degradations() =
            order
            |> Seq.map (fun key -> fst key, first[key])
            |> Seq.groupBy (fun (_, (feature, fallback, _)) -> feature.Index, fallback)
            |> Seq.sortBy fst
            |> Seq.map (fun (_, items) ->
                let items = Seq.toArray items
                let feature, fallback, _ = snd items[0]
                Degradation(
                    feature,
                    fallback,
                    items |> Array.map fst |> ImmutableArray.CreateRange,
                    items |> Array.choose (fun (_, (_, _, reference)) -> reference) |> ImmutableArray.CreateRange
                ))
            |> Seq.toList

    /// Rebuilds every drawable of a page, children first.
    let rec private map (change: Drawable -> Drawable) (drawable: Drawable) : Drawable =
        let drawable =
            match drawable.Kind with
            | DrawableKind.Element children -> { drawable with kind = DrawableKind.Element(children |> Seq.map (map change) |> ImmutableArray.CreateRange) }
            | DrawableKind.Group children -> { drawable with kind = DrawableKind.Group(children |> Seq.map (map change) |> ImmutableArray.CreateRange) }
            | DrawableKind.Container container ->
                { drawable with kind = DrawableKind.Container { container with children = container.children |> Seq.map (map change) |> ImmutableArray.CreateRange } }
            | _ -> drawable

        change drawable

    /// Removes the drawables a test selects, wherever they are.
    let rec private prune (keep: Drawable -> bool) (drawables: ImmutableArray<Drawable>) : ImmutableArray<Drawable> =
        drawables
        |> Seq.filter keep
        |> Seq.map (fun drawable ->
            match drawable.Kind with
            | DrawableKind.Element children -> { drawable with kind = DrawableKind.Element(prune keep children) }
            | DrawableKind.Group children -> { drawable with kind = DrawableKind.Group(prune keep children) }
            | DrawableKind.Container container -> { drawable with kind = DrawableKind.Container { container with children = prune keep container.children } }
            | _ -> drawable)
        |> ImmutableArray.CreateRange

    let private derived (drawable: Drawable) (suffix: string) kind : Drawable =
        { id = DrawableId.Create($"{drawable.Id.Value}-{suffix}"); reference = None; paintOrder = drawable.paintOrder; kind = kind; links = ImmutableArray.Empty }

    let private point x y = LayoutPoint.Create(x, y)
    let private half (distance: Distance) = Distance.FromQuarterEmu(distance.QuarterEmu / 2L)

    /// The outline of a named shape kind as path data in its box.
    let outline (kind: ShapeKind) (box: LayoutBox) =
        let cx, cy = box.X + half box.Width, box.Y + half box.Height
        let rx, ry = half box.Width, half box.Height

        let commands =
            match kind with
            | ShapeKind.Ellipse ->
                [
                    PathCommand.MoveTo(point box.X cy)
                    PathCommand.ArcTo(rx, ry, 0.0, true, false, point box.Right cy)
                    PathCommand.ArcTo(rx, ry, 0.0, true, false, point box.X cy)
                    PathCommand.Close
                ]
            | ShapeKind.Diamond ->
                [
                    PathCommand.MoveTo(point cx box.Y)
                    PathCommand.LineTo(point box.Right cy)
                    PathCommand.LineTo(point cx box.Bottom)
                    PathCommand.LineTo(point box.X cy)
                    PathCommand.Close
                ]
            | ShapeKind.Hexagon inset ->
                [
                    PathCommand.MoveTo(point box.X cy)
                    PathCommand.LineTo(point (box.X + inset) box.Y)
                    PathCommand.LineTo(point (box.Right - inset) box.Y)
                    PathCommand.LineTo(point box.Right cy)
                    PathCommand.LineTo(point (box.Right - inset) box.Bottom)
                    PathCommand.LineTo(point (box.X + inset) box.Bottom)
                    PathCommand.Close
                ]
            | ShapeKind.Cylinder cap ->
                let top, bottom = box.Y + cap, box.Bottom - cap
                [
                    // The body, then the front edge of the top as a second subpath.
                    PathCommand.MoveTo(point box.X top)
                    PathCommand.ArcTo(rx, cap, 0.0, false, true, point box.Right top)
                    PathCommand.LineTo(point box.Right bottom)
                    PathCommand.ArcTo(rx, cap, 0.0, false, true, point box.X bottom)
                    PathCommand.Close
                    PathCommand.MoveTo(point box.X top)
                    PathCommand.ArcTo(rx, cap, 0.0, false, false, point box.Right top)
                ]
            | ShapeKind.RoundedRectangle radius ->
                let r = Distance.Min(radius, Distance.Min(rx, ry))
                [
                    PathCommand.MoveTo(point (box.X + r) box.Y)
                    PathCommand.LineTo(point (box.Right - r) box.Y)
                    PathCommand.ArcTo(r, r, 0.0, false, true, point box.Right (box.Y + r))
                    PathCommand.LineTo(point box.Right (box.Bottom - r))
                    PathCommand.ArcTo(r, r, 0.0, false, true, point (box.Right - r) box.Bottom)
                    PathCommand.LineTo(point (box.X + r) box.Bottom)
                    PathCommand.ArcTo(r, r, 0.0, false, true, point box.X (box.Bottom - r))
                    PathCommand.LineTo(point box.X (box.Y + r))
                    PathCommand.ArcTo(r, r, 0.0, false, true, point (box.X + r) box.Y)
                    PathCommand.Close
                ]
            | ShapeKind.Rectangle ->
                [
                    PathCommand.MoveTo(point box.X box.Y)
                    PathCommand.LineTo(point box.Right box.Y)
                    PathCommand.LineTo(point box.Right box.Bottom)
                    PathCommand.LineTo(point box.X box.Bottom)
                    PathCommand.Close
                ]
            | ShapeKind.Path data -> List.ofSeq data.Commands

        PathData.OfCommands commands

    /// A standalone SVG document that draws a path in its own box, in its paint.
    let private pathImage (data: PathData) (box: LayoutBox) (paint: LayoutPaint) =
        let n (distance: Distance) = distance.ToPixels().ToString("0.####", Globalization.CultureInfo.InvariantCulture)
        let color (value: LayoutColor option) = value |> Option.map (fun color -> color.Color.ToCss()) |> Option.defaultValue "none"

        SvgDocument.Create(
            [|
                SvgAttribute.Create("viewBox", $"{n box.X} {n box.Y} {n box.Width} {n box.Height}")
                SvgAttribute.Create("width", n box.Width)
                SvgAttribute.Create("height", n box.Height)
            |],
            [|
                SvgNode.CreateElement(
                    "path",
                    [|
                        SvgAttribute.Create("d", string data)
                        SvgAttribute.Create("fill", color paint.fill)
                        SvgAttribute.Create("stroke", color paint.stroke)
                        SvgAttribute.Create("stroke-width", n paint.strokeWidth)
                    |],
                    [||]
                )
            |]
        )

    let private headFeature (head: EdgeHead) =
        match head with
        | EdgeHead.Async
        | EdgeHead.HalfUpper
        | EdgeHead.HalfLower
        | EdgeHead.HalfUpperFilled
        | EdgeHead.HalfLowerFilled
        | EdgeHead.Cross -> Some LayoutFeature.MessageHeads
        | EdgeHead.OpenArrow
        | EdgeHead.Circle -> Some LayoutFeature.OpenAndCircleHeads
        | EdgeHead.None
        | EdgeHead.Arrow -> None

    /// Where the route table sends a line, judged without its bindings when `bound` is false.
    let private routeRule (declared: LayoutFeatures) (line: LayoutLine) (bound: bool) =
        if declared.Contains LayoutFeature.ExactRoutes then 1
        elif declared.Contains LayoutFeature.SmoothCurves && line.Route = RouteStyle.Curved then 2
        elif declared.Contains LayoutFeature.Polylines then 3
        elif bound || line.Waypoints.Length <= 2 then 4
        else 5

    let private textOf (declared: LayoutFeatures) (record: LayoutFeature -> string -> unit) (text: LayoutText) =
        let text =
            if text.rotation <> 0 && not (declared.Contains LayoutFeature.RotatedText) then
                record LayoutFeature.RotatedText "upright text"
                { text with rotation = 0 }
            else
                text

        if text.wraps && not (declared.Contains LayoutFeature.WrappedText) then
            record LayoutFeature.WrappedText "explicit lines"
            { text with wraps = false }
        else
            text

    /// Lowers one page to the declared features, returning the page and its degradations.
    let lower (declared: LayoutFeatures) (page: DiagramLayout) : DiagramLayout * Degradation list =
        // The nearest referenced drawable that holds each drawable.
        let owners = Dictionary<DrawableId, DiagramElementRef>()

        let rec own (inherited: DiagramElementRef option) (drawable: Drawable) =
            inherited |> Option.iter (fun reference -> owners[drawable.Id] <- reference)
            let next = drawable.reference |> Option.orElse inherited
            for child in drawable.Kind.Children do own next child

        for drawable in page.Drawables do own None drawable

        let recorder = Recorder(owners)
        let has (feature: LayoutFeature) = declared.Contains feature
        let roots = page.Drawables

        recorder.Pass 1
        // 1. Containers become groups that hold their frame, header band, header text and children.
        let roots =
            if has LayoutFeature.Containers then
                roots
            else
                roots
                |> Seq.map (map (fun drawable ->
                    match drawable.Kind with
                    | DrawableKind.Container container ->
                        recorder.Record LayoutFeature.Containers "group" drawable
                        let frame: LayoutShape =
                            { kind = (if container.radius.QuarterEmu > 0L then ShapeKind.RoundedRectangle container.radius else ShapeKind.Rectangle)
                              box = container.frame
                              paint = container.framePaint
                              text = None
                              role = None
                              backdrop = None }

                        let band =
                            if container.band.QuarterEmu > 0L then
                                let x = container.frame.X + container.band
                                let divider: LayoutShape =
                                    { frame with
                                        kind = ShapeKind.Path(PathData.OfCommands [ PathCommand.MoveTo(point x container.frame.Y); PathCommand.LineTo(point x container.frame.Bottom) ])
                                        box = LayoutBox.Create(x, container.frame.Y, Distance.Zero, container.frame.Height)
                                        paint = { container.framePaint with fill = None } }
                                [ derived drawable "band" (DrawableKind.Shape divider) ]
                            else
                                []

                        let header = container.header |> Option.map (fun text -> derived drawable "header" (DrawableKind.Text text)) |> Option.toList

                        { drawable with
                            kind =
                                DrawableKind.Group(
                                    ImmutableArray.CreateRange(
                                        [ derived drawable "frame" (DrawableKind.Shape frame) ] @ band @ header @ List.ofSeq container.children
                                    )
                                ) }
                    | _ -> drawable))
                |> ImmutableArray.CreateRange

        recorder.Pass 2
        // 2. Family roles are removed.
        let roots =
            if has LayoutFeature.FamilyRoles then
                roots
            else
                roots
                |> Seq.map (map (fun drawable ->
                    match drawable.Kind with
                    | DrawableKind.Shape shape when shape.role.IsSome ->
                        recorder.Record LayoutFeature.FamilyRoles "no role" drawable
                        { drawable with kind = DrawableKind.Shape { shape with role = None } }
                    | _ -> drawable))
                |> ImmutableArray.CreateRange

        recorder.Pass 3
        // 3. Shape kinds fall back to paths, then rectangles; a path from the layout falls back to an image, then a
        // rectangle. A kind never becomes an image, so its text stays editable.
        let roots =
            roots
            |> Seq.map (map (fun drawable ->
                match drawable.Kind with
                | DrawableKind.Shape shape ->
                    let feature =
                        match shape.kind with
                        | ShapeKind.Rectangle -> None
                        | ShapeKind.RoundedRectangle _ -> Some LayoutFeature.RoundedRectangles
                        | ShapeKind.Ellipse -> Some LayoutFeature.Ellipses
                        | ShapeKind.Diamond -> Some LayoutFeature.Diamonds
                        | ShapeKind.Cylinder _ -> Some LayoutFeature.Cylinders
                        | ShapeKind.Hexagon _ -> Some LayoutFeature.Hexagons
                        | ShapeKind.Path _ -> Some LayoutFeature.Paths

                    match feature, shape.kind with
                    | Some feature, _ when has feature -> drawable
                    | None, _ -> drawable
                    | Some LayoutFeature.RoundedRectangles, _ ->
                        recorder.Record LayoutFeature.RoundedRectangles "rectangle" drawable
                        { drawable with kind = DrawableKind.Shape { shape with kind = ShapeKind.Rectangle } }
                    | Some LayoutFeature.Paths, ShapeKind.Path data ->
                        if has LayoutFeature.SvgImages then
                            recorder.Record LayoutFeature.Paths "image" drawable
                            let image = LayoutImage(shape.box, pathImage data shape.box shape.paint)

                            match shape.text with
                            | Some text ->
                                { drawable with
                                    kind =
                                        DrawableKind.Group(
                                            ImmutableArray.Create(derived drawable "image" (DrawableKind.Image image), derived drawable "text" (DrawableKind.Text text))
                                        ) }
                            | None -> { drawable with kind = DrawableKind.Image image }
                        else
                            recorder.Record LayoutFeature.Paths "rectangle" drawable
                            { drawable with kind = DrawableKind.Shape { shape with kind = ShapeKind.Rectangle } }
                    | Some feature, kind ->
                        if has LayoutFeature.Paths then
                            recorder.Record feature "path" drawable
                            { drawable with kind = DrawableKind.Shape { shape with kind = ShapeKind.Path(outline kind shape.box) } }
                        else
                            recorder.Record feature "rectangle" drawable
                            { drawable with kind = DrawableKind.Shape { shape with kind = ShapeKind.Rectangle } }
                | _ -> drawable))
            |> ImmutableArray.CreateRange

        recorder.Pass 4
        // 4. Images are removed when SVG images are not declared.
        let roots =
            if has LayoutFeature.SvgImages then
                roots
            else
                roots
                |> prune (fun drawable ->
                    match drawable.Kind with
                    | DrawableKind.Image _ ->
                        recorder.Record LayoutFeature.SvgImages "removed" drawable
                        false
                    | _ -> true)

        recorder.Pass 5
        // 5. Bindings, over the whole page now that every replacement is known.
        let all = Drawables.flatten roots |> Seq.map (fun drawable -> drawable.Id, drawable) |> dict

        let containers =
            page.Drawables |> Drawables.flatten |> Seq.filter (fun drawable -> match drawable.Kind with DrawableKind.Container _ -> true | _ -> false) |> Seq.map (fun drawable -> drawable.Id) |> HashSet

        let eligible (target: DrawableId) : LayoutFeature option =
            match all.TryGetValue target with
            | false, _ -> Some LayoutFeature.AttachedConnectors
            | true, drawable ->
                match drawable.Kind with
                | DrawableKind.Shape _
                | DrawableKind.Image _ -> None
                | DrawableKind.Container _ -> if has LayoutFeature.Containers then None else Some LayoutFeature.Containers
                | DrawableKind.Line line ->
                    if not (has LayoutFeature.LineTargets) then Some LayoutFeature.LineTargets
                    elif routeRule declared line false = 5 then Some LayoutFeature.Polylines
                    else None
                | DrawableKind.Group _ when containers.Contains drawable.Id -> Some LayoutFeature.Containers
                | _ -> Some LayoutFeature.AttachedConnectors

        let roots =
            roots
            |> Seq.map (map (fun drawable ->
                match drawable.Kind with
                | DrawableKind.Line line ->
                    let resolve (lineEnd: LineEnd) =
                        match lineEnd.binding with
                        | None -> lineEnd
                        | Some binding ->
                            let missing = if has LayoutFeature.AttachedConnectors then eligible binding.target else Some LayoutFeature.AttachedConnectors

                            match missing with
                            | None -> lineEnd
                            | Some feature ->
                                recorder.Record feature "free end" drawable
                                { lineEnd with binding = None }

                    { drawable with kind = DrawableKind.Line { line with source = resolve line.source; target = resolve line.target } }
                | _ -> drawable))
            |> ImmutableArray.CreateRange

        recorder.Pass 6
        // 6. Routes, from the route table.
        let roots =
            roots
            |> Seq.map (map (fun drawable ->
                match drawable.Kind with
                | DrawableKind.Line line ->
                    let bound = line.source.binding.IsSome || line.target.binding.IsSome
                    let differs = Drawables.exactDiffers line

                    match routeRule declared line bound with
                    | 1 -> drawable
                    | 2 -> { drawable with kind = DrawableKind.Line { line with exactPath = None } }
                    | 3 ->
                        if differs then recorder.Record LayoutFeature.ExactRoutes "polyline" drawable
                        { drawable with kind = DrawableKind.Line { line with exactPath = None; route = (if line.route = RouteStyle.Curved then RouteStyle.Straight else line.route) } }
                    | 4 ->
                        if line.waypoints.Length > 2 then recorder.Record LayoutFeature.Polylines "straight segment" drawable
                        elif differs then recorder.Record LayoutFeature.ExactRoutes "straight segment" drawable

                        { drawable with
                            kind =
                                DrawableKind.Line
                                    { line with
                                        exactPath = None
                                        route = RouteStyle.Straight
                                        waypoints = ImmutableArray.Create(line.source.point, line.target.point) } }
                    | _ ->
                        recorder.Record LayoutFeature.Polylines "segments" drawable
                        let points = line.waypoints
                        let last = points.Length - 2
                        let middle = last / 2

                        let segments =
                            [
                                for index in 0..last do
                                    let start, finish = points[index], points[index + 1]
                                    let segment: LayoutLine =
                                        { source = { point = start; head = (if index = 0 then line.source.head else EdgeHead.None); binding = None }
                                          target = { point = finish; head = (if index = last then line.target.head else EdgeHead.None); binding = None }
                                          waypoints = ImmutableArray.Create(start, finish)
                                          route = RouteStyle.Straight
                                          exactPath = None
                                          paint = line.paint
                                          label = (if index = middle then line.label else None) }

                                    derived drawable $"s{index}" (DrawableKind.Line segment)
                            ]

                        { drawable with kind = DrawableKind.Group(ImmutableArray.CreateRange segments) }
                | _ -> drawable))
            |> ImmutableArray.CreateRange

        recorder.Pass 7
        // 7. Arrowheads fall back: message heads to open or filled arrows, then open arrows and circles to filled.
        let roots =
            roots
            |> Seq.map (map (fun drawable ->
                match drawable.Kind with
                | DrawableKind.Line line ->
                    let lower (lineEnd: LineEnd) =
                        let afterMessage =
                            if has LayoutFeature.MessageHeads then
                                lineEnd.head
                            else
                                match lineEnd.head with
                                | EdgeHead.Async
                                | EdgeHead.HalfUpper
                                | EdgeHead.HalfLower -> EdgeHead.OpenArrow
                                | EdgeHead.HalfUpperFilled
                                | EdgeHead.HalfLowerFilled
                                | EdgeHead.Cross -> EdgeHead.Arrow
                                | head -> head

                        let final =
                            if has LayoutFeature.OpenAndCircleHeads then
                                afterMessage
                            else
                                match afterMessage with
                                | EdgeHead.OpenArrow
                                | EdgeHead.Circle -> EdgeHead.Arrow
                                | head -> head

                        if final <> lineEnd.head then
                            headFeature lineEnd.head |> Option.iter (fun feature -> recorder.Record feature final.WireValue drawable)

                        { lineEnd with head = final }

                    { drawable with kind = DrawableKind.Line { line with source = lower line.source; target = lower line.target } }
                | _ -> drawable))
            |> ImmutableArray.CreateRange

        recorder.Pass 8
        // 8. Text loses its rotation, then its wrapping.
        let roots =
            roots
            |> Seq.map (map (fun drawable ->
                let text = textOf declared (fun feature fallback -> recorder.Record feature fallback drawable)

                match drawable.Kind with
                | DrawableKind.Text value -> { drawable with kind = DrawableKind.Text(text value) }
                | DrawableKind.Shape shape -> { drawable with kind = DrawableKind.Shape { shape with text = shape.text |> Option.map text } }
                | DrawableKind.Line line -> { drawable with kind = DrawableKind.Line { line with label = line.label |> Option.map text } }
                | DrawableKind.Container container -> { drawable with kind = DrawableKind.Container { container with header = container.header |> Option.map text } }
                | _ -> drawable))
            |> ImmutableArray.CreateRange

        recorder.Pass 9
        // 9. Opacity. A backdrop and the translucent fill over it are one opaque colour, whatever the renderer
        // declares; other translucent fills, then strokes and ink, are composited over the page background.
        let background = page.Background

        let opaque (color: LayoutColor) (under: Color) = { color with color = color.color.CompositeOver under }

        let roots =
            roots
            |> Seq.map (map (fun drawable ->
                let record feature = recorder.Record feature "opaque colour" drawable

                let fill (value: LayoutColor option) (under: LayoutColor option) =
                    match value, under with
                    | Some color, Some under when not color.IsOpaque -> Some(opaque color (under.color.CompositeOver background))
                    | None, Some under -> Some(opaque under background)
                    | Some color, None when not color.IsOpaque && not (has LayoutFeature.FillOpacity) ->
                        record LayoutFeature.FillOpacity
                        Some(opaque color background)
                    | other, _ -> other

                let line (value: LayoutColor) =
                    if not value.IsOpaque && not (has LayoutFeature.StrokeAndTextOpacity) then
                        record LayoutFeature.StrokeAndTextOpacity
                        opaque value background
                    else
                        value

                // The paint's ink is derived from its text or stroke, so it follows them without a degradation of its own.
                let paint (value: LayoutPaint) (under: LayoutColor option) =
                    let ink = if value.ink.IsOpaque || has LayoutFeature.StrokeAndTextOpacity then value.ink else opaque value.ink background
                    { value with fill = fill value.fill under; stroke = value.stroke |> Option.map line; ink = ink }

                let text (value: LayoutText) =
                    { value with
                        ink = line value.ink
                        chip = value.chip |> Option.map (fun chip -> { chip with chipColor = (fill (Some chip.chipColor) None).Value }) }

                match drawable.Kind with
                | DrawableKind.Shape shape ->
                    { drawable with kind = DrawableKind.Shape { shape with paint = paint shape.paint shape.backdrop; text = shape.text |> Option.map text; backdrop = None } }
                | DrawableKind.Line value -> { drawable with kind = DrawableKind.Line { value with paint = paint value.paint None; label = value.label |> Option.map text } }
                | DrawableKind.Text value -> { drawable with kind = DrawableKind.Text(text value) }
                | DrawableKind.Container container ->
                    { drawable with kind = DrawableKind.Container { container with framePaint = paint container.framePaint None; header = container.header |> Option.map text } }
                | _ -> drawable))
            |> ImmutableArray.CreateRange

        recorder.Pass 10
        // 10. Dash patterns become the default dash.
        let roots =
            if has LayoutFeature.DashPatterns then
                roots
            else
                roots
                |> Seq.map (map (fun drawable ->
                    let dash (paint: LayoutPaint) =
                        if Drawables.isBaselineDash paint.dash then
                            paint
                        else
                            recorder.Record LayoutFeature.DashPatterns "default dash" drawable
                            { paint with dash = Drawables.defaultDash }

                    match drawable.Kind with
                    | DrawableKind.Shape shape -> { drawable with kind = DrawableKind.Shape { shape with paint = dash shape.paint } }
                    | DrawableKind.Line line -> { drawable with kind = DrawableKind.Line { line with paint = dash line.paint } }
                    | DrawableKind.Container container -> { drawable with kind = DrawableKind.Container { container with framePaint = dash container.framePaint } }
                    | _ -> drawable))
                |> ImmutableArray.CreateRange

        recorder.Pass 11
        // 11. Links are removed.
        let roots =
            if has LayoutFeature.Links then
                roots
            else
                roots
                |> Seq.map (map (fun drawable ->
                    if drawable.Links.IsEmpty then
                        drawable
                    else
                        recorder.Record LayoutFeature.Links "removed" drawable
                        { drawable with links = ImmutableArray.Empty }))
                |> ImmutableArray.CreateRange

        recorder.Pass 12
        // 12. Paint order is only checked: a unit that the renderer paints together but whose drawables are not
        // consecutive is reported.
        let leaves = Drawables.leaves roots

        for drawable in Drawables.flatten roots do
            match drawable.Kind with
            | DrawableKind.Element _
            | DrawableKind.Group _ when not (has LayoutFeature.InterleavedPainting) && not (Drawables.consecutive leaves drawable) ->
                recorder.Record LayoutFeature.InterleavedPainting "painted together" drawable
            | DrawableKind.Container _ when not (has LayoutFeature.InterleavedContainers) && not (Drawables.consecutive leaves drawable) ->
                recorder.Record LayoutFeature.InterleavedContainers "painted together" drawable
            | _ -> ()

        page.WithDrawables roots, recorder.Degradations()
