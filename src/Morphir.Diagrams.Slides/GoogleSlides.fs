// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Slides

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Runtime.InteropServices
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Diagrams
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout

module internal SlideObjectText =
    /// A character that a Slides object ID allows.
    let allowed (c: char) = Char.IsAsciiLetterOrDigit c || c = '_' || c = '-' || c = ':'

/// A prefix for the IDs of the objects that the Google Slides renderer creates: 2 to 30 ASCII letters, digits,
/// underscores, hyphens and colons, starting with a letter, digit or underscore. The caller chooses one that the
/// presentation does not use yet.
[<Sealed>]
type SlideObjectNamespace private (value: string) =
    /// The namespace text.
    member _.Value = value

    override _.ToString() = value

    override _.Equals(other: obj | null) =
        match other with
        | :? SlideObjectNamespace as other -> String.Equals(other.Value, value, StringComparison.Ordinal)
        | _ -> false

    override _.GetHashCode() = StringComparer.Ordinal.GetHashCode value

    interface IEquatable<SlideObjectNamespace> with
        member _.Equals(other: SlideObjectNamespace | null) =
            match other with
            | null -> false
            | other -> String.Equals(other.Value, value, StringComparison.Ordinal)

    /// A namespace, or the reason the text is not one.
    static member Parse(text: string | null) : Result<SlideObjectNamespace, Diagnostic> =
        match text with
        | null -> Error(Diagnostic.Error("slides/invalid-namespace", "An object ID namespace is required."))
        | text when
            text.Length >= 2
            && text.Length <= 30
            && (Char.IsAsciiLetterOrDigit text[0] || text[0] = '_')
            && Seq.forall SlideObjectText.allowed text
            ->
            Ok(SlideObjectNamespace text)
        | text ->
            Error(
                Diagnostic.Error(
                    "slides/invalid-namespace",
                    $"'{text}' is not an object ID namespace: use 2 to 30 ASCII letters, digits, underscores, hyphens and colons, starting with a letter, digit or underscore."
                )
            )

    /// Parses a namespace for C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<SlideObjectNamespace>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Diagnostic>
        ) =
        match SlideObjectNamespace.Parse text with
        | Ok parsed ->
            result <- parsed
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// A namespace from a literal in source code. Throws an argument error for invalid text.
    static member Create(literal: string) =
        match SlideObjectNamespace.Parse literal with
        | Ok parsed -> parsed
        | Error error -> invalidArg (nameof literal) error.Message

/// The object ID of an existing slide: 1 to 50 ASCII letters, digits, underscores, hyphens and colons. Google's own
/// first slide often has the ID p.
[<Sealed>]
type SlideObjectId private (value: string) =
    /// The ID text.
    member _.Value = value

    override _.ToString() = value

    override _.Equals(other: obj | null) =
        match other with
        | :? SlideObjectId as other -> String.Equals(other.Value, value, StringComparison.Ordinal)
        | _ -> false

    override _.GetHashCode() = StringComparer.Ordinal.GetHashCode value

    interface IEquatable<SlideObjectId> with
        member _.Equals(other: SlideObjectId | null) =
            match other with
            | null -> false
            | other -> String.Equals(other.Value, value, StringComparison.Ordinal)

    /// An object ID, or the reason the text is not one.
    static member Parse(text: string | null) : Result<SlideObjectId, Diagnostic> =
        match text with
        | null -> Error(Diagnostic.Error("slides/invalid-object-id", "A slide object ID is required."))
        | text when text.Length >= 1 && text.Length <= 50 && Seq.forall SlideObjectText.allowed text -> Ok(SlideObjectId text)
        | text ->
            Error(
                Diagnostic.Error(
                    "slides/invalid-object-id",
                    $"'{text}' is not a slide object ID: use 1 to 50 ASCII letters, digits, underscores, hyphens and colons."
                )
            )

    /// Parses an object ID for C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<SlideObjectId>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Diagnostic>
        ) =
        match SlideObjectId.Parse text with
        | Ok parsed ->
            result <- parsed
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// An object ID from a literal in source code. Throws an argument error for invalid text.
    static member Create(literal: string) =
        match SlideObjectId.Parse literal with
        | Ok parsed -> parsed
        | Error error -> invalidArg (nameof literal) error.Message

