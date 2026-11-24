// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Layout

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.Diagrams
open Morphir.Svg

/// The identifier of a drawable, unique on its page and the same for equal input, such as d17.
[<Sealed>]
type DrawableId private (value: string) =
    /// The identifier text.
    member _.Value = value

    override _.ToString() = value

    override _.Equals(other: obj | null) =
        match other with
        | :? DrawableId as id -> String.Equals(id.Value, value, StringComparison.Ordinal)
        | _ -> false

    override _.GetHashCode() = StringComparer.Ordinal.GetHashCode value

    interface IComparable<DrawableId> with
        member _.CompareTo(other: DrawableId | null) =
            match other with
            | null -> 1
            | other -> String.CompareOrdinal(value, other.Value)

    /// An identifier of one to 64 ASCII letters, digits, hyphens and underscores, or the reason the text is not one.
    static member Parse(text: string | null) : Result<DrawableId, Diagnostic> =
        match text with
        | null -> Error(Diagnostic("layout/invalid-drawable-id", DiagnosticSeverity.Error, "A drawable identifier is required.", None))
        | text when text.Length > 0 && text.Length <= 64 && text |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '-' || c = '_') ->
            Ok(DrawableId text)
        | text ->
            Error(
                Diagnostic(
                    "layout/invalid-drawable-id",
                    DiagnosticSeverity.Error,
                    $"'{text}' is not a drawable identifier: use one to 64 ASCII letters, digits, hyphens and underscores.",
                    None
                )
            )

    /// Parses an identifier for C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<DrawableId>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Diagnostic>
        ) =
        match DrawableId.Parse text with
        | Ok id ->
            result <- id
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// An identifier from a literal in source code. Throws an argument error for invalid text.
    static member Create(literal: string) =
        match DrawableId.Parse literal with
        | Ok id -> id
        | Error error -> invalidArg (nameof literal) error.Message

    /// The identifier of the drawable recorded at an index of its page.
    static member internal OfIndex(index: int) = DrawableId("d" + string index)

/// The outline of a shape.
[<RequireQualifiedAccess>]
type ShapeKind =
    /// A rectangle over the box.
    | Rectangle
    /// A rectangle with rounded corners of a radius.
    | RoundedRectangle of radius: Distance
    /// An ellipse in the box.
    | Ellipse
    /// A diamond through the midpoints of the box's edges.
    | Diamond
    /// A cylinder: a rounded body with an elliptical top whose height is the cap.
    | Cylinder of cap: Distance
    /// A hexagon whose left and right points are inset from the box's corners.
    | Hexagon of inset: Distance
    /// Any other outline.
    | Path of data: PathData

    /// The kind as documentation writes it, such as rounded-rectangle.
    member this.WireValue =
        match this with
        | ShapeKind.Rectangle -> "rectangle"
        | ShapeKind.RoundedRectangle _ -> "rounded-rectangle"
        | ShapeKind.Ellipse -> "ellipse"
        | ShapeKind.Diamond -> "diamond"
        | ShapeKind.Cylinder _ -> "cylinder"
        | ShapeKind.Hexagon _ -> "hexagon"
        | ShapeKind.Path _ -> "path"

    /// For C# callers: every case needs a function.
    member this.Match
        (
            rectangle: Func<'T>,
            roundedRectangle: Func<Distance, 'T>,
            ellipse: Func<'T>,
            diamond: Func<'T>,
            cylinder: Func<Distance, 'T>,
            hexagon: Func<Distance, 'T>,
            path: Func<PathData, 'T>
        ) =
        match this with
        | ShapeKind.Rectangle -> rectangle.Invoke()
        | ShapeKind.RoundedRectangle radius -> roundedRectangle.Invoke radius
        | ShapeKind.Ellipse -> ellipse.Invoke()
        | ShapeKind.Diamond -> diamond.Invoke()
        | ShapeKind.Cylinder cap -> cylinder.Invoke cap
        | ShapeKind.Hexagon inset -> hexagon.Invoke inset
        | ShapeKind.Path data -> path.Invoke data

    override this.ToString() =
        match this with
        | ShapeKind.RoundedRectangle radius -> $"rounded-rectangle {radius}"
        | ShapeKind.Cylinder cap -> $"cylinder {cap}"
        | ShapeKind.Hexagon inset -> $"hexagon {inset}"
        | ShapeKind.Path data -> $"path {data}"
        | other -> other.WireValue

