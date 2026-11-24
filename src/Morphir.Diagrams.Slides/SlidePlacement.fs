// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Slides

open System
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout

/// The size of a slide.
[<Sealed>]
type SlideSize private (width: Distance, height: Distance) =
    static let widescreen = SlideSize(Distance.FromEmu 12_192_000L, Distance.FromEmu 6_858_000L)
    static let standard = SlideSize(Distance.FromEmu 9_144_000L, Distance.FromEmu 6_858_000L)

    /// The width.
    member _.Width = width

    /// The height.
    member _.Height = height

    /// The text form, for diagnostics and logs.
    override _.ToString() = $"{width} by {height}"

    /// Ordinal equality of the values.
    override _.Equals(other: obj | null) =
        match other with
        | :? SlideSize as other -> other.Width.Equals width && other.Height.Equals height
        | _ -> false

    /// A hash consistent with Equals.
    override _.GetHashCode() = HashCode.Combine(width, height)

    /// Widescreen 16:9, 40/3 by 7.5 inches. The default.
    static member Widescreen = widescreen

    /// Standard 4:3, 10 by 7.5 inches.
    static member Standard = standard

    /// A custom size. Throws an argument error unless both sides are greater than zero.
    static member Create(width: Distance, height: Distance) =
        if width.QuarterEmu <= 0L then invalidArg (nameof width) "A slide's width must be greater than zero."
        if height.QuarterEmu <= 0L then invalidArg (nameof height) "A slide's height must be greater than zero."
        SlideSize(width, height)

/// The area of a slide that a diagram goes in: fractions of the slide's width and height, or exact distances.
[<Sealed>]
type SlideArea private (resolve: SlideSize -> Distance * Distance * Distance * Distance, description: string) =
    static let content =
        let twelfth = Fraction.Create(1.0 / 12.0)
        let tenTwelfths = Fraction.Create(10.0 / 12.0)
        SlideArea((fun size -> SlideArea.Of(size, twelfth, twelfth, tenTwelfths, tenTwelfths)), "content area")

    static let full = SlideArea((fun size -> Distance.Zero, Distance.Zero, size.Width, size.Height), "whole slide")

    static member private Of(size: SlideSize, left: Fraction, top: Fraction, width: Fraction, height: Fraction) =
        size.Width.Scale left.Value, size.Height.Scale top.Value, size.Width.Scale width.Value, size.Height.Scale height.Value

    /// The left edge, top edge, width and height on a slide.
    member internal _.On(size: SlideSize) = resolve size

    /// The text form, for diagnostics and logs.
    override _.ToString() = description

    /// The content area: a margin of one twelfth of the slide's width and height on each side. The default.
    static member Content = content

    /// The whole slide.
    static member Full = full

    /// An area given as fractions of the slide's width and height. Throws an argument error when it is empty or reaches
    /// past the slide's right or bottom edge.
    static member FromFractions(left: Fraction, top: Fraction, width: Fraction, height: Fraction) =
        ArgumentNullException.ThrowIfNull left
        ArgumentNullException.ThrowIfNull top
        ArgumentNullException.ThrowIfNull width
        ArgumentNullException.ThrowIfNull height
        if width.Value <= 0.0 || height.Value <= 0.0 then invalidArg (nameof width) "An area must have a positive width and height."
        if left.Value + width.Value > 1.0 || top.Value + height.Value > 1.0 then invalidArg (nameof width) "An area must lie within the slide."
        SlideArea((fun size -> SlideArea.Of(size, left, top, width, height)), $"fractions {left}, {top}, {width} by {height}")

    /// An area given as exact distances from the slide's top-left corner. Placement refuses it when it does not lie within
    /// the slide.
    static member FromDistances(left: Distance, top: Distance, width: Distance, height: Distance) =
        if width.QuarterEmu <= 0L || height.QuarterEmu <= 0L then invalidArg (nameof width) "An area must have a positive width and height."
        if left.QuarterEmu < 0L || top.QuarterEmu < 0L then invalidArg (nameof left) "An area must lie within the slide."
        SlideArea((fun _ -> left, top, width, height), $"{left}, {top}, {width} by {height}")

/// The grid that a slide format writes its lengths on.
[<RequireQualifiedAccess>]
type SlideGrid =
    /// Whole English Metric Units, as PowerPoint and Google Slides write them.
    | Emu
    /// Thousandths of a millimetre (36 EMU), as OpenDocument writes them.
    | Micrometre

    /// One step of the grid.
    member this.Step =
        match this with
        | SlideGrid.Emu -> Distance.FromEmu 1L
        | SlideGrid.Micrometre -> Distance.FromEmu 36L

    /// The text form, for diagnostics and logs.
    override this.ToString() =
        match this with
        | SlideGrid.Emu -> "EMU"
        | SlideGrid.Micrometre -> "micrometre"

