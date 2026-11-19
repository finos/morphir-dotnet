// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Globalization
open Morphir.Bpmn

/// Paints a placed BPMN view with BPMN 2.0.2 notation in the library's themes.
[<RequireQualifiedAccess>]
module internal BpmnSvg =
    let private f (value: float) = value.ToString("0.##", CultureInfo.InvariantCulture)
    let private pt (x: float) (y: float) = f x + "," + f y

    /// The event definitions of an event: its own definitions and those it names by reference.
    let private definitions (document: BpmnDocument) (event: BpmnElement) =
        [
            for child in event.Children do
                if child.Kind.EndsWith("EventDefinition", StringComparison.Ordinal) then child.Kind.Replace("EventDefinition", "")
                elif child.Kind = "eventDefinitionRef" then
                    match document.Find(child.Text.Trim()) with
                    | BpmnLookup.Found definition -> definition.Kind.Replace("EventDefinition", "")
                    | _ -> ()
        ]

    let private words (text: string) width =
        let lines = ResizeArray<string>()
        let mutable line = ""

        for word in text.Split([| ' '; '\n' |], StringSplitOptions.RemoveEmptyEntries) do
            if line = "" then line <- word
            elif line.Length + 1 + word.Length <= width then line <- line + " " + word
            else
                lines.Add line
                line <- word

        if line <> "" then lines.Add line
        List.ofSeq lines

    let render (options: SvgRenderOptions) (metadata: Morphir.Svg.SvgNode list option) (model: BpmnDiagram) =
        let scene = model.Scene
        let document = model.Document
        let projection = model.Projection
        let edgeRefs = ElementRefs.Edges projection
        let arrow = Theme.arrow

        let heads =
            [ EdgeHead.Arrow, arrow; EdgeHead.OpenArrow, arrow ]

        SvgWriting.document
            "bpmn"
            (model.ToString())
            (int (ceil scene.Width))
            (int (ceil scene.Height))
            heads
            options
            metadata
            false
            (fun writer ->
                let path (data: string) (attributes: (string * string) list) = SvgWriting.path writer data attributes

                let text x y (size: int) (weight: int) fill (anchor: string) (lines: string list) =
                    if not lines.IsEmpty then
                        let step = float size + 3.0
                        let first = y - step * float (lines.Length - 1) / 2.0

                        SvgWriting.element
                            writer
                            "text"
                            [ "x", f x; "y", f first; "text-anchor", anchor; "font-family", Theme.fontStack; "font-size", string size; "font-weight", string weight; "fill", fill ]
                            (fun () ->
                                lines
                                |> List.iteri (fun index line ->
                                    SvgWriting.element
                                        writer
                                        "tspan"
                                        [ "x", f x; "dy", (if index = 0 then "0" else f step) ]
                                        (fun () -> writer.Add(Morphir.Svg.SvgNode.CreateText line))))

                let vertical x y (label: string) =
                    SvgWriting.element
                        writer
                        "text"
                        [ "x", f x; "y", f y; "transform", $"rotate(-90 {pt x y})"; "text-anchor", "middle"; "font-family", Theme.fontStack; "font-size", "12"; "font-weight", "600"; "fill", Theme.text ]
                        (fun () -> writer.Add(Morphir.Svg.SvgNode.CreateText label))

                let reference (box: BpmnBox) =
                    if box.Container then (DiagramElementRef.Group(DiagramId.Create box.Id)).ToString() else (DiagramElementRef.Node(DiagramId.Create box.Id)).ToString()

                let group (box: BpmnBox) (body: unit -> unit) =
                    SvgWriting.element
                        writer
                        "g"
                        [
                            "class", $"bpmn-{box.Kind}"
                            if box.Id <> "" then "data-ref", reference box
                            "data-kind", box.Kind
                        ]
                        body

                // Containers ---------------------------------------------------------------------------------------
                for box in scene.Boxes |> List.filter (fun box -> box.Container) do
                    group box (fun () ->
                        match box.Kind with
                        | "participant" ->
                            SvgWriting.empty writer "rect" [ "class", "outline"; "x", f box.X; "y", f box.Y; "width", f box.Width; "height", f box.Height; "fill", "none"; "stroke", Theme.stroke ColorFamily.External; "stroke-width", "1.5" ]

                            if box.Expanded then
                                path $"M{pt (box.X + 30.0) box.Y} V{f box.Bottom}" [ "fill", "none"; "stroke", Theme.stroke ColorFamily.External; "stroke-width", "1.5" ]
                                vertical (box.X + 19.0) box.CenterY box.Label
                            else
                                text box.CenterX (box.CenterY + 4.0) 12 600 Theme.text "middle" [ box.Label ]
                        | "lane" ->
                            SvgWriting.empty writer "rect" [ "class", "outline"; "x", f box.X; "y", f box.Y; "width", f box.Width; "height", f box.Height; "fill", "none"; "stroke", Theme.frame; "stroke-width", "1" ]
                            path $"M{pt (box.X + 30.0) box.Y} V{f box.Bottom}" [ "fill", "none"; "stroke", Theme.frame; "stroke-width", "1" ]
                            vertical (box.X + 19.0) box.CenterY box.Label
                        | "group" ->
                            SvgWriting.empty writer "rect" [ "class", "outline"; "x", f box.X; "y", f box.Y; "width", f box.Width; "height", f box.Height; "rx", "10"; "fill", "none"; "stroke", Theme.muted; "stroke-width", "1.5"; "stroke-dasharray", "8 4 2 4" ]
                            text (box.X + 8.0) (box.Y + 16.0) 11 600 Theme.muted "start" [ box.Label ]
                        | _ ->
                            let triggered = box.Element.Field "triggeredByEvent" = "true"
                            let stroke = Theme.stroke ColorFamily.Frontend

                            SvgWriting.empty
                                writer
                                "rect"
                                [
                                    "class", "outline"
                                    "x", f box.X
                                    "y", f box.Y
                                    "width", f box.Width
                                    "height", f box.Height
                                    "rx", "12"
                                    "fill", "none"
                                    "stroke", stroke
                                    "stroke-width", "1.5"
                                    if triggered then "stroke-dasharray", "2 3"
                                ]

                            if box.Kind = "transaction" then
                                SvgWriting.empty writer "rect" [ "x", f (box.X + 4.0); "y", f (box.Y + 4.0); "width", f (box.Width - 8.0); "height", f (box.Height - 8.0); "rx", "9"; "fill", "none"; "stroke", stroke; "stroke-width", "1" ]

                            text (box.X + 12.0) (box.Y + 18.0) 12 600 Theme.text "start" [ box.Label ]

                            if box.Kind = "adHocSubProcess" then
                                text box.CenterX (box.Bottom - 8.0) 14 700 stroke "middle" [ "~" ])

                // Connections ----------------------------------------------------------------------------------------
                let known = HashSet<string>(scene.Boxes |> Seq.map (fun box -> box.Id), StringComparer.Ordinal)
                let boxes = Dictionary<string, BpmnBox>(StringComparer.Ordinal)
                for box in scene.Boxes do boxes.TryAdd(box.Id, box) |> ignore
                let mutable ordinal = 0

                for line in scene.Lines do
                    if known.Contains line.Source && known.Contains line.Target && line.Points.Length >= 2 then
                        let data = "M" + (line.Points |> List.map (fun (x, y) -> pt x y) |> String.concat " L")
                        let edge = edgeRefs[ordinal]
                        ordinal <- ordinal + 1

                        SvgWriting.element
                            writer
                            "g"
                            [ "class", $"edge bpmn-{line.Kind}"; "data-ref", edge.ToString(); "data-kind", line.Kind ]
                            (fun () ->
                                let first = line.Points.Head
                                let second = line.Points.Tail.Head

                                match line.Kind with
                                | "sequenceFlow" ->
                                    SvgWriting.connector writer data EdgeHead.None EdgeHead.Arrow arrow None "1.5" []
                                    let x1, y1 = first
                                    let x2, y2 = second
                                    let length = sqrt ((x2 - x1) ** 2.0 + (y2 - y1) ** 2.0)
                                    let ux, uy = (if length = 0.0 then 1.0, 0.0 else (x2 - x1) / length, (y2 - y1) / length)
                                    let source = match boxes.TryGetValue line.Source with | true, box -> Some box | _ -> None

                                    // A default flow has a slash near its start; a conditional flow from an activity a diamond.
                                    if source |> Option.exists (fun box -> box.Element.Field "default" = line.Id) then
                                        let cx, cy = x1 + ux * 10.0, y1 + uy * 10.0
                                        path $"M{pt (cx - uy * 6.0 - ux * 3.0) (cy + ux * 6.0 - uy * 3.0)} L{pt (cx + uy * 6.0 + ux * 3.0) (cy - ux * 6.0 + uy * 3.0)}" [ "fill", "none"; "stroke", arrow; "stroke-width", "1.5" ]
                                    elif line.Element.Children |> Seq.exists (fun child -> child.Kind = "conditionExpression") && source |> Option.exists (fun box -> BpmnLayout.activities.Contains box.Kind) then
                                        let tip x y = pt (x1 + ux * x - uy * y) (y1 + uy * x + ux * y)
                                        path $"M{tip 0.0 0.0} L{tip 8.0 5.0} L{tip 16.0 0.0} L{tip 8.0 -5.0} Z" [ "fill", Theme.bg; "stroke", arrow; "stroke-width", "1.2" ]
                                | "messageFlow" ->
                                    SvgWriting.connector writer data EdgeHead.None EdgeHead.OpenArrow arrow (Some "6 4") "1.5" []
                                    SvgWriting.empty writer "circle" [ "cx", f (fst first); "cy", f (snd first); "r", "4"; "fill", Theme.bg; "stroke", arrow; "stroke-width", "1.2" ]
                                | "association" ->
                                    let direction = line.Element.Field "associationDirection"
                                    let start = if direction = "Both" then EdgeHead.OpenArrow else EdgeHead.None
                                    let finish = if direction = "Both" || direction = "One" then EdgeHead.OpenArrow else EdgeHead.None
                                    SvgWriting.connector writer data start finish arrow (Some "2 3") "1.2" []
                                | _ -> SvgWriting.connector writer data EdgeHead.None EdgeHead.OpenArrow arrow (Some "2 3") "1.2" []

                                if line.Label <> "" then
                                    let (ax, ay), (bx, by) = line.Points |> List.pairwise |> List.maxBy (fun ((ax, ay), (bx, by)) -> abs (bx - ax) + abs (by - ay))
                                    let mx, my = (ax + bx) / 2.0, (ay + by) / 2.0
                                    SvgWriting.chip writer (int mx) (int my - 4) 11 Theme.mask line.Label
                                    text mx (my - 4.0) 11 500 Theme.text "middle" [ line.Label ])

                        writer.BindConnector (DiagramId.Create line.Source) (DiagramId.Create line.Target) data false

                // Shapes ---------------------------------------------------------------------------------------------
                for box in scene.Boxes |> List.filter (fun box -> not box.Container) do
                    group box (fun () ->
                        let kind = box.Kind

                        if BpmnLayout.events.Contains kind then
                            let defs = definitions document box.Element
                            let throwing = kind = "endEvent" || kind = "intermediateThrowEvent" || kind = "event" || kind = "implicitThrowEvent"

                            let family =
                                match kind with
                                | "startEvent" -> ColorFamily.Backend
                                | "endEvent" -> ColorFamily.Security
                                | _ -> ColorFamily.Cloud

                            let stroke = Theme.stroke family
                            let r = min box.Width box.Height / 2.0
                            let cx, cy = box.CenterX, box.CenterY
                            let interrupting = not (box.Element.Field "isInterrupting" = "false" || box.Element.Field "cancelActivity" = "false")
                            let dash = if interrupting then [] else [ "stroke-dasharray", "4 3" ]
                            let width = if kind = "endEvent" then "3.5" else "1.5"
                            SvgWriting.empty writer "circle" ([ "class", "box"; "cx", f cx; "cy", f cy; "r", f r; "fill", Theme.fill family; "stroke", stroke; "stroke-width", width ] @ dash)

                            if kind <> "startEvent" && kind <> "endEvent" then
                                SvgWriting.empty writer "circle" ([ "cx", f cx; "cy", f cy; "r", f (r - 3.5); "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ] @ dash)

                            let s = r / 18.0
                            let p x y = pt (cx + x * s) (cy + y * s)
                            let fill = if throwing then stroke else "none"
                            let icon data = path data [ "fill", fill; "stroke", stroke; "stroke-width", "1.4"; "stroke-linejoin", "round" ]

                            let definition =
                                match defs with
                                | [] -> None
                                | [ single ] -> Some single
                                | _ when box.Element.Field "parallelMultiple" = "true" -> Some "parallelMultiple"
                                | _ -> Some "multiple"

                            match definition with
                            | Some "message" ->
                                icon $"M{p -8.0 -5.5} H{f (cx + 8.0 * s)} V{f (cy + 5.5 * s)} H{f (cx - 8.0 * s)} Z"
                                path $"M{p -8.0 -5.5} L{p 0.0 1.0} L{p 8.0 -5.5}" [ "fill", "none"; "stroke", (if throwing then Theme.bg else stroke); "stroke-width", "1.2" ]
                            | Some "timer" ->
                                SvgWriting.empty writer "circle" [ "cx", f cx; "cy", f cy; "r", f (9.0 * s); "fill", Theme.bg; "stroke", stroke; "stroke-width", "1.4" ]
                                path $"M{p 0.0 -6.0} L{p 0.0 0.0} L{p 4.0 2.5}" [ "fill", "none"; "stroke", stroke; "stroke-width", "1.4"; "stroke-linecap", "round" ]
                            | Some "signal" -> icon $"M{p 0.0 -8.0} L{p 8.0 6.0} L{p -8.0 6.0} Z"
                            | Some "error" -> icon $"M{p -7.0 7.0} L{p -3.0 -7.0} L{p 2.0 1.0} L{p 7.0 -7.0} L{p 3.0 7.0} L{p -2.0 -1.0} Z"
                            | Some "escalation" -> icon $"M{p 0.0 -8.0} L{p 6.0 7.0} L{p 0.0 2.0} L{p -6.0 7.0} Z"
                            | Some "compensate" -> icon $"M{p -8.0 0.0} L{p -1.0 -6.0} V{f (cy + 6.0 * s)} Z M{p -1.0 0.0} L{p 6.0 -6.0} V{f (cy + 6.0 * s)} Z"
                            | Some "conditional" ->
                                path $"M{p -6.0 -8.0} H{f (cx + 6.0 * s)} V{f (cy + 8.0 * s)} H{f (cx - 6.0 * s)} Z M{p -4.0 -4.0} H{f (cx + 4.0 * s)} M{p -4.0 0.0} H{f (cx + 4.0 * s)} M{p -4.0 4.0} H{f (cx + 4.0 * s)}" [ "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]
                            | Some "link" -> icon $"M{p -8.0 -3.0} H{f (cx + 2.0 * s)} V{f (cy - 7.0 * s)} L{p 9.0 0.0} L{p 2.0 7.0} V{f (cy + 3.0 * s)} H{f (cx - 8.0 * s)} Z"
                            | Some "cancel" -> path $"M{p -6.0 -6.0} L{p 6.0 6.0} M{p 6.0 -6.0} L{p -6.0 6.0}" [ "fill", "none"; "stroke", stroke; "stroke-width", f (3.0 * s); "stroke-linecap", "round" ]
                            | Some "terminate" -> SvgWriting.empty writer "circle" [ "cx", f cx; "cy", f cy; "r", f (9.0 * s); "fill", stroke ]
                            | Some "multiple" -> icon $"M{p 0.0 -8.0} L{p 8.0 -2.0} L{p 5.0 7.0} L{p -5.0 7.0} L{p -8.0 -2.0} Z"
                            | Some "parallelMultiple" -> path $"M{p -2.5 -8.0} H{f (cx + 2.5 * s)} V{f (cy - 2.5 * s)} H{f (cx + 8.0 * s)} V{f (cy + 2.5 * s)} H{f (cx + 2.5 * s)} V{f (cy + 8.0 * s)} H{f (cx - 2.5 * s)} V{f (cy + 2.5 * s)} H{f (cx - 8.0 * s)} V{f (cy - 2.5 * s)} H{f (cx - 2.5 * s)} Z" [ "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]
                            | _ -> ()

                            text cx (box.Bottom + 15.0) 11 500 Theme.text "middle" (words box.Label 18)
                        elif BpmnLayout.gateways.Contains kind then
                            let stroke = Theme.stroke ColorFamily.MessageBus
                            let cx, cy = box.CenterX, box.CenterY
                            let hw, hh = box.Width / 2.0, box.Height / 2.0
                            path $"M{pt cx box.Y} L{pt box.Right cy} L{pt cx box.Bottom} L{pt box.X cy} Z" [ "class", "box"; "fill", Theme.fill ColorFamily.MessageBus; "stroke", stroke; "stroke-width", "1.5" ]
                            let s = min hw hh / 25.0
                            let p x y = pt (cx + x * s) (cy + y * s)

                            match kind with
                            | "exclusiveGateway" -> path $"M{p -8.0 -8.0} L{p 8.0 8.0} M{p 8.0 -8.0} L{p -8.0 8.0}" [ "stroke", stroke; "stroke-width", f (4.0 * s); "stroke-linecap", "round" ]
                            | "parallelGateway" -> path $"M{p 0.0 -11.0} V{f (cy + 11.0 * s)} M{p -11.0 0.0} H{f (cx + 11.0 * s)}" [ "stroke", stroke; "stroke-width", f (4.0 * s); "stroke-linecap", "round" ]
                            | "inclusiveGateway" -> SvgWriting.empty writer "circle" [ "cx", f cx; "cy", f cy; "r", f (10.0 * s); "fill", "none"; "stroke", stroke; "stroke-width", f (3.0 * s) ]
                            | "complexGateway" ->
                                path $"M{p 0.0 -11.0} V{f (cy + 11.0 * s)} M{p -11.0 0.0} H{f (cx + 11.0 * s)} M{p -8.0 -8.0} L{p 8.0 8.0} M{p 8.0 -8.0} L{p -8.0 8.0}" [ "stroke", stroke; "stroke-width", f (3.0 * s); "stroke-linecap", "round" ]
                            | _ ->
                                SvgWriting.empty writer "circle" [ "cx", f cx; "cy", f cy; "r", f (12.0 * s); "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]
                                SvgWriting.empty writer "circle" [ "cx", f cx; "cy", f cy; "r", f (9.5 * s); "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]
                                path $"M{p 0.0 -6.0} L{p 6.0 -2.0} L{p 4.0 5.0} L{p -4.0 5.0} L{p -6.0 -2.0} Z" [ "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]

                            text cx (box.Bottom + 15.0) 11 500 Theme.text "middle" (words box.Label 18)
                        elif kind = "dataStoreReference" then
                            let stroke = Theme.stroke ColorFamily.Database
                            let ry = min 8.0 (box.Height / 6.0)
                            path
                                $"M{pt box.X (box.Y + ry)} A{f (box.Width / 2.0)},{f ry} 0 0 1 {pt box.Right (box.Y + ry)} V{f (box.Bottom - ry)} A{f (box.Width / 2.0)},{f ry} 0 0 1 {pt box.X (box.Bottom - ry)} Z M{pt box.X (box.Y + ry)} A{f (box.Width / 2.0)},{f ry} 0 0 0 {pt box.Right (box.Y + ry)}"
                                [ "class", "box"; "fill", Theme.fill ColorFamily.Database; "stroke", stroke; "stroke-width", "1.5" ]
                            text box.CenterX (box.Bottom + 15.0) 11 500 Theme.text "middle" (words box.Label 18)
                        elif BpmnLayout.data.Contains kind then
                            let stroke = Theme.stroke ColorFamily.Database
                            let fold = min 10.0 (box.Width / 3.0)
                            path $"M{pt box.X box.Y} H{f (box.Right - fold)} L{pt box.Right (box.Y + fold)} V{f box.Bottom} H{f box.X} Z M{pt (box.Right - fold) box.Y} V{f (box.Y + fold)} H{f box.Right}" [ "class", "box"; "fill", Theme.fill ColorFamily.Database; "stroke", stroke; "stroke-width", "1.5"; "stroke-linejoin", "round" ]

                            let referenced =
                                match box.Element.Field "dataObjectRef" with
                                | null -> box.Element
                                | target -> match document.Find target with | BpmnLookup.Found element -> element | _ -> box.Element

                            if referenced.Field "isCollection" = "true" then
                                path $"M{pt (box.CenterX - 5.0) (box.Bottom - 12.0)} v8 M{pt box.CenterX (box.Bottom - 12.0)} v8 M{pt (box.CenterX + 5.0) (box.Bottom - 12.0)} v8" [ "stroke", stroke; "stroke-width", "1.5" ]

                            if kind = "dataInput" || kind = "dataOutput" then
                                path $"M{pt (box.X + 4.0) (box.Y + 8.0)} h6 v-4 l7,7 l-7,7 v-4 h-6 Z" [ "fill", (if kind = "dataOutput" then stroke else "none"); "stroke", stroke; "stroke-width", "1.2" ]

                            text box.CenterX (box.Bottom + 15.0) 11 500 Theme.text "middle" (words box.Label 18)
                        elif kind = "textAnnotation" then
                            path $"M{pt (box.X + 12.0) box.Y} H{f box.X} V{f box.Bottom} H{f (box.X + 12.0)}" [ "class", "box"; "fill", "none"; "stroke", Theme.muted; "stroke-width", "1.5" ]
                            text (box.X + 8.0) box.CenterY 11 400 Theme.text "start" (words box.Label 22)
                        else
                            // Activities.
                            let stroke = Theme.stroke ColorFamily.Frontend
                            let width = if kind = "callActivity" then "4" else "1.5"
                            SvgWriting.empty writer "rect" [ "class", "box"; "x", f box.X; "y", f box.Y; "width", f box.Width; "height", f box.Height; "rx", "10"; "fill", Theme.fill ColorFamily.Frontend; "stroke", stroke; "stroke-width", width ]

                            if kind = "transaction" then
                                SvgWriting.empty writer "rect" [ "x", f (box.X + 3.0); "y", f (box.Y + 3.0); "width", f (box.Width - 6.0); "height", f (box.Height - 6.0); "rx", "8"; "fill", "none"; "stroke", stroke; "stroke-width", "1" ]

                            if box.Element.Field "triggeredByEvent" = "true" then
                                SvgWriting.empty writer "rect" [ "x", f box.X; "y", f box.Y; "width", f box.Width; "height", f box.Height; "rx", "10"; "fill", "none"; "stroke", Theme.bg; "stroke-width", "1.6"; "stroke-dasharray", "3 3" ]

                            let ix, iy = box.X + 8.0, box.Y + 8.0
                            let icon data filled = path data [ "fill", (if filled then stroke else "none"); "stroke", stroke; "stroke-width", "1.2"; "stroke-linejoin", "round" ]

                            match kind with
                            | "userTask" -> icon $"M{pt (ix + 8.0) (iy + 1.0)} a3.5,3.5 0 1 1 0,7 a3.5,3.5 0 1 1 0,-7 M{pt (ix + 1.0) (iy + 16.0)} c0,-5 3,-7 7,-7 s7,2 7,7 Z" false
                            | "serviceTask" ->
                                SvgWriting.empty writer "circle" [ "cx", f (ix + 8.0); "cy", f (iy + 8.0); "r", "4.5"; "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]
                                icon $"M{pt (ix + 8.0) iy} v3 M{pt (ix + 8.0) (iy + 13.0)} v3 M{pt ix (iy + 8.0)} h3 M{pt (ix + 13.0) (iy + 8.0)} h3 M{pt (ix + 2.3) (iy + 2.3)} l2.1,2.1 M{pt (ix + 11.6) (iy + 11.6)} l2.1,2.1 M{pt (ix + 2.3) (iy + 13.7)} l2.1,-2.1 M{pt (ix + 11.6) (iy + 4.4)} l2.1,-2.1" false
                            | "sendTask"
                            | "receiveTask" ->
                                icon $"M{pt ix (iy + 2.0)} h16 v11 h-16 Z" (kind = "sendTask")
                                path $"M{pt ix (iy + 2.0)} l8,6 l8,-6" [ "fill", "none"; "stroke", (if kind = "sendTask" then Theme.fill ColorFamily.Frontend else stroke); "stroke-width", "1.2" ]
                            | "manualTask" -> icon $"M{pt ix (iy + 7.0)} h9 M{pt ix (iy + 10.0)} h11 M{pt (ix + 2.0) (iy + 13.0)} h9 M{pt ix (iy + 7.0)} q0,8 5,8 h5 M{pt (ix + 9.0) (iy + 7.0)} l4,-4" false
                            | "businessRuleTask" -> icon $"M{pt ix (iy + 2.0)} h16 v12 h-16 Z M{pt ix (iy + 6.0)} h16 M{pt ix (iy + 10.0)} h16 M{pt (ix + 5.0) (iy + 6.0)} v8" false
                            | "scriptTask" -> icon $"M{pt (ix + 3.0) (iy + 1.0)} h11 q-3,3 0,7 t0,7 h-11 q3,-3 0,-7 t0,-7 Z M{pt (ix + 5.0) (iy + 5.0)} h6 M{pt (ix + 5.0) (iy + 8.0)} h6 M{pt (ix + 5.0) (iy + 11.0)} h6" false
                            | _ -> ()

                            text box.CenterX (box.CenterY + 4.0) 12 500 Theme.text "middle" (words box.Label 14)

                            // Markers along the bottom, centred: collapsed subprocess, loop, multi-instance, compensation.
                            let markers =
                                [
                                    if BpmnLayout.isSubProcess kind || (kind = "callActivity" && (match box.Element.Field "calledElement" with | null -> false | target -> match document.Find target with | BpmnLookup.Found called -> called.Kind = "process" | _ -> false)) then "plus"
                                    for child in box.Element.Children do
                                        if child.Kind = "standardLoopCharacteristics" then "loop"
                                        elif child.Kind = "multiInstanceLoopCharacteristics" then (if child.Field "isSequential" = "true" then "sequential" else "parallel")
                                    if kind = "adHocSubProcess" then "adhoc"
                                    if box.Element.Field "isForCompensation" = "true" then "compensation"
                                ]

                            markers
                            |> List.iteri (fun index marker ->
                                let mx = box.CenterX - 8.0 * float markers.Length + 16.0 * float index + 2.0
                                let my = box.Bottom - 16.0

                                match marker with
                                | "plus" -> path $"M{pt mx my} h12 v12 h-12 Z M{pt (mx + 6.0) (my + 3.0)} v6 M{pt (mx + 3.0) (my + 6.0)} h6" [ "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]
                                | "loop" -> path $"M{pt (mx + 3.0) (my + 10.0)} A5,5 0 1 1 {pt (mx + 9.0) (my + 10.0)} M{pt (mx + 9.0) (my + 10.0)} l0,-3 M{pt (mx + 9.0) (my + 10.0)} l3,0" [ "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]
                                | "sequential" -> path $"M{pt mx (my + 2.0)} h12 M{pt mx (my + 6.0)} h12 M{pt mx (my + 10.0)} h12" [ "stroke", stroke; "stroke-width", "1.5" ]
                                | "parallel" -> path $"M{pt (mx + 2.0) my} v12 M{pt (mx + 6.0) my} v12 M{pt (mx + 10.0) my} v12" [ "stroke", stroke; "stroke-width", "1.5" ]
                                | "adhoc" -> text (mx + 6.0) (my + 11.0) 14 700 stroke "middle" [ "~" ]
                                | _ -> path $"M{pt mx (my + 6.0)} l6,-5 v10 Z M{pt (mx + 6.0) (my + 6.0)} l6,-5 v10 Z" [ "fill", "none"; "stroke", stroke; "stroke-width", "1.2" ]))
            )
