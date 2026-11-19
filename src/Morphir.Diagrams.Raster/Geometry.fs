// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Raster

open System
open System.Collections.Generic
open System.Globalization

/// Sine and cosine from additions and multiplications only, so every platform gives the same bits. Library
/// trigonometry may differ in the last bit between operating systems.
[<RequireQualifiedAccess>]
module internal Trig =
    let private twoPi = 6.283185307179586

    let private series (x: double) =
        // sin x for |x| <= pi/4 by Taylor series to x^15.
        let x2 = x * x
        x * (1.0 - x2 / 6.0 * (1.0 - x2 / 20.0 * (1.0 - x2 / 42.0 * (1.0 - x2 / 72.0 * (1.0 - x2 / 110.0 * (1.0 - x2 / 156.0 * (1.0 - x2 / 210.0)))))))

    let private cosSeries (x: double) =
        let x2 = x * x
        1.0 - x2 / 2.0 * (1.0 - x2 / 12.0 * (1.0 - x2 / 30.0 * (1.0 - x2 / 56.0 * (1.0 - x2 / 90.0 * (1.0 - x2 / 132.0 * (1.0 - x2 / 182.0))))))

    /// sin and cos of an angle in radians.
    let sinCos (angle: double) =
        let turns = Math.Floor(angle / twoPi)
        let a = angle - turns * twoPi
        let quarter = Math.PI / 2.0
        let q = int (Math.Floor(a / quarter))
        let r = a - float q * quarter
        // Reduce to [-pi/4, pi/4] around the nearest multiple of pi/2.
        let r, q = if r > Math.PI / 4.0 then r - quarter, q + 1 else r, q
        let s, c = series r, cosSeries r

        match ((q % 4) + 4) % 4 with
        | 0 -> s, c
        | 1 -> c, -s
        | 2 -> -s, -c
        | _ -> -c, s

    /// atan2 by bisection on the deterministic sine and cosine: only for marker orientation, where 1e-9 is plenty.
    let atan2 (y: double) (x: double) =
        if x = 0.0 && y = 0.0 then 0.0
        else
            let length = Math.Sqrt(x * x + y * y)
            let nx, ny = x / length, y / length
            // Search the angle in [-pi, pi] whose cosine and sine are nearest, by bisection on the half-plane.
            let mutable low = -Math.PI
            let mutable high = Math.PI

            for _ in 1..60 do
                let middle = (low + high) / 2.0
                let s, c = sinCos middle
                // The cross product's sign says on which side of `middle` the direction lies.
                if c * ny - s * nx > 0.0 then low <- middle else high <- middle

            (low + high) / 2.0

/// A 2D affine transform `[a c e; b d f]`.
[<Struct>]
type internal Affine =
    {
        A: double
        B: double
        C: double
        D: double
        E: double
        F: double
    }

    static member Identity = { A = 1.0; B = 0.0; C = 0.0; D = 1.0; E = 0.0; F = 0.0 }
    static member Translate(x, y) = { Affine.Identity with E = x; F = y }
    static member Scale(x, y) = { Affine.Identity with A = x; D = y }

    static member Rotate(degrees: double) =
        let s, c = Trig.sinCos (degrees * Math.PI / 180.0)
        { A = c; B = s; C = -s; D = c; E = 0.0; F = 0.0 }

    /// This transform applied after `inner`.
    member m.Then(inner: Affine) =
        {
            A = m.A * inner.A + m.C * inner.B
            B = m.B * inner.A + m.D * inner.B
            C = m.A * inner.C + m.C * inner.D
            D = m.B * inner.C + m.D * inner.D
            E = m.A * inner.E + m.C * inner.F + m.E
            F = m.B * inner.E + m.D * inner.F + m.F
        }

    member m.Apply(x: double, y: double) = m.A * x + m.C * y + m.E, m.B * x + m.D * y + m.F