/// Where one layout goes on a slide: an exact rational scale and a centring offset on the target's grid. Positions,
/// sizes, waypoints, path points, radii and font sizes scale; stroke widths, dash lengths and arrowheads do not.
[<Sealed>]
type SlideFit internal (area: LayoutBox, extent: LayoutBox, numerator: Int128, denominator: Int128, offsetX: int64, offsetY: int64, step: int64) =
    let round (value: Int128) (divisor: Int128) =
        // Half to even, onto a multiple of the grid step.
        let quotient = Int128.op_Division(value, divisor)
        let remainder = value - quotient * divisor
        let twice = Int128.Abs(remainder) * Int128.op_Implicit 2
        let away = if value < Int128.Zero then quotient - Int128.One else quotient + Int128.One

        if twice > Int128.Abs divisor || (twice = Int128.Abs divisor && quotient % Int128.op_Implicit 2 <> Int128.Zero) then away
        else quotient

    let map (value: Distance) (origin: Distance) (start: Distance) (offset: int64) =
        let scaled = Int128.op_Implicit (value - origin).QuarterEmu * numerator
        let steps = round scaled (denominator * Int128.op_Implicit step)
        Distance.FromQuarterEmu(Checked.(+) (Checked.(+) start.QuarterEmu offset) (int64 steps * step))

    /// The area, rounded inwards onto the grid.
    member _.Area = area

    /// The fitted extent of the layout: its bounds and the geometry of every drawable.
    member _.Extent = extent

    /// The fit factor.
    member _.Factor = float numerator / float denominator

    /// A layout x coordinate on the slide, on the grid.
    member _.X(x: Distance) = map x extent.X area.X offsetX

    /// A layout y coordinate on the slide, on the grid.
    member _.Y(y: Distance) = map y extent.Y area.Y offsetY

    /// A layout point on the slide.
    member this.Point(point: LayoutPoint) = LayoutPoint.Create(this.X point.X, this.Y point.Y)

    /// A layout box on the slide: its mapped edges, so its size is the difference of two mapped edges.
    member this.Box(box: LayoutBox) =
        let left, top = this.X box.X, this.Y box.Y
        LayoutBox.Create(left, top, this.X box.Right - left, this.Y box.Bottom - top)

    /// A length that scales, such as a font size or a radius, without the grid.
    member _.Length(length: Distance) =
        Distance.FromQuarterEmu(int64 (round (Int128.op_Implicit length.QuarterEmu * numerator) denominator))

    /// The text form, for diagnostics and logs.
    override _.ToString() = $"fit {extent} into {area} at {float numerator / float denominator}"

