// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Canvas

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Net
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Xml
open Morphir.Diagrams
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout

/// One cell of a draw.io page, before it is written.
type internal DrawIoCell =
    {
        Id: string
        Parent: string
        PaintOrder: int
        Label: string
        Reference: string option
        Link: string option
        Style: string
        /// A vertex box, relative to its parent.
        Box: LayoutBox option
        /// An edge's ends and interior points, relative to its parent.
        Edge: (string option * LayoutPoint * string option * LayoutPoint * LayoutPoint list) option
    }

[<RequireQualifiedAccess>]
module internal DrawIoWriting =
    let private pixels = CanvasWriting.pixels

    /// A style string with each key once, the first value winning.
    let private style (entries: string list) =
        entries
        |> List.distinctBy (fun entry -> match entry.IndexOf '=' with | -1 -> entry | at -> entry.Substring(0, at))
        |> String.concat ";"

    let private opacity (color: LayoutColor) =
        int (Math.Round(color.Color.Opacity * 100m, MidpointRounding.ToEven))

    /// HTML for a label: each line escaped, joined by line breaks, so draw.io shows the text exactly.
    let label (lines: string seq) =
        lines |> Seq.map WebUtility.HtmlEncode |> String.concat "<br>"

    let private colour name (value: LayoutColor option) =
        match value with
        | Some color -> [ $"{name}Color={CanvasWriting.hex color.Color}"; if not color.IsOpaque then $"{name}Opacity={opacity color}" ]
        | None -> [ $"{name}Color=none" ]

    let private stroke (paint: LayoutPaint) =
        [
            yield! colour "stroke" (Option.ofObj paint.Stroke)
            if paint.Stroke <> null then $"strokeWidth={pixels paint.StrokeWidth}"

            if not paint.Dash.IsEmpty then
                let width = if paint.StrokeWidth.QuarterEmu > 0L then paint.StrokeWidth.ToPixels() else 1.0
                let pattern = paint.Dash |> Seq.map (fun dash -> CanvasWriting.number (dash.ToPixels() / width)) |> String.concat " "
                "dashed=1"
                $"dashPattern={pattern}"
        ]

    let private font (text: LayoutText) =
        [
            "html=1"
            $"fontSize={pixels text.Size}"
            "fontFamily=JetBrains Mono"
            $"fontColor={CanvasWriting.hex text.Ink.Color}"
            if not text.Ink.IsOpaque then $"textOpacity={opacity text.Ink}"
            if text.Weight >= 600 then "fontStyle=1"
            if text.Wraps then "whiteSpace=wrap"
            if text.Rotation <> 0 then $"rotation={text.Rotation}"
        ]

    /// The draw.io style of a shape kind, or of the family role that draw.io has a symbol for.
    let private kind (shape: LayoutShape) =
        let symbol =
            match shape.Role with
            | null -> None
            | role when role.Equals FamilyRole.Actor -> Some [ "shape=umlActor"; "verticalLabelPosition=bottom"; "verticalAlign=top" ]
            | role when role.Equals FamilyRole.Gateway -> Some [ "shape=mxgraph.bpmn.gateway2"; "gwType=exclusive"; "perimeter=rhombusPerimeter"; "outline=none"; "symbol=none" ]
            | role when role.Equals FamilyRole.Event -> Some [ "shape=mxgraph.bpmn.event"; "outline=standard"; "symbol=general"; "perimeter=ellipsePerimeter" ]
            | role when role.Equals FamilyRole.Task -> Some [ "shape=mxgraph.bpmn.task"; "taskMarker=abstract" ]
            | role when role.Equals FamilyRole.DataObject -> Some [ "shape=mxgraph.bpmn.data"; "perimeter=rectanglePerimeter" ]
            | role when role.Equals FamilyRole.Final -> Some [ "shape=endState"; "perimeter=ellipsePerimeter" ]
            | role when role.Equals FamilyRole.Note -> Some [ "shape=note"; "size=10" ]
            | _ -> None

        match symbol with
        | Some style -> style
        | None ->
            match shape.Kind with
            | ShapeKind.Rectangle -> [ "rounded=0" ]
            | ShapeKind.RoundedRectangle radius -> [ "rounded=1"; "absoluteArcSize=1"; $"arcSize={pixels (radius + radius)}" ]
            | ShapeKind.Ellipse -> [ "ellipse" ]
            | ShapeKind.Diamond -> [ "rhombus" ]
            | ShapeKind.Cylinder cap -> [ "shape=cylinder3"; "boundedLbl=1"; "backgroundOutline=1"; $"size={pixels cap}" ]
            | ShapeKind.Hexagon inset -> [ "shape=hexagon"; "perimeter=hexagonPerimeter2"; "fixedSize=1"; $"size={pixels inset}" ]
            | ShapeKind.Path _ -> [ "rounded=0" ]

    let shapeStyle (shape: LayoutShape) =
        [
            yield! kind shape
            yield! colour "fill" (Option.ofObj shape.Paint.Fill)
            yield! stroke shape.Paint
            match shape.Text with
            | null -> "html=1"
            | text -> yield! font text
        ]
        |> style

    let textStyle (text: LayoutText) =
        [
            "text"
            yield! font text
            "strokeColor=none"
            match text.Chip with
            | null -> "fillColor=none"
            | chip ->
                yield! colour "fill" (Some chip.Color)
                if chip.Radius.QuarterEmu > 0L then
                    "rounded=1"
                    "absoluteArcSize=1"
                    $"arcSize={pixels (chip.Radius + chip.Radius)}"
            match text.Align with
            | TextAlign.Start -> "align=left"
            | TextAlign.Middle -> "align=center"
            | TextAlign.End -> "align=right"
            "verticalAlign=middle"
            "spacing=0"
        ]
        |> style

    let imageStyle (image: LayoutImage) =
        let data = Convert.ToBase64String(Encoding.UTF8.GetBytes image.SvgText)
        $"shape=image;imageAspect=0;aspect=fixed;image=data:image/svg+xml,{data}"

    let containerStyle (container: LayoutContainer) =
        [
            if container.HeaderBand.QuarterEmu > 0L then
                "swimlane"
                "horizontal=0"
                $"startSize={pixels container.HeaderBand}"
            else
                if container.Radius.QuarterEmu > 0L then "rounded=1;absoluteArcSize=1" else "rounded=0"
                if container.Radius.QuarterEmu > 0L then $"arcSize={pixels (container.Radius + container.Radius)}"
                "verticalAlign=top"

            "container=1"
            "collapsible=0"
            yield! colour "fill" (Option.ofObj container.Paint.Fill)
            yield! stroke container.Paint
            match container.Header with
            | null -> "html=1"
            | header -> yield! font header
        ]
        |> style

    let private marker (head: EdgeHead) =
        match head with
        | EdgeHead.None -> "none", "0"
        | EdgeHead.OpenArrow -> "open", "0"
        | EdgeHead.Circle -> "oval", "1"
        | _ -> "block", "1"

    let edgeStyle (line: LayoutLine) (sourceAnchor: string list) (targetAnchor: string list) =
        let startArrow, startFill = marker line.Source.Head
        let endArrow, endFill = marker line.Target.Head

        [
            match line.Route with
            | RouteStyle.Orthogonal -> "edgeStyle=orthogonalEdgeStyle"
            | RouteStyle.Curved -> "curved=1"
            | RouteStyle.Straight -> "edgeStyle=none"
            "rounded=0"
            "html=1"
            $"startArrow={startArrow}"
            $"startFill={startFill}"
            $"endArrow={endArrow}"
            $"endFill={endFill}"
            yield! stroke line.Paint
            yield! sourceAnchor
            yield! targetAnchor
            match line.Label with
            | null -> ()
            | text ->
                yield! font text
                match text.Chip with
                | null -> ()
                | chip -> $"labelBackgroundColor={CanvasWriting.hex chip.Color.Color}"
        ]
        |> style

    let anchor (prefix: string) (binding: LineBinding) =
        binding.Anchor.Match(
            (fun point -> [ $"{prefix}X={CanvasWriting.number point.X.Value}"; $"{prefix}Y={CanvasWriting.number point.Y.Value}"; $"{prefix}Dx=0"; $"{prefix}Dy=0" ]),
            (fun _ -> [])
        )