/// An element kind that a target may draw with a dedicated symbol. Roles are advisory: the shape kind always stays,
/// so a renderer that does not recognise a role draws the shape without it.
[<RequireQualifiedAccess>]
type FamilyRole =
    /// A sequence actor.
    | Actor
    /// A sequence lifeline.
    | Lifeline
    /// A sequence activation bar.
    | Activation
    /// A BPMN task.
    | Task
    /// A BPMN gateway.
    | Gateway
    /// A BPMN event.
    | Event
    /// A BPMN data object or data store.
    | DataObject
    /// A state diagram's initial state.
    | Initial
    /// A state diagram's final state.
    | Final
    /// A state diagram's choice.
    | Choice
    /// A state diagram's history state.
    | History
    /// A state diagram's fork or join bar.
    | ForkJoin
    /// A note.
    | Note

    /// The role as documentation writes it, such as data-object.
    member this.WireValue =
        match this with
        | FamilyRole.Actor -> "actor"
        | FamilyRole.Lifeline -> "lifeline"
        | FamilyRole.Activation -> "activation"
        | FamilyRole.Task -> "task"
        | FamilyRole.Gateway -> "gateway"
        | FamilyRole.Event -> "event"
        | FamilyRole.DataObject -> "data-object"
        | FamilyRole.Initial -> "initial"
        | FamilyRole.Final -> "final"
        | FamilyRole.Choice -> "choice"
        | FamilyRole.History -> "history"
        | FamilyRole.ForkJoin -> "fork-join"
        | FamilyRole.Note -> "note"

    override this.ToString() = this.WireValue

/// One of the theme's base colours.
[<RequireQualifiedAccess>]
type BaseRole =
    /// The page background.
    | Background
    /// Body text.
    | Text
    /// Secondary text.
    | Muted
    /// The opaque underlay of labels and boxes.
    | Mask
    /// Connectors.
    | Arrow
    /// Frames and outlines.
    | Frame

    /// The role as the theme's CSS variables name it, such as bg.
    member this.WireValue =
        match this with
        | BaseRole.Background -> "bg"
        | BaseRole.Text -> "text"
        | BaseRole.Muted -> "muted"
        | BaseRole.Mask -> "mask"
        | BaseRole.Arrow -> "arrow"
        | BaseRole.Frame -> "frame"

    override this.ToString() = this.WireValue

