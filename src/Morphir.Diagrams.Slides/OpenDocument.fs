// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Slides

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Xml
open System.Xml.Linq
open Morphir.Diagrams
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout

/// Names, units and number formats of OpenDocument.
[<RequireQualifiedAccess>]
module internal Odf =
    let office = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:office:1.0"
    let style = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:style:1.0"
    let text = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:text:1.0"
    let draw = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
    let fo = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"
    let xlink = XNamespace.Get "http://www.w3.org/1999/xlink"
    let svg = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
    let presentation = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:presentation:1.0"
    let script = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:script:1.0"
    let dom = XNamespace.Get "http://www.w3.org/2001/xml-events"
    let manifest = XNamespace.Get "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"
    let xml = XNamespace.Xml

    /// The media type of a presentation.
    let mediaType = "application/vnd.oasis.opendocument.presentation"

    /// The ODF version written.
    let version = "1.3"

    /// The prefix declarations of the content and styles documents.
    let declarations =
        [
            "office", office
            "style", style
            "text", text
            "draw", draw
            "fo", fo
            "xlink", xlink
            "svg", svg
            "presentation", presentation
            "script", script
            "dom", dom
        ]
        |> List.map (fun (prefix, ns) -> XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName) :> obj)

    let integer (value: int64) =
        value.ToString(CultureInfo.InvariantCulture)

    /// A distance in thousandths of a millimetre, rounded half to even.
    let micrometres (distance: Distance) =
        int64 (Math.Round(decimal distance.QuarterEmu / 144m, MidpointRounding.ToEven))

    /// A distance in hundredths of a millimetre, the unit of a connector's path, rounded half to even.
    let hundredths (distance: Distance) =
        int64 (Math.Round(decimal distance.QuarterEmu / 1440m, MidpointRounding.ToEven))

    let private millimetres (thousandths: int64) =
        let sign = if thousandths < 0L then "-" else ""
        let whole, part = Math.DivRem(abs thousandths, 1000L)

        if part = 0L then
            $"{sign}{integer whole}mm"
        else
            let digits = part.ToString("000", CultureInfo.InvariantCulture).TrimEnd('0')
            $"{sign}{integer whole}.{digits}mm"

    /// A length in millimetres, to the thousandth.
    let length (distance: Distance) = millimetres (micrometres distance)

    /// A length in millimetres from hundredths of a millimetre.
    let hundredthsLength (value: int64) = millimetres (value * 10L)

    /// A decimal with at most four places.
    let number (value: float) =
        Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture)

    /// A colour as #rrggbb.
    let color (value: Color) =
        String.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", value.R, value.G, value.B)

    /// An opacity as a percentage.
    let percent (opacity: decimal) =
        (Math.Round(opacity * 100m, 2)).ToString("0.##", CultureInfo.InvariantCulture) + "%"

    let attribute (name: XName) (value: string) = XAttribute(name, value)

