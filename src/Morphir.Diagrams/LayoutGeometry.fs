// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Layout

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Runtime.InteropServices
open Morphir.Diagrams

/// A point in page coordinates: x to the right, y down.
[<Struct>]
type LayoutPoint =
    private
        { x: Distance
          y: Distance }

    /// The distance from the page's left edge.
    member this.X = this.x

    /// The distance from the page's top edge.
    member this.Y = this.y

    /// The point moved by another point's coordinates.
    member this.Offset(dx: Distance, dy: Distance) = { x = this.x + dx; y = this.y + dy }

    override this.ToString() = $"({this.x}, {this.y})"

    /// A point from two coordinates.
    static member Create(x: Distance, y: Distance) = { x = x; y = y }

    /// The origin.
    static member Origin = { x = Distance.Zero; y = Distance.Zero }

/// An axis-aligned box in page coordinates, with a width and height of zero or more.
[<Struct>]
type LayoutBox =
    private
        { left: Distance
          top: Distance
          width: Distance
          height: Distance }

    /// The left edge.
    member this.X = this.left

    /// The top edge.
    member this.Y = this.top

    /// The width.
    member this.Width = this.width

    /// The height.
    member this.Height = this.height

    /// The right edge.
    member this.Right = this.left + this.width

    /// The bottom edge.
    member this.Bottom = this.top + this.height

    /// The centre.
    member this.Centre =
        LayoutPoint.Create(this.left + Distance.FromQuarterEmu(this.width.QuarterEmu / 2L), this.top + Distance.FromQuarterEmu(this.height.QuarterEmu / 2L))

    /// The smallest box that holds this box and another.
    member this.Union(other: LayoutBox) =
        let left = Distance.Min(this.left, other.left)
        let top = Distance.Min(this.top, other.top)
        { left = left; top = top; width = Distance.Max(this.Right, other.Right) - left; height = Distance.Max(this.Bottom, other.Bottom) - top }

    /// The smallest box that holds this box and a point.
    member this.Include(point: LayoutPoint) =
        let left = Distance.Min(this.left, point.X)
        let top = Distance.Min(this.top, point.Y)
        { left = left; top = top; width = Distance.Max(this.Right, point.X) - left; height = Distance.Max(this.Bottom, point.Y) - top }

    override this.ToString() = $"{this.left},{this.top} {this.width}×{this.height}"

    /// A box from its left and top edges, width and height. Throws an argument error for a negative size.
    static member Create(x: Distance, y: Distance, width: Distance, height: Distance) =
        if width.QuarterEmu < 0L then invalidArg (nameof width) "A box cannot have a negative width."
        if height.QuarterEmu < 0L then invalidArg (nameof height) "A box cannot have a negative height."
        { left = x; top = y; width = width; height = height }

    /// The box of zero size at a point.
    static member At(point: LayoutPoint) = { left = point.X; top = point.Y; width = Distance.Zero; height = Distance.Zero }

/// One command of normalised path data. Every command is absolute.
[<RequireQualifiedAccess>]
type PathCommand =
    /// Starts a subpath at a point.
    | MoveTo of point: LayoutPoint
    /// A straight line to a point.
    | LineTo of point: LayoutPoint
    /// A cubic Bézier curve through two control points to a point.
    | CubicTo of control1: LayoutPoint * control2: LayoutPoint * point: LayoutPoint
    /// A quadratic Bézier curve through one control point to a point.
    | QuadraticTo of control: LayoutPoint * point: LayoutPoint
    /// An elliptical arc to a point, as SVG defines it.
    | ArcTo of radiusX: Distance * radiusY: Distance * rotation: float * largeArc: bool * sweep: bool * point: LayoutPoint
    /// Closes the subpath with a line to its start.
    | Close

    /// For C# callers: every case needs a function. Throws an invalid-operation error for a case added later.
    member this.Match
        (
            move: Func<LayoutPoint, 'T>,
            line: Func<LayoutPoint, 'T>,
            cubic: Func<LayoutPoint, LayoutPoint, LayoutPoint, 'T>,
            quadratic: Func<LayoutPoint, LayoutPoint, 'T>,
            arc: Func<Distance, Distance, float, bool, bool, LayoutPoint, 'T>,
            close: Func<'T>
        ) =
        match this with
        | PathCommand.MoveTo point -> move.Invoke point
        | PathCommand.LineTo point -> line.Invoke point
        | PathCommand.CubicTo(control1, control2, point) -> cubic.Invoke(control1, control2, point)
        | PathCommand.QuadraticTo(control, point) -> quadratic.Invoke(control, point)
        | PathCommand.ArcTo(rx, ry, rotation, large, sweep, point) -> arc.Invoke(rx, ry, rotation, large, sweep, point)
        | PathCommand.Close -> close.Invoke()

    /// The end point, or none for a close.
    member internal this.End =
        match this with
        | PathCommand.MoveTo point
        | PathCommand.LineTo point
        | PathCommand.CubicTo(_, _, point)
        | PathCommand.QuadraticTo(_, point)
        | PathCommand.ArcTo(_, _, _, _, _, point) -> Some point
        | PathCommand.Close -> None

    override this.ToString() =
        let n (distance: Distance) = distance.ToPixels().ToString("0.####", CultureInfo.InvariantCulture)
        let p (point: LayoutPoint) = n point.X + "," + n point.Y

        match this with
        | PathCommand.MoveTo point -> "M" + p point
        | PathCommand.LineTo point -> "L" + p point
        | PathCommand.CubicTo(c1, c2, point) -> $"C{p c1} {p c2} {p point}"
        | PathCommand.QuadraticTo(control, point) -> $"Q{p control} {p point}"
        | PathCommand.ArcTo(rx, ry, rotation, large, sweep, point) ->
            let flag value = if value then "1" else "0"
            let angle = rotation.ToString("0.####", CultureInfo.InvariantCulture)
            $"A{n rx},{n ry} {angle} {flag large},{flag sweep} {p point}"
        | PathCommand.Close -> "Z"