/// The cells of one page.
type internal DrawIoPage(layout: DiagramLayout) =
    let cells = ResizeArray<DrawIoCell>()
    let containers = Dictionary<DrawableId, LayoutBox>()
    let parents = Dictionary<DrawableId, DrawableId option>()

    let relative (parent: DrawableId option) (point: LayoutPoint) =
        match parent with
        | Some parent -> point.Offset(-containers[parent].X, -containers[parent].Y)
        | None -> point

    let parentId (parent: DrawableId option) =
        parent |> Option.map (fun id -> id.Value) |> Option.defaultValue "1"

    /// The own reference and first web link of a drawable, or those that it inherits from its composite element or group.
    let own (drawable: Drawable) (inheritedReference: string option, inheritedLink: string option) =
        drawable.Reference |> Option.ofObj |> Option.map (fun reference -> reference.ToString()) |> Option.orElse inheritedReference,
        drawable.Links |> Seq.tryFind (fun link -> link.IsWeb) |> Option.map (fun link -> link.Target) |> Option.orElse inheritedLink

    let cell (drawable: Drawable) (parent: DrawableId option) (inherited: string option * string option) label style box edge =
        let reference, link = own drawable inherited

        {
            Id = drawable.Id.Value
            Parent = parentId parent
            PaintOrder = drawable.PaintOrder
            Label = label
            Reference = reference
            Link = link
            Style = style
            Box = box
            Edge = edge
        }

    let vertexBox (parent: DrawableId option) (box: LayoutBox) =
        let origin = relative parent (LayoutPoint.Create(box.X, box.Y))
        LayoutBox.Create(origin.X, origin.Y, box.Width, box.Height)

    // First pass: every container's box and every drawable's nearest container.
    let rec place (parent: DrawableId option) (drawable: Drawable) =
        parents[drawable.Id] <- parent

        drawable.Kind.Match(
            (fun _ -> ()),
            (fun _ -> ()),
            (fun _ -> ()),
            (fun _ -> ()),
            (fun children -> for child in children do place parent child),
            (fun container ->
                containers[drawable.Id] <- container.Box
                for child in container.Children do place (Some drawable.Id) child),
            (fun children -> for child in children do place parent child)
        )

    do for drawable in layout.Drawables do place None drawable

    let rec ancestors (id: DrawableId) =
        match parents.TryGetValue id with
        | true, Some parent -> parent :: ancestors parent
        | _ -> []

    let rec visit (inherited: string option * string option) (drawable: Drawable) =
        let parent = parents[drawable.Id]
        let own = own drawable inherited

        drawable.Kind.Match(
            (fun shape ->
                let label = match shape.Text with | null -> "" | text -> DrawIoWriting.label text.Lines
                cells.Add(cell drawable parent inherited label (DrawIoWriting.shapeStyle shape) (Some(vertexBox parent shape.Box)) None)),
            (fun line ->
                let ends = [ line.Source; line.Target ] |> List.choose (fun lineEnd -> lineEnd.Binding |> Option.ofObj |> Option.map (fun binding -> binding.Target))

                // The nearest container that holds both bound ends, or the layer.
                let edgeParent =
                    match ends with
                    | [] -> parent
                    | first :: rest ->
                        let chains = ends |> List.map (fun id -> Set.ofList (ancestors id))
                        let common = chains |> List.reduce Set.intersect
                        (ancestors first) |> List.tryFind common.Contains

                let interior = line.Waypoints |> Seq.skip 1 |> Seq.truncate (max 0 (line.Waypoints.Length - 2)) |> Seq.map (relative edgeParent) |> List.ofSeq
                let source = line.Source.Binding |> Option.ofObj
                let target = line.Target.Binding |> Option.ofObj
                let style =
                    DrawIoWriting.edgeStyle
                        line
                        (source |> Option.map (DrawIoWriting.anchor "exit") |> Option.defaultValue [])
                        (target |> Option.map (DrawIoWriting.anchor "entry") |> Option.defaultValue [])

                let label = match line.Label with | null -> "" | text -> DrawIoWriting.label text.Lines

                cells.Add(
                    cell
                        drawable
                        edgeParent
                        inherited
                        label
                        style
                        None
                        (Some(
                            source |> Option.map (fun binding -> binding.Target.Value),
                            relative edgeParent line.Source.Point,
                            target |> Option.map (fun binding -> binding.Target.Value),
                            relative edgeParent line.Target.Point,
                            interior
                        ))
                )),
            (fun text -> cells.Add(cell drawable parent inherited (DrawIoWriting.label text.Lines) (DrawIoWriting.textStyle text) (Some(vertexBox parent text.Box)) None)),
            (fun image -> cells.Add(cell drawable parent inherited "" (DrawIoWriting.imageStyle image) (Some(vertexBox parent image.Box)) None)),
            (fun children -> for child in children do visit own child),
            (fun container ->
                let label = match container.Header with | null -> "" | header -> DrawIoWriting.label header.Lines
                cells.Add(cell drawable parent inherited label (DrawIoWriting.containerStyle container) (Some(vertexBox parent container.Box)) None)
                for child in container.Children do visit (None, None) child),
            (fun children -> for child in children do visit own child)
        )

    do for drawable in layout.Drawables do visit (None, None) drawable

    /// The cells, parents before children and siblings in paint order.
    member _.Cells =
        let byParent = cells |> Seq.groupBy (fun cell -> cell.Parent) |> Seq.map (fun (parent, items) -> parent, items |> Seq.sortBy (fun cell -> cell.PaintOrder) |> List.ofSeq) |> dict

        let rec under (parent: string) : DrawIoCell list =
            match byParent.TryGetValue parent with
            | true, children -> children |> List.collect (fun child -> child :: under child.Id)
            | false, _ -> []

        under "1"

