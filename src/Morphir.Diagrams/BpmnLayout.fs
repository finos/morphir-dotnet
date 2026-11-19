// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open Morphir.Bpmn

/// A placed BPMN shape: a flow node, data element, annotation, or a container (pool, lane, expanded subprocess,
/// group) drawn behind what it holds.
type internal BpmnBox =
    {
        Id: string
        Element: BpmnElement
        Kind: string
        X: float
        Y: float
        Width: float
        Height: float
        Label: string
        Container: bool
        Expanded: bool
    }

    member this.Right = this.X + this.Width
    member this.Bottom = this.Y + this.Height
    member this.CenterX = this.X + this.Width / 2.0
    member this.CenterY = this.Y + this.Height / 2.0

/// A placed BPMN connection with its waypoints.
type internal BpmnLine =
    {
        Id: string
        Element: BpmnElement
        Kind: string
        Source: string
        Target: string
        Points: (float * float) list
        Label: string
    }

/// The placed view: containers first, then shapes, in paint order.
type internal BpmnScene =
    {
        Boxes: BpmnBox list
        Lines: BpmnLine list
        Width: float
        Height: float
        Supplied: bool
    }

/// Selects a view, reads its diagram interchange geometry or lays it out, and builds the projection.
[<RequireQualifiedAccess>]
module internal BpmnLayout =
    let activities =
        set [ "task"; "userTask"; "serviceTask"; "sendTask"; "receiveTask"; "manualTask"; "businessRuleTask"; "scriptTask"; "callActivity"; "subProcess"; "adHocSubProcess"; "transaction" ]

    let events = set [ "startEvent"; "endEvent"; "intermediateCatchEvent"; "intermediateThrowEvent"; "boundaryEvent"; "event"; "implicitThrowEvent" ]
    let gateways = set [ "exclusiveGateway"; "inclusiveGateway"; "parallelGateway"; "complexGateway"; "eventBasedGateway" ]
    let data = set [ "dataObjectReference"; "dataObject"; "dataStoreReference"; "dataInput"; "dataOutput" ]
    let artifacts = set [ "textAnnotation"; "group" ]
    let flows = set [ "sequenceFlow"; "messageFlow"; "association"; "dataInputAssociation"; "dataOutputAssociation" ]
    let unsupported = set [ "choreographyTask"; "subChoreography"; "callChoreography"; "conversation"; "subConversation"; "callConversation"; "conversationLink" ]

    let isSubProcess kind = kind = "subProcess" || kind = "adHocSubProcess" || kind = "transaction"

    let private idOf (element: BpmnElement) = match element.Id with | null -> "" | value -> value

    let label (element: BpmnElement) =
        match element.Kind with
        | "textAnnotation" -> element.Field "text" |> Option.ofObj |> Option.defaultValue ""
        | _ -> element.Name |> Option.ofObj |> Option.defaultValue ""

    let private resolve (document: BpmnDocument) (value: string) =
        match document.Find value with
        | BpmnLookup.Found element -> Some element
        | BpmnLookup.Missing when value.Contains ':' ->
            match document.Find(value.Substring(value.LastIndexOf ':' + 1)) with
            | BpmnLookup.Found element -> Some element
            | _ -> None
        | _ -> None

    /// The process or collaboration to draw, or the code and message of why there is none.
    let select (document: BpmnDocument) (view: string option) : Result<BpmnElement, string * string> =
        let drawable (element: BpmnElement) =
            match element.Kind with
            | "process" -> Ok element
            | "collaboration" when element.Children |> Seq.exists (fun child -> child.Kind = "conversation" || child.Kind = "conversationLink") ->
                Error("bpmn/unsupported-view", $"{idOf element} is a conversation view, which is not drawn.")
            | "collaboration" -> Ok element
            | "choreography" -> Error("bpmn/unsupported-view", $"{idOf element} is a choreography, which is not drawn.")
            | kind -> Error("bpmn/unsupported-view", $"{idOf element} is a {kind}, not a process or collaboration.")

        let ofPlane (plane: BpmnElement) =
            match plane.Field "bpmnElement" |> Option.ofObj |> Option.bind (resolve document) with
            | Some element -> drawable element
            | None -> Error("bpmn/missing-view", $"The plane {idOf plane} names no element.")

        let tier (name: string) (candidates: ImmutableArray<BpmnElement>) pick next =
            match candidates.Length with
            | 0 -> next ()
            | 1 -> pick candidates[0]
            | _ ->
                let names = candidates |> Seq.map idOf |> String.concat ", "
                Error("bpmn/ambiguous-view", $"The document has several {name} ({names}); select one.")

        match view with
        | Some wanted ->
            match document.Find wanted with
            | BpmnLookup.Found element when element.Kind = "BPMNPlane" -> ofPlane element
            | BpmnLookup.Found element -> drawable element
            | BpmnLookup.Missing -> Error("bpmn/missing-view", $"No element has the ID {wanted}.")
            | BpmnLookup.Ambiguous _ -> Error("bpmn/ambiguous-view", $"Several elements have the ID {wanted}.")
        | None ->
            tier "planes" document.Planes ofPlane (fun () ->
                tier "collaborations" document.Collaborations drawable (fun () ->
                    tier "processes" document.Processes drawable (fun () ->
                        if document.Choreographies.IsEmpty then Error("bpmn/missing-view", "The document has no process or collaboration.")
                        else Error("bpmn/unsupported-view", "Choreography views are not drawn."))))

    /// The shapes and connections of a view, in document order: (shapes, connections), with containers.
    let contents (document: BpmnDocument) (view: BpmnElement) =
        let shapes = ResizeArray<BpmnElement>()
        let lines = ResizeArray<BpmnElement>()
        let problems = ResizeArray<string>()

        let rec walk (container: BpmnElement) =
            for child in container.Children do
                if child.IsModel then
                    match child.Kind with
                    | kind when activities.Contains kind || events.Contains kind || gateways.Contains kind || artifacts.Contains kind ->
                        shapes.Add child
                        if isSubProcess kind then walk child

                        for association in child.Children do
                            if association.Kind = "dataInputAssociation" || association.Kind = "dataOutputAssociation" then lines.Add association
                    | "dataObjectReference"
                    | "dataStoreReference" -> shapes.Add child
                    | "laneSet"
                    | "childLaneSet" -> lanes child
                    | kind when flows.Contains kind -> lines.Add child
                    | kind when unsupported.Contains kind -> problems.Add $"The {kind} {idOf child} cannot be drawn in a process or collaboration view."
                    | _ -> ()

        and lanes (set: BpmnElement) =
            for lane in set.Children do
                if lane.Kind = "lane" then
                    shapes.Add lane

                    for nested in lane.Children do
                        if nested.Kind = "childLaneSet" then lanes nested

        match view.Kind with
        | "collaboration" ->
            for child in view.Children do
                match child.Kind with
                | "participant" ->
                    shapes.Add child

                    match child.Field "processRef" |> Option.ofObj |> Option.bind (resolve document) with
                    | Some proc -> walk proc
                    | None -> ()
                | kind when artifacts.Contains kind -> shapes.Add child
                | kind when flows.Contains kind -> lines.Add child
                | kind when unsupported.Contains kind -> problems.Add $"The {kind} {idOf child} cannot be drawn in a collaboration view."
                | _ -> ()
        | _ -> walk view

        shapes, lines, problems

    /// The ends of a connection: a data association names one end in a child and the other is its activity.
    let ends (document: BpmnDocument) (line: BpmnElement) =
        let first (name: string) = line.References name |> Seq.tryHead |> Option.bind (resolve document)

        match line.Kind with
        | "dataInputAssociation" -> first "sourceRef", Option.ofObj line.Parent
        | "dataOutputAssociation" -> Option.ofObj line.Parent, first "targetRef"
        | _ -> first "sourceRef", first "targetRef"

    let private number (value: string | null) =
        match value with
        | null -> None
        | text ->
            match Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, parsed when Double.IsFinite parsed -> Some parsed
            | _ -> None

    /// Geometry from the view's plane: every shape's bounds and every connection's waypoints, or none when the plane
    /// leaves something out.
    let supplied (document: BpmnDocument) (view: BpmnElement) (shapes: ResizeArray<BpmnElement>) (lines: ResizeArray<BpmnElement>) =
        let plane =
            document.Planes
            |> Seq.tryFind (fun plane ->
                match plane.Field "bpmnElement" |> Option.ofObj |> Option.bind (resolve document) with
                | Some element -> Object.ReferenceEquals(element, view)
                | None -> false)

        match plane with
        | None -> None
        | Some plane ->
            let bounds = Dictionary<string, BpmnElement>(StringComparer.Ordinal)
            let points = Dictionary<string, (float * float) list>(StringComparer.Ordinal)

            for item in plane.Children do
                match item.Field "bpmnElement" with
                | null -> ()
                | target ->
                    match item.Kind with
                    | "BPMNShape" -> item.Children |> Seq.tryFind (fun child -> child.Kind = "Bounds") |> Option.iter (fun found -> bounds[target] <- found)
                    | "BPMNEdge" ->
                        points[target] <-
                            [
                                for point in item.Children do
                                    if point.Kind = "waypoint" then
                                        match number (point.Field "x"), number (point.Field "y") with
                                        | Some x, Some y -> x, y
                                        | _ -> ()
                            ]
                    | _ -> ()

            let box (element: BpmnElement) =
                match bounds.TryGetValue(idOf element) with
                | true, found ->
                    match number (found.Field "x"), number (found.Field "y"), number (found.Field "width"), number (found.Field "height") with
                    | Some x, Some y, Some width, Some height -> Some(element, x, y, width, height)
                    | _ -> None
                | false, _ -> None

            let placed = shapes |> Seq.map box |> Seq.toList
            let routed = lines |> Seq.map (fun line -> match points.TryGetValue(idOf line) with | true, found when found.Length >= 2 -> Some(line, found) | _ -> None) |> Seq.toList

            if placed |> List.forall Option.isSome && routed |> List.forall Option.isSome then
                let expanded (element: BpmnElement) =
                    plane.Children
                    |> Seq.exists (fun item -> item.Kind = "BPMNShape" && item.Field "bpmnElement" = element.Id && item.Field "isExpanded" = "true")

                Some(List.choose (fun found -> found) placed, List.choose (fun found -> found) routed, expanded)
            else
                None

    // Automatic layout ------------------------------------------------------------------------------------------------

    let size (kind: string) =
        if events.Contains kind then 36.0, 36.0
        elif gateways.Contains kind then 50.0, 50.0
        elif kind = "dataStoreReference" then 50.0, 50.0
        elif data.Contains kind then 36.0, 50.0
        elif kind = "textAnnotation" then 110.0, 44.0
        else 100.0, 80.0

    type private Block =
        {
            Boxes: BpmnBox list
            Width: float
            Height: float
        }

    let private shift dx dy (box: BpmnBox) = { box with X = box.X + dx; Y = box.Y + dy }

    /// Lays out one process or expanded subprocess: columns by sequence-flow rank, rows within each lane band, the
    /// rows of each band aligned on their centres, then data and annotations in a row below.
    let rec private block (document: BpmnDocument) (container: BpmnElement) (laneOf: Dictionary<string, BpmnBox option>) (lanes: BpmnElement list) : Block =
        let members = container.Children |> Seq.filter (fun child -> child.IsModel) |> Seq.toList
        let nodes = members |> List.filter (fun child -> (activities.Contains child.Kind || events.Contains child.Kind || gateways.Contains child.Kind) && child.Kind <> "boundaryEvent")
        let attached = members |> List.filter (fun child -> child.Kind = "boundaryEvent")
        let extras = members |> List.filter (fun child -> child.Kind = "dataObjectReference" || child.Kind = "dataStoreReference" || artifacts.Contains child.Kind)
        let index = Dictionary<string, int>(StringComparer.Ordinal)
        nodes |> List.iteri (fun position node -> index[idOf node] <- position)

        let hostOf =
            attached
            |> List.choose (fun event -> match event.Field "attachedToRef" with | null -> None | host -> Some(idOf event, host))
            |> dict

        // Sequence flows between nodes of this container; a flow from a boundary event leaves its host.
        let edges =
            members
            |> List.filter (fun child -> child.Kind = "sequenceFlow")
            |> List.choose (fun flow ->
                let source = flow.Field "sourceRef" |> Option.ofObj |> Option.map (fun value -> match hostOf.TryGetValue value with | true, host -> host | _ -> value)
                let target = flow.Field "targetRef" |> Option.ofObj

                match source, target with
                | Some source, Some target when index.ContainsKey source && index.ContainsKey target && source <> target -> Some(index[source], index[target])
                | _ -> None)

        // Back edges are those that close a cycle in a depth-first walk in document order.
        let state = Array.zeroCreate nodes.Length
        let back = HashSet<int * int>()
        let outgoing = edges |> List.groupBy fst |> dict

        for start in 0 .. nodes.Length - 1 do
            if state[start] = 0 then
                let stack = Stack<int * int list>()
                state[start] <- 1
                stack.Push(start, (match outgoing.TryGetValue start with | true, found -> List.map snd found | _ -> []))

                while stack.Count > 0 do
                    let node, rest = stack.Pop()

                    match rest with
                    | [] -> state[node] <- 2
                    | next :: rest ->
                        stack.Push(node, rest)

                        match state[next] with
                        | 1 -> back.Add((node, next)) |> ignore
                        | 0 ->
                            state[next] <- 1
                            stack.Push(next, (match outgoing.TryGetValue next with | true, found -> List.map snd found | _ -> []))
                        | _ -> ()

        let forward = edges |> List.filter (back.Contains >> not)
        let rank = Array.zeroCreate nodes.Length
        let mutable changed = true
        let mutable rounds = 0

        while changed && rounds <= nodes.Length do
            changed <- false
            rounds <- rounds + 1

            for source, target in forward do
                if rank[target] < rank[source] + 1 then
                    rank[target] <- rank[source] + 1
                    changed <- true

        // Nodes with no flow at all fill columns of four after the connected ones, so they do not make one tall column.
        let linked = HashSet<int>(edges |> Seq.collect (fun (source, target) -> [ source; target ]))
        let after = if linked.Count = 0 then 0 else (linked |> Seq.map (fun position -> rank[position]) |> Seq.max) + 1
        let mutable isolated = 0

        for position in 0 .. nodes.Length - 1 do
            if not (linked.Contains position) then
                rank[position] <- after + isolated / 4
                isolated <- isolated + 1

        // Expanded subprocesses are blocks of their own.
        let inner = Dictionary<string, Block>(StringComparer.Ordinal)

        for node in nodes do
            if isSubProcess node.Kind && node.Children |> Seq.exists (fun child -> activities.Contains child.Kind || events.Contains child.Kind || gateways.Contains child.Kind) then
                inner[idOf node] <- block document node (Dictionary()) []

        let sizeOf (node: BpmnElement) =
            match inner.TryGetValue(idOf node) with
            | true, found -> found.Width + 40.0, found.Height + 50.0
            | false, _ -> size node.Kind

        let ranks = if nodes.IsEmpty then 0 else Array.max rank + 1
        let columnWidth = Array.init ranks (fun column -> nodes |> List.mapi (fun position node -> position, node) |> List.filter (fun (position, _) -> rank[position] = column) |> List.fold (fun widest (_, node) -> max widest (fst (sizeOf node))) 0.0)
        let columnX = Array.zeroCreate ranks
        let mutable cursor = 30.0

        for column in 0 .. ranks - 1 do
            columnX[column] <- cursor
            cursor <- cursor + columnWidth[column] + 60.0

        let width = max 200.0 cursor

        // Bands: one per lane, or one for the whole container.
        let bands = if lanes.IsEmpty then [ None ] else lanes |> List.map Some
        let bandOf (node: BpmnElement) = if lanes.IsEmpty then None else (match laneOf.TryGetValue(idOf node) with | true, found -> found |> Option.map (fun box -> box.Element) | _ -> None)
        let boxes = ResizeArray<BpmnBox>()
        let mutable top = 0.0
        let bandTops = ResizeArray<BpmnElement option * float * float>()

        for band in bands do
            let inBand = nodes |> List.mapi (fun position node -> position, node) |> List.filter (fun (_, node) -> (match band, bandOf node with | None, _ when lanes.IsEmpty -> true | Some lane, Some found -> Object.ReferenceEquals(lane, found) | Some lane, None -> Object.ReferenceEquals(lane, List.head lanes) | _ -> false))
            let rowOf = Dictionary<int, int>()
            let perColumn = Dictionary<int, int>()

            for position, _ in inBand do
                let column = rank[position]
                let row = match perColumn.TryGetValue column with | true, count -> count | _ -> 0
                perColumn[column] <- row + 1
                rowOf[position] <- row

            let rows = if inBand.IsEmpty then 1 else (Seq.max rowOf.Values + 1)
            let rowHeight = Array.zeroCreate rows

            for position, node in inBand do
                rowHeight[rowOf[position]] <- max rowHeight[rowOf[position]] (snd (sizeOf node))

            let rowCentre = Array.zeroCreate rows
            let mutable y = top + 30.0

            for row in 0 .. rows - 1 do
                let height = max 50.0 rowHeight[row]
                rowCentre[row] <- y + height / 2.0
                y <- y + height + 40.0

            for position, node in inBand do
                let width, height = sizeOf node
                let x = columnX[rank[position]] + (columnWidth[rank[position]] - width) / 2.0
                let y = rowCentre[rowOf[position]] - height / 2.0

                boxes.Add
                    {
                        Id = idOf node
                        Element = node
                        Kind = node.Kind
                        X = x
                        Y = y
                        Width = width
                        Height = height
                        Label = label node
                        Container = inner.ContainsKey(idOf node)
                        Expanded = inner.ContainsKey(idOf node)
                    }

                match inner.TryGetValue(idOf node) with
                | true, found -> for child in found.Boxes do boxes.Add(shift (x + 20.0) (y + 30.0) child)
                | false, _ -> ()

            let bottom = y - 10.0
            bandTops.Add(band, top, bottom - top)
            top <- bottom

        // Boundary events sit on the bottom border of their host, from its right end.
        let placed = boxes |> Seq.map (fun box -> box.Id, box) |> Seq.toList |> dict

        attached
        |> List.groupBy (fun event -> event.Field "attachedToRef" |> Option.ofObj |> Option.defaultValue "")
        |> List.iter (fun (host, events) ->
            match placed.TryGetValue host with
            | true, hostBox ->
                events
                |> List.iteri (fun position event ->
                    boxes.Add
                        {
                            Id = idOf event
                            Element = event
                            Kind = event.Kind
                            X = hostBox.Right - 46.0 - 44.0 * float position
                            Y = hostBox.Bottom - 18.0
                            Width = 36.0
                            Height = 36.0
                            Label = label event
                            Container = false
                            Expanded = false
                        })
            | false, _ -> ())

        // Data and annotations: a row below, each under the first element it is associated with where there is one.
        let mutable extraX = 30.0
        let extraY = top + 20.0
        let mutable extraHeight = 0.0

        for extra in extras do
            let width, height = size extra.Kind

            if extra.Kind = "group" then
                ()
            else
                boxes.Add
                    {
                        Id = idOf extra
                        Element = extra
                        Kind = extra.Kind
                        X = extraX
                        Y = extraY
                        Width = width
                        Height = height
                        Label = label extra
                        Container = false
                        Expanded = false
                    }

                extraX <- extraX + width + 40.0
                extraHeight <- max extraHeight (height + 40.0)

        // Lane bands become containers behind the nodes.
        let laneBoxes =
            [
                for band, bandTop, bandHeight in bandTops do
                    match band with
                    | Some lane ->
                        {
                            Id = idOf lane
                            Element = lane
                            Kind = "lane"
                            X = 0.0
                            Y = bandTop
                            Width = width
                            Height = bandHeight + (if Object.ReferenceEquals(lane, List.last lanes) then extraHeight else 0.0)
                            Label = label lane
                            Container = true
                            Expanded = true
                        }
                    | None -> ()
            ]

        {
            Boxes = laneBoxes @ List.ofSeq boxes
            Width = max width extraX
            Height = top + extraHeight + 10.0
        }

    /// The leaf lanes of a process in order, and for each node the lane that holds it.
    let private laneTable (proc: BpmnElement) =
        let leaves = ResizeArray<BpmnElement>()

        let rec collect (set: BpmnElement) =
            for lane in set.Children do
                if lane.Kind = "lane" then
                    match lane.Children |> Seq.tryFind (fun child -> child.Kind = "childLaneSet") with
                    | Some nested -> collect nested
                    | None -> leaves.Add lane

        for child in proc.Children do
            if child.Kind = "laneSet" then collect child

        let owner = Dictionary<string, BpmnBox option>(StringComparer.Ordinal)

        for lane in leaves do
            for reference in lane.References "flowNodeRef" do
                owner[reference] <-
                    Some
                        {
                            Id = idOf lane
                            Element = lane
                            Kind = "lane"
                            X = 0.0
                            Y = 0.0
                            Width = 0.0
                            Height = 0.0
                            Label = ""
                            Container = true
                            Expanded = true
                        }

        List.ofSeq leaves, owner

    /// Routes connections between placed boxes: sequence flows leave right and enter left, back flows run below,
    /// message flows run vertically between pools, and associations are straight.
    let route (document: BpmnDocument) (boxes: IDictionary<string, BpmnBox>) (line: BpmnElement) =
        match ends document line with
        | Some source, Some target ->
            match boxes.TryGetValue(idOf source), boxes.TryGetValue(idOf target) with
            | (true, a), (true, b) ->
                let points =
                    match line.Kind with
                    | "sequenceFlow" when a.Kind = "boundaryEvent" ->
                        [ a.CenterX, a.Bottom; a.CenterX, max (a.Bottom + 20.0) b.CenterY; b.X, max (a.Bottom + 20.0) b.CenterY ] @ (if b.CenterY < a.Bottom + 20.0 then [ b.X, b.CenterY ] else [])
                    | "sequenceFlow" when b.X >= a.Right ->
                        if abs (a.CenterY - b.CenterY) < 0.5 then
                            [ a.Right, a.CenterY; b.X, b.CenterY ]
                        else
                            let middle = (a.Right + b.X) / 2.0
                            [ a.Right, a.CenterY; middle, a.CenterY; middle, b.CenterY; b.X, b.CenterY ]
                    | "sequenceFlow" ->
                        let below = max a.Bottom b.Bottom + 24.0
                        [ a.CenterX, a.Bottom; a.CenterX, below; b.CenterX, below; b.CenterX, b.Bottom ]
                    | "messageFlow" ->
                        let down = b.CenterY > a.CenterY
                        let fromY, toY = (if down then a.Bottom, b.Y else a.Y, b.Bottom)
                        let middle = (fromY + toY) / 2.0
                        [ a.CenterX, fromY; a.CenterX, middle; b.CenterX, middle; b.CenterX, toY ]
                    | _ ->
                        // A straight line between the borders, along the line between the centres.
                        let clip (box: BpmnBox) (towardX: float) (towardY: float) =
                            let dx, dy = towardX - box.CenterX, towardY - box.CenterY
                            if dx = 0.0 && dy = 0.0 then box.CenterX, box.CenterY
                            else
                                let scale = min (if dx = 0.0 then infinity else box.Width / 2.0 / abs dx) (if dy = 0.0 then infinity else box.Height / 2.0 / abs dy)
                                box.CenterX + dx * scale, box.CenterY + dy * scale

                        [ clip a b.CenterX b.CenterY; clip b a.CenterX a.CenterY ]

                Some
                    {
                        Id = idOf line
                        Element = line
                        Kind = line.Kind
                        Source = idOf source
                        Target = idOf target
                        Points = points
                        Label = label line
                    }
            | _ -> None
        | _ -> None

    /// The automatic layout of a view: a process alone, or a collaboration's pools stacked with their processes.
    let automatic (document: BpmnDocument) (view: BpmnElement) (lines: ResizeArray<BpmnElement>) =
        let laid (proc: BpmnElement) =
            let lanes, owner = laneTable proc
            block document proc owner lanes

        let boxes =
            match view.Kind with
            | "collaboration" ->
                let pools =
                    [
                        for participant in view.Children do
                            if participant.Kind = "participant" then
                                participant, participant.Field "processRef" |> Option.ofObj |> Option.bind (resolve document) |> Option.map laid
                    ]

                let width = pools |> List.fold (fun widest (_, inner) -> match inner with | Some block -> max widest (block.Width + 30.0) | None -> widest) 400.0
                let mutable top = 20.0

                [
                    for participant, inner in pools do
                        let height = match inner with | Some block -> max 80.0 block.Height | None -> 60.0

                        yield
                            {
                            Id = idOf participant
                            Element = participant
                            Kind = "participant"
                            X = 20.0
                            Y = top
                            Width = width
                            Height = height
                            Label = label participant
                            Container = true
                            Expanded = inner.IsSome
                        }

                        match inner with
                        | Some block ->
                            for box in block.Boxes do
                                // Lanes fill the pool beside its name band.
                                if box.Kind = "lane" then yield { shift 50.0 top box with Width = width - 30.0 }
                                else yield shift 50.0 top box
                        | None -> ()

                        top <- top + height + 40.0
                ]
            | _ ->
                let inner = laid view
                [ for box in inner.Boxes -> if box.Kind = "lane" then { shift 20.0 20.0 box with Width = inner.Width + 30.0 } else shift 50.0 20.0 box ]

        // Containers paint first.
        let ordered = (boxes |> List.filter (fun box -> box.Container)) @ (boxes |> List.filter (fun box -> not box.Container))
        let byId = Dictionary<string, BpmnBox>(StringComparer.Ordinal)
        for box in ordered do byId.TryAdd(box.Id, box) |> ignore
        let routed = lines |> Seq.choose (route document byId) |> Seq.toList
        let right = ordered |> List.fold (fun widest box -> max widest box.Right) 0.0
        let bottom = (ordered |> List.fold (fun lowest box -> max lowest box.Bottom) 0.0, routed) ||> List.fold (fun lowest line -> line.Points |> List.fold (fun low (_, y) -> max low y) lowest)

        {
            Boxes = ordered
            Lines = routed
            Width = right + 30.0
            Height = bottom + 30.0
            Supplied = false
        }

    /// The projection: flow nodes, data and annotations are nodes; pools, lanes and expanded subprocesses are groups;
    /// connections are edges with BPMN line styles.
    let projection (scene: BpmnScene) =
        let span = SourceSpan(SourcePosition(0, 1, 1), SourcePosition(0, 1, 1))

        let containerOf (box: BpmnBox) =
            scene.Boxes
            |> List.filter (fun other -> other.Container && other.Id <> box.Id && other.X <= box.X && other.Y <= box.Y && other.Right >= box.Right && other.Bottom >= box.Bottom)
            |> List.sortBy (fun other -> other.Width * other.Height)
            |> List.tryHead

        let nodes =
            scene.Boxes
            |> List.filter (fun box -> not box.Container && box.Id <> "")
            |> List.map (fun box ->
                let shape =
                    if events.Contains box.Kind then FlowShape.Circle
                    elif gateways.Contains box.Kind then FlowShape.Diamond
                    elif box.Kind = "dataStoreReference" then FlowShape.Cylinder
                    elif data.Contains box.Kind then FlowShape.Document
                    elif box.Kind = "textAnnotation" then FlowShape.Note
                    else FlowShape.Rounded

                FlowNode(DiagramId.Create box.Id, box.Label, shape, span))

        let groups =
            scene.Boxes
            |> List.filter (fun box -> box.Container && box.Id <> "")
            |> List.map (fun box ->
                let members =
                    scene.Boxes
                    |> List.filter (fun inner -> not inner.Container && inner.Id <> "" && (containerOf inner |> Option.map (fun found -> found.Id)) = Some box.Id)
                    |> List.map (fun inner -> DiagramId.Create inner.Id)

                FlowGroup(DiagramId.Create box.Id, box.Label, containerOf box |> Option.map (fun parent -> DiagramId.Create parent.Id), ImmutableArray.CreateRange members, span))

        let known = HashSet<string>(scene.Boxes |> Seq.map (fun box -> box.Id), StringComparer.Ordinal)

        let edges =
            scene.Lines
            |> List.filter (fun line -> known.Contains line.Source && known.Contains line.Target)
            |> List.map (fun line ->
                let style, start, finish =
                    match line.Kind with
                    | "sequenceFlow" -> FlowLine.Solid, EdgeHead.None, EdgeHead.Arrow
                    | "messageFlow" -> FlowLine.Dotted, EdgeHead.Circle, EdgeHead.OpenArrow
                    | "association" when line.Element.Field "associationDirection" = "Both" -> FlowLine.Dotted, EdgeHead.OpenArrow, EdgeHead.OpenArrow
                    | "association" when line.Element.Field "associationDirection" = "One" -> FlowLine.Dotted, EdgeHead.None, EdgeHead.OpenArrow
                    | "association" -> FlowLine.Dotted, EdgeHead.None, EdgeHead.None
                    | _ -> FlowLine.Dotted, EdgeHead.None, EdgeHead.OpenArrow

                FlowEdge(DiagramId.Create line.Source, DiagramId.Create line.Target, (if line.Label = "" then None else Some line.Label), style, start, finish, span))

        Flowchart(FlowDirection.LeftToRight, ImmutableArray.CreateRange nodes, ImmutableArray.CreateRange edges, ImmutableArray.CreateRange groups)