/// Reads SVG path grammar into normalised commands.
module internal PathReading =
    type private Scanner(text: string) =
        let mutable at = 0

        member _.SkipSeparators() =
            while at < text.Length && (Char.IsWhiteSpace text[at] || text[at] = ',') do
                at <- at + 1

        member this.AtEnd =
            this.SkipSeparators()
            at >= text.Length

        member this.PeekCommand =
            this.SkipSeparators()
            if at < text.Length && Char.IsLetter text[at] then Some text[at] else None

        member _.TakeCommand() =
            let command = text[at]
            at <- at + 1
            command

        member this.StartsNumber =
            this.SkipSeparators()
            at < text.Length && (Char.IsDigit text[at] || text[at] = '-' || text[at] = '+' || text[at] = '.')

        member this.Number() : decimal option =
            this.SkipSeparators()
            let start = at
            let digits () =
                let begin' = at
                while at < text.Length && Char.IsDigit text[at] do at <- at + 1
                at > begin'

            if at < text.Length && (text[at] = '-' || text[at] = '+') then at <- at + 1
            let whole = digits ()
            let fraction = at < text.Length && text[at] = '.' && (at <- at + 1; digits ())

            if not whole && not fraction then
                None
            else
                if at < text.Length && (text[at] = 'e' || text[at] = 'E') then
                    let mark = at
                    at <- at + 1
                    if at < text.Length && (text[at] = '-' || text[at] = '+') then at <- at + 1
                    if not (digits ()) then at <- mark

                match Decimal.TryParse(text.AsSpan(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, value -> Some value
                | false, _ -> None

        member this.Flag() =
            this.SkipSeparators()

            if at < text.Length && (text[at] = '0' || text[at] = '1') then
                let value = text[at] = '1'
                at <- at + 1
                Some value
            else
                None

    let fail message =
        Error(Diagnostic("layout/invalid-path", DiagnosticSeverity.Error, message, None))

    let read (text: string) : Result<ImmutableArray<PathCommand>, Diagnostic> =
        let scanner = Scanner text
        let commands = ImmutableArray.CreateBuilder<PathCommand>()
        let mutable current = LayoutPoint.Origin
        let mutable start = LayoutPoint.Origin
        let mutable lastCubic: LayoutPoint option = None
        let mutable lastQuadratic: LayoutPoint option = None
        let mutable error: string option = None
        let mutable command = ' '

        let distance (value: decimal) = Distance.FromPixelDecimal value

        let number () =
            match scanner.Number() with
            | Some value -> Some value
            | None ->
                error <- Some $"Command {command} expects a number."
                None

        let point relative =
            match number () with
            | None -> None
            | Some x ->
                match number () with
                | None -> None
                | Some y ->
                    let point = LayoutPoint.Create(distance x, distance y)
                    Some(if relative then point.Offset(current.X, current.Y) else point)

        let reflect (control: LayoutPoint option) =
            match control with
            | Some control -> LayoutPoint.Create(current.X + (current.X - control.X), current.Y + (current.Y - control.Y))
            | None -> current

        if scanner.AtEnd then
            fail "Path data must not be empty."
        else
            match scanner.PeekCommand with
            | Some('M' | 'm') -> ()
            | _ -> error <- Some "Path data must start with a move command."

            while error.IsNone && not scanner.AtEnd do
                match scanner.PeekCommand with
                | Some letter ->
                    scanner.TakeCommand() |> ignore
                    command <- letter
                | None -> ()

                let relative = Char.IsLower command
                let mutable cubic = None
                let mutable quadratic = None

                let emit next =
                    commands.Add next
                    next.End |> Option.iter (fun point -> current <- point)

                match Char.ToUpperInvariant command with
                | 'M' ->
                    match point relative with
                    | Some target ->
                        emit (PathCommand.MoveTo target)
                        start <- target
                        // Pairs after a move are lines.
                        command <- if relative then 'l' else 'L'
                    | None -> ()
                | 'L' -> point relative |> Option.iter (PathCommand.LineTo >> emit)
                | 'H' ->
                    number ()
                    |> Option.iter (fun x ->
                        let x = distance x
                        emit (PathCommand.LineTo(LayoutPoint.Create((if relative then current.X + x else x), current.Y))))
                | 'V' ->
                    number ()
                    |> Option.iter (fun y ->
                        let y = distance y
                        emit (PathCommand.LineTo(LayoutPoint.Create(current.X, (if relative then current.Y + y else y)))))
                | 'C' ->
                    match point relative with
                    | Some c1 ->
                        match point relative with
                        | Some c2 ->
                            match point relative with
                            | Some target ->
                                emit (PathCommand.CubicTo(c1, c2, target))
                                cubic <- Some c2
                            | None -> ()
                        | None -> ()
                    | None -> ()
                | 'S' ->
                    let c1 = reflect lastCubic

                    match point relative with
                    | Some c2 ->
                        match point relative with
                        | Some target ->
                            emit (PathCommand.CubicTo(c1, c2, target))
                            cubic <- Some c2
                        | None -> ()
                    | None -> ()
                | 'Q' ->
                    match point relative with
                    | Some control ->
                        match point relative with
                        | Some target ->
                            emit (PathCommand.QuadraticTo(control, target))
                            quadratic <- Some control
                        | None -> ()
                    | None -> ()
                | 'T' ->
                    let control = reflect lastQuadratic

                    match point relative with
                    | Some target ->
                        emit (PathCommand.QuadraticTo(control, target))
                        quadratic <- Some control
                    | None -> ()
                | 'A' ->
                    match number (), number (), number () with
                    | Some rx, Some ry, Some rotation ->
                        match scanner.Flag(), scanner.Flag() with
                        | Some large, Some sweep ->
                            point relative
                            |> Option.iter (fun target ->
                                emit (PathCommand.ArcTo(distance (abs rx), distance (abs ry), float rotation, large, sweep, target)))
                        | _ -> error <- Some "An arc's flags must each be 0 or 1."
                    | _ -> ()
                | 'Z' ->
                    commands.Add PathCommand.Close
                    current <- start
                    command <- ' '
                | _ -> error <- Some $"{command} is not a path command."

                lastCubic <- cubic
                lastQuadratic <- quadratic

                // A command letter must follow a close, and a number must follow every other command letter.
                if error.IsNone && command = ' ' && not scanner.AtEnd && scanner.PeekCommand.IsNone then
                    error <- Some "Expected a command after a close."

            match error with
            | Some message -> fail message
            | None -> Ok(commands.ToImmutable())

    /// The cubic curves that draw an SVG arc within a tolerance, or a line for a zero radius.
    let arcToCubics (tolerance: float) (from: LayoutPoint) rx ry (rotation: float) large sweep (target: LayoutPoint) : PathCommand list =
        let px (distance: Distance) = distance.ToPixels()
        let x0, y0, x1, y1 = px from.X, px from.Y, px target.X, px target.Y
        let mutable rx = abs (px rx)
        let mutable ry = abs (px ry)

        if x0 = x1 && y0 = y1 then
            []
        elif rx = 0.0 || ry = 0.0 then
            [ PathCommand.LineTo target ]
        else
            let phi = rotation * Math.PI / 180.0
            let cos, sin = Math.Cos phi, Math.Sin phi
            let dx, dy = (x0 - x1) / 2.0, (y0 - y1) / 2.0
            let x1', y1' = cos * dx + sin * dy, -sin * dx + cos * dy
            let lambda = x1' * x1' / (rx * rx) + y1' * y1' / (ry * ry)

            if lambda > 1.0 then
                rx <- rx * sqrt lambda
                ry <- ry * sqrt lambda

            let numerator = rx * rx * ry * ry - rx * rx * y1' * y1' - ry * ry * x1' * x1'
            let factor = sqrt (max 0.0 (numerator / (rx * rx * y1' * y1' + ry * ry * x1' * x1'))) * (if large = sweep then -1.0 else 1.0)
            let cx', cy' = factor * rx * y1' / ry, -factor * ry * x1' / rx
            let cx, cy = cos * cx' - sin * cy' + (x0 + x1) / 2.0, sin * cx' + cos * cy' + (y0 + y1) / 2.0
            let angle ux uy vx vy = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy)
            let theta = angle 1.0 0.0 ((x1' - cx') / rx) ((y1' - cy') / ry)
            let mutable delta = angle ((x1' - cx') / rx) ((y1' - cy') / ry) ((-x1' - cx') / rx) ((-y1' - cy') / ry)

            if not sweep && delta > 0.0 then delta <- delta - 2.0 * Math.PI
            if sweep && delta < 0.0 then delta <- delta + 2.0 * Math.PI

            // A cubic of a quarter circle strays by about 2.7e-4 of its radius, and the error grows as the sixth
            // power of the angle, so each segment's angle keeps the error within the tolerance.
            let radius = max rx ry
            let quarter = Math.PI / 2.0
            let limit = quarter * Math.Pow(min 1.0 (tolerance / (2.8e-4 * radius)), 1.0 / 6.0)
            let segments = max 1 (int (Math.Ceiling(abs delta / limit)))
            let step = delta / float segments
            let k = 4.0 / 3.0 * Math.Tan(step / 4.0)
            let at angle = cx + rx * Math.Cos angle * cos - ry * Math.Sin angle * sin, cy + rx * Math.Cos angle * sin + ry * Math.Sin angle * cos
            let tangent angle = -rx * Math.Sin angle * cos - ry * Math.Cos angle * sin, -rx * Math.Sin angle * sin + ry * Math.Cos angle * cos
            let point (x, y) = LayoutPoint.Create(Distance.FromPixels x, Distance.FromPixels y)

            [
                for index in 0 .. segments - 1 do
                    let a1 = theta + step * float index
                    let a2 = a1 + step
                    let p1x, p1y = at a1
                    let p2x, p2y = at a2
                    let t1x, t1y = tangent a1
                    let t2x, t2y = tangent a2
                    let finish = if index = segments - 1 then target else point (p2x, p2y)
                    PathCommand.CubicTo(point (p1x + k * t1x, p1y + k * t1y), point (p2x - k * t2x, p2y - k * t2y), finish)
            ]

/// Path data normalised to absolute move, line, cubic, quadratic, arc and close commands. Relative commands become
/// absolute, H and V become lines, and S and T become cubic and quadratic curves with their control points reflected.
[<Sealed>]
type PathData private (commands: ImmutableArray<PathCommand>) =
    /// The commands in order.
    member _.Commands = commands

    /// The same outline with each arc replaced by cubic curves that stay within the tolerance of it, for targets
    /// without arcs. A zero-radius arc becomes a line, and an arc that ends where it starts is removed.
    member _.WithoutArcs(tolerance: Distance) =
        if tolerance.QuarterEmu <= 0L then invalidArg (nameof tolerance) "The tolerance must be greater than zero."

        let builder = ImmutableArray.CreateBuilder<PathCommand>()
        let mutable current = LayoutPoint.Origin
        let mutable start = LayoutPoint.Origin

        for command in commands do
            match command with
            | PathCommand.ArcTo(rx, ry, rotation, large, sweep, target) ->
                builder.AddRange(PathReading.arcToCubics (tolerance.ToPixels()) current rx ry rotation large sweep target)
                current <- target
            | PathCommand.MoveTo point ->
                builder.Add command
                current <- point
                start <- point
            | PathCommand.Close ->
                builder.Add command
                current <- start
            | other ->
                builder.Add other
                other.End |> Option.iter (fun point -> current <- point)

        PathData(builder.ToImmutable())

    /// Whether any command is an arc.
    member _.HasArcs =
        commands |> Seq.exists (function | PathCommand.ArcTo _ -> true | _ -> false)

    /// The path as SVG path data, with coordinates in pixels.
    override _.ToString() =
        commands |> Seq.map string |> String.concat " "

    override _.Equals(other: obj | null) =
        match other with
        | :? PathData as path -> Seq.forall2 (=) commands path.Commands && commands.Length = path.Commands.Length
        | _ -> false

    override _.GetHashCode() =
        let mutable hash = HashCode()
        for command in commands do hash.Add command
        hash.ToHashCode()

    /// Parses SVG path data, or gives the reason it is not valid. For F# callers.
    static member Parse(text: string | null) : Result<PathData, Diagnostic> =
        match text with
        | null -> PathReading.fail "Path data must not be null."
        | text -> PathReading.read text |> Result.map PathData

    /// Parses SVG path data for C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<PathData>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Diagnostic>
        ) =
        match PathData.Parse text with
        | Ok path ->
            result <- path
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Parses path data from a literal in source code. Throws an argument error for invalid data.
    static member Create(literal: string) =
        match PathData.Parse literal with
        | Ok path -> path
        | Error error -> invalidArg (nameof literal) error.Message

    /// Path data from normalised commands.
    static member internal OfCommands(commands: PathCommand seq) = PathData(ImmutableArray.CreateRange commands)
