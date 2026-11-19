// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic

[<RequireQualifiedAccess>]
module internal FlowchartSvg =
    type private Bounds =
        {
            Left: int
            Top: int
            Right: int
            Bottom: int
        }

    let private includeBounds first second =
        {
            Left = min first.Left second.Left
            Top = min first.Top second.Top
            Right = max first.Right second.Right
            Bottom = max first.Bottom second.Bottom
        }

    let private groupBounds
        (model: Flowchart)
        (indices: Dictionary<DiagramId, int>)
        positions
        sizes
        bottom
        =
        let inner = Dictionary<DiagramId, Bounds>()
        let mutable nextEmptyY = bottom

        let add id bounds =
            inner[id] <-
                match inner.TryGetValue id with
                | true, existing -> includeBounds bounds existing
                | _ -> bounds

        model.Groups
        |> Seq.rev
        |> Seq.map (fun group ->
            for id in group.NodeIds do
                let i = indices[id]
                let x, y = Array.item i positions
                let w, h = Array.item i sizes

                add
                    group.Id
                    {
                        Left = x - w
                        Top = y - h
                        Right = x + w
                        Bottom = y + h
                    }

            let bounds =
                match inner.TryGetValue group.Id with
                | true, box ->
                    let width = max (box.Right - box.Left + 32) (group.Label.Length * 8 + 24)

                    let center = (box.Right + box.Left) / 2

                    {
                        Left = center - width / 2
                        Top = box.Top - 26
                        Right = center + width / 2
                        Bottom = box.Bottom + 16
                    }
                | _ ->
                    let width = max 160 (group.Label.Length * 8 + 40)

                    let empty =
                        {
                            Left = 48
                            Top = nextEmptyY
                            Right = 48 + width
                            Bottom = nextEmptyY + 42
                        }

                    nextEmptyY <- empty.Bottom + 64

                    empty

            match group.ParentId with
            | null -> ()
            | parent -> add parent bounds

            group, bounds
        )
        |> Seq.toArray
        |> Array.rev

    let private ranks count (edges: (int * int) array) =
        let outgoing = Array.init count (fun _ -> ResizeArray<int>())
        let incoming = Array.zeroCreate<int> count
        let levels = Array.zeroCreate<int> count

        for source, target in edges do
            if source <> target then
                outgoing[source].Add target

                incoming[target] <- incoming[target] + 1

        let ready = Queue<int>()

        for index in 0 .. count - 1 do
            if incoming[index] = 0 then
                ready.Enqueue index

        let mutable nextCycleRoot = 0

        for _ in 1..count do
            if ready.Count = 0 then
                // Break a cycle at the earliest remaining node in source order.
                while incoming[nextCycleRoot] = -1 do
                    nextCycleRoot <- nextCycleRoot + 1

                ready.Enqueue nextCycleRoot

            let source = ready.Dequeue()
            incoming[source] <- -1

            for target in outgoing[source] do
                if incoming[target] > 0 then
                    levels[target] <- max levels[target] (levels[source] + 1)

                    incoming[target] <- incoming[target] - 1

                    if incoming[target] = 0 then
                        ready.Enqueue target

        levels

    let private shape writer (node: FlowNode) x y halfWidth halfHeight =
        let left, right = x - halfWidth, x + halfWidth

        let top, bottom = y - halfHeight, y + halfHeight

        let rectangle radius =
            SvgWriting.rectangle
                writer
                left
                top
                (2 * halfWidth)
                (2 * halfHeight)
                radius
                SvgWriting.fill
                SvgWriting.ink

        let polygon points =
            let data =
                points |> List.map (fun (px, py) -> SvgWriting.point px py) |> String.concat " "

            SvgWriting.empty
                writer
                "polygon"
                [ "points", data; "fill", SvgWriting.fill; "stroke", SvgWriting.ink ]

        let circle radius fill =
            SvgWriting.circle writer x y radius fill SvgWriting.ink

        match node.Shape with
        | FlowShape.Rectangle -> rectangle 0
        | FlowShape.Rounded -> rectangle 8
        | FlowShape.Stadium -> rectangle halfHeight
        | FlowShape.Circle -> circle halfWidth SvgWriting.fill
        | FlowShape.DoubleCircle ->
            circle halfWidth SvgWriting.fill

            circle (halfWidth - 5) "none"
        | FlowShape.Diamond -> polygon [ x, top; right, y; x, bottom; left, y ]
        | FlowShape.Subroutine ->
            rectangle 0

            SvgWriting.path
                writer
                $"M{SvgWriting.point (left + 7) top} V{SvgWriting.number bottom} M{SvgWriting.point (right - 7) top} V{SvgWriting.number bottom}"
                [ "stroke", SvgWriting.ink ]
        | FlowShape.Cylinder ->
            rectangle 10

            SvgWriting.empty
                writer
                "ellipse"
                [
                    "cx", SvgWriting.number x
                    "cy", SvgWriting.number (top + 8)
                    "rx", SvgWriting.number halfWidth
                    "ry", "8"
                    "fill", SvgWriting.fill
                    "stroke", SvgWriting.ink
                ]
        | FlowShape.Hexagon ->
            polygon
                [
                    left, y
                    left + 12, top
                    right - 12, top
                    right, y
                    right - 12, bottom
                    left + 12, bottom
                ]
        | FlowShape.Asymmetric ->
            polygon [ left, y; left + 12, top; right, top; right, bottom; left + 12, bottom ]
        | FlowShape.Parallelogram ->
            polygon [ left + 12, top; right, top; right - 12, bottom; left, bottom ]
        | FlowShape.ReverseParallelogram ->
            polygon [ left, top; right - 12, top; right, bottom; left + 12, bottom ]
        | FlowShape.Trapezoid ->
            polygon [ left + 12, top; right - 12, top; right, bottom; left, bottom ]
        | FlowShape.ReverseTrapezoid ->
            polygon [ left, top; right, top; right - 12, bottom; left + 12, bottom ]

    let private dimensions (node: FlowNode) =
        let textWidth = node.Label.Length * 8 |> min 320

        match node.Shape with
        | FlowShape.Circle
        | FlowShape.DoubleCircle ->
            let radius = max 26 (textWidth / 2 + 12)

            radius, radius
        | FlowShape.Diamond -> max 45 (textWidth + 10), 32
        | _ -> max 40 (textWidth / 2 + 18), 23

    let render (model: Flowchart) =
        let indices = Dictionary<DiagramId, int>()

        let sizes = model.Nodes |> Seq.map dimensions |> Seq.toArray

        model.Nodes |> Seq.iteri (fun index node -> indices.Add(node.Id, index))

        let edges =
            model.Edges
            |> Seq.map (fun edge -> indices[edge.Source], indices[edge.Target])
            |> Seq.toArray

        let levels = ranks model.Nodes.Length edges

        let rankCount = 1 + (if levels.Length = 0 then 0 else Array.max levels)

        let positionsInRank = Array.zeroCreate<int> rankCount

        let maxWidth = sizes |> Array.fold (fun width (x, _) -> max width x) 40

        let maxHeight = sizes |> Array.fold (fun height (_, y) -> max height y) 23

        let horizontal =
            model.Direction = FlowDirection.LeftToRight
            || model.Direction = FlowDirection.RightToLeft

        let reversed =
            model.Direction = FlowDirection.RightToLeft
            || model.Direction = FlowDirection.BottomToTop

        let xStep, yStep = 2 * maxWidth + 96, 2 * maxHeight + 72

        let depthByGroup = Dictionary<DiagramId, int>()

        for group in model.Groups do
            let depth =
                match group.ParentId with
                | null -> 1
                | parent -> depthByGroup[parent] + 1

            depthByGroup.Add(group.Id, depth)

        let maxDepth = depthByGroup.Values |> Seq.fold max 0

        let labelMargin =
            model.Groups |> Seq.fold (fun widest group -> max widest (group.Label.Length * 4)) 0

        let margin = max 48 labelMargin + 26 * maxDepth

        let positions =
            levels
            |> Array.map (fun level ->
                let slot = positionsInRank[level]
                positionsInRank[level] <- slot + 1

                let rank = if reversed then rankCount - level - 1 else level

                let column, row = if horizontal then rank, slot else slot, rank

                margin + maxWidth + column * xStep, margin + maxHeight + row * yStep
            )

        let slots = max 1 (Array.max positionsInRank)
        let columns, rows = if horizontal then rankCount, slots else slots, rankCount

        let baseWidth = 2 * margin + (columns - 1) * xStep + 2 * maxWidth

        let baseHeight = 2 * margin + (rows - 1) * yStep + 2 * maxHeight

        let groups = groupBounds model indices positions sizes baseHeight

        let width =
            groups |> Array.fold (fun width (_, box) -> max width (box.Right + 24)) baseWidth

        let height =
            groups |> Array.fold (fun height (_, box) -> max height (box.Bottom + 24)) baseHeight

        let heads =
            model.Edges |> Seq.collect (fun edge -> [ edge.StartHead; edge.EndHead ])

        SvgWriting.document
            "Flowchart"
            width
            height
            heads
            (fun writer ->
                for group, box in groups do
                    SvgWriting.element
                        writer
                        "g"
                        [ "data-group", group.Id.Value ]
                        (fun () ->
                            SvgWriting.rectangle
                                writer
                                box.Left
                                box.Top
                                (box.Right - box.Left)
                                (box.Bottom - box.Top)
                                8
                                "#f5f8fd"
                                "#bac8db"

                            SvgWriting.text
                                writer
                                ((box.Left + box.Right) / 2)
                                (box.Top + 18)
                                group.Label
                        )

                let edgeCounts = Dictionary<int * int, int>()

                for index in 0 .. edges.Length - 1 do
                    let source, target = edges[index]
                    let edge = model.Edges[index]

                    if edge.Line <> FlowLine.Invisible then
                        let sx, sy = positions[source]
                        let tx, ty = positions[target]
                        let sw, sh = sizes[source]
                        let tw, th = sizes[target]
                        let key = min source target, max source target
                        let _, count = edgeCounts.TryGetValue key
                        edgeCounts[key] <- count + 1

                        let shift =
                            (if count % 2 = 0 then -1 else 1) * min 24 (8 * ((count + 1) / 2))

                        let startPoint, bend1, bend2, endPoint, (labelX, labelY) =
                            if source = target then
                                let routeX = sx + sw + 22 + shift

                                let top = sy - sh - 22

                                (sx + sw, sy),
                                (routeX, sy),
                                (routeX, top),
                                (sx, top),
                                (routeX, top - 6)
                            elif (horizontal && sx <> tx) || sy = ty then
                                let sign = if tx > sx then 1 else -1

                                let a, b = sx + sign * sw, tx - sign * tw

                                let mid = (a + b) / 2 + shift

                                (a, sy), (mid, sy), (mid, ty), (b, ty), (mid, min sy ty - 9)
                            else
                                let sign = if ty > sy then 1 else -1

                                let a, b = sy + sign * sh, ty - sign * th

                                let mid = (a + b) / 2 + shift

                                (sx, a),
                                (sx, mid),
                                (tx, mid),
                                (tx, b),
                                ((sx + tx) / 2 + 15, mid - 8)

                        let point (x, y) = SvgWriting.point x y

                        let finish =
                            if source = target then
                                $" L{SvgWriting.point sx (sy - sh)}"
                            else
                                ""

                        let route =
                            $"M{point startPoint} L{point bend1} L{point bend2} L{point endPoint}{finish}"

                        SvgWriting.connector
                            writer
                            route
                            edge.StartHead
                            edge.EndHead
                            (edge.Line = FlowLine.Dotted)
                            (if edge.Line = FlowLine.Thick then 3 else 1)
                            [ "data-source", edge.Source.Value; "data-target", edge.Target.Value ]

                        if edge.StartHead <> EdgeHead.None || edge.EndHead <> EdgeHead.None then
                            let reversed = edge.EndHead = EdgeHead.None
                            writer.BindConnector edge.Source edge.Target route reversed

                        match edge.Label with
                        | null -> ()
                        | label -> SvgWriting.text writer labelX labelY label

                model.Nodes
                |> Seq.iteri (fun index node ->
                    let x, y = positions[index]
                    let w, h = sizes[index]

                    SvgWriting.element
                        writer
                        "g"
                        [ "data-node", node.Id.Value; "data-shape", node.Shape.WireValue ]
                        (fun () ->
                            shape writer node x y w h
                            SvgWriting.text writer x (y + 5) node.Label
                        )
                )
            )
