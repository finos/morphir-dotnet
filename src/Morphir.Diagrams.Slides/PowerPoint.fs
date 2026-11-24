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
open Morphir.Diagrams
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout

/// How the PowerPoint renderer draws lines.
[<RequireQualifiedAccess>]
type PowerPointConnectors =
    /// Each line is its exact path, with every arrowhead, and is not glued to shapes. The default.
    | Exact
    /// Each line is a straight PowerPoint connector glued to the connection sites nearest its anchors, so it follows
    /// moved shapes; it loses the exact route and the sequence-diagram heads.
    | Glued

    override this.ToString() =
        match this with
        | PowerPointConnectors.Exact -> "exact"
        | PowerPointConnectors.Glued -> "glued"

[<RequireQualifiedAccess>]
module internal Ooxml =
    let a = "http://schemas.openxmlformats.org/drawingml/2006/main"
    let r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    let p = "http://schemas.openxmlformats.org/presentationml/2006/main"
    let relationships = "http://schemas.openxmlformats.org/package/2006/relationships"
    let contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types"
    let svgBlip = "http://schemas.microsoft.com/office/drawing/2016/SVG/main"
    let svgExtension = "{96DAC541-7B7A-43D3-8B79-37D633B846F1}"

    let relType name =
        $"http://schemas.openxmlformats.org/officeDocument/2006/relationships/{name}"

    let n (value: int64) =
        value.ToString(CultureInfo.InvariantCulture)

    let emu (distance: Distance) = distance.ToEmu()


    let hex (color: Color) =
        String.Format(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B)

    let solidColor (writer: XmlWriter) (color: Color) =
        writer.WriteStartElement("a", "solidFill", a)
        writer.WriteStartElement("a", "srgbClr", a)
        writer.WriteAttributeString("val", hex color)

        if color.Opacity < 1m then
            writer.WriteStartElement("a", "alpha", a)

            writer.WriteAttributeString(
                "val",
                n (
                    int64 (
                        Math.Round(
                            color.Opacity
                            * 100000m
                        )
                    )
                )
            )

            writer.WriteEndElement()

        writer.WriteEndElement()
        writer.WriteEndElement()

    let solid (writer: XmlWriter) (color: LayoutColor) = solidColor writer color.Color

    let empty (writer: XmlWriter) (prefix: string) (name: string) (ns: string) =
        writer.WriteStartElement(prefix, name, ns)
        writer.WriteEndElement()

