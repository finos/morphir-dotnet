// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

/// Turns routed connector points into final SVG path data: the connector style and bridges over earlier crossings.
[<RequireQualifiedAccess>]
module internal Connectors =
    type Point = int * int

    let private bridgeRadius = 5
    let private cornerRadius = 8
    let private clearance = 8

    let private segments (points: Point array) =
        points |> Array.pairwise |> Array.filter (fun (first, second) -> first <> second)

    /// Where a horizontal and a vertical segment cross, away from both segments' ends.
    let private crossing ((ax1, ay1), (ax2, ay2)) ((bx1, by1), (bx2, by2)) =
        let across (hx1, hy, hx2) (vx, vy1, vy2) =
            if
                vx > min hx1 hx2 + clearance
                && vx < max hx1 hx2 - clearance
                && hy > min vy1 vy2 + clearance
                && hy < max vy1 vy2 - clearance
            then
                Some(vx, hy)
            else
                None

        if ay1 = ay2 && bx1 = bx2 then across (ax1, ay1, ax2) (bx1, by1, by2)
        elif ax1 = ax2 && by1 = by2 then across (bx1, by1, bx2) (ax1, ay1, ay2)
        else None

    /// For each route, the bridge points on each of its segments: crossings with earlier routes that share
    /// no endpoint, ordered along the segment.
    let bridges (routes: (Point array * int * int) array) =
        routes
        |> Array.mapi (fun index (points, source, target) ->
            segments points
            |> Array.map (fun ((x1, y1), _ as segment) ->
                [
                    for earlier in 0 .. index - 1 do
                        let other, first, second = routes[earlier]

                        if first <> source && first <> target && second <> source && second <> target then
                            for candidate in segments other do
                                match crossing segment candidate with
                                | Some point -> yield point
                                | None -> ()
                ]
                |> List.distinct
                |> List.sortBy (fun (x, y) -> abs (x - x1) + abs (y - y1))))

    let private direction ((x1, y1): Point) ((x2, y2): Point) = sign (x2 - x1), sign (y2 - y1)

    let private length ((x1, y1): Point) ((x2, y2): Point) = abs (x2 - x1) + abs (y2 - y1)

    let private point ((x, y): Point) = SvgWriting.point x y

    /// SVG path data for a route in a style, with arcs over the given bridge points of each segment.
    let path (style: ConnectorStyle) (points: Point array) (hops: Point list array) =
        let curved =
            match style, points.Length with
            | ConnectorStyle.Curved, 3 -> Some $"M{point points[0]} Q{point points[1]} {point points[2]}"
            | ConnectorStyle.Curved, 4 ->
                Some $"M{point points[0]} C{point points[1]} {point points[2]} {point points[3]}"
            | _ -> None

        match curved with
        | Some data -> data
        | None when style = ConnectorStyle.Elbow && hops |> Array.forall List.isEmpty ->
            "M" + (points |> Array.map point |> String.concat " L")
        | None ->
            let route = segments points
            let builder = System.Text.StringBuilder()
            builder.Append('M').Append(point (fst route[0])) |> ignore

            for index in 0 .. route.Length - 1 do
                let start, finish = route[index]
                let dx, dy = direction start finish

                for x, y in (if index < hops.Length then hops[index] else []) do
                    let before = x - dx * bridgeRadius, y - dy * bridgeRadius
                    let after = x + dx * bridgeRadius, y + dy * bridgeRadius
                    let sweep = if dx + dy > 0 then 1 else 0

                    builder
                        .Append(" L")
                        .Append(point before)
                        .Append($" A{bridgeRadius} {bridgeRadius} 0 0 {sweep} ")
                        .Append(point after)
                    |> ignore

                if style <> ConnectorStyle.Elbow && index < route.Length - 1 then
                    let next = snd route[index + 1]
                    let nx, ny = direction finish next

                    let radius =
                        min cornerRadius (min (length start finish / 2) (length finish next / 2))

                    let fx, fy = finish

                    builder
                        .Append(" L")
                        .Append(point (fx - dx * radius, fy - dy * radius))
                        .Append(" Q")
                        .Append(point finish)
                        .Append(' ')
                        .Append(point (fx + nx * radius, fy + ny * radius))
                    |> ignore
                else
                    builder.Append(" L").Append(point finish) |> ignore

            builder.ToString()