/// What the Google Slides renderer needs to know about the target presentation: where diagrams go on its pages (the
/// placement's slide size is the presentation's page size), an unused object ID namespace, and optionally an existing
/// slide to draw one page on.
[<Sealed>]
type GoogleSlidesOptions private (placement: SlidePlacement, objects: SlideObjectNamespace, existing: SlideObjectId option) =
    /// Options that create one new slide for each page.
    static member Create(placement: SlidePlacement, objectNamespace: SlideObjectNamespace) =
        ArgumentNullException.ThrowIfNull placement
        ArgumentNullException.ThrowIfNull objectNamespace
        GoogleSlidesOptions(placement, objectNamespace, None)

    /// Where diagrams go on the pages; its slide size is the presentation's page size.
    member _.Placement = placement

    /// The prefix of every created object's ID.
    member _.ObjectNamespace = objects

    /// The existing slide to draw on, or null to create slides.
    member _.ExistingSlide: SlideObjectId | null = Option.toObj existing

    /// These options with an existing slide, which takes exactly one page.
    member _.WithExistingSlide(slide: SlideObjectId) =
        ArgumentNullException.ThrowIfNull slide
        GoogleSlidesOptions(placement, objects, Some slide)

    override _.ToString() =
        match existing with
        | Some slide -> $"GoogleSlidesOptions({objects}, slide {slide})"
        | None -> $"GoogleSlidesOptions({objects}, new slides)"