/// How diagrams are placed on slides: the slide size, the area the diagram goes in, and an optional maximum scale. A
/// layout is scaled uniformly by the largest exact factor that keeps it inside the area, and centred there.
[<Sealed>]
type SlidePlacement private (size: SlideSize, area: SlideArea, maximum: Scale option) =
    static let widescreen = SlidePlacement(SlideSize.Widescreen, SlideArea.Content, None)
    static let standard = SlidePlacement(SlideSize.Standard, SlideArea.Content, None)

    /// The slide size.
    member _.Size = size

    /// The area.
    member _.Area = area

    /// The largest factor a diagram may be scaled by, or null for no limit.
    member _.MaximumScale: Scale | null = Option.toObj maximum

    /// A copy with another area. Throws an argument error when the area does not lie within the slide.
    member _.WithArea(other: SlideArea) =
        ArgumentNullException.ThrowIfNull other
        let left, top, width, height = other.On size

        if left.QuarterEmu < 0L || top.QuarterEmu < 0L || (left + width).CompareTo size.Width > 0 || (top + height).CompareTo size.Height > 0 then
            invalidArg (nameof other) "The area must lie within the slide."

        SlidePlacement(size, other, maximum)

    /// A copy that never scales a diagram by more than the factor.
    member _.WithMaximumScale(scale: Scale) =
        ArgumentNullException.ThrowIfNull scale
        SlidePlacement(size, area, Some scale)

    /// The fitted extent of a layout: the smallest box that holds its bounds and the geometry of every drawable.
    static member Extent(layout: DiagramLayout) =
        ArgumentNullException.ThrowIfNull layout
        let mutable box = layout.Bounds

        let point (p: LayoutPoint) = box <- box.Include p
        let rect (b: LayoutBox) = box <- box.Union b

        let path (data: PathData) =
            for command in data.Commands do
                command.Match(
                    (fun p -> point p),
                    (fun p -> point p),
                    (fun a b p -> point a; point b; point p),
                    (fun a p -> point a; point p),
                    (fun _ _ _ _ _ p -> point p),
                    (fun () -> ())
                )

        let rec visit (drawable: Drawable) =
            drawable.Kind.Match(
                (fun shape ->
                    rect shape.Box
                    match shape.Kind with
                    | ShapeKind.Path data -> path data
                    | _ -> ()
                    match shape.Text with
                    | null -> ()
                    | text -> rect text.Box),
                (fun line ->
                    for p in line.Waypoints do point p
                    point line.Source.Point
                    point line.Target.Point
                    match line.ExactPath with
                    | null -> ()
                    | exact -> path exact),
                (fun text -> rect text.Box),
                (fun image -> rect image.Box),
                (fun children -> for child in children do visit child),
                (fun container ->
                    rect container.Box
                    for child in container.Children do visit child),
                (fun children -> for child in children do visit child)
            )

        for drawable in layout.Drawables do visit drawable
        box

    /// Fits a layout to the area on a grid. Throws an argument error when the extent has no width or height, or the
    /// area is empty once rounded inwards onto the grid.
    member _.Fit(layout: DiagramLayout, grid: SlideGrid) =
        ArgumentNullException.ThrowIfNull layout
        ArgumentNullException.ThrowIfNull grid
        let extent = SlidePlacement.Extent layout

        if extent.Width.QuarterEmu = 0L || extent.Height.QuarterEmu = 0L then
            invalidArg (nameof layout) "The layout's extent has no width or no height."

        let step = grid.Step.QuarterEmu
        let left, top, width, height = area.On size
        let up (value: int64) = (if value % step = 0L then value / step elif value > 0L then value / step + 1L else value / step) * step
        let down (value: int64) = (if value % step = 0L then value / step elif value > 0L then value / step else value / step - 1L) * step
        let x0, y0 = up left.QuarterEmu, up top.QuarterEmu
        let x1, y1 = down (left + width).QuarterEmu, down (top + height).QuarterEmu

        if x1 <= x0 || y1 <= y0 then invalidArg (nameof grid) "The area is empty once rounded onto the grid."

        let areaWidth, areaHeight = Int128.op_Implicit (x1 - x0), Int128.op_Implicit (y1 - y0)
        let extentWidth, extentHeight = Int128.op_Implicit extent.Width.QuarterEmu, Int128.op_Implicit extent.Height.QuarterEmu

        // The smaller of the two ratios, compared exactly by cross-multiplying.
        let numerator, denominator =
            if areaWidth * extentHeight <= areaHeight * extentWidth then areaWidth, extentWidth else areaHeight, extentHeight

        let numerator, denominator =
            match maximum with
            | Some scale ->
                let limit = Int128.op_Implicit (int64 (Math.Round(scale.Value * 1_000_000.0)))
                let million = Int128.op_Implicit 1_000_000L
                if limit * denominator < numerator * million then limit, million else numerator, denominator
            | None -> numerator, denominator

        let areaBox = LayoutBox.Create(Distance.FromQuarterEmu x0, Distance.FromQuarterEmu y0, Distance.FromQuarterEmu(x1 - x0), Distance.FromQuarterEmu(y1 - y0))
        let unplaced = SlideFit(areaBox, extent, numerator, denominator, 0L, 0L, step)
        let spareX = (x1 - x0) - (unplaced.X extent.Right - unplaced.X extent.X).QuarterEmu
        let spareY = (y1 - y0) - (unplaced.Y extent.Bottom - unplaced.Y extent.Y).QuarterEmu
        let half (spare: int64) = (spare / 2L / step) * step
        SlideFit(areaBox, extent, numerator, denominator, half spareX, half spareY, step)

    /// The text form, for diagnostics and logs.
    override _.ToString() =
        let capped = maximum |> Option.map (fun scale -> $", at most {scale}") |> Option.defaultValue ""
        $"{size}, {area}{capped}"

    /// A 16:9 slide with the content area. The default.
    static member Widescreen = widescreen

    /// A 4:3 slide with the content area.
    static member Standard = standard

    /// A slide of a size with the content area.
    static member Create(size: SlideSize) =
        ArgumentNullException.ThrowIfNull size
        SlidePlacement(size, SlideArea.Content, None)