/// The arrowheads of the theme as OpenDocument markers. ODF fills a marker and never strokes it, so a stroked head is
/// the closed outline of its stroke at the theme's marker stroke width.
[<RequireQualifiedAccess>]
module internal OdfMarkers =
    /// The stroke width of the theme's markers, in CSS pixels.
    let strokeWidth = 1.4

    /// The outline of an open polyline's stroke, with miter joins (limit 4) and butt caps.
    let strokeOutline (points: (float * float) list) (width: float) =
        let h = width / 2.0
        let array = List.toArray points

        let normal (x0: float, y0: float) (x1: float, y1: float) =
            let dx, dy = x1 - x0, y1 - y0
            let length = Math.Sqrt(dx * dx + dy * dy)
            -dy / length, dx / length

        let normals =
            [|
                for index in 0 .. array.Length - 2 -> normal array[index] array[index + 1]
            |]

        let side (sign: float) =
            [
                let (x, y), (nx, ny) = array[0], normals[0]
                yield x + sign * nx * h, y + sign * ny * h

                for index in 1 .. array.Length - 2 do
                    let x, y = array[index]
                    let (ax, ay), (bx, by) = normals[index - 1], normals[index]
                    let mx, my = ax + bx, ay + by
                    let ml = Math.Sqrt(mx * mx + my * my)
                    let cosine = (mx / ml) * bx + (my / ml) * by
                    let miter = h / cosine

                    if ml > 1e-9 && miter / h <= 4.0 then
                        yield x + sign * mx / ml * miter, y + sign * my / ml * miter
                    else
                        yield x + sign * ax * h, y + sign * ay * h
                        yield x + sign * bx * h, y + sign * by * h

                let (x, y), (nx, ny) = array[array.Length - 1], normals[normals.Length - 1]
                yield x + sign * nx * h, y + sign * ny * h
            ]

        side 1.0 @ List.rev (side -1.0)

    /// The union of the cross's two strokes: twelve corners.
    let crossOutline (half: float) (width: float) =
        let h = width / 2.0
        let length = half * Math.Sqrt 2.0
        let s = Math.Sqrt 0.5
        let u, v = (s, s), (-s, s)
        let at (a: float) (b: float) = a * fst u + b * fst v, a * snd u + b * snd v

        [
            at length -h
            at length h
            at h h
            at h length
            at -h length
            at -h h
            at -length h
            at -length -h
            at -h -h
            at -h -length
            at h -length
            at h -h
        ]

    /// The outline of a head in the SVG marker's frame (tip at the origin, the line arriving along +x), as closed
    /// subpaths of points or a circle, from the geometry in SvgWriting.
    let private outline (head: EdgeHead) =
        match head with
        | EdgeHead.Arrow -> Some(Choice1Of2 [ -9.0, -4.0; 0.0, 0.0; -9.0, 4.0 ])
        | EdgeHead.OpenArrow -> Some(Choice1Of2(strokeOutline [ -9.0, -4.0; 0.0, 0.0; -9.0, 4.0 ] strokeWidth))
        | EdgeHead.Async
        | EdgeHead.HalfUpper -> Some(Choice1Of2(strokeOutline [ -9.0, -4.0; 0.0, 0.0 ] strokeWidth))
        | EdgeHead.HalfLower -> Some(Choice1Of2(strokeOutline [ -9.0, 4.0; 0.0, 0.0 ] strokeWidth))
        | EdgeHead.HalfUpperFilled -> Some(Choice1Of2 [ -9.0, -4.0; 0.0, 0.0; -9.0, 0.0 ])
        | EdgeHead.HalfLowerFilled -> Some(Choice1Of2 [ -9.0, 4.0; 0.0, 0.0; -9.0, 0.0 ])
        | EdgeHead.Cross -> Some(Choice1Of2(crossOutline 4.0 strokeWidth))
        | EdgeHead.Circle -> Some(Choice2Of2((-4.0, 0.0), 4.0))
        | _ -> None

    /// A marker's frame: ODF draws the marker's view box with its centre at the line's end when the marker is centred,
    /// pointing along the line towards -y. The SVG frame's +x becomes -y and its +y becomes +x.
    let private turn (x: float, y: float) = y, -x

    /// The name, view box, path and width in CSS pixels of a head's marker, or none for no head.
    let marker (head: EdgeHead) =
        outline head
        |> Option.map (fun shape ->
            let scale = 100.0
            let unit (value: float) = int64 (Math.Round(value * scale, MidpointRounding.ToEven))

            let points, path =
                match shape with
                | Choice1Of2 points ->
                    let turned = points |> List.map turn
                    let text = turned |> List.mapi (fun index (x, y) -> $"""{(if index = 0 then "M" else "L")}{Odf.integer (unit x)} {Odf.integer (unit y)}""")
                    turned, String.Join(" ", text) + " Z"
                | Choice2Of2((cx, cy), r) ->
                    let x, y = turn (cx, cy)
                    let k = r * 0.5522847498
                    let p (a: float) (b: float) = $"{Odf.integer (unit a)} {Odf.integer (unit b)}"

                    let text =
                        $"M{p (x + r) y} C{p (x + r) (y + k)} {p (x + k) (y + r)} {p x (y + r)} "
                        + $"C{p (x - k) (y + r)} {p (x - r) (y + k)} {p (x - r) y} "
                        + $"C{p (x - r) (y - k)} {p (x - k) (y - r)} {p x (y - r)} "
                        + $"C{p (x + k) (y - r)} {p (x + r) (y - k)} {p (x + r) y} Z"

                    [ x - r, y - r; x + r, y + r ], text

            // The view box is centred on the tip, so a centred marker puts the tip at the line's end.
            let reachX = points |> List.map (fst >> abs) |> List.max
            let reachY = points |> List.map (snd >> abs) |> List.max
            let mx, my = unit reachX + 1L, unit reachY + 1L
            let name = $"Morphir_{head.WireValue}"

            // Impress centres the bounds of the path rather than its view box, so a head that is not symmetric about
            // its tip gets two single-point subpaths at opposite corners of the view box, which fill nothing.
            let symmetric =
                let xs, ys = points |> List.map fst, points |> List.map snd
                abs (List.min xs + List.max xs) < 1e-6 && abs (List.min ys + List.max ys) < 1e-6

            let anchored =
                if symmetric then path
                else $"{path} M{Odf.integer -mx} {Odf.integer -my} Z M{Odf.integer mx} {Odf.integer my} Z"

            name, $"{Odf.integer -mx} {Odf.integer -my} {Odf.integer (2L * mx)} {Odf.integer (2L * my)}", anchored, 2.0 * float mx / scale)

