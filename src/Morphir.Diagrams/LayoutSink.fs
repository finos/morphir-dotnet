// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open Morphir.Diagrams.Layout
open Morphir.Svg

/// Builds the layout of a page from what its final drawing pass recorded: each primitive the pass emitted, in paint
/// order, becomes part of a drawable, with kinds, owners, routes and roles from the writer's hints and attributes.
[<RequireQualifiedAccess>]
module internal LayoutSink =
    type private Attributes = IReadOnlyDictionary<string, string>

    let private (|Node|Content|) (node: SvgNode) =
        node.Match((fun name attributes children -> Choice1Of2(name, attributes, children)), (fun value -> Choice2Of2 value))

    /// One painted SVG primitive.
    type private Primitive =
        {
            Index: int
            Name: string
            Attributes: Attributes
            Children: ImmutableArray<SvgNode>
            Hints: LayoutHint list
        }

    /// A primitive, or a group that draws one element.
    type private Item =
        | Primitive of Primitive
        | Gathered of reference: string option * attributes: Attributes * hints: LayoutHint list * items: Item list

    /// How a line end binds, before the drawables it names exist.
    type private EndRequest =
        | Candidates of references: string list
        | Lifeline of participant: string
        | NoBinding

    /// A drawable before containment and bindings, with what the conversion still needs about it.
    type private Built =
        {
            Drawable: Drawable
            Classes: string list
            Owner: string option
            Ends: (EndRequest * EndRequest) option
            Lifeline: string option
        }

    let private primitives = set [ "rect"; "circle"; "ellipse"; "polygon"; "polyline"; "line"; "path"; "text"; "use" ]
    let private skipped = set [ "title"; "defs"; "style"; "metadata"; "desc"; "marker"; "symbol"; "clipPath"; "mask" ]

    let private attributesOf (attributes: ImmutableArray<SvgAttribute>) : Attributes =
        let table = Dictionary<string, string>(StringComparer.Ordinal)
        for attribute in attributes do table[attribute.Name.LocalName] <- attribute.Value
        table

    let private get (attributes: Attributes) name =
        match attributes.TryGetValue name with
        | true, value -> Some value
        | false, _ -> None

    let private number (text: string) =
        Decimal.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)

    let private px (attributes: Attributes) name =
        get attributes name |> Option.map (number >> Distance.FromPixelDecimal) |> Option.defaultValue Distance.Zero

    let private classes (attributes: Attributes) =
        get attributes "class" |> Option.map (fun text -> text.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray) |> Option.defaultValue []

    let private textOf (children: ImmutableArray<SvgNode>) =
        children |> Seq.choose (function Content value -> Some value | _ -> None) |> String.concat ""

    /// The colours of one palette, resolved from the CSS variables that writers paint with.
    type Palette(palette: ThemePalette, onFills: bool) =
        let families = ColorFamily.All |> Seq.map (fun family -> family.WireValue, family) |> dict

        member _.Background = palette.Background

        member _.Variable(name: string) : (Color * ColorSource) option =
            let baseRole role (color: Color) = Some(color, ColorSource.Base role)

            match name with
            | "bg" -> baseRole BaseRole.Background palette.Background
            | "text" -> baseRole BaseRole.Text palette.Text
            | "muted" -> baseRole BaseRole.Muted palette.Muted
            | "mask" -> baseRole BaseRole.Mask palette.Mask
            | "arrow" -> baseRole BaseRole.Arrow palette.Arrow
            | "frame" -> baseRole BaseRole.Frame palette.Frame
            | name ->
                let part = name.Substring(name.LastIndexOf '-' + 1)
                let wire = if name.Contains '-' then name.Substring(0, name.LastIndexOf '-') else name

                match families.TryGetValue wire with
                | true, family ->
                    let colors = palette.Family family

                    match part with
                    | "fill" -> Some(colors.Fill, ColorSource.Family family)
                    | "stroke" -> Some(colors.Stroke, ColorSource.Family family)
                    | "ink" -> Some(Theme.resolveInk onFills palette family, ColorSource.Family family)
                    | _ -> None
                | false, _ -> None

    /// Converts one recorded pass.
    type private Converter(capture: LayoutCapture, palette: Palette, warnings: ResizeArray<Diagnostic>) =
        let symbols = Dictionary<string, SvgNode>(StringComparer.Ordinal)
        let ends = Dictionary<DrawableId, EndRequest * EndRequest>()
        let lifelines = Dictionary<string, DrawableId>(StringComparer.Ordinal)
        let mutable counter = 0

        let rec collectSymbols (node: SvgNode) =
            match node with
            | Node(name, attributes, children) ->
                if name.LocalName = "symbol" then
                    match get (attributesOf attributes) "id" with
                    | Some id -> symbols[id] <- node
                    | None -> ()

                for child in children do
                    collectSymbols child
            | Content _ -> ()

        let hintsOf node =
            match capture.Hints.TryGetValue node with
            | true, hints -> hints
            | false, _ -> []

        let rec walk (node: SvgNode) : Item list =
            match node with
            | Content _ -> []
            | Node(name, attributes, children) ->
                let local = name.LocalName
                let attributes = attributesOf attributes
                let hints = hintsOf node

                if skipped.Contains local || name.IsQualified then
                    []
                elif primitives.Contains local then
                    counter <- counter + 1
                    [ Primitive { Index = counter; Name = local; Attributes = attributes; Children = children; Hints = hints } ]
                else
                    let items = children |> Seq.collect walk |> List.ofSeq

                    match get attributes "data-ref", hints with
                    | None, [] -> items
                    | reference, _ -> [ Gathered(reference, attributes, hints, items) ]

        let unreadable (value: string) (element: string) =
            warnings.Add(
                Diagnostic(
                    "layout/unresolved-colour",
                    DiagnosticSeverity.Warning,
                    $"The colour '{value}' of {element} cannot be read as a CSS colour; the layout uses the writer's default colour.",
                    None
                )
            )

        let color (value: string option) (opacity: decimal) : LayoutColor option =
            let effective (color: Color, source) =
                let alpha = Math.Round(color.Opacity * opacity, 6)
                ({ color = Color.FromRgba(color.R, color.G, color.B, alpha); source = source }: LayoutColor)

            match value with
            | None -> None
            | Some "none" -> None
            | Some value when value.StartsWith("var(--", StringComparison.Ordinal) ->
                palette.Variable(value.Substring(6, value.Length - 7)) |> Option.map effective
            | Some "currentColor" -> None
            | Some value ->
                match CssColors.read value with
                | Some color -> Some(effective (color, ColorSource.Authored))
                | None ->
                    unreadable value "an element"
                    None

        let opacity (attributes: Attributes) name =
            let own = get attributes name |> Option.map number |> Option.defaultValue 1m
            let all = get attributes "opacity" |> Option.map number |> Option.defaultValue 1m
            own * all

        let paint (primitive: Primitive) (defaultFill: string option) : LayoutPaint =
            let attributes = primitive.Attributes
            for hint in primitive.Hints do
                match hint with
                | LayoutHint.Unresolved(value, element) -> unreadable value element
                | _ -> ()

            let fill = color (get attributes "fill" |> Option.orElse defaultFill) (opacity attributes "fill-opacity")
            let stroke = color (get attributes "stroke") (opacity attributes "stroke-opacity")

            let dash =
                match get attributes "stroke-dasharray" with
                | Some text when text <> "none" ->
                    text.Split([| ' '; ',' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (number >> Distance.FromPixelDecimal)
                    |> ImmutableArray.CreateRange
                | _ -> ImmutableArray.Empty

            let cap =
                match get attributes "stroke-linecap" with
                | Some "round" -> LineCap.Round
                | Some "square" -> LineCap.Square
                | _ -> LineCap.Butt

            let join =
                match get attributes "stroke-linejoin" with
                | Some "round" -> LineJoin.Round
                | Some "bevel" -> LineJoin.Bevel
                | _ -> LineJoin.Miter

            let ink =
                stroke |> Option.orElse fill |> Option.defaultValue ({ color = palette.Background; source = ColorSource.Base BaseRole.Background }: LayoutColor)

            {
                fill = fill
                stroke = stroke
                ink = ink
                strokeWidth = (if stroke.IsSome then (get attributes "stroke-width" |> Option.map (number >> Distance.FromPixelDecimal) |> Option.defaultValue (Distance.FromPixels 1.0)) else Distance.Zero)
                dash = dash
                cap = cap
                join = join
            }

        let point (x: Distance) (y: Distance) = LayoutPoint.Create(x, y)

        let drawable (index: int) (reference: string option) (kind: DrawableKind) : Drawable =
            {
                id = DrawableId.OfIndex index
                reference = reference |> Option.bind (fun text -> match DiagramElementRef.Parse text with | Ok reference -> Some reference | Error _ -> None)
                paintOrder = index
                kind = kind
                links = ImmutableArray.Empty
            }

        let built primitive kind =
            {
                Drawable = drawable primitive.Index None kind
                Classes = classes primitive.Attributes
                Owner = primitive.Hints |> List.tryPick (function LayoutHint.Owner owner -> Some owner | _ -> None)
                Ends = None
                Lifeline = primitive.Hints |> List.tryPick (function LayoutHint.Lifeline participant -> Some participant | _ -> None)
            }

        let shape primitive kind box =
            let role = primitive.Hints |> List.tryPick (function LayoutHint.Role role -> Some role | _ -> None)
            let shape: LayoutShape = { kind = kind; box = box; paint = paint primitive (Some "#000000"); text = None; role = role; backdrop = None }
            built primitive (DrawableKind.Shape shape)

        let text (primitive: Primitive) : LayoutText =
            let attributes = primitive.Attributes
            let x = px attributes "x"
            let y = px attributes "y"
            let size = px attributes "font-size"
            let tspans = primitive.Children |> Seq.choose (function Node(name, attributes, children) when name.LocalName = "tspan" -> Some(attributesOf attributes, children) | _ -> None) |> Seq.toArray

            let lines, first, advance =
                if tspans.Length = 0 then
                    ImmutableArray.Create(textOf primitive.Children), Distance.Zero, size + Distance.FromPixels 3.0
                else
                    let dy index = px (fst tspans[index]) "dy"
                    tspans |> Array.map (snd >> textOf) |> ImmutableArray.CreateRange, dy 0, (if tspans.Length > 1 then dy 1 else size + Distance.FromPixels 3.0)

            let align =
                match get attributes "text-anchor" with
                | Some "middle" -> TextAlign.Middle
                | Some "end" -> TextAlign.End
                | _ -> TextAlign.Start

            let rotation =
                match get attributes "transform" with
                | Some transform when transform.StartsWith("rotate(", StringComparison.Ordinal) ->
                    let angle = number (transform.Substring(7).Split([| ' '; ','; ')' |], StringSplitOptions.RemoveEmptyEntries)[0])
                    int ((angle % 360m + 360m) % 360m)
                | _ -> 0

            let weight = get attributes "font-weight" |> Option.map (number >> int) |> Option.defaultValue 400
            let pixels = int (Math.Round(size.ToPixels()))
            let width = lines |> Seq.map (SvgWriting.textWidth pixels) |> Seq.fold max 0 |> float |> Distance.FromPixels

            let left =
                match align with
                | TextAlign.Start -> x
                | TextAlign.Middle -> x - Distance.FromQuarterEmu(width.QuarterEmu / 2L)
                | TextAlign.End -> x - width

            let origin = point x (y + first)
            let height = advance * float (lines.Length - 1) + size * 1.25
            let ink = color (get attributes "fill" |> Option.orElse (Some "#000000")) (opacity attributes "fill-opacity")

            {
                lines = lines
                origin = origin
                advance = advance
                align = align
                size = size
                weight = weight
                rotation = rotation
                box = LayoutBox.Create(left, origin.Y - size, width, height)
                wraps = primitive.Hints |> List.contains LayoutHint.Wraps
                ink = ink |> Option.defaultValue ({ color = palette.Background; source = ColorSource.Authored }: LayoutColor)
                chip = None
            }

        let pathPoints (path: PathData) =
            path.Commands |> Seq.choose (fun command -> command.End) |> Seq.toList

        let head (attributes: Attributes) name =
            match get attributes name with
            | Some reference ->
                let id = reference.Replace("url(#", "").TrimEnd(')')

                [ EdgeHead.HalfUpperFilled; EdgeHead.HalfLowerFilled; EdgeHead.HalfUpper; EdgeHead.HalfLower; EdgeHead.OpenArrow; EdgeHead.Arrow; EdgeHead.Circle; EdgeHead.Cross; EdgeHead.Async ]
                |> List.tryFind (fun head -> id.Contains($"-{head.WireValue}-", StringComparison.Ordinal))
                |> Option.defaultValue EdgeHead.None
            | None -> EdgeHead.None

        let line (primitive: Primitive) (path: PathData) (requests: (EndRequest * EndRequest) option) =
            let points = pathPoints path
            let first, last = List.head points, List.last points

            let waypoints, style =
                match primitive.Hints |> List.tryPick (function LayoutHint.Route(points, style) -> Some(points, style) | _ -> None) with
                | Some(points, style) ->
                    points
                    |> List.map (fun (x, y) -> point (Distance.FromPixels x) (Distance.FromPixels y))
                    |> List.fold (fun (kept: LayoutPoint list) (p: LayoutPoint) ->
                        match kept with
                        | last :: _ when last.Equals p -> kept
                        // A point in the middle of a straight run is not a bend.
                        | last :: before :: rest when
                            (last.X - before.X).QuarterEmu * (p.Y - last.Y).QuarterEmu = (last.Y - before.Y).QuarterEmu * (p.X - last.X).QuarterEmu ->
                            p :: before :: rest
                        | _ -> p :: kept) []
                    |> List.rev
                    |> fun points -> points, (if points.Length <= 2 && style = RouteStyle.Orthogonal then RouteStyle.Straight else style)
                | None ->
                    let deduped = points |> List.fold (fun kept (p: LayoutPoint) -> match kept with | last :: _ when last.Equals p -> kept | _ -> p :: kept) [] |> List.rev
                    let curved = path.Commands |> Seq.exists (function PathCommand.CubicTo _ | PathCommand.QuadraticTo _ | PathCommand.ArcTo _ -> true | _ -> false)
                    let orthogonal = deduped |> List.pairwise |> List.forall (fun (a, b) -> a.X.Equals b.X || a.Y.Equals b.Y)

                    let style =
                        if curved then RouteStyle.Curved
                        elif deduped.Length <= 2 then RouteStyle.Straight
                        elif orthogonal then RouteStyle.Orthogonal
                        else RouteStyle.Straight

                    (if curved then [ first; last ] else deduped), style

            let paint = paint primitive None
            let ends' (head: EdgeHead) p : LineEnd = { point = p; head = head; binding = None }

            let layout: LayoutLine =
                {
                    source = ends' (head primitive.Attributes "marker-start") first
                    target = ends' (head primitive.Attributes "marker-end") last
                    waypoints = ImmutableArray.CreateRange waypoints
                    route = style
                    exactPath = Some path
                    paint = paint
                    label = None
                }

            let result = built primitive (DrawableKind.Line layout)
            requests |> Option.iter (fun requests -> ends[result.Drawable.id] <- requests)
            result.Lifeline |> Option.iter (fun participant -> lifelines[participant] <- result.Drawable.id)
            result

        let endsOf (primitive: Primitive) (unitReference: string option) =
            let attributes = primitive.Attributes

            let node id = [ DiagramElementRef.Node(DiagramId.Create id).ToString(); DiagramElementRef.Group(DiagramId.Create id).ToString() ]

            match get attributes "data-source", get attributes "data-target", get attributes "data-sender", get attributes "data-receiver" with
            | Some source, Some target, _, _ -> Some(Candidates(node source), Candidates(node target))
            | _, _, Some sender, Some receiver -> Some(Lifeline sender, Lifeline receiver)
            | _ ->
                match unitReference |> Option.map DiagramElementRef.Parse with
                | Some(Ok reference) ->
                    reference.Match(
                        (fun _ -> None),
                        (fun _ -> None),
                        (fun source target _ -> Some(Candidates(node source.Value), Candidates(node target.Value))),
                        (fun _ -> None),
                        (fun sender receiver _ -> Some(Lifeline sender.Value, Lifeline receiver.Value)),
                        (fun _ -> None),
                        (fun _ -> None),
                        (fun _ -> None)
                    )
                | _ -> None

        let image (primitive: Primitive) =
            let attributes = primitive.Attributes
            let box = LayoutBox.Create(px attributes "x", px attributes "y", px attributes "width", px attributes "height")
            let id = (get attributes "href" |> Option.defaultValue "").TrimStart('#')
            let ink = color (get attributes "color") 1m |> Option.map (fun color -> color.Color.ToCss()) |> Option.defaultValue "#000000"

            let rec resolve (node: SvgNode) =
                match node with
                | Content _ -> node
                | Node(name, attributes, children) ->
                    let attributes =
                        attributes
                        |> Seq.map (fun attribute -> if attribute.Value = "currentColor" then SvgAttribute.Create(attribute.Name, ink) else attribute)
                        |> Seq.toArray

                    SvgNode.CreateElement(name, attributes, children |> Seq.map resolve |> Seq.toArray)

            let document =
                match symbols.TryGetValue id with
                | true, Node(_, symbolAttributes, children) ->
                    let painted =
                        symbolAttributes
                        |> Seq.filter (fun attribute -> attribute.Name.LocalName <> "id")
                        |> Seq.map (fun attribute -> if attribute.Value = "currentColor" then SvgAttribute.Create(attribute.Name, ink) else attribute)

                    SvgDocument.Create(
                        [|
                            yield! painted
                            SvgAttribute.Create("width", get attributes "width" |> Option.defaultValue "0")
                            SvgAttribute.Create("height", get attributes "height" |> Option.defaultValue "0")
                        |],
                        children |> Seq.map resolve |> Seq.toArray
                    )
                | _ -> SvgDocument.Create([||], [||])

            built primitive (DrawableKind.Image(LayoutImage(box, document)))

        let convert (primitive: Primitive) (unit: (string option * Attributes) option) (firstOpenPath: bool ref) : Built =
            let attributes = primitive.Attributes
            let unitShape = unit |> Option.bind (fun (_, attributes) -> get attributes "data-shape")
            let unitReference = unit |> Option.bind fst
            let outline = classes attributes |> List.exists (fun name -> name = "outline" || name = "box")

            match primitive.Name with
            | "rect" ->
                let box = LayoutBox.Create(px attributes "x", px attributes "y", px attributes "width", px attributes "height")
                let radius = px attributes "rx"
                shape primitive (if radius.QuarterEmu > 0L then ShapeKind.RoundedRectangle radius else ShapeKind.Rectangle) box
            | "circle" ->
                let r = px attributes "r"
                let diameter = r + r
                shape primitive ShapeKind.Ellipse (LayoutBox.Create(px attributes "cx" - r, px attributes "cy" - r, diameter, diameter))
            | "ellipse" ->
                let rx, ry = px attributes "rx", px attributes "ry"
                shape primitive ShapeKind.Ellipse (LayoutBox.Create(px attributes "cx" - rx, px attributes "cy" - ry, rx + rx, ry + ry))
            | "polygon"
            | "polyline" ->
                let values = (get attributes "points" |> Option.defaultValue "").Split([| ' '; ',' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map (number >> Distance.FromPixelDecimal)
                let points = values |> Array.chunkBySize 2 |> Array.map (fun pair -> point pair[0] pair[1]) |> List.ofArray

                let path =
                    PathData.OfCommands(
                        [
                            yield! points |> List.mapi (fun index p -> if index = 0 then PathCommand.MoveTo p else PathCommand.LineTo p)
                            if primitive.Name = "polygon" then PathCommand.Close
                        ]
                    )

                let box = points |> List.tail |> List.fold (fun (box: LayoutBox) p -> box.Include p) (LayoutBox.At(List.head points))

                let kind =
                    match unitShape, points with
                    | Some "diamond", [ _; _; _; _ ] when outline -> ShapeKind.Diamond
                    | Some "hexagon", [ first; second; _; _; _; _ ] when outline -> ShapeKind.Hexagon(second.X - first.X)
                    | _ -> ShapeKind.Path path

                shape primitive kind box
            | "line" ->
                let path = PathData.OfCommands [ PathCommand.MoveTo(point (px attributes "x1") (px attributes "y1")); PathCommand.LineTo(point (px attributes "x2") (px attributes "y2")) ]
                line primitive path (endsOf primitive unitReference)
            | "path" ->
                let path = PathData.Create(get attributes "d" |> Option.defaultValue "")
                let closed = path.Commands |> Seq.exists (fun command -> command = PathCommand.Close)
                let subpaths = path.Commands |> Seq.filter (function PathCommand.MoveTo _ -> true | _ -> false) |> Seq.length
                let names = classes attributes
                let routed = primitive.Hints |> List.exists (function LayoutHint.Route _ | LayoutHint.Lifeline _ -> true | _ -> false)
                let marked = (get attributes "marker-start").IsSome || (get attributes "marker-end").IsSome
                let named = names |> List.exists (fun name -> name = "connector" || name = "m-line" || name = "lifeline" || name = "track")
                let edgeUnit = unitReference |> Option.exists (fun reference -> reference.StartsWith("edge=", StringComparison.Ordinal) || reference.StartsWith("message=", StringComparison.Ordinal))
                let fill = get attributes "fill"

                if not closed && subpaths = 1 && (routed || marked || named || (edgeUnit && firstOpenPath.Value && fill = Some "none")) then
                    if edgeUnit then firstOpenPath.Value <- false
                    line primitive path (endsOf primitive unitReference)
                else
                    let points = pathPoints path
                    let box = points |> List.tail |> List.fold (fun (box: LayoutBox) p -> box.Include p) (LayoutBox.At(List.head points))

                    let kind =
                        match unitShape, points with
                        | _, [ a; b; c; d ] when outline && closed && a.X.Equals c.X && b.Y.Equals d.Y -> ShapeKind.Diamond
                        | _ -> ShapeKind.Path path

                    shape primitive kind box
            | "text" -> built primitive (DrawableKind.Text(text primitive))
            | _ -> image primitive

        let inside (box: LayoutBox) (p: LayoutPoint) =
            p.X.CompareTo box.X >= 0 && p.X.CompareTo box.Right <= 0 && p.Y.CompareTo box.Y >= 0 && p.Y.CompareTo box.Bottom <= 0

        /// Joins adjacent drawables that stand for one thing: first an underlay and its box, a chip and its text, and a
        /// cylinder's body and its cap; then a body shape and the text inside it, and a line and its label.
        let combine (unitShape: string option) (items: Built list) =
            // An underlay is a mask-coloured rectangle with no stroke; label chips also carry the mask class.
            let underlay (item: Built) =
                List.contains "mask" item.Classes
                || (match item.Drawable.Kind with
                    | DrawableKind.Shape shape -> shape.paint.stroke.IsNone && (match shape.paint.fill with Some fill -> fill.source = ColorSource.Base BaseRole.Mask | None -> false)
                    | _ -> false)

            let rec underlays (items: Built list) =
                match items with
                | mask :: next :: rest when underlay mask ->
                    match mask.Drawable.Kind, next.Drawable.Kind with
                    | DrawableKind.Shape underlay, DrawableKind.Shape body when underlay.Box.Equals body.Box ->
                        let merged = { body with backdrop = underlay.paint.fill }
                        underlays ({ next with Drawable = { next.Drawable with id = mask.Drawable.id; paintOrder = mask.Drawable.paintOrder; kind = DrawableKind.Shape merged } } :: rest)
                    | DrawableKind.Shape chip, DrawableKind.Text label ->
                        let radius = match chip.kind with ShapeKind.RoundedRectangle radius -> radius | _ -> Distance.Zero
                        let label =
                            match chip.paint.fill with
                            | Some color -> { label with chip = Some { chipColor = color; chipBox = chip.box; chipRadius = radius } }
                            | None -> label
                        underlays ({ next with Drawable = { next.Drawable with id = mask.Drawable.id; paintOrder = mask.Drawable.paintOrder; kind = DrawableKind.Text label } } :: rest)
                    | _ -> mask :: underlays (next :: rest)
                | body :: cap :: rest when unitShape = Some "cylinder" && List.contains "decoration" cap.Classes ->
                    match body.Drawable.Kind, cap.Drawable.Kind with
                    | DrawableKind.Shape shape, DrawableKind.Shape top when top.kind = ShapeKind.Ellipse ->
                        let cylinder = { shape with kind = ShapeKind.Cylinder(Distance.FromQuarterEmu(top.box.Height.QuarterEmu / 2L)) }
                        underlays ({ body with Drawable = { body.Drawable with kind = DrawableKind.Shape cylinder } } :: rest)
                    | _ -> body :: underlays (cap :: rest)
                | first :: rest -> first :: underlays rest
                | [] -> []

            let body (item: Built) = item.Classes |> List.exists (fun name -> name = "outline" || name = "box") || item.Classes.IsEmpty

            let rec labels (items: Built list) =
                match items with
                | first :: next :: rest ->
                    match first.Drawable.Kind, next.Drawable.Kind with
                    | DrawableKind.Shape shape, DrawableKind.Text label when body first && shape.text.IsNone && label.chip.IsNone && inside shape.box label.origin ->
                        labels ({ first with Drawable = { first.Drawable with kind = DrawableKind.Shape { shape with text = Some label } } } :: rest)
                    | DrawableKind.Line line, DrawableKind.Text label when line.label.IsNone && (label.chip.IsSome || ends.ContainsKey first.Drawable.id) ->
                        labels ({ first with Drawable = { first.Drawable with kind = DrawableKind.Line { line with label = Some label } } } :: rest)
                    | _ -> first :: labels (next :: rest)
                | other -> other

            items |> underlays |> labels

        let rec items (unit: (string option * Attributes) option) (list: Item list) : Built list =
            let firstOpenPath = ref true
            let unitShape = unit |> Option.bind (fun (_, attributes) -> get attributes "data-shape")

            list
            |> List.map (fun item ->
                match item with
                | Primitive primitive -> convert primitive unit firstOpenPath
                | Gathered(reference, attributes, hints, children) -> unitDrawable reference attributes hints children)
            |> combine unitShape

        and unitDrawable reference attributes hints children : Built =
            let built = items (Some(reference, attributes)) children
            let owner = hints |> List.tryPick (function LayoutHint.Owner owner -> Some owner | _ -> None)
            let role = hints |> List.tryPick (function LayoutHint.Role role -> Some role | _ -> None)

            let withRole (drawable: Drawable) =
                match role, drawable.Kind with
                | Some role, DrawableKind.Shape shape -> { drawable with kind = DrawableKind.Shape { shape with role = Some role } }
                | _ -> drawable

            let parsed = reference |> Option.bind (fun text -> match DiagramElementRef.Parse text with | Ok reference -> Some reference | Error _ -> None)

            if hints |> List.contains LayoutHint.Container then
                let frame = built |> List.tryFind (fun item -> match item.Drawable.Kind with DrawableKind.Shape _ -> true | _ -> false)
                match frame with
                | Some frame ->
                    let shape = match frame.Drawable.Kind with DrawableKind.Shape shape -> shape | _ -> invalidOp "A frame is a shape."
                    let after = built |> List.filter (fun item -> not (Object.ReferenceEquals(item, frame)))

                    // A vertical line across the frame, in the frame's stroke, divides a header band from the body.
                    let band, after =
                        match after with
                        | divider :: others ->
                            match divider.Drawable.Kind with
                            | DrawableKind.Shape line ->
                                match line.kind with
                                | ShapeKind.Path path ->
                                    match List.ofSeq path.Commands with
                                    | [ PathCommand.MoveTo top; PathCommand.LineTo bottom ] when
                                        top.X.Equals bottom.X && top.Y.Equals shape.box.Y && bottom.Y.Equals shape.box.Bottom
                                        && line.paint.stroke = shape.paint.stroke && line.paint.strokeWidth.Equals shape.paint.strokeWidth ->
                                        top.X - shape.box.X, others
                                    | _ -> Distance.Zero, after
                                | _ -> Distance.Zero, after
                            | _ -> Distance.Zero, after
                        | [] -> Distance.Zero, after

                    // The header is the text painted straight after the frame, or the text inside it.
                    let headerText, rest =
                        match shape.text, after with
                        | Some text, _ -> Some text, after
                        | None, next :: others ->
                            match next.Drawable.Kind with
                            | DrawableKind.Text text -> Some text, others
                            | _ -> None, after
                        | None, [] -> None, after

                    let container: LayoutContainer =
                        {
                            frame = shape.box
                            header = headerText
                            framePaint = shape.paint
                            radius = (match shape.kind with ShapeKind.RoundedRectangle radius -> radius | _ -> Distance.Zero)
                            band = band
                            children = rest |> List.map (fun item -> item.Drawable) |> ImmutableArray.CreateRange
                        }

                    { frame with Drawable = { frame.Drawable with reference = parsed; kind = DrawableKind.Container container }; Owner = owner; Classes = [] }
                | None -> invalidOp "A container needs a frame."
            else
                match built with
                | [ single ] -> { single with Drawable = withRole { single.Drawable with reference = parsed }; Owner = owner |> Option.orElse single.Owner }
                | _ ->
                    // A role belongs to the element's first shape, its body.
                    let body = built |> List.tryFindIndex (fun item -> match item.Drawable.Kind with DrawableKind.Shape _ -> true | _ -> false)
                    let children = built |> List.mapi (fun index item -> if Some index = body then withRole item.Drawable else item.Drawable) |> ImmutableArray.CreateRange
                    let first = if children.IsEmpty then counter + 1 else children |> Seq.map (fun child -> child.PaintOrder) |> Seq.min

                    {
                        Drawable = { id = DrawableId.Create($"e{first}"); reference = parsed; paintOrder = first; kind = DrawableKind.Element children; links = ImmutableArray.Empty }
                        Classes = []
                        Owner = owner
                        Ends = None
                        Lifeline = None
                    }

        /// The end requests of each line, by its identifier.
        member _.Ends = ends :> IReadOnlyDictionary<_, _>

        /// The lifeline of each participant.
        member _.Lifelines = lifelines :> IReadOnlyDictionary<_, _>

        member _.Convert(root: SvgNode) =
            collectSymbols root

            match root with
            | Node(_, attributes, children) ->
                let attributes = attributesOf attributes

                let viewBox =
                    (get attributes "viewBox" |> Option.defaultValue "0 0 0 0").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (number >> Distance.FromPixelDecimal)

                let bounds = LayoutBox.Create(viewBox[0], viewBox[1], viewBox[2], viewBox[3])
                let walked = children |> Seq.collect walk |> List.ofSeq

                // The page background is the layout's background colour, not a drawable.
                let walked =
                    match walked with
                    | Primitive background :: rest when background.Name = "rect" && get background.Attributes "fill" = Some "var(--bg)" -> rest
                    | other -> other

                bounds, items None walked
            | Content _ -> invalidOp "A layout is captured from an SVG element."

    /// Places each drawable under the drawable that owns it, then binds line ends to the drawables they name.
    let private assemble (capture: LayoutCapture) (owners: (string * string) list) (ends: IReadOnlyDictionary<DrawableId, EndRequest * EndRequest>) (lifelineIds: IReadOnlyDictionary<string, DrawableId>) (built: Built list) : ImmutableArray<Drawable> =
        let all = built |> List.map (fun item -> item.Drawable.id, item) |> dict
        let byReference = Dictionary<string, ResizeArray<Built>>(StringComparer.Ordinal)

        for item in built do
            match item.Drawable.reference with
            | Some reference ->
                let text = reference.ToString()
                if not (byReference.ContainsKey text) then byReference[text] <- ResizeArray()
                byReference[text].Add item
            | None -> ()

        let ownerTable = Dictionary<string, string>(StringComparer.Ordinal)
        for child, owner in owners do ownerTable[child] <- owner

        let parentOf (item: Built) =
            let owner =
                item.Owner
                |> Option.orElse (item.Drawable.reference |> Option.bind (fun reference -> match ownerTable.TryGetValue(reference.ToString()) with | true, owner -> Some owner | false, _ -> None))

            owner |> Option.bind (fun owner -> match byReference.TryGetValue owner with | true, items -> Some items[0].Drawable.id | false, _ -> None)

        let parents = built |> List.map (fun item -> item.Drawable.id, parentOf item) |> dict
        let childrenOf = built |> List.groupBy (fun item -> parents[item.Drawable.id]) |> dict

        let rec nest (item: Built) : Drawable =
            let extra =
                match childrenOf.TryGetValue(Some item.Drawable.id) with
                | true, children -> children |> List.map nest
                | false, _ -> []

            if extra.IsEmpty then
                item.Drawable
            else
                let sorted (existing: ImmutableArray<Drawable>) =
                    Seq.append existing extra |> Seq.sortBy (fun drawable -> drawable.PaintOrder) |> ImmutableArray.CreateRange

                match item.Drawable.Kind with
                | DrawableKind.Container container -> { item.Drawable with kind = DrawableKind.Container { container with children = sorted container.children } }
                | DrawableKind.Element children -> { item.Drawable with kind = DrawableKind.Element(sorted children) }
                | _ ->
                    let first = item.Drawable.paintOrder
                    let inner = { item.Drawable with reference = None }
                    { id = DrawableId.Create($"e{first}"); reference = item.Drawable.reference; paintOrder = first; kind = DrawableKind.Element(sorted (ImmutableArray.Create inner)); links = ImmutableArray.Empty }

        let roots =
            match childrenOf.TryGetValue None with
            | true, roots -> roots |> List.map nest
            | false, _ -> []

        // Bindings, now that every drawable has its place.
        let flat = Drawables.flatten (ImmutableArray.CreateRange roots) |> Seq.toArray
        let byRef = Dictionary<string, ResizeArray<Drawable>>(StringComparer.Ordinal)

        for drawable in flat do
            match drawable.reference with
            | Some reference ->
                let text = reference.ToString()
                if not (byRef.ContainsKey text) then byRef[text] <- ResizeArray()
                byRef[text].Add drawable
            | None -> ()

        let byId = flat |> Seq.map (fun drawable -> drawable.id, drawable) |> dict
        let lifelines = lifelineIds |> Seq.choose (fun pair -> match byId.TryGetValue pair.Value with | true, drawable -> Some(pair.Key, drawable) | false, _ -> None) |> dict

        let heads =
            flat
            |> Seq.choose (fun drawable ->
                match drawable.reference, drawable.Kind with
                | Some reference, DrawableKind.Element children when reference.Kind = DiagramElementKind.Participant ->
                    children |> Seq.tryFind (fun child -> match child.Kind with DrawableKind.Shape _ -> true | _ -> false) |> Option.map (fun head -> reference.ToString(), head)
                | _ -> None)
            |> dict

        let boxOf (drawable: Drawable) =
            match drawable.Kind with
            | DrawableKind.Shape shape -> Some shape.box
            | DrawableKind.Image image -> Some image.Box
            | DrawableKind.Container container -> Some container.frame
            | DrawableKind.Element children ->
                children |> Seq.choose (fun child -> match child.Kind with DrawableKind.Shape shape -> Some shape.box | _ -> None) |> Seq.tryHead
            | _ -> None

        let fraction (value: Distance) (start: Distance) (length: Distance) =
            if length.QuarterEmu = 0L then Fraction.Half
            else Fraction.Create(Math.Clamp(float (value - start).QuarterEmu / float length.QuarterEmu, 0.0, 1.0))

        let across (target: Drawable) (box: LayoutBox) (p: LayoutPoint) =
            Some({ target = target.id; anchor = LineAnchor.Across(FractionPoint.Create(fraction p.X box.X box.Width, fraction p.Y box.Y box.Height)) }: LineBinding)

        let distanceTo (box: LayoutBox) (p: LayoutPoint) =
            let clamp (value: Distance) (low: Distance) (high: Distance) = Distance.Max(low, Distance.Min(value, high))
            let dx = (p.X - clamp p.X box.X box.Right).ToPixels()
            let dy = (p.Y - clamp p.Y box.Y box.Bottom).ToPixels()
            dx * dx + dy * dy

        let bind (request: EndRequest) (p: LayoutPoint) : LineBinding option =
            match request with
            | NoBinding -> None
            | Candidates references ->
                references
                |> List.collect (fun reference -> match byRef.TryGetValue reference with | true, items -> List.ofSeq items | false, _ -> [])
                |> List.choose (fun target ->
                    // An element binds through its first shape, its body.
                    let target =
                        match target.Kind with
                        | DrawableKind.Element children -> children |> Seq.tryFind (fun child -> match child.Kind with DrawableKind.Shape _ -> true | _ -> false)
                        | _ -> Some target

                    target |> Option.bind (fun target -> boxOf target |> Option.map (fun box -> target, box)))
                |> List.sortBy (fun (_, box) -> distanceTo box p)
                |> List.tryHead
                |> Option.bind (fun (target, box) -> across target box p)
            | Lifeline participant ->
                match lifelines.TryGetValue participant with
                | true, lifeline ->
                    match lifeline.Kind with
                    | DrawableKind.Line line ->
                        let start, finish = line.source.point, line.target.point

                        if p.Y.CompareTo start.Y >= 0 && p.Y.CompareTo finish.Y <= 0 then
                            Some({ target = lifeline.id; anchor = LineAnchor.Along(fraction p.Y start.Y (finish.Y - start.Y)) }: LineBinding)
                        else
                            // A creation message ends at the side of the new participant's head, before its lifeline starts.
                            let participantRef = DiagramElementRef.Participant(DiagramId.Create participant).ToString()

                            match heads.TryGetValue participantRef with
                            | true, head -> boxOf head |> Option.bind (fun box -> across head box p)
                            | false, _ -> None
                    | _ -> None
                | false, _ -> None

        let rec bindAll (drawable: Drawable) : Drawable =
            match drawable.Kind with
            | DrawableKind.Line line ->
                match ends.TryGetValue drawable.id with
                | true, (source, target) ->
                    let line =
                        { line with
                            source = { line.source with binding = bind source line.source.point }
                            target = { line.target with binding = bind target line.target.point } }

                    { drawable with kind = DrawableKind.Line line }
                | false, _ -> drawable
            | DrawableKind.Element children -> { drawable with kind = DrawableKind.Element(children |> Seq.map bindAll |> ImmutableArray.CreateRange) }
            | DrawableKind.Group children -> { drawable with kind = DrawableKind.Group(children |> Seq.map bindAll |> ImmutableArray.CreateRange) }
            | DrawableKind.Container container -> { drawable with kind = DrawableKind.Container { container with children = container.children |> Seq.map bindAll |> ImmutableArray.CreateRange } }
            | _ -> drawable

        ignore all
        let roles = Dictionary<string, FamilyRole>(StringComparer.Ordinal)
        for reference, role in capture.Roles do roles[reference] <- role

        let rec withRoles (drawable: Drawable) : Drawable =
            let role = drawable.reference |> Option.bind (fun reference -> match roles.TryGetValue(reference.ToString()) with | true, role -> Some role | false, _ -> None)

            let drawable =
                match role, drawable.Kind with
                | Some role, DrawableKind.Shape shape -> { drawable with kind = DrawableKind.Shape { shape with role = Some role } }
                | Some role, DrawableKind.Element children ->
                    // An element's role belongs to its first shape, its body.
                    match children |> Seq.tryFindIndex (fun child -> match child.Kind with DrawableKind.Shape _ -> true | _ -> false) with
                    | Some index ->
                        let body = children[index]
                        let shape = match body.Kind with DrawableKind.Shape shape -> shape | _ -> invalidOp "A body is a shape."
                        { drawable with kind = DrawableKind.Element(children.SetItem(index, { body with kind = DrawableKind.Shape { shape with role = Some role } })) }
                    | None -> drawable
                | _ -> drawable

            match drawable.Kind with
            | DrawableKind.Element children -> { drawable with kind = DrawableKind.Element(children |> Seq.map withRoles |> ImmutableArray.CreateRange) }
            | DrawableKind.Group children -> { drawable with kind = DrawableKind.Group(children |> Seq.map withRoles |> ImmutableArray.CreateRange) }
            | DrawableKind.Container container -> { drawable with kind = DrawableKind.Container { container with children = container.children |> Seq.map withRoles |> ImmutableArray.CreateRange } }
            | _ -> drawable

        roots |> List.map (bindAll >> withRoles) |> ImmutableArray.CreateRange

    /// The layout of a scene's page, with any warnings, in the palette of the scene's mode (auto reads as light).
    let layout (scene: DiagramScene) : DiagramLayout * Diagnostic list =
        match scene.Capture with
        | None -> invalidOp "The scene was not drawn by a writer that records its layout."
        | Some capture ->
            let options = scene.Options
            let theme = options.Theme
            let palettes = if options.Mode = ThemeMode.Dark then theme.Dark else theme.Light
            let palette = Palette(palettes, capture.OnFills)
            let warnings = ResizeArray<Diagnostic>()
            let converter = Converter(capture, palette, warnings)
            let bounds, built = converter.Convert capture.Root
            let roots = assemble capture capture.Owners converter.Ends converter.Lifelines built

            // Each referenced drawable carries the links the metadata projection gives its reference, in its order.
            let links =
                scene.Annotations
                |> List.map (fun (reference, items) ->
                    reference,
                    items
                    |> List.filter (fun item -> item.Kind = "link")
                    |> List.map (fun item -> ({ predicate = item.Predicate; linkTarget = item.Target; isWeb = item.IsWeb }: DrawableLink))
                    |> ImmutableArray.CreateRange)
                |> List.filter (fun (_, items) -> not items.IsEmpty)
                |> dict

            let rec link (drawable: Drawable) : Drawable =
                let drawable =
                    match drawable.reference with
                    | Some reference ->
                        match links.TryGetValue(reference.ToString()) with
                        | true, items -> { drawable with links = items }
                        | false, _ -> drawable
                    | None -> drawable

                match drawable.Kind with
                | DrawableKind.Element children -> { drawable with kind = DrawableKind.Element(children |> Seq.map link |> ImmutableArray.CreateRange) }
                | DrawableKind.Group children -> { drawable with kind = DrawableKind.Group(children |> Seq.map link |> ImmutableArray.CreateRange) }
                | DrawableKind.Container container -> { drawable with kind = DrawableKind.Container { container with children = container.children |> Seq.map link |> ImmutableArray.CreateRange } }
                | _ -> drawable

            let roots = if links.Count = 0 then roots else roots |> Seq.map link |> ImmutableArray.CreateRange
            DiagramLayout(bounds, palette.Background, roots, scene.Document), List.ofSeq warnings