/// Writes layouts as an uncompressed draw.io file: one diagram per page, each cell wrapped in a UserObject that keeps
/// its Morphir element reference, with edges attached to the cells of their bound ends.
[<Sealed>]
type DrawIoRenderer() =
    inherit DiagramRenderer()

    static let features =
        LayoutFeatures.Of(
            LayoutFeature.RoundedRectangles,
            LayoutFeature.Ellipses,
            LayoutFeature.Diamonds,
            LayoutFeature.Cylinders,
            LayoutFeature.Hexagons,
            LayoutFeature.Polylines,
            LayoutFeature.SmoothCurves,
            LayoutFeature.AttachedConnectors,
            LayoutFeature.OpenAndCircleHeads,
            LayoutFeature.WrappedText,
            LayoutFeature.RotatedText,
            LayoutFeature.Containers,
            LayoutFeature.FamilyRoles,
            LayoutFeature.InterleavedPainting,
            LayoutFeature.FillOpacity,
            LayoutFeature.StrokeAndTextOpacity,
            LayoutFeature.DashPatterns,
            LayoutFeature.Links,
            LayoutFeature.SvgImages,
            LayoutFeature.Pages
        )

    /// application/vnd.jgraph.mxfile.
    override _.MediaType = "application/vnd.jgraph.mxfile"

    /// .drawio.
    override _.FileExtension = ".drawio"

    /// The features draw.io draws natively. Paths reach it as SVG images, exact routes as waypoints and message heads
    /// as arrows.
    override _.NativeFeatures = features

    /// Writes the plan's pages as one draw.io file.
    override this.RenderAsync(plan: RenderPlan, output: Stream, cancellationToken: CancellationToken) =
        ArgumentNullException.ThrowIfNull plan
        ArgumentNullException.ThrowIfNull output
        plan.EnsureFor this

        let buffer = new MemoryStream()
        let settings = XmlWriterSettings(Encoding = UTF8Encoding(false), Indent = true, NewLineChars = "\n", OmitXmlDeclaration = true)

        do
            use writer = XmlWriter.Create(buffer, settings)
            writer.WriteStartElement "mxfile"

            plan.Pages
            |> Seq.iteri (fun index layout ->
                let page = DrawIoPage(layout)
                writer.WriteStartElement "diagram"
                writer.WriteAttributeString("id", $"page-{index + 1}")
                writer.WriteAttributeString("name", $"Page {index + 1}")
                writer.WriteStartElement "mxGraphModel"

                for name, value in
                    [
                        "grid", "0"
                        "gridSize", "10"
                        "guides", "1"
                        "tooltips", "1"
                        "connect", "1"
                        "arrows", "1"
                        "fold", "1"
                        "page", "0"
                        "pageScale", "1"
                        "pageWidth", CanvasWriting.pixels layout.Bounds.Width
                        "pageHeight", CanvasWriting.pixels layout.Bounds.Height
                        "background", CanvasWriting.hex layout.Background
                        "math", "0"
                        "shadow", "0"
                    ] do
                    writer.WriteAttributeString(name, value)

                writer.WriteStartElement "root"
                writer.WriteStartElement "mxCell"
                writer.WriteAttributeString("id", "0")
                writer.WriteEndElement()
                writer.WriteStartElement "mxCell"
                writer.WriteAttributeString("id", "1")
                writer.WriteAttributeString("parent", "0")
                writer.WriteEndElement()

                let point (name: string) (value: LayoutPoint) (role: string option) =
                    writer.WriteStartElement "mxPoint"
                    writer.WriteAttributeString("x", CanvasWriting.pixels value.X)
                    writer.WriteAttributeString("y", CanvasWriting.pixels value.Y)
                    role |> Option.iter (fun role -> writer.WriteAttributeString("as", role))
                    writer.WriteEndElement()
                    ignore name

                for cell in page.Cells do
                    writer.WriteStartElement "UserObject"
                    writer.WriteAttributeString("label", cell.Label)
                    cell.Reference |> Option.iter (fun reference -> writer.WriteAttributeString("morphir-ref", reference))
                    cell.Link |> Option.iter (fun link -> writer.WriteAttributeString("link", link))
                    writer.WriteAttributeString("id", cell.Id)
                    writer.WriteStartElement "mxCell"
                    writer.WriteAttributeString("style", cell.Style)

                    match cell.Edge with
                    | Some(source, _, target, _, _) ->
                        writer.WriteAttributeString("edge", "1")
                        writer.WriteAttributeString("parent", cell.Parent)
                        source |> Option.iter (fun id -> writer.WriteAttributeString("source", id))
                        target |> Option.iter (fun id -> writer.WriteAttributeString("target", id))
                    | None ->
                        writer.WriteAttributeString("vertex", "1")
                        writer.WriteAttributeString("parent", cell.Parent)

                    writer.WriteStartElement "mxGeometry"

                    match cell.Box, cell.Edge with
                    | Some box, _ ->
                        writer.WriteAttributeString("x", CanvasWriting.pixels box.X)
                        writer.WriteAttributeString("y", CanvasWriting.pixels box.Y)
                        writer.WriteAttributeString("width", CanvasWriting.pixels box.Width)
                        writer.WriteAttributeString("height", CanvasWriting.pixels box.Height)
                        writer.WriteAttributeString("as", "geometry")
                    | None, Some(source, start, target, finish, interior) ->
                        writer.WriteAttributeString("relative", "1")
                        writer.WriteAttributeString("as", "geometry")
                        if source.IsNone then point "source" start (Some "sourcePoint")
                        if target.IsNone then point "target" finish (Some "targetPoint")

                        if not interior.IsEmpty then
                            writer.WriteStartElement "Array"
                            writer.WriteAttributeString("as", "points")
                            for waypoint in interior do point "point" waypoint None
                            writer.WriteEndElement()
                    | None, None -> writer.WriteAttributeString("as", "geometry")

                    writer.WriteEndElement()
                    writer.WriteEndElement()
                    writer.WriteEndElement()

                writer.WriteEndElement()
                writer.WriteEndElement()
                writer.WriteEndElement())

            writer.WriteEndElement()

        buffer.WriteByte(byte '\n')

        task {
            use _ = buffer
            buffer.Position <- 0L
            do! buffer.CopyToAsync(output, cancellationToken)
            return ImmutableArray<Diagnostic>.Empty
        }