/// The automatic styles of a content document and the common styles of the styles document, made once for each
/// distinct set of properties.
type internal OdfStyles() =
    let automatic = ResizeArray<XElement>()
    let names = Dictionary<string, string>(StringComparer.Ordinal)
    let common = ResizeArray<XElement>()
    let commonNames = HashSet<string>(StringComparer.Ordinal)

    let make (family: string) (prefix: string) (properties: XElement list) =
        let key = family + "|" + String.Join("|", properties |> List.map (fun element -> element.ToString(SaveOptions.DisableFormatting)))

        match names.TryGetValue key with
        | true, name -> name
        | false, _ ->
            let name = $"{prefix}{names.Count + 1}"
            names[key] <- name
            automatic.Add(XElement(Odf.style + "style", XAttribute(Odf.style + "name", name), XAttribute(Odf.style + "family", family), properties))
            name

    /// A graphic style.
    member _.Graphic(attributes: XAttribute list) =
        make "graphic" "gr" [ XElement(Odf.style + "graphic-properties", attributes) ]

    /// A paragraph style.
    member _.Paragraph(align: string) =
        make "paragraph" "P" [ XElement(Odf.style + "paragraph-properties", XAttribute(Odf.fo + "text-align", align)) ]

    /// A text style.
    member _.Text(attributes: XAttribute list) =
        make "text" "T" [ XElement(Odf.style + "text-properties", attributes) ]

    /// A drawing-page style with a background colour.
    member _.Page(background: Color) =
        make "drawing-page" "dp" [
            XElement(
                Odf.style + "drawing-page-properties",
                XAttribute(Odf.draw + "fill", "solid"),
                XAttribute(Odf.draw + "fill-color", Odf.color background),
                XAttribute(Odf.draw + "background-size", "full")
            )
        ]

    /// Adds a common style once by name.
    member _.Common(name: string, create: unit -> XElement) =
        if commonNames.Add name then common.Add(create ())
        name

    member _.Automatic = automatic :> IReadOnlyList<XElement>
    member _.CommonStyles = common :> IReadOnlyList<XElement>