/// Writes the shapes of a slide.
type internal PowerPointShapes
    (
        writer: XmlWriter,
        fit: SlideFit,
        typeface: string,
        connectors: PowerPointConnectors,
        ids: Dictionary<DrawableId, int>,
        shapes: Dictionary<DrawableId, LayoutShape>,
        images: Dictionary<DrawableId, string * string>,
        links: Dictionary<DrawableId, string>
    ) =
    let a, p, r = Ooxml.a, Ooxml.p, Ooxml.r
    let n = Ooxml.n
    let mutable extraId = 100_000

    let start (prefix: string) (name: string) =
        writer.WriteStartElement(prefix, name, (if prefix = "a" then a else p))

    let finish () = writer.WriteEndElement()

    let attribute (name: string) (value: string) =
        writer.WriteAttributeString(name, value)

    let properties
        (kind: string)
        (id: int)
        (name: string)
        (link: string option)
        (inner: unit -> unit)
        =
        start "p" kind
        start "p" "cNvPr"
        attribute "id" (string id)
        attribute "name" name

        link
        |> Option.iter (fun rid ->
            start "a" "hlinkClick"
            writer.WriteAttributeString("id", r, rid)
            finish ()
        )

        finish ()
        inner ()
        start "p" "nvPr"
        finish ()
        finish ()

    let transform (box: LayoutBox) (rotation: int option) (flipH: bool) (flipV: bool) =
        start "a" "xfrm"

        rotation
        |> Option.iter (fun degrees ->
            attribute
                "rot"
                (string (
                    int64 degrees
                    * 60000L
                ))
        )

        if flipH then
            attribute "flipH" "1"

        if flipV then
            attribute "flipV" "1"

        start "a" "off"
        attribute "x" (n (Ooxml.emu box.X))
        attribute "y" (n (Ooxml.emu box.Y))
        finish ()
        start "a" "ext"
        attribute "cx" (n (max 1L (Ooxml.emu box.Width)))
        attribute "cy" (n (max 1L (Ooxml.emu box.Height)))
        finish ()
        finish ()

    let fill (color: LayoutColor | null) =
        match color with
        | null ->
            start "a" "noFill"
            finish ()
        | color -> Ooxml.solid writer color

    let lineEnd (name: string) (head: EdgeHead) (width: Distance) =
        let kind =
            match head with
            | EdgeHead.Arrow -> Some "triangle"
            | EdgeHead.OpenArrow -> Some "arrow"
            | EdgeHead.Circle -> Some "oval"
            | _ -> None

        match kind with
        | Some kind ->
            start "a" name
            attribute "type" kind
            // The theme's marker is about 9 px long; DrawingML sizes heads relative to the line width.
            let ratio =
                9.0
                / max 0.5 (width.ToPixels())

            let size =
                if ratio < 2.5 then "sm"
                elif ratio < 4.0 then "med"
                else "lg"

            attribute "w" size
            attribute "len" size
            finish ()
        | None -> ()

    let outline (paint: LayoutPaint) (heads: (EdgeHead * EdgeHead) option) =
        start "a" "ln"

        match paint.Stroke with
        | null ->
            attribute "w" "0"
            start "a" "noFill"
            finish ()
        | stroke ->
            attribute "w" (n (max 1L (paint.StrokeWidth.ToEmu())))
            Ooxml.solid writer stroke

            if not paint.Dash.IsEmpty then
                start "a" "custDash"
                let width = max 0.1 (paint.StrokeWidth.ToPixels())

                for pair in
                    paint.Dash
                    |> Seq.chunkBySize 2 do
                    start "a" "ds"

                    attribute
                        "d"
                        (n (
                            int64 (
                                Math.Round(
                                    pair[0].ToPixels()
                                    / width
                                    * 100000.0
                                )
                            )
                        ))

                    attribute
                        "sp"
                        (n (
                            int64 (
                                Math.Round(
                                    (if pair.Length > 1 then pair[1] else pair[0]).ToPixels()
                                    / width
                                    * 100000.0
                                )
                            )
                        ))

                    finish ()

                finish ()

            match heads with
            | Some(source, target) ->
                lineEnd "headEnd" source paint.StrokeWidth
                lineEnd "tailEnd" target paint.StrokeWidth
            | None -> ()

        finish ()

    let geometry (preset: string) =
        start "a" "prstGeom"
        attribute "prst" preset
        start "a" "avLst"
        finish ()
        finish ()

    let custom (box: LayoutBox) (commands: PathCommand seq) (filled: bool) =
        let x0, y0 = Ooxml.emu box.X, Ooxml.emu box.Y
        start "a" "custGeom"

        for name in [ "avLst"; "gdLst"; "ahLst"; "cxnLst" ] do
            start "a" name
            finish ()

        start "a" "rect"
        attribute "l" "0"
        attribute "t" "0"
        attribute "r" "r"
        attribute "b" "b"
        finish ()
        start "a" "pathLst"
        start "a" "path"
        attribute "w" (n (max 1L (Ooxml.emu box.Width)))
        attribute "h" (n (max 1L (Ooxml.emu box.Height)))

        if not filled then
            attribute "fill" "none"

        let point (pt: LayoutPoint) =
            start "a" "pt"

            attribute
                "x"
                (n (
                    Ooxml.emu pt.X
                    - x0
                ))

            attribute
                "y"
                (n (
                    Ooxml.emu pt.Y
                    - y0
                ))

            finish ()

        for command in commands do
            match command with
            | PathCommand.MoveTo pt ->
                start "a" "moveTo"
                point pt
                finish ()
            | PathCommand.LineTo pt ->
                start "a" "lnTo"
                point pt
                finish ()
            | PathCommand.CubicTo(c1, c2, pt) ->
                start "a" "cubicBezTo"
                point c1
                point c2
                point pt
                finish ()
            | PathCommand.QuadraticTo(c, pt) ->
                start "a" "quadBezTo"
                point c
                point pt
                finish ()
            | PathCommand.Close ->
                start "a" "close"
                finish ()
            | PathCommand.ArcTo _ -> ()

        finish ()
        finish ()
        finish ()

    let placedCommands (commands: PathCommand seq) =
        commands
        |> Seq.map (
            function
            | PathCommand.MoveTo pt -> PathCommand.MoveTo(fit.Point pt)
            | PathCommand.LineTo pt -> PathCommand.LineTo(fit.Point pt)
            | PathCommand.CubicTo(c1, c2, pt) ->
                PathCommand.CubicTo(fit.Point c1, fit.Point c2, fit.Point pt)
            | PathCommand.QuadraticTo(c, pt) -> PathCommand.QuadraticTo(fit.Point c, fit.Point pt)
            | other -> other
        )
        |> List.ofSeq

    let placed (data: PathData) =
        placedCommands (data.WithoutArcs(Distance.FromPixels 0.01).Commands)

    let bounds (commands: PathCommand list) =
        let points =
            commands
            |> List.choose (
                function
                | PathCommand.MoveTo pt
                | PathCommand.LineTo pt -> Some [ pt ]
                | PathCommand.CubicTo(a, b, pt) -> Some [ a; b; pt ]
                | PathCommand.QuadraticTo(a, pt) -> Some [ a; pt ]
                | _ -> None
            )
            |> List.concat

        points
        |> List.tail
        |> List.fold (fun (box: LayoutBox) pt -> box.Include pt) (LayoutBox.At(List.head points))

    let text (body: LayoutText) (inShape: bool) =
        start "p" "txBody"
        start "a" "bodyPr"
        attribute "wrap" (if body.Wraps then "square" else "none")

        for inset in [ "lIns"; "tIns"; "rIns"; "bIns" ] do
            attribute inset "0"

        attribute "anchor" "ctr"

        match body.Rotation with
        | 90 -> attribute "vert" "vert"
        | 270 -> attribute "vert" "vert270"
        | 180 -> attribute "rot" "10800000"
        | _ -> ()

        finish ()
        start "a" "lstStyle"
        finish ()

        for line in body.Lines do
            start "a" "p"
            start "a" "pPr"

            attribute
                "algn"
                (match body.Align with
                 | TextAlign.Start when not inShape -> "l"
                 | TextAlign.End when not inShape -> "r"
                 | _ -> "ctr")

            finish ()
            start "a" "r"
            start "a" "rPr"
            attribute "lang" "en-US"

            attribute
                "sz"
                (n (
                    max
                        100L
                        (int64 (
                            Math.Round(
                                (fit.Length body.Size).ToPoints()
                                * 100.0
                            )
                        ))
                ))

            attribute
                "b"
                (if
                     body.Weight
                     >= 600
                 then
                     "1"
                 else
                     "0")

            Ooxml.solid writer body.Ink
            start "a" "latin"
            attribute "typeface" typeface
            finish ()
            start "a" "cs"
            attribute "typeface" typeface
            finish ()
            finish ()
            start "a" "t"
            writer.WriteString line
            finish ()
            finish ()
            finish ()

        finish ()

    /// A text's box on the slide, turned for a quarter rotation about its origin.
    let textBox (body: LayoutText) =
        let box = fit.Box body.Box
        let origin = fit.Point body.Origin

        match body.Rotation with
        | 90
        | 270 ->
            let corners =
                [ box.X, box.Y; box.Right, box.Y; box.X, box.Bottom; box.Right, box.Bottom ]
                |> List.map (fun (x, y) ->
                    let dx, dy =
                        x
                        - origin.X,
                        y
                        - origin.Y

                    if body.Rotation = 90 then
                        LayoutPoint.Create(
                            origin.X
                            - dy,
                            origin.Y
                            + dx
                        )
                    else
                        LayoutPoint.Create(
                            origin.X
                            + dy,
                            origin.Y
                            - dx
                        )
                )

            corners
            |> List.tail
            |> List.fold (fun (b: LayoutBox) pt -> b.Include pt) (LayoutBox.At(List.head corners))
        | _ -> box

    /// The outline of a message head at a line end, in slide coordinates, and whether it is filled.
    let head (kind: EdgeHead) (tip: LayoutPoint) (from: LayoutPoint) =
        let angle =
            Math.Atan2(
                (tip.Y
                 - from.Y)
                    .ToPixels(),
                (tip.X
                 - from.X)
                    .ToPixels()
            )

        let cos, sin = Math.Cos angle, Math.Sin angle

        let at (x: float, y: float) =
            LayoutPoint.Create(
                tip.X
                + Distance.FromPixels(
                    x * cos
                    - y * sin
                ),
                tip.Y
                + Distance.FromPixels(
                    x * sin
                    + y * cos
                )
            )

        let open' points = [
            yield PathCommand.MoveTo(at (List.head points))
            for pt in List.tail points -> PathCommand.LineTo(at pt)
        ]

        match kind with
        | EdgeHead.Cross ->
            Some(
                open' [ -4.0, -4.0; 4.0, 4.0 ]
                @ open' [ -4.0, 4.0; 4.0, -4.0 ],
                false
            )
        | EdgeHead.Async
        | EdgeHead.HalfUpper -> Some(open' [ -9.0, -4.0; 0.0, 0.0 ], false)
        | EdgeHead.HalfLower -> Some(open' [ -9.0, 4.0; 0.0, 0.0 ], false)
        | EdgeHead.HalfUpperFilled ->
            Some(
                open' [ -9.0, -4.0; 0.0, 0.0; -9.0, 0.0 ]
                @ [ PathCommand.Close ],
                true
            )
        | EdgeHead.HalfLowerFilled ->
            Some(
                open' [ -9.0, 4.0; 0.0, 0.0; -9.0, 0.0 ]
                @ [ PathCommand.Close ],
                true
            )
        | _ -> None

    member this.Write(item: SlideItem) =
        let drawable = item.Drawable
        let id = ids[drawable.Id]

        let link =
            match links.TryGetValue drawable.Id with
            | true, rid -> Some rid
            | false, _ -> None

        drawable.Kind.Match(
            (fun shape ->
                let box = fit.Box shape.Box
                start "p" "sp"

                properties
                    "nvSpPr"
                    id
                    item.Name
                    link
                    (fun () ->
                        (start "p" "cNvSpPr"
                         finish ())
                    )

                start "p" "spPr"
                transform box None false false

                match shape.Kind with
                | ShapeKind.Rectangle -> geometry "rect"
                | ShapeKind.RoundedRectangle radius ->
                    start "a" "prstGeom"
                    attribute "prst" "roundRect"
                    start "a" "avLst"
                    start "a" "gd"
                    attribute "name" "adj"
                    let shortest = max 1L (min (Ooxml.emu box.Width) (Ooxml.emu box.Height))

                    attribute
                        "fmla"
                        $"val {min
                                   50000L
                                   (Ooxml.emu (fit.Length radius)
                                    * 100000L
                                    / shortest)}"

                    finish ()
                    finish ()
                    finish ()
                | ShapeKind.Ellipse -> geometry "ellipse"
                | ShapeKind.Diamond -> geometry "diamond"
                | ShapeKind.Cylinder _ -> geometry "can"
                | ShapeKind.Hexagon _ -> geometry "hexagon"
                | ShapeKind.Path data ->
                    let commands = placed data

                    let closed =
                        commands
                        |> List.exists (fun command -> command = PathCommand.Close)

                    custom
                        box
                        commands
                        (closed
                         && shape.Paint.Fill
                            <> null)

                fill shape.Paint.Fill
                outline shape.Paint None
                finish ()

                match shape.Text with
                | null -> ()
                | body -> text body true

                finish ()
            ),
            (fun line ->
                let commands =
                    match line.ExactPath with
                    | null ->
                        placedCommands (
                            line.Waypoints
                            |> Seq.mapi (fun index pt ->
                                if index = 0 then
                                    PathCommand.MoveTo pt
                                else
                                    PathCommand.LineTo pt
                            )
                        )
                    | exact -> placed exact

                let box = bounds commands

                let points =
                    line.Waypoints
                    |> Seq.map fit.Point
                    |> Seq.toArray

                let start' = fit.Point line.Source.Point
                let end' = fit.Point line.Target.Point

                let drawnHeads = [
                    match
                        head
                            line.Source.Head
                            start'
                            (if points.Length > 1 then points[1] else end')
                    with
                    | Some outline -> yield outline
                    | None -> ()
                    match
                        head
                            line.Target.Head
                            end'
                            (if points.Length > 1 then
                                 points[points.Length
                                        - 2]
                             else
                                 start')
                    with
                    | Some outline -> yield outline
                    | None -> ()
                ]

                let connector () =
                    // An exact line is a freeform shape: an application may re-route a connector between the corners of
                    // its box, so only a glued line, which is meant to follow its shapes, is a connector.
                    let glued = connectors = PowerPointConnectors.Glued
                    start "p" (if glued then "cxnSp" else "sp")

                    properties
                        (if glued then "nvCxnSpPr" else "nvSpPr")
                        id
                        item.Name
                        link
                        (fun () ->
                            start "p" (if glued then "cNvCxnSpPr" else "cNvSpPr")

                            if glued then
                                for name, end'' in [ "stCxn", line.Source; "endCxn", line.Target ] do
                                    match end''.Binding with
                                    | null -> ()
                                    | binding ->
                                        match
                                            ids.TryGetValue binding.Target,
                                            shapes.TryGetValue binding.Target
                                        with
                                        | (true, target), (true, shape) ->
                                            match PresetSites.nearest shape binding with
                                            | Some site ->
                                                start "a" name
                                                attribute "id" (string target)
                                                attribute "idx" (string site)
                                                finish ()
                                            | None -> ()
                                        | _ -> ()

                            finish ()
                        )

                    start "p" "spPr"

                    if glued then
                        let left, top =
                            Distance.Min(start'.X, end'.X), Distance.Min(start'.Y, end'.Y)

                        let straight =
                            LayoutBox.Create(
                                left,
                                top,
                                Distance.Max(start'.X, end'.X)
                                - left,
                                Distance.Max(start'.Y, end'.Y)
                                - top
                            )

                        transform
                            straight
                            None
                            (end'.X.CompareTo start'.X < 0)
                            (end'.Y.CompareTo start'.Y < 0)

                        geometry "straightConnector1"
                    else
                        transform box None false false
                        custom box commands false

                    outline line.Paint (Some(line.Source.Head, line.Target.Head))
                    finish ()
                    finish ()

                if drawnHeads.IsEmpty then
                    connector ()
                else
                    // A line with drawn heads is a group, so that its heads move with it.
                    let all =
                        drawnHeads
                        |> List.fold
                            (fun (b: LayoutBox) (outline, _) -> b.Union(bounds outline))
                            box

                    start "p" "grpSp"

                    extraId <-
                        extraId
                        + 1

                    properties
                        "nvGrpSpPr"
                        extraId
                        item.Name
                        None
                        (fun () ->
                            start "p" "cNvGrpSpPr"
                            finish ()
                        )

                    start "p" "grpSpPr"
                    start "a" "xfrm"

                    for name, x, y in
                        [
                            "off", "x", "y"
                            "ext", "cx", "cy"
                            "chOff", "x", "y"
                            "chExt", "cx", "cy"
                        ] do
                        start "a" name

                        attribute
                            x
                            (n (
                                if x = "x" then
                                    Ooxml.emu all.X
                                else
                                    max 1L (Ooxml.emu all.Width)
                            ))

                        attribute
                            y
                            (n (
                                if y = "y" then
                                    Ooxml.emu all.Y
                                else
                                    max 1L (Ooxml.emu all.Height)
                            ))

                        finish ()

                    finish ()
                    finish ()
                    connector ()

                    for drawn, filled in drawnHeads do
                        let headBox = bounds drawn

                        extraId <-
                            extraId
                            + 1

                        start "p" "sp"

                        properties
                            "nvSpPr"
                            extraId
                            $"{item.Name} head"
                            None
                            (fun () ->
                                (start "p" "cNvSpPr"
                                 finish ())
                            )

                        start "p" "spPr"
                        transform headBox None false false
                        custom headBox drawn filled
                        if filled then fill line.Paint.Stroke else fill null
                        outline line.Paint None
                        finish ()
                        finish ()

                    finish ()

                match line.Label with
                | null -> ()
                | label ->
                    extraId <-
                        extraId
                        + 1

                    this.TextShape(extraId, $"{item.Name} label", None, label)
            ),
            (fun body -> this.TextShape(id, item.Name, link, body)),
            (fun image ->
                let pngId, svgId = images[drawable.Id]
                start "p" "pic"

                properties
                    "nvPicPr"
                    id
                    item.Name
                    link
                    (fun () ->
                        start "p" "cNvPicPr"
                        start "a" "picLocks"
                        attribute "noChangeAspect" "1"
                        finish ()
                        finish ()
                    )

                start "p" "blipFill"
                start "a" "blip"
                writer.WriteAttributeString("embed", r, pngId)
                start "a" "extLst"
                start "a" "ext"
                attribute "uri" Ooxml.svgExtension
                writer.WriteStartElement("asvg", "svgBlip", Ooxml.svgBlip)
                writer.WriteAttributeString("embed", r, svgId)
                finish ()
                finish ()
                finish ()
                finish ()
                start "a" "stretch"
                start "a" "fillRect"
                finish ()
                finish ()
                finish ()
                start "p" "spPr"
                transform (fit.Box image.Box) None false false
                geometry "rect"
                finish ()
                finish ()
            ),
            (fun _ -> ()),
            (fun _ -> ()),
            (fun _ -> ())
        )

    member private this.TextShape(id: int, name: string, link: string option, body: LayoutText) =
        let box = textBox body
        start "p" "sp"

        properties
            "nvSpPr"
            id
            name
            link
            (fun () ->
                start "p" "cNvSpPr"
                attribute "txBox" "1"
                finish ()
            )

        start "p" "spPr"
        transform box None false false
        geometry "rect"

        match body.Chip with
        | null -> fill null
        | chip -> fill chip.Color

        start "a" "ln"
        start "a" "noFill"
        finish ()
        finish ()
        finish ()
        text body false
        finish ()

/// The fixed parts of a presentation.
[<RequireQualifiedAccess>]
module internal PowerPointParts =
    let rels (items: (string * string * string * bool) list) =
        Packaging.xml (fun writer ->
            writer.WriteStartElement("Relationships", Ooxml.relationships)

            for id, kind, target, external in items do
                writer.WriteStartElement("Relationship", Ooxml.relationships)
                writer.WriteAttributeString("Id", id)
                writer.WriteAttributeString("Type", kind)
                writer.WriteAttributeString("Target", target)

                if external then
                    writer.WriteAttributeString("TargetMode", "External")

                writer.WriteEndElement()

            writer.WriteEndElement()
        )

    let contentTypes (slides: int) (media: ResizeArray<PackageEntry>) =
        Packaging.xml (fun writer ->
            let ns = Ooxml.contentTypes
            writer.WriteStartElement("Types", ns)

            let fallback extension kind =
                writer.WriteStartElement("Default", ns)
                writer.WriteAttributeString("Extension", extension)
                writer.WriteAttributeString("ContentType", kind)
                writer.WriteEndElement()

            let part name kind =
                writer.WriteStartElement("Override", ns)
                writer.WriteAttributeString("PartName", name)
                writer.WriteAttributeString("ContentType", kind)
                writer.WriteEndElement()

            fallback "rels" "application/vnd.openxmlformats-package.relationships+xml"
            fallback "xml" "application/xml"

            if
                media
                |> Seq.exists (fun entry -> entry.Name.EndsWith(".png", StringComparison.Ordinal))
            then
                fallback "png" "image/png"

            if
                media
                |> Seq.exists (fun entry -> entry.Name.EndsWith(".svg", StringComparison.Ordinal))
            then
                fallback "svg" "image/svg+xml"

            part
                "/ppt/presentation.xml"
                "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"

            part
                "/ppt/slideMasters/slideMaster1.xml"
                "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"

            part
                "/ppt/slideLayouts/slideLayout1.xml"
                "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"

            part "/ppt/theme/theme1.xml" "application/vnd.openxmlformats-officedocument.theme+xml"

            for index in 1..slides do
                part
                    $"/ppt/slides/slide{index}.xml"
                    "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"

            writer.WriteEndElement()
        )

    let presentation (size: SlideSize) (slides: int) =
        Packaging.xml (fun writer ->
            let p, r = Ooxml.p, Ooxml.r
            writer.WriteStartElement("p", "presentation", p)
            writer.WriteAttributeString("xmlns", "a", null, Ooxml.a)
            writer.WriteAttributeString("xmlns", "r", null, r)
            writer.WriteAttributeString("saveSubsetFonts", "1")
            writer.WriteStartElement("p", "sldMasterIdLst", p)
            writer.WriteStartElement("p", "sldMasterId", p)
            writer.WriteAttributeString("id", "2147483648")
            writer.WriteAttributeString("id", r, "rId1")
            writer.WriteEndElement()
            writer.WriteEndElement()
            writer.WriteStartElement("p", "sldIdLst", p)

            for index in 1..slides do
                writer.WriteStartElement("p", "sldId", p)

                writer.WriteAttributeString(
                    "id",
                    string (
                        255
                        + index
                    )
                )

                writer.WriteAttributeString("id", r, $"rId{index + 2}")
                writer.WriteEndElement()

            writer.WriteEndElement()
            writer.WriteStartElement("p", "sldSz", p)
            writer.WriteAttributeString("cx", Ooxml.n (Ooxml.emu size.Width))
            writer.WriteAttributeString("cy", Ooxml.n (Ooxml.emu size.Height))
            writer.WriteEndElement()
            writer.WriteStartElement("p", "notesSz", p)
            writer.WriteAttributeString("cx", "6858000")
            writer.WriteAttributeString("cy", "9144000")
            writer.WriteEndElement()
            writer.WriteEndElement()
        )

    let private tree (writer: XmlWriter) =
        let p, a = Ooxml.p, Ooxml.a
        writer.WriteStartElement("p", "spTree", p)
        writer.WriteStartElement("p", "nvGrpSpPr", p)
        writer.WriteStartElement("p", "cNvPr", p)
        writer.WriteAttributeString("id", "1")
        writer.WriteAttributeString("name", "")
        writer.WriteEndElement()
        Ooxml.empty writer "p" "cNvGrpSpPr" p
        Ooxml.empty writer "p" "nvPr" p
        writer.WriteEndElement()
        writer.WriteStartElement("p", "grpSpPr", p)
        writer.WriteStartElement("a", "xfrm", a)

        for name, x, y in
            [ "off", "x", "y"; "ext", "cx", "cy"; "chOff", "x", "y"; "chExt", "cx", "cy" ] do
            writer.WriteStartElement("a", name, a)
            writer.WriteAttributeString(x, "0")
            writer.WriteAttributeString(y, "0")
            writer.WriteEndElement()

        writer.WriteEndElement()
        writer.WriteEndElement()

    /// The start of a slide tree; the caller writes its shapes and closes it.
    let openTree (writer: XmlWriter) = tree writer

    let master () =
        Packaging.xml (fun writer ->
            let p, r = Ooxml.p, Ooxml.r
            writer.WriteStartElement("p", "sldMaster", p)
            writer.WriteAttributeString("xmlns", "a", null, Ooxml.a)
            writer.WriteAttributeString("xmlns", "r", null, r)
            writer.WriteStartElement("p", "cSld", p)
            tree writer
            writer.WriteEndElement()
            writer.WriteEndElement()
            writer.WriteStartElement("p", "clrMap", p)

            for name, value in
                [
                    "bg1", "lt1"
                    "tx1", "dk1"
                    "bg2", "lt2"
                    "tx2", "dk2"
                    "accent1", "accent1"
                    "accent2", "accent2"
                    "accent3", "accent3"
                    "accent4", "accent4"
                    "accent5", "accent5"
                    "accent6", "accent6"
                    "hlink", "hlink"
                    "folHlink", "folHlink"
                ] do
                writer.WriteAttributeString(name, value)

            writer.WriteEndElement()
            writer.WriteStartElement("p", "sldLayoutIdLst", p)
            writer.WriteStartElement("p", "sldLayoutId", p)
            writer.WriteAttributeString("id", "2147483649")
            writer.WriteAttributeString("id", r, "rId1")
            writer.WriteEndElement()
            writer.WriteEndElement()
            writer.WriteEndElement()
        )

    let layout () =
        Packaging.xml (fun writer ->
            let p = Ooxml.p
            writer.WriteStartElement("p", "sldLayout", p)
            writer.WriteAttributeString("xmlns", "a", null, Ooxml.a)
            writer.WriteAttributeString("xmlns", "r", null, Ooxml.r)
            writer.WriteAttributeString("type", "blank")
            writer.WriteAttributeString("preserve", "1")
            writer.WriteStartElement("p", "cSld", p)
            writer.WriteAttributeString("name", "Blank")
            tree writer
            writer.WriteEndElement()
            writer.WriteEndElement()
            writer.WriteStartElement("p", "clrMapOvr", p)
            Ooxml.empty writer "a" "masterClrMapping" Ooxml.a
            writer.WriteEndElement()
            writer.WriteEndElement()
        )

    let theme (typeface: string) =
        Packaging.xml (fun writer ->
            let a = Ooxml.a

            let element name (attributes: (string * string) list) (body: unit -> unit) =
                writer.WriteStartElement("a", name, a)

                for key, value in attributes do
                    writer.WriteAttributeString(key, value)

                body ()
                writer.WriteEndElement()

            let srgb value =
                element "srgbClr" [ "val", value ] ignore

            let scheme value =
                element "schemeClr" [ "val", value ] ignore

            element
                "theme"
                [ "name", "Morphir" ]
                (fun () ->
                    element
                        "themeElements"
                        []
                        (fun () ->
                            element
                                "clrScheme"
                                [ "name", "Morphir" ]
                                (fun () ->
                                    element "dk1" [] (fun () -> srgb "000000")
                                    element "lt1" [] (fun () -> srgb "FFFFFF")
                                    element "dk2" [] (fun () -> srgb "111827")
                                    element "lt2" [] (fun () -> srgb "F4F5F7")

                                    for name, value in
                                        [
                                            "accent1", "0891B2"
                                            "accent2", "059669"
                                            "accent3", "7C3AED"
                                            "accent4", "D97706"
                                            "accent5", "E11D48"
                                            "accent6", "EA580C"
                                            "hlink", "0563C1"
                                            "folHlink", "954F72"
                                        ] do
                                        element name [] (fun () -> srgb value)
                                )

                            element
                                "fontScheme"
                                [ "name", "Morphir" ]
                                (fun () ->
                                    for font in [ "majorFont"; "minorFont" ] do
                                        element
                                            font
                                            []
                                            (fun () ->
                                                element "latin" [ "typeface", typeface ] ignore
                                                element "ea" [ "typeface", "" ] ignore
                                                element "cs" [ "typeface", "" ] ignore
                                            )
                                )

                            element
                                "fmtScheme"
                                [ "name", "Morphir" ]
                                (fun () ->
                                    element
                                        "fillStyleLst"
                                        []
                                        (fun () ->
                                            for _ in 1..3 do
                                                element
                                                    "solidFill"
                                                    []
                                                    (fun () -> scheme "phClr")
                                        )

                                    element
                                        "lnStyleLst"
                                        []
                                        (fun () ->
                                            for width in [ "6350"; "12700"; "19050" ] do
                                                element
                                                    "ln"
                                                    [ "w", width ]
                                                    (fun () ->
                                                        element
                                                            "solidFill"
                                                            []
                                                            (fun () -> scheme "phClr")
                                                    )
                                        )

                                    element
                                        "effectStyleLst"
                                        []
                                        (fun () ->
                                            for _ in 1..3 do
                                                element
                                                    "effectStyle"
                                                    []
                                                    (fun () -> element "effectLst" [] ignore)
                                        )

                                    element
                                        "bgFillStyleLst"
                                        []
                                        (fun () ->
                                            for _ in 1..3 do
                                                element
                                                    "solidFill"
                                                    []
                                                    (fun () -> scheme "phClr")
                                        )
                                )
                        )

                    element "objectDefaults" [] ignore
                    element "extraClrSchemeLst" [] ignore
                )
        )

/// One slide's XML and relationships.
[<RequireQualifiedAccess>]
module internal PowerPointSlide =
    let write
        (placement: SlidePlacement)
        (connectors: PowerPointConnectors)
        (typeface: string)
        (page: DiagramLayout)
        (number: int)
        (media: ResizeArray<PackageEntry>)
        (warnings: ResizeArray<Diagnostic>)
        (cancellationToken: CancellationToken)
        : Result<byte[] * byte[], Diagnostic> =
        let fit = placement.Fit(page, SlideGrid.Emu)
        let rels = ResizeArray<string * string * string * bool>()
        rels.Add(("rId1", Ooxml.relType "slideLayout", "../slideLayouts/slideLayout1.xml", false))

        let relId () =
            $"rId{rels.Count
                  + 1}"

        let ids = Dictionary<DrawableId, int>()
        let mutable nextId = 1
        let mutable failure: Diagnostic option = None
        let items = SlideItems.ofPage page

        let shapes = Dictionary<DrawableId, LayoutShape>()

        for item in items do
            nextId <- nextId + 1
            ids[item.Drawable.Id] <- nextId

            match item.Drawable.Kind with
            | DrawableKind.Shape shape -> shapes[item.Drawable.Id] <- shape
            | _ -> ()

        // Images are drawn first, so that a raster failure writes nothing.
        let images = Dictionary<DrawableId, string * string>()

        for item in items do
            match item.Drawable.Kind, failure with
            | DrawableKind.Image image, None ->
                cancellationToken.ThrowIfCancellationRequested()
                let box = fit.Box image.Box
                let name = $"image {item.Name} on slide {number}"

                Packaging.approximated image.SvgText name
                |> Option.iter warnings.Add

                match
                    Packaging.fallback
                        Packaging.rasterize
                        image.SvgText
                        (float (Ooxml.emu box.Width)
                         / 9525.0)
                        name
                with
                | Error error -> failure <- Some error
                | Ok png ->
                    let index =
                        media.Count
                        / 2
                        + 1

                    media.Add
                        {
                            Name = $"ppt/media/image{index}.png"
                            Bytes = png.Bytes
                            Stored = false
                        }

                    media.Add
                        {
                            Name = $"ppt/media/image{index}.svg"
                            Bytes = Encoding.UTF8.GetBytes(Packaging.portable image.SvgText)
                            Stored = false
                        }

                    let pngId = relId ()
                    rels.Add((pngId, Ooxml.relType "image", $"../media/image{index}.png", false))
                    let svgId = relId ()
                    rels.Add((svgId, Ooxml.relType "image", $"../media/image{index}.svg", false))
                    images[item.Drawable.Id] <- (pngId, svgId)
            | _ -> ()

        match failure with
        | Some error -> Error error
        | None ->
            let links = Dictionary<DrawableId, string>()

            for item in items do
                match
                    item.Drawable.Links
                    |> Seq.tryFind (fun link -> link.IsWeb)
                with
                | Some link ->
                    let id = relId ()
                    rels.Add((id, Ooxml.relType "hyperlink", link.Target, true))
                    links[item.Drawable.Id] <- id
                | None -> ()

            let slide =
                Packaging.xml (fun writer ->
                    let p, a = Ooxml.p, Ooxml.a
                    writer.WriteStartElement("p", "sld", p)
                    writer.WriteAttributeString("xmlns", "a", null, a)
                    writer.WriteAttributeString("xmlns", "r", null, Ooxml.r)
                    writer.WriteStartElement("p", "cSld", p)
                    writer.WriteStartElement("p", "bg", p)
                    writer.WriteStartElement("p", "bgPr", p)
                    Ooxml.solidColor writer page.Background
                    Ooxml.empty writer "a" "effectLst" a
                    writer.WriteEndElement()
                    writer.WriteEndElement()
                    PowerPointParts.openTree writer

                    let shapes =
                        PowerPointShapes(
                            writer,
                            fit,
                            typeface,
                            connectors,
                            ids,
                            shapes,
                            images,
                            links
                        )

                    for item in items do
                        shapes.Write item

                    writer.WriteEndElement()
                    writer.WriteEndElement()
                    writer.WriteStartElement("p", "clrMapOvr", p)
                    Ooxml.empty writer "a" "masterClrMapping" a
                    writer.WriteEndElement()
                    writer.WriteEndElement()
                )

            Ok(slide, PowerPointParts.rels (List.ofSeq rels))

/// Writes the parts of a presentation.
[<RequireQualifiedAccess>]
module internal PowerPointWriting =
    /// The package bytes and warnings, or the error that stops the render.
    let package
        (placement: SlidePlacement)
        (connectors: PowerPointConnectors)
        (typeface: string)
        (pages: ImmutableArray<DiagramLayout>)
        (cancellationToken: CancellationToken)
        : Result<byte[] * Diagnostic list, Diagnostic> =
        let warnings = ResizeArray<Diagnostic>()
        let media = ResizeArray<PackageEntry>()
        let slides = ResizeArray<byte[] * byte[]>()
        let mutable failure: Diagnostic option = None

        pages
        |> Seq.iteri (fun index page ->
            if failure.IsNone then
                match
                    PowerPointSlide.write
                        placement
                        connectors
                        typeface
                        page
                        (index + 1)
                        media
                        warnings
                        cancellationToken
                with
                | Ok slide -> slides.Add slide
                | Error error -> failure <- Some error
        )

        match failure with
        | Some error -> Error error
        | None ->
            let size = placement.Size
            let entries = ResizeArray<PackageEntry>()

            let add name bytes =
                entries.Add
                    {
                        Name = name
                        Bytes = bytes
                        Stored = false
                    }

            add "[Content_Types].xml" (PowerPointParts.contentTypes slides.Count media)

            add
                "_rels/.rels"
                (PowerPointParts.rels [
                    "rId1", Ooxml.relType "officeDocument", "ppt/presentation.xml", false
                ])

            add "ppt/presentation.xml" (PowerPointParts.presentation size slides.Count)

            add
                "ppt/_rels/presentation.xml.rels"
                (PowerPointParts.rels [
                    yield
                        "rId1", Ooxml.relType "slideMaster", "slideMasters/slideMaster1.xml", false
                    yield "rId2", Ooxml.relType "theme", "theme/theme1.xml", false
                    for index in 1 .. slides.Count ->
                        $"rId{index + 2}", Ooxml.relType "slide", $"slides/slide{index}.xml", false
                ])

            add "ppt/slideMasters/slideMaster1.xml" (PowerPointParts.master ())

            add
                "ppt/slideMasters/_rels/slideMaster1.xml.rels"
                (PowerPointParts.rels [
                    "rId1", Ooxml.relType "slideLayout", "../slideLayouts/slideLayout1.xml", false
                    "rId2", Ooxml.relType "theme", "../theme/theme1.xml", false
                ])

            add "ppt/slideLayouts/slideLayout1.xml" (PowerPointParts.layout ())

            add
                "ppt/slideLayouts/_rels/slideLayout1.xml.rels"
                (PowerPointParts.rels [
                    "rId1", Ooxml.relType "slideMaster", "../slideMasters/slideMaster1.xml", false
                ])

            add "ppt/theme/theme1.xml" (PowerPointParts.theme typeface)

            slides
            |> Seq.iteri (fun index (slide, rels) ->
                add $"ppt/slides/slide{index + 1}.xml" slide
                add $"ppt/slides/_rels/slide{index + 1}.xml.rels" rels
            )

            entries.AddRange media
            Ok(Packaging.zip (List.ofSeq entries), List.ofSeq warnings)

/// Writes layouts as a PowerPoint presentation: one slide per page, with native shapes, editable text, lines and
/// images. The presentation is written by hand as Office Open XML, with no third-party dependency.
[<Sealed>]
type PowerPointRenderer
    (placement: SlidePlacement, connectors: PowerPointConnectors, typeface: string) =
    inherit DiagramRenderer()

    do
        ArgumentNullException.ThrowIfNull placement
        ArgumentNullException.ThrowIfNull connectors

        if String.IsNullOrWhiteSpace typeface then
            invalidArg (nameof typeface) "A typeface is required."

    static let common =
        LayoutFeatures.Of(
            LayoutFeature.RoundedRectangles,
            LayoutFeature.Ellipses,
            LayoutFeature.Diamonds,
            LayoutFeature.Cylinders,
            LayoutFeature.Hexagons,
            LayoutFeature.Paths,
            LayoutFeature.OpenAndCircleHeads,
            LayoutFeature.WrappedText,
            LayoutFeature.RotatedText,
            LayoutFeature.InterleavedPainting,
            LayoutFeature.FillOpacity,
            LayoutFeature.StrokeAndTextOpacity,
            LayoutFeature.DashPatterns,
            LayoutFeature.Links,
            LayoutFeature.SvgImages,
            LayoutFeature.Pages
        )

    let features =
        match connectors with
        | PowerPointConnectors.Exact ->
            common
                .Add(LayoutFeature.Polylines)
                .Add(LayoutFeature.ExactRoutes)
                .Add(LayoutFeature.MessageHeads)
        | PowerPointConnectors.Glued -> common.Add LayoutFeature.AttachedConnectors

    /// A renderer with widescreen placement, exact connectors and the Consolas typeface.
    new() = PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Exact, "Consolas")

    /// A renderer with a placement, exact connectors and the Consolas typeface.
    new(placement: SlidePlacement) =
        PowerPointRenderer(placement, PowerPointConnectors.Exact, "Consolas")

    /// A renderer with a placement and a connector option, and the Consolas typeface.
    new(placement: SlidePlacement, connectors: PowerPointConnectors) =
        PowerPointRenderer(placement, connectors, "Consolas")

    /// Where diagrams go on the slides.
    member _.Placement = placement

    /// How lines are drawn.
    member _.Connectors = connectors

    /// The typeface of text.
    member _.Typeface = typeface

    /// application/vnd.openxmlformats-officedocument.presentationml.presentation.
    override _.MediaType =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation"

    /// .pptx.
    override _.FileExtension = ".pptx"

    /// The shape kinds, paths, open and circle heads, wrapped and rotated text, interleaved painting, opacity, dash
    /// patterns, links, SVG images and pages; with exact connectors also polylines, exact routes and message heads,
    /// and with glued connectors attached connectors.
    override _.NativeFeatures = features

    /// Writes the plan's pages as one presentation.
    override this.RenderAsync
        (
            plan: RenderPlan,
            output: Stream,
            cancellationToken: CancellationToken
        ) =
        ArgumentNullException.ThrowIfNull plan
        ArgumentNullException.ThrowIfNull output
        plan.EnsureFor this
        cancellationToken.ThrowIfCancellationRequested()

        match
            PowerPointWriting.package placement connectors typeface plan.Pages cancellationToken
        with
        | Error error -> Task.FromResult(ImmutableArray.Create<Diagnostic> error)
        | Ok(bytes, warnings) -> task {
            do! Packaging.copy bytes output cancellationToken
            return ImmutableArray.CreateRange<Diagnostic> warnings
          }