/// Writes the request body of a Slides API batch update.
[<RequireQualifiedAccess>]
module internal GoogleRequests =
    /// The most pages and objects per page, which keep every ID within 50 characters.
    let pageLimit = 9_999
    let objectLimit = 9_999_999

    type private Writer = Utf8JsonWriter

    let private emu (writer: Writer) (name: string) (value: Distance) =
        writer.WriteStartObject name
        writer.WriteNumber("magnitude", value.ToEmu())
        writer.WriteString("unit", "EMU")
        writer.WriteEndObject()

    let private rgb (writer: Writer) (color: Color) =
        writer.WriteStartObject "rgbColor"
        writer.WriteNumber("red", Math.Round(float color.R / 255.0, 4))
        writer.WriteNumber("green", Math.Round(float color.G / 255.0, 4))
        writer.WriteNumber("blue", Math.Round(float color.B / 255.0, 4))
        writer.WriteEndObject()

    let private solid (writer: Writer) (color: Color) (alpha: bool) =
        writer.WriteStartObject "solidFill"
        writer.WriteStartObject "color"
        rgb writer color
        writer.WriteEndObject()
        writer.WriteNumber("alpha", (if alpha then Math.Round(float color.Opacity, 4) else 1.0))
        writer.WriteEndObject()

    let private elementProperties (writer: Writer) (page: string) (box: LayoutBox) (flipX: bool) (flipY: bool) =
        writer.WriteStartObject "elementProperties"
        writer.WriteString("pageObjectId", page)
        writer.WriteStartObject "size"
        emu writer "width" (Distance.Max(box.Width, Distance.FromEmu 1L))
        emu writer "height" (Distance.Max(box.Height, Distance.FromEmu 1L))
        writer.WriteEndObject()
        writer.WriteStartObject "transform"
        writer.WriteNumber("scaleX", (if flipX then -1 else 1))
        writer.WriteNumber("scaleY", (if flipY then -1 else 1))
        writer.WriteNumber("translateX", (if flipX then box.Right else box.X).ToEmu())
        writer.WriteNumber("translateY", (if flipY then box.Bottom else box.Y).ToEmu())
        writer.WriteString("unit", "EMU")
        writer.WriteEndObject()
        writer.WriteEndObject()

    let private shapeType (kind: ShapeKind) =
        match kind with
        | ShapeKind.RoundedRectangle _ -> "ROUND_RECTANGLE"
        | ShapeKind.Ellipse -> "ELLIPSE"
        | ShapeKind.Diamond -> "DIAMOND"
        | ShapeKind.Cylinder _ -> "CAN"
        | ShapeKind.Hexagon _ -> "HEXAGON"
        | _ -> "RECTANGLE"

    let private arrow (head: EdgeHead) =
        match head with
        | EdgeHead.None -> "NONE"
        | EdgeHead.OpenArrow -> "OPEN_ARROW"
        | EdgeHead.Circle -> "FILL_CIRCLE"
        | _ -> "FILL_ARROW"

    /// One page's objects: what to create, then style, then connect.
    type private Planned =
        | PlannedShape of id: string * shape: LayoutShape * link: string option
        | PlannedText of id: string * text: LayoutText * link: string option
        | PlannedLine of id: string * line: LayoutLine * link: string option

    /// A text's box on the slide.
    let private textBox (fit: SlideFit) (body: LayoutText) = fit.Box body.Box

    /// Writes the requests of all pages, or the error that stops the render.
    let write (options: GoogleSlidesOptions) (pages: ImmutableArray<DiagramLayout>) (cancellationToken: CancellationToken) : Result<byte[], Diagnostic> =
        let existing = options.ExistingSlide

        if not (isNull existing) && pages.Length <> 1 then
            Error(Diagnostic.Error("slides/existing-slide-pages", $"An existing slide takes exactly one page, but the plan has {pages.Length} pages."))
        elif pages.Length > pageLimit then
            Error(Diagnostic.Error("slides/page-limit", $"Google Slides requests hold at most 9,999 pages, but the plan has {pages.Length:N0}."))
        else
            let planned =
                pages
                |> Seq.mapi (fun index page ->
                    let items = SlideItems.ofPage page

                    let drawn =
                        items
                        |> List.filter (fun item ->
                            match item.Drawable.Kind with
                            | DrawableKind.Shape _
                            | DrawableKind.Text _ -> true
                            | DrawableKind.Line line -> true
                            | _ -> false)

                    let labels = drawn |> List.sumBy (fun item -> match item.Drawable.Kind with | DrawableKind.Line line when not (isNull line.Label) -> 1 | _ -> 0)
                    index, page, items, drawn.Length + labels)
                |> List.ofSeq

            match planned |> List.tryFind (fun (_, _, _, count) -> count > objectLimit) with
            | Some(index, _, _, count) ->
                Error(Diagnostic.Error("slides/object-limit", $"Google Slides requests hold at most 9,999,999 objects on a page, but page {index + 1} has {count:N0}."))
            | None ->
                use buffer = new MemoryStream()

                do
                    use writer = new Utf8JsonWriter(buffer, JsonWriterOptions(Indented = true, NewLine = "\n"))
                    let prefix = options.ObjectNamespace.Value
                    writer.WriteStartObject()
                    writer.WriteStartArray "requests"

                    let request (kind: string) (body: unit -> unit) =
                        writer.WriteStartObject()
                        writer.WriteStartObject kind
                        body ()
                        writer.WriteEndObject()
                        writer.WriteEndObject()

                    for index, page, items, _ in planned do
                        cancellationToken.ThrowIfCancellationRequested()
                        let fit = options.Placement.Fit(page, SlideGrid.Emu)

                        let slide =
                            match existing with
                            | null -> $"{prefix}_{index}_s0"
                            | slide -> slide.Value

                        if isNull existing then
                            request "createSlide" (fun () ->
                                writer.WriteString("objectId", slide)
                                writer.WriteStartObject "slideLayoutReference"
                                writer.WriteString("predefinedLayout", "BLANK")
                                writer.WriteEndObject())

                        let mutable next = 0

                        let nextId () =
                            next <- next + 1
                            $"{prefix}_{index}_d{next}"

                        let ids = Dictionary<DrawableId, string>()
                        let shapes = Dictionary<DrawableId, LayoutShape>()

                        let plan =
                            [
                                for item in items do
                                    let link = item.Drawable.Links |> Seq.tryFind (fun link -> link.IsWeb) |> Option.map (fun link -> link.Target)

                                    match item.Drawable.Kind with
                                    | DrawableKind.Shape shape ->
                                        let id = nextId ()
                                        ids[item.Drawable.Id] <- id
                                        shapes[item.Drawable.Id] <- shape
                                        yield PlannedShape(id, shape, link)
                                    | DrawableKind.Text text -> yield PlannedText(nextId (), text, link)
                                    | DrawableKind.Line line ->
                                        yield PlannedLine(nextId (), line, link)

                                        match line.Label with
                                        | null -> ()
                                        | label -> yield PlannedText(nextId (), label, None)
                                    | _ -> ()
                            ]

                        // Every object first, in paint order.
                        for planned in plan do
                            match planned with
                            | PlannedShape(id, shape, _) ->
                                request "createShape" (fun () ->
                                    writer.WriteString("objectId", id)
                                    writer.WriteString("shapeType", shapeType shape.Kind)
                                    elementProperties writer slide (fit.Box shape.Box) false false)
                            | PlannedText(id, text, _) ->
                                request "createShape" (fun () ->
                                    writer.WriteString("objectId", id)
                                    writer.WriteString("shapeType", "TEXT_BOX")
                                    elementProperties writer slide (textBox fit text) false false)
                            | PlannedLine(id, line, _) ->
                                let start, finish = fit.Point line.Source.Point, fit.Point line.Target.Point
                                let left, top = Distance.Min(start.X, finish.X), Distance.Min(start.Y, finish.Y)
                                let box = LayoutBox.Create(left, top, Distance.Max(start.X, finish.X) - left, Distance.Max(start.Y, finish.Y) - top)

                                request "createLine" (fun () ->
                                    writer.WriteString("objectId", id)
                                    writer.WriteString("category", "STRAIGHT")
                                    elementProperties writer slide box (finish.X.CompareTo start.X < 0) (finish.Y.CompareTo start.Y < 0))

                        if isNull existing then
                            request "updatePageProperties" (fun () ->
                                writer.WriteString("objectId", slide)
                                writer.WriteStartObject "pageProperties"
                                writer.WriteStartObject "pageBackgroundFill"
                                solid writer page.Background false
                                writer.WriteEndObject()
                                writer.WriteEndObject()
                                writer.WriteString("fields", "pageBackgroundFill"))

                        // Then text and styles.
                        let text (id: string) (body: LayoutText) (inShape: bool) =
                            if body.Lines.Length > 0 then
                                request "insertText" (fun () ->
                                    writer.WriteString("objectId", id)
                                    writer.WriteString("text", String.Join("\n", body.Lines))
                                    writer.WriteNumber("insertionIndex", 0))

                                request "updateTextStyle" (fun () ->
                                    writer.WriteString("objectId", id)
                                    writer.WriteStartObject "style"
                                    writer.WriteString("fontFamily", "Roboto Mono")
                                    writer.WriteStartObject "fontSize"
                                    writer.WriteNumber("magnitude", Math.Round(Math.Max(1.0, (fit.Length body.Size).ToPoints()), 2))
                                    writer.WriteString("unit", "PT")
                                    writer.WriteEndObject()
                                    writer.WriteBoolean("bold", body.Weight >= 600)
                                    writer.WriteStartObject "foregroundColor"
                                    writer.WriteStartObject "opaqueColor"
                                    rgb writer body.Ink.Color
                                    writer.WriteEndObject()
                                    writer.WriteEndObject()
                                    writer.WriteEndObject()
                                    writer.WriteStartObject "textRange"
                                    writer.WriteString("type", "ALL")
                                    writer.WriteEndObject()
                                    writer.WriteString("fields", "fontFamily,fontSize,bold,foregroundColor"))

                                request "updateParagraphStyle" (fun () ->
                                    writer.WriteString("objectId", id)
                                    writer.WriteStartObject "style"

                                    writer.WriteString(
                                        "alignment",
                                        match body.Align with
                                        | TextAlign.Start when not inShape -> "START"
                                        | TextAlign.End when not inShape -> "END"
                                        | _ -> "CENTER"
                                    )

                                    writer.WriteEndObject()
                                    writer.WriteStartObject "textRange"
                                    writer.WriteString("type", "ALL")
                                    writer.WriteEndObject()
                                    writer.WriteString("fields", "alignment"))

                        let shapeProperties (id: string) (fill: LayoutColor | null) (paint: LayoutPaint option) (link: string option) =
                            request "updateShapeProperties" (fun () ->
                                writer.WriteString("objectId", id)
                                writer.WriteStartObject "shapeProperties"
                                writer.WriteStartObject "shapeBackgroundFill"

                                match fill with
                                | null -> writer.WriteString("propertyState", "NOT_RENDERED")
                                | fill -> solid writer fill.Color true

                                writer.WriteEndObject()
                                writer.WriteStartObject "outline"

                                match paint |> Option.bind (fun paint -> paint.Stroke |> Option.ofObj |> Option.map (fun stroke -> paint, stroke)) with
                                | None -> writer.WriteString("propertyState", "NOT_RENDERED")
                                | Some(paint, stroke) ->
                                    writer.WriteStartObject "outlineFill"
                                    solid writer stroke.Color false
                                    writer.WriteEndObject()
                                    emu writer "weight" (Distance.Max(paint.StrokeWidth, Distance.FromEmu 1L))
                                    writer.WriteString("dashStyle", (if paint.Dash.IsEmpty then "SOLID" else "DASH"))

                                writer.WriteEndObject()
                                writer.WriteString("contentAlignment", "MIDDLE")

                                match link with
                                | Some url ->
                                    writer.WriteStartObject "link"
                                    writer.WriteString("url", url)
                                    writer.WriteEndObject()
                                | None -> ()

                                writer.WriteEndObject()

                                writer.WriteString(
                                    "fields",
                                    "shapeBackgroundFill,outline,contentAlignment" + (if link.IsSome then ",link" else "")
                                ))

                        for planned in plan do
                            match planned with
                            | PlannedShape(id, shape, link) ->
                                match shape.Text with
                                | null -> ()
                                | body -> text id body true

                                shapeProperties id shape.Paint.Fill (Some shape.Paint) link
                            | PlannedText(id, body, link) ->
                                text id body false

                                shapeProperties
                                    id
                                    (match body.Chip with
                                     | null -> null
                                     | chip -> chip.Color)
                                    None
                                    link
                            | PlannedLine(id, line, link) ->
                                request "updateLineProperties" (fun () ->
                                    writer.WriteString("objectId", id)
                                    writer.WriteStartObject "lineProperties"

                                    match line.Paint.Stroke with
                                    | null -> ()
                                    | stroke ->
                                        writer.WriteStartObject "lineFill"
                                        solid writer stroke.Color false
                                        writer.WriteEndObject()

                                    emu writer "weight" (Distance.Max(line.Paint.StrokeWidth, Distance.FromEmu 1L))
                                    writer.WriteString("dashStyle", (if line.Paint.Dash.IsEmpty then "SOLID" else "DASH"))
                                    writer.WriteString("startArrow", arrow line.Source.Head)
                                    writer.WriteString("endArrow", arrow line.Target.Head)

                                    match link with
                                    | Some url ->
                                        writer.WriteStartObject "link"
                                        writer.WriteString("url", url)
                                        writer.WriteEndObject()
                                    | None -> ()

                                    writer.WriteEndObject()

                                    writer.WriteString(
                                        "fields",
                                        (if isNull line.Paint.Stroke then "" else "lineFill,")
                                        + "weight,dashStyle,startArrow,endArrow"
                                        + (if link.IsSome then ",link" else "")
                                    ))

                        // Connections last, when every shape they name exists.
                        for planned in plan do
                            match planned with
                            | PlannedLine(id, line, _) ->
                                let connection (lineEnd: LineEnd) =
                                    match lineEnd.Binding with
                                    | null -> None
                                    | binding ->
                                        match ids.TryGetValue binding.Target, shapes.TryGetValue binding.Target with
                                        | (true, target), (true, shape) -> PresetSites.nearest shape binding |> Option.map (fun site -> target, site)
                                        | _ -> None

                                let ends = [ "startConnection", connection line.Source; "endConnection", connection line.Target ] |> List.choose (fun (name, found) -> found |> Option.map (fun found -> name, found))

                                if not ends.IsEmpty then
                                    request "updateLineProperties" (fun () ->
                                        writer.WriteString("objectId", id)
                                        writer.WriteStartObject "lineProperties"

                                        for name, (target, site) in ends do
                                            writer.WriteStartObject name
                                            writer.WriteString("connectedObjectId", target)
                                            writer.WriteNumber("connectionSiteIndex", site)
                                            writer.WriteEndObject()

                                        writer.WriteEndObject()
                                        writer.WriteString("fields", String.Join(",", ends |> List.map fst)))
                            | _ -> ()

                    writer.WriteEndArray()
                    writer.WriteEndObject()

                Ok(buffer.ToArray())

