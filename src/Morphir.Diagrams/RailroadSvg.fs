// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic

/// Lays out and paints railroad diagrams: each item has a width and a height above and below its track, and draws
/// itself with its track entering on the left and leaving on the right at the same height.
[<RequireQualifiedAccess>]
module internal RailroadSvg =
    type private Laid =
        {
            Width: int
            Up: int
            Down: int
            Draw: int -> int -> unit
        }

    let private size = 13
    let private half = 13
    let private gap = 12
    let private n = SvgWriting.number
    let private p = SvgWriting.point

    let private family (kind: RailroadKind) =
        match kind with
        | RailroadKind.Terminal -> ColorFamily.Frontend
        | RailroadKind.NonTerminal -> ColorFamily.Database
        | RailroadKind.Special -> ColorFamily.Cloud
        | _ -> ColorFamily.MessageBus

    /// Text as a box shows it; blank literals get a visible space mark.
    let private shown (item: RailroadItem) =
        match item.Kind with
        | RailroadKind.CharacterClass -> $"[{item.Text}]"
        | _ when String.IsNullOrWhiteSpace item.Text -> String.replicate (max 1 item.Text.Length) "␣"
        | _ -> item.Text

    let private assertion (text: string) =
        match text with
        | "?=" -> "followed by", false
        | "?!" -> "not followed by", true
        | "?<=" -> "preceded by", false
        | _ -> "not preceded by", true

    let private bounds (item: RailroadItem) =
        match item.Minimum, item.MaximumOption with
        | 0, None
        | 1, None -> None
        | low, Some high when low = high -> Some $"×{low}"
        | low, Some high -> Some $"{low}..{high}"
        | low, None -> Some $"{low}..∞"

    let private leftText writer x y fontSize weight fill (value: string) =
        SvgWriting.element
            writer
            "text"
            [ "x", n x; "y", n y; "font-family", Theme.fontStack; "font-size", n fontSize; "font-weight", n weight; "fill", fill ]
            (fun () -> writer.Add(Morphir.Svg.SvgNode.CreateText value))

    /// Paints the diagram; trace highlights are separate layers over boxes whose places do not change.
    let render (options: SvgRenderOptions) (metadata: Morphir.Svg.SvgNode list option) (model: RailroadDiagram) =
        let boxes = Dictionary<string, int * int * int>()
        let scene = ResizeArray<SvgWriting.SceneBuilder>()
        let w () = scene[0]

        let track (data: string) =
            SvgWriting.path (w ()) data [ "class", "track"; "fill", "none"; "stroke", Theme.arrow; "stroke-width", "2" ]

        let line x1 x2 y =
            if x2 > x1 then track $"M{p x1 y} H{n x2}"

        let frame x y width height (stroke: string) (label: string) =
            SvgWriting.empty
                (w ())
                "rect"
                [
                    "class", "railroad-frame"
                    "x", n x
                    "y", n y
                    "width", n width
                    "height", n height
                    "rx", "6"
                    "fill", "none"
                    "stroke", stroke
                    "stroke-width", "1.2"
                    "stroke-dasharray", "4 3"
                ]

            leftText (w ()) (x + 6) (y + 12) 10 600 Theme.muted label

        let rec lay (item: RailroadItem) : Laid =
            match item.Kind with
            | RailroadKind.Terminal
            | RailroadKind.NonTerminal
            | RailroadKind.Special
            | RailroadKind.CharacterClass ->
                let text = shown item
                let boxWidth = max 26 (SvgWriting.textWidth size text + 20)
                let id = model.OccurrenceOf item |> Option.ofObj |> Option.defaultValue ""
                let tone = family item.Kind

                {
                    Width = boxWidth + 20
                    Up = half
                    Down = half
                    Draw =
                        fun x y ->
                            line x (x + 10) y
                            boxes[id] <- (x + 10, y, boxWidth)

                            SvgWriting.element
                                (w ())
                                "g"
                                [ "class", $"railroad-box f-{tone.WireValue}"; "data-ref", (DiagramElementRef.Node(DiagramId.Create id)).ToString(); "data-kind", item.Kind.WireValue ]
                                (fun () ->
                                    let left = x + 10
                                    let fill = Theme.fill tone
                                    let stroke = Theme.stroke tone

                                    match item.Kind with
                                    | RailroadKind.Special ->
                                        SvgWriting.path
                                            (w ())
                                            $"M{p (left + 8) (y - half)} H{n (left + boxWidth - 8)} L{p (left + boxWidth) y} L{p (left + boxWidth - 8) (y + half)} H{n (left + 8)} L{p left y} Z"
                                            [ "class", "box"; "fill", fill; "stroke", stroke; "stroke-width", "1.5" ]
                                    | _ ->
                                        let radius =
                                            match item.Kind with
                                            | RailroadKind.Terminal -> half
                                            | RailroadKind.CharacterClass -> 6
                                            | _ -> 2

                                        SvgWriting.empty
                                            (w ())
                                            "rect"
                                            [
                                                "class", "box"
                                                "x", n left
                                                "y", n (y - half)
                                                "width", n boxWidth
                                                "height", n (2 * half)
                                                "rx", n radius
                                                "fill", fill
                                                "stroke", stroke
                                                "stroke-width", "1.5"
                                            ]

                                    let weight = if item.Kind = RailroadKind.NonTerminal then 600 else 500
                                    SvgWriting.label (w ()) (left + boxWidth / 2) (y + 4) size weight Theme.text text)

                            line (x + 10 + boxWidth) (x + boxWidth + 20) y
                }
            | RailroadKind.Sequence ->
                let parts = item.Children |> Seq.map lay |> Seq.toArray

                {
                    Width = parts |> Array.sumBy (fun part -> part.Width)
                    Up = parts |> Array.fold (fun up part -> max up part.Up) 0
                    Down = parts |> Array.fold (fun down part -> max down part.Down) 0
                    Draw =
                        fun x y ->
                            let mutable left = x

                            for part in parts do
                                part.Draw left y
                                left <- left + part.Width
                }
            | RailroadKind.Choice ->
                let parts = item.Children |> Seq.map lay |> Seq.toArray
                let inner = parts |> Array.fold (fun width part -> max width part.Width) 0
                let offsets = Array.zeroCreate parts.Length

                for index in 1 .. parts.Length - 1 do
                    offsets[index] <- offsets[index - 1] + parts[index - 1].Down + gap + parts[index].Up

                {
                    Width = inner + 40
                    Up = parts[0].Up
                    Down = offsets[parts.Length - 1] + parts[parts.Length - 1].Down
                    Draw =
                        fun x y ->
                            let right = x + inner + 40

                            for index in 0 .. parts.Length - 1 do
                                let row = y + offsets[index]

                                if index = 0 then
                                    line x (x + 20) y
                                    line (x + 20 + parts[0].Width) right y
                                else
                                    track $"M{p x y} Q{p (x + 10) y} {p (x + 10) (y + 10)} V{n (row - 10)} Q{p (x + 10) row} {p (x + 20) row}"
                                    track $"M{p (x + 20 + parts[index].Width) row} H{n (right - 20)} Q{p (right - 10) row} {p (right - 10) (row - 10)} V{n (y + 10)} Q{p (right - 10) y} {p right y}"

                                parts[index].Draw (x + 20) row
                }
            | RailroadKind.Optional
            | RailroadKind.Repeat ->
                let part = lay item.Children[0]
                let skips = item.Minimum = 0
                let loops = item.Kind = RailroadKind.Repeat && item.MaximumOption <> Some 1
                let label = if item.Kind = RailroadKind.Repeat then bounds item else None
                let loopDepth = part.Down + gap
                let labelDepth = if label.IsSome then 18 else 0

                {
                    Width = part.Width + 40
                    Up = if skips then part.Up + gap + 4 else part.Up
                    Down = if loops then loopDepth + labelDepth + 2 else part.Down
                    Draw =
                        fun x y ->
                            let right = x + part.Width + 40
                            line x (x + 20) y
                            line (x + 20 + part.Width) right y

                            if skips then
                                let above = y - part.Up - gap

                                track
                                    $"M{p x y} Q{p (x + 10) y} {p (x + 10) (y - 10)} V{n (above + 10)} Q{p (x + 10) above} {p (x + 20) above} H{n (right - 20)} Q{p (right - 10) above} {p (right - 10) (above + 10)} V{n (y - 10)} Q{p (right - 10) y} {p right y}"

                            if loops then
                                let below = y + loopDepth

                                track
                                    $"M{p (right - 20) y} Q{p (right - 10) y} {p (right - 10) (y + 10)} V{n (below - 10)} Q{p (right - 10) below} {p (right - 20) below} H{n (x + 20)} Q{p (x + 10) below} {p (x + 10) (below - 10)} V{n (y + 10)} Q{p (x + 10) y} {p (x + 20) y}"

                                // A chevron shows that the loop runs back to the left.
                                let middle = x + (part.Width + 40) / 2
                                track $"M{p (middle + 4) (below - 4)} L{p middle below} L{p (middle + 4) (below + 4)}"

                                match label with
                                | Some text -> SvgWriting.label (w ()) middle (below + 17) 10 600 Theme.muted text
                                | None -> ()

                            part.Draw (x + 20) y
                }
            | RailroadKind.Group when item.Text <> "" -> framed item item.Text Theme.frame
            | RailroadKind.Assertion ->
                let text, negative = assertion item.Text
                framed item text (Theme.stroke (if negative then ColorFamily.Security else ColorFamily.External))
            | RailroadKind.Exception ->
                let kept = lay item.Children[0]
                let excluded = lay item.Children[1]
                let width = max kept.Width (excluded.Width + 20)
                let top = kept.Down + gap

                {
                    Width = width
                    Up = kept.Up
                    Down = top + excluded.Up + excluded.Down + 30
                    Draw =
                        fun x y ->
                            kept.Draw x y
                            line (x + kept.Width) (x + width) y
                            frame (x + 2) (y + top) (excluded.Width + 16) (excluded.Up + excluded.Down + 28) (Theme.stroke ColorFamily.Security) "except"
                            excluded.Draw (x + 10) (y + top + 20 + excluded.Up)
                }
            | RailroadKind.Group when item.Children.Length = 1 -> lay item.Children[0]
            | _ ->
                {
                    Width = 20
                    Up = 0
                    Down = 0
                    Draw = fun x y -> line x (x + 20) y
                }

        /// A dashed frame with a caption around the item's track.
        and framed (item: RailroadItem) (caption: string) (stroke: string) =
            let part = lay item.Children[0]
            let width = max (part.Width + 20) (SvgWriting.textWidth 10 caption + 24)

            {
                Width = width
                Up = part.Up + 22
                Down = part.Down + 10
                Draw =
                    fun x y ->
                        frame (x + 3) (y - part.Up - 20) (width - 6) (part.Up + part.Down + 28) stroke caption
                        let left = x + (width - part.Width) / 2
                        line x left y
                        part.Draw left y
                        line (left + part.Width) (x + width) y
            }

        let rows =
            model.Rules
            |> Seq.map (fun rule ->
                let body = lay rule.Body
                let heading = if rule.Name = "" then 0 else 24
                rule, body, heading)
            |> Seq.toArray

        let margin = 24
        let width = 2 * margin + 24 + (rows |> Array.fold (fun widest (_, body, _) -> max widest body.Width) 0)
        let height = margin + (rows |> Array.sumBy (fun (_, body, heading) -> heading + body.Up + body.Down + 28)) + margin - 28 + 16

        let trace = options.RailroadTraceOption

        let layer (name: string) (stroke: string) (dash: string option) (ids: string seq) =
            let ids = ids |> Seq.filter boxes.ContainsKey |> Seq.distinct |> Seq.toArray

            if ids.Length > 0 then
                SvgWriting.element
                    (w ())
                    "g"
                    [ "class", $"trace trace-{name}"; "data-trace", name; "pointer-events", "none" ]
                    (fun () ->
                        for id in ids do
                            let x, y, boxWidth = boxes[id]

                            SvgWriting.empty
                                (w ())
                                "rect"
                                [
                                    "data-occurrence", id
                                    "x", n (x - 4)
                                    "y", n (y - half - 4)
                                    "width", n (boxWidth + 8)
                                    "height", n (2 * half + 8)
                                    "rx", "8"
                                    "fill", "none"
                                    "stroke", stroke
                                    "stroke-width", "2.5"
                                    match dash with
                                    | Some pattern -> "stroke-dasharray", pattern
                                    | None -> ()
                                ]

                            if name = "failure" then
                                SvgWriting.path
                                    (w ())
                                    $"M{p (x + boxWidth - 1) (y - half - 9)} l10,10 m0,-10 l-10,10"
                                    [ "fill", "none"; "stroke", stroke; "stroke-width", "2.5"; "stroke-linecap", "round" ])

        let title = if model.IsRegex then "Railroad diagram of a regular expression" else "Railroad diagram of an EBNF grammar"

        SvgWriting.document
            "railroad"
            title
            width
            height
            []
            options
            metadata
            false
            (fun builder ->
                scene.Clear()
                scene.Add builder
                let mutable top = margin

                for rule, body, heading in rows do
                    if heading > 0 then
                        SvgWriting.element
                            (w ())
                            "g"
                            [ "class", "railroad-rule"; "data-ref", (DiagramElementRef.Node(DiagramId.Create rule.Name)).ToString() ]
                            (fun () -> leftText (w ()) margin (top + 14) 14 700 Theme.text rule.Name)

                    let y = top + heading + body.Up + 4
                    let start = margin
                    track $"M{p start (y - 8)} v16 M{p (start + 5) (y - 8)} v16"
                    line (start + 5) (start + 12) y
                    body.Draw (start + 12) y
                    let finish = start + 12 + body.Width
                    line finish (finish + 7) y
                    track $"M{p (finish + 7) (y - 8)} v16 M{p (finish + 12) (y - 8)} v16"
                    top <- top + heading + body.Up + body.Down + 28

                match trace with
                | None -> ()
                | Some trace ->
                    let matched (visit: RailroadVisit) = visit.Result = RailroadVisitResult.Matched
                    let ids predicate = trace.Visits |> Seq.filter predicate |> Seq.map (fun visit -> visit.Occurrence)
                    let success = trace.Outcome = RailroadOutcome.Matched
                    // Text ink reads on every theme; the failure takes the security family's warning colour.
                    layer "not-matched" Theme.muted (Some "2 3") (ids (matched >> not))
                    if success then layer "matched" Theme.text None (ids matched)
                    else layer "taken" Theme.text (Some "6 3") (ids matched)
                    layer "failure" (Theme.stroke ColorFamily.Security) None (Option.toList trace.FailureOption))

    /// The errors that keep a trace from being drawn: a trace for another diagram, or boxes the diagram lacks.
    let validate (diagram: Diagram) (options: SvgRenderOptions) =
        match options.RailroadTraceOption, diagram with
        | None, _ -> []
        | Some _, diagram when (match diagram with | Diagram.Railroad _ -> false | _ -> true) ->
            [ Reading.error "railroad/trace-family" "A railroad trace can only be drawn over a railroad diagram." None ]
        | Some trace, Diagram.Railroad model when trace.Fingerprint <> model.Fingerprint ->
            [ Reading.error "railroad/stale-trace" "The trace was made for another railroad diagram; its boxes are not matched by label." None ]
        | Some trace, Diagram.Railroad model ->
            let known = HashSet<string>(model.OccurrenceIds, StringComparer.Ordinal)

            [
                for id in Seq.append (trace.Visits |> Seq.map (fun visit -> visit.Occurrence)) (Option.toList trace.FailureOption) |> Seq.distinct do
                    if not (known.Contains id) then
                        Reading.error "railroad/trace-occurrence" $"The trace names {id}, which is not a box of the diagram." None
            ]
        | Some _, _ -> []
