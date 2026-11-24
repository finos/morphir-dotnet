// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Globalization

[<RequireQualifiedAccess>]
module internal SequenceSvg =
    /// Plain participants cycle through these families in order.
    let private cycle =
        [| ColorFamily.Frontend; ColorFamily.Backend; ColorFamily.Database; ColorFamily.Cloud; ColorFamily.MessageBus |]

    let private families (participants: seq<Participant>) =
        let result = Dictionary<DiagramId, ColorFamily>()
        let mutable next = 0

        for participant in participants do
            match participant.Kind with
            | ParticipantKind.Actor
            | ParticipantKind.Boundary -> result.Add(participant.Id, ColorFamily.Frontend)
            | ParticipantKind.Control -> result.Add(participant.Id, ColorFamily.Cloud)
            | ParticipantKind.Entity -> result.Add(participant.Id, ColorFamily.Backend)
            | ParticipantKind.Database
            | ParticipantKind.Collections -> result.Add(participant.Id, ColorFamily.Database)
            | ParticipantKind.Queue -> result.Add(participant.Id, ColorFamily.MessageBus)
            | ParticipantKind.Participant ->
                result.Add(participant.Id, cycle[next % cycle.Length])
                next <- next + 1

        result

    let private drawParticipant writer (participant: Participant) (family: ColorFamily) x y halfWidth =
        SvgWriting.element
            writer
            "g"
            [ "class", $"participant f-{family.WireValue}"; "data-participant", participant.Id.Value ]
            (fun () ->
                let stroke = Theme.stroke family
                let line data = SvgWriting.path writer data [ "fill", "none"; "stroke", stroke; "stroke-width", "1.5" ]

                let symbol () =
                    SvgWriting.chip writer x (y + 47) 12 Theme.mask participant.Label
                    SvgWriting.label writer x (y + 47) 12 600 Theme.text participant.Label

                match participant.Kind with
                | ParticipantKind.Actor ->
                    SvgWriting.circle writer x (y + 6) 6 (Theme.fill family) stroke
                    line $"M{SvgWriting.point x (y + 12)} v12 m-10,-7 h20 m-10,7 l-8,9 m8,-9 l8,9"
                    symbol ()
                | ParticipantKind.Boundary ->
                    SvgWriting.circle writer (x + 5) (y + 16) 11 (Theme.fill family) stroke
                    line $"M{SvgWriting.point (x - 16) (y + 4)} v24 M{SvgWriting.point (x - 16) (y + 16)} h10"
                    symbol ()
                | ParticipantKind.Control ->
                    SvgWriting.circle writer x (y + 17) 11 (Theme.fill family) stroke
                    line $"M{SvgWriting.point (x + 4) (y + 2)} l-6,4 l6,4"
                    symbol ()
                | ParticipantKind.Entity ->
                    SvgWriting.circle writer x (y + 15) 11 (Theme.fill family) stroke
                    line $"M{SvgWriting.point (x - 13) (y + 29)} h26"
                    symbol ()
                | ParticipantKind.Participant
                | ParticipantKind.Database
                | ParticipantKind.Collections
                | ParticipantKind.Queue ->
                    let box role fill stroke =
                        SvgWriting.empty
                            writer
                            "rect"
                            [
                                "class", role
                                "x", SvgWriting.number (x - halfWidth)
                                "y", SvgWriting.number y
                                "width", SvgWriting.number (2 * halfWidth)
                                "height", "42"
                                "rx", "6"
                                "fill", fill
                                if stroke <> "none" then
                                    "stroke", stroke
                                    "stroke-width", "1.5"
                            ]

                    if participant.Kind = ParticipantKind.Collections then
                        SvgWriting.empty
                            writer
                            "rect"
                            [
                                "class", "stack"
                                "x", SvgWriting.number (x - halfWidth + 5)
                                "y", SvgWriting.number (y - 5)
                                "width", SvgWriting.number (2 * halfWidth)
                                "height", "42"
                                "rx", "6"
                                "fill", Theme.mask
                                "stroke", stroke
                                "stroke-width", "1.5"
                            ]

                    box "mask" Theme.mask "none"
                    box "box" (Theme.fill family) stroke

                    match participant.Kind with
                    | ParticipantKind.Database ->
                        line $"M{SvgWriting.point (x - halfWidth) (y + 5)} q{halfWidth},10 {2 * halfWidth},0"
                    | ParticipantKind.Queue ->
                        line $"M{SvgWriting.point (x + halfWidth - 10) y} q10,21 0,42"
                    | _ -> ()

                    SvgWriting.label writer x (y + 25) 12 600 Theme.text participant.Label
            )

    let private drawNote writer (reference: DiagramElementRef) x y (note: SequenceNote) =
        let px = x note.Participants[0]
        let width = max 76 (min 380 (SvgWriting.textWidth 12 note.Text + 28))

        let left =
            match note.Placement with
            | NotePlacement.Left -> px - width - 12
            | NotePlacement.Right -> px + 12
            | NotePlacement.Over when note.Participants.Length = 2 ->
                (px + x note.Participants[1] - width) / 2
            | NotePlacement.Over -> px - width / 2

        SvgWriting.element
            writer
            "g"
            [ "class", "note"; "data-ref", reference.ToString() ]
            (fun () ->
                let external = ColorFamily.External
                SvgWriting.rectangle writer left (y - 21) width 35 4 Theme.mask "none"

                SvgWriting.rectangle
                    writer
                    left
                    (y - 21)
                    width
                    35
                    4
                    (Theme.fill external)
                    (Theme.stroke external)

                SvgWriting.label writer (left + width / 2) (y + 1) 12 400 Theme.text note.Text
            )

    /// Cross heads use the security family, and dotted replies the external family.
    let private messageFamily (families: Dictionary<DiagramId, ColorFamily>) (message: SequenceMessage) =
        if message.SenderHead = EdgeHead.Cross || message.ReceiverHead = EdgeHead.Cross then
            ColorFamily.Security
        elif message.IsDashed then
            ColorFamily.External
        else
            families[message.Sender]

    /// Dotted replies are grey dashed lines unless they are crossed.
    let private messageStroke family (message: SequenceMessage) =
        if message.IsDashed && family <> ColorFamily.Security then
            Theme.arrow
        else
            Theme.stroke family

    let private drawMessage writer (reference: DiagramElementRef) families x y (inset: int) (message: SequenceMessage) =
        let sx, tx = x message.Sender, x message.Receiver
        // A message that creates its receiver ends on the side of the new participant's box.
        let tx = if sx = tx then tx else tx - sign (tx - sx) * inset
        let family = messageFamily families message

        let attributes =
            [ "data-sender", message.Sender.Value; "data-receiver", message.Receiver.Value ]

        let attributes, text =
            match Option.ofNullable message.Number with
            | None -> attributes, message.Text
            | Some number ->
                let number = number.ToString("0.##", CultureInfo.InvariantCulture)
                ("data-number", number) :: attributes, $"[{number}] {message.Text}"

        let route, labelX =
            if sx = tx then
                $"M{SvgWriting.point sx y} h36 v22 h-36", sx + 44 + SvgWriting.textWidth 11 text / 2
            else
                $"M{SvgWriting.point sx y} H{SvgWriting.number tx}", (sx + tx) / 2

        SvgWriting.element
            writer
            "g"
            [ "class", $"message f-{family.WireValue}"; "data-ref", reference.ToString() ]
            (fun () ->
                SvgWriting.connector
                    writer
                    route
                    message.SenderHead
                    message.ReceiverHead
                    (messageStroke family message)
                    (if message.IsDashed then Some "4 4" else None)
                    "1.4"
                    (("class", "m-line") :: attributes)

                for central, cx in [ message.StartsAtCentre, sx; message.EndsAtCentre, tx ] do
                    if central then
                        SvgWriting.circle writer cx y 4 Theme.bg (messageStroke family message)

                SvgWriting.chip writer labelX (y - 8) 11 Theme.mask text
                SvgWriting.label writer labelX (y - 8) 11 400 (Theme.ink family) text
            )

        writer.BindConnector message.Sender message.Receiver route false

    // The authored value is retained on the model. Only literal colors can be SVG paint values.
    let private literalColor =
        System.Text.RegularExpressions.Regex(
            @"\A(?:#[0-9a-fA-F]{3,8}|[A-Za-z]{1,24}|rgba?\([0-9,.% ]{1,48}\))\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant
        )

    let private drawFrame writer (references: Dictionary<SequenceFrame, DiagramElementRef>) width (frame: SequenceLayout.Frame) =
        let left = 18 + frame.Depth * 9
        let right = width - left
        let kind = frame.Model.Kind

        // Author colours become 18 percent tints over an opaque background band.
        let tint =
            match kind, Option.ofObj frame.Model.Color with
            | SequenceFrameKind.Rectangle, Some color when literalColor.IsMatch color -> Some color
            | _ -> None

        SvgWriting.element
            writer
            "g"
            [ "class", "frame"; "data-ref", references[frame.Model].ToString(); "data-frame", kind.WireValue ]
            (fun () ->
                let band role fill (extra: (string * string) list) =
                    SvgWriting.empty
                        writer
                        "rect"
                        [
                            "class", role
                            "x", SvgWriting.number left
                            "y", SvgWriting.number frame.Top
                            "width", SvgWriting.number (right - left)
                            "height", SvgWriting.number (frame.Bottom - frame.Top)
                            "rx", "6"
                            "fill", fill
                            yield! extra
                        ]

                band "band-mask" Theme.bg []

                band
                    "band"
                    (tint |> Option.defaultValue (Theme.fill ColorFamily.External))
                    [
                        if tint.IsSome then
                            "fill-opacity", "0.18"
                        "stroke", Theme.frame
                        "stroke-width", "1.5"
                    ]

                frame.Sections
                |> Array.iteri (fun index (y, label) ->
                    if index > 0 then
                        SvgWriting.path
                            writer
                            $"M{SvgWriting.point left y} H{SvgWriting.number right}"
                            [
                                "class", "section-divider"
                                "fill", "none"
                                "stroke", Theme.frame
                                "stroke-width", "1.4"
                                "stroke-dasharray", "4 4"
                            ]

                    let caption =
                        match index, label with
                        | _, Some title when kind = SequenceFrameKind.Try -> title
                        | 0, Some title -> $"{kind.WireValue} [{title}]"
                        | 0, None -> kind.WireValue
                        | _, Some title -> $"[{title}]"
                        | _, None -> ""

                    if caption.Length > 0 then
                        let weight = if index = 0 then 700 else 400
                        SvgWriting.chip writer (width / 2) (y + 18) 11 Theme.mask caption
                        SvgWriting.label writer (width / 2) (y + 18) 11 weight Theme.text caption
                )
            )

    let private drawActivation
        writer
        (families: Dictionary<DiagramId, ColorFamily>)
        x
        (activation: SequenceLayout.Activation)
        =
        let indent = (activation.Depth - 1) * 4
        let family = families[activation.Participant]
        let left = x activation.Participant - 6 + indent
        let height = activation.Bottom - activation.Top

        // The bar is opaque, so lifelines and frames do not show through its tint.
        SvgWriting.rectangle writer left activation.Top 12 height 0 Theme.mask "none"

        SvgWriting.element
            writer
            "g"
            [
                "class", "activation"
                "data-activation", activation.Participant.Value
                "data-depth", SvgWriting.number activation.Depth
            ]
            (fun () ->
                writer.Hint(LayoutHint.Role Layout.FamilyRole.Activation)

                SvgWriting.rectangle
                    writer
                    left
                    activation.Top
                    12
                    height
                    0
                    (Theme.fill family)
                    (Theme.stroke family)
            )

    let render (options: SvgRenderOptions) (metadata: Morphir.Svg.SvgNode list option) (model: SequenceDiagram) =
        let indices = Dictionary<DiagramId, int>()

        model.Participants
        |> Seq.iteri (fun index participant -> indices.Add(participant.Id, index))

        let maxParticipantLabel =
            model.Participants
            |> Seq.fold (fun length participant -> max length participant.Label.Length) 0

        let maxMessageLabel =
            model.Messages |> Seq.fold (fun length message -> max length message.Text.Length) 0

        let maxNoteLabel =
            model.Notes |> Seq.fold (fun length note -> max length note.Text.Length) 0

        let top = if model.Boxes.IsEmpty then 40 else 64
        let layout = SequenceLayout.build top model.Events

        let rowOf = Dictionary<SequenceMessage, int>(HashIdentity.Reference)

        for row in layout.Rows do
            match row with
            | SequenceLayout.Message(y, message) -> rowOf[message] <- y
            | SequenceLayout.Note _ -> ()

        let messageRow (index: Nullable<int>) =
            if index.HasValue then Some rowOf[model.Messages[index.Value]] else None

        // A created participant's box sits on the row of the message that creates it.
        let headTop (participant: Participant) =
            messageRow participant.CreatedBy |> Option.map (fun y -> y - 21) |> Option.defaultValue top

        let creates = HashSet<SequenceMessage>(HashIdentity.Reference)

        for participant in model.Participants do
            if participant.CreatedBy.HasValue then
                creates.Add model.Messages[participant.CreatedBy.Value] |> ignore

        let maxFrameDepth =
            layout.Frames |> Seq.fold (fun depth frame -> max depth (frame.Depth + 1)) 0

        let maxActivationDepth =
            layout.Activations |> Seq.fold (fun depth activation -> max depth activation.Depth) 0

        let halfWidth = (maxParticipantLabel * 8 / 2 + 12) |> min 144 |> max 52
        let contentPadding = max (maxMessageLabel * 4) (maxNoteLabel * 8 + 24) |> min 400

        let padding =
            48 + max contentPadding (max (maxFrameDepth * 9) (maxActivationDepth * 4))

        let step =
            max (2 * halfWidth + 36) (min 420 (max maxMessageLabel maxNoteLabel * 8 + 36))

        let x id =
            padding + halfWidth + indices[id] * step

        let width =
            2 * (padding + halfWidth) + (max 0 (model.Participants.Length - 1)) * step

        let bottom = layout.Bottom
        let height = bottom + 80

        let families = families model.Participants
        let messageRefs = ElementRefs.Messages model
        let noteRefs = ElementRefs.Notes model

        let heads =
            model.Messages
            |> Seq.collect (fun message ->
                let stroke = messageStroke (messageFamily families message) message
                [ message.SenderHead, stroke; message.ReceiverHead, stroke ]
            )

        SvgWriting.document
            "sequence"
            "Sequence diagram"
            width
            height
            heads
            options
            metadata
            false
            (fun writer ->
                layout.Frames
                |> Seq.sortBy (fun frame -> frame.Top, frame.Depth)
                |> Seq.iter (drawFrame writer (ElementRefs.Frames model) width)

                for box in model.Boxes do
                    if not box.Participants.IsEmpty then
                        let xs = box.Participants |> Seq.map x
                        let left, right = Seq.min xs - halfWidth - 10, Seq.max xs + halfWidth + 10

                        let fill =
                            match Option.ofObj box.Color with
                            | Some color when literalColor.IsMatch color && color <> "transparent" -> color
                            | Some color when color <> "transparent" ->
                                let name = if box.Label = "" then "a box" else $"the box {box.Label}"
                                writer.Hint(LayoutHint.Unresolved(color, name))
                                "none"
                            | _ -> "none"

                        SvgWriting.empty
                            writer
                            "rect"
                            [
                                "class", "participant-box"
                                "x", SvgWriting.number left
                                "y", SvgWriting.number (top - 30)
                                "width", SvgWriting.number (right - left)
                                "height", SvgWriting.number (bottom + 90 - top)
                                "rx", "8"
                                "fill", fill
                                "fill-opacity", "0.12"
                                "stroke", Theme.frame
                                "stroke-width", "1"
                            ]

                        if box.Label <> "" then
                            SvgWriting.label writer ((left + right) / 2) (top - 12) 12 600 Theme.text box.Label

                for participant in model.Participants do
                    let px = x participant.Id
                    let lifelineEnd = messageRow participant.DestroyedBy |> Option.defaultValue (bottom + 6)

                    // The lifeline is drawn under the messages, outside its participant's group, but belongs to it.
                    writer.Hint(LayoutHint.Owner((DiagramElementRef.Participant participant.Id).ToString()))
                    writer.Hint(LayoutHint.Lifeline participant.Id.Value)

                    SvgWriting.path
                        writer
                        $"M{SvgWriting.point px (headTop participant + 42)} V{SvgWriting.number lifelineEnd}"
                        [
                            "class", "lifeline"
                            "fill", "none"
                            "stroke", Theme.arrow
                            "stroke-width", "0.8"
                            "stroke-dasharray", "3 7"
                        ]

                layout.Activations
                |> Seq.sortBy (fun activation -> activation.Depth)
                |> Seq.iter (drawActivation writer families x)

                for row in layout.Rows do
                    match row with
                    | SequenceLayout.Message(y, message) ->
                        let inset =
                            if not (creates.Contains message) then 0
                            else
                                match model.Participants[indices[message.Receiver]].Kind with
                                | ParticipantKind.Actor
                                | ParticipantKind.Boundary
                                | ParticipantKind.Control
                                | ParticipantKind.Entity -> 16
                                | _ -> halfWidth

                        drawMessage writer messageRefs[message] families x y inset message
                    | SequenceLayout.Note(y, note) -> drawNote writer noteRefs[note] x y note

                // Participants paint last, over lifelines and self-messages, as upstream does.
                for participant in model.Participants do
                    let px = x participant.Id
                    let family = families[participant.Id]
                    // One element, drawn at the top and the bottom of its lifeline.
                    SvgWriting.element
                        writer
                        "g"
                        [ "class", "lane"; "data-ref", (DiagramElementRef.Participant participant.Id).ToString() ]
                        (fun () ->
                            drawParticipant writer participant family px (headTop participant) halfWidth

                            match messageRow participant.DestroyedBy with
                            | Some y ->
                                SvgWriting.path
                                    writer
                                    $"M{SvgWriting.point (px - 9) (y - 9)} l18,18 m0,-18 l-18,18"
                                    [ "class", "destroy"; "fill", "none"; "stroke", Theme.stroke ColorFamily.Security; "stroke-width", "2" ]
                            | None -> drawParticipant writer participant family px (bottom + 6) halfWidth)
            )