/// Writes the drawables of one page.
type internal OdfPage(fit: SlideFit, typeface: string, styles: OdfStyles, ids: Dictionary<DrawableId, string>, shapes: Dictionary<DrawableId, LayoutShape>, images: Dictionary<DrawableId, string * string>) =
    let um = Odf.micrometres
    let xy (name: string) (value: Distance) = Odf.attribute (Odf.svg + name) (Odf.length value)

    let placedCommands (commands: PathCommand seq) =
        commands
        |> Seq.map (function
            | PathCommand.MoveTo pt -> PathCommand.MoveTo(fit.Point pt)
            | PathCommand.LineTo pt -> PathCommand.LineTo(fit.Point pt)
            | PathCommand.CubicTo(c1, c2, pt) -> PathCommand.CubicTo(fit.Point c1, fit.Point c2, fit.Point pt)
            | PathCommand.QuadraticTo(c, pt) -> PathCommand.QuadraticTo(fit.Point c, fit.Point pt)
            | other -> other)
        |> List.ofSeq

    let withoutArcs (data: PathData) = data.WithoutArcs(Distance.FromPixels 0.01).Commands |> List.ofSeq

    /// The subpaths of commands, each with whether it is closed.
    let subpaths (commands: PathCommand list) =
        let result = ResizeArray<PathCommand list * bool>()
        let current = ResizeArray<PathCommand>()

        let flush closed =
            if current.Count > 0 then
                result.Add((List.ofSeq current, closed))
                current.Clear()

        for command in commands do
            match command with
            | PathCommand.MoveTo _ ->
                flush false
                current.Add command
            | PathCommand.Close ->
                flush true
            | other -> current.Add other

        flush false
        List.ofSeq result

    /// An enhanced path relative to an origin, in thousandths of a millimetre: each closed subpath is a filled path,
    /// each open one an unfilled path.
    let enhancedPath (commands: PathCommand list) (x0: Distance) (y0: Distance) =
        let point (pt: LayoutPoint) = $"{Odf.integer (um pt.X - um x0)} {Odf.integer (um pt.Y - um y0)}"

        let parts =
            subpaths commands
            |> List.map (fun (path, closed) ->
                let body =
                    path
                    |> List.map (function
                        | PathCommand.MoveTo pt -> $"M {point pt}"
                        | PathCommand.LineTo pt -> $"L {point pt}"
                        | PathCommand.CubicTo(c1, c2, pt) -> $"C {point c1} {point c2} {point pt}"
                        | PathCommand.QuadraticTo(c, pt) -> $"Q {point c} {point pt}"
                        | _ -> "")
                    |> List.filter (fun part -> part <> "")

                String.Join(" ", body) + (if closed then " Z N" else " F N"))

        String.Join(" ", parts)

    let fill (color: LayoutColor | null) =
        match color with
        | null -> [ Odf.attribute (Odf.draw + "fill") "none" ]
        | color ->
            [
                yield Odf.attribute (Odf.draw + "fill") "solid"
                yield Odf.attribute (Odf.draw + "fill-color") (Odf.color color.Color)
                if color.Color.Opacity < 1m then yield Odf.attribute (Odf.draw + "opacity") (Odf.percent color.Color.Opacity)
            ]

    let stroke (paint: LayoutPaint) =
        match paint.Stroke with
        | null -> [ Odf.attribute (Odf.draw + "stroke") "none" ]
        | color ->
            [
                if paint.Dash.IsEmpty then
                    yield Odf.attribute (Odf.draw + "stroke") "solid"
                else
                    let dash, gap = paint.Dash[0], (if paint.Dash.Length > 1 then paint.Dash[1] else paint.Dash[0])
                    let name = $"Morphir_dash_{Odf.integer (um dash)}_{Odf.integer (um gap)}"

                    styles.Common(
                        name,
                        fun () ->
                            XElement(
                                Odf.draw + "stroke-dash",
                                Odf.attribute (Odf.draw + "name") name,
                                Odf.attribute (Odf.draw + "style") "rect",
                                Odf.attribute (Odf.draw + "dots1") "1",
                                Odf.attribute (Odf.draw + "dots1-length") (Odf.length dash),
                                Odf.attribute (Odf.draw + "distance") (Odf.length gap)
                            )
                    )
                    |> ignore

                    yield Odf.attribute (Odf.draw + "stroke") "dash"
                    yield Odf.attribute (Odf.draw + "stroke-dash") name
                yield Odf.attribute (Odf.svg + "stroke-color") (Odf.color color.Color)
                yield Odf.attribute (Odf.svg + "stroke-width") (Odf.length paint.StrokeWidth)
                yield Odf.attribute (Odf.draw + "stroke-linejoin") "miter"
            ]

    let textFrame (wraps: bool) =
        [
            Odf.attribute (Odf.draw + "textarea-horizontal-align") "center"
            Odf.attribute (Odf.draw + "textarea-vertical-align") "middle"
            Odf.attribute (Odf.draw + "auto-grow-height") "false"
            Odf.attribute (Odf.draw + "auto-grow-width") "false"
            Odf.attribute (Odf.fo + "padding-top") "0mm"
            Odf.attribute (Odf.fo + "padding-bottom") "0mm"
            Odf.attribute (Odf.fo + "padding-left") "0mm"
            Odf.attribute (Odf.fo + "padding-right") "0mm"
            Odf.attribute (Odf.fo + "wrap-option") (if wraps then "wrap" else "no-wrap")
        ]

    let paragraphs (body: LayoutText) (inShape: bool) =
        let align =
            match body.Align with
            | TextAlign.Start when not inShape -> "start"
            | TextAlign.End when not inShape -> "end"
            | _ -> "center"

        let paragraph = styles.Paragraph align

        let run =
            styles.Text [
                Odf.attribute (Odf.fo + "color") (Odf.color body.Ink.Color)
                Odf.attribute (Odf.fo + "font-size") (Odf.number (Math.Max(1.0, (fit.Length body.Size).ToPoints())) + "pt")
                Odf.attribute (Odf.fo + "font-weight") (if body.Weight >= 600 then "bold" else "normal")
                Odf.attribute (Odf.fo + "font-family") typeface
                Odf.attribute (Odf.style + "font-family-generic") "modern"
                Odf.attribute (Odf.style + "font-pitch") "fixed"
            ]

        [
            for line in body.Lines ->
                XElement(Odf.text + "p", XAttribute(Odf.text + "style-name", paragraph), XElement(Odf.text + "span", XAttribute(Odf.text + "style-name", run), line))
        ]

    /// The counter-clockwise angle of rotated text, from the layout's clockwise quarter turns.
    let textAngle (body: LayoutText | null) =
        match body with
        | null -> []
        | body when body.Rotation % 360 <> 0 -> [ Odf.attribute (Odf.draw + "text-rotate-angle") (Odf.integer (int64 ((360 - body.Rotation % 360) % 360))) ]
        | _ -> []

    /// A text's box on the slide, turned for a quarter rotation about its origin.
    let textBox (body: LayoutText) =
        let box = fit.Box body.Box
        let origin = fit.Point body.Origin

        match body.Rotation with
        | 90
        | 270 ->
            let turn (x: Distance, y: Distance) =
                let dx, dy = x - origin.X, y - origin.Y
                if body.Rotation = 90 then LayoutPoint.Create(origin.X - dy, origin.Y + dx) else LayoutPoint.Create(origin.X + dy, origin.Y - dx)

            let corners = [ box.X, box.Y; box.Right, box.Y; box.X, box.Bottom; box.Right, box.Bottom ] |> List.map turn
            corners |> List.tail |> List.fold (fun (b: LayoutBox) pt -> b.Include pt) (LayoutBox.At(List.head corners))
        | _ -> box

    let listeners (link: string option) =
        match link with
        | None -> []
        | Some target ->
            [
                XElement(
                    Odf.office + "event-listeners",
                    XElement(
                        Odf.presentation + "event-listener",
                        Odf.attribute (Odf.script + "event-name") "dom:click",
                        Odf.attribute (Odf.presentation + "action") "show",
                        Odf.attribute (Odf.xlink + "href") target,
                        Odf.attribute (Odf.xlink + "type") "simple",
                        Odf.attribute (Odf.xlink + "show") "embed",
                        Odf.attribute (Odf.xlink + "actuate") "onRequest"
                    )
                )
                :> obj
            ]

    let identified (id: string option) =
        match id with
        | Some id -> [ Odf.attribute (Odf.xml + "id") id :> obj; Odf.attribute (Odf.draw + "id") id ]
        | None -> []

    let tiny = Distance.FromEmu 36L

    let box (value: LayoutBox) : obj list =
        [ xy "x" value.X; xy "y" value.Y; xy "width" (Distance.Max(value.Width, tiny)); xy "height" (Distance.Max(value.Height, tiny)) ]

    /// A custom shape with its own geometry, paint and text.
    let customShape name id link (placed: LayoutBox) (kind: string) (commands: PathCommand list) (graphic: XAttribute list) (body: LayoutText | null) (inShape: bool) =
        let children: obj list =
            [
                yield Odf.attribute (Odf.draw + "name") name
                yield Odf.attribute (Odf.draw + "style-name") (styles.Graphic graphic)
                yield! identified id
                yield! box placed
                yield! listeners link

                match body with
                | null -> ()
                | body ->
                    for paragraph in paragraphs body inShape do
                        yield paragraph

                let geometry: obj list =
                    [
                        yield Odf.attribute (Odf.svg + "viewBox") $"0 0 {Odf.integer (max 1L (um placed.Width))} {Odf.integer (max 1L (um placed.Height))}"
                        yield Odf.attribute (Odf.draw + "type") kind
                        yield Odf.attribute (Odf.draw + "enhanced-path") (enhancedPath commands placed.X placed.Y)
                        for angle in textAngle body do
                            yield angle
                    ]

                yield XElement(Odf.draw + "enhanced-geometry", geometry)
            ]

        XElement(Odf.draw + "custom-shape", children)

    /// The shape kind scaled onto the slide.
    let scaled (kind: ShapeKind) =
        match kind with
        | ShapeKind.RoundedRectangle radius -> ShapeKind.RoundedRectangle(fit.Length radius)
        | ShapeKind.Cylinder cap -> ShapeKind.Cylinder(fit.Length cap)
        | ShapeKind.Hexagon inset -> ShapeKind.Hexagon(fit.Length inset)
        | other -> other

    /// Impress replaces the geometry and text area of a preset type it recognises with its own, so every shape is a
    /// non-primitive shape that carries the outline the SVG draws.
    let typeName (_: ShapeKind) = "non-primitive"

    /// The default glue points of a shape are the middles of its box's top, right, bottom and left edges.
    let glue (binding: LineBinding) =
        binding.Anchor.Match(
            (fun point ->
                let x, y = float point.X.Value, float point.Y.Value

                [ 0, (0.5, 0.0); 1, (1.0, 0.5); 2, (0.5, 1.0); 3, (0.0, 0.5) ]
                |> List.minBy (fun (_, (cx, cy)) -> (cx - x) * (cx - x) + (cy - y) * (cy - y))
                |> fst
                |> Some),
            (fun _ -> None)
        )

    /// A text drawn on its own: an unstroked rectangle, filled with its chip if it has one.
    member _.TextShape(name: string, link: string option, body: LayoutText) =
        let placed = textBox body
        let rectangle = Lowering.outline ShapeKind.Rectangle placed |> withoutArcs

        let fillOf =
            match body.Chip with
            | null -> fill null
            | chip -> fill chip.Color

        customShape name None link placed "non-primitive" rectangle (fillOf @ [ Odf.attribute (Odf.draw + "stroke") "none" ] @ textFrame body.Wraps) body false

    /// The elements that draw one item.
    member this.Write(item: SlideItem, link: string option) : XElement list =
        let drawable = item.Drawable
        let id = match ids.TryGetValue drawable.Id with | true, id -> Some id | false, _ -> None

        drawable.Kind.Match(
            (fun shape ->
                let placed = fit.Box shape.Box

                let commands =
                    match shape.Kind with
                    | ShapeKind.Path data -> placedCommands (data.WithoutArcs(Distance.FromPixels 0.01).Commands)
                    | kind -> Lowering.outline (scaled kind) placed |> withoutArcs

                let wraps = match shape.Text with | null -> false | body -> body.Wraps
                let graphic = fill shape.Paint.Fill @ stroke shape.Paint @ textFrame wraps
                [ customShape item.Name id link placed (typeName shape.Kind) commands graphic shape.Text true ]),
            (fun line ->
                let commands =
                    match line.ExactPath with
                    | null -> placedCommands (line.Waypoints |> Seq.mapi (fun index pt -> if index = 0 then PathCommand.MoveTo pt else PathCommand.LineTo pt))
                    | exact -> placedCommands (exact.WithoutArcs(Distance.FromPixels 0.01).Commands)

                let start' = fit.Point line.Source.Point
                let end' = fit.Point line.Target.Point
                let h = Odf.hundredths

                // The path starts and ends exactly at the connector's two ends, in hundredths of a millimetre.
                let point (pt: LayoutPoint) = $"{Odf.integer (h pt.X)} {Odf.integer (h pt.Y)}"

                let d =
                    commands
                    |> List.choose (function
                        | PathCommand.MoveTo pt -> Some $"M{point pt}"
                        | PathCommand.LineTo pt -> Some $"L{point pt}"
                        | PathCommand.CubicTo(c1, c2, pt) -> Some $"C{point c1} {point c2} {point pt}"
                        | PathCommand.QuadraticTo(c, pt) -> Some $"Q{point c} {point pt}"
                        | _ -> None)
                    |> fun parts -> String.Join(" ", parts)

                let points =
                    commands
                    |> List.collect (function
                        | PathCommand.MoveTo pt
                        | PathCommand.LineTo pt -> [ pt ]
                        | PathCommand.CubicTo(c1, c2, pt) -> [ c1; c2; pt ]
                        | PathCommand.QuadraticTo(c, pt) -> [ c; pt ]
                        | _ -> [])

                let xs, ys = points |> List.map (fun pt -> h pt.X), points |> List.map (fun pt -> h pt.Y)
                let left, top = List.min xs, List.min ys

                let curved = commands |> List.exists (function PathCommand.CubicTo _ | PathCommand.QuadraticTo _ -> true | _ -> false)

                let orthogonal =
                    points |> List.pairwise |> List.forall (fun (a, b) -> h a.X = h b.X || h a.Y = h b.Y)

                let kind =
                    if curved then "curve"
                    elif points.Length <= 2 then "line"
                    elif orthogonal then "standard"
                    else "lines"

                let markers =
                    [
                        for name, head in [ "start", line.Source.Head; "end", line.Target.Head ] do
                            match OdfMarkers.marker head with
                            | Some(marker, viewBox, path, width) ->
                                styles.Common(
                                    marker,
                                    fun () ->
                                        XElement(
                                            Odf.draw + "marker",
                                            Odf.attribute (Odf.draw + "name") marker,
                                            Odf.attribute (Odf.svg + "viewBox") viewBox,
                                            Odf.attribute (Odf.svg + "d") path
                                        )
                                )
                                |> ignore

                                yield Odf.attribute (Odf.draw + $"marker-{name}") marker
                                yield Odf.attribute (Odf.draw + $"marker-{name}-width") (Odf.length (Distance.FromPixels width))
                                yield Odf.attribute (Odf.draw + $"marker-{name}-center") "true"
                            | None -> ()
                    ]

                let graphic = [ Odf.attribute (Odf.draw + "fill") "none" ] @ stroke line.Paint @ markers

                let ends: obj list =
                    [
                        for name, lineEnd in [ "start", line.Source; "end", line.Target ] do
                            match lineEnd.Binding with
                            | null -> ()
                            | binding ->
                                match ids.TryGetValue binding.Target, shapes.ContainsKey binding.Target, glue binding with
                                | (true, target), true, Some point ->
                                    yield Odf.attribute (Odf.draw + $"{name}-shape") target
                                    yield Odf.attribute (Odf.draw + $"{name}-glue-point") (Odf.integer (int64 point))
                                | _ -> ()
                    ]

                let connector =
                    XElement(
                        Odf.draw + "connector",
                        [
                            yield Odf.attribute (Odf.draw + "name") item.Name :> obj
                            yield Odf.attribute (Odf.draw + "style-name") (styles.Graphic graphic)
                            yield Odf.attribute (Odf.draw + "type") kind
                            yield Odf.attribute (Odf.svg + "x1") (Odf.hundredthsLength (h start'.X))
                            yield Odf.attribute (Odf.svg + "y1") (Odf.hundredthsLength (h start'.Y))
                            yield Odf.attribute (Odf.svg + "x2") (Odf.hundredthsLength (h end'.X))
                            yield Odf.attribute (Odf.svg + "y2") (Odf.hundredthsLength (h end'.Y))
                            yield! ends
                            yield Odf.attribute (Odf.svg + "viewBox") $"{Odf.integer left} {Odf.integer top} {Odf.integer (max 1L (List.max xs - left))} {Odf.integer (max 1L (List.max ys - top))}"
                            yield Odf.attribute (Odf.svg + "d") d
                            yield! listeners link
                        ]
                    )

                [
                    yield connector

                    match line.Label with
                    | null -> ()
                    | label -> yield this.TextShape($"{item.Name} label", None, label)
                ]),
            (fun body -> [ this.TextShape(item.Name, link, body) ]),
            (fun image ->
                let svgPath, pngPath = images[drawable.Id]
                let source (href: string) (mediaType: string) =
                    XElement(
                        Odf.draw + "image",
                        Odf.attribute (Odf.xlink + "href") href,
                        Odf.attribute (Odf.xlink + "type") "simple",
                        Odf.attribute (Odf.xlink + "show") "embed",
                        Odf.attribute (Odf.xlink + "actuate") "onLoad",
                        Odf.attribute (Odf.draw + "mime-type") mediaType
                    )

                [
                    XElement(
                        Odf.draw + "frame",
                        [
                            yield Odf.attribute (Odf.draw + "name") item.Name :> obj
                            yield Odf.attribute (Odf.draw + "style-name") (styles.Graphic [ Odf.attribute (Odf.draw + "fill") "none"; Odf.attribute (Odf.draw + "stroke") "none" ])
                            yield! box (fit.Box image.Box)
                            yield! listeners link
                            yield source svgPath "image/svg+xml"
                            yield source pngPath "image/png"
                        ]
                    )
                ]),
            (fun _ -> []),
            (fun _ -> []),
            (fun _ -> [])
        )

/// The parts of an OpenDocument presentation.
[<RequireQualifiedAccess>]
module internal OdfParts =
    let private document (name: XName) (children: obj list) =
        Packaging.xml (fun writer ->
            let root = XElement(name, Odf.declarations @ [ Odf.attribute (Odf.office + "version") Odf.version :> obj ] @ children)
            root.WriteTo writer)

    /// The content document: the automatic styles, then one page for each layout.
    let content (styles: OdfStyles) (pages: XElement list) =
        document (Odf.office + "document-content") [
            XElement(Odf.office + "automatic-styles", styles.Automatic)
            XElement(Odf.office + "body", XElement(Odf.office + "presentation", pages))
        ]

    /// The styles document: markers and dashes, the page layout of the slide size and the master page.
    let styles (styles: OdfStyles) (size: SlideSize) =
        document (Odf.office + "document-styles") [
            XElement(Odf.office + "styles", styles.CommonStyles)
            XElement(
                Odf.office + "automatic-styles",
                XElement(
                    Odf.style + "page-layout",
                    Odf.attribute (Odf.style + "name") "PM1",
                    XElement(
                        Odf.style + "page-layout-properties",
                        Odf.attribute (Odf.fo + "margin-top") "0mm",
                        Odf.attribute (Odf.fo + "margin-bottom") "0mm",
                        Odf.attribute (Odf.fo + "margin-left") "0mm",
                        Odf.attribute (Odf.fo + "margin-right") "0mm",
                        Odf.attribute (Odf.fo + "page-width") (Odf.length size.Width),
                        Odf.attribute (Odf.fo + "page-height") (Odf.length size.Height),
                        Odf.attribute (Odf.style + "print-orientation") (if size.Width.CompareTo size.Height >= 0 then "landscape" else "portrait")
                    )
                ),
                XElement(
                    Odf.style + "style",
                    Odf.attribute (Odf.style + "name") "Mdp1",
                    Odf.attribute (Odf.style + "family") "drawing-page",
                    XElement(Odf.style + "drawing-page-properties", Odf.attribute (Odf.draw + "fill") "none")
                )
            )
            XElement(
                Odf.office + "master-styles",
                XElement(
                    Odf.style + "master-page",
                    Odf.attribute (Odf.style + "name") "Default",
                    Odf.attribute (Odf.style + "page-layout-name") "PM1",
                    Odf.attribute (Odf.draw + "style-name") "Mdp1"
                )
            )
        ]

    /// The manifest of the package's documents and pictures.
    let manifest (pictures: (string * string) list) =
        let entry (path: string) (mediaType: string) =
            XElement(Odf.manifest + "file-entry", Odf.attribute (Odf.manifest + "full-path") path, Odf.attribute (Odf.manifest + "media-type") mediaType)

        Packaging.xml (fun writer ->
            let root =
                XElement(
                    Odf.manifest + "manifest",
                    XAttribute(XNamespace.Xmlns + "manifest", Odf.manifest.NamespaceName),
                    Odf.attribute (Odf.manifest + "version") Odf.version,
                    [
                        yield XElement(Odf.manifest + "file-entry", Odf.attribute (Odf.manifest + "full-path") "/", Odf.attribute (Odf.manifest + "version") Odf.version, Odf.attribute (Odf.manifest + "media-type") Odf.mediaType)
                        yield entry "content.xml" "text/xml"
                        yield entry "styles.xml" "text/xml"
                        for path, mediaType in pictures -> entry path mediaType
                    ]
                )

            root.WriteTo writer)

/// Writes OpenDocument presentations.
[<RequireQualifiedAccess>]
module internal OdfWriting =
    /// The package bytes and warnings, or the error that stops the render.
    let package (placement: SlidePlacement) (typeface: string) (pages: ImmutableArray<DiagramLayout>) (cancellationToken: CancellationToken) : Result<byte[] * Diagnostic list, Diagnostic> =
        let styles = OdfStyles()
        let warnings = ResizeArray<Diagnostic>()
        let pictures = ResizeArray<PackageEntry>()
        let written = ResizeArray<XElement>()
        let mutable failure: Diagnostic option = None

        pages
        |> Seq.iteri (fun index page ->
            if failure.IsNone then
                let number = index + 1
                let fit = placement.Fit(page, SlideGrid.Micrometre)
                let items = SlideItems.ofPage page
                let ids = Dictionary<DrawableId, string>()
                let shapes = Dictionary<DrawableId, LayoutShape>()
                let images = Dictionary<DrawableId, string * string>()

                for item in items do
                    match item.Drawable.Kind with
                    | DrawableKind.Shape shape ->
                        ids[item.Drawable.Id] <- $"p{number}d{ids.Count + 1}"
                        shapes[item.Drawable.Id] <- shape
                    | _ -> ()

                // Images are drawn first, so that a raster failure writes nothing.
                for item in items do
                    match item.Drawable.Kind, failure with
                    | DrawableKind.Image image, None ->
                        cancellationToken.ThrowIfCancellationRequested()
                        let placed = fit.Box image.Box
                        let name = $"image {item.Name} on page {number}"
                        match Packaging.approximated image.SvgText name with
                        | null -> ()
                        | warning -> warnings.Add warning

                        match PngFallbacks.TryDraw(image.SvgText, float (placed.Width.ToEmu()) / 9525.0, name) with
                        | false, _, error -> failure <- Some error
                        | true, png, _ ->
                            let index = pictures.Count / 2 + 1
                            let svgPath, pngPath = $"Pictures/image{index}.svg", $"Pictures/image{index}.png"
                            pictures.Add { Name = svgPath; Bytes = Encoding.UTF8.GetBytes(Packaging.portable image.SvgText); Stored = false }
                            pictures.Add { Name = pngPath; Bytes = png.Bytes; Stored = true }
                            images[item.Drawable.Id] <- (svgPath, pngPath)
                    | _ -> ()

                if failure.IsNone then
                    let writer = OdfPage(fit, typeface, styles, ids, shapes, images)

                    let children =
                        [
                            for item in items do
                                let link = item.Drawable.Links |> Seq.tryFind (fun link -> link.IsWeb) |> Option.map (fun link -> link.Target)
                                yield! writer.Write(item, link)
                        ]

                    written.Add(
                        XElement(
                            Odf.draw + "page",
                            Odf.attribute (Odf.draw + "name") $"page{number}",
                            Odf.attribute (Odf.draw + "style-name") (styles.Page page.Background),
                            Odf.attribute (Odf.draw + "master-page-name") "Default",
                            children
                        )
                    ))

        match failure with
        | Some error -> Error error
        | None ->
            let pages = List.ofSeq written
            let content = OdfParts.content styles pages
            let stylesXml = OdfParts.styles styles placement.Size

            let entries =
                [
                    { Name = "mimetype"; Bytes = Encoding.ASCII.GetBytes Odf.mediaType; Stored = true }
                    { Name = "META-INF/manifest.xml"; Bytes = OdfParts.manifest [ for picture in pictures -> picture.Name, (if picture.Name.EndsWith(".svg", StringComparison.Ordinal) then "image/svg+xml" else "image/png") ]; Stored = false }
                    { Name = "content.xml"; Bytes = content; Stored = false }
                    { Name = "styles.xml"; Bytes = stylesXml; Stored = false }
                    yield! pictures
                ]

            Ok(Packaging.zip entries, List.ofSeq warnings)

/// Writes diagrams as an OpenDocument presentation for LibreOffice and OpenOffice Impress: one page per layout, with
/// custom shapes that carry their own geometry and connectors glued to them along the diagram's routes.
[<Sealed>]
type OpenDocumentRenderer(placement: SlidePlacement, typeface: string | null) =
    inherit DiagramRenderer()

    do ArgumentNullException.ThrowIfNull placement

    let typeface = SlideText.typeface typeface

    static let features =
        LayoutFeatures.Of(
            LayoutFeature.RoundedRectangles,
            LayoutFeature.Ellipses,
            LayoutFeature.Diamonds,
            LayoutFeature.Cylinders,
            LayoutFeature.Hexagons,
            LayoutFeature.Paths,
            LayoutFeature.Polylines,
            LayoutFeature.ExactRoutes,
            LayoutFeature.AttachedConnectors,
            LayoutFeature.OpenAndCircleHeads,
            LayoutFeature.MessageHeads,
            LayoutFeature.WrappedText,
            LayoutFeature.RotatedText,
            LayoutFeature.InterleavedPainting,
            LayoutFeature.FillOpacity,
            LayoutFeature.DashPatterns,
            LayoutFeature.Links,
            LayoutFeature.SvgImages,
            LayoutFeature.Pages
        )

    /// A renderer with widescreen placement and the Liberation Mono typeface, which LibreOffice ships.
    new() = OpenDocumentRenderer(SlidePlacement.Widescreen, "Liberation Mono")

    /// A renderer with a placement and the Liberation Mono typeface.
    new(placement: SlidePlacement) = OpenDocumentRenderer(placement, "Liberation Mono")

    /// Where diagrams go on the pages.
    member _.Placement = placement

    /// The typeface of text.
    member _.Typeface = typeface

    /// application/vnd.oasis.opendocument.presentation.
    override _.MediaType = Odf.mediaType

    /// .odp.
    override _.FileExtension = ".odp"

    /// The shape kinds, paths, polylines, exact routes, attached connectors, every head, wrapped and rotated text,
    /// interleaved painting, fill opacity, dash patterns, links, SVG images and pages. Not stroke and text opacity,
    /// which OpenDocument text has no standard way to draw.
    override _.NativeFeatures = features

    /// Writes the plan's pages as one presentation.
    override this.RenderAsync(plan: RenderPlan, output: Stream, cancellationToken: CancellationToken) =
        ArgumentNullException.ThrowIfNull plan
        ArgumentNullException.ThrowIfNull output
        plan.EnsureFor this
        cancellationToken.ThrowIfCancellationRequested()

        match OdfWriting.package placement typeface plan.Pages cancellationToken with
        | Error error -> Task.FromResult(ImmutableArray.Create<Diagnostic> error)
        | Ok(bytes, warnings) ->
            task {
                do! Packaging.copy bytes output cancellationToken
                return ImmutableArray.CreateRange<Diagnostic> warnings
            }

    /// The typeface.
    override _.ToString() = $"OpenDocumentRenderer({typeface})"
