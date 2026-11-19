// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Collections.Generic
open System.Collections.Immutable

[<RequireQualifiedAccess>]
module internal SequenceLayout =
    type Row =
        | Message of y: int * message: SequenceMessage
        | Note of y: int * note: SequenceNote

    type Frame =
        {
            Model: SequenceFrame
            Depth: int
            Top: int
            Bottom: int
            Sections: (int * string option) array
        }

    type Activation =
        {
            Participant: DiagramId
            Depth: int
            Top: int
            Bottom: int
        }

    type Layout =
        {
            Rows: ResizeArray<Row>
            Frames: ResizeArray<Frame>
            Activations: ResizeArray<Activation>
            Bottom: int
        }

    let build top (events: ImmutableArray<SequenceEvent>) =
        let rows = ResizeArray<Row>()
        let frames = ResizeArray<Frame>()
        let activations = ResizeArray<Activation>()
        let active = Dictionary<DiagramId, Stack<int>>()
        let mutable nextY = top + 70
        let mutable latestMessage = None

        let closeActivation participant (starts: Stack<int>) y =
            let depth = starts.Count
            let start = starts.Pop()

            activations.Add
                {
                    Participant = participant
                    Depth = depth
                    Top = start
                    Bottom = max (start + 8) y
                }

        let rec walk depth (events: ImmutableArray<SequenceEvent>) =
            for event in events do
                match event with
                | SequenceEvent.Message message ->
                    rows.Add(Message(nextY, message))
                    latestMessage <- Some(message.Span.Start.Offset, nextY)
                    nextY <- nextY + 54
                | SequenceEvent.Note note ->
                    rows.Add(Note(nextY, note))
                    nextY <- nextY + 54
                | SequenceEvent.Activation activation ->
                    let y =
                        match latestMessage with
                        | Some(offset, y) when offset = activation.Span.Start.Offset -> y
                        | _ -> nextY - 28

                    let participant = activation.Participant

                    match activation.Kind with
                    | SequenceActivationKind.Activate ->
                        let starts =
                            match active.TryGetValue participant with
                            | true, starts -> starts
                            | _ ->
                                let starts = Stack<int>()
                                active.Add(participant, starts)
                                starts

                        starts.Push y
                    | SequenceActivationKind.Deactivate ->
                        closeActivation participant active[participant] y
                | SequenceEvent.Frame frame ->
                    let firstY = nextY - 24
                    let sections = Array.zeroCreate frame.Sections.Length

                    for index in 0 .. frame.Sections.Length - 1 do
                        let section = frame.Sections[index]
                        sections[index] <- nextY - 24, Option.ofObj section.Label
                        nextY <- nextY + 30
                        walk (depth + 1) section.Events

                    frames.Add
                        {
                            Model = frame
                            Depth = depth
                            Top = firstY
                            Bottom = nextY - 24
                            Sections = sections
                        }

                    nextY <- nextY + 14

        walk 0 events
        let bottom = nextY - 8

        // Activations left open in the source last through the final lifeline row.
        for pair in active do
            while pair.Value.Count > 0 do
                closeActivation pair.Key pair.Value bottom

        {
            Rows = rows
            Frames = frames
            Activations = activations
            Bottom = bottom
        }
