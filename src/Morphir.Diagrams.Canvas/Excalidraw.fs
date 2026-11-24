// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Canvas

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Diagrams
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout

/// One Excalidraw element before it is written.
type internal ExcalidrawElement =
    {
        Id: string
        Kind: string
        Box: LayoutBox
        Stroke: string
        Background: string
        StrokeWidth: float
        Dashed: bool
        Rounded: int option
        Groups: string list
        Reference: string option
        Link: string option
        /// For text: the text, its size, alignment and the container it is bound to.
        Text: (string * Distance * string * string option) option
        /// For arrows: the points relative to the box's origin, the heads and the bindings.
        Arrow: ((float * float) list * string option * string option * (string * float) option * (string * float) option) option
        /// For images: the file identifier.
        File: string option
        Bound: ResizeArray<string * string>
    }

/// The elements and files of one scene.
type internal ExcalidrawScene(layout: DiagramLayout) =
    let elements = ResizeArray<ExcalidrawElement>()
    let byDrawable = Dictionary<DrawableId, ExcalidrawElement>()
    let files = SortedDictionary<string, string>(StringComparer.Ordinal)
    let mutable counter = 0

    let nextId () =
        counter <- counter + 1
        let digits = Convert.ToString(counter, 16).PadLeft(7, '0')
        "m" + digits

    let hex (color: LayoutColor | null) =
        match color with
        | null -> "transparent"
        | color -> CanvasWriting.hex color.Color

    let element kind (box: LayoutBox) (paint: LayoutPaint option) (groups: string list) reference link =
        {
            Id = nextId ()
            Kind = kind
            Box = box
            Stroke = paint |> Option.map (fun paint -> hex paint.Stroke) |> Option.defaultValue "transparent"
            Background = paint |> Option.map (fun paint -> hex paint.Fill) |> Option.defaultValue "transparent"
            StrokeWidth = paint |> Option.map (fun paint -> paint.StrokeWidth.ToPixels()) |> Option.defaultValue 1.0
            Dashed = paint |> Option.exists (fun paint -> not paint.Dash.IsEmpty)
            Rounded = None
            Groups = groups
            Reference = reference
            Link = link
            Text = None
            Arrow = None
            File = None
            Bound = ResizeArray()
        }

    let align (text: LayoutText) =
        match text.Align with
        | TextAlign.Start -> "left"
        | TextAlign.Middle -> "center"
        | TextAlign.End -> "right"

    // Excalidraw deletes empty text when it loads a scene, so empty text is left out.
    let rec textElement (text: LayoutText) (container: ExcalidrawElement option) groups reference link =
        if text.Lines |> Seq.forall String.IsNullOrEmpty then () else textElementOf text container groups reference link

    and textElementOf (text: LayoutText) (container: ExcalidrawElement option) groups reference link =
        let box =
            match text.Chip with
            | null -> text.Box
            | chip -> chip.Box

        let base' = element "text" box None groups reference link

        let textual =
            { base' with
                Stroke = CanvasWriting.hex text.Ink.Color
                Background = (match text.Chip with | null -> "transparent" | chip -> CanvasWriting.hex chip.Color.Color)
                Text = Some(String.Join("\n", text.Lines), text.Size, align text, container |> Option.map (fun container -> container.Id)) }

        container |> Option.iter (fun container -> container.Bound.Add((textual.Id, "text")))
        elements.Add textual

    let canContain kind = kind = "rectangle" || kind = "ellipse" || kind = "diamond" || kind = "arrow"

    /// The parts of a composite element or a group share its reference and its first web link.
    let rec visit (groups: string list) (inherited: string option) (inheritedLink: string option) (drawable: Drawable) =
        let reference = drawable.Reference |> Option.ofObj |> Option.map (fun reference -> reference.ToString()) |> Option.orElse inherited
        let link = drawable.Links |> Seq.tryFind (fun link -> link.IsWeb) |> Option.map (fun link -> link.Target) |> Option.orElse inheritedLink

        drawable.Kind.Match(
            (fun shape ->
                let kind =
                    match shape.Kind with
                    | ShapeKind.Ellipse -> "ellipse"
                    | ShapeKind.Diamond -> "diamond"
                    | _ -> "rectangle"

                let shaped =
                    { element kind shape.Box (Some shape.Paint) groups reference link with
                        Rounded = (match shape.Kind with ShapeKind.RoundedRectangle _ -> Some 3 | _ -> None) }

                elements.Add shaped
                byDrawable[drawable.Id] <- shaped

                match shape.Text with
                | null -> ()
                | text -> textElement text (if canContain kind then Some shaped else None) groups reference link),
            (fun line ->
                let points = line.Waypoints |> Seq.map (fun point -> point.X.ToPixels(), point.Y.ToPixels()) |> List.ofSeq
                let x0, y0 = List.head points
                let relative = points |> List.map (fun (x, y) -> x - x0, y - y0)
                let xs, ys = relative |> List.map fst, relative |> List.map snd
                let box = LayoutBox.Create(Distance.FromPixels x0, Distance.FromPixels y0, Distance.FromPixels(List.max xs - List.min xs), Distance.FromPixels(List.max ys - List.min ys))

                let head (end': LineEnd) =
                    match end'.Head with
                    | EdgeHead.None -> None
                    | EdgeHead.OpenArrow -> Some "arrow"
                    | EdgeHead.Circle -> Some "dot"
                    | _ -> Some "triangle"

                let arrow =
                    { element "arrow" box (Some line.Paint) groups reference link with
                        Rounded = (if line.Route = RouteStyle.Curved then Some 2 else None)
                        Arrow = Some(relative, head line.Source, head line.Target, None, None) }

                elements.Add arrow
                byDrawable[drawable.Id] <- arrow

                match line.Label with
                | null -> ()
                | text -> textElement text (Some arrow) groups reference link),
            (fun text -> textElement text None groups reference link),
            (fun image ->
                let digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes image.SvgText))
                let id = digest.Substring(0, 40)
                files[id] <- "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes image.SvgText)
                let pictured = { element "image" image.Box None groups reference link with File = Some id }
                elements.Add pictured
                byDrawable[drawable.Id] <- pictured),
            (fun children ->
                // A composite element is a group, innermost first.
                let group = $"g{drawable.Id.Value}"
                for child in children do visit (group :: groups) reference link child),
            (fun container ->
                // Excalidraw declares no containers, so lowering never sends one.
                for child in container.Children do visit groups reference None child),
            (fun children ->
                let group = $"g{drawable.Id.Value}"
                for child in children do visit (group :: groups) reference link child)
        )

    let ordered (drawables: ImmutableArray<Drawable>) =
        // Leaves in paint order, each with the groups and reference it inherits.
        let leaves = ResizeArray<int * int * (unit -> unit)>()

        let rec collect (groups: string list) (inherited: string option) (inheritedLink: string option) (drawable: Drawable) =
            let reference = drawable.Reference |> Option.ofObj |> Option.map (fun reference -> reference.ToString()) |> Option.orElse inherited
            let link = drawable.Links |> Seq.tryFind (fun link -> link.IsWeb) |> Option.map (fun link -> link.Target) |> Option.orElse inheritedLink

            match drawable.Kind with
            | DrawableKind.Element children
            | DrawableKind.Group children ->
                let group = $"g{drawable.Id.Value}"
                for child in children do collect (group :: groups) reference link child
            | DrawableKind.Container container ->
                for child in container.Children do collect groups reference None child
            | _ -> leaves.Add((drawable.PaintOrder, leaves.Count, fun () -> visit groups inherited inheritedLink drawable))

        for drawable in drawables do collect [] None None drawable
        leaves |> Seq.sortBy (fun (order, index, _) -> order, index) |> Seq.iter (fun (_, _, write) -> write ())

    do ordered layout.Drawables

    // Bindings: each arrow end bound to an element, mirrored in the element's bound elements.
    do
        let rec bind (drawable: Drawable) =
            match drawable.Kind with
            | DrawableKind.Line line ->
                let arrow = byDrawable[drawable.Id]

                let target (end': LineEnd) =
                    match end'.Binding with
                    | null -> None
                    | binding ->
                        match byDrawable.TryGetValue binding.Target with
                        | true, bound ->
                            if not (bound.Bound |> Seq.exists (fun (id, _) -> id = arrow.Id)) then bound.Bound.Add((arrow.Id, "arrow"))

                            // The gap from the end's point to the bound element's box.
                            let box = bound.Box
                            let point = end'.Point
                            let clamp (value: Distance) (low: Distance) (high: Distance) = Distance.Max(low, Distance.Min(value, high))
                            let dx = (point.X - clamp point.X box.X box.Right).ToPixels()
                            let dy = (point.Y - clamp point.Y box.Y box.Bottom).ToPixels()
                            Some(bound.Id, Math.Sqrt(dx * dx + dy * dy))
                        | false, _ -> None

                match arrow.Arrow with
                | Some(points, startHead, endHead, _, _) ->
                    let index = elements.FindIndex(fun element -> element.Id = arrow.Id)
                    let bound = { arrow with Arrow = Some(points, startHead, endHead, target line.Source, target line.Target) }
                    elements[index] <- bound
                    byDrawable[drawable.Id] <- bound
                | None -> ()
            | _ -> for child in drawable.Kind.Children do bind child

        for drawable in layout.Drawables do bind drawable

    member _.Elements = elements :> IReadOnlyList<_>
    member _.Files = files :> IReadOnlyDictionary<_, _>

/// Writes Excalidraw scenes.
[<RequireQualifiedAccess>]
module internal ExcalidrawWriting =
    let options = JsonWriterOptions(Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    // Numbers are written as decimals of at most four places, with no trailing zeros.
    let private decimalOf (text: string) = Decimal.Parse(text, Globalization.CultureInfo.InvariantCulture)

    let private number (writer: Utf8JsonWriter) (name: string) (value: float) =
        writer.WriteNumber(name, decimalOf (CanvasWriting.number value))

    let private px (writer: Utf8JsonWriter) (name: string) (value: Distance) =
        writer.WriteNumber(name, decimalOf (CanvasWriting.pixels value))

    /// The text of a scene for a page, with the hand-drawn roughness.
    let write (writer: Utf8JsonWriter) (layout: DiagramLayout) (roughness: int) =
        let scene = ExcalidrawScene(layout)
        writer.WriteStartObject()
        writer.WriteString("type", "excalidraw")
        writer.WriteNumber("version", 2)
        writer.WriteString("source", "https://github.com/finos/morphir-dotnet")
        writer.WriteStartArray "elements"

        for element in scene.Elements do
            writer.WriteStartObject()
            writer.WriteString("id", element.Id)
            writer.WriteString("type", element.Kind)
            px writer "x" element.Box.X
            px writer "y" element.Box.Y
            px writer "width" element.Box.Width
            px writer "height" element.Box.Height
            writer.WriteNumber("angle", 0)
            writer.WriteString("strokeColor", element.Stroke)
            writer.WriteString("backgroundColor", element.Background)
            writer.WriteString("fillStyle", "solid")
            number writer "strokeWidth" element.StrokeWidth
            writer.WriteString("strokeStyle", (if element.Dashed then "dashed" else "solid"))
            writer.WriteNumber("roughness", roughness)
            writer.WriteNumber("opacity", 100)
            writer.WriteStartArray "groupIds"
            for group in element.Groups do writer.WriteStringValue group
            writer.WriteEndArray()
            writer.WriteNull "frameId"

            match element.Rounded with
            | Some kind ->
                writer.WriteStartObject "roundness"
                writer.WriteNumber("type", kind)
                writer.WriteEndObject()
            | None -> writer.WriteNull "roundness"

            writer.WriteNumber("seed", int64 (CanvasWriting.fnv element.Id))
            writer.WriteNumber("version", 1)
            writer.WriteNumber("versionNonce", int64 (CanvasWriting.fnv (element.Id + "/nonce")))
            writer.WriteBoolean("isDeleted", false)

            if element.Bound.Count = 0 then
                writer.WriteNull "boundElements"
            else
                writer.WriteStartArray "boundElements"

                for id, kind in element.Bound do
                    writer.WriteStartObject()
                    writer.WriteString("id", id)
                    writer.WriteString("type", kind)
                    writer.WriteEndObject()

                writer.WriteEndArray()

            writer.WriteNumber("updated", 1)

            match element.Link with
            | Some link -> writer.WriteString("link", link)
            | None -> writer.WriteNull "link"

            writer.WriteBoolean("locked", false)

            match element.Text with
            | Some(text, size, align, container) ->
                px writer "fontSize" size
                writer.WriteNumber("fontFamily", 3)
                writer.WriteString("text", text)
                writer.WriteString("textAlign", align)
                writer.WriteString("verticalAlign", (if container.IsSome then "middle" else "top"))

                match container with
                | Some id -> writer.WriteString("containerId", id)
                | None -> writer.WriteNull "containerId"

                writer.WriteString("originalText", text)
                writer.WriteBoolean("autoResize", true)
                number writer "lineHeight" 1.25
            | None -> ()

            match element.Arrow with
            | Some(points, startHead, endHead, startBinding, endBinding) ->
                writer.WriteStartArray "points"

                for x, y in points do
                    writer.WriteStartArray()
                    writer.WriteNumberValue(decimalOf (CanvasWriting.number x))
                    writer.WriteNumberValue(decimalOf (CanvasWriting.number y))
                    writer.WriteEndArray()

                writer.WriteEndArray()
                writer.WriteNull "lastCommittedPoint"

                let binding (name: string) (value: (string * float) option) =
                    match value with
                    | Some(id, gap) ->
                        writer.WriteStartObject name
                        writer.WriteString("elementId", id)
                        writer.WriteNumber("focus", 0)
                        number writer "gap" gap
                        writer.WriteEndObject()
                    | None -> writer.WriteNull name

                binding "startBinding" startBinding
                binding "endBinding" endBinding

                match startHead with
                | Some head -> writer.WriteString("startArrowhead", head)
                | None -> writer.WriteNull "startArrowhead"

                match endHead with
                | Some head -> writer.WriteString("endArrowhead", head)
                | None -> writer.WriteNull "endArrowhead"

                writer.WriteBoolean("elbowed", false)
            | None -> ()

            match element.File with
            | Some id ->
                writer.WriteString("fileId", id)
                writer.WriteString("status", "saved")
                writer.WriteStartArray "scale"
                writer.WriteNumberValue 1
                writer.WriteNumberValue 1
                writer.WriteEndArray()
            | None -> ()

            match element.Reference with
            | Some reference ->
                writer.WriteStartObject "customData"
                writer.WriteString("morphirRef", reference)
                writer.WriteEndObject()
            | None -> ()

            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteStartObject "appState"
        writer.WriteString("viewBackgroundColor", CanvasWriting.hex layout.Background)
        writer.WriteNull "gridSize"
        writer.WriteEndObject()
        writer.WriteStartObject "files"

        for KeyValue(id, data) in scene.Files do
            writer.WriteStartObject id
            writer.WriteString("mimeType", "image/svg+xml")
            writer.WriteString("id", id)
            writer.WriteString("dataURL", data)
            writer.WriteNumber("created", 1)
            writer.WriteEndObject()

        writer.WriteEndObject()
        writer.WriteEndObject()
        scene

    /// The features that an Excalidraw scene draws natively.
    let features =
        LayoutFeatures.Of(
            LayoutFeature.RoundedRectangles,
            LayoutFeature.Ellipses,
            LayoutFeature.Diamonds,
            LayoutFeature.Polylines,
            LayoutFeature.SmoothCurves,
            LayoutFeature.AttachedConnectors,
            LayoutFeature.OpenAndCircleHeads,
            LayoutFeature.WrappedText,
            LayoutFeature.InterleavedPainting,
            LayoutFeature.Links,
            LayoutFeature.SvgImages
        )

    let checkRoughness (roughness: int) =
        if roughness < 0 || roughness > 2 then
            raise (ArgumentOutOfRangeException(nameof roughness, roughness, "Excalidraw's roughness is 0, 1 or 2."))

    let copy (buffer: MemoryStream) (output: Stream) (cancellationToken: CancellationToken) =
        task {
            use _ = buffer
            buffer.Position <- 0L
            do! buffer.CopyToAsync(output, cancellationToken)
            return ImmutableArray<Diagnostic>.Empty
        }

/// Writes a layout as an Excalidraw scene: shapes, bound text, arrows bound to their shapes, and images. Excalidraw
/// draws one page, so a plan with several pages is unsupported.
[<Sealed>]
type ExcalidrawRenderer(roughness: int) =
    inherit DiagramRenderer()
    do ExcalidrawWriting.checkRoughness roughness

    /// A renderer with no hand-drawn roughness.
    new() = ExcalidrawRenderer(0)

    /// The hand-drawn roughness of every shape and arrow: 0, 1 or 2.
    member _.Roughness = roughness

    /// application/vnd.excalidraw+json.
    override _.MediaType = "application/vnd.excalidraw+json"

    /// .excalidraw.
    override _.FileExtension = ".excalidraw"

    /// Rounded rectangles, ellipses, diamonds, polylines, smooth curves, attached connectors, open and circle heads,
    /// wrapped text, interleaved painting, links and SVG images.
    override _.NativeFeatures = ExcalidrawWriting.features

    /// Writes the plan's page as an Excalidraw scene.
    override this.RenderAsync(plan: RenderPlan, output: Stream, cancellationToken: CancellationToken) =
        ArgumentNullException.ThrowIfNull plan
        ArgumentNullException.ThrowIfNull output
        plan.EnsureFor this
        let buffer = new MemoryStream()

        do
            use writer = new Utf8JsonWriter(buffer, ExcalidrawWriting.options)
            ExcalidrawWriting.write writer plan.Pages[0] roughness |> ignore

        buffer.WriteByte(byte '\n')
        ExcalidrawWriting.copy buffer output cancellationToken
