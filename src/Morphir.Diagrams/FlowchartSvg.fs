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

    /// Nodes and nested subgraphs placed on a grid of ranks and rows, relative to the top of the block.
    type private Block =
        {
            /// Each node of the block with its rank and row.
            Cells: (int * int * int) list
            /// The rows that the block takes.
            Height: int
            First: int
            Last: int
            /// The lowest source index among its nodes, which orders the block among its siblings.
            Lowest: int
        }

    /// The row of each node. A subgraph takes a band of rows across all the ranks that it spans, and no other node
    /// is placed in that band, so that the box of the subgraph holds only its own nodes. In source order, each node
    /// takes the free row of its rank nearest to the mean row of the nodes already placed that link to it, or the
    /// first free row when none does.
    let private rows (model: Flowchart) (indices: Dictionary<DiagramId, int>) (levels: int array) (edges: (int * int) array) =
        let predecessors = Array.init levels.Length (fun _ -> ResizeArray<int>())

        for source, target in edges do
            if source <> target then
                predecessors[target].Add source

        let children = Dictionary<DiagramId, ResizeArray<FlowGroup>>()
        let topGroups = ResizeArray<FlowGroup>()
        let grouped = HashSet<int>()

        for group in model.Groups do
            for id in group.NodeIds do
                grouped.Add indices[id] |> ignore

            match group.ParentId with
            | null -> topGroups.Add group
            | parent ->
                match children.TryGetValue parent with
                | true, list -> list.Add group
                | _ -> children[parent] <- ResizeArray [ group ]

        let childrenOf (group: FlowGroup) =
            match children.TryGetValue group.Id with
            | true, list -> list :> seq<FlowGroup>
            | _ -> Seq.empty

        let rec place (nodes: int seq) (groups: FlowGroup seq) =
            let blocks = groups |> Seq.choose blockOf |> Seq.map Choice2Of2
            let items =
                Seq.append (nodes |> Seq.map Choice1Of2) blocks
                |> Seq.sortBy (function
                    | Choice1Of2 node -> node
                    | Choice2Of2 block -> block.Lowest)

            let taken = HashSet<int * int>()
            let cells = ResizeArray<int * int * int>()
            let placed = Dictionary<int, int>()

            for item in items do
                match item with
                | Choice1Of2 node ->
                    let rank = levels[node]

                    let leading =
                        predecessors[node]
                        |> Seq.choose (fun source ->
                            match placed.TryGetValue source with
                            | true, row -> Some row
                            | _ -> None
                        )
                        |> Seq.toArray

                    let preferred =
                        if leading.Length = 0 then 0 else Array.sum leading / leading.Length

                    // The nearest free row, below before above.
                    let candidates =
                        Seq.initInfinite id
                        |> Seq.collect (fun distance -> [ preferred + distance; preferred - distance ])
                        |> Seq.filter (fun row -> row >= 0)

                    let row = candidates |> Seq.find (fun row -> not (taken.Contains((rank, row))))

                    taken.Add((rank, row)) |> ignore
                    placed[node] <- row
                    cells.Add((node, rank, row))
                | Choice2Of2 block ->
                    let band offset =
                        [ for rank in block.First .. block.Last do
                              for row in offset .. offset + block.Height - 1 -> rank, row ]

                    let mutable offset = 0

                    while band offset |> List.exists taken.Contains do
                        offset <- offset + 1

                    for cell in band offset do
                        taken.Add cell |> ignore

                    for node, rank, row in block.Cells do
                        placed[node] <- row + offset
                        cells.Add((node, rank, row + offset))

            if cells.Count = 0 then
                None
            else
                Some
                    {
                        Cells = List.ofSeq cells
                        Height = 1 + (taken |> Seq.map snd |> Seq.max)
                        First = cells |> Seq.map (fun (_, rank, _) -> rank) |> Seq.min
                        Last = cells |> Seq.map (fun (_, rank, _) -> rank) |> Seq.max
                        Lowest = cells |> Seq.map (fun (node, _, _) -> node) |> Seq.min
                    }

        and blockOf (group: FlowGroup) =
            place (group.NodeIds |> Seq.map (fun id -> indices[id])) (childrenOf group)

        let result = Array.zeroCreate<int> levels.Length

        match place (Seq.init levels.Length id |> Seq.filter (grouped.Contains >> not)) topGroups with
        | Some root ->
            for node, _, row in root.Cells do
                result[node] <- row
        | None -> ()

        result

    /// The colour family of a shape, as diagrams.mbt assigns it.
    let private family shape =
        match shape with
        | FlowShape.Diamond
        | FlowShape.Hexagon -> ColorFamily.Cloud
        | FlowShape.Cylinder -> ColorFamily.Database
        | FlowShape.Circle
        | FlowShape.DoubleCircle -> ColorFamily.MessageBus
        | _ -> ColorFamily.Backend

    /// Paints the shape of a node and returns its colour family.
    let private shape writer (node: FlowNode) x y halfWidth halfHeight =
        let left, right = x - halfWidth, x + halfWidth

        let top, bottom = y - halfHeight, y + halfHeight
        let family = family node.Shape
        let fill, stroke = Theme.fill family, Theme.stroke family

        let paint (role: string) fill =
            [ "class", role; "fill", fill; "stroke", stroke; "stroke-width", "1.5" ]

        let rectangle radius =
            SvgWriting.empty
                writer
                "rect"
                [
                    "x", SvgWriting.number left
                    "y", SvgWriting.number top
                    "width", SvgWriting.number (2 * halfWidth)
                    "height", SvgWriting.number (2 * halfHeight)
                    "rx", SvgWriting.number radius
                    yield! paint "outline" fill
                ]

        let polygon points =
            let data =
                points |> List.map (fun (px, py) -> SvgWriting.point px py) |> String.concat " "

            SvgWriting.empty writer "polygon" (("points", data) :: paint "outline" fill)

        let circle role radius fill =
            SvgWriting.empty
                writer
                "circle"
                [
                    "cx", SvgWriting.number x
                    "cy", SvgWriting.number y
                    "r", SvgWriting.number radius
                    yield! paint role fill
                ]

        match node.Shape with
        | FlowShape.Rectangle -> rectangle 0
        | FlowShape.Rounded -> rectangle 8
        | FlowShape.Stadium -> rectangle halfHeight
        | FlowShape.Circle -> circle "outline" halfWidth fill
        | FlowShape.DoubleCircle ->
            circle "outline" halfWidth fill

            circle "decoration" (halfWidth - 6) "none"
        | FlowShape.Diamond -> polygon [ x, top; right, y; x, bottom; left, y ]
        | FlowShape.Subroutine ->
            rectangle 0

            SvgWriting.path
                writer
                $"M{SvgWriting.point (left + 10) top} V{SvgWriting.number bottom} M{SvgWriting.point (right - 10) top} V{SvgWriting.number bottom}"
                (paint "decoration" "none")
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
                    yield! paint "decoration" "none"
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

        family

    let private dimensions (node: FlowNode) =
        let textWidth = node.Label.Length * 8 |> min 320

        match node.Shape with
        | FlowShape.Circle
        | FlowShape.DoubleCircle ->
            let radius = max 26 (textWidth / 2 + 12)

            radius, radius
        | FlowShape.Diamond -> max 45 (textWidth + 10), 32
        | _ -> max 40 (textWidth / 2 + 18), 23

    let render (options: SvgRenderOptions) (metadata: Morphir.Svg.SvgNode list option) (model: Flowchart) =
        let indices = Dictionary<DiagramId, int>()

        let sizes = model.Nodes |> Seq.map dimensions |> Seq.toArray

        model.Nodes |> Seq.iteri (fun index node -> indices.Add(node.Id, index))

        let edges =
            model.Edges
            |> Seq.map (fun edge -> indices[edge.Source], indices[edge.Target])
            |> Seq.toArray

        let levels = ranks model.Nodes.Length edges

        let rankCount = 1 + (if levels.Length = 0 then 0 else Array.max levels)

        let slotOf = rows model indices levels edges

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
            |> Array.mapi (fun index level ->
                let slot = slotOf[index]

                let rank = if reversed then rankCount - level - 1 else level

                let column, row = if horizontal then rank, slot else slot, rank

                margin + maxWidth + column * xStep, margin + maxHeight + row * yStep
            )

        let slots = 1 + (if slotOf.Length = 0 then 0 else Array.max slotOf)
        let columns, rows = if horizontal then rankCount, slots else slots, rankCount

        let baseWidth = 2 * margin + (columns - 1) * xStep + 2 * maxWidth

        let baseHeight = 2 * margin + (rows - 1) * yStep + 2 * maxHeight

        let groups = groupBounds model indices positions sizes baseHeight

        let width =
            groups |> Array.fold (fun width (_, box) -> max width (box.Right + 24)) baseWidth

        let height =
            groups |> Array.fold (fun height (_, box) -> max height (box.Bottom + 24)) baseHeight

        let heads =
            model.Edges
            |> Seq.filter (fun edge -> edge.Line <> FlowLine.Invisible)
            |> Seq.collect (fun edge -> [ edge.StartHead, Theme.arrow; edge.EndHead, Theme.arrow ])

        SvgWriting.document
            "flowchart"
            "Flowchart"
            width
            height
            heads
            options
            metadata
            true
            (fun writer ->
                for group, box in groups do
                    SvgWriting.element
                        writer
                        "g"
                        [ "data-ref", (DiagramElementRef.Group group.Id).ToString(); "data-group", group.Id.Value ]
                        (fun () ->
                            SvgWriting.empty
                                writer
                                "rect"
                                [
                                    "class", "outline"
                                    "x", SvgWriting.number box.Left
                                    "y", SvgWriting.number box.Top
                                    "width", SvgWriting.number (box.Right - box.Left)
                                    "height", SvgWriting.number (box.Bottom - box.Top)
                                    "rx", "8"
                                    "fill", Theme.bg
                                    "stroke", Theme.muted
                                    "stroke-width", "1.5"
                                    "stroke-dasharray", "6 4"
                                ]

                            SvgWriting.label
                                writer
                                ((box.Left + box.Right) / 2)
                                (box.Top + 19)
                                13
                                700
                                Theme.text
                                group.Label
                        )

                let edgeCounts = Dictionary<int * int, int>()
                let edgeRefs = ElementRefs.Edges model
                let pairOf (source, target) = min source target, max source target

                let pairTotals =
                    edges |> Array.countBy pairOf |> dict

                let occupied =
                    HashSet<int * int>(Seq.init levels.Length (fun index -> levels[index], slotOf[index]))

                // A link to a node in another row and another rank turns once: it leaves the source from the side
                // that faces the target's row, and enters the target from its rank side. It does so only when no
                // node stands on either leg and no other link joins the same two nodes.
                let turnsOnce source target =
                    let sourceRank, sourceRow = levels[source], slotOf[source]
                    let targetRank, targetRow = levels[target], slotOf[target]
                    let between first last = seq { min first last + 1 .. max first last - 1 }

                    sourceRank <> targetRank
                    && sourceRow <> targetRow
                    && pairTotals[pairOf (source, target)] = 1
                    && not (occupied.Contains((sourceRank, targetRow)))
                    && between sourceRow targetRow |> Seq.forall (fun row -> not (occupied.Contains((sourceRank, row))))
                    && between sourceRank targetRank |> Seq.forall (fun rank -> not (occupied.Contains((rank, targetRow))))

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
                            if source <> target && turnsOnce source target then
                                let signX = if tx > sx then 1 else -1
                                let signY = if ty > sy then 1 else -1

                                if horizontal then
                                    let b = tx - signX * tw

                                    (sx, sy + signY * sh), (sx, ty), (sx, ty), (b, ty), ((sx + b) / 2, ty - 9)
                                else
                                    let b = ty - signY * th

                                    (sx + signX * sw, sy), (tx, sy), (tx, sy), (tx, b), (tx, (sy + b) / 2 + 5)
                            elif source = target then
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

                        SvgWriting.element
                            writer
                            "g"
                            [ "class", "edge"; "data-ref", edgeRefs[index].ToString() ]
                            (fun () ->
                            SvgWriting.connector
                                writer
                                route
                                edge.StartHead
                                edge.EndHead
                                Theme.arrow
                                (if edge.Line = FlowLine.Dotted then Some "5 4" else None)
                                (if edge.Line = FlowLine.Thick then "3" else "1.5")
                                [
                                    "class", "connector"
                                    "data-source", edge.Source.Value
                                    "data-target", edge.Target.Value
                                ]

                            if edge.StartHead <> EdgeHead.None || edge.EndHead <> EdgeHead.None then
                                let reversed = edge.EndHead = EdgeHead.None
                                writer.BindConnector edge.Source edge.Target route reversed

                            match edge.Label with
                            | null -> ()
                            | label ->
                                SvgWriting.chip writer labelX labelY 13 Theme.bg label
                                SvgWriting.label writer labelX labelY 13 400 Theme.text label
                            )

                model.Nodes
                |> Seq.iteri (fun index node ->
                    let x, y = positions[index]
                    let w, h = sizes[index]

                    SvgWriting.element
                        writer
                        "g"
                        [ "data-ref", (DiagramElementRef.Node node.Id).ToString(); "data-node", node.Id.Value; "data-shape", node.Shape.WireValue ]
                        (fun () ->
                            let family = shape writer node x y w h
                            SvgWriting.label writer x (y + 5) 13 400 (Theme.ink family) node.Label
                        )
                )
            )