/// Where a colour came from. Sources are advisory.
[<RequireQualifiedAccess>]
type ColorSource =
    /// A colour family of the theme.
    | Family of family: ColorFamily
    /// One of the theme's base colours.
    | Base of role: BaseRole
    /// A colour written in the diagram source.
    | Authored

    /// For C# callers: every case needs a function.
    member this.Match(family: Func<ColorFamily, 'T>, baseRole: Func<BaseRole, 'T>, authored: Func<'T>) =
        match this with
        | ColorSource.Family value -> family.Invoke value
        | ColorSource.Base role -> baseRole.Invoke role
        | ColorSource.Authored -> authored.Invoke()

    override this.ToString() =
        match this with
        | ColorSource.Family family -> $"family {family}"
        | ColorSource.Base role -> $"base {role}"
        | ColorSource.Authored -> "authored"

/// A resolved colour, with its effective opacity, and its source.
[<NoComparison>]
type LayoutColor =
    internal
        { color: Color
          source: ColorSource }

    /// The colour. Its opacity is the effective opacity: the colour's own alpha times any opacity the SVG applies.
    member this.Color = this.color

    /// Where the colour came from.
    member this.Source = this.source

    /// Whether the colour is fully opaque.
    member this.IsOpaque = this.color.Opacity >= 1m

    override this.ToString() = $"{this.color} ({this.source})"

/// How the ends of an open stroke are drawn. Advisory.
[<RequireQualifiedAccess>]
type LineCap =
    /// Ends square at the end point.
    | Butt
    /// Ends with a half circle.
    | Round
    /// Ends with a half square beyond the end point.
    | Square

    override this.ToString() =
        match this with
        | LineCap.Butt -> "butt"
        | LineCap.Round -> "round"
        | LineCap.Square -> "square"

/// How the corners of a stroke are drawn. Advisory.
[<RequireQualifiedAccess>]
type LineJoin =
    /// Sharp corners.
    | Miter
    /// Rounded corners.
    | Round
    /// Cut-off corners.
    | Bevel

    override this.ToString() =
        match this with
        | LineJoin.Miter -> "miter"
        | LineJoin.Round -> "round"
        | LineJoin.Bevel -> "bevel"

/// The colours and stroke of a drawable. A missing fill or stroke is none.
[<NoComparison>]
type LayoutPaint =
    internal
        { fill: LayoutColor option
          stroke: LayoutColor option
          ink: LayoutColor
          strokeWidth: Distance
          dash: ImmutableArray<Distance>
          cap: LineCap
          join: LineJoin }

    /// The fill, or null for none.
    member this.Fill: LayoutColor | null = Option.toObj this.fill

    /// The stroke, or null for none.
    member this.Stroke: LayoutColor | null = Option.toObj this.stroke

    /// The colour of the drawable's text.
    member this.Ink = this.ink

    /// The stroke width.
    member this.StrokeWidth = this.strokeWidth

    /// The dash pattern, empty for a solid stroke.
    member this.Dash = this.dash

    /// The line cap. Advisory.
    member this.Cap = this.cap

    /// The line join. Advisory.
    member this.Join = this.join

    override this.ToString() =
        let show = Option.map string >> Option.defaultValue "none"
        $"fill {show this.fill}, stroke {show this.stroke} {this.strokeWidth}"

/// The horizontal alignment of text at its origin.
[<RequireQualifiedAccess>]
type TextAlign =
    /// The origin is at the start of each line.
    | Start
    /// The origin is at the middle of each line.
    | Middle
    /// The origin is at the end of each line.
    | End

    override this.ToString() =
        match this with
        | TextAlign.Start -> "start"
        | TextAlign.Middle -> "middle"
        | TextAlign.End -> "end"

/// Text with its lines, baseline origin and measured box.
[<NoComparison>]
type LayoutText =
    internal
        { lines: ImmutableArray<string>
          origin: LayoutPoint
          advance: Distance
          align: TextAlign
          size: Distance
          weight: int
          rotation: int
          box: LayoutBox
          wraps: bool
          ink: LayoutColor
          background: LayoutColor option
          backgroundBox: LayoutBox
          backgroundRadius: Distance }

    /// The lines, top to bottom.
    member this.Lines = this.lines

    /// The baseline origin of the first line.
    member this.Origin = this.origin

    /// The distance from one baseline to the next.
    member this.Advance = this.advance

    /// How each line aligns at the origin's x.
    member this.Align = this.align

    /// The font size.
    member this.Size = this.size

    /// The font weight, from 100 to 900.
    member this.Weight = this.weight

    /// The clockwise rotation about the origin: 0, 90, 180 or 270 degrees.
    member this.Rotation = this.rotation

    /// A box that holds the lines, measured with the theme's text-width estimate, before rotation.
    member this.Box = this.box

    /// Whether the text may wrap within its box.
    member this.Wraps = this.wraps

    /// The text colour.
    member this.Ink = this.ink

    /// The colour of an opaque chip behind the text, or null for none.
    member this.Background: LayoutColor | null = Option.toObj this.background

    /// The chip's box, when there is a background.
    member this.BackgroundBox = this.backgroundBox

    /// The chip's corner radius, when there is a background.
    member this.BackgroundRadius = this.backgroundRadius

    override this.ToString() = "text " + String.Join(" / ", this.lines)

/// A line end bound to another drawable: across a shape, image or container, or along a line.
[<RequireQualifiedAccess>]
type LineAnchor =
    /// Fractions across the target's box, clamped to 0 and 1.
    | Across of point: FractionPoint
    /// A fraction of the target line's length.
    | Along of fraction: Fraction

    /// For C# callers: every case needs a function.
    member this.Match(across: Func<FractionPoint, 'T>, along: Func<Fraction, 'T>) =
        match this with
        | LineAnchor.Across point -> across.Invoke point
        | LineAnchor.Along fraction -> along.Invoke fraction

    override this.ToString() =
        match this with
        | LineAnchor.Across point -> $"across {point}"
        | LineAnchor.Along fraction -> $"along {fraction}"

/// The drawable a line end is bound to, and where.
[<NoComparison>]
type LineBinding =
    internal
        { target: DrawableId
          anchor: LineAnchor }

    /// The drawable on the same page.
    member this.Target = this.target

    /// Where on the drawable.
    member this.Anchor = this.anchor

    override this.ToString() = $"{this.target} {this.anchor}"

/// One end of a line. Its arrowhead is independent of its binding.
[<NoComparison>]
type LineEnd =
    internal
        { point: LayoutPoint
          head: EdgeHead
          binding: LineBinding option }

    /// The exact point the SVG draws, even when it lies outside a bound target's box.
    member this.Point = this.point

    /// The arrowhead.
    member this.Head = this.head

    /// The drawable the end is bound to, or null for a free end.
    member this.Binding: LineBinding | null = Option.toObj this.binding

    override this.ToString() =
        let bound = this.binding |> Option.map (fun binding -> $" bound to {binding}") |> Option.defaultValue ""
        $"{this.point} {this.head}{bound}"

/// How a line bends between its waypoints. Advisory.
[<RequireQualifiedAccess>]
type RouteStyle =
    /// One straight segment.
    | Straight
    /// Straight segments that meet at right angles.
    | Orthogonal
    /// A smooth curve through the waypoints.
    | Curved

    override this.ToString() =
        match this with
        | RouteStyle.Straight -> "straight"
        | RouteStyle.Orthogonal -> "orthogonal"
        | RouteStyle.Curved -> "curved"

/// A line with its ends, routing waypoints and exact final path.
[<NoComparison>]
type LayoutLine =
    internal
        { source: LineEnd
          target: LineEnd
          waypoints: ImmutableArray<LayoutPoint>
          route: RouteStyle
          exactPath: PathData option
          paint: LayoutPaint
          label: LayoutText option }

    /// The start.
    member this.Source = this.source

    /// The end.
    member this.Target = this.target

    /// The routing waypoints, ends included.
    member this.Waypoints = this.waypoints

    /// How the line bends between its waypoints.
    member this.Route = this.route

    /// The exact path the SVG draws, including rounded corners, curves and bridge arcs, or null.
    member this.ExactPath: PathData | null = Option.toObj this.exactPath

    /// The stroke and its colours.
    member this.Paint = this.paint

    /// The label, or null.
    member this.Label: LayoutText | null = Option.toObj this.label

    override this.ToString() = $"line {this.source} to {this.target}"

/// A shape with its kind, box, paint and optional text and role.
[<NoComparison>]
type LayoutShape =
    internal
        { kind: ShapeKind
          box: LayoutBox
          paint: LayoutPaint
          text: LayoutText option
          role: FamilyRole option
          backdrop: LayoutColor option }

    /// The outline.
    member this.Kind = this.kind

    /// The box the outline fills.
    member this.Box = this.box

    /// The fill and stroke.
    member this.Paint = this.paint

    /// The text inside the shape, or null.
    member this.Text: LayoutText | null = Option.toObj this.text

    /// The family role, or null. Advisory.
    member this.Role: FamilyRole | null = Option.toObj this.role

    /// An opaque colour painted under the fill, so that a translucent fill hides what lies behind it, or null.
    member this.Backdrop: LayoutColor | null = Option.toObj this.backdrop

    override this.ToString() = $"{this.kind} at {this.box}"

/// An image drawn from a standalone SVG document.
[<Sealed>]
type LayoutImage internal (box: LayoutBox, svg: SvgDocument) =
    let text = lazy (svg.Render())

    /// The box the image fills.
    member _.Box = box

    /// The standalone SVG document.
    member _.Svg = svg

    /// The SVG as text.
    member _.SvgText = text.Value

    override _.ToString() = $"image at {box}"

    override this.Equals(other: obj | null) =
        match other with
        | :? LayoutImage as other -> other.Box.Equals box && other.SvgText = this.SvgText
        | _ -> false

    override this.GetHashCode() = HashCode.Combine(box, this.SvgText)

/// A link from a drawable, from the diagram's metadata.
[<NoComparison>]
type DrawableLink =
    internal
        { predicate: string
          linkTarget: string
          isWeb: bool }

    /// The predicate IRI.
    member this.Predicate = this.predicate

    /// The target: an IRI, a selection or a web address.
    member this.Target = this.linkTarget

    /// Whether the target is a web address.
    member this.IsWeb = this.isWeb

    override this.ToString() = $"{this.predicate} {this.linkTarget}"

/// A drawable with its page-local identifier, optional reference and paint-order index.
[<NoComparison>]
type Drawable =
    internal
        { id: DrawableId
          reference: DiagramElementRef option
          paintOrder: int
          kind: DrawableKind
          links: ImmutableArray<DrawableLink> }

    /// The identifier, unique on the page.
    member this.Id = this.id

    /// The diagram element it draws, or null.
    member this.Reference: DiagramElementRef | null = Option.toObj this.reference

    /// The index in the page's paint order, which can differ from containment order. A drawable that gathers others
    /// has the index of its first.
    member this.PaintOrder = this.paintOrder

    /// What the drawable is.
    member this.Kind = this.kind

    /// The links that the diagram's metadata gives for its reference, in the metadata's order.
    member this.Links = this.links

    override this.ToString() =
        let reference = this.reference |> Option.map (fun reference -> $" {reference}") |> Option.defaultValue ""
        $"{this.id}{reference} {this.kind}"

/// What a drawable is.
and [<RequireQualifiedAccess; NoComparison>] DrawableKind =
    /// A shape.
    | Shape of shape: LayoutShape
    /// A line.
    | Line of line: LayoutLine
    /// Free text.
    | Text of text: LayoutText
    /// An image.
    | Image of image: LayoutImage
    /// One reference over several drawables, such as a sequence participant.
    | Element of children: ImmutableArray<Drawable>
    /// A container with a header, such as a subgraph, a lane or a pool.
    | Container of container: LayoutContainer
    /// Drawables gathered for painting only.
    | Group of children: ImmutableArray<Drawable>

    /// For C# callers: every case needs a function.
    member this.Match
        (
            shape: Func<LayoutShape, 'T>,
            line: Func<LayoutLine, 'T>,
            text: Func<LayoutText, 'T>,
            image: Func<LayoutImage, 'T>,
            element: Func<ImmutableArray<Drawable>, 'T>,
            container: Func<LayoutContainer, 'T>,
            group: Func<ImmutableArray<Drawable>, 'T>
        ) =
        match this with
        | DrawableKind.Shape value -> shape.Invoke value
        | DrawableKind.Line value -> line.Invoke value
        | DrawableKind.Text value -> text.Invoke value
        | DrawableKind.Image value -> image.Invoke value
        | DrawableKind.Element children -> element.Invoke children
        | DrawableKind.Container value -> container.Invoke value
        | DrawableKind.Group children -> group.Invoke children

    /// The drawables this one gathers, or none.
    member this.Children =
        match this with
        | DrawableKind.Element children
        | DrawableKind.Group children -> children
        | DrawableKind.Container container -> container.Children
        | _ -> ImmutableArray.Empty

    override this.ToString() =
        match this with
        | DrawableKind.Shape value -> string value
        | DrawableKind.Line value -> string value
        | DrawableKind.Text value -> string value
        | DrawableKind.Image value -> string value
        | DrawableKind.Element children -> $"element of {children.Length}"
        | DrawableKind.Container value -> string value
        | DrawableKind.Group children -> $"group of {children.Length}"

/// A container's frame, header and children.
and [<NoComparison>] LayoutContainer =
    internal
        { frame: LayoutBox
          header: LayoutText option
          framePaint: LayoutPaint
          radius: Distance
          band: Distance
          children: ImmutableArray<Drawable> }

    /// The frame's box.
    member this.Box = this.frame

    /// The header, or null.
    member this.Header: LayoutText | null = Option.toObj this.header

    /// The frame's fill and stroke.
    member this.Paint = this.framePaint

    /// The frame's corner radius.
    member this.Radius = this.radius

    /// The width of a header band along the frame's left edge, divided from the body by a line in the frame's
    /// stroke, such as a BPMN pool's; zero for none.
    member this.HeaderBand = this.band

    /// The drawables inside, in paint order.
    member this.Children = this.children

    override this.ToString() =
        let header = this.header |> Option.map (fun text -> " " + String.Join(" ", text.Lines)) |> Option.defaultValue ""
        $"container{header} of {this.children.Length}"
