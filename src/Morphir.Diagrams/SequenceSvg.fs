// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Globalization

[<RequireQualifiedAccess>]
module internal SequenceSvg =
    let private drawParticipant writer (participant: Participant) x y halfWidth =
        SvgWriting.element
            writer
            "g"
            [ "data-participant", participant.Id.Value ]
            (fun () ->
                if participant.Kind = ParticipantKind.Actor then
                    SvgWriting.circle writer x (y - 15) 6 SvgWriting.fill SvgWriting.ink

                    SvgWriting.path
                        writer
                        $"M{SvgWriting.point x (y - 9)} v16 m-10,-10 h20 m-10,10 l-8,9 m8,-9 l8,9"
                        [ "fill", "none"; "stroke", SvgWriting.ink ]

                    SvgWriting.text writer x (y + 34) participant.Label
                else
                    SvgWriting.rectangle
                        writer
                        (x - halfWidth)
                        y
                        (2 * halfWidth)
                        42
                        6
                        SvgWriting.fill
                        SvgWriting.ink

                    SvgWriting.text writer x (y + 26) participant.Label
            )

    let private drawNote writer x y (note: SequenceNote) =
        let px = x note.Participants[0]
        let width = max 76 (min 380 (note.Text.Length * 8 + 24))

        let left =
            match note.Placement with
            | NotePlacement.Left -> px - width - 12
            | NotePlacement.Right -> px + 12
            | NotePlacement.Over when note.Participants.Length = 2 ->
                (px + x note.Participants[1] - width) / 2
            | NotePlacement.Over -> px - width / 2

        SvgWriting.rectangle writer left (y - 21) width 35 5 "#fff8d8" SvgWriting.ink
        SvgWriting.text writer (left + width / 2) (y + 1) note.Text

    let private drawMessage writer x y (message: SequenceMessage) =
        let sx, tx = x message.Sender, x message.Receiver

        let route, labelX =
            if sx = tx then
                $"M{SvgWriting.point sx y} h36 v22 h-36", sx + 26
            else
                $"M{SvgWriting.point sx y} H{SvgWriting.number tx}", (sx + tx) / 2

        let attributes =
            [ "data-sender", message.Sender.Value; "data-receiver", message.Receiver.Value ]

        let attributes, text =
            match Option.ofNullable message.Number with
            | None -> attributes, message.Text
            | Some number ->
                let number = number.ToString("0.##", CultureInfo.InvariantCulture)
                ("data-number", number) :: attributes, $"[{number}] {message.Text}"

        SvgWriting.connector
            writer
            route
            message.SenderHead
            message.ReceiverHead
            message.IsDashed
            1
            attributes

        writer.BindConnector message.Sender message.Receiver route false
        SvgWriting.text writer labelX (y - 8) text

    // The authored value is retained on the model. Only literal colors can be SVG paint values.
    let private literalColor =
        System.Text.RegularExpressions.Regex(
            @"\A(?:#[0-9a-fA-F]{3,8}|[A-Za-z]{1,24}|rgba?\([0-9,.% ]{1,48}\))\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant
        )

    let private drawFrame writer width (frame: SequenceLayout.Frame) =
        let left = 18 + frame.Depth * 9
        let right = width - left
        let kind = frame.Model.Kind

        let shade =
            match kind, Option.ofObj frame.Model.Color with
            | SequenceFrameKind.Rectangle, Some color when literalColor.IsMatch color -> color
            | SequenceFrameKind.Rectangle, _ -> SvgWriting.fill
            | _ -> "none"

        SvgWriting.element
            writer
            "g"
            [ "data-frame", kind.WireValue ]
            (fun () ->
                SvgWriting.rectangle
                    writer
                    left
                    frame.Top
                    (right - left)
                    (frame.Bottom - frame.Top)
                    4
                    shade
                    SvgWriting.ink

                frame.Sections
                |> Array.iteri (fun index (y, label) ->
                    if index > 0 then
                        SvgWriting.path
                            writer
                            $"M{SvgWriting.point left y} H{SvgWriting.number right}"
                            [ "stroke", SvgWriting.ink; "stroke-dasharray", "5 4" ]

                    let caption =
                        match index, label with
                        | 0, Some title -> $"{kind.WireValue} [{title}]"
                        | 0, None -> kind.WireValue
                        | _, Some title -> $"[{title}]"
                        | _, None -> ""

                    if caption.Length > 0 then
                        SvgWriting.text writer (width / 2) (y + 18) caption
                )
            )

    let private drawActivation writer x (activation: SequenceLayout.Activation) =
        let indent = (activation.Depth - 1) * 4

        SvgWriting.element
            writer
            "g"
            [
                "data-activation", activation.Participant.Value
                "data-depth", SvgWriting.number activation.Depth
            ]
            (fun () ->
                SvgWriting.rectangle
                    writer
                    (x activation.Participant - 6 + indent)
                    activation.Top
                    12
                    (activation.Bottom - activation.Top)
                    0
                    SvgWriting.fill
                    SvgWriting.ink
            )

    let render (model: SequenceDiagram) =
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

        let top = 40
        let layout = SequenceLayout.build top model.Events

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

        let heads =
            model.Messages
            |> Seq.collect (fun message -> [ message.SenderHead; message.ReceiverHead ])

        SvgWriting.document
            "Sequence diagram"
            width
            height
            heads
            (fun writer ->
                layout.Frames
                |> Seq.sortBy (fun frame -> frame.Top, frame.Depth)
                |> Seq.iter (drawFrame writer width)

                for participant in model.Participants do
                    let px = x participant.Id

                    SvgWriting.path
                        writer
                        $"M{SvgWriting.point px (top + 42)} V{SvgWriting.number (bottom + 6)}"
                        [ "stroke", SvgWriting.ink; "stroke-dasharray", "4 4" ]

                    drawParticipant writer participant px top halfWidth
                    drawParticipant writer participant px (bottom + 6) halfWidth

                layout.Activations
                |> Seq.sortBy (fun activation -> activation.Depth)
                |> Seq.iter (drawActivation writer x)

                for row in layout.Rows do
                    match row with
                    | SequenceLayout.Message(y, message) -> drawMessage writer x y message
                    | SequenceLayout.Note(y, note) -> drawNote writer x y note
            )