/// Paths as polylines: each subpath a list of points, and whether it is closed.
[<RequireQualifiedAccess>]
module internal Geometry =
    type Subpath = { Points: ResizeArray<double * double>; mutable Closed: bool }

    let private number = Globalization.NumberStyles.Float

    /// The numbers and command letters of path data, in order.
    let private tokens (data: string) =
        let result = ResizeArray<Choice<char, double>>()
        let mutable index = 0

        while index < data.Length do
            let c = data[index]

            if Char.IsLetter c && c <> 'e' && c <> 'E' then
                result.Add(Choice1Of2 c)
                index <- index + 1
            elif Char.IsDigit c || c = '-' || c = '+' || c = '.' then
                let start = index
                index <- index + 1
                let mutable dot = c = '.'
                let mutable more = true

                while more && index < data.Length do
                    let d = data[index]

                    if Char.IsDigit d then index <- index + 1
                    elif d = '.' && not dot then
                        dot <- true
                        index <- index + 1
                    elif (d = 'e' || d = 'E') && index + 1 < data.Length then
                        index <- index + 1
                        if data[index] = '-' || data[index] = '+' then index <- index + 1
                    else more <- false

                match Double.TryParse(data.AsSpan(start, index - start), number, CultureInfo.InvariantCulture) with
                | true, value -> result.Add(Choice2Of2 value)
                | _ -> ()
            else
                index <- index + 1

        result

    let private quadSteps = 12
    let private cubicSteps = 16

    /// Flattens SVG path data into subpaths in user space.
    let parse (data: string) =
        let all = ResizeArray<Subpath>()
        let items = tokens data
        let mutable index = 0
        let mutable command = 'M'
        let mutable x, y = 0.0, 0.0
        let mutable startX, startY = 0.0, 0.0
        let mutable lastControl = None
        let mutable current: Subpath option = None

        let point (px, py) =
            match current with
            | Some subpath -> subpath.Points.Add((px, py))
            | None ->
                let subpath = { Points = ResizeArray [ (x, y); (px, py) ]; Closed = false }
                all.Add subpath
                current <- Some subpath

        let next () =
            match items[index] with
            | Choice2Of2 value ->
                index <- index + 1
                value
            | _ -> nan

        let hasNumber () = index < items.Count && (match items[index] with Choice2Of2 _ -> true | _ -> false)

        while index < items.Count do
            match items[index] with
            | Choice1Of2 c ->
                command <- c
                index <- index + 1
                if (c = 'Z' || c = 'z') then
                    match current with
                    | Some subpath ->
                        subpath.Closed <- true
                        current <- None
                    | None -> ()

                    x <- startX
                    y <- startY
                    lastControl <- None
            | Choice2Of2 _ ->
                let relative = Char.IsLower command
                let ox, oy = if relative then x, y else 0.0, 0.0

                match Char.ToUpperInvariant command with
                | 'M' ->
                    let px = ox + next ()
                    let py = oy + next ()
                    x <- px
                    y <- py
                    startX <- px
                    startY <- py
                    let subpath = { Points = ResizeArray [ (px, py) ]; Closed = false }
                    all.Add subpath
                    current <- Some subpath
                    command <- if relative then 'l' else 'L'
                    lastControl <- None
                | 'L' ->
                    let px = ox + next ()
                    let py = oy + next ()
                    point (px, py)
                    x <- px
                    y <- py
                    lastControl <- None
                | 'H' ->
                    let px = ox + next ()
                    point (px, y)
                    x <- px
                    lastControl <- None
                | 'V' ->
                    let py = oy + next ()
                    point (x, py)
                    y <- py
                    lastControl <- None
                | 'C' | 'S' ->
                    let c1x, c1y =
                        if Char.ToUpperInvariant command = 'C' then
                            let a = ox + next ()
                            a, oy + next ()
                        else
                            match lastControl with
                            | Some(cx, cy, true) -> 2.0 * x - cx, 2.0 * y - cy
                            | _ -> x, y

                    let c2x = ox + next ()
                    let c2y = oy + next ()
                    let ex = ox + next ()
                    let ey = oy + next ()

                    for step in 1..cubicSteps do
                        let t = float step / float cubicSteps
                        let u = 1.0 - t
                        point (u * u * u * x + 3.0 * u * u * t * c1x + 3.0 * u * t * t * c2x + t * t * t * ex, u * u * u * y + 3.0 * u * u * t * c1y + 3.0 * u * t * t * c2y + t * t * t * ey)

                    lastControl <- Some(c2x, c2y, true)
                    x <- ex
                    y <- ey
                | 'Q' | 'T' ->
                    let cx, cy =
                        if Char.ToUpperInvariant command = 'Q' then
                            let a = ox + next ()
                            a, oy + next ()
                        else
                            match lastControl with
                            | Some(px, py, false) -> 2.0 * x - px, 2.0 * y - py
                            | _ -> x, y

                    let ex = ox + next ()
                    let ey = oy + next ()

                    for step in 1..quadSteps do
                        let t = float step / float quadSteps
                        let u = 1.0 - t
                        point (u * u * x + 2.0 * u * t * cx + t * t * ex, u * u * y + 2.0 * u * t * cy + t * t * ey)

                    lastControl <- Some(cx, cy, false)
                    x <- ex
                    y <- ey
                | 'A' ->
                    let rx0 = Math.Abs(next ())
                    let ry0 = Math.Abs(next ())
                    let rotation = next ()
                    let large = next () <> 0.0
                    let sweep = next () <> 0.0
                    let ex = ox + next ()
                    let ey = oy + next ()

                    if rx0 = 0.0 || ry0 = 0.0 || (ex = x && ey = y) then
                        point (ex, ey)
                    else
                        // Endpoint to centre parameterisation, SVG 1.1 appendix F.6.
                        let sinPhi, cosPhi = Trig.sinCos (rotation * Math.PI / 180.0)
                        let dx, dy = (x - ex) / 2.0, (y - ey) / 2.0
                        let x1 = cosPhi * dx + sinPhi * dy
                        let y1 = -sinPhi * dx + cosPhi * dy
                        let lambda = (x1 * x1) / (rx0 * rx0) + (y1 * y1) / (ry0 * ry0)
                        let rx, ry = if lambda > 1.0 then rx0 * Math.Sqrt lambda, ry0 * Math.Sqrt lambda else rx0, ry0
                        let numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
                        let denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1
                        let factor = Math.Sqrt(Math.Max(0.0, numerator / denominator)) * (if large = sweep then -1.0 else 1.0)
                        let cx1 = factor * rx * y1 / ry
                        let cy1 = -factor * ry * x1 / rx
                        let cx = cosPhi * cx1 - sinPhi * cy1 + (x + ex) / 2.0
                        let cy = sinPhi * cx1 + cosPhi * cy1 + (y + ey) / 2.0
                        let start = Trig.atan2 ((y1 - cy1) / ry) ((x1 - cx1) / rx)
                        let finish = Trig.atan2 ((-y1 - cy1) / ry) ((-x1 - cx1) / rx)
                        let mutable delta = finish - start

                        if sweep && delta < 0.0 then delta <- delta + 2.0 * Math.PI
                        elif not sweep && delta > 0.0 then delta <- delta - 2.0 * Math.PI

                        let steps = max 4 (int (Math.Ceiling(Math.Abs delta / (Math.PI / 16.0))))

                        for step in 1..steps do
                            if step = steps then point (ex, ey)
                            else
                                let s, c = Trig.sinCos (start + delta * float step / float steps)
                                point (cosPhi * rx * c - sinPhi * ry * s + cx, sinPhi * rx * c + cosPhi * ry * s + cy)

                    x <- ex
                    y <- ey
                    lastControl <- None
                | _ -> index <- index + 1

                if not (hasNumber ()) || Char.ToUpperInvariant command = 'Z' then ()

        all

    /// A circle or ellipse as one closed subpath.
    let ellipse (cx: double) (cy: double) (rx: double) (ry: double) =
        let steps = max 16 (int (Math.Ceiling((rx + ry) * 1.2)) |> min 96)
        let points = ResizeArray<double * double>()

        for step in 0 .. steps - 1 do
            let s, c = Trig.sinCos (2.0 * Math.PI * float step / float steps)
            points.Add((cx + rx * c, cy + ry * s))

        { Points = points; Closed = true }

    /// A rectangle with optional rounded corners as one closed subpath.
    let rectangle (x: double) (y: double) (width: double) (height: double) (rx: double) (ry: double) =
        let rx = min rx (width / 2.0)
        let ry = min ry (height / 2.0)

        if rx <= 0.0 || ry <= 0.0 then
            { Points = ResizeArray [ (x, y); (x + width, y); (x + width, y + height); (x, y + height) ]; Closed = true }
        else
            let points = ResizeArray<double * double>()

            let corner cx cy fromAngle =
                for step in 0..6 do
                    let s, c = Trig.sinCos ((fromAngle + 90.0 * float step / 6.0) * Math.PI / 180.0)
                    points.Add((cx + rx * c, cy + ry * s))

            corner (x + width - rx) (y + ry) 270.0
            corner (x + width - rx) (y + height - ry) 0.0
            corner (x + rx) (y + height - ry) 90.0
            corner (x + rx) (y + ry) 180.0
            { Points = points; Closed = true }

    /// Splits subpaths by a dash pattern into open pieces.
    let dash (pattern: double list) (subpaths: Subpath seq) =
        let pattern = pattern |> List.filter (fun value -> value >= 0.0)

        if pattern.IsEmpty || List.sum pattern <= 0.0 then
            List.ofSeq subpaths
        else
            let cycle = if pattern.Length % 2 = 1 then pattern @ pattern else pattern
            let lengths = Array.ofList cycle

            [
                for subpath in subpaths do
                    let points = List.ofSeq subpath.Points
                    let points = if subpath.Closed && not points.IsEmpty then points @ [ points.Head ] else points
                    let mutable which = 0
                    let mutable left = lengths[0]
                    let mutable current = ResizeArray<double * double>()
                    let pieces = ResizeArray<Subpath>()

                    match points with
                    | first :: _ -> current.Add first
                    | [] -> ()

                    for (ax, ay), (bx, by) in List.pairwise points do
                        let mutable sx, sy = ax, ay
                        let mutable remaining = Math.Sqrt((bx - ax) ** 2.0 + (by - ay) ** 2.0)
                        let length = remaining

                        while remaining > 0.0 do
                            let take = min left remaining
                            let t = if length = 0.0 then 1.0 else take / length
                            let nx, ny = sx + (bx - ax) * t, sy + (by - ay) * t

                            if which % 2 = 0 then current.Add((nx, ny))

                            sx <- nx
                            sy <- ny
                            remaining <- remaining - take
                            left <- left - take

                            if left <= 1e-9 then
                                if which % 2 = 0 && current.Count > 1 then pieces.Add { Points = current; Closed = false }
                                which <- (which + 1) % lengths.Length
                                left <- lengths[which]
                                current <- ResizeArray [ (sx, sy) ]

                    if which % 2 = 0 && current.Count > 1 then pieces.Add { Points = current; Closed = false }
                    yield! pieces
            ]

    /// The outline of a stroke as closed counter-clockwise polygons: a quadrilateral per segment, a disc at each joint
    /// and, for round caps, at each end. Filled with the non-zero rule, the pieces make one shape.
    let stroke (width: double) (roundCaps: bool) (subpaths: Subpath seq) =
        let half = width / 2.0
        let result = ResizeArray<ResizeArray<double * double>>()

        let oriented (points: ResizeArray<double * double>) =
            let mutable area = 0.0

            for index in 0 .. points.Count - 1 do
                let x1, y1 = points[index]
                let x2, y2 = points[(index + 1) % points.Count]
                area <- area + x1 * y2 - x2 * y1

            if area < 0.0 then points.Reverse()
            result.Add points

        let disc (cx, cy) =
            let steps = max 8 (int (Math.Ceiling(half * 4.0)) |> min 32)

            oriented (
                ResizeArray(
                    seq {
                        for step in 0 .. steps - 1 do
                            let s, c = Trig.sinCos (2.0 * Math.PI * float step / float steps)
                            yield cx + half * c, cy + half * s
                    }
                )
            )

        for subpath in subpaths do
            let points = List.ofSeq subpath.Points |> List.fold (fun acc point -> match acc with | last :: _ when last = point -> acc | _ -> point :: acc) [] |> List.rev
            let points = if subpath.Closed && points.Length > 2 then points @ [ points.Head ] else points

            for (ax, ay), (bx, by) in List.pairwise points do
                let length = Math.Sqrt((bx - ax) ** 2.0 + (by - ay) ** 2.0)

                if length > 0.0 then
                    let nx, ny = -(by - ay) / length * half, (bx - ax) / length * half
                    oriented (ResizeArray [ (ax + nx, ay + ny); (bx + nx, by + ny); (bx - nx, by - ny); (ax - nx, ay - ny) ])

            // Joints, and the ends of a closed or round-capped line.
            match points with
            | [] -> ()
            | [ single ] -> if roundCaps then disc single
            | _ ->
                let inner = points |> List.tail |> List.take (points.Length - 2)
                for joint in inner do disc joint
                if subpath.Closed || roundCaps then
                    disc points.Head
                    disc (List.last points)

        result