/// Writes diagrams as the request body of a Slides API `presentations.batchUpdate`. The library makes no network
/// requests and holds no credentials: the caller sends the body with their own authorised client.
[<Sealed>]
type GoogleSlidesRenderer(options: GoogleSlidesOptions) =
    inherit DiagramRenderer()

    do ArgumentNullException.ThrowIfNull options

    static let features =
        LayoutFeatures.Of(
            LayoutFeature.RoundedRectangles,
            LayoutFeature.Ellipses,
            LayoutFeature.Diamonds,
            LayoutFeature.Cylinders,
            LayoutFeature.Hexagons,
            LayoutFeature.AttachedConnectors,
            LayoutFeature.OpenAndCircleHeads,
            LayoutFeature.WrappedText,
            LayoutFeature.InterleavedPainting,
            LayoutFeature.FillOpacity,
            LayoutFeature.Links,
            LayoutFeature.Pages
        )

    /// The target presentation.
    member _.Options = options

    /// application/json.
    override _.MediaType = "application/json"

    /// .gslides.json.
    override _.FileExtension = ".gslides.json"

    /// The shape kinds, attached connectors, open and circle heads, wrapped text, interleaved painting, fill opacity,
    /// links and pages. It writes no groups, so paths reach it as rectangles, bound lines as straight segments,
    /// message heads as arrows and dash patterns as the default dash.
    override _.NativeFeatures = features

    /// Writes the plan's pages as one request body.
    override this.RenderAsync(plan: RenderPlan, output: Stream, cancellationToken: CancellationToken) =
        ArgumentNullException.ThrowIfNull plan
        ArgumentNullException.ThrowIfNull output
        plan.EnsureFor this
        cancellationToken.ThrowIfCancellationRequested()

        match GoogleRequests.write options plan.Pages cancellationToken with
        | Error error -> Task.FromResult(ImmutableArray.Create<Diagnostic> error)
        | Ok bytes ->
            task {
                do! Packaging.copy bytes output cancellationToken
                return ImmutableArray<Diagnostic>.Empty
            }

    override _.ToString() = $"GoogleSlidesRenderer({options})"
