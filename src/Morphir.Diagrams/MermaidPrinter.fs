// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Globalization
open System.Text

/// Writes flowcharts and sequences as canonical Mermaid text. Reading the text gives the same model, so printing
/// it again gives the same text. Presentation that the model does not keep is not printed.
[<RequireQualifiedAccess>]
module internal MermaidPrinter =
    /// Label text as a quoted Mermaid string: quotes and line breaks become entity codes and `<br>`.
    let quote (text: string) =
        "\"" + text.Replace("#", "#35;").Replace("\"", "#quot;").Replace("\n", "<br>") + "\""

    let private direction (value: FlowDirection) =
        match value with
        | FlowDirection.TopToBottom -> "TB"
        | FlowDirection.BottomToTop -> "BT"
        | FlowDirection.LeftToRight -> "LR"
        | FlowDirection.RightToLeft -> "RL"

    let private node (node: FlowNode) =
        let id = node.Id.Value
        let label = quote node.Label
        let data name = Ok $"{id}@{{ shape: {name}, label: {label} }}"

        match node.Shape with
        | FlowShape.Rectangle when node.Label = id -> Ok id
        | FlowShape.Rectangle -> Ok $"{id}[{label}]"
        | FlowShape.Rounded -> Ok $"{id}({label})"
        | FlowShape.Stadium -> Ok $"{id}([{label}])"
        | FlowShape.Subroutine -> Ok $"{id}[[{label}]]"
        | FlowShape.Cylinder -> Ok $"{id}[({label})]"
        | FlowShape.Circle -> Ok $"{id}(({label}))"
        | FlowShape.DoubleCircle -> Ok $"{id}((({label})))"
        | FlowShape.Diamond -> Ok $"{id}{{{label}}}"
        | FlowShape.Hexagon -> Ok $"{id}{{{{{label}}}}}"
        | FlowShape.Asymmetric -> Ok $"{id}>{label}]"
        | FlowShape.Parallelogram -> Ok $"{id}[/{label}/]"
        | FlowShape.ReverseParallelogram -> Ok $"{id}[\\{label}\\]"
        | FlowShape.Trapezoid -> Ok $"{id}[/{label}\\]"
        | FlowShape.ReverseTrapezoid -> Ok $"{id}[\\{label}/]"
        | FlowShape.FilledCircle -> data "f-circ"
        | FlowShape.FramedCircle -> data "fr-circ"
        | FlowShape.Bar -> data "fork"
        | FlowShape.Note -> data "note"
        | FlowShape.Document -> data "doc"
        | FlowShape.SmallCircle -> data "sm-circ"
        | FlowShape.CrossedCircle -> data "cross-circ"
        | FlowShape.Text -> data "text"
        | FlowShape.ThickCircle -> Error $"Node {id} has a shape that Mermaid flowcharts cannot write."

    let private link (edge: FlowEdge) =
        let head side (value: EdgeHead) =
            match value with
            | EdgeHead.None -> Ok ""
            | EdgeHead.Arrow -> Ok(if side then "<" else ">")
            | EdgeHead.Circle -> Ok "o"
            | EdgeHead.Cross -> Ok "x"
            | other -> Error $"A flowchart link cannot have a {other} head."

        match head true edge.StartHead, head false edge.EndHead with
        | Ok start, Ok finish ->
            let shaft =
                match edge.Line, finish with
                | FlowLine.Invisible, _ -> "~~~"
                | FlowLine.Solid, "" -> "---"
                | FlowLine.Solid, _ -> "--"
                | FlowLine.Dotted, "" -> "-.-"
                | FlowLine.Dotted, _ -> "-.-"
                | FlowLine.Thick, "" -> "==="
                | FlowLine.Thick, _ -> "=="

            let text =
                match edge.Label with
                | null -> ""
                | label -> $"|{quote label}|"

            if edge.Line = FlowLine.Invisible then
                Ok $"{edge.Source.Value} ~~~{text} {edge.Target.Value}"
            else
                Ok $"{edge.Source.Value} {start}{shaft}{finish}{text} {edge.Target.Value}"
        | Error message, _
        | _, Error message -> Error message

    let flowchart (model: Flowchart) =
        let lines = ResizeArray<string>()
        let errors = ResizeArray<string>()
        let declared = Collections.Generic.HashSet<DiagramId>()
        let nodes = model.Nodes |> Seq.map (fun node -> node.Id, node) |> dict
        let add indent (result: Result<string, string>) =
            match result with
            | Ok text -> lines.Add(String(' ', 2 * indent) + text)
            | Error message -> errors.Add message

        lines.Add $"flowchart {direction model.Direction}"

        // Explicit stack: nested subgraphs print without recursion.
        let stack = Collections.Generic.Stack<Choice<FlowGroup * int, int>>()

        for group in model.Groups |> Seq.filter (fun group -> isNull group.ParentId) |> Seq.rev do
            stack.Push(Choice1Of2(group, 1))

        while stack.Count > 0 do
            match stack.Pop() with
            | Choice2Of2 indent -> lines.Add(String(' ', 2 * indent) + "end")
            | Choice1Of2(group, indent) ->
                let pad = String(' ', 2 * indent)

                if group.Label = group.Id.Value then
                    lines.Add $"{pad}subgraph {group.Id.Value}"
                else
                    lines.Add $"{pad}subgraph {group.Id.Value} [{quote group.Label}]"

                for id in group.NodeIds do
                    if declared.Add id then add (indent + 1) (node nodes[id])

                stack.Push(Choice2Of2 indent)

                for child in model.Groups |> Seq.filter (fun child -> child.ParentId = group.Id) |> Seq.rev do
                    stack.Push(Choice1Of2(child, indent + 1))

        for node' in model.Nodes do
            if declared.Add node'.Id then add 1 (node node')

        for edge in model.Edges do
            add 1 (link edge)

        if errors.Count > 0 then Error(List.ofSeq errors) else Ok(String.Join("\n", lines) + "\n")

    let private arrow (message: SequenceMessage) =
        let shaft = if message.IsDashed then "--" else "-"

        match message.SenderHead, message.ReceiverHead with
        | EdgeHead.None, EdgeHead.Arrow -> Ok(shaft + ">>")
        | EdgeHead.Arrow, EdgeHead.Arrow -> Ok("<<" + shaft + ">>")
        | EdgeHead.None, EdgeHead.OpenArrow -> Ok(shaft + ">")
        | EdgeHead.None, EdgeHead.Cross -> Ok(shaft + "x")
        | EdgeHead.None, EdgeHead.Async -> Ok(shaft + ")")
        | EdgeHead.None, EdgeHead.HalfUpperFilled -> Ok(shaft + "|\\")
        | EdgeHead.None, EdgeHead.HalfLowerFilled -> Ok(shaft + "|/")
        | EdgeHead.None, EdgeHead.HalfUpper -> Ok(shaft + "\\\\")
        | EdgeHead.None, EdgeHead.HalfLower -> Ok(shaft + "//")
        | EdgeHead.HalfUpperFilled, EdgeHead.None -> Ok("/|" + shaft)
        | EdgeHead.HalfLowerFilled, EdgeHead.None -> Ok("\\|" + shaft)
        | EdgeHead.HalfUpper, EdgeHead.None -> Ok("//" + shaft)
        | EdgeHead.HalfLower, EdgeHead.None -> Ok("\\\\" + shaft)
        | first, second -> Error $"A Mermaid sequence message cannot have the heads {first} and {second}."

    let private number (value: decimal) = value.ToString("0.##", CultureInfo.InvariantCulture)

    let private declaration (participant: Participant) =
        let word = if participant.Kind = ParticipantKind.Actor then "actor" else "participant"

        let kind =
            match participant.Kind with
            | ParticipantKind.Participant
            | ParticipantKind.Actor -> ""
            | other -> $"@{{ \"type\": \"{other}\" }}"

        let alias = if participant.Label = participant.Id.Value then "" else $" as {participant.Label}"
        $"{word} {participant.Id.Value}{kind}{alias}"

    let sequence (model: SequenceDiagram) =
        let lines = ResizeArray<string>()
        let errors = ResizeArray<string>()
        let creators = Collections.Generic.Dictionary<int, Participant>()
        let destroyers = Collections.Generic.Dictionary<int, Participant>()
        let boxed = Collections.Generic.HashSet<DiagramId>()

        lines.Add "sequenceDiagram"

        for participant in model.Participants do
            if participant.CreatedBy.HasValue then creators[participant.CreatedBy.Value] <- participant
            if participant.DestroyedBy.HasValue then destroyers[participant.DestroyedBy.Value] <- participant

        let byId = model.Participants |> Seq.map (fun participant -> participant.Id, participant) |> dict

        for box in model.Boxes do
            let color = match box.Color with | null -> "" | color -> color + " "
            lines.Add($"  box {color}{box.Label}".TrimEnd())

            for id in box.Participants do
                boxed.Add id |> ignore
                lines.Add $"    {declaration byId[id]}"

            lines.Add "  end"

        for participant in model.Participants do
            if not (participant.CreatedBy.HasValue || boxed.Contains participant.Id) then
                lines.Add $"  {declaration participant}"

        let mutable messageIndex = 0
        let mutable numbering: (decimal * decimal) option = None

        // An explicit stack of (events, position, indent) keeps deep frames off the call stack.
        let stack = Collections.Generic.Stack<Choice<Collections.Immutable.ImmutableArray<SequenceEvent> * int * int, string>>()
        stack.Push(Choice1Of2(model.Events, 0, 1))

        while stack.Count > 0 do
            match stack.Pop() with
            | Choice2Of2 line -> lines.Add line
            | Choice1Of2(events, position, indent) when position < events.Length ->
                stack.Push(Choice1Of2(events, position + 1, indent))
                let pad = String(' ', 2 * indent)

                match events[position] with
                | SequenceEvent.Message message ->
                    match creators.TryGetValue messageIndex with
                    | true, created -> lines.Add $"{pad}create {declaration created}"
                    | false, _ -> ()

                    match destroyers.TryGetValue messageIndex with
                    | true, destroyed -> lines.Add $"{pad}destroy {destroyed.Id.Value}"
                    | false, _ -> ()

                    // The reader numbers from its state: the next number and the step. Print a statement
                    // whenever that state would not give this message's number.
                    match Option.ofNullable message.Number, numbering with
                    | None, Some _ ->
                        lines.Add $"{pad}autonumber off"
                        numbering <- None
                    | None, None -> ()
                    | Some value, Some(next, step) when value = next -> numbering <- Some(value + step, step)
                    | Some value, current ->
                        let following =
                            model.Messages
                            |> Seq.skip (messageIndex + 1)
                            |> Seq.tryHead
                            |> Option.bind (fun next -> Option.ofNullable next.Number)

                        let step =
                            match following, current with
                            | Some next, _ when next > value -> next - value
                            | _, Some(_, step) -> step
                            | _ -> 1m

                        lines.Add $"{pad}autonumber {number value} {number step}"
                        numbering <- Some(value + step, step)

                    match arrow message with
                    | Ok shaft ->
                        let sender = if message.StartsAtCentre then message.Sender.Value + "()" else message.Sender.Value
                        let receiver = if message.EndsAtCentre then "()" + message.Receiver.Value else message.Receiver.Value
                        lines.Add $"{pad}{sender}{shaft}{receiver}: {message.Text}"
                    | Error error -> errors.Add error

                    messageIndex <- messageIndex + 1
                | SequenceEvent.Note note ->
                    let anchors = note.Participants |> Seq.map (fun id -> id.Value) |> String.concat ","

                    match note.Placement with
                    | NotePlacement.Left -> lines.Add $"{pad}note left of {anchors}: {note.Text}"
                    | NotePlacement.Right -> lines.Add $"{pad}note right of {anchors}: {note.Text}"
                    | NotePlacement.Over -> lines.Add $"{pad}note over {anchors}: {note.Text}"
                | SequenceEvent.Activation activation ->
                    lines.Add $"{pad}{activation.Kind} {activation.Participant.Value}"
                | SequenceEvent.Frame frame when frame.Kind = SequenceFrameKind.Try ->
                    errors.Add "Mermaid sequence diagrams cannot write a ZenUML try frame."
                | SequenceEvent.Frame frame ->
                    let keyword =
                        match frame.Kind with
                        | SequenceFrameKind.Alternative -> "else"
                        | SequenceFrameKind.Critical -> "option"
                        | _ -> "and"

                    stack.Push(Choice2Of2 $"{pad}end")

                    for index in frame.Sections.Length - 1 .. -1 .. 0 do
                        let section = frame.Sections[index]
                        stack.Push(Choice1Of2(section.Events, 0, indent + 1))

                        let text =
                            if index = 0 then
                                let label =
                                    if frame.Kind = SequenceFrameKind.Rectangle then frame.Color else section.Label

                                match label with
                                | null -> frame.Kind.WireValue
                                | label -> $"{frame.Kind.WireValue} {label}"
                            else
                                match section.Label with
                                | null -> keyword
                                | label -> $"{keyword} {label}"

                        stack.Push(Choice2Of2 $"{pad}{text}")
            | Choice1Of2 _ -> ()

        if errors.Count > 0 then Error(List.ofSeq errors) else Ok(String.Join("\n", lines) + "\n")

    /// Canonical ZenUML text for a sequence. Calls print without bodies, so activations are not written; reading the
    /// text and printing it again gives the same text.
    let zenuml (model: SequenceDiagram) =
        let lines = ResizeArray<string>()
        let errors = ResizeArray<string>()
        let creations = Collections.Generic.Dictionary<int, Participant>()
        let starter = "_STARTER_"

        lines.Add "zenuml"

        for participant in model.Participants do
            if participant.CreatedBy.HasValue then creations[participant.CreatedBy.Value] <- participant

            if participant.Id.Value <> starter then
                let annotator =
                    match participant.Kind with
                    | ParticipantKind.Participant -> ""
                    | kind ->
                        let text = kind.ToString()
                        "@" + string (Char.ToUpperInvariant text[0]) + text.Substring 1 + " "

                let alias = if participant.Label = participant.Id.Value then "" else $" as {participant.Label}"
                lines.Add $"  {annotator}{participant.Id.Value}{alias}"

        let mutable messageIndex = 0
        let stack = Collections.Generic.Stack<Choice<Collections.Immutable.ImmutableArray<SequenceEvent> * int * int, string>>()
        stack.Push(Choice1Of2(model.Events, 0, 1))

        while stack.Count > 0 do
            match stack.Pop() with
            | Choice2Of2 line -> lines.Add line
            | Choice1Of2(events, position, indent) when position < events.Length ->
                stack.Push(Choice1Of2(events, position + 1, indent))
                let pad = String(' ', 2 * indent)

                match events[position] with
                | SequenceEvent.Message message ->
                    let from, target = message.Sender.Value, message.Receiver.Value

                    match creations.TryGetValue messageIndex with
                    | true, created when created.Id = message.Receiver ->
                        let text = message.Text

                        if text = "«create»" then lines.Add $"{pad}new {target}"
                        elif text.StartsWith('«') && text.EndsWith('»') then lines.Add $"{pad}new {target}({text.Substring(1, text.Length - 2)})"
                        else errors.Add $"The creation of {target} has a label that ZenUML cannot write."
                    | _ when message.IsDashed ->
                        lines.Add $"{pad}@return"
                        lines.Add $"{pad}{from}->{target}: {message.Text}"
                    | _ when message.ReceiverHead = EdgeHead.Arrow && from = starter -> lines.Add $"{pad}{target}.{message.Text}"
                    | _ when message.ReceiverHead = EdgeHead.Arrow -> lines.Add $"{pad}{from}->{target}.{message.Text}"
                    | _ -> lines.Add $"{pad}{from}->{target}: {message.Text}"

                    messageIndex <- messageIndex + 1
                | SequenceEvent.Activation _ -> ()
                | SequenceEvent.Note _ -> errors.Add "ZenUML has no notes."
                | SequenceEvent.Frame frame ->
                    let opening (label: string | null) =
                        match frame.Kind, label with
                        | SequenceFrameKind.Loop, null -> Ok "loop {"
                        | SequenceFrameKind.Loop, label -> Ok $"loop({label}) {{"
                        | SequenceFrameKind.Alternative, label -> Ok $"if({label}) {{"
                        | SequenceFrameKind.Optional, _ -> Ok "opt {"
                        | SequenceFrameKind.Parallel, _ -> Ok "par {"
                        | SequenceFrameKind.Try, _ -> Ok "try {"
                        | kind, _ -> Error $"ZenUML has no {kind} fragment."

                    let continuing (label: string | null) =
                        match label with
                        | null
                        | "else" -> "} else {"
                        | label when frame.Kind = SequenceFrameKind.Try -> $"}} {label} {{"
                        | label -> $"}} else if({label}) {{"

                    match opening frame.Sections[0].Label with
                    | Error error -> errors.Add error
                    | Ok first ->
                        stack.Push(Choice2Of2 $"{pad}}}")

                        for index in frame.Sections.Length - 1 .. -1 .. 0 do
                            let section = frame.Sections[index]
                            stack.Push(Choice1Of2(section.Events, 0, indent + 1))
                            stack.Push(Choice2Of2(pad + (if index = 0 then first else continuing section.Label)))
            | Choice1Of2 _ -> ()

        if errors.Count > 0 then
            Error(errors |> List.ofSeq |> List.map (fun message -> Reading.error "zenuml/unprintable" message None))
        else
            Ok(String.Join("\n", lines) + "\n")

    /// Canonical Mermaid text for a flowchart or sequence diagram.
    let print (diagram: Diagram) =
        let result =
            match diagram with
            | Diagram.Flowchart model -> flowchart model
            | Diagram.Sequence model -> sequence model
            | other -> Error [ $"{other} has no Mermaid printer." ]

        result
        |> Result.mapError (List.map (fun message -> Reading.error "mermaid/unprintable" message None))
